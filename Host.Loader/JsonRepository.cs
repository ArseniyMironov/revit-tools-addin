using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Host.Loader
{
    public class JsonRepository
    {
        private readonly string _configPath;

        public JsonRepository(string configPath)
        {
            _configPath = configPath;
        }

        public PluginMetadata GetPlugin(string id)
        {
            if (!File.Exists(_configPath)) return null;

            string json = File.ReadAllText(_configPath);
            var plugins = JsonSerializer.Deserialize<List<PluginMetadata>>(json);

            return plugins?.FirstOrDefault(p => p.Id == id);
        }

        public List<PluginMetadata> GetAllPlugins()
        {
            if (!File.Exists(_configPath)) return new List<PluginMetadata>();
            try
            {
                string json = File.ReadAllText(_configPath);
                return JsonSerializer.Deserialize<List<PluginMetadata>>(json);
            }
            catch { return new List<PluginMetadata>(); }
        }
    }
}
