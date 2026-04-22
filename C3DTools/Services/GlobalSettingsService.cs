using C3DTools.Models;
using System;
using System.IO;
using System.Text.Json;

namespace C3DTools.Services
{
    /// <summary>
    /// Persists and loads BasinSettings to/from %APPDATA%\C3DTools\settings.json.
    /// </summary>
    public class GlobalSettingsService
    {
        private static readonly string SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "C3DTools",
            "settings.json");

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        /// <summary>
        /// Loads global settings. Returns defaults silently if the file is missing or corrupt.
        /// </summary>
        public BasinSettings Load()
        {
            try
            {
                if (!File.Exists(SettingsPath))
                    return BasinSettings.CreateDefaults();

                string json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<BasinSettings>(json, JsonOptions)
                       ?? BasinSettings.CreateDefaults();
            }
            catch
            {
                return BasinSettings.CreateDefaults();
            }
        }

        /// <summary>
        /// Saves settings to %APPDATA%\C3DTools\settings.json.
        /// </summary>
        public void Save(BasinSettings settings)
        {
            try
            {
                string? dir = Path.GetDirectoryName(SettingsPath);
                if (dir != null)
                    Directory.CreateDirectory(dir);

                string json = JsonSerializer.Serialize(settings, JsonOptions);
                File.WriteAllText(SettingsPath, json);
            }
            catch
            {
                // Best-effort — don't crash the palette if we can't write
            }
        }
    }
}
