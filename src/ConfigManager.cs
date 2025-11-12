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
        
        // FIX: Initialized to default value to satisfy CS8618 warning
        public string TextFilePath { get; private set; } = string.Empty; 
        
        public int BrightnessShiftRatio { get; private set; }
        public int ColorShiftRatio { get; private set; }
        
        // FIX: Added TextOpacity
        public int TextOpacity { get; private set; }

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
            BrightnessShiftRatio = GetInt("BrightnessShiftRatio", 50);
            ColorShiftRatio = GetInt("ColorShiftRatio", 50);
            // FIX: Load TextOpacity, default to 75
            TextOpacity = GetInt("TextOpacity", 75);
        }

        private void CreateDefaultConfig()
        {
            string defaultConfig = @"; Camo-Reader Configuration
WindowPosX = 100
WindowPosY = 100
WindowWidth = 800
WindowHeight = 200
TextSize = 14
TextFilePath = book.txt
BrightnessShiftRatio = 50
ColorShiftRatio = 50
TextOpacity = 75
"; // Added TextOpacity
            File.WriteAllText(filePath, defaultConfig);
        }

        private int GetInt(string key, int defaultValue)
        {
            // Clamp opacity to a valid range
            if (key == "TextOpacity")
            {
                int val = settings.ContainsKey(key) && int.TryParse(settings[key], out int oVal) ? oVal : defaultValue;
                return Math.Max(0, Math.Min(100, val)); // Clamp 0-100
            }
            return settings.ContainsKey(key) && int.TryParse(settings[key], out int v) ? v : defaultValue;
        }

        private string GetString(string key, string defaultValue)
        {
            return settings.ContainsKey(key) ? settings[key] : defaultValue;
        }
    }
}