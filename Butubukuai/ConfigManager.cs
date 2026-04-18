using System;
using System.IO;
using System.Text.Json;
using System.Collections.ObjectModel;

namespace Butubukuai
{
    public class RuleGroup : System.ComponentModel.INotifyPropertyChanged
    {
        private string _groupName = string.Empty;
        public string GroupName
        {
            get => _groupName;
            set { _groupName = value; OnPropertyChanged(); }
        }

        private string _words = string.Empty;
        public string Words
        {
            get => _words;
            set { _words = value; OnPropertyChanged(); }
        }

        private string _soundPath = string.Empty;
        public string SoundPath
        {
            get => _soundPath;
            set { _soundPath = value; OnPropertyChanged(); }
        }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
        }
    }

    public class AppConfig
    {
        public string ApiKey { get; set; } = string.Empty;
        public string AppId { get; set; } = string.Empty;
        public string ObsIpAddress { get; set; } = "127.0.0.1";
        public int ObsPort { get; set; } = 4455;
        public string ObsPassword { get; set; } = "123456";
        public ObservableCollection<RuleGroup> RuleGroups { get; set; } = new ObservableCollection<RuleGroup>();
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
