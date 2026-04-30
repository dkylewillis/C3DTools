using C3DTools.Core.PipeContracts;
using System.IO.Pipes;
using System.Text.Json;

namespace C3DTools.CLI.Infrastructure
{
    /// <summary>
    /// Sends a <see cref="PipeRequest"/> to the running plugin and returns the
    /// <see cref="PipeResponse"/>.  Throws <see cref="TimeoutException"/> if the
    /// plugin pipe is not available within <paramref name="timeoutMs"/>.
    /// </summary>
    public static class PipeClient
    {
        public const string PipeName = "c3dtools";

        public static async Task<PipeResponse> SendAsync(PipeRequest request, int timeoutMs = 5000)
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

            await client.ConnectAsync(timeoutMs);

            using var writer = new StreamWriter(client, leaveOpen: true) { AutoFlush = true };
            using var reader = new StreamReader(client, leaveOpen: true);

            await writer.WriteLineAsync(JsonSerializer.Serialize(request));

            var line = await reader.ReadLineAsync();
            var response = line is null ? null : JsonSerializer.Deserialize<PipeResponse>(line);
            return response ?? new PipeResponse(false, null, "Empty response from plugin.");
        }
    }
}
