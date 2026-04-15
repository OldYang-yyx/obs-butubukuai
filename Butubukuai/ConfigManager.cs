using System;
using System.IO;
using System.Text.Json;

namespace Butubukuai
{
    public class AppConfig
    {
        public string ApiKey { get; set; } = string.Empty;
        public string AppId { get; set; } = string.Empty;
        public string BannedWords { get; set; } = "测试违禁词,傻X,SB,弱智"; // 逗号分隔
    }

    public static class ConfigManager
    {
        private const string ConfigFile = "appconfig.json";

        public static AppConfig Load()
        {
            try
            {
                if (File.Exists(ConfigFile))
                {
                    string json = File.ReadAllText(ConfigFile);
                    var config = JsonSerializer.Deserialize<AppConfig>(json);
                    return config ?? new AppConfig();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"读取配置失败: {ex.Message}");
            }
            return new AppConfig();
        }

        public static void Save(AppConfig config)
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(config, options);
                File.WriteAllText(ConfigFile, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"写入配置失败: {ex.Message}");
            }
        }
    }
}
