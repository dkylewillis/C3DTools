# C3DTools CLI

A standalone command-line interface for querying and modifying an active Civil 3D drawing via the C3DTools AutoCAD plugin.

The CLI communicates with the plugin over a named pipe (`c3dtools`). AutoCAD must be running with the C3DTools plugin loaded for live-drawing commands to work.

---

## Requirements

- Windows
- AutoCAD 2025 with the C3DTools plugin loaded (for live-drawing commands)
- .NET 8 runtime

---

## Usage

```
C3DTools.exe <command> [options]
```

---

## Commands

### `list-basins`

Lists all basin polylines in the active drawing.

```
C3DTools.exe list-basins
```

**Output (stdout):** JSON array of basin objects.

```json
[
  {
    "basinId": "A",
    "layer": "CALC-BASN-ONSITE",
    "boundary": "ONSITE",
    "development": "Post",
    "isTagged": true,
    "displayText": "Post A ONSITE"
  },
  {
    "basinId": null,
    "layer": "CALC-BASN-OFFSITE",
    "boundary": "",
    "development": "",
    "isTagged": false,
    "displayText": "[Untagged]"
  }
]
```

---

### `get-basin`

Gets details for a specific basin by its ID.

```
C3DTools.exe get-basin --basin <id>
```

| Option | Required | Description |
|--------|----------|-------------|
| `--basin` | Yes | Basin ID (e.g. `A`, `Basin_A`) |

**Output (stdout):** JSON object for the matched basin.

```json
{
  "basinId": "A",
  "layer": "CALC-BASN-ONSITE",
  "boundary": "ONSITE",
  "development": "Post",
  "isTagged": true,
  "displayText": "Post A ONSITE"
}
```

---

## Output Contract

All commands follow this contract so AI agents and scripts can parse responses consistently:

| Stream | Content |
|--------|---------|
| **stdout** | JSON result only — no labels, status messages, or prose |
| **stderr** | Error messages, warnings, diagnostic output |
| **Exit code 0** | Success |
| **Exit code 1** | Error |

---

## Error Cases

If AutoCAD is not open or the plugin is not loaded, the CLI will print to stderr and exit with code 1:

```
Could not connect to C3DTools plugin. Is AutoCAD open with the plugin loaded?
```

If a basin ID is not found:

```
Basin 'X' not found.
```

---

## AI Agent Integration

The CLI is designed to be invoked as a subprocess by an AI agent. No MCP protocol is required — the agent runs commands and reads stdout as JSON.

Example system prompt snippet:

> You have access to `C3DTools.exe`. Use it to query and modify the active Civil 3D drawing.
> Always parse stdout as JSON. Write errors and diagnostics to stderr only.
>
> Available commands:
> - `C3DTools.exe list-basins`
> - `C3DTools.exe get-basin --basin <id>`

---

## Architecture

```
AI Agent
  ↓  subprocess
C3DTools.exe  (this project)
  ↓  named pipe JSON  ("c3dtools")
C3DTools.dll  (AutoCAD plugin inside acad.exe)
  ↓  AutoCAD / Civil 3D .NET API
Active Drawing
```

See [`plan.md`](../plan.md) for the full architecture and refactor plan.
