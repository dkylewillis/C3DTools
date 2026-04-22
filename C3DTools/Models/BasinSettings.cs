using System.Collections.Generic;

namespace C3DTools.Models
{
    public enum AreaUnit
    {
        SquareFeet,
        Acres
    }

    /// <summary>
    /// User-configurable settings for Basin commands.
    /// </summary>
    public class BasinSettings
    {
        /// <summary>
        /// Layer name patterns (supports wildcards * and ?) used to auto-collect
        /// hatches in the BASINLANDUSE command. E.g. "LU-*", "*HATCH*".
        /// </summary>
        public List<string> LanduseHatchLayers { get; set; } = new List<string>();

        /// <summary>
        /// Layer name for onsite split basin polylines created by SPLITBASINS.
        /// </summary>
        public string OnsiteLayer { get; set; } = "CALC-BASN-ONSITE";

        /// <summary>
        /// Layer name for offsite split basin polylines created by SPLITBASINS.
        /// </summary>
        public string OffsiteLayer { get; set; } = "CALC-BASN-OFFSITE";

        /// <summary>
        /// Unit to display area values in the BASINLANDUSE output.
        /// Drawing units are assumed to be square feet.
        /// </summary>
        public AreaUnit AreaUnit { get; set; } = AreaUnit.SquareFeet;

        /// <summary>
        /// Returns a new instance with hardcoded default values.
        /// </summary>
        public static BasinSettings CreateDefaults() => new BasinSettings
        {
            LanduseHatchLayers = new List<string>(),
            OnsiteLayer = "CALC-BASN-ONSITE",
            OffsiteLayer = "CALC-BASN-OFFSITE",
            AreaUnit = AreaUnit.SquareFeet
        };
    }
}
