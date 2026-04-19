# C3DTools - Polyline Tools for AutoCAD

A collection of geometry processing tools for AutoCAD polylines using NetTopologySuite for topologically correct operations.

## Overview

C3DTools provides commands for fixing invalid geometries and performing boolean operations on polylines in AutoCAD. These tools are particularly useful for Civil 3D workflows, GIS data preparation, and ensuring geometrically valid polygons for parcels, surfaces, and boundaries.

## Commands

### FIXGEOM - Fix Invalid Geometries

Fixes self-intersecting, bowtie, and other invalid polygon geometries.

**Usage:**
1. Type `FIXGEOM` in the command line
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
Command: FIXGEOM
Select polylines to fix: 
  Issue: Self-intersection at [100.0, 200.0]
  Split into 2 geometries

FIXGEOM complete: 1 fixed, 0 skipped.
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
5. Commands are now available: `FIXGEOM`, `PLUNION`, `PLDIFF`, `PLINT`

### Auto-load on Startup
Add to your `acad.lsp` or use the AutoCAD Startup Suite to automatically load C3DTools.

---

## Technical Details

### Supported Polyline Types
- LWPOLYLINE (Lightweight Polyline)
- POLYLINE (2D Polyline)

### Limitations
- **Arc segments are flattened** - Bulges (arcs) are converted to straight segments
- **2D only** - Z-coordinates are ignored
- **Closed polylines** are treated as polygons
- **Open polylines** are treated as linestrings (limited support in boolean operations)

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

### Civil Engineering
- Clean up imported survey data
- Fix invalid parcel boundaries
- Merge/split parcels
- Create surface boundaries

### GIS Data Preparation
- Validate topology before export
- Fix self-intersecting polygons for Shapefiles
- Prepare geometries for GeoJSON export

### General CAD
- Boolean operations on closed polylines
- Geometry cleanup
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
