using Core.Abstractions;
using Host.Loader;
using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;

namespace Host.Builder
{
    internal class Program
    {
        // Папка, где лежат твои скомпилированные DLL (откуда брать)
        // Обычно это папка решения, куда ты настроил Output всех проектов, или конкретная папка bin
        // Для примера укажем путь к WallFinisher/bin/Release, но в идеале настроить общий Output для решения
        private static string SOLUTION_ROOT_DIR;

        // Папка "Сервера"
        private static string SERVER_ROOT;

        // Путь к файлу манифеста
        private static string JSON_PATH;

        static void Main(string[] args)
        {
            Console.WriteLine("===============================================");
            Console.WriteLine("          ATP-TLP PLUGIN BUILDER v3.3          ");
            Console.WriteLine("===============================================");

            // Настройка путей
            SOLUTION_ROOT_DIR = Environment.ExpandEnvironmentVariables(@"C:\Users\ARMI\source\repos\revit-tools-addin");
            SERVER_ROOT = @"P:\MOS-TLP\GROUPS\ALLGEMEIN\02_ATP_STANDARDS\07_BIM\01_Settings\01_Add-Ins\001_ATP_Common_Plugin\01_Dev\01_Prod";
            JSON_PATH = Path.Combine(SERVER_ROOT, "plugins.json");

            if (!ValidatePaths()) return;

            try
            {
                // 1. Загрузка базы
                Console.Write("Чтение plugins.json... ");
                List<PluginMetadata> existingPlugins = LoadCurrentPlugins(); 
                Console.WriteLine($"Найдено записей: {existingPlugins.Count}");
                bool jsonChanged = false;

                // 2. Глобальное сканирование
                Console.WriteLine($"\nСканирование всего решения:\n-> {SOLUTION_ROOT_DIR}");

                // Ищем все DLL во всех подпапках
                var allDllFiles = Directory.GetFiles(SOLUTION_ROOT_DIR, "*.dll", SearchOption.AllDirectories);

                foreach (var dllPath in allDllFiles)
                {
                    // ФИЛЬТРЫ: Отсекаем мусор
                    // 1. Игнорируем файлы внутри папок 'obj' (промежуточная сборка)
                    if (dllPath.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;

                    string fileName = Path.GetFileName(dllPath);
                    if (IsSystemFile(fileName)) continue;

                    try
                    {
                        using (var assembly = AssemblyDefinition.ReadAssembly(dllPath))
                        {
                            var commands = ExtractPluginAttributes(assembly);

                            // Если в DLL нет наших плагинов — пропускаем молча (это может быть просто библиотека)
                            if (commands.Count == 0)
                            {
                                Console.ForegroundColor = ConsoleColor.DarkGray;
                                Console.WriteLine("   |-- [INFO] Плагинов не найдено.");
                                Console.ResetColor();
                                continue;
                            }

                            // Определяем версию файла
                            string fileVersion = assembly.Name.Version.ToString();

                            // Определяем папку, ОТКУДА копировать (папка, где лежит эта DLL)
                            string currentSourceDir = Path.GetDirectoryName(dllPath);

                            Console.WriteLine("\n-----------------------------------------------------------");
                            Console.WriteLine($"[НАЙДЕН МОДУЛЬ] {fileName}");
                            Console.WriteLine($"   |-- Путь: {currentSourceDir}");
                            Console.WriteLine($"   |-- Версия: {fileVersion}");

                            foreach (var attr in commands)
                            {
                                Console.WriteLine($"   |---- [Команда ID: {attr.Id}]");

                                string targetVersionFolder = Path.Combine(SERVER_ROOT, attr.Id, fileVersion);
                                string deployedAssemblyPath = Path.Combine(targetVersionFolder, fileName);

                                // Деплоим файлы
                                Console.WriteLine($"   |     Деплой в: ...\\{attr.Id}\\{fileVersion}\\");
                                DeployFiles(currentSourceDir, targetVersionFolder);

                                string newHash = ComputeMD5(deployedAssemblyPath);

                                UpdateOrAddPlugin(existingPlugins, attr, fileVersion, newHash, targetVersionFolder, fileName, ref jsonChanged);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"   |-- [ERROR] Ошибка обработки файла: {ex.Message}");
                        Console.ResetColor();
                    }
                }

                Console.WriteLine("\n-----------------------------------------------------------");
                if (jsonChanged)
                {
                    SavePlugins(existingPlugins);
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("ИТОГ: JSON успешно обновлен и сохранен.");
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.White;
                    Console.WriteLine("ИТОГ: Изменений в конфигурации не требуется.");
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n!!! CRITICAL ERROR !!!\n{ex.Message}\n{ex.StackTrace}");
                Console.ResetColor();
            }

            Console.WriteLine("\nНажмите любую клавишу для выхода...");
            Console.ReadKey();
        }

        // --- ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ ---

        static void UpdateOrAddPlugin(
            List<PluginMetadata> plugins,
            RevitPluginAttribute attr,
            string version,
            string hash,
            string folder,
            string assemblyName,
            ref bool changed)
        {
            var entry = plugins.FirstOrDefault(p => p.Id == attr.Id);

            if (entry == null)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"   |     [РЕЗУЛЬТАТ]: NEW (Новый плагин)");
                Console.ResetColor();

                plugins.Add(new PluginMetadata
                {
                    Id = attr.Id,
                    Version = version,
                    IsEnabled = true,
                    TabName = attr.TabName,
                    PanelName = attr.PanelName,
                    ButtonTitle = attr.Name,
                    Tooltip = attr.Tooltip,
                    BuildHash = hash,
                    ServerFolder = folder,
                    MainAssembly = assemblyName
                });
                changed = true;
            }
            else
            {
                Version currentVer, newVer;
                bool parseCurrent = Version.TryParse(entry.Version, out currentVer);
                bool parseNew = Version.TryParse(version, out newVer);

                // ЗАЩИТА ОТ ДАУНГРЕЙДА (Debug перезаписывает Release)
                // Если версии валидны и Новая < Старой -> Игнорируем
                if (parseCurrent && parseNew && newVer < currentVer)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"   |     [ИГНОР]: Найдена старая версия {version}. В базе уже есть {entry.Version}.");
                    Console.ResetColor();
                    return; // Выходим, не меняя ничего
                }

                string oldPath = (entry.ServerFolder ?? "").TrimEnd('\\');
                string newPath = (folder ?? "").TrimEnd('\\');
                string oldFile = (entry.MainAssembly ?? "").Trim();
                string newFile = (assemblyName ?? "").Trim();

                // ЛОГИРОВАНИЕ СРАВНЕНИЯ
                bool verChanged = !string.Equals(entry.Version, version, StringComparison.OrdinalIgnoreCase);
                bool hashChanged = !string.Equals(entry.BuildHash, hash, StringComparison.OrdinalIgnoreCase);
                bool pathChanged = !string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase);
                bool fileChanged = !string.Equals(oldFile, newFile, StringComparison.OrdinalIgnoreCase);

