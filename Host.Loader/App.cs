using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Host.Loader
{
    public class App : IExternalApplication
    {
        private const string CONFIG_PATH = @"P:\MOS-TLP\GROUPS\ALLGEMEIN\02_ATP_STANDARDS\07_BIM\01_Settings\01_Add-Ins\001_ATP_Common_Plugin\01_Dev\01_Prod\plugins.json";

        public Result OnStartup(UIControlledApplication application)
        {
            //TaskDialog.Show("DEBUG", "Я запустился! Сейчас буду читать конфиг: " + CONFIG_PATH);
            try
            {
                // 1. Инициализация генератора динамических классов
                DynamicCommandBuilder.Initialize();

                // 2. Чтение конфига
                var repo = new JsonRepository(CONFIG_PATH); 
                var plugins = repo.GetAllPlugins();

                if (plugins == null) return Result.Succeeded;

                // 3. ЛЕНИВАЯ ЗАГРУЗКА (Кнопки UI)
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
                    if (panel == null) panel = application.CreateRibbonPanel(meta.TabName, meta.PanelName);

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

                // 4. ХОЛОДНАЯ ЗАГРУЗКА (Фоновые плагины)
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
            return Result.Succeeded;
        }
    }
}
