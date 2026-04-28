using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using C3DTools.Models;
using C3DTools.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;

namespace C3DTools.ViewModels
{
    /// <summary>
    /// ViewModel for the Basin Tools Palette.
    /// </summary>
    public class BasinPaletteViewModel : INotifyPropertyChanged
    {
        private readonly BasinDataService _dataService;
        private readonly GlobalSettingsService _globalSettings = new GlobalSettingsService();
        private readonly DrawingSettingsService _drawingSettings = new DrawingSettingsService();

        private BasinInfo? _selectedBasin;
        private BasinInfo? _selectedTaggedBasin;
        private BasinInfo? _selectedUntaggedBasin;
        private bool _suppressSelectionSync;
        private string _newBasinId = string.Empty;
        private string _selectedBoundary = "None";
        private string _selectedDevelopment = string.Empty;
        private string _selectedLayer = "All Layers";
        private List<BasinInfo> _allUntaggedBasins = new List<BasinInfo>();

        // Settings fields
        private string _onsiteLayer = "CALC-BASN-ONSITE";
        private string _offsiteLayer = "CALC-BASN-OFFSITE";
        private string _newLayerPattern = string.Empty;
        private string? _selectedLayerPattern;
        private AreaUnit _areaUnit = AreaUnit.SquareFeet;
        private string _activeTab = "Basins";
        private string _basinsSubTab = "Tagged";
        private string _landuseLayerFilter = string.Empty;
        private bool _showOnlySelected = false;
        private ObservableCollection<LanduseLayerItem> _allLanduseLayers = new ObservableCollection<LanduseLayerItem>();

