using System;
using System.Collections.Generic;
using System.IO;

namespace CamoReader
{
    /// <summary>
    /// Minimal INI-like configuration manager for the app.
    /// Supports simple key=value lines (comments starting with # or ; are ignored).
    /// Values are kept in-memory and can be saved back to the file.
    /// </summary>
    public class ConfigManager
    {
        private readonly string _path;
        private readonly Dictionary<string, string> _values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public ConfigManager(string path)
        {
            _path = path;
            Load();
        }

        private void Load()
        {
            _values.Clear();
            if (!File.Exists(_path)) return;

            foreach (var raw in File.ReadAllLines(_path))
            {
                var line = raw.Trim();
                if (string.IsNullOrEmpty(line)) continue;
                if (line.StartsWith("#") || line.StartsWith(";")) continue;

                var idx = line.IndexOf('=');
                if (idx <= 0) continue;

                var key = line.Substring(0, idx).Trim();
                var val = line.Substring(idx + 1).Trim();
                _values[key] = val;
            }
        }

        public string? Get(string key, string? defaultValue = null)
        {
            if (_values.TryGetValue(key, out var v)) return v;
            return defaultValue;
        }

        public void Set(string key, string value)
        {
            _values[key] = value;
        }

        public void Save()
        {
            try
            {
                using var sw = new StreamWriter(_path, false);
                foreach (var kv in _values)
                {
                    sw.WriteLine($"{kv.Key}={kv.Value}");
                }
            }
            catch (Exception)
            {
                // Swallow exceptions for now - caller may handle file permissions etc.
            }
        }
    }
}
