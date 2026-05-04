namespace C3DTools.Infrastructure
{
    /// <summary>
    /// Lightweight static handoff used to pass a picked polyline handle from the
    /// MASKPICK CAD command back to <see cref="ViewModels.MasksPaletteViewModel"/>.
    /// </summary>
    internal static class MaskPickState
    {
        /// <summary>
        /// Set by <c>MASKPICK</c> after the user picks a polyline.
        /// Null means no pick has occurred (or it was already consumed).
        /// </summary>
        public static string? PendingHandle { get; set; }
    }
}
