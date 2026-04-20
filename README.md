# C3DTools

A Civil 3D / AutoCAD plugin (.NET 8) for hydrology and basin analysis. Uses [NetTopologySuite](https://github.com/NetTopologySuite/NetTopologySuite) for all polygon boolean operations.

---

## Commands

### Basin Tagging

#### `TAGBASIN` — Tag a basin polyline

Attaches a basin ID to a closed polyline as XData under the `C3DTools_Basin` RegApp.

```
Command: TAGBASIN
Select basin polyline to tag:
Enter basin ID: B-101
Polyline tagged with basin ID: B-101
```

---

#### `GETBASIN` — Read basin attributes

Reads and prints the basin ID and, if the polyline is a split piece, the split classification.

```
Command: GETBASIN
Select basin polyline to retrieve ID:

Basin ID: B-101
Split Classification: ONSITE
Parent Basin ID: B-101
```

For an unclassified parent polyline only `Basin ID` is printed.

---

#### `LABELBASIN` — Place a basin ID label

Places a `DBText` entity at a user-picked point using the basin ID from a tagged polyline. Text height inherits `db.Textsize`; layer inherits the polyline layer.

```
Command: LABELBASIN
Select tagged basin polyline:
Pick point for label:
Basin label 'B-101' placed at (1234.00, 5678.00, 0.00)
```

---

### Basin Splitting

#### `SPLITBASINS` — Split basins against a site boundary

Automatically finds every `C3DTools_Basin`-tagged closed polyline in the drawing, prompts for a site boundary, then generates onsite (intersection) and offsite (difference) result polylines. The command is **idempotent** — existing split results are erased before regenerating.

**Workflow:**
1. Scans model space for all `C3DTools_Basin`-tagged closed polylines.
2. Prompts for a site boundary polyline (must be closed).
3. Erases any polylines already carrying `C3DTools_BasinSplit` XData.
4. For each tagged basin:
   - Computes `basin ∩ site` → **onsite** pieces placed on `CALC-BASN-ONSITE` (green).
   - Computes `basin − site` → **offsite** pieces placed on `CALC-BASN-OFFSITE` (red).
   - Each result polyline is tagged with both `C3DTools_Basin` (parent ID) and `C3DTools_BasinSplit` (parent ID + classification).
5. Reports totals to the command line.

```
Command: SPLITBASINS
Found 4 tagged basin(s).
Select site boundary polyline:
Erased 8 existing split basin polyline(s).
SPLITBASINS complete: 4 basin(s) processed, 4 onsite piece(s) created, 3 offsite piece(s) created.
```

**Output layers** (created automatically if absent):

| Layer | Color | Contents |
|---|---|---|
| `CALC-BASN-ONSITE` | 3 (green) | Basin area inside the site boundary |
| `CALC-BASN-OFFSITE` | 1 (red) | Basin area outside the site boundary |

---

### Analysis

#### `BASINLANDUSE` — Basin × landuse hatch area analysis

Automatically collects all basin-tagged polylines from model space, prompts for a hatch selection, then computes the intersection area of each basin against each hatch layer.

**Workflow:**
1. Scans model space for all `C3DTools_Basin`-tagged polylines (parents and split pieces).
2. Prompts for hatch selection.
3. For each invalid hatch, offers an inline fix-or-skip prompt.
4. Computes intersection area per (basin, hatch layer) pair.
5. Aggregates multiple geometry fragments sharing the same basin ID and classification into one row.
6. Displays results in a resizable dialog. Includes a **Copy to Clipboard** button (tab-separated, paste directly into Excel).

**Output table** (Classification column appears only when split pieces are present):

```
id        Classification    C-IMPERVIOUS    C-OPNSP    Total
--------  --------------    ------------    -------    -------
B-101     (parent)            1.2500         0.8000     2.0500
B-101     ONSITE              0.7500         0.5000     1.2500
B-101     OFFSITE             0.5000         0.3000     0.8000

B-102     (parent)            2.1000         1.4000     3.5000
B-102     ONSITE              1.3000         0.9000     2.2000
B-102     OFFSITE             0.8000         0.5000     1.3000
```

- **`(parent)`** — the original whole-basin polyline; unambiguously not ONSITE or OFFSITE.
- Rows are grouped by basin ID with a blank line between basins.
- Basins without split pieces produce a single row (no Classification column).

---

### Boolean Operations

| Command | Description |
|---|---|
| `PLUNION` | Unions two or more selected polylines. Originals are deleted; result polyline(s) are created. |
| `PLINT` | Intersects two or more selected polylines. Originals are preserved. |
| `PLDIFF` | Subtracts selected polylines from a base polyline. |

All three commands optionally prompt to delete the input polylines and report the number of result geometries created.

---

### Geometry Repair

| Command | Description |
|---|---|
| `FIXHATCH` | Detects and repairs topologically invalid hatches (self-intersections, mis-wound rings, holes outside shell). Recreates the hatch using the `AppendLoop` / `EvaluateHatch` pattern. Properties preserved: layer, lineweight, pattern name/scale/angle. |
| `FIXPOLY` | Detects and repairs invalid polyline geometries (bowties, self-intersections, duplicate vertices). May split one polyline into multiple result polylines. |

---

## XData Schema

Both RegApp names are registered automatically on first write.

### `C3DTools_Basin`

Written by `TAGBASIN`. Also stamped on every split result by `SPLITBASINS` so `GETBASIN` works on any basin piece.

| Slot | DxfCode | Value |
|---|---|---|
| 0 | `ExtendedDataRegAppName` | `"C3DTools_Basin"` |
| 1 | `ExtendedDataAsciiString` | `"<basinId>"` |

### `C3DTools_BasinSplit`

Written by `SPLITBASINS` on generated split pieces.

| Slot | DxfCode | Value |
|---|---|---|
| 0 | `ExtendedDataRegAppName` | `"C3DTools_BasinSplit"` |
| 1 | `ExtendedDataAsciiString` | `"<basinId>"` |
| 2 | `ExtendedDataAsciiString` | `"ONSITE"` or `"OFFSITE"` |

---

## Arc Handling

All geometry conversion is arc-accurate.

| Source | Arc handling |
|---|---|
| `Polyline` (bulge segments) | Tessellated via the bulge formula — `bulge = tan(θ/4)` — into 16 segments per arc |
| Hatch polyline loop (`BulgeVertexCollection`) | Same bulge tessellation, 16 segments |
| Hatch curve loop `CircularArc2d` | `GetSamplePoints(17)` — AutoCAD handles traversal direction |
| Hatch curve loop `EllipticalArc2d` | `GetSamplePoints(17)` |
| Spline | Silently skipped |

---

## Project Structure

```
C3DTools/
├── Commands/
│   ├── BasinCommands.cs          # TAGBASIN, GETBASIN, LABELBASIN
│   ├── SplitBasinsCommand.cs     # SPLITBASINS
│   ├── BasinLanduseCommand.cs    # BASINLANDUSE
│   ├── UnionCommand.cs           # PLUNION
│   ├── IntersectionCommand.cs    # PLINT
│   ├── DifferenceCommand.cs      # PLDIFF
│   ├── FixPolylineCommand.cs     # FIXPOLY
│   └── FixHatchCommand.cs        # FIXHATCH
├── Helpers/
│   ├── BooleanOperationHelper.cs # Shared NTS Intersect / Difference wrappers
│   ├── GeometryConverter.cs      # Polyline / Hatch -> NTS (arc-accurate)
│   ├── GeometryHelper.cs         # Polyline creation from NTS results
│   ├── GeometryValidator.cs      # Pre-operation geometry validation & repair
│   ├── HatchFixer.cs             # Hatch repair logic
│   └── PolylineHelper.cs         # Selection filter helpers
└── UI/
    └── BasinLanduseResultForm.cs # Results dialog for BASINLANDUSE
```

---

## Requirements

- AutoCAD Civil 3D 2024 / 2025
- .NET 8
- [NetTopologySuite](https://www.nuget.org/packages/NetTopologySuite) (bundled via NuGet)

## Building & Loading

1. Open `C3DTools.sln` in Visual Studio 2022 or later and build.
2. In AutoCAD type `NETLOAD` and browse to the output `C3DTools.dll`.
3. All commands listed above are immediately available for the session.

To load automatically on every session, add the `NETLOAD` call to your `acad.lsp` or the AutoCAD Startup Suite.
