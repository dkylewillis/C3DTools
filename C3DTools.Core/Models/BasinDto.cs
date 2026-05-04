namespace C3DTools.Core.Models
{
    /// <summary>
    /// Lightweight basin data transfer object for pipe communication.
    /// Does not reference AutoCAD types.
    /// </summary>
    public class BasinDto
    {
        public string? BasinId { get; set; }
        public string? Layer { get; set; }
        public string Development { get; set; } = string.Empty;
        public bool IsTagged => !string.IsNullOrWhiteSpace(BasinId);

        public string DisplayText
        {
            get
            {
                if (IsTagged)
                {
                    var parts = new List<string>();
                    if (!string.IsNullOrEmpty(Development)) parts.Add(Development);
                    parts.Add(BasinId!);
                    return string.Join(" ", parts);
                }
                return "[Untagged]";
            }
        }
    }
}