                // --- ДИАГНОСТИКА ---
                //Console.WriteLine($"   |     [DEBUG] Сравнение для {attr.Id}:");
                //Console.WriteLine($"   |       Путь JSON: '{oldPath}'");
                //Console.WriteLine($"   |       Путь NEW:  '{newPath}'");
                //Console.WriteLine($"   |       Равны?:    {!pathChanged}");

                // Проверяем изменения (Версия или Хэш)
                if (verChanged || hashChanged || pathChanged || fileChanged)
                {
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"   |     [СРАВНЕНИЕ] Обнаружены изменения:");
                    if (verChanged) Console.WriteLine($"   |       Версия: {entry.Version} -> {version}");
                    if (hashChanged) Console.WriteLine($"   |       Хэш:    {entry.BuildHash?.Substring(0, 8)}... -> {hash.Substring(0, 8)}...");
                    if (pathChanged) Console.WriteLine($"   |       Путь:   ОБНОВЛЕН");
                    if (fileChanged) Console.WriteLine($"   |       Файл:   {entry.MainAssembly} -> {assemblyName}");
                    Console.ResetColor();

                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"   |     [РЕЗУЛЬТАТ]: UPDATE (Обновление)");
                    Console.ResetColor();

                    entry.Version = version;
                    entry.BuildHash = hash;
                    entry.ServerFolder = folder;
                    entry.MainAssembly = assemblyName;

                    // UI поля тоже обновляем
                    entry.TabName = attr.TabName;
                    entry.PanelName = attr.PanelName;
                    entry.ButtonTitle = attr.Name;
                    entry.Tooltip = attr.Tooltip;

