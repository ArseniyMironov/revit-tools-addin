namespace Core.Abstractions
{
    /// <summary>
    /// Определяет стратегию загрузки плагина в Revit.
    /// </summary>
    public enum PluginLoadType
    {
        /// <summary>
        /// Ленивая загрузка. Плагин скачивается и запускается только при нажатии на кнопку.
        /// Требует реализации IPluginCommand.
        /// </summary>
        OnClick,

        /// <summary>
        /// Холодная загрузка. Плагин скачивается и загружается при старте Revit.
        /// Требует реализации IPluginApplication. (Используется для IUpdater и подписок на события).
        /// </summary>
        Startup
    }
}
