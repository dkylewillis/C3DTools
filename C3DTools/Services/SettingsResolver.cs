using Autodesk.AutoCAD.DatabaseServices;
using C3DTools.Models;

namespace C3DTools.Services
{
    /// <summary>
    /// Resolves the effective BasinSettings in priority order:
    ///   1. In-memory cache (live palette UI state)
    ///   2. Per-drawing NOD XRecord
    ///   3. Global %APPDATA% defaults
    /// </summary>
    public class SettingsResolver
    {
        private readonly GlobalSettingsService _global = new GlobalSettingsService();
        private readonly DrawingSettingsService _drawing = new DrawingSettingsService();

        public BasinSettings Resolve(Database db)
        {
            return SettingsCache.Get()
                ?? _drawing.Load(db)
                ?? _global.Load();
        }
    }
}
