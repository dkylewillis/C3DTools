# C3DTools - Polyline & Hatch Tools for AutoCAD

A collection of geometry processing tools for AutoCAD polylines and hatches using NetTopologySuite for topologically correct operations. Includes an XData-based tagging system, hatch overlap analysis, and geometry repair tools for land development workflows.

## Overview

C3DTools provides commands for:
- **Tagging polylines** with custom IDs (XData-based)
- **Analyzing hatch overlaps** with tagged polylines
- **Fixing invalid hatch geometries** (self-intersections, bowties, mis-wound rings, arcs-as-holes)
- **Fixing invalid polyline geometries** (self-intersections, bowties)
- **Boolean operations** on polylines (Union, Difference, Intersection)

These tools are particularly useful for Civil 3D workflows, GIS data preparation, parcel analysis, and ensuring geometrically valid polygons.

---

## Commands

### Tag Commands

#### TAGID - Tag Polyline with ID

Attach a custom string ID to a polyline using XData (app name: `C3DTools_ID`).

**Usage:**
1. Type `TAGID` in the command line
2. Select a polyline to tag
3. Enter a string ID (e.g., `LOT-001`, `PARCEL-A`)
4. ID is stored as XData on the polyline

**Example:**
```
Command: TAGID
Select polyline to tag:
Enter ID string: LOT-001
Polyline tagged with ID: LOT-001
```

---

#### GETID - Retrieve Polyline ID

Display the stored ID tag from a polyline.

**Usage:**
1. Type `GETID` in the command line
2. Select a tagged polyline
3. ID is displayed in the command line

**Example:**
```
Command: GETID
Select polyline to retrieve ID:
Polyline ID: LOT-001
```

---

#### LABELID - Place ID Label

Place a text label showing the polyline's ID at a specified point.

**Usage:**
1. Type `LABELID` in the command line
2. Select a tagged polyline
3. Pick a point for label placement
4. A `DBText` entity is created with the ID, inheriting the polyline's layer

**Properties:**
- Text height: `db.Textsize`
- Layer: Same as polyline
- Position: User-specified point

**Example:**
```
Command: LABELID
Select tagged polyline:
Pick point for label:
Label 'LOT-001' placed at (100.0, 200.0, 0.0)
```

---

### HATCHOVERLAP - Hatch Overlap Analysis

Compute and report overlap areas between tagged polylines and hatches.

**Usage:**
1. Tag polylines with `TAGID` first
2. Type `HATCHOVERLAP` in the command line
3. Select polylines with ID tags
4. Select hatches
5. Overlap areas are computed and reported for each polyline-hatch pair

**What it does:**
- Converts polylines to NTS polygons
- Converts hatches to NTS polygons using area-based ring classification (see Technical Details)
- Computes intersection area using `Geometry.Intersection()`
- Reports results to the command line

**Example output:**
```
Command: HATCHOVERLAP
Select polylines with ID tags:
Select hatches:

--- Overlap Analysis Results ---
Polyline ID: LOT-001
  Hatch 1: Overlap Area = 1234.5678
  Hatch 2: No overlap
Polyline ID: LOT-002
  Hatch 1: Overlap Area = 567.8901
  Hatch 2: Overlap Area = 890.1234
--- End of Analysis ---
```

**Notes:**
- Polylines without ID tags are skipped
- Invalid geometries are skipped with a warning
- Errors are reported to the command line

---

### FIXHATCH - Fix Invalid Hatch Geometries

Detects and repairs topologically invalid hatch geometries by extracting loop coordinates, fixing the geometry with NetTopologySuite, and recreating clean hatches using proper AutoCAD boundary objects.

**Usage:**
1. Type `FIXHATCH` in the command line
2. Select one or more hatches to fix
3. Invalid hatches are repaired and replaced; valid hatches are skipped untouched

**What it fixes:**
- Self-intersecting hatch boundaries (bowties)
- Holes mis-classified due to CW/CCW winding inconsistencies
- Holes reported outside their shell (often caused by reversed arc segments)
- Collapsed or degenerate geometry
- Other NTS topology validation errors

**How it works (pipeline):**
1. Extract all hatch loop coordinates using `HatchToNts()` (area-based ring classification — no winding direction assumptions)
2. Validate with NTS `IsValidOp`; skip if already valid
3. Repair with `GeometryFixer.Fix()`; fall back to `FixHolesOutsideShell()` for `HoleOutsideShell` errors
4. Create temporary closed `Polyline` entities from the fixed geometry rings
5. Build new `Hatch` objects using `AppendLoop(HatchLoopTypes.Outermost, objectIdCollection)` + `EvaluateHatch()` — the correct Autodesk boundary-object pattern
6. Set `Associative = false` so the hatch owns its loop data
7. Erase temporary boundary polylines (no drawing clutter)
8. Erase the original hatch only after replacement succeeds

