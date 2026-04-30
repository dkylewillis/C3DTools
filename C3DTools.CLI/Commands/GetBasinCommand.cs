using C3DTools.CLI.Infrastructure;
using C3DTools.Core.PipeContracts;
using System.CommandLine;

namespace C3DTools.CLI.Commands
{
    public static class GetBasinCommand
    {
        public static Command Build()
        {
            var basinOption = new Option<string>(
                name: "--basin",
                description: "Basin ID to retrieve.")
            { IsRequired = true };

            var cmd = new Command("get-basin", "Get details for a specific basin by ID.")
            {
                basinOption
            };

            cmd.SetHandler(async (string basin) =>
            {
                var request = new PipeRequest("get-basin", new Dictionary<string, string>
                {
                    ["basin"] = basin
                });

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
            }, basinOption);

            return cmd;
        }
    }
}
