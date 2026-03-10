using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Host.Loader
{
    public class App : IExternalApplication
    {
        private const string CONFIG_PATH = @"P:\MOS-TLP\GROUPS\ALLGEMEIN\02_ATP_STANDARDS\07_BIM\01_Settings\01_Add-Ins\001_ATP_Common_Plugin\01_Dev\01_Prod\plugins.json";

        // Переменные для обновления Хоста
        private static bool _needHostUpdate = false;
        private static string _updaterScriptPath = string.Empty;

        public Result OnStartup(UIControlledApplication application)
        {
            try
            {
                // Инициализация генератора динамических классов
                DynamicCommandBuilder.Initialize();

                // Чтение конфига
                var repo = new JsonRepository(CONFIG_PATH);
                var manifest = repo.GetManifest();

                // ---------------------------------------------------------
                // ПРОВЕРКА ОБНОВЛЕНИЙ САМОГО ЗАГРУЗЧИКА
                // --------------------------------------------------------

                CheckHostForUpdates(manifest?.Host);

                var plugins = manifest?.Plugins;
                if (plugins == null) return Result.Succeeded;

                // ЛЕНИВАЯ ЗАГРУЗКА (Кнопки UI)
                var uiPlugins = plugins.Where(p =>
                    p.IsEnabled &&
                    !string.Equals(p.LoadType, "Startup", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(p.TabName) &&
                    !string.IsNullOrWhiteSpace(p.PanelName)
                ).ToList();

                foreach (var meta in uiPlugins)
                {
                    DynamicCommandBuilder.CreateProxyCommandType(meta.Id);
                }

                DynamicCommandBuilder.SaveAssembly();
                string proxyAssemblyPath = DynamicCommandBuilder.GetProxyDllPath();

                // Построение UI
                foreach (var meta in uiPlugins)
                {
                    if (!meta.IsEnabled) continue;

                    try
                    {
                        // Создаем/Получаем вкладку
                        application.CreateRibbonTab(meta.TabName);
                    }
                    catch { }

                    // Создаем/Получаем панель
                    RibbonPanel panel = null;
                    List<RibbonPanel> panels = application.GetRibbonPanels(meta.TabName);
                    foreach (var p in panels)
                    {
                        if (p.Name == meta.PanelName)
                        {
                            panel = p;
                            break;
                        }
                    }
                    if (panel == null) 
                        panel = application.CreateRibbonPanel(meta.TabName, meta.PanelName);

                    // Создание кнопки
                    PushButtonData btnData = new PushButtonData(
                        $"btn_{meta.Id}",
                        meta.ButtonTitle ?? meta.Id,
                        proxyAssemblyPath,
                        $"ProxyCommand_{meta.Id}"
                    );

                    btnData.ToolTip = meta.Tooltip;
                    panel.AddItem(btnData);
                }

                // ХОЛОДНАЯ ЗАГРУЗКА (Фоновые плагины)
                var startupPlugins = plugins.Where(p => p.IsEnabled && string.Equals(p.LoadType, "Startup", StringComparison.OrdinalIgnoreCase)).ToList();

                foreach (var meta in startupPlugins)
                {
                    try
                    {
                        PluginManager.initializeStartupPlugin(meta, application);
                    }
                    catch (Exception ex)
                    {
                        TaskDialog.Show("Startup Error", $"Ошибка запуска фонового плагина {meta.Id}:\n{ex.Message}");
                    }
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Host Error", "Ошибка инициализации плагинов:\n" + ex.Message);
                return Result.Failed;
            }
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            PluginManager.ShutdownStartupPlugins(application);

            // ЗАПУСК СКРИПТА ОБНОВЛЕНИЯ ХОСТА ПОСЛЕ ЗАКРЫТИЯ REVIT
            if (_needHostUpdate && File.Exists(_updaterScriptPath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = _updaterScriptPath,
                    UseShellExecute = true,
                    CreateNoWindow = false,
                    WindowStyle = ProcessWindowStyle.Normal
                });
            }

            return Result.Succeeded;
        }

        // --- ЛОГИКА ГЕНЕРАЦИИ СКРИПТА ОБНОВЛЕНИЯ ---
        private void CheckHostForUpdates(HostData hostData)
        {
            if (hostData == null || string.IsNullOrWhiteSpace(hostData.ServerFolder)) 
                return;

            try
            {
                // Текущая версия этого файла (Host.Loader.dll)
                Version currentVersion = Assembly.GetExecutingAssembly().GetName().Version;

                if (Version.TryParse(hostData.Version, out Version serverVersion))
                {
                    if (serverVersion > currentVersion)
                    {
                        // Определяем, где физически установлен наш плагин
                        string localHostDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                        string serverDir = hostData.ServerFolder.TrimEnd('\\');

                        _updaterScriptPath = Path.Combine(Path.GetTempPath(), "ATP_HostUpdater.bat");

                        // Пишем скрипт. Он ждет 30 секунды (пока процесс Revit окончательно умрет),
                        // затем копирует новые файлы Хоста из серверной папки поверх текущих и самоудаляется.
                        string batContent = $@"@echo off
                                                echo Выполняется обновление системного ядра ATP BIM...
                                                echo Пожалуйста, подождите.
                                                timeout /t 30 /nobreak >nul
                                                xcopy /Y /E /I ""{serverDir}\*"" ""{localHostDir}\""
                                                del ""%~f0""
                                                ";

                        // Обязательно в 866 кодировке, чтобы кириллица в консоли отображалась нормально
                        File.WriteAllText(_updaterScriptPath, batContent, System.Text.Encoding.GetEncoding(866));

                        _needHostUpdate = true;
                    }
                }
            }
            catch { }
        }
    }
}
