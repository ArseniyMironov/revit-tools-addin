using Core.Abstractions;
using System.Collections.Generic;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;

namespace Host.Loader
{
    public class JsonRepository
    {
        private readonly string _path;
        private PluginManifest _manifest;

        public JsonRepository(string configPath)
        {
            _path = configPath;
            Load();
        }

        private void Load()
        {
            if (!File.Exists(_path))
            {
                _manifest = new PluginManifest();
                return;
            }

            try
            {
                string json = File.ReadAllText(_path);
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    Encoder = JavaScriptEncoder.Create(UnicodeRanges.BasicLatin, UnicodeRanges.Cyrillic)
                };

                // Теперь мы читаем весь манифест (Хост + Плагины)
                _manifest = JsonSerializer.Deserialize<PluginManifest>(json, options) ?? new PluginManifest(); 
            }
            catch
            {
                _manifest = new PluginManifest();
            }
        }

        public PluginManifest GetManifest() => _manifest;

        public List<PluginMetadata> GetAllPlugins() => _manifest?.Plugins;

        public PluginMetadata GetPlugin(string id) => _manifest?.Plugins?.Find(p => p.Id == id);
    }
}
