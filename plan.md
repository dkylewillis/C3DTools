# C3DTools — CLI Refactor Plan
**AI Tool Integration via Standalone CLI**
Kyle Willis, P.E. | Integrated Science & Engineering

---

## 1. Objective

Extend the existing C3DTools Visual Studio solution to support AI agent integration. The AI agent (Claude or similar) will drive Civil 3D operations by invoking a standalone CLI executable that communicates with the loaded AutoCAD plugin over a named pipe.

This refactor does not rewrite existing functionality. It reorganizes boundaries, extracts shared code, and adds two new components alongside the existing plugin.

---

## 2. Current State

The solution currently consists of a single project:

| Folder | Type | Contents |
|---|---|---|
| `Commands/` | AutoCAD commands | `[CommandMethod]` registered AutoCAD commands |
| `Helpers/` | Utilities | Shared helper logic, extensions |
| `Models/` | Data models | Basin, CN, and other domain models |
| `Services/` | Business logic | Core calculation and query services |
| `UI/` | WPF views | Basin Palette and other dockable panels |
| `ViewModels/` | MVVM | View models for WPF panels |

> **Key constraint:** The AutoCAD .NET API is in-process only. No code outside the `acad.exe` process can call Civil 3D API methods directly. All live drawing access must go through the plugin.

---

## 3. Target Architecture

The refactored solution will contain three projects:

| Project | Output | Role |
|---|---|---|
| `C3DTools` (existing) | `.dll` | AutoCAD plugin. Loaded by acad.exe. Owns all Civil 3D API calls. Adds named pipe listener. |
| `C3DTools.Core` (new) | `.dll` | Class library. Shared models, DTOs, pipe message contracts. No AutoCAD references. |
| `C3DTools.CLI` (new) | `.exe` | Standalone Console App. AI agent invokes this. Communicates with plugin via named pipe when AutoCAD is open. |

### 3.1 Communication Flow

When AutoCAD is open and the plugin is loaded:

```
AI Agent
  ↓  runs command (args via stdin or CLI args)
C3DTools.CLI.exe
  ↓  named pipe message (JSON)
C3DTools.Plugin.dll  (inside acad.exe)
  ↓  AutoCAD / Civil 3D .NET API
Drawing / Database
```

When AutoCAD is not open, the CLI can still handle file-based operations independently (parsing exports, generating reports, building `.scr` scripts).

---

## 4. Refactor Steps

### Step 1 — Create C3DTools.Core

Add a new Class Library project to the solution targeting the same .NET version as the CLI (not the AutoCAD-constrained version).

- Right-click solution → Add → New Project → Class Library
- Name: `C3DTools.Core`
- Move the following from `C3DTools` into Core:
  - `Models/` folder contents (`Basin`, `CurveNumber`, etc.)
  - Any `Helpers/` that have no dependency on AutoCAD assemblies
- Create a `PipeContracts/` folder in Core for named pipe message types:

```csharp
// C3DTools.Core/PipeContracts/PipeRequest.cs
public record PipeRequest(string Command, Dictionary<string, string> Args);

// C3DTools.Core/PipeContracts/PipeResponse.cs
public record PipeResponse(bool Success, string? Data, string? Error);
```

---

### Step 2 — Add Named Pipe Listener to Plugin

Add a `PipeListener.cs` to the existing C3DTools plugin project. This starts a background thread on plugin load and waits for CLI requests.

- Add project reference: `C3DTools` → `C3DTools.Core`
- Create `Infrastructure/PipeListener.cs`:

```csharp
// Startup in IExtensionApplication.Initialize()
Task.Run(() => PipeListener.Start(CancellationToken));

// PipeListener loop
using var server = new NamedPipeServerStream("c3dtools", PipeDirection.InOut);
await server.WaitForConnectionAsync(token);
var request = await JsonSerializer.DeserializeAsync<PipeRequest>(server);
var response = CommandRouter.Handle(request);
await JsonSerializer.SerializeAsync(server, response);
```

- Create `Infrastructure/CommandRouter.cs` that dispatches pipe requests to existing Services:

```csharp
"get-cn"     => BasinService.GetCurveNumber(args["basin"]),
"list-basins" => BasinService.ListBasins(),
"update-cn"  => BasinService.UpdateCN(args["basin"], args["cn"]),
```

