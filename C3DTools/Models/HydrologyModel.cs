using System.Text.Json.Serialization;

namespace C3DTools.Models
{
    public class HydrologyModel
    {
        [JsonPropertyName("project")]
        public HydrologyProject Project { get; set; } = new HydrologyProject();

        [JsonPropertyName("storms")]
        public List<HydrologyStorm> Storms { get; set; } = new List<HydrologyStorm>();

        [JsonPropertyName("basins")]
        public List<HydrologyBasin> Basins { get; set; } = new List<HydrologyBasin>();

        [JsonPropertyName("hydrographs")]
        public List<HydrologyHydrograph> Hydrographs { get; set; } = new List<HydrologyHydrograph>();

        [JsonPropertyName("nodes")]
        public List<HydrologyNode> Nodes { get; set; } = new List<HydrologyNode>();

        [JsonPropertyName("links")]
        public List<HydrologyLink> Links { get; set; } = new List<HydrologyLink>();
    }

    public class HydrologyProject
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("drawing_path")]
        public string DrawingPath { get; set; } = string.Empty;

        [JsonPropertyName("exported_at_utc")]
        public DateTime ExportedAtUtc { get; set; } = DateTime.UtcNow;

        [JsonPropertyName("source")]
        public string Source { get; set; } = "C3DTools";
    }

    public class HydrologyStorm
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("rainfall_depth_inches")]
        public double? RainfallDepthInches { get; set; }
    }

    public class HydrologyBasin
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("condition")]
        public string Condition { get; set; } = string.Empty;

        [JsonPropertyName("boundary")]
        public string Boundary { get; set; } = string.Empty;

        [JsonPropertyName("development")]
        public string Development { get; set; } = string.Empty;

        [JsonPropertyName("layer")]
        public string Layer { get; set; } = string.Empty;

        [JsonPropertyName("handle")]
        public string Handle { get; set; } = string.Empty;

        [JsonPropertyName("area_acres")]
        public double AreaAcres { get; set; }

        [JsonPropertyName("curve_number")]
        public double? CurveNumber { get; set; }

        [JsonPropertyName("tc_minutes")]
        public double? TcMinutes { get; set; }

        [JsonPropertyName("outlet_node")]
        public string OutletNode { get; set; } = string.Empty;

        [JsonPropertyName("downstream_id")]
        public string DownstreamId { get; set; } = string.Empty;

        [JsonPropertyName("geometry")]
        public HydrologyGeometry? Geometry { get; set; }

        [JsonPropertyName("land_uses")]
        public List<HydrologyLandUseArea> LandUses { get; set; } = new List<HydrologyLandUseArea>();

        [JsonPropertyName("tc_segments")]
        public List<HydrologyTcSegment> TcSegments { get; set; } = new List<HydrologyTcSegment>();
    }

    public class HydrologyLandUseArea
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("layer")]
        public string Layer { get; set; } = string.Empty;

        [JsonPropertyName("area_acres")]
        public double AreaAcres { get; set; }

        [JsonPropertyName("curve_number")]
        public double? CurveNumber { get; set; }
    }

    public class HydrologyTcSegment
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("length_ft")]
        public double? LengthFeet { get; set; }

        [JsonPropertyName("slope")]
        public double? Slope { get; set; }

        [JsonPropertyName("manning_n")]
        public double? ManningN { get; set; }
    }

    public class HydrologyNode
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
    }

    public class HydrologyLink
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("type")]
        public string Type { get; set; } = "basin_route";

        [JsonPropertyName("from_basin")]
        public string FromBasin { get; set; } = string.Empty;

        [JsonPropertyName("to_basin")]
        public string ToBasin { get; set; } = string.Empty;

        [JsonPropertyName("from_node")]
        public string FromNode { get; set; } = string.Empty;

        [JsonPropertyName("to_node")]
        public string ToNode { get; set; } = string.Empty;
    }

    public class HydrologyHydrograph
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("type")]
        public string Type { get; set; } = "SCS";

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("basin_id")]
        public string BasinId { get; set; } = string.Empty;

        [JsonPropertyName("inflow_ids")]
        public List<string> InflowIds { get; set; } = new List<string>();

        [JsonPropertyName("downstream_id")]
        public string DownstreamId { get; set; } = string.Empty;

        [JsonPropertyName("parameters")]
        public Dictionary<string, object> Parameters { get; set; } = new Dictionary<string, object>();
    }

    public class HydrologyGeometry
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("coordinates")]
        public List<List<List<double>>> Coordinates { get; set; } = new List<List<List<double>>>();
    }

    public class HydrologyResultsFile
    {
        [JsonPropertyName("project")]
        public HydrologyResultsProject Project { get; set; } = new HydrologyResultsProject();

        [JsonPropertyName("summary")]
        public HydrologyResultsSummary Summary { get; set; } = new HydrologyResultsSummary();

        [JsonPropertyName("basins")]
        public List<HydrologyBasinResult> Basins { get; set; } = new List<HydrologyBasinResult>();

        [JsonPropertyName("hydrographs")]
        public List<HydrologyHydrographResult> Hydrographs { get; set; } = new List<HydrologyHydrographResult>();

        [JsonPropertyName("hydrograph_peak_flows")]
        public List<HydrologyHydrographPeakFlow> HydrographPeakFlows { get; set; } = new List<HydrologyHydrographPeakFlow>();

        [JsonPropertyName("validation")]
        public HydrologyValidationReport Validation { get; set; } = new HydrologyValidationReport();
    }

    public class HydrologyHydrographResult
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("basin_id")]
        public string BasinId { get; set; } = string.Empty;

        [JsonPropertyName("inflow_ids")]
        public List<string> InflowIds { get; set; } = new List<string>();

        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;
    }

    public class HydrologyHydrographPeakFlow
    {
        [JsonPropertyName("storm_id")]
        public string StormId { get; set; } = string.Empty;

        [JsonPropertyName("ari_years")]
        public int? AriYears { get; set; }

        [JsonPropertyName("rainfall_depth_inches")]
        public double? RainfallDepthInches { get; set; }

        [JsonPropertyName("duration_minutes")]
        public double? DurationMinutes { get; set; }

        [JsonPropertyName("hydrograph_id")]
        public string HydrographId { get; set; } = string.Empty;

        [JsonPropertyName("hydrograph_type")]
        public string HydrographType { get; set; } = string.Empty;

        [JsonPropertyName("basin_id")]
        public string BasinId { get; set; } = string.Empty;

        [JsonPropertyName("inflow_ids")]
        public List<string> InflowIds { get; set; } = new List<string>();

        [JsonPropertyName("peak_flow_cfs")]
        public double? PeakFlowCfs { get; set; }

        [JsonPropertyName("time_to_peak_minutes")]
        public double? TimeToPeakMinutes { get; set; }

        [JsonPropertyName("volume_cuft")]
        public double? VolumeCuft { get; set; }

        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;
    }

    public class HydrologyResultsProject
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("source")]
        public string Source { get; set; } = string.Empty;
    }

    public class HydrologyResultsSummary
    {
        [JsonPropertyName("basin_count")]
        public int BasinCount { get; set; }

        [JsonPropertyName("total_area_acres")]
        public double TotalAreaAcres { get; set; }

        [JsonPropertyName("error_count")]
        public int ErrorCount { get; set; }

        [JsonPropertyName("warning_count")]
        public int WarningCount { get; set; }
    }

    public class HydrologyBasinResult
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("handle")]
        public string Handle { get; set; } = string.Empty;

        [JsonPropertyName("condition")]
        public string Condition { get; set; } = string.Empty;

        [JsonPropertyName("area_acres")]
        public double AreaAcres { get; set; }

        [JsonPropertyName("curve_number")]
        public double? CurveNumber { get; set; }

        [JsonPropertyName("tc_minutes")]
        public double? TcMinutes { get; set; }

        [JsonPropertyName("impervious_percent")]
        public double? ImperviousPercent { get; set; }

        [JsonPropertyName("land_use_count")]
        public int LandUseCount { get; set; }

        [JsonPropertyName("downstream_id")]
        public string DownstreamId { get; set; } = string.Empty;

        [JsonPropertyName("upstream_ids")]
        public List<string> UpstreamIds { get; set; } = new List<string>();

        [JsonPropertyName("local_peak_cfs")]
        public double? LocalPeakCfs { get; set; }

        [JsonPropertyName("routed_peak_cfs")]
        public double? RoutedPeakCfs { get; set; }

        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;
    }

    public class HydrologyValidationReport
    {
        [JsonPropertyName("errors")]
        public List<string> Errors { get; set; } = new List<string>();

        [JsonPropertyName("warnings")]
        public List<string> Warnings { get; set; } = new List<string>();

        [JsonPropertyName("info")]
        public List<string> Info { get; set; } = new List<string>();
    }
}