**Properties preserved:**
- Layer
- Line weight
- Hatch pattern name, scale, and angle
- Pattern type

**Example output:**
```
Command: FIXHATCH
Select hatches to fix:

Hatch 1A2B: Self-intersection
  Created replacement hatch (area: 1234.5678)
  Replaced with 1 hatch(es).
Hatch 3C4D: already valid — skipped.
Hatch 5E6F: Hole lies outside shell
  Applied fallback: re-classified mis-wound rings.
  Created replacement hatch (area: n/a)
  Replaced with 2 hatch(es).

FIXHATCH complete: 2 fixed, 1 already valid, 0 error(s).
```

---

### FIXPLINE - Fix Invalid Polyline Geometries

Fixes self-intersecting, bowtie, and other invalid polyline geometries.

**Usage:**
1. Type `FIXPLINE` in the command line
2. Select one or more polylines to fix
3. Invalid geometries are automatically corrected and replaced

**What it fixes:**
- Self-intersecting polygons (bowties)
- Invalid ring orientation
- Duplicate vertices
- Collapsed geometries
- Other NTS topology validation errors

**Behavior:**
- Valid polylines are skipped (no changes)
- Fixed polylines replace the originals
- Bowties may be split into multiple polylines

**Example output:**
```
Command: FIXPLINE
Select polylines to fix:
  Issue: Self-intersection at [100.0, 200.0]
  Split into 2 geometries

FIXPLINE complete: 1 fixed, 0 skipped.
```

---

### PLUNION - Union Polylines

Combines multiple polylines into a single geometry (or multiple disjoint geometries).

**Usage:**
1. Type `PLUNION` in the command line
2. Select two or more polylines
3. Result polyline(s) are created; originals are deleted

**Example output:**
```
Command: PLUNION
Select polylines to union:

PLUNION complete: 3 polylines → 1 result(s).
```

---

### PLDIFF - Difference (Subtract Polylines)

Subtracts one or more polylines from a base polyline.

