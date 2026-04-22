using C3DTools.Models;

namespace C3DTools.Services
{
    /// <summary>
    /// In-memory cache of the current session settings.
    /// Written by BasinPaletteViewModel on every UI change;
    /// read by SettingsResolver as the highest-priority source.
    /// </summary>
    public static class SettingsCache
    {
        private static BasinSettings? _current;

        /// <summary>
        /// Stores settings from the active palette session.
        /// </summary>
        public static void Set(BasinSettings settings)
        {
            _current = settings;
        }

        /// <summary>
        /// Returns the cached settings, or null if the palette has not been opened this session.
        /// </summary>
        public static BasinSettings? Get() => _current;

        /// <summary>
        /// Clears the cache (e.g. when a new document is activated and settings are reloaded).
        /// </summary>
        public static void Clear() => _current = null;
    }
}
