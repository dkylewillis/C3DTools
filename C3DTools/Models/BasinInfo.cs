using Autodesk.AutoCAD.DatabaseServices;

namespace C3DTools.Models
{
    /// <summary>
    /// Represents a basin polyline in the drawing.
    /// </summary>
    public class BasinInfo
    {
        public ObjectId ObjectId { get; set; }
        public string? BasinId { get; set; }
        public bool IsTagged => !string.IsNullOrWhiteSpace(BasinId);
        public string? Layer { get; set; }

        /// <summary>
        /// Boundary attribute: "Onsite", "Offsite", or empty string
        /// </summary>
        public string Boundary { get; set; } = string.Empty;

        /// <summary>
        /// Development stage: "Pre", "Post", or empty string
        /// </summary>
        public string Development { get; set; } = string.Empty;

        public string DisplayText
        {
            get
            {
                if (IsTagged)
                {
                    // Format: "Pre E Offsite" or "Post A" or just "B"
                    var parts = new List<string>();

                    if (!string.IsNullOrEmpty(Development))
                        parts.Add(Development);

                    parts.Add(BasinId!);

                    if (!string.IsNullOrEmpty(Boundary))
                        parts.Add(Boundary);

                    return string.Join(" ", parts);
                }
                return $"[Untagged] {ObjectId.Handle}";
            }
        }
    }
}
