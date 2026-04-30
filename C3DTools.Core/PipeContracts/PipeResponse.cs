namespace C3DTools.Core.PipeContracts
{
    /// <summary>
    /// Message returned from plugin to CLI over named pipe.
    /// </summary>
    public record PipeResponse(bool Success, string? Data, string? Error);
}
