namespace C3DTools.Models
{
    /// <summary>
    /// Defines a named clipping mask (a closed polyline in the drawing) used to
    /// spatially split tagged basins into inside / outside regions.
    /// </summary>
    public class MaskDefinition
    {
        /// <summary>
        /// User-supplied name, e.g. "SITE_BNDY". Used to suffix layer names.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// AutoCAD object handle for the mask polyline. Handles survive
        /// save/reload, unlike ObjectIds.
        /// </summary>
        public string PolylineHandle { get; set; } = string.Empty;

        /// <summary>
        /// Label stamped on geometry that falls INSIDE the mask.
        /// e.g. "IN". Results in layer: {LayerPrefix}-{Name}_IN
        /// </summary>
        public string InsideLabel { get; set; } = "IN";

        /// <summary>
        /// Label stamped on geometry that falls OUTSIDE the mask.
        /// e.g. "OUT". Results in layer: {LayerPrefix}-{Name}_OUT
        /// </summary>
        public string OutsideLabel { get; set; } = "OUT";

        /// <summary>
        /// Layer name prefix for generated geometry, e.g. "CALC-BASN".
        /// Final inside layer  = "{LayerPrefix}-{Name}_{InsideLabel}"
        /// Final outside layer = "{LayerPrefix}-{Name}_{OutsideLabel}"
        /// </summary>
        public string LayerPrefix { get; set; } = "CALC-BASN";

        /// <summary>
        /// When true, running SPLITBASINS will generate physical split polylines
        /// for this mask in the drawing.
        /// </summary>
        public bool GenerateGeometry { get; set; } = false;

        // ── Derived helpers ──────────────────────────────────────────────────────

        public string InsideLayerName  => $"{LayerPrefix}-{Name}_{InsideLabel}";
        public string OutsideLayerName => $"{LayerPrefix}-{Name}_{OutsideLabel}";
    }
}
