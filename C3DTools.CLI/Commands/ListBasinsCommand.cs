using C3DTools.CLI.Infrastructure;
using C3DTools.Core.PipeContracts;
using System.CommandLine;
using System.Text.Json;

namespace C3DTools.CLI.Commands
{
    public static class ListBasinsCommand
    {
        public static Command Build()
        {
            var cmd = new Command("list-basins", "List all basin polylines in the active drawing.");

            cmd.SetHandler(async () =>
            {
                var request = new PipeRequest("list-basins", new Dictionary<string, string>());
                await ExecuteAsync(request);
            });

            return cmd;
        }

        private static async Task ExecuteAsync(PipeRequest request)
        {
            try
            {
                var response = await PipeClient.SendAsync(request);
                if (response.Success)
                {
                    Console.WriteLine(response.Data);
                }
                else
                {
                    Console.Error.WriteLine(response.Error);
                    Environment.Exit(1);
                }
            }
            catch (TimeoutException)
            {
                Console.Error.WriteLine("Could not connect to C3DTools plugin. Is AutoCAD open with the plugin loaded?");
                Environment.Exit(1);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                Environment.Exit(1);
            }
        }
    }
}