        public BasinPaletteViewModel()
        {
            _dataService = new BasinDataService();
            TaggedBasins = new ObservableCollection<BasinInfo>();
            UntaggedBasins = new ObservableCollection<BasinInfo>();
            AvailableLayers = new ObservableCollection<string>();
            BoundaryOptions = new ObservableCollection<string> { "", "ONSITE", "OFFSITE" };
            DevelopmentOptions = new ObservableCollection<string> { "", "Pre", "Post" };
            LanduseHatchLayers = new ObservableCollection<string>();

            TagBasinCommand = new RelayCommand(ExecuteTagBasin, CanExecuteTagBasin);
            SelectBasinItemCommand = new RelayCommand<BasinInfo>(ExecuteSelectBasinItem);
            RefreshCommand = new RelayCommand(ExecuteRefresh);

            AddLayerPatternCommand = new RelayCommand(ExecuteAddLayerPattern, () => !string.IsNullOrWhiteSpace(_newLayerPattern));
            RemoveLayerPatternCommand = new RelayCommand(ExecuteRemoveLayerPattern, () => _selectedLayerPattern != null);
            SaveToDrawingCommand = new RelayCommand(ExecuteSaveToDrawing);
            SaveAsDefaultCommand = new RelayCommand(ExecuteSaveAsDefault);
            SaveToDrawingAndSetAsDefaultCommand = new RelayCommand(ExecuteSaveToDrawingAndSetAsDefault);
            RunBasinLanduseCommand = new RelayCommand(ExecuteRunBasinLanduse);
            RunSplitBasinsCommand = new RelayCommand(ExecuteRunSplitBasins);
            GetBasinCommand = new RelayCommand(ExecuteGetBasin);
            LabelBasinCommand = new RelayCommand(ExecuteLabelBasin);
            SwitchTabCommand = new RelayCommand<string>(ExecuteSwitchTab);
            SwitchBasinsSubTabCommand = new RelayCommand<string>(tab => BasinsSubTab = tab);
            RefreshLandusesCommand = new RelayCommand(ExecuteRefreshLanduses);
            ClearLanduseSelectionsCommand = new RelayCommand(ExecuteClearLanduseSelections);

            FilteredLanduseLayers = new ObservableCollection<LanduseLayerItem>();

            // Subscribe to document activation to reload settings when the user switches drawings
            Application.DocumentManager.DocumentActivated += OnDocumentActivated;
            Application.DocumentManager.DocumentCreated += OnDocumentActivated;

            LoadSettingsForActiveDocument();
            RefreshData();
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public ObservableCollection<BasinInfo> TaggedBasins { get; }
        public ObservableCollection<BasinInfo> UntaggedBasins { get; }
        public ObservableCollection<string> AvailableLayers { get; }
        public ObservableCollection<string> BoundaryOptions { get; }
        public ObservableCollection<string> DevelopmentOptions { get; }

        // ── Settings ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Layer name patterns used to auto-collect hatches in BASINLANDUSE.
        /// Supports wildcards (* and ?).
        /// </summary>
        public ObservableCollection<string> LanduseHatchLayers { get; }

        public string OnsiteLayer
        {
            get => _onsiteLayer;
            set { _onsiteLayer = value; OnPropertyChanged(); PushToCache(); }
        }

        public string OffsiteLayer
        {
            get => _offsiteLayer;
            set { _offsiteLayer = value; OnPropertyChanged(); PushToCache(); }
        }

        public string NewLayerPattern
        {
            get => _newLayerPattern;
            set
            {
                _newLayerPattern = value;
                OnPropertyChanged();
                ((RelayCommand)AddLayerPatternCommand).RaiseCanExecuteChanged();
            }
        }

        public string? SelectedLayerPattern
        {
            get => _selectedLayerPattern;
            set
            {
                _selectedLayerPattern = value;
                OnPropertyChanged();
                ((RelayCommand)RemoveLayerPatternCommand).RaiseCanExecuteChanged();
            }
        }

        public ICommand AddLayerPatternCommand { get; }
        public ICommand RemoveLayerPatternCommand { get; }
        public ICommand SaveToDrawingCommand { get; }
        public ICommand SaveAsDefaultCommand { get; }
        public ICommand SaveToDrawingAndSetAsDefaultCommand { get; }
        public ICommand RunBasinLanduseCommand { get; }
        public ICommand RunSplitBasinsCommand { get; }
        public ICommand GetBasinCommand { get; }
        public ICommand LabelBasinCommand { get; }
        public ICommand SwitchTabCommand { get; }
        public ICommand SwitchBasinsSubTabCommand { get; }
        public ICommand RefreshLandusesCommand { get; }
        public ICommand ClearLanduseSelectionsCommand { get; }

        public ObservableCollection<string> AreaUnitOptions { get; } = new ObservableCollection<string> { "Square Feet", "Acres" };

        public ObservableCollection<LanduseLayerItem> FilteredLanduseLayers { get; }

        public string ActiveTab
        {
            get => _activeTab;
            set
            {
                _activeTab = value;
                OnPropertyChanged();
            }
        }

        public string BasinsSubTab
        {
            get => _basinsSubTab;
            set
            {
                _basinsSubTab = value;
                OnPropertyChanged();
            }
        }

        public string LanduseLayerFilter
        {
            get => _landuseLayerFilter;
            set
            {
                _landuseLayerFilter = value;
                OnPropertyChanged();
                FilterLanduseLayers();
            }
        }

        public bool ShowOnlySelected
        {
            get => _showOnlySelected;
            set
            {
                _showOnlySelected = value;
                OnPropertyChanged();
                FilterLanduseLayers();
            }
        }

        public int SelectedLanduseCount => _allLanduseLayers.Count(l => l.IsSelected);

        public string SelectedAreaUnit
        {
            get => _areaUnit == AreaUnit.Acres ? "Acres" : "Square Feet";
            set
            {
                _areaUnit = value == "Acres" ? AreaUnit.Acres : AreaUnit.SquareFeet;
                OnPropertyChanged();
                PushToCache();
            }
        }

        // ─────────────────────────────────────────────────────────────────────────

        public string SelectedLayer
        {
            get => _selectedLayer;
            set
            {
                _selectedLayer = value;
                OnPropertyChanged();
                FilterUntaggedBasins();
            }
        }

        public BasinInfo? SelectedBasin
        {
            get => _selectedBasin;
            set
            {
                if (_selectedBasin == value)
                    return;

                _selectedBasin = value;

                // Populate the edit fields with current values
                NewBasinId = value?.BasinId ?? string.Empty;

                // Normalize boundary to match available options (case-insensitive)
                string boundaryValue = value?.Boundary ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(boundaryValue))
                {
                    // Match to available options case-insensitively
                    var matchingOption = BoundaryOptions.FirstOrDefault(
                        opt => opt.Equals(boundaryValue, System.StringComparison.OrdinalIgnoreCase));
                    SelectedBoundary = matchingOption ?? string.Empty;
                }
                else
                {
                    SelectedBoundary = string.Empty;
                }

                // Normalize development to match available options (case-insensitive)
                string developmentValue = value?.Development ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(developmentValue))
                {
                    // Match to available options case-insensitively
                    var matchingDevOption = DevelopmentOptions.FirstOrDefault(
                        opt => opt.Equals(developmentValue, System.StringComparison.OrdinalIgnoreCase));
                    SelectedDevelopment = matchingDevOption ?? string.Empty;
                }
                else
                {
                    SelectedDevelopment = string.Empty;
                }

                OnPropertyChanged();
                OnPropertyChanged(nameof(TagButtonText));
            }
        }