**Usage:**
1. Type `PLDIFF` in the command line
2. Select the base polyline (what you're cutting from)
3. Select polyline(s) to subtract
4. Result polyline(s) are created; base is deleted

**Example output:**
```
Command: PLDIFF
Select base polyline:
Select polylines to subtract:

PLDIFF complete: 1 result(s).
```

---

### PLINT - Intersection

Finds the overlapping area between two or more polylines.

**Usage:**
1. Type `PLINT` in the command line
2. Select two or more polylines
3. Result polyline(s) representing the common area are created; originals remain unchanged

**Example output:**
```
Command: PLINT
Select polylines to intersect:

PLINT complete: 1 result(s).
```

---

## Installation

### Requirements
- AutoCAD 2025 or later (or Civil 3D 2025+)
- .NET 8.0 Runtime

### Steps
1. Download the latest `C3DTools.dll` from the releases
2. Copy `C3DTools.dll` and `NetTopologySuite.dll` to a folder
3. In AutoCAD, type `NETLOAD`
4. Browse to and select `C3DTools.dll`
5. All commands are now available

### Auto-load on Startup
Add to your `acad.lsp` or use the AutoCAD Startup Suite to automatically load C3DTools on every session.

---

## Technical Details

### XData Tag System
- **App Name**: `C3DTools_ID`
- **Storage**: Extended Data (XData) attached to polylines
- **Format**: `{(1001, "C3DTools_ID"), (1000, "LOT-001")}`
- **Access**: `entity.GetXDataForApplication("C3DTools_ID")`
- **Index**: ID string is at `ResultBuffer[1]`

### Hatch Conversion to NTS Geometry (`HatchToNts`)

Ring classification uses **area-based centroid containment**, not winding direction or `HatchLoopTypes` flags. Both are unreliable: AutoCAD does not enforce winding direction on hatch loop coordinates, and `HatchLoopTypes.External` / `Outermost` flags are inconsistently set by different authoring tools (Civil 3D, Dynamo, imports).

**Algorithm:**
1. Extract all loop rings without any pre-classification
2. Sort rings by area magnitude, largest first (the largest ring is always the outermost shell)
3. For each ring: if its centroid is contained by an already-accepted shell → it is a hole for that shell; otherwise it is promoted to a new shell
4. Build NTS `Polygon` (single shell) or `MultiPolygon` (multiple shells)

**Boundary types supported:**
| Type | Handling |
|---|---|
| `BulgeVertexCollection` (polyline loop) | Vertices extracted directly; bulges flattened to straight segments |
| `LineSegment2d` | Start point extracted |
| `CircularArc2d` | Tessellated using `GetSamplePoints(17)` — AutoCAD handles traversal direction |
| `EllipticalArc2d` | Tessellated using `GetSamplePoints(17)` — AutoCAD handles traversal direction |
| Spline / other | Silently skipped |

> **Arc tessellation note:** `Curve2d.GetSamplePoints(n)` is used instead of manual angle math. It returns points in the curve's natural traversal direction regardless of `IsClockWise`, eliminating reversed-arc self-intersection bugs.

### Hatch Recreation Pattern

New hatches are built using the correct Autodesk boundary-object pattern:

```csharp
// 1. Create temporary closed polyline from fixed ring coordinates
Polyline boundary = RingToPolyline(polygon.ExteriorRing, layer, lineWeight);
btr.AppendEntity(boundary);
tr.AddNewlyCreatedDBObject(boundary, true);

// 2. Build hatch from boundary ObjectId
Hatch hatch = new Hatch();
btr.AppendEntity(hatch);
tr.AddNewlyCreatedDBObject(hatch, true);
hatch.SetHatchPattern(patternType, patternName);
hatch.Associative = false;  // set AFTER AppendEntity, BEFORE AppendLoop
hatch.AppendLoop(HatchLoopTypes.Outermost, new ObjectIdCollection { boundary.ObjectId });
hatch.EvaluateHatch(true);

// 3. Erase boundary — non-associative hatch owns its loop data after EvaluateHatch
boundary.UpgradeOpen();
boundary.Erase();
```

### Supported Polyline Types
- `LWPOLYLINE` (Lightweight Polyline)
- `POLYLINE` (2D Polyline)
- `CIRCLE`, `ELLIPSE`, `SPLINE`, `ARC` (as hatch boundary selection targets in FIXHATCH)

### Properties Preserved on Result Entities
| Property | Polyline operations | Hatch operations |
|---|---|---|
| Layer | ✅ | ✅ |
| Color | ✅ | — |
| LineWeight | ✅ | ✅ |
| Linetype | ✅ | — |
| Hatch pattern name / scale / angle | — | ✅ |

### Limitations
- **Arc segments in polylines are flattened** — bulges are converted to straight segments during NTS conversion
- **Hatch arcs are tessellated** — approximated with 17 sample points (configurable via `segments` parameter)
- **2D only** — Z-coordinates are ignored
- **Open polylines** — treated as LineStrings; limited support in boolean operations
- **Spline boundaries** — spline curves in hatch loops are not yet supported

---

## Dependencies

- **NetTopologySuite** (v2.6+) — geometry processing and topology validation
- **AutoCAD .NET API** — `accoremgd.dll`, `acdbmgd.dll`, `acmgd.dll`

---

## Building from Source

### Requirements
- Visual Studio 2022 or later
- .NET 8.0 SDK
- AutoCAD 2025 (for reference assemblies)

### Build Steps
1. Clone the repository
2. Open `C3DTools.sln` in Visual Studio
3. Restore NuGet packages
4. Build the solution (Release configuration)
5. Output: `bin\Release\net8.0\C3DTools.dll`

---

## Use Cases

### Land Development
- Tag parcels/lots with ID codes and analyze hatch overlaps (floodplains, zoning, utilities)
- Report overlap areas for regulatory compliance, easements, or setbacks
- Label parcels automatically with text annotations
- Fix imported hatch geometries from survey or GIS sources

### Civil Engineering
- Clean up imported survey data with invalid boundaries
- Fix invalid parcel/lot boundaries before boolean operations
- Merge or split parcels
- Compute overlap between hatched regions and lot boundaries

### GIS Data Preparation
- Validate topology before export
- Fix self-intersecting polygons for Shapefile or GeoJSON output
- Tag and analyze features with custom IDs

---

## Troubleshooting

**"Could not load file or assembly 'NetTopologySuite'"**
- Ensure `NetTopologySuite.dll` is in the same folder as `C3DTools.dll`

**FIXHATCH reports errors but geometry looks valid**
- The hatch may contain arc-based loops that were tessellated differently before fixing. This is expected — the fixed hatch uses straight-segment approximations of arcs.

**Empty results from boolean operations**
- Check that polylines are closed
- Verify polylines actually overlap (for intersection)
- Run `FIXPLINE` first to ensure valid input geometries

**FIXHATCH leaves the original hatch unchanged**
- The original is only erased after at least one replacement hatch is successfully created. If the fix fails entirely, the original is preserved and an error is reported.

**Unexpected multi-part results**
- Expected when operations produce disjoint geometries (e.g., subtracting a polyline that splits a region into two separate parts)

---

## License

[Specify your license here]

---

## Contributing

Contributions are welcome! Please submit pull requests or open issues for bugs and feature requests.

---

## Version History

### v1.1.0 (Current)
- **FIXHATCH**: Complete rewrite using proper Autodesk boundary-object pattern (`ObjectIdCollection` → `AppendLoop` → `EvaluateHatch`)
- **FIXHATCH**: Area-based ring classification in `HatchToNts` replaces unreliable winding-direction and `HatchLoopTypes` flag checks
- **FIXHATCH**: Arc tessellation switched to `Curve2d.GetSamplePoints()` — eliminates reversed-arc self-intersection bugs
- **FIXHATCH**: `FixHolesOutsideShell` fallback promotes mis-wound outer rings to shells instead of dropping them
- **HATCHOVERLAP**: Benefits from improved `HatchToNts` accuracy

### v1.0.0
- FIXPLINE: Fix invalid polygon geometries
- PLUNION: Union polylines
- PLDIFF: Difference/subtract polylines
- PLINT: Intersection of polylines
- TAGID / GETID / LABELID: XData-based polyline tagging
- HATCHOVERLAP: Hatch overlap analysis with tagged polylines
- Multi-geometry result handling
- Property preservation


## Commands

### Tag Commands

#### TAGID - Tag Polyline with ID

Attach a custom string ID to a polyline using XData (app name: `C3DTools_ID`).

**Usage:**
1. Type `TAGID` in the command line
2. Select a polyline to tag
3. Enter a string ID (e.g., `LOT-001`, `PARCEL-A`)
4. ID is stored as XData on the polyline

**Use cases:**
- Label parcels, lots, or zones
- Prepare polylines for overlap analysis
- Track polylines by name/number

**Example:**
```
Command: TAGID
Select polyline to tag: 
Enter ID string: LOT-001
Polyline tagged with ID: LOT-001
```

---

#### GETID - Retrieve Polyline ID

Display the stored ID tag from a polyline.

**Usage:**
1. Type `GETID` in the command line
2. Select a tagged polyline
3. ID is displayed in the command line

**Example:**
```
Command: GETID
Select polyline to retrieve ID: 
Polyline ID: LOT-001
```

---

#### LABELID - Place ID Label

Place a text label showing the polyline's ID at a specified point.

**Usage:**
1. Type `LABELID` in the command line
2. Select a tagged polyline
3. Pick a point for label placement
4. DBText entity is created with the ID, inheriting the polyline's layer

**Properties:**
- Text height: `db.Textsize`
- Layer: Same as polyline
- Position: User-specified point

**Example:**
```
Command: LABELID
Select tagged polyline: 
Pick point for label: 
Label 'LOT-001' placed at (100.0, 200.0, 0.0)
```

---

### HATCHOVERLAP - Hatch Overlap Analysis

Compute and report overlap areas between tagged polylines and hatches.

**Usage:**
1. Type `HATCHOVERLAP` in the command line
2. Select polylines with ID tags (use `TAGID` first)
3. Select hatches
4. Overlap areas are computed and reported for each polyline-hatch pair

**What it does:**
- Converts polylines to NTS polygons
- Converts hatches to NTS polygons (handles outer loops and holes)
- Computes intersection area using `Geometry.Intersection()`
- Reports results to command line

**Hatch support:**
- Outer loops (external boundaries)
- Inner loops (holes)
- Polyline boundaries (with bulges)
- Line, circular arc, and elliptical arc segments
- Arc tessellation (16 segments)

**Example output:**
```
Command: HATCHOVERLAP
Select polylines with ID tags: 
Select hatches: 

--- Overlap Analysis Results ---
Polyline ID: LOT-001
  Hatch 1: Overlap Area = 1234.5678
  Hatch 2: No overlap
Polyline ID: LOT-002
  Hatch 1: Overlap Area = 567.8901
  Hatch 2: Overlap Area = 890.1234
--- End of Analysis ---
```

**Warnings:**
- Polylines without ID tags are skipped
- Invalid geometries are skipped
- Errors are reported to command line

---

### FIXHATCH - Fix Invalid Hatch Geometries

Fixes self-intersecting, bowtie, and other invalid hatch geometries by extracting boundaries, validating, and recreating hatches.

**Usage:**
1. Type `FIXHATCH` in the command line
2. Select one or more hatches to fix (across multiple layers)
3. Invalid geometries will be automatically corrected
4. Original hatches are deleted and replaced with fixed hatches

**What it fixes:**
- Self-intersecting hatch boundaries (bowties)
- Invalid ring orientation
- Duplicate vertices
- Collapsed geometries
- Other topological issues in hatch loops

**Properties preserved:**
- Layer
- Color
- Hatch pattern name
- Pattern scale
- Pattern angle
- Pattern type

**Behavior:**
- Valid hatches are skipped (no changes)
- Fixed hatches replace the originals
- Complex hatches may be split into multiple hatches
- Reports issues found and results

**Example output:**
```
Command: FIXHATCH
Select hatches to fix: 
Hatch 1A2B: Invalid geometry detected.
  Issue: Self-intersection at [100.0, 200.0]
  Fixed and recreated hatch.
Hatch 3C4D: Geometry is valid. Skipped.

FIXHATCH complete: 1 fixed, 1 skipped, 0 errors.
```

---

### FIXPLINE - Fix Invalid Polyline Geometries

Fixes self-intersecting, bowtie, and other invalid polyline geometries.

**Usage:**
1. Type `FIXPLINE` in the command line
2. Select one or more polylines to fix
3. Invalid geometries will be automatically corrected

**What it fixes:**
- Self-intersecting polygons (bowties)
- Invalid ring orientation
- Duplicate vertices
- Collapsed geometries
- Other topological issues

**Behavior:**
- Valid polylines are skipped (no changes)
- Fixed polylines replace the originals
- Bowties may be split into multiple polylines
- Reports issues found and results

**Example output:**
```
Command: FIXPLINE
Select polylines to fix: 
  Issue: Self-intersection at [100.0, 200.0]
  Split into 2 geometries

FIXPLINE complete: 1 fixed, 0 skipped.
  1 geometries split into multiple polylines.
```

---

### PLUNION - Union Polylines

Combines multiple polylines into a single geometry (or multiple disjoint geometries).

**Usage:**
1. Type `PLUNION` in the command line
2. Select two or more polylines to union
3. Result polyline(s) are created, originals are deleted

**Use cases:**
- Merge adjacent parcels
- Combine overlapping boundaries
- Create composite regions

**Example output:**
```
Command: PLUNION
Select polylines to union: 

PLUNION complete: 3 polylines → 1 result(s).
```

---

### PLDIFF - Difference (Subtract Polylines)

Subtracts one or more polylines from a base polyline.

**Usage:**
1. Type `PLDIFF` in the command line
2. Select the base polyline (what you're cutting from)
3. Select polyline(s) to subtract from the base
4. Result polyline(s) are created, base is deleted

**Use cases:**
- Cut holes in polygons
- Remove overlapping areas
- Create exclusion zones
- Parcel subdivisions

**Example output:**
```
Command: PLDIFF
Select base polyline: 
Select polylines to subtract: 

PLDIFF complete: 1 result(s).
```

---

### PLINT - Intersection

Finds the overlapping area between two or more polylines.

**Usage:**
1. Type `PLINT` in the command line
2. Select two or more polylines
3. Result polyline(s) are created with the common area
4. Original polylines remain unchanged

**Use cases:**
- Find overlapping zones
- Determine shared boundaries
- Identify conflict areas
- Calculate overlap regions

**Example output:**
```
Command: PLINT
Select polylines to intersect: 

PLINT complete: 1 result(s).
```

---

## Installation

### Requirements
- AutoCAD 2025 or later (or Civil 3D 2025+)
- .NET 8.0 Runtime

### Steps
1. Download the latest `C3DTools.dll` from the releases
2. Copy `C3DTools.dll` and `NetTopologySuite.dll` to a folder
3. In AutoCAD, type `NETLOAD`
4. Browse to and select `C3DTools.dll`
5. Commands are now available: `TAGID`, `GETID`, `LABELID`, `HATCHOVERLAP`, `FIXHATCH`, `FIXPLINE`, `PLUNION`, `PLDIFF`, `PLINT`

### Auto-load on Startup
Add to your `acad.lsp` or use the AutoCAD Startup Suite to automatically load C3DTools.

---

## Technical Details

### XData Tag System
- **App Name**: `C3DTools_ID`
- **Storage**: Extended Data (XData) attached to polylines
- **Format**: `{(1001, "C3DTools_ID"), (1000, "LOT-001")}`
- **Access**: `entity.GetXDataForApplication("C3DTools_ID")`
- **Index**: ID string is at `ResultBuffer[1]`

### Hatch Conversion to NTS Geometry
- **Outer loops**: Identified by `HatchLoopTypes.External` flag
- **Inner loops (holes)**: Non-external loops
- **Boundary types supported**:
  - PolylineBoundary (BulgeVertexCollection)
  - LineSegment2d
  - CircularArc2d (tessellated with 16 segments)
  - EllipticalArc2d (tessellated with 16 segments)
- **Output**: NTS `Polygon` (single outer loop) or `MultiPolygon` (multiple outer loops)
- **Rotation direction**: Outer = CCW, Holes = CW (NTS convention)
- **Validation**: All geometries are checked with `IsValid`

### Supported Polyline Types
- LWPOLYLINE (Lightweight Polyline)
- POLYLINE (2D Polyline)

### Limitations
- **Arc segments are flattened** - Bulges (arcs) in polylines are converted to straight segments
- **Hatch arcs are tessellated** - Circular and elliptical arcs are approximated with 16 line segments (configurable)
- **2D only** - Z-coordinates are ignored
- **Closed polylines** are treated as polygons
- **Open polylines** are treated as linestrings (limited support in boolean operations)
- **Hatch holes**: Multi-loop hatches are simplified; holes may not be spatially assigned to correct outer loops in complex cases
- **Spline boundaries**: Spline curves in hatch loops are not yet supported

### Properties Preserved
When creating result polylines, these properties are copied from the original:
- Layer
- Color
- LineWeight
- Linetype

---

## Dependencies

- **NetTopologySuite** (v2.6.0) - Geometry processing library
- **AutoCAD .NET API** - accoremgd.dll, acdbmgd.dll, acmgd.dll

---

## Building from Source

### Requirements
- Visual Studio 2022 or later
- .NET 8.0 SDK
- AutoCAD 2025 (for reference assemblies)

### Build Steps
1. Clone the repository
2. Open `C3DTools.sln` in Visual Studio
3. Restore NuGet packages
4. Build the solution (Release configuration)
5. Output: `bin\Release\net8.0\C3DTools.dll`

---

## Use Cases

### Land Development
- **Tag parcels/lots** with ID codes (e.g., LOT-001, PARCEL-A)
- **Analyze hatch overlaps** with parcel boundaries (e.g., floodplains, zoning, utilities)
- **Report overlap areas** for regulatory compliance, easements, or setbacks
- **Label parcels** automatically with text annotations

### Civil Engineering
- Clean up imported survey data
- Fix invalid parcel boundaries
- Merge/split parcels
- Create surface boundaries
- **Compute overlap** between hatched regions and lot boundaries

### GIS Data Preparation
- Validate topology before export
- Fix self-intersecting polygons for Shapefiles
- Prepare geometries for GeoJSON export
- **Tag and analyze** features with custom IDs

### General CAD
- Boolean operations on closed polylines
- Geometry cleanup
- **ID-based tracking** of polylines
- **Hatch area calculations**
- Area calculations (after ensuring validity)

---

## Troubleshooting

**"Could not load file or assembly 'NetTopologySuite'"**
- Make sure `NetTopologySuite.dll` is in the same folder as `C3DTools.dll`

**"eDegenerateGeometry" error**
- This has been fixed in current version - update to latest release

**Empty results from boolean operations**
- Check that polylines are closed
- Verify polylines actually overlap (for intersection)
- Try FIXGEOM first to ensure valid geometries

**Unexpected multi-part results**
- This is expected behavior when operations produce disjoint geometries
- Example: Subtracting a polyline that creates two separate regions

---

## License

[Specify your license here]

---

## Contributing

Contributions are welcome! Please submit pull requests or open issues for bugs and feature requests.

---

## Version History

### v1.0.0 (Current)
- FIXGEOM: Fix invalid polygon geometries
- PLUNION: Union polylines
- PLDIFF: Difference/subtract polylines  
- PLINT: Intersection of polylines
- Multi-geometry result handling
- Property preservation
