using Autodesk.Revit.UI;
using Core.Abstractions;
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
        // Переменные для обновления Хоста
        private static bool _needHostUpdate = false;
        private static string _updaterScriptPath = string.Empty;

        public Result OnStartup(UIControlledApplication application)
        {
            try
            {// Убеждаемся, что базовые локальные папки существуют
                HostEnvironment.InitializeDirectories();

                // Инициализация генератора динамических классов
                DynamicCommandBuilder.Initialize();

                // Чтение конфига
                var repo = new JsonRepository(HostEnvironment.ServerConfigPath);
                var manifest = repo.GetManifest();

                // Инициализация логгера (папка Logs рядом с plugins.json) (пока передаем логин Windows как временную заглушку)
                string logFolder = Path.Combine(Path.GetDirectoryName(HostEnvironment.LogsDir), "Logs");
                Core.Abstractions.Logger.Initialize(logFolder, Environment.UserName);
                Core.Abstractions.Logger.Info("Host.Loader", "Revit Started");

                application.Idling += Application_Idling;

                // ---------------------------------------------------------
                // ПРОВЕРКА ОБНОВЛЕНИЙ САМОГО ЗАГРУЗЧИКА
                // --------------------------------------------------------

                CheckHostForUpdates(manifest?.Host);

                // ---------------------------------------------------------
                // АСИНХРОННАЯ ОЧИСТКА УСТАРЕВШЕГО КЭША
                // ---------------------------------------------------------
                PluginManager.CleanupCacheAsync(manifest?.Plugins);

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

                    // УСТАНОВКА ПОДСКАЗКИ
                    btnData.ToolTip = meta.Tooltip;

                    // УСТАНОВКА ИКОНКИ
                    var icon = GetIconFromBase64(meta.IconBase64);
                    if (icon != null)
                    {
                        btnData.LargeImage = icon;
                    }

                    panel.AddItem(btnData);
                }

                // ХОЛОДНАЯ ЗАГРУЗКА (Фоновые плагины)
                var startupPlugins = plugins.Where(p => p.IsEnabled && string.Equals(p.LoadType, "Startup", StringComparison.OrdinalIgnoreCase)).ToList();

                foreach (var meta in startupPlugins)
                {
                    try
                    {
                        PluginManager.InitializeStartupPlugin(meta, application);
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
            try
            {
                if (_needHostUpdate && !string.IsNullOrEmpty(_updaterScriptPath) && File.Exists(_updaterScriptPath))
                {
                    // Явный вызов командной строки
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/c \"{_updaterScriptPath}\"",
                        UseShellExecute = false,
                        CreateNoWindow = false
                    };

                    Process.Start(startInfo);
                }

                Core.Abstractions.Logger.Info("Host.Loader", "Revit Shutdown");
                Core.Abstractions.Logger.Shutdown();
            }
            catch (Exception ex)
            {
                File.WriteAllText(Path.Combine(Path.GetTempPath(), "HostShutdown_Error.txt"), ex.ToString());
            }

            try
            {
                PluginManager.ShutdownStartupPlugins(application);
            }
            catch
            {

            }

            return Result.Succeeded;
        }

        // --- ЛОГИКА ГЕНЕРАЦИИ СКРИПТА ОБНОВЛЕНИЯ ---
        private void CheckHostForUpdates(HostData hostData)
        {
            // ПРОВЕРКА 1: Пришли ли данные из JSON вообще?
            if (hostData == null)
            {
                //TaskDialog.Show("DEBUG", "hostData == null. JSON не прочитался или структура неверная.");
                return;
            }

            // ПРОВЕРКА 2: Заполнен ли путь к серверной папке?
            if (string.IsNullOrWhiteSpace(hostData.ServerFolder))
            {
                //TaskDialog.Show("DEBUG", $"ServerFolder пуст! Версия в JSON: {hostData.Version}. Билдер не записал путь к Хосту.");
                return;
            }

            try
            {
                Version currentVersion = Assembly.GetExecutingAssembly().GetName().Version;

                // ПРОВЕРКА 3: Что видит программа?
                //TaskDialog.Show("DEBUG", $"Текущая (локальная) версия: {currentVersion}\nВерсия в JSON: {hostData.Version}");

                if (Version.TryParse(hostData.Version, out Version serverVersion))
                {
                    if (serverVersion > currentVersion)
                    {
                        string localHostDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location).TrimEnd('\\');
                        string serverDir = hostData.ServerFolder.TrimEnd('\\');

                        _updaterScriptPath = Path.Combine(Path.GetTempPath(), "ATP_HostUpdater.bat");

                        string batContent = $@"@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion
set LOG_FILE=""%TEMP%\HostUpdateLog.txt""
echo [%time%] Старт обновления... > !LOG_FILE!
echo Путь сервера: ""{serverDir}"" >> !LOG_FILE!
echo Локальный путь: ""{localHostDir}"" >> !LOG_FILE!

set tryCount=0
:loop
timeout /t 2 /nobreak >nul
echo [%time%] Попытка !tryCount! >> !LOG_FILE!

xcopy /Y /E /I ""{serverDir}\*"" ""{localHostDir}"" >> !LOG_FILE! 2>&1

if !errorlevel! neq 0 (
    set /a tryCount+=1
    if !tryCount! lss 15 goto loop
)

echo [%time%] Завершено с кодом: !errorlevel! >> !LOG_FILE!
del ""%~f0""
";
                        File.WriteAllText(_updaterScriptPath, batContent, System.Text.Encoding.GetEncoding(866));

                        _needHostUpdate = true;

                        // ПРОВЕРКА 4: Скрипт записан успешно
                        //TaskDialog.Show("DEBUG", "Скрипт .bat успешно сгенерирован в Temp!");

                        // Вызов Toast вынесен в конец, чтобы проверить, не падает ли он
                        Core.Abstractions.Toast.Show("Доступно обновление ядра...", 7);
                    }
                    else
                    {
                        //TaskDialog.Show("DEBUG", "Серверная версия НЕ больше текущей (по логике кода).");
                    }
                }
                else
                {
                    //TaskDialog.Show("DEBUG", "Ошибка TryParse: не удалось распознать формат версии из JSON.");
                }
            }
            catch (Exception ex)
            {
                // ПРОВЕРКА 5: Ловим скрытую ошибку
                TaskDialog.Show("DEBUG FATAL ERROR", ex.ToString());
            }
        }

        private System.Windows.Media.ImageSource GetIconFromBase64(string base64String)
        {
            if (string.IsNullOrWhiteSpace(base64String)) 
                return null;

            try
            {
                byte[] imageBytes = Convert.FromBase64String(base64String);
                using (var ms = new MemoryStream(imageBytes))
                {
                    var image = new System.Windows.Media.Imaging.BitmapImage();
                    image.BeginInit();
                    image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    image.StreamSource = ms;
                    image.EndInit();
                    image.Freeze(); // Замораживаем объект для безопасного использования в UI-потоке Revit
                    return image;
                }
            }
            catch
            {
                return null;
            }
        }

        private void Application_Idling(object sender, Autodesk.Revit.UI.Events.IdlingEventArgs e)
        {
            // В момент этого события sender является полноценным UIApplication
            var uiApp = sender as UIApplication;
            if (uiApp != null)
            {
                // Берем Username из настроек Revit
                string realRevitUser = uiApp.Application.Username;

                // Обновляем имя в логгере
                Core.Abstractions.Logger.SetRevitUserName(realRevitUser);

                // отписываемся от события, чтобы оно не срабатывало каждую секунду
                uiApp.Idling -= Application_Idling;
            }
        }
    }
}
