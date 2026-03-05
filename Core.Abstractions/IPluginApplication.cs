using Autodesk.Revit.UI;

namespace Core.Abstractions
{
    /// <summary>
    /// Интерфейс для фоновых плагинов (IUpdater, Events), которые должны загружаться при старте Revit.
    /// </summary>
    public interface IPluginApplication
    {
        Result OnStartup(UIControlledApplication application);
        Result OnShutdown(UIControlledApplication application);
    }
}
