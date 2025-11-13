using System;
using System.IO;
using System.Collections.Generic;

namespace CamoReader
{
    public class ConfigManager
    {
        private string filePath;
        private Dictionary<string, string> settings;

        public int WindowPosX { get; private set; }
        public int WindowPosY { get; private set; }
        public int WindowWidth { get; private set; }
        public int WindowHeight { get; private set; }
        public int TextSize { get; private set; }
        public string TextFilePath { get; private set; } = string.Empty;
        public int BrightnessShiftRatio { get; private set; }
        public int ColorShiftRatio { get; private set; }

        public ConfigManager(string path)
        {
            filePath = path;
            settings = new Dictionary<string, string>();
            LoadConfig();
        }

        private void LoadConfig()
        {
            if (!File.Exists(filePath))
            {
                CreateDefaultConfig();
            }

            foreach (string line in File.ReadAllLines(filePath))
            {
                if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith(";"))
                    continue;

                string[] parts = line.Split(new[] { '=' }, 2);
                if (parts.Length == 2)
                {
                    settings[parts[0].Trim()] = parts[1].Trim();
                }
            }

            WindowPosX = GetInt("WindowPosX", 100);
            WindowPosY = GetInt("WindowPosY", 100);
            WindowWidth = GetInt("WindowWidth", 800);
            WindowHeight = GetInt("WindowHeight", 200);
            TextSize = GetInt("TextSize", 14);
            TextFilePath = GetString("TextFilePath", "book.txt");
            BrightnessShiftRatio = GetInt("BrightnessShiftRatio", 30);
            ColorShiftRatio = GetInt("ColorShiftRatio", 20);
        }

        private void CreateDefaultConfig()
        {
            string defaultConfig = @"; Camo-Reader Configuration
; Window position and size
WindowPosX = 100
WindowPosY = 100
WindowWidth = 800
WindowHeight = 200

; Text settings
TextSize = 14
TextFilePath = book.txt

; Adaptive transparency settings (0-100)
; BrightnessShiftRatio: How much to shift brightness away from background (higher = more contrast)
BrightnessShiftRatio = 30
; ColorShiftRatio: How much to shift color away from background (higher = more color difference)
ColorShiftRatio = 20
";
            File.WriteAllText(filePath, defaultConfig);
        }

        private int GetInt(string key, int defaultValue)
        {
            if (settings.ContainsKey(key) && int.TryParse(settings[key], out int v))
            {
                // Clamp shift ratios to 0-100
                if (key == "BrightnessShiftRatio" || key == "ColorShiftRatio")
                {
                    return Math.Max(0, Math.Min(100, v));
                }
                return v;
            }
            return defaultValue;
        }

        private string GetString(string key, string defaultValue)
        {
            return settings.ContainsKey(key) ? settings[key] : defaultValue;
        }

        public void IncreaseBrightnessShift()
        {
            if (BrightnessShiftRatio < 100)
            {
                BrightnessShiftRatio = Math.Min(100, BrightnessShiftRatio + 5);
                SaveConfig();
            }
        }

        public void DecreaseBrightnessShift()
        {
            if (BrightnessShiftRatio > 0)
            {
                BrightnessShiftRatio = Math.Max(0, BrightnessShiftRatio - 5);
                SaveConfig();
            }
        }

        private void SaveConfig()
        {
            settings["BrightnessShiftRatio"] = BrightnessShiftRatio.ToString();
            
            List<string> lines = new List<string>();
            lines.Add("; Camo-Reader Configuration");
            lines.Add("; Window position and size");
            lines.Add($"WindowPosX = {WindowPosX}");
            lines.Add($"WindowPosY = {WindowPosY}");
            lines.Add($"WindowWidth = {WindowWidth}");
            lines.Add($"WindowHeight = {WindowHeight}");
            lines.Add("");
            lines.Add("; Text settings");
            lines.Add($"TextSize = {TextSize}");
            lines.Add($"TextFilePath = {TextFilePath}");
            lines.Add("");
            lines.Add("; Adaptive transparency settings (0-100)");
            lines.Add("; BrightnessShiftRatio: How much to shift brightness away from background (higher = more contrast)");
            lines.Add($"BrightnessShiftRatio = {BrightnessShiftRatio}");
            lines.Add("; ColorShiftRatio: How much to shift color away from background (higher = more color difference)");
            lines.Add($"ColorShiftRatio = {ColorShiftRatio}");
            
            File.WriteAllLines(filePath, lines);
        }
    }
}