        public string SelectedBoundary
        {
            get => _selectedBoundary;
            set
            {
                _selectedBoundary = value;
                OnPropertyChanged();
            }
        }

        public string SelectedDevelopment
        {
            get => _selectedDevelopment;
            set
            {
                _selectedDevelopment = value;
                OnPropertyChanged();
            }
        }

        public BasinInfo? SelectedTaggedBasin
        {
            get => _selectedTaggedBasin;
            set
            {
                if (_selectedTaggedBasin == value) return;
                _selectedTaggedBasin = value;
                OnPropertyChanged();

                if (value != null && !_suppressSelectionSync)
                {
                    _suppressSelectionSync = true;
                    try
                    {
                        var doc = Application.DocumentManager.MdiActiveDocument;
                        if (doc != null)
                            _dataService.SelectBasin(doc, value.ObjectId);
                        SelectedBasin = value;
                    }
                    finally
                    {
                        _suppressSelectionSync = false;
                    }
                }
            }
        }

        public BasinInfo? SelectedUntaggedBasin
        {
            get => _selectedUntaggedBasin;
            set
            {
                if (_selectedUntaggedBasin == value) return;
                _selectedUntaggedBasin = value;
                OnPropertyChanged();

                if (value != null && !_suppressSelectionSync)
                {
                    _suppressSelectionSync = true;
                    try
                    {
                        var doc = Application.DocumentManager.MdiActiveDocument;
                        if (doc != null)
                            _dataService.SelectBasin(doc, value.ObjectId);
                        SelectedBasin = value;
                    }
                    finally
                    {
                        _suppressSelectionSync = false;
                    }
                }
            }
        }

        public string NewBasinId
        {
            get => _newBasinId;
            set
            {
                _newBasinId = value;
                OnPropertyChanged();
                ((RelayCommand)TagBasinCommand).RaiseCanExecuteChanged();
            }
        }

        public string TagButtonText
        {
            get
            {
                if (SelectedBasin == null)
                    return "Tag Basin";
                return SelectedBasin.IsTagged ? "Update Basin ID" : "Tag Basin";
            }
        }

        public ICommand TagBasinCommand { get; }
        public ICommand SelectBasinItemCommand { get; }
        public ICommand RefreshCommand { get; }

        public void RefreshData()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            var allBasins = _dataService.GetAllBasins(doc);

            TaggedBasins.Clear();
            _allUntaggedBasins.Clear();
            AvailableLayers.Clear();

            foreach (var basin in allBasins.Where(b => b.IsTagged).OrderBy(b => b.BasinId))
            {
                TaggedBasins.Add(basin);
            }

