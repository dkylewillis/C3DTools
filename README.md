# C3DTools - Polyline Tools for AutoCAD

A collection of geometry processing tools for AutoCAD polylines using NetTopologySuite for topologically correct operations. Includes XData-based tagging system and hatch overlap analysis for land development workflows.

## Overview

C3DTools provides commands for:
- **Tagging polylines** with custom IDs (XData-based)
- **Analyzing hatch overlaps** with tagged polylines
- **Fixing invalid geometries** (self-intersections, bowties)
- **Boolean operations** on polylines (Union, Difference, Intersection)

These tools are particularly useful for Civil 3D workflows, GIS data preparation, parcel analysis, and ensuring geometrically valid polygons.

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
