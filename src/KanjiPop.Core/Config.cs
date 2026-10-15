using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace KanjiPop
{
    /// <summary>User settings, stored as JSON in %APPDATA%\KanjiPop\config.json.</summary>
    public sealed class Config
    {
        /// <summary>Key combo, e.g. "Ctrl+C", "Ctrl+Alt+J", "F8".</summary>
        public string Hotkey { get; set; } = OperatingSystem.IsMacOS() ? "Cmd+C" : "Ctrl+C";
        /// <summary>Press the hotkey twice within DoubleTapMs to trigger.</summary>
        public bool DoubleTap { get; set; } = true;
        public int DoubleTapMs { get; set; } = 500;

        public bool ShowRomaji { get; set; } = false;
        public bool ShowKanaWords { get; set; } = false;
        public int MaxEntries { get; set; } = 3;
        public int MaxSensesPerEntry { get; set; } = 5;
        public int MaxKanji { get; set; } = 10;
        /// <summary>App names (Windows: process name e.g. "game"; macOS: app name e.g. "Steam") in which the hotkey is ignored.</summary>
        public List<string> PausedApps { get; set; } = new();
        public int MaxHistory { get; set; } = 30;
        public int MaxChars { get; set; } = 120;
        public double FontSize { get; set; } = 14;
        /// <summary>0 = stay until Esc / click elsewhere.</summary>
        public int AutoCloseSeconds { get; set; } = 0;

        public static string Dir => OperatingSystem.IsMacOS()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "KanjiPop")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KanjiPop");
        public static string FilePath => Path.Combine(Dir, "config.json");

        static readonly JsonSerializerOptions Opts = new()
        {
            WriteIndented = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        public static Config Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    return JsonSerializer.Deserialize<Config>(File.ReadAllText(FilePath), Opts) ?? new Config();
            }
            catch { /* fall through to defaults */ }
            var c = new Config();
            c.Save();
            return c;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Opts));
            }
            catch { }
        }
    }
}