            foreach (var basin in allBasins.Where(b => !b.IsTagged))
            {
                _allUntaggedBasins.Add(basin);
            }

            // Build unique layer list
            var layers = _allUntaggedBasins
                .Select(b => b.Layer ?? "0")
                .Distinct()
                .OrderBy(l => l)
                .ToList();

            AvailableLayers.Add("All Layers");
            foreach (var layer in layers)
            {
                AvailableLayers.Add(layer);
            }

            // Apply current filter
            FilterUntaggedBasins();

            // Update selected basin from current selection
            var selectedBasin = _dataService.GetSelectedBasin(doc);
            SelectedBasin = selectedBasin;
        }

        private void FilterUntaggedBasins()
        {
            UntaggedBasins.Clear();

            var filtered = _selectedLayer == "All Layers"
                ? _allUntaggedBasins
                : _allUntaggedBasins.Where(b => b.Layer == _selectedLayer);

            foreach (var basin in filtered)
            {
                UntaggedBasins.Add(basin);
            }
        }

        public void UpdateSelection()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            var selectedBasin = _dataService.GetSelectedBasin(doc);
            SelectedBasin = selectedBasin;

            if (_suppressSelectionSync) return;
            _suppressSelectionSync = true;
            try
            {
                if (selectedBasin?.IsTagged == true)
                {
                    var match = TaggedBasins.FirstOrDefault(b => b.ObjectId == selectedBasin.ObjectId);
                    SelectedTaggedBasin = match;
                    SelectedUntaggedBasin = null;
                }
                else if (selectedBasin != null)
                {
                    var match = UntaggedBasins.FirstOrDefault(b => b.ObjectId == selectedBasin.ObjectId);
                    SelectedUntaggedBasin = match;
                    SelectedTaggedBasin = null;
                }
                else
                {
                    SelectedTaggedBasin = null;
                    SelectedUntaggedBasin = null;
                }
            }
            finally
            {
                _suppressSelectionSync = false;
            }
        }

        private bool CanExecuteTagBasin()
        {
            return SelectedBasin != null && !string.IsNullOrWhiteSpace(NewBasinId);
        }

        private void ExecuteTagBasin()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null || SelectedBasin == null)
                return;

            string oldId = SelectedBasin.BasinId ?? "[Untagged]";
            _dataService.TagBasin(doc, SelectedBasin.ObjectId, NewBasinId, SelectedBoundary, SelectedDevelopment);

            // Show message in command line
            doc.Editor.WriteMessage($"\nBasin updated: {oldId} → {NewBasinId} (Boundary: {SelectedBoundary}, Dev: {SelectedDevelopment})");

            RefreshData();
        }

        private void ExecuteSelectBasin(BasinInfo? basin)
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null || basin == null)
                return;

            // Update UI immediately for responsiveness
            SelectedBasin = basin;

            // Then update AutoCAD selection (may take 1-2 seconds - this is normal AutoCAD behavior)
            // The delay is in AutoCAD's selection mechanism, not our code
            _dataService.SelectBasin(doc, basin.ObjectId);
        }

        private void ExecuteSelectBasinItem(BasinInfo? basin)
        {
            if (basin != null)
            {
                ExecuteSelectBasin(basin);
            }
        }

        private void ExecuteRefresh()
        {
            RefreshData();
        }

        // ── Settings helpers ─────────────────────────────────────────────────────

        private void LoadSettingsForActiveDocument()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            BasinSettings settings = doc != null
                ? new SettingsResolver().Resolve(doc.Database)
                : _globalSettings.Load();

            OnsiteLayer = settings.OnsiteLayer;
            OffsiteLayer = settings.OffsiteLayer;
            _areaUnit = settings.AreaUnit;
            OnPropertyChanged(nameof(SelectedAreaUnit));
            LanduseHatchLayers.Clear();
            foreach (string p in settings.LanduseHatchLayers)
                LanduseHatchLayers.Add(p);

            PushToCache();
        }

        private void OnDocumentActivated(object sender, DocumentCollectionEventArgs e)
        {
            LoadSettingsForActiveDocument();
        }

        private BasinSettings BuildSettingsFromUi() => new BasinSettings
        {
            OnsiteLayer = OnsiteLayer,
            OffsiteLayer = OffsiteLayer,
            LanduseHatchLayers = new List<string>(LanduseHatchLayers),
            AreaUnit = _areaUnit
        };

        private void ExecuteAddLayerPattern()
        {
            string pattern = _newLayerPattern.Trim();
            if (!string.IsNullOrEmpty(pattern) && !LanduseHatchLayers.Contains(pattern))
                LanduseHatchLayers.Add(pattern);
            NewLayerPattern = string.Empty;
            PushToCache();
        }

        private void ExecuteRemoveLayerPattern()
        {
            if (_selectedLayerPattern != null)
                LanduseHatchLayers.Remove(_selectedLayerPattern);
            PushToCache();
        }

        private void ExecuteSaveToDrawing()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            _drawingSettings.Save(doc.Database, BuildSettingsFromUi());
            doc.Editor.WriteMessage("\nBasin settings saved to drawing.");
        }

        private void ExecuteSaveAsDefault()
        {
            _globalSettings.Save(BuildSettingsFromUi());
            var doc = Application.DocumentManager.MdiActiveDocument;
            doc?.Editor.WriteMessage("\nBasin settings saved as global default.");
        }

        private void ExecuteSaveToDrawingAndSetAsDefault()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            BasinSettings s = BuildSettingsFromUi();
            if (doc != null)
                _drawingSettings.Save(doc.Database, s);
            _globalSettings.Save(s);
            doc?.Editor.WriteMessage("\nBasin settings saved to drawing and set as global default.");
        }

        private void PushToCache()
        {
            SettingsCache.Set(BuildSettingsFromUi());
        }

        private void ExecuteRunBasinLanduse()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            doc?.SendStringToExecute("BASINLANDUSE\n", true, false, false);
        }

        private void ExecuteRunSplitBasins()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            doc?.SendStringToExecute("SPLITBASINS\n", true, false, false);
        }

        private void ExecuteGetBasin()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            doc?.SendStringToExecute("GETBASIN\n", true, false, false);
        }

        private void ExecuteLabelBasin()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            doc?.SendStringToExecute("LABELBASIN\n", true, false, false);
        }

        private void ExecuteSwitchTab(string? tabName)
        {
            if (!string.IsNullOrEmpty(tabName))
            {
                ActiveTab = tabName;
                if (tabName == "Landuses")
                {
                    RefreshLanduseLayers();
                }
            }
        }

        private void ExecuteRefreshLanduses()
        {
            RefreshLanduseLayers();
        }

        private void ExecuteClearLanduseSelections()
        {
            // Temporarily unsubscribe from IsSelected changes to avoid saving one by one
            foreach (var layer in _allLanduseLayers)
            {
                layer.IsSelected = false;
            }
            LanduseHatchLayers.Clear();
            PushToCache();
            SaveLanduseSelectionsToDrawing();
            OnPropertyChanged(nameof(SelectedLanduseCount));
        }

        private void RefreshLanduseLayers()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            _allLanduseLayers.Clear();

            using (var tr = doc.TransactionManager.StartTransaction())
            {
                var lt = (LayerTable)tr.GetObject(doc.Database.LayerTableId, OpenMode.ForRead);
                var bt = (BlockTable)tr.GetObject(doc.Database.BlockTableId, OpenMode.ForRead);
                var modelSpace = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                // Count hatches per layer
                var hatchCounts = new System.Collections.Generic.Dictionary<string, int>();

                foreach (ObjectId objId in modelSpace)
                {
                    var obj = tr.GetObject(objId, OpenMode.ForRead);
                    if (obj is Hatch hatch)
                    {
                        string layerName = hatch.Layer;
                        if (!hatchCounts.ContainsKey(layerName))
                            hatchCounts[layerName] = 0;
                        hatchCounts[layerName]++;
                    }
                }

                // Create layer items
                foreach (ObjectId layerId in lt)
                {
                    var ltr = (LayerTableRecord)tr.GetObject(layerId, OpenMode.ForRead);
                    string layerName = ltr.Name;
                    int hatchCount = hatchCounts.ContainsKey(layerName) ? hatchCounts[layerName] : 0;

                    var item = new LanduseLayerItem
                    {
                        LayerName = layerName,
                        HatchCount = hatchCount,
                        IsSelected = false // Will be set below
                    };

                    // Check if this layer matches any pattern in LanduseHatchLayers
                    foreach (var pattern in LanduseHatchLayers)
                    {
                        if (LayerMatchesPattern(layerName, pattern))
                        {
                            item.IsSelected = true;
                            break;
                        }
                    }

                    item.PropertyChanged += (s, e) =>
                    {
                        if (e.PropertyName == nameof(LanduseLayerItem.IsSelected))
                        {
                            OnPropertyChanged(nameof(SelectedLanduseCount));
                            SyncLanduseLayerToSettings(item.LayerName, item.IsSelected);
                            if (_showOnlySelected)
                                FilterLanduseLayers();
                        }
                    };

                    _allLanduseLayers.Add(item);
                }

                tr.Commit();
            }

            FilterLanduseLayers();
        }

        private void SyncLanduseLayerToSettings(string layerName, bool isSelected)
        {
            if (isSelected)
            {
                if (!LanduseHatchLayers.Contains(layerName))
                    LanduseHatchLayers.Add(layerName);
            }
            else
            {
                LanduseHatchLayers.Remove(layerName);
            }
            PushToCache();
            SaveLanduseSelectionsToDrawing();
        }

        private void SaveLanduseSelectionsToDrawing()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            try
            {
                _drawingSettings.Save(doc.Database, BuildSettingsFromUi());
            }
            catch { }
        }

        private void FilterLanduseLayers()
        {
            FilteredLanduseLayers.Clear();

            IEnumerable<LanduseLayerItem> filtered = _allLanduseLayers;

            if (!string.IsNullOrWhiteSpace(_landuseLayerFilter))
                filtered = filtered.Where(l => l.LayerName.Contains(_landuseLayerFilter, System.StringComparison.OrdinalIgnoreCase));

            if (_showOnlySelected)
                filtered = filtered.Where(l => l.IsSelected);

            foreach (var layer in filtered)
            {
                FilteredLanduseLayers.Add(layer);
            }
        }

        private bool LayerMatchesPattern(string layerName, string pattern)
        {
            // Simple wildcard matching: * and ?
            var regex = new System.Text.RegularExpressions.Regex(
                "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
                    .Replace("\\*", ".*")
                    .Replace("\\?", ".") + "$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return regex.IsMatch(layerName);
        }

        // ─────────────────────────────────────────────────────────────────────────

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// Simple ICommand implementation for MVVM.
    /// </summary>
    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool>? _canExecute;

        public RelayCommand(Action execute, Func<bool>? canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

        public void Execute(object? parameter) => _execute();

        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Generic ICommand implementation for MVVM with parameter.
    /// </summary>
    public class RelayCommand<T> : ICommand
    {
        private readonly Action<T?> _execute;
        private readonly Func<T?, bool>? _canExecute;

        public RelayCommand(Action<T?> execute, Func<T?, bool>? canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => _canExecute?.Invoke((T?)parameter) ?? true;

        public void Execute(object? parameter) => _execute((T?)parameter);

        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Represents a layer item in the landuse layers list with hatch count and selection state.
    /// </summary>
    public class LanduseLayerItem : INotifyPropertyChanged
    {
        private bool _isSelected;

        public string LayerName { get; set; } = string.Empty;
        public int HatchCount { get; set; }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
