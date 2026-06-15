using System;
using System.IO;
using System.Text.Json;

namespace AltRunSharp
{
    public class ConfigService : IConfigService
    {
        private readonly string _configPath;

        public ConfigService(string configPath)
        {
            _configPath = configPath;
        }

        public AppConfig LoadConfig()
        {
            int attempts = 5;
            int[] delays = { 50, 100, 200, 300 };
            for (int i = 0; i < attempts; i++)
            {
                try
                {
                    if (File.Exists(_configPath))
                    {
                        string json = File.ReadAllText(_configPath);
                        var config = JsonSerializer.Deserialize(json, AppConfigJsonContext.Default.AppConfig);
                        return config ?? new AppConfig();
                    }
                    else
                    {
                        return new AppConfig();
                    }
                }
                catch (JsonException)
                {
                    break;
                }
                catch (IOException) when (i < attempts - 1)
                {
                    System.Threading.Thread.Sleep(delays[i]);
                }
                catch (Exception)
                {
                    break;
                }
            }
            return new AppConfig();
        }

        public void SaveConfig(AppConfig config)
        {
            try
            {
                string dir = Path.GetDirectoryName(_configPath)!;
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                string json = JsonSerializer.Serialize(config, AppConfigJsonContext.Default.AppConfig);
                File.WriteAllText(_configPath, json);
            }
            catch (Exception)
            {
                // Graceful error handling
            }
        }
    }
}
