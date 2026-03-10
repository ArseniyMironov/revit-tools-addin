using System.Collections.Generic;

namespace Host.Loader
{

    public class HostData
    {
        public string Version { get; set; } = "1.0.0.0";
        public string ServerFolder { get; set; } = "";
    }

    public class PluginManifest
    {
        public HostData Host { get; set; } = new HostData();
        public List<PluginMetadata> Plugins { get; set; } = new List<PluginMetadata>();
    }

    /// <summary>
    /// Единый контракт данных для plugins.json
    /// Используется и Билдером (для записи), и Хостом (для чтения).
    /// </summary>
    public class PluginMetadata
    {
        public string Id { get; set; }            // Уникальное имя
        public string Version { get; set; }       // "1.0.0"

        // --- ТИП ЗАГРУЗКИ ---
        public string LoadType { get; set; } = "Startup";

        // --- БЕЗОПАСНОСТЬ И КОНТРОЛЬ ---
        public bool IsEnabled { get; set; }       // Стоп-кран
        public string DisableReason { get; set; } // Причина отключения
        public string BuildHash { get; set; }

        // --- ЛОКАЦИЯ ---
        public string ServerFolder { get; set; }
        public string MainAssembly { get; set; }

        // --- UI --
        public string TabName { get; set; }
        public string PanelName { get; set; }
        public string ButtonTitle { get; set; }
        public string Tooltip { get; set; }
        //public string ImageName { get; set; }
    }
}