                    changed = true;
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine($"   |     [СРАВНЕНИЕ] Версия ({version}) и Хэш ({hash.Substring(0, 8)}...) совпадают.");
                    Console.WriteLine($"   |     [РЕЗУЛЬТАТ]: SKIP (Без изменений)");
                    Console.ResetColor();
                }
            }
        }

        static List<RevitPluginAttribute> ExtractPluginAttributes(AssemblyDefinition assembly)
        {
            var results = new List<RevitPluginAttribute>();

            foreach (var module in assembly.Modules)
            {
                foreach (var type in module.Types)
                {
                    if (!type.HasCustomAttributes) continue;

                    foreach (var attr in type.CustomAttributes)
                    {
                        if (attr.AttributeType.FullName == "Core.Abstractions.RevitPluginAttribute")
                        {
                            var args = attr.ConstructorArguments;

                            // НОВЫЙ ПОРЯДОК АРГУМЕНТОВ (после удаления Version из конструктора):
                            // 0: id
                            // 1: name
                            // 2: tabName  <-- Сдвинулось влево
                            // 3: panelName
                            // 4: tooltip
                            // 5: description

                            try
                            {
                                string id = args[0].Value?.ToString();
                                string name = args[1].Value?.ToString();
                                string tab = args[2].Value?.ToString();
                                string panel = args[3].Value?.ToString();
                                string tip = args.Count > 4 ? args[4].Value?.ToString() : "";
                                string desc = args.Count > 5 ? args[5].Value?.ToString() : "";

                                // Создаем объект без версии
                                var pluginAttr = new RevitPluginAttribute(id, name, tab, panel, tip, desc);
                                results.Add(pluginAttr);
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"   |-- Ошибка атрибута в {type.Name}: {ex.Message}");
                            }
                        }
                    }
                }
            }
            return results;
        }

        static void DeployFiles(string sourceDir, string targetDir)
        {
            if (!Directory.Exists(targetDir))
            {
                Directory.CreateDirectory(targetDir);
                Console.WriteLine($"Создана папка версии: {targetDir}");
            }

            // Копируем ВСЕ файлы из папки билда (включая зависимости)
            var files = Directory.GetFiles(sourceDir);
            foreach (var file in files)
            {
                string fileName = Path.GetFileName(file);

                if (fileName.EndsWith(".pdb") || fileName.EndsWith(".xml")) continue;
                try
                {
                    string destFile = Path.Combine(targetDir, fileName);

                    // Копируем с перезаписью (если версия та же, но мы пересобрали)
                    File.Copy(file, destFile, true);
                }
                catch (IOException)
                {
                    Console.WriteLine($"   |     [WARN] Не удалось скопировать {fileName} (возможно, занят)");
                }
            }
        }

        static bool ValidatePaths()
        {
            if (!Directory.Exists(SOLUTION_ROOT_DIR))
            {
                Console.WriteLine($"[ERROR] Корневая папка решения не найдена:\n{SOLUTION_ROOT_DIR}");
                Console.ReadKey();
                return false;
            }
            if (!Directory.Exists(SERVER_ROOT))
            {
                Console.WriteLine($"[ERROR] Папка сервера не найдена:\n{SERVER_ROOT}");
                Console.ReadKey();
                return false;
            }
            return true;
        }

        static bool IsSystemFile(string name)
        {
            return name.StartsWith("System.") ||
                   name.StartsWith("Microsoft.") ||
                   name.StartsWith("Mono.") ||
                   name.EndsWith(".pdb") ||
                   name.EndsWith(".xml");
        }

        static List<PluginMetadata> LoadCurrentPlugins()
        {
            if (!File.Exists(JSON_PATH)) return new List<PluginMetadata>();

            string json = File.ReadAllText(JSON_PATH);
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                Encoder = JavaScriptEncoder.Create(UnicodeRanges.BasicLatin, UnicodeRanges.Cyrillic)
            };

            try
            {
                return JsonSerializer.Deserialize<List<PluginMetadata>>(json, options) ?? new List<PluginMetadata>();
            }
            catch { return new List<PluginMetadata>(); }
        }

        static void SavePlugins(List<PluginMetadata> plugins)
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.Create(UnicodeRanges.BasicLatin, UnicodeRanges.Cyrillic)
            };
            string json = JsonSerializer.Serialize(plugins, options);
            File.WriteAllText(JSON_PATH, json);
        }

        static string ComputeMD5(string filename)
        {
            using (var md5 = MD5.Create())
            using (var stream = File.OpenRead(filename))
            {
                return BitConverter.ToString(md5.ComputeHash(stream)).Replace("-", "").ToUpperInvariant();
            }
        }
    }
}
