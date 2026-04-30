namespace C3DTools.Core.PipeContracts
{
    /// <summary>
    /// Message sent from CLI to plugin over named pipe.
    /// </summary>
    public record PipeRequest(string Command, Dictionary<string, string> Args);
}
