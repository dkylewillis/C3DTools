using C3DTools.Core.PipeContracts;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;

namespace C3DTools.Infrastructure
{
    /// <summary>
    /// Hosts a named pipe server that accepts one connection at a time,
    /// reads a <see cref="PipeRequest"/>, dispatches it, and writes back
    /// a <see cref="PipeResponse"/>.  Runs on a long-lived background thread
    /// started by the plugin's IExtensionApplication.Initialize().
    /// </summary>
    public static class PipeListener
    {
        public const string PipeName = "c3dtools";

        public static async Task StartAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream(
                        PipeName,
                        PipeDirection.InOut,
                        maxNumberOfServerInstances: 1,
                        transmissionMode: PipeTransmissionMode.Byte,
                        options: PipeOptions.Asynchronous);

                    await server.WaitForConnectionAsync(token);

                    using var reader = new StreamReader(server, leaveOpen: true);
                    using var writer = new StreamWriter(server, leaveOpen: true) { AutoFlush = true };

                    var line = await reader.ReadLineAsync(token);
                    var request = line is null ? null : JsonSerializer.Deserialize<PipeRequest>(line);

                    PipeResponse response = request is null
                        ? new PipeResponse(false, null, "Empty or malformed request.")
                        : CommandRouter.Handle(request);

                    await writer.WriteLineAsync(JsonSerializer.Serialize(response));
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    // Keep the listener alive through transient errors.
                    await Task.Delay(500, token).ConfigureAwait(false);
                }
            }
        }
    }
}
