using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Core.Abstractions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace Host.Loader
{
    public class PluginManager
    {
        // Папки
        private readonly string _localCacheDir;
        private readonly string _shadowCopyDir;
        private readonly JsonRepository _repository;

        // Кэш проверки обновлений (Id -> Время последней проверки)
        private static Dictionary<string, DateTime> _lastCheckTime = new Dictionary<string, DateTime>();
        private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(0); // Таймер для обновления

        public PluginManager()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            _localCacheDir = Path.Combine(appData, "ATP-TLP", "RevitPlugins", "Cache");
            _shadowCopyDir = Path.Combine(Path.GetTempPath(), "ATP-TLP", "RevitPlugins", "Shadow");
            _repository = new JsonRepository(@"P:\MOS-TLP\GROUPS\ALLGEMEIN\02_ATP_STANDARDS\07_BIM\01_Settings\01_Add-Ins\001_ATP_Common_Plugin\01_Dev\01_Prod\plugins.json");
        }

        public Result Run(string pluginId, ExternalCommandData data, ref string msg, ElementSet elem)
        {
            try
            {
                // ---------------------------------------------------------
                // 1. ПОЛУЧЕНИЕ МЕТАДАННЫХ
                // ---------------------------------------------------------
                PluginMetadata meta = _repository.GetPlugin(pluginId);

                if (meta == null) 
                {
                    TaskDialog.Show("Error", $"Плагин {pluginId} не найден в конфигурации.");
                    return Result.Failed; 
                }

                if (!meta.IsEnabled) 
                {
                    TaskDialog.Show("Access denied", meta.DisableReason ?? "Плагин отключен.");
                    return Result.Cancelled; 
                }

                // ---------------------------------------------------------
                // 2. СИНХРОНИЗАЦИЯ С КЭШЕМ (ПРОВЕРКА ОБНОВЛЕНИЙ)
                // ---------------------------------------------------------
                string cachedPluginFolder = Path.Combine(_localCacheDir, pluginId, meta.Version);
                string cachedAssemblyPath = Path.Combine(cachedPluginFolder, meta.MainAssembly);

                bool needDownload = true;

                // Если файл есть локально, проверяем его хэш
                if (Directory.Exists(cachedPluginFolder) && File.Exists(cachedAssemblyPath))
                {
                    string localHash = ComputeMD5(cachedAssemblyPath);

                    // Сравниваем
                    if (string.Equals(localHash, meta.BuildHash, StringComparison.OrdinalIgnoreCase))
                    {
                        needDownload = false;
                    }
                }

                // Если нужно обновить или скачать с нуля
                if (needDownload)
                {
                    if (string.IsNullOrEmpty(meta.ServerFolder) || !Directory.Exists(meta.ServerFolder))
                    {
                        TaskDialog.Show("Error", $"Сервер недоступен: {meta.ServerFolder}");
                        return Result.Failed;
                    }

                    // Удаляем старый кэш, если был
                    if (Directory.Exists(cachedPluginFolder)) Directory.Delete(cachedPluginFolder, true);

                    // Копируем новую версию с сервера
                    CopyDirectory(meta.ServerFolder, cachedPluginFolder);
                }

                // ---------------------------------------------------------
                // 3. СОЗДАНИЕ SHADOW COPY
                // ---------------------------------------------------------
                // Генерируем уникальную папку для этого конкретного запуска (клика)
                string sessionGuid = Guid.NewGuid().ToString();
                string shadowFolder = Path.Combine(_shadowCopyDir, pluginId, sessionGuid);

                // Копируем всё содержимое кэша во временную папку
                CopyDirectory(cachedPluginFolder, shadowFolder);

                string shadowAssemblyPath = Path.Combine(shadowFolder, meta.MainAssembly);

                // ---------------------------------------------------------
                // 4. ПРЕДВАРИТЕЛЬНАЯ ЗАГРУЗКА ЗАВИСИМОСТЕЙ (PRE-LOADING)
                // ---------------------------------------------------------

                string[] allDlls = Directory.GetFiles(shadowFolder, "*.dll");
                foreach (string dllPath in allDlls)
                {
                    // Основную сборку пропустим, её загрузим отдельно с PDB ниже
                    if (Path.GetFileName(dllPath).Equals(meta.MainAssembly, StringComparison.OrdinalIgnoreCase))
                        continue;

                    try
                    {
                        byte[] dllBytes = File.ReadAllBytes(dllPath);
                        Assembly.Load(dllBytes);
                    }
                    catch
                    {
                        // Игнорируем ошибки (например, если попалась native dll, которую нельзя загрузить так)
                    }
                }

                // ---------------------------------------------------------
                // 5. ЗАГРУЗКА ОСНОВНОЙ СБОРКИ
                // ---------------------------------------------------------
                byte[] mainAssemblyBytes = File.ReadAllBytes(shadowAssemblyPath);

                // Пытаемся найти файлы символов (.pdb) для отладки
                byte[] pdbBytes = null;
                string pdbPath = Path.ChangeExtension(shadowAssemblyPath, ".pdb");
                if (File.Exists(pdbPath)) pdbBytes = File.ReadAllBytes(pdbPath);

                Assembly mainAssembly;
                if (pdbBytes != null)
                {
                    mainAssembly = Assembly.Load(mainAssemblyBytes, pdbBytes);
                }
                else
                {
                    mainAssembly = Assembly.Load(mainAssemblyBytes);
                }

                // ---------------------------------------------------------
                // 6. ПОИСК КЛАССА КОМАНДЫ
                // ---------------------------------------------------------
                Type commandType = null;

                foreach (Type type in mainAssembly.GetTypes())
                {
                    var attr = type.GetCustomAttribute<RevitPluginAttribute>();
                    if (attr != null && string.Equals(attr.Id, pluginId, StringComparison.OrdinalIgnoreCase))
                    {
                        commandType = type;
                        break;
                    }
                }

                if (commandType == null)
                {
                    msg = $"Class with ID {pluginId} not found";
                    return Result.Failed; 
                }

                // ---------------------------------------------------------
                // 7. ЗАПУСК
                // ---------------------------------------------------------
                // Создаем экземпляр команды
                var commandInstance = (IPluginCommand)Activator.CreateInstance(commandType);

                // Запускаем асинхронную очистку старых папок
                CleanupAsync(pluginId);

                // Выполняем команду
                return commandInstance.Execute(data, ref msg, elem);
            }
            catch (Exception ex)
            {
                TaskDialog.Show("CRITICAL ERROR", ex.ToString());
                msg = ex.ToString();
                return Result.Failed;
            }
        }

        public static Result RunStatic(string pluginId, ExternalCommandData data, ref string msg, ElementSet elem)
        {
            var manager = new PluginManager();
            return manager.Run(pluginId, data, ref msg, elem);
        }

        private PluginMetadata GetPluginMetadataOrCached(string id)
        {
            // Простая логика: если проверяли недавно, не дергаем репозиторий
            if (_lastCheckTime.ContainsKey(id) && (DateTime.Now - _lastCheckTime[id] < _checkInterval))
            {
                // Тут мы должны бы вернуть сохраненные метаданные, 
                // но для упрощения пока просто читаем репозиторий.
                // В идеале метаданные тоже нужно хранить в памяти static переменной.
            }

            var meta = _repository.GetPlugin(id);
            if (meta != null) _lastCheckTime[id] = DateTime.Now;
            return meta;
        }

        // Вспомогательный метод копирования папки
        private void CopyDirectory(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);
            foreach (var file in Directory.GetFiles(sourceDir))
                File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), true);

            foreach (var dir in Directory.GetDirectories(sourceDir))
                CopyDirectory(dir, Path.Combine(destDir, Path.GetFileName(dir)));
        }

        private string ComputeMD5(string fileName)
        {
            using (var md5 = MD5.Create())
            {
                using (var stream = File.OpenRead(fileName))
                {
                    var hash = md5.ComputeHash(stream);
                    return BitConverter.ToString(hash).Replace("-", "").ToUpperInvariant();
                }
            }
        }

        private void CleanupAsync(string pluginId)
        {
            Task.Run(() =>
            {
                try
                {
                    string pluginShadowRoot = Path.Combine(_shadowCopyDir, pluginId);

                    if (!Directory.Exists(pluginShadowRoot)) return;

                    var directories = Directory.GetDirectories(pluginShadowRoot);

                    foreach (var dir in directories)
                    {
                        try
                        {
                            Directory.Delete(dir, true);
                        }
                        catch (IOException)
                        {

                        }
                        catch (UnauthorizedAccessException)
                        {

                        }
                        catch (Exception)
                        {

                        }
                    }
                }
                catch
                {

                }
            });
        }
    }
}
