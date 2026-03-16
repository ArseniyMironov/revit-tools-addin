using System;
using System.IO;

namespace Host.Loader
{
    public static class HostEnvironment
    {
        // ГЛАВНЫЙ ПУТЬ К СЕРВЕРУ
        public static readonly string ServerConfigPath = @"P:\MOS-TLP\GROUPS\ALLGEMEIN\02_ATP_STANDARDS\07_BIM\01_Settings\01_Add-Ins\001_ATP_Common_Plugin\01_Dev\01_Prod\plugins.json";

        // Локальные папки
        public static string LocalCacheDir { get; }
        public static string ShadowCopyDir { get; }
        public static string LogsDir { get; }

        static HostEnvironment()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            LocalCacheDir = Path.Combine(appData, "ATP-TLP", "RevitPlugins", "Cache");

            ShadowCopyDir = Path.Combine(Path.GetTempPath(), "ATP-TLP", "RevitPlugins", "Shadow");

            // Папка логов лежит на сервере рядом с конфигом
            LogsDir = Path.Combine(Path.GetDirectoryName(ServerConfigPath), "Logs");
        }

        // Вспомогательный метод для создания базовых директорий при старте
        public static void InitializeDirectories()
        {
            try
            {
                if (!Directory.Exists(LocalCacheDir)) Directory.CreateDirectory(LocalCacheDir);
                if (!Directory.Exists(ShadowCopyDir)) Directory.CreateDirectory(ShadowCopyDir);
            }
            catch { }
        }
    }
}
