# BASINLANDUSE Command - Updated for New Attribute System

## Changes Made

Updated the `BASINLANDUSE` command to use the new Boundary and Development attributes instead of the old Classification system.

## Before vs After

### Old System (Classification)
- Read from `C3DTools_BasinSplit` XData
- Single column: "Classification" (ONSITE/OFFSITE)
- Only shown for split basins

### New System (Boundary + Development)
- Read from `C3DTools_Basin` XData (same as Basin ID)
- Two optional columns: "Boundary" and "Development"
- Shown for all basins when attributes are present

## XData Structure

The command now reads the complete XData structure:
```
[AppName, BasinId, Boundary, Development]
Index 0: "C3DTools_Basin"
Index 1: Basin ID (string)
Index 2: Boundary (string) - "", "Onsite", "Offsite"
Index 3: Development (string) - "", "Pre", "Post"
```

## New Output Format

### Example with all attributes:
```
id  Boundary  Development  LAWN    PAVEMENT  Total
--  --------  -----------  ------  --------  ------
A   Onsite    Pre          150.23  45.12     195.35
A   Offsite   Pre          220.45  78.90     299.35

B   Onsite    Post         180.50  60.25     240.75
B   Offsite   Post         190.30  55.40     245.70
```

### Example with only Boundary:
```
id  Boundary  LAWN    PAVEMENT  Total
--  --------  ------  --------  ------
A   Onsite    150.23  45.12     195.35
A   Offsite   220.45  78.90     299.35
```

### Example with no attributes:
```
id  LAWN    PAVEMENT  Total
--  ------  --------  ------
A   150.23  45.12     195.35
B   220.45  78.90     299.35
```

## Dynamic Column Display

- **Boundary column**: Only shown if at least one basin has a Boundary attribute
- **Development column**: Only shown if at least one basin has a Development attribute
- **Column order**: id, Boundary, Development, [layers...], Total

## Aggregation Logic

Basins are aggregated by the combination of:
1. Basin ID
2. Boundary attribute
3. Development attribute

Multiple polylines with the same ID and attributes are combined into one row.

## Row Ordering

Within each basin ID:
1. Sort by Development: (empty) → Pre → Post
2. Then by Boundary: (empty) → Onsite → Offsite

Example order for Basin A:
```
A                     (no attributes)
A   Onsite            (boundary only)
A   Offsite           (boundary only)
A           Pre       (development only)
A   Onsite  Pre       (both)
A   Offsite Pre       (both)
A           Post      (development only)
A   Onsite  Post      (both)
A   Offsite Post      (both)
```

## Code Changes

### 1. Removed
- `BasinSplitAppName` constant
- `GetPolylineClassification()` method
- All references to split classification logic

### 2. Added
- `GetPolylineBoundary()` method - reads index 2 of XData
- `GetPolylineDevelopment()` method - reads index 3 of XData
- `hasBoundary` and `hasDevelopment` flags for dynamic column display

### 3. Updated
- Tuple structure from `(RowKey, BasinId, Classification)` to `(RowKey, BasinId, Boundary, Development)`
- Aggregation key from `(BasinId, Classification)` to `(BasinId, Boundary, Development)`
- Display logic to show Boundary and Development as separate columns
- Row ordering to sort by Development then Boundary

## Clipboard Format

Tab-separated format includes dynamic columns:
```
id	Boundary	Development	LAWN	PAVEMENT	Total
A	Onsite	Pre	150.23	45.12	195.35
A	Offsite	Pre	220.45	78.90	299.35
```

Can be pasted directly into Excel or other spreadsheet applications.

## Backward Compatibility

- Old basins with only 2 fields `[AppName, BasinId]` will show with empty Boundary and Development
- Old split basins with `C3DTools_BasinSplit` XData are **no longer read** - run SPLITBASINS again to update them
- The command will work with mixed old/new basins, but old split data won't be recognized

## Benefits

1. ✅ Consistent with new unified XData structure
2. ✅ More flexible - any combination of attributes
3. ✅ Clearer labels - "Boundary" and "Development" vs "Classification"
4. ✅ Better sorting - organized by development stage then boundary
5. ✅ Dynamic columns - only show columns when needed
