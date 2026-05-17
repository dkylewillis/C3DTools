namespace C3DTools.Core.Models
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
        /// Unit to display area values in the BASINLANDUSE output.
        /// Drawing units are assumed to be square feet.
        /// </summary>
        public AreaUnit AreaUnit { get; set; } = AreaUnit.SquareFeet;

        public string StormDistribution { get; set; } = "ATLAS14_ALTERNATING_BLOCK";

        public int HydrographTimeStepMinutes { get; set; } = 2;

        /// <summary>
        /// Returns a new instance with hardcoded default values.
        /// </summary>
        public static BasinSettings CreateDefaults() => new BasinSettings
        {
            LanduseHatchLayers = new List<string>(),
            AreaUnit = AreaUnit.SquareFeet,
            StormDistribution = "ATLAS14_ALTERNATING_BLOCK",
            HydrographTimeStepMinutes = 2
        };
    }
}