---

### Step 3 — Create C3DTools.CLI

Add a new Console App project to the solution.

- Right-click solution → Add → New Project → Console App (.NET 8)
- Name: `C3DTools.CLI`
- Add project reference: `C3DTools.CLI` → `C3DTools.Core`
- Add NuGet package: `System.CommandLine`
- Create `Infrastructure/PipeClient.cs`:

```csharp
using var client = new NamedPipeClientStream(".", "c3dtools", PipeDirection.InOut);
await client.ConnectAsync(timeout: 3000);
await JsonSerializer.SerializeAsync(client, request);
return await JsonSerializer.DeserializeAsync<PipeResponse>(client);
```

- Create `Commands/` folder with one file per CLI verb:

```
Commands/
  ListBasins.cs     →  ISETools.exe list-basins
  GetCN.cs          →  ISETools.exe get-cn --basin "Basin_A"
  UpdateCN.cs       →  ISETools.exe update-cn --basin "Basin_A" --cn 75
  ExportReport.cs   →  ISETools.exe export-report --output report.json
```

---

### Step 4 — Wire Program.cs

Use `System.CommandLine` to register all commands:

```csharp
var root = new RootCommand("C3DTools CLI — AI interface for Civil 3D");
root.AddCommand(ListBasinsCommand.Build());
root.AddCommand(GetCNCommand.Build());
root.AddCommand(UpdateCNCommand.Build());
return await root.InvokeAsync(args);
```

---

## 5. Output Contract

All CLI commands must follow this contract so the AI agent can parse responses consistently:

- **stdout:** JSON result only. No status messages, no labels, no prose.
- **stderr:** Error messages, warnings, diagnostic output.
- **Exit code 0:** success. **Exit code 1:** error.

Example output from `get-cn`:

```json
{
  "basin": "Basin_A",
  "cn": 75,
  "area_ac": 4.2,
  "cover_type": "Residential 1/4 ac"
}
```

---

## 6. What NOT to Change

This refactor is additive. The following must remain untouched:

- All existing `[CommandMethod]` classes in `Commands/`
- `BasinPalette` WPF UI and its ViewModel
- All existing `Services/` logic — the CLI routes to these, not around them
- AutoCAD assembly references and plugin load/unload lifecycle

> The CLI does not replace AutoCAD commands. It adds a parallel interface for AI agents. Engineers still use the plugin normally inside AutoCAD.

---

## 7. Summary of File Changes

| Action | File / Location | Notes |
|---|---|---|
| MOVE | `C3DTools/Models/*` → `C3DTools.Core/Models/` | No code changes, just relocation |
| MOVE | `C3DTools/Helpers/*` (safe ones) → Core | Only helpers with no AutoCAD refs |
| ADD | `C3DTools.Core/PipeContracts/` | Shared message types for pipe IPC |
| ADD | `C3DTools/Infrastructure/PipeListener.cs` | Named pipe server, background thread |
| ADD | `C3DTools/Infrastructure/CommandRouter.cs` | Dispatches pipe requests to Services |
| ADD | `C3DTools.CLI/` (new project) | Console App, entire new project |
| ADD | `C3DTools.CLI/Commands/*.cs` | One file per CLI verb |
| ADD | `C3DTools.CLI/Infrastructure/PipeClient.cs` | Named pipe client to talk to plugin |
| MODIFY | `C3DTools/C3DTools.csproj` | Add project ref to Core |
| MODIFY | `C3DTools` `IExtensionApplication.Initialize()` | Start PipeListener on plugin load |

---

## 8. How the AI Agent Uses the CLI

Once built, the AI agent is told about available commands in its system prompt or tool definitions. No MCP protocol is required — the agent runs commands as subprocesses and reads stdout.

Example system prompt snippet for the agent:

> You have access to `C3DTools.CLI.exe`. Use it to query and modify the active Civil 3D drawing. Always parse stdout as JSON. Available commands: `list-basins`, `get-cn --basin <name>`, `update-cn --basin <name> --cn <value>`, `export-report --output <path>`.

The agent decides which command to run, executes it, reads the JSON result, and incorporates it into its next reasoning step. No additional framework needed.
