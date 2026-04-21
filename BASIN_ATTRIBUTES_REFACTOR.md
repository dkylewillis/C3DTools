# Basin Attribute System - Implementation Summary

## Overview
Refactored the basin tagging system to support structured attributes for all basins, replacing the old split-classification system with universal, editable attributes.

## New XData Structure

All basins now use a single XData format under `C3DTools_Basin`:

```
[AppName, BasinId, Boundary, Development]
```

**Index 0**: AppName = "C3DTools_Basin"  
**Index 1**: Basin ID (string) - e.g., "A", "Basin-1"  
**Index 2**: Boundary (string) - "", "Onsite", or "Offsite"  
**Index 3**: Development (string) - "", "Pre", or "Post"

## Changes Made

### 1. Model (`BasinInfo.cs`)
- **Removed**: `ParentBasinId`, `SplitClassification` properties
- **Added**: `Boundary` (default "None") and `Development` (default "") properties
- **Updated**: `DisplayText` to show attributes like: `A (Post, Onsite)`

### 2. Service Layer (`BasinDataService.cs`)
- **GetAllBasins/GetSelectedBasin**: Now reads all 4 XData fields
- **TagBasin**: Updated signature to accept `(doc, id, basinId, boundary, development)`
- **Removed**: All references to `BasinSplitAppName` XData

### 3. ViewModel (`BasinPaletteViewModel.cs`)
- **Added**: `SelectedBoundary` and `SelectedDevelopment` properties
- **Added**: `BoundaryOptions` ("None", "Onsite", "Offsite") and `DevelopmentOptions` ("", "Pre", "Post")
- **Updated**: `SelectedBasin` setter auto-populates all three edit fields
- **Updated**: `ExecuteTagBasin` saves all three attributes

### 4. View (`BasinPaletteView.xaml`)
- **Added**: Two ComboBox dropdowns for Boundary and Development
- **Layout**: Basin ID (TextBox), Boundary (ComboBox), Development (ComboBox), Update button
- **Removed**: Split classification warning message

### 5. Commands (`BasinCommands.cs`)
- **TAGBASIN**: Now writes 4-field XData structure with "" (empty) boundary and "" development
- **GETBASIN**: Displays Basin ID, Boundary (if set), and Development (if set)
- **Removed**: Split classification reporting

### 6. Split Basins (`SplitBasinsCommand.cs`)
- **Updated**: Creates split polylines with Boundary set to "ONSITE" or "OFFSITE"
- **Updated**: Erase logic now identifies split basins by Boundary attribute (not old XData app)
- **Removed**: `C3DTools_BasinSplit` XData app usage

## Migration Notes

**Backward Compatibility:**
- Old basins with only 2 fields `[AppName, BasinId]` will read as Boundary="None", Development=""
- Old split basins with `C3DTools_BasinSplit` XData will need manual cleanup (or run SPLITBASINS again)

## Usage

### In the Palette:
1. Select any basin polyline
2. Edit Basin ID, Boundary, and Development in dropdowns
3. Click "Update Basin ID" (or "Tag Basin" for untagged)
4. Changes are immediately saved to XData

### Command Line:
- `TAGBASIN` - Quick tag with ID (sets Boundary=None, Development="")
- `GETBASIN` - View all attributes
- `SPLITBASINS` - Automatically sets Boundary=ONSITE/OFFSITE
- `BASINPALETTE` - Open the editing palette

## Display Format

Basins now display with combined attributes in lists:
- `A` - Simple basin with no attributes
- `A (Post)` - Post-development, no boundary  
- `A (Onsite)` - Onsite basin, no development stage
- `A (Post, Onsite)` - Post-development onsite basin
- `[Untagged] 151D8` - Untagged basin (shows handle)

## Benefits

1. **Universal**: All basins have the same attribute structure
2. **Flexible**: Boundary and Development are optional (empty values allowed)
3. **Editable**: Change any attribute anytime via the palette
4. **Consistent**: SPLITBASINS now uses the same system as manual tagging
5. **Cleaner**: Single XData app instead of two separate ones
