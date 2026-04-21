# Basin Tools Palette

## Overview

The Basin Tools Palette is a dockable WPF palette that provides a graphical interface for managing basin tagging and inspection in AutoCAD. It complements the existing command-line commands (`TAGBASIN`, `GETBASIN`, `LABELBASIN`, `SPLITBASINS`) with a visual, low-friction workflow.

## Opening the Palette

Type `BASINPALETTE` in the AutoCAD command line to show/hide the palette.

## Features

### 1. Selected Basin Section
- Shows details of the currently selected polyline (if it's a closed polyline)
- Displays:
  - Basin ID (if tagged)
  - Split Classification (ONSITE/OFFSITE, if applicable)
  - Layer name
  - Object handle
- Provides a quick-tag interface for untagged basins:
  - Enter a Basin ID in the text box
  - Click "Tag Basin" to apply the tag

### 2. Tagged Basins List
- Shows all basins in the drawing that have been tagged with `TAGBASIN`
- For each basin:
  - Displays Basin ID and split classification (if any)
  - **→ button**: Selects the basin in the drawing
  - **🔍 button**: Zooms to the basin
- Shows total count of tagged basins
- **Refresh button (↻)**: Manually refreshes the basin list

### 3. Untagged Basins List
- Shows all closed polylines that haven't been tagged yet
- Helps identify which basins still need tagging
- Same selection/zoom buttons as tagged basins
- Shows total count of untagged basins

## Architecture

The palette follows MVVM (Model-View-ViewModel) architecture:

- **Model**: `BasinInfo.cs` - Represents a basin polyline
- **ViewModel**: `BasinPaletteViewModel.cs` - Manages UI state and commands
- **View**: `BasinPaletteView.xaml/.xaml.cs` - WPF user interface
- **Service**: `BasinDataService.cs` - Wraps all AutoCAD API access with proper locking
- **Command**: `BasinPaletteCommand.cs` - Command to show/hide the palette

## Auto-Refresh Behavior

The palette automatically refreshes when:
- You switch to a different document
- You change the selection in the drawing (updates Selected Basin section)
- You click the refresh button

## Existing Commands

The palette works alongside existing commands, which remain fully functional:

- `TAGBASIN` - Tag a polyline with a basin ID
- `GETBASIN` - Retrieve the tag from a polyline
- `LABELBASIN` - Place a text label with the basin ID
- `SPLITBASINS` - Split basins by a site boundary into ONSITE/OFFSITE

## Technical Notes

- All AutoCAD API calls are properly document-locked
- The service layer ensures thread-safety when the palette interacts with AutoCAD
- WPF is enabled in the project (`<UseWPF>true</UseWPF>`)
- The palette is dockable to left/right sides of the AutoCAD window
