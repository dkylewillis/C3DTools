using C3DTools.CLI.Commands;
using System.CommandLine;

var root = new RootCommand("C3DTools CLI — AI interface for Civil 3D");
root.AddCommand(ListBasinsCommand.Build());
root.AddCommand(GetBasinCommand.Build());

return await root.InvokeAsync(args);

