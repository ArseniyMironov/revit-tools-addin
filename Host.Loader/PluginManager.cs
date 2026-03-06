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

        // Хранилище запущенных фоновых плагинов
        private static List<IPluginApplication> _runningApplication = new List<IPluginApplication>();

        public PluginManager()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            _localCacheDir = Path.Combine(appData, "ATP-TLP", "RevitPlugins", "Cache");
            _shadowCopyDir = Path.Combine(Path.GetTempPath(), "ATP-TLP", "RevitPlugins", "Shadow");
            _repository = new JsonRepository(@"P:\MOS-TLP\GROUPS\ALLGEMEIN\02_ATP_STANDARDS\07_BIM\01_Settings\01_Add-Ins\001_ATP_Common_Plugin\01_Dev\01_Prod\plugins.json");
        }

        // =========================================================
        // ОБЩЕЕ ЯДРО (ЗАГРУЗКА И СИНХРОНИЗАЦИЯ ФАЙЛОВ)
        // =========================================================
        private Assembly PrepareAndLoadAssembly(PluginMetadata meta)
        {
            // 1. СИНХРОНИЗАЦИЯ С СЕРВЕРОМ
            string cachedPluginFolder = Path.Combine(_localCacheDir, meta.Id, meta.Version);
            string cachedAssemblyPath = Path.Combine(cachedPluginFolder, meta.MainAssembly);
            bool needDownload = true;

            if (Directory.Exists(cachedPluginFolder) && File.Exists(cachedAssemblyPath))
            {
                string localHash = ComputeMD5(cachedAssemblyPath);
                if (string.Equals(localHash, meta.BuildHash, StringComparison.OrdinalIgnoreCase))
                {
                    needDownload = false;
                }
            }

            if (needDownload)
            {
                if (string.IsNullOrEmpty(meta.ServerFolder) || !Directory.Exists(meta.ServerFolder)) 
                    throw new Exception($"Сервер недоступен: {meta.ServerFolder}");

                if (Directory.Exists(cachedPluginFolder))
                    Directory.Delete(cachedPluginFolder, true);
                CopyDirectory(meta.ServerFolder, cachedPluginFolder);
            }

            // 2. СОЗДАНИЕ SHADOW COPY
            string sessionGuid = Guid.NewGuid().ToString();
            string shadowFolder = Path.Combine(_shadowCopyDir, meta.Id, sessionGuid);
            CopyDirectory(cachedPluginFolder, shadowFolder);
            string shadowAssemblyPath = Path.Combine(shadowFolder, meta.MainAssembly);

            bool isStartup = string.Equals(meta.LoadType, "Startup", StringComparison.OrdinalIgnoreCase);

            // 3. ПРЕДВАРИТЕЛЬНАЯ ЗАГРУЗКА ЗАВИСИМОСТЕЙ
            string[] allDlls = Directory.GetFiles(shadowFolder, "*.dll");
            foreach (string dllPath in allDlls)
            {
                string fName = Path.GetFileName(dllPath);
                if (fName.Equals(meta.MainAssembly, StringComparison.OrdinalIgnoreCase))
                    continue;

                // ЗАЩИТА ОТ КОНФЛИКТА ТИПОВ: Ни в коем случае не грузим API Revit повторно!
                if (fName.StartsWith("RevitAPI", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (fName.StartsWith("AdWindows", StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    if (isStartup)
                        Assembly.LoadFrom(dllPath);
                    else
                        Assembly.Load(File.ReadAllBytes(dllPath));
                }
                catch 
                { 
                    // Log Exception
                }
            }

            // 4. ЗАГРУЗКА ОСНОВНОЙ СБОРКИ
            Assembly mainAssembly;
            if (isStartup)
            {
                mainAssembly = Assembly.LoadFrom(shadowAssemblyPath);
            }
            else
            {
                byte[] mainAssemblyBytes = File.ReadAllBytes(shadowAssemblyPath);
                byte[] pdbBytes = null;
                string pdbPath = Path.ChangeExtension(shadowAssemblyPath, ".pdb");
                if (File.Exists(pdbPath)) 
                    pdbBytes = File.ReadAllBytes(pdbPath);

                mainAssembly = pdbBytes != null ?
                    Assembly.Load(mainAssemblyBytes, pdbBytes) :
                    Assembly.Load(mainAssemblyBytes);
            }

            // Запускаем асинхронную очистку старых папок
            CleanupAsync(meta.Id);

            return mainAssembly;
        }

        // =========================================================
        // ХОЛОДНАЯ ЗАГРУЗКА (ФОНОВЫЕ ПРОЦЕССЫ - STARTUP)
        // =========================================================
        public static void initializeStartupPlugin(PluginMetadata meta, UIControlledApplication app)
        {
            var manager = new PluginManager();
            manager.InitializeStartupInternal(meta, app);
        }

        private void InitializeStartupInternal(PluginMetadata meta, UIControlledApplication app)
        {
            // Получаем готовую сборку через общее ядро
            Assembly mainAssembly = PrepareAndLoadAssembly(meta);

            // Ищем и запускаем IPluginApplication
            foreach (Type type in mainAssembly.GetTypes())
            {
                if (typeof(IPluginApplication).IsAssignableFrom(type) && !type.IsInterface && !type.IsAbstract)
                {
                    var attr = type.GetCustomAttribute<RevitPluginAttribute>();
                    if (attr != null && string.Equals(attr.Id, meta.Id, StringComparison.OrdinalIgnoreCase))
                    {
                        var appInsatnce = (IPluginApplication)Activator.CreateInstance(type);
                        appInsatnce.OnStartup(app);
                        _runningApplication.Add(appInsatnce);
                        break;
                    }
                }
            }
        }

        public static void ShutdownStartupPlugins(UIControlledApplication app)
        {
            foreach (var instance in _runningApplication)
            {
                try
                {
                    instance.OnShutdown(app);
                }
                catch
                {
                    // Log Exception
                }
                _runningApplication.Clear();
            }
        }

        // =========================================================
        // ЛЕНИВАЯ ЗАГРУЗКА (КНОПКИ - ON CLICK)
        // =========================================================
        public Result Run(string pluginId, ExternalCommandData data, ref string msg, ElementSet elem)
        {
            try
            {
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

                // Получаем готовую сборку через общее ядро
                Assembly mainAssembly = PrepareAndLoadAssembly(meta);

                // Ищем класс команды IPluginCommand
                Type commandType = null;
                foreach (Type type in mainAssembly.GetTypes())
                {
                    var attr = type.GetCustomAttribute<RevitPluginAttribute>();
                    if (attr != null && string.Equals(attr.Id, pluginId, StringComparison.OrdinalIgnoreCase))
                    {
                        if (typeof(IPluginCommand).IsAssignableFrom(type))
                        {
                            commandType = type;
                            break;
                        }
                        else
                        {
                            TaskDialog.Show("Ошибка архитектуры", $"Класс с ID '{pluginId}' найден, но он является фоновым процессом (IPluginApplication), а не командой-кнопкой!");
                            return Result.Failed;
                        }
                    }
                }

                if (commandType == null)
                {
                    msg = $"Class with ID {pluginId} not found";
                    return Result.Failed;
                }

                var commandInstance = (IPluginCommand)Activator.CreateInstance(commandType);
                return commandInstance.Execute(data, ref msg, elem);
            }
            catch (Exception ex)
            {
                TaskDialog.Show("CRITICAL ERROR", $"Unexpected error: {ex.Message}");
                msg = ex.Message;
                return Result.Failed;
            }
        }

        public static Result RunStatic(string pluginId, ExternalCommandData data, ref string msg, ElementSet elem)
        {
            var manager = new PluginManager();
            return manager.Run(pluginId, data, ref msg, elem);
        }

        // =========================================================
        // УТИЛИТЫ 
        // =========================================================
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
            using (var stream = File.OpenRead(fileName))
            {
                var hash = md5.ComputeHash(stream);
                return BitConverter.ToString(hash).Replace("-", "").ToUpperInvariant();
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
                            // Log Exception
                        }
                        catch (UnauthorizedAccessException)
                        {
                            // Log Exception
                        }
                        catch (Exception)
                        {
                            // Log Exception
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
