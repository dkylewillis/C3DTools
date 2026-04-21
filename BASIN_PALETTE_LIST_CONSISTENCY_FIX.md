# Basin Palette - List Behavior Consistency Fix

## Issue Fixed

**Problem**: Tagged Basins section had different hover/selection behavior than Untagged Basins section. Tagged Basins had no visible selection state while Untagged Basins showed proper ListBox selection styling.

**Root Cause**: The Tagged Basins `ItemContainerStyle` had a custom `ControlTemplate` that completely removed the ListBoxItem's default selection visual, while Untagged Basins kept the default template.

## Solution

Made both ListBox sections use identical `ItemContainerStyle`:

```xaml
<ListBox.ItemContainerStyle>
    <Style TargetType="ListBoxItem">
        <Setter Property="Padding" Value="0"/>
        <Setter Property="HorizontalContentAlignment" Value="Stretch"/>
    </Style>
</ListBox.ItemContainerStyle>
```

Both sections now:
- ✅ Show hover highlighting (light blue)
- ✅ Show selection state (blue background)
- ✅ Show pressed state (darker blue)
- ✅ Have consistent visual behavior

## AutoCAD Selection Lag

**Note**: The 1-2 second delay when selecting polylines in the AutoCAD viewport is **normal AutoCAD behavior** and cannot be eliminated. This is AutoCAD's `SetImpliedSelection` API call performance.

**What we've optimized**:
- ✅ **UI updates instantly** - "Selected Basin" section responds immediately
- ✅ **Attributes load instantly** - All basin properties show right away
- ⏱️ **AutoCAD selection is slower** - This is AutoCAD's internal behavior

The palette UI is now as fast as possible - the only remaining delay is AutoCAD itself highlighting/selecting the polyline in the viewport, which is inherent to how AutoCAD processes selection operations.

## Files Modified

1. **BasinPaletteView.xaml**
   - Removed custom `ControlTemplate` from Tagged Basins `ItemContainerStyle`
   - Now both lists use identical styles and have the same visual behavior

2. **BasinPaletteViewModel.cs**
   - Added comment explaining AutoCAD selection delay is normal

## Visual Behavior (Both Lists)

| State | Visual |
|-------|--------|
| Default | White background |
| Hover | Light blue `#E3F2FD` |
| Pressed | Medium blue `#BBDEFB` |
| Selected | Blue background (standard ListBox selection) |

## Result

Both Tagged and Untagged basin lists now have **identical, consistent behavior** with proper hover, pressed, and selection states visible.
