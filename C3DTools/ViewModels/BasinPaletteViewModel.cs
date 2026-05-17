using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using C3DTools.Models;
using C3DTools.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
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
        private readonly HydrologyHydrographService _hydrographService;
        private readonly GlobalSettingsService _globalSettings = new GlobalSettingsService();
        private readonly DrawingSettingsService _drawingSettings = new DrawingSettingsService();

        private BasinInfo? _selectedBasin;
        private BasinInfo? _selectedTaggedBasin;
        private BasinInfo? _selectedUntaggedBasin;
        private bool _suppressSelectionSync;
        private string _newBasinId = string.Empty;
        private string _selectedDevelopment = string.Empty;
        private string _selectedCurveNumber = string.Empty;
        private string _selectedTcMinutes = string.Empty;
        private string _selectedDownstreamId = string.Empty;
        private string _selectedLayer = "All Layers";
        private BasinRouteItem? _selectedNetworkRoute;
        private HydrographRowItem? _selectedHydrographRow;
        private HydrographRowItem? _selectedPondRow;
        private string _resultsStatus = "No hydrology results loaded.";
        private bool _updatingInflowOptions;
        private List<BasinInfo> _allUntaggedBasins = new List<BasinInfo>();

        // Settings fields
        private string _newLayerPattern = string.Empty;
        private string? _selectedLayerPattern;
        private AreaUnit _areaUnit = AreaUnit.SquareFeet;
        private string _stormDistribution = "ATLAS14_ALTERNATING_BLOCK";
        private int _hydrographTimeStepMinutes = 2;
        private string _activeTab = "Basins";
        private string _basinsSubTab = "Tagged";
        private string _landuseLayerFilter = string.Empty;
        private bool _showOnlySelected = false;
        private ObservableCollection<LanduseLayerItem> _allLanduseLayers = new ObservableCollection<LanduseLayerItem>();

        public BasinPaletteViewModel()
        {
            _dataService = new BasinDataService();
            _hydrographService = new HydrologyHydrographService();
            TaggedBasins = new ObservableCollection<BasinInfo>();
            UntaggedBasins = new ObservableCollection<BasinInfo>();
            NetworkRoutes = new ObservableCollection<BasinRouteItem>();
            HydrographRows = new ObservableCollection<HydrographRowItem>();
            PondRows = new ObservableCollection<HydrographRowItem>();
            AvailableInflowRows = new ObservableCollection<HydrographInflowOptionItem>();
            AvailableHydrographBasins = new ObservableCollection<HydrographBasinOptionItem>();
            HydrographResultRows = new ObservableCollection<HydrographResultRowItem>();
            AvailableLayers = new ObservableCollection<string>();
            DevelopmentOptions = new ObservableCollection<string> { "", "Pre", "Post" };
            LanduseHatchLayers = new ObservableCollection<string>();

            TagBasinCommand = new RelayCommand(ExecuteTagBasin, CanExecuteTagBasin);
            UntagBasinCommand = new RelayCommand(ExecuteUntagBasin, CanExecuteUntagBasin);
            SaveHydrologyInputsCommand = new RelayCommand(ExecuteSaveHydrologyInputs, CanExecuteSaveHydrologyInputs);
            SaveNetworkRouteCommand = new RelayCommand(ExecuteSaveNetworkRoute, CanExecuteSaveNetworkRoute);
            AddHydrographRowCommand = new RelayCommand(ExecuteAddHydrographRow);
            RemoveHydrographRowCommand = new RelayCommand(ExecuteRemoveHydrographRow, CanExecuteRemoveHydrographRow);
            SaveHydrographRowsCommand = new RelayCommand(ExecuteSaveHydrographRows);
            RegenerateHydrographRowsCommand = new RelayCommand(ExecuteRegenerateHydrographRows);
            AddPondRowCommand = new RelayCommand(ExecuteAddPondRow);
            RemovePondRowCommand = new RelayCommand(ExecuteRemovePondRow, CanExecuteRemovePondRow);
            SavePondRowsCommand = new RelayCommand(ExecuteSavePondRows);
            RefreshHydrologyResultsCommand = new RelayCommand(ExecuteRefreshHydrologyResults);
            SelectBasinItemCommand = new RelayCommand<BasinInfo>(ExecuteSelectBasinItem);
            RefreshCommand = new RelayCommand(ExecuteRefresh);

            AddLayerPatternCommand = new RelayCommand(ExecuteAddLayerPattern, () => !string.IsNullOrWhiteSpace(_newLayerPattern));
            RemoveLayerPatternCommand = new RelayCommand(ExecuteRemoveLayerPattern, () => _selectedLayerPattern != null);
            SaveToDrawingCommand = new RelayCommand(ExecuteSaveToDrawing);
            SaveAsDefaultCommand = new RelayCommand(ExecuteSaveAsDefault);
            SaveToDrawingAndSetAsDefaultCommand = new RelayCommand(ExecuteSaveToDrawingAndSetAsDefault);
            RunBasinLanduseCommand = new RelayCommand(ExecuteRunBasinLanduse);
            RunSplitBasinsCommand = new RelayCommand(ExecuteRunSplitBasins);
            RunHydroModelCommand = new RelayCommand(ExecuteRunHydroModel);
            GetBasinCommand = new RelayCommand(ExecuteGetBasin);
            LabelBasinCommand = new RelayCommand(ExecuteLabelBasin);
            SwitchTabCommand = new RelayCommand<string>(ExecuteSwitchTab);
            SwitchBasinsSubTabCommand = new RelayCommand<string>(tab => BasinsSubTab = tab ?? "Tagged");
            RefreshLandusesCommand = new RelayCommand(ExecuteRefreshLanduses);
            ClearLanduseSelectionsCommand = new RelayCommand(ExecuteClearLanduseSelections);

            FilteredLanduseLayers = new ObservableCollection<LanduseLayerItem>();

            MasksViewModel = new MasksPaletteViewModel();

            // Subscribe to document activation to reload settings when the user switches drawings
            Application.DocumentManager.DocumentActivated += OnDocumentActivated;
            Application.DocumentManager.DocumentCreated += OnDocumentActivated;

            LoadSettingsForActiveDocument();
            RefreshData();
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>ViewModel for the Masks palette tab.</summary>
        public MasksPaletteViewModel MasksViewModel { get; }

        public ObservableCollection<BasinInfo> TaggedBasins { get; }
        public ObservableCollection<BasinInfo> UntaggedBasins { get; }
        public ObservableCollection<BasinRouteItem> NetworkRoutes { get; }
        public ObservableCollection<HydrographRowItem> HydrographRows { get; }
        public ObservableCollection<HydrographRowItem> PondRows { get; }
        public ObservableCollection<HydrographInflowOptionItem> AvailableInflowRows { get; }
        public ObservableCollection<HydrographBasinOptionItem> AvailableHydrographBasins { get; }
        public ObservableCollection<HydrographResultRowItem> HydrographResultRows { get; }
        public ObservableCollection<string> AvailableLayers { get; }
        public ObservableCollection<string> DevelopmentOptions { get; }
        public ObservableCollection<string> HydrographTypeOptions { get; } = new ObservableCollection<string> { "SCS", "Combine", "Rational", "Reach", "Reservoir" };

        // ── Settings ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Layer name patterns used to auto-collect hatches in BASINLANDUSE.
        /// Supports wildcards (* and ?).
        /// </summary>
        public ObservableCollection<string> LanduseHatchLayers { get; }

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
        public ICommand RunHydroModelCommand { get; }
        public ICommand GetBasinCommand { get; }
        public ICommand LabelBasinCommand { get; }
        public ICommand SwitchTabCommand { get; }
        public ICommand SwitchBasinsSubTabCommand { get; }
        public ICommand RefreshLandusesCommand { get; }
        public ICommand ClearLanduseSelectionsCommand { get; }

        public ObservableCollection<string> AreaUnitOptions { get; } = new ObservableCollection<string> { "Square Feet", "Acres" };
        public ObservableCollection<string> StormDistributionOptions { get; } = new ObservableCollection<string>
        {
            "Atlas 14 Alternating Block",
            "TR-55 Type I 24-hr Tabular",
            "TR-55 Type IA 24-hr Tabular",
            "TR-55 Type II 24-hr Tabular",
            "TR-55 Type III 24-hr Tabular"
        };

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

        public string SelectedStormDistribution
        {
            get => StormDistributionDisplayName(_stormDistribution);
            set
            {
                _stormDistribution = StormDistributionCode(value);
                OnPropertyChanged();
                PushToCache();
            }
        }

        public string HydrographTimeStepMinutes
        {
            get => _hydrographTimeStepMinutes.ToString(CultureInfo.InvariantCulture);
            set
            {
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int minutes) || minutes <= 0)
                    return;

                _hydrographTimeStepMinutes = minutes;
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
                SelectedCurveNumber = FormatNullableDouble(value?.CurveNumber);
                SelectedTcMinutes = FormatNullableDouble(value?.TcMinutes);
                SelectedDownstreamId = value?.DownstreamId ?? string.Empty;

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
                ((RelayCommand)UntagBasinCommand).RaiseCanExecuteChanged();
                ((RelayCommand)SaveNetworkRouteCommand).RaiseCanExecuteChanged();
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

        public string SelectedCurveNumber
        {
            get => _selectedCurveNumber;
            set
            {
                _selectedCurveNumber = value;
                OnPropertyChanged();
            }
        }

        public string SelectedTcMinutes
        {
            get => _selectedTcMinutes;
            set
            {
                _selectedTcMinutes = value;
                OnPropertyChanged();
            }
        }

        public string SelectedDownstreamId
        {
            get => _selectedDownstreamId;
            set
            {
                _selectedDownstreamId = value;
                OnPropertyChanged();
            }
        }

        public BasinRouteItem? SelectedNetworkRoute
        {
            get => _selectedNetworkRoute;
            set
            {
                if (_selectedNetworkRoute == value)
                    return;

                _selectedNetworkRoute = value;
                OnPropertyChanged();

                if (value != null)
                {
                    SelectedBasin = value.Basin;
                    var doc = Application.DocumentManager.MdiActiveDocument;
                    if (doc != null)
                        _dataService.SelectBasin(doc, value.Basin.ObjectId);
                }

                ((RelayCommand)SaveNetworkRouteCommand).RaiseCanExecuteChanged();
            }
        }

        public HydrographRowItem? SelectedHydrographRow
        {
            get => _selectedHydrographRow;
            set
            {
                if (_selectedHydrographRow == value)
                    return;

                _selectedHydrographRow = value;
                OnPropertyChanged();
                ApplySelectedHydrographInflowRules();
                RefreshAvailableInflowRows();
                ((RelayCommand)RemoveHydrographRowCommand).RaiseCanExecuteChanged();
            }
        }

        public HydrographRowItem? SelectedPondRow
        {
            get => _selectedPondRow;
            set
            {
                if (_selectedPondRow == value)
                    return;

                _selectedPondRow = value;
                OnPropertyChanged();
                if (value != null)
                    SelectedHydrographRow = value;
                ((RelayCommand)RemovePondRowCommand).RaiseCanExecuteChanged();
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
                    return "Save";
                return "Save";
            }
        }

        public ICommand TagBasinCommand { get; }
        public ICommand UntagBasinCommand { get; }
        public ICommand SaveHydrologyInputsCommand { get; }
        public ICommand SaveNetworkRouteCommand { get; }
        public ICommand AddHydrographRowCommand { get; }
        public ICommand RemoveHydrographRowCommand { get; }
        public ICommand SaveHydrographRowsCommand { get; }
        public ICommand RegenerateHydrographRowsCommand { get; }
        public ICommand AddPondRowCommand { get; }
        public ICommand RemovePondRowCommand { get; }
        public ICommand SavePondRowsCommand { get; }
        public ICommand RefreshHydrologyResultsCommand { get; }
        public ICommand SelectBasinItemCommand { get; }
        public ICommand RefreshCommand { get; }

        public string ResultsStatus
        {
            get => _resultsStatus;
            set
            {
                _resultsStatus = value;
                OnPropertyChanged();
            }
        }

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
            RefreshHydrographBasinOptions();
            RefreshNetworkRoutes();
            RefreshHydrographRows();
        }

        private void RefreshHydrographBasinOptions()
        {
            AvailableHydrographBasins.Clear();
            AvailableHydrographBasins.Add(new HydrographBasinOptionItem { BasinId = string.Empty, DisplayText = "None" });

            foreach (var basin in TaggedBasins.Where(basin => !string.IsNullOrWhiteSpace(basin.BasinId)).OrderBy(basin => basin.BasinId))
            {
                AvailableHydrographBasins.Add(new HydrographBasinOptionItem
                {
                    BasinId = basin.BasinId ?? string.Empty,
                    DisplayText = basin.DisplayText
                });
            }
        }

        private void RefreshNetworkRoutes()
        {
            NetworkRoutes.Clear();
            var upstreamByBasin = TaggedBasins
                .Where(b => !string.IsNullOrWhiteSpace(b.DownstreamId) && !IsTerminalRoute(b.DownstreamId))
                .GroupBy(b => b.DownstreamId, System.StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g => string.Join(", ", g.Select(b => b.BasinId).Where(id => !string.IsNullOrWhiteSpace(id)).OrderBy(id => id)),
                    System.StringComparer.OrdinalIgnoreCase);

            foreach (var basin in TaggedBasins.OrderBy(b => b.BasinId))
            {
                string basinId = basin.BasinId ?? string.Empty;
                NetworkRoutes.Add(new BasinRouteItem
                {
                    Basin = basin,
                    BasinId = basinId,
                    DownstreamId = basin.DownstreamId,
                    UpstreamDisplay = upstreamByBasin.TryGetValue(basinId, out string? upstream) ? upstream : string.Empty
                });
            }
        }

        private void RefreshHydrographRows()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            HydrographRows.Clear();
            var savedRows = _hydrographService.GetHydrographs(doc.Database);
            var rows = savedRows.Count > 0
                ? savedRows.Select(HydrographRowItem.FromModel)
                : GenerateDefaultHydrographRows();

            foreach (var row in rows)
                AddHydrographRowItem(row);

            SelectedHydrographRow = HydrographRows.FirstOrDefault();
            RefreshPondRows();
        }

        private void AddHydrographRowItem(HydrographRowItem row)
        {
            row.PropertyChanged += OnHydrographRowPropertyChanged;
            HydrographRows.Add(row);
        }

        private void OnHydrographRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_updatingInflowOptions)
                return;

            if (ReferenceEquals(sender, SelectedHydrographRow) &&
                (e.PropertyName == nameof(HydrographRowItem.Type) || e.PropertyName == nameof(HydrographRowItem.InflowIdsText)))
            {
                ApplySelectedHydrographInflowRules();
                RefreshAvailableInflowRows();
                if (e.PropertyName == nameof(HydrographRowItem.Type))
                    RefreshPondRows();
                return;
            }

            if (e.PropertyName == nameof(HydrographRowItem.Id) || e.PropertyName == nameof(HydrographRowItem.Description))
                RefreshAvailableInflowRows();

            if (e.PropertyName == nameof(HydrographRowItem.Type) || e.PropertyName == nameof(HydrographRowItem.Id) || e.PropertyName == nameof(HydrographRowItem.Description))
                RefreshPondRows();
        }

        private void RefreshPondRows()
        {
            HydrographRowItem? previousSelection = SelectedPondRow;
            PondRows.Clear();
            foreach (var row in HydrographRows.Where(row => row.Type.Equals("Reservoir", System.StringComparison.OrdinalIgnoreCase)).OrderBy(row => ParseHydrographNumber(row.Id)).ThenBy(row => row.Id))
                PondRows.Add(row);

            SelectedPondRow = previousSelection != null && PondRows.Contains(previousSelection)
                ? previousSelection
                : PondRows.FirstOrDefault();
        }

        private void RefreshAvailableInflowRows()
        {
            AvailableInflowRows.Clear();
            if (SelectedHydrographRow == null)
                return;

            var selectedIds = new HashSet<string>(SplitHydrographIds(SelectedHydrographRow.InflowIdsText), System.StringComparer.OrdinalIgnoreCase);
            foreach (var row in HydrographRows.Where(row => !ReferenceEquals(row, SelectedHydrographRow) && !string.IsNullOrWhiteSpace(row.Id)).OrderBy(row => ParseHydrographNumber(row.Id)).ThenBy(row => row.Id))
            {
                var option = new HydrographInflowOptionItem
                {
                    Id = row.Id.Trim(),
                    Type = row.Type.Trim(),
                    Description = row.Description.Trim(),
                    IsSelected = selectedIds.Contains(row.Id.Trim())
                };
                option.PropertyChanged += OnHydrographInflowOptionChanged;
                AvailableInflowRows.Add(option);
            }
        }

        private void OnHydrographInflowOptionChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_updatingInflowOptions || e.PropertyName != nameof(HydrographInflowOptionItem.IsSelected) || SelectedHydrographRow == null)
                return;

            var changedOption = sender as HydrographInflowOptionItem;
            _updatingInflowOptions = true;
            try
            {
                if (IsSourceHydrographType(SelectedHydrographRow.Type))
                {
                    foreach (var option in AvailableInflowRows)
                        option.IsSelected = false;
                }
                else if (IsSingleInflowHydrographType(SelectedHydrographRow.Type) && changedOption?.IsSelected == true)
                {
                    foreach (var option in AvailableInflowRows.Where(option => !ReferenceEquals(option, changedOption)))
                        option.IsSelected = false;
                }

                SelectedHydrographRow.InflowIdsText = string.Join(", ", AvailableInflowRows.Where(option => option.IsSelected).Select(option => option.Id));
            }
            finally
            {
                _updatingInflowOptions = false;
            }
        }

        private void ApplySelectedHydrographInflowRules()
        {
            if (SelectedHydrographRow == null)
                return;

            var inflowIds = SplitHydrographIds(SelectedHydrographRow.InflowIdsText);
            if (IsSourceHydrographType(SelectedHydrographRow.Type) && inflowIds.Count > 0)
            {
                SelectedHydrographRow.InflowIdsText = string.Empty;
            }
            else if (IsSingleInflowHydrographType(SelectedHydrographRow.Type) && inflowIds.Count > 1)
            {
                SelectedHydrographRow.InflowIdsText = inflowIds[0];
            }
        }

        private List<HydrographRowItem> GenerateDefaultHydrographRows()
        {
            var rows = new List<HydrographRowItem>();
            var localRowsByBasin = new Dictionary<string, HydrographRowItem>(System.StringComparer.OrdinalIgnoreCase);
            var outputRowsByBasin = new Dictionary<string, HydrographRowItem>(System.StringComparer.OrdinalIgnoreCase);
            var routeByBasin = TaggedBasins.ToDictionary(
                basin => basin.BasinId ?? string.Empty,
                basin => basin.DownstreamId ?? string.Empty,
                System.StringComparer.OrdinalIgnoreCase);
            int nextId = 1;

            foreach (var basin in TaggedBasins.OrderBy(basin => basin.BasinId))
            {
                if (string.IsNullOrWhiteSpace(basin.BasinId))
                    continue;

                var row = new HydrographRowItem
                {
                    Id = $"H{nextId++}",
                    Type = "SCS",
                    BasinId = basin.BasinId,
                    DownstreamId = routeByBasin.TryGetValue(basin.BasinId, out string? downstreamId) ? downstreamId : string.Empty,
                    Description = $"{basin.BasinId} SCS hydrograph"
                };
                rows.Add(row);
                localRowsByBasin[basin.BasinId] = row;
                outputRowsByBasin[basin.BasinId] = row;
            }

            var upstreamByBasin = TaggedBasins
                .Where(basin => !string.IsNullOrWhiteSpace(basin.DownstreamId) && !IsTerminalRoute(basin.DownstreamId))
                .GroupBy(basin => basin.DownstreamId, System.StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Select(basin => basin.BasinId ?? string.Empty).Where(id => !string.IsNullOrWhiteSpace(id)).OrderBy(id => id).ToList(), System.StringComparer.OrdinalIgnoreCase);

            foreach (var basin in TaggedBasins.OrderBy(basin => basin.BasinId))
            {
                string basinId = basin.BasinId ?? string.Empty;
                if (!upstreamByBasin.TryGetValue(basinId, out var upstreamIds) || upstreamIds.Count == 0)
                    continue;

                var inflowIds = upstreamIds
                    .Where(outputRowsByBasin.ContainsKey)
                    .Select(upstreamId => outputRowsByBasin[upstreamId].Id)
                    .ToList();

                if (localRowsByBasin.TryGetValue(basinId, out HydrographRowItem? localRow))
                    inflowIds.Add(localRow.Id);

                if (inflowIds.Count == 0)
                    continue;

                var combineRow = new HydrographRowItem
                {
                    Id = $"H{nextId++}",
                    Type = "Combine",
                    BasinId = basinId,
                    InflowIdsText = string.Join(", ", inflowIds),
                    DownstreamId = routeByBasin.TryGetValue(basinId, out string? downstreamId) ? downstreamId : string.Empty,
                    Description = $"Study point at {basinId} outlet"
                };

                rows.Add(combineRow);
                outputRowsByBasin[basinId] = combineRow;
            }

            return rows;
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

        private bool CanExecuteUntagBasin()
        {
            return SelectedBasin?.IsTagged == true;
        }

        private bool CanExecuteSaveHydrologyInputs()
        {
            return SelectedBasin?.IsTagged == true;
        }

        private bool CanExecuteSaveNetworkRoute()
        {
            return SelectedNetworkRoute != null || SelectedBasin?.IsTagged == true;
        }

        private bool CanExecuteRemoveHydrographRow()
        {
            return SelectedHydrographRow != null;
        }

        private bool CanExecuteRemovePondRow()
        {
            return SelectedPondRow != null;
        }

        private void ExecuteUntagBasin()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null || SelectedBasin == null) return;

            string oldId = SelectedBasin.BasinId ?? string.Empty;
            _dataService.UntagBasin(doc, SelectedBasin.ObjectId);
            doc.Editor.WriteMessage($"\nBasin '{oldId}' untagged.");
            RefreshData();
        }

        private bool TrySaveHydrologyInputs(Document doc, BasinInfo basin)
        {
            if (!basin.IsTagged)
                return true;

            bool curveNumberParsed = TryParseOptionalDouble(SelectedCurveNumber, out double? curveNumber, out string curveNumberError);
            bool tcParsed = TryParseOptionalDouble(SelectedTcMinutes, out double? tcMinutes, out string tcError);
            if (!curveNumberParsed || !tcParsed)
            {
                doc.Editor.WriteMessage($"\nHydrology inputs not saved: {curveNumberError}{tcError}");
                return false;
            }

            if (curveNumber.HasValue && (curveNumber.Value < 30 || curveNumber.Value > 100))
            {
                doc.Editor.WriteMessage("\nHydrology inputs not saved: curve number must be between 30 and 100.");
                return false;
            }

            if (tcMinutes.HasValue && tcMinutes.Value <= 0)
            {
                doc.Editor.WriteMessage("\nHydrology inputs not saved: Tc must be greater than zero.");
                return false;
            }

            _dataService.UpdateHydrologyInputs(doc, basin.ObjectId, curveNumber, tcMinutes, SelectedDownstreamId.Trim());
            return true;
        }

        private void ExecuteSaveHydrologyInputs()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null || SelectedBasin == null)
                return;

            if (TrySaveHydrologyInputs(doc, SelectedBasin))
            {
                doc.Editor.WriteMessage($"\nHydrology inputs saved for basin {SelectedBasin.BasinId}.");
                RefreshData();
            }
        }

        private void ExecuteSaveNetworkRoute()
        {
            if (SelectedNetworkRoute != null)
                SelectedDownstreamId = SelectedNetworkRoute.DownstreamId;

            ExecuteSaveHydrologyInputs();
        }

        private void ExecuteAddHydrographRow()
        {
            var row = new HydrographRowItem
            {
                Id = NextHydrographId(),
                Type = "Combine",
                Description = "New hydrograph"
            };
            AddHydrographRowItem(row);
            SelectedHydrographRow = row;
            RefreshPondRows();
        }

        private void ExecuteRemoveHydrographRow()
        {
            if (SelectedHydrographRow == null)
                return;

            int index = HydrographRows.IndexOf(SelectedHydrographRow);
            HydrographRows.Remove(SelectedHydrographRow);
            SelectedHydrographRow = HydrographRows.Count == 0 ? null : HydrographRows[Math.Min(index, HydrographRows.Count - 1)];
            RefreshAvailableInflowRows();
            RefreshPondRows();
        }

        private void ExecuteSaveHydrographRows()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            var rows = HydrographRows
                .Where(row => !string.IsNullOrWhiteSpace(row.Id))
                .Select(row => row.ToModel())
                .ToList();
            _hydrographService.SaveHydrographs(doc.Database, rows);
            doc.Editor.WriteMessage($"\nSaved {rows.Count} hydrograph row(s) to the drawing.");
        }

        private void ExecuteAddPondRow()
        {
            var row = new HydrographRowItem
            {
                Id = NextHydrographId(),
                Type = "Reservoir",
                Description = "New pond"
            };
            AddHydrographRowItem(row);
            RefreshPondRows();
            SelectedPondRow = row;
        }

        private void ExecuteRemovePondRow()
        {
            if (SelectedPondRow == null)
                return;

            HydrographRows.Remove(SelectedPondRow);
            RefreshPondRows();
            RefreshAvailableInflowRows();
        }

        private void ExecuteSavePondRows()
        {
            ExecuteSaveHydrographRows();
        }

        private void ExecuteRegenerateHydrographRows()
        {
            HydrographRows.Clear();
            foreach (var row in GenerateDefaultHydrographRows())
                AddHydrographRowItem(row);
            SelectedHydrographRow = HydrographRows.FirstOrDefault();
            RefreshPondRows();
        }

        private string NextHydrographId()
        {
            int next = HydrographRows
                .Select(row => ParseHydrographNumber(row.Id))
                .Where(number => number > 0)
                .DefaultIfEmpty(0)
                .Max() + 1;
            return $"H{next}";
        }

        private void ExecuteTagBasin()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null || SelectedBasin == null)
                return;

            string oldId = SelectedBasin.BasinId ?? "[Untagged]";
            _dataService.TagBasin(doc, SelectedBasin.ObjectId, NewBasinId, SelectedDevelopment);

            var savedBasin = new BasinInfo
            {
                ObjectId = SelectedBasin.ObjectId,
                BasinId = NewBasinId,
                Development = SelectedDevelopment,
                Layer = SelectedBasin.Layer
            };

            if (!TrySaveHydrologyInputs(doc, savedBasin))
                return;

            doc.Editor.WriteMessage($"\nBasin saved: {oldId} → {NewBasinId} (Dev: {SelectedDevelopment})");

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

            _areaUnit = settings.AreaUnit;
            _stormDistribution = string.IsNullOrWhiteSpace(settings.StormDistribution)
                ? "ATLAS14_ALTERNATING_BLOCK"
                : settings.StormDistribution;
            _hydrographTimeStepMinutes = settings.HydrographTimeStepMinutes > 0
                ? settings.HydrographTimeStepMinutes
                : 2;
            OnPropertyChanged(nameof(SelectedAreaUnit));
            OnPropertyChanged(nameof(SelectedStormDistribution));
            OnPropertyChanged(nameof(HydrographTimeStepMinutes));
            LanduseHatchLayers.Clear();
            foreach (string p in settings.LanduseHatchLayers)
                LanduseHatchLayers.Add(p);

            PushToCache();
        }

        private void OnDocumentActivated(object sender, DocumentCollectionEventArgs e)
        {
            LoadSettingsForActiveDocument();
            MasksViewModel.RefreshMasks();
        }

        private BasinSettings BuildSettingsFromUi() => new BasinSettings
        {
            LanduseHatchLayers = new List<string>(LanduseHatchLayers),
            AreaUnit = _areaUnit,
            StormDistribution = _stormDistribution,
            HydrographTimeStepMinutes = _hydrographTimeStepMinutes > 0 ? _hydrographTimeStepMinutes : 2
        };

        private static string StormDistributionDisplayName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "Atlas 14 Alternating Block";

            return value.ToUpperInvariant() switch
            {
                "SCS_TYPE_I" => "TR-55 Type I 24-hr Tabular",
                "NRCS_TYPE_I" => "TR-55 Type I 24-hr Tabular",
                "TR55_TYPE_I_24HR_TABULAR" => "TR-55 Type I 24-hr Tabular",
                "SCS_TYPE_IA" => "TR-55 Type IA 24-hr Tabular",
                "NRCS_TYPE_IA" => "TR-55 Type IA 24-hr Tabular",
                "TR55_TYPE_IA_24HR_TABULAR" => "TR-55 Type IA 24-hr Tabular",
                "SCS_TYPE_II" => "TR-55 Type II 24-hr Tabular",
                "NRCS_TYPE_II" => "TR-55 Type II 24-hr Tabular",
                "TR55_TYPE_II_24HR_TABULAR" => "TR-55 Type II 24-hr Tabular",
                "SCS_TYPE_III" => "TR-55 Type III 24-hr Tabular",
                "NRCS_TYPE_III" => "TR-55 Type III 24-hr Tabular",
                "TR55_TYPE_III_24HR_TABULAR" => "TR-55 Type III 24-hr Tabular",
                "ATLAS14_ALTERNATING_BLOCK" => "Atlas 14 Alternating Block",
                _ => value
            };
        }

        private static string StormDistributionCode(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "ATLAS14_ALTERNATING_BLOCK";

            return value.ToUpperInvariant() switch
            {
                "ATLAS 14 ALTERNATING BLOCK" => "ATLAS14_ALTERNATING_BLOCK",
                "TR-55 TYPE I 24-HR TABULAR" => "TR55_TYPE_I_24HR_TABULAR",
                "NRCS TYPE I" => "TR55_TYPE_I_24HR_TABULAR",
                "TR-55 TYPE IA 24-HR TABULAR" => "TR55_TYPE_IA_24HR_TABULAR",
                "NRCS TYPE IA" => "TR55_TYPE_IA_24HR_TABULAR",
                "TR-55 TYPE II 24-HR TABULAR" => "TR55_TYPE_II_24HR_TABULAR",
                "NRCS TYPE II" => "TR55_TYPE_II_24HR_TABULAR",
                "TR-55 TYPE III 24-HR TABULAR" => "TR55_TYPE_III_24HR_TABULAR",
                "NRCS TYPE III" => "TR55_TYPE_III_24HR_TABULAR",
                _ => value
            };
        }

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

        private void ExecuteRunHydroModel()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            doc?.SendStringToExecute("RUN_HYDRO_MODEL\n", true, false, false);
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
                    RefreshLanduseLayers();
                else if (tabName == "Masks")
                    MasksViewModel.RefreshMasks();
                else if (tabName == "Hydrographs")
                    RefreshNetworkRoutes();
                else if (tabName == "Results")
                    ExecuteRefreshHydrologyResults();
            }
        }

        private void ExecuteRefreshHydrologyResults()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            HydrographResultRows.Clear();
            if (doc == null)
            {
                ResultsStatus = "No active drawing.";
                return;
            }

            string resultsPath = GetHydrologyResultsPath(doc.Database.Filename);
            if (!File.Exists(resultsPath))
            {
                ResultsStatus = $"No results.json found at {resultsPath}. Run RUN_HYDRO_MODEL first.";
                return;
            }

            try
            {
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                HydrologyResultsFile? results = JsonSerializer.Deserialize<HydrologyResultsFile>(File.ReadAllText(resultsPath), options);
                if (results == null || (results.Hydrographs.Count == 0 && results.HydrographPeakFlows.Count == 0))
                {
                    ResultsStatus = "Results file does not contain hydrograph rows.";
                    return;
                }

                var hydrographsById = results.Hydrographs
                    .Where(row => !string.IsNullOrWhiteSpace(row.Id))
                    .ToDictionary(row => row.Id, System.StringComparer.OrdinalIgnoreCase);
                var peakFlowGroupsById = results.HydrographPeakFlows
                    .Where(row => !string.IsNullOrWhiteSpace(row.HydrographId))
                    .GroupBy(row => row.HydrographId, System.StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => group.ToList(), System.StringComparer.OrdinalIgnoreCase);
                var hydrographIds = hydrographsById.Keys
                    .Concat(peakFlowGroupsById.Keys)
                    .Distinct(System.StringComparer.OrdinalIgnoreCase);

                foreach (string hydrographId in hydrographIds
                    .OrderBy(ParseHydrographNumber)
                    .ThenBy(id => id, System.StringComparer.OrdinalIgnoreCase))
                {
                    hydrographsById.TryGetValue(hydrographId, out HydrologyHydrographResult? hydrograph);
                    peakFlowGroupsById.TryGetValue(hydrographId, out List<HydrologyHydrographPeakFlow>? peakFlowRows);
                    HydrologyHydrographPeakFlow? firstPeak = peakFlowRows?.FirstOrDefault();
                    var peaksByAri = (peakFlowRows ?? new List<HydrologyHydrographPeakFlow>())
                        .Where(row => row.AriYears.HasValue)
                        .GroupBy(row => row.AriYears!.Value)
                        .ToDictionary(rowGroup => rowGroup.Key, rowGroup => rowGroup.First().PeakFlowCfs);

                    HydrographResultRows.Add(new HydrographResultRowItem
                    {
                        HydNo = hydrographId,
                        Type = hydrograph?.Type ?? firstPeak?.HydrographType ?? string.Empty,
                        InflowHyds = FormatInflows(hydrograph?.InflowIds?.Count > 0 == true ? hydrograph.InflowIds : firstPeak?.InflowIds),
                        Description = hydrograph?.Description ?? string.Empty,
                        Q1 = FormatPeak(peaksByAri, 1),
                        Q2 = FormatPeak(peaksByAri, 2),
                        Q5 = FormatPeak(peaksByAri, 5),
                        Q10 = FormatPeak(peaksByAri, 10),
                        Q25 = FormatPeak(peaksByAri, 25),
                        Q50 = FormatPeak(peaksByAri, 50),
                        Q100 = FormatPeak(peaksByAri, 100),
                        Status = string.IsNullOrWhiteSpace(firstPeak?.Status) ? hydrograph?.Status ?? string.Empty : firstPeak.Status
                    });
                }

                ResultsStatus = $"Loaded {HydrographResultRows.Count} hydrograph row(s) from {resultsPath}.";
            }
            catch (System.Exception ex)
            {
                ResultsStatus = $"Could not load hydrology results: {ex.Message}";
            }
        }

        private static string GetHydrologyResultsPath(string drawingPath)
        {
            if (!string.IsNullOrWhiteSpace(drawingPath))
            {
                string? drawingDirectory = Path.GetDirectoryName(drawingPath);
                if (!string.IsNullOrWhiteSpace(drawingDirectory))
                    return Path.Combine(drawingDirectory, "hydrology-run", "results.json");
            }

            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "C3DTools", "hydrology-run", "results.json");
        }

        private static string FormatPeak(Dictionary<int, double?> peaksByAri, int ariYears)
        {
            return peaksByAri.TryGetValue(ariYears, out double? value) && value.HasValue
                ? value.Value.ToString("0.###", CultureInfo.InvariantCulture)
                : "--";
        }

        private static string FormatInflows(List<string>? inflowIds)
        {
            return inflowIds == null || inflowIds.Count == 0 ? "--" : string.Join(", ", inflowIds);
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

        private static bool TryParseOptionalDouble(string value, out double? parsed, out string error)
        {
            parsed = null;
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(value))
                return true;

            if (double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double result))
            {
                parsed = result;
                return true;
            }

            error = $"'{value}' is not a valid number. ";
            return false;
        }

        private static string FormatNullableDouble(double? value)
        {
            return value.HasValue ? value.Value.ToString("0.##", CultureInfo.InvariantCulture) : string.Empty;
        }

        private static bool IsTerminalRoute(string downstreamId)
        {
            return downstreamId.Equals("OUTLET", System.StringComparison.OrdinalIgnoreCase) ||
                   downstreamId.Equals("OUTFALL", System.StringComparison.OrdinalIgnoreCase) ||
                   downstreamId.Equals("NONE", System.StringComparison.OrdinalIgnoreCase);
        }

        private static int ParseHydrographNumber(string id)
        {
            if (id.Length > 1 && id[0] == 'H' && int.TryParse(id.Substring(1), out int number))
                return number;
            return 0;
        }

        private static List<string> SplitHydrographIds(string value)
        {
            return value
                .Split(new[] { ',', ';' }, System.StringSplitOptions.RemoveEmptyEntries)
                .Select(item => item.Trim())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .ToList();
        }

        private static bool IsSourceHydrographType(string type)
        {
            return type.Equals("SCS", System.StringComparison.OrdinalIgnoreCase) ||
                   type.Equals("Rational", System.StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSingleInflowHydrographType(string type)
        {
            return type.Equals("Reach", System.StringComparison.OrdinalIgnoreCase) ||
                   type.Equals("Reservoir", System.StringComparison.OrdinalIgnoreCase);
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

    public class BasinRouteItem : INotifyPropertyChanged
    {
        private string _downstreamId = string.Empty;

        public BasinInfo Basin { get; set; } = new BasinInfo();
        public string BasinId { get; set; } = string.Empty;
        public string UpstreamDisplay { get; set; } = string.Empty;

        public string DownstreamId
        {
            get => _downstreamId;
            set
            {
                if (_downstreamId != value)
                {
                    _downstreamId = value;
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

    public class HydrographRowItem : INotifyPropertyChanged
    {
        private string _id = string.Empty;
        private string _type = "SCS";
        private string _basinId = string.Empty;
        private string _inflowIdsText = string.Empty;
        private string _downstreamId = string.Empty;
        private string _description = string.Empty;
        private string _pondName = string.Empty;
        private string _storageCurveText = string.Empty;
        private string _outletStructureText = string.Empty;
        private string _tailwaterElevation = "0.00";
        private string _exfiltrationRate = "0.00";
        private string _exfiltrationApplyTo = "Wetted Area";
        private string _exfiltrationExtractFromOutflowHyd = "No";
        private bool _suppressWeirDefaultUpdates;

        public HydrographRowItem()
        {
            StageStorageRows = new ObservableCollection<PondStageStorageRowItem>(CreateDefaultStageStorageRows());
            CulvertOrificeRows = new ObservableCollection<PondOutletTableRowItem>(CreateDefaultCulvertOrificeRows());
            WeirRows = new ObservableCollection<PondOutletTableRowItem>(CreateDefaultWeirRows());
            PondOutletTableRowItem? weirTypeRow = WeirRows.FirstOrDefault(row => row.IsWeirTypeRow);
            if (weirTypeRow != null)
                weirTypeRow.PropertyChanged += OnWeirTypeRowPropertyChanged;
        }

        public string Id
        {
            get => _id;
            set
            {
                if (_id != value)
                {
                    _id = value;
                    OnPropertyChanged();
                }
            }
        }

        public string Type
        {
            get => _type;
            set
            {
                if (_type != value)
                {
                    _type = value;
                    OnPropertyChanged();
                }
            }
        }

        public string BasinId
        {
            get => _basinId;
            set
            {
                if (_basinId != value)
                {
                    _basinId = value;
                    OnPropertyChanged();
                }
            }
        }

        public string InflowIdsText
        {
            get => _inflowIdsText;
            set
            {
                if (_inflowIdsText != value)
                {
                    _inflowIdsText = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(InflowIdsDisplay));
                }
            }
        }

        public string InflowIdsDisplay => string.IsNullOrWhiteSpace(InflowIdsText) ? "None" : InflowIdsText;

        public string DownstreamId
        {
            get => _downstreamId;
            set
            {
                if (_downstreamId != value)
                {
                    _downstreamId = value;
                    OnPropertyChanged();
                }
            }
        }

        public string Description
        {
            get => _description;
            set
            {
                if (_description != value)
                {
                    _description = value;
                    OnPropertyChanged();
                }
            }
        }

        public string PondName
        {
            get => _pondName;
            set
            {
                if (_pondName != value)
                {
                    _pondName = value;
                    OnPropertyChanged();
                }
            }
        }

        public string StorageCurveText
        {
            get => _storageCurveText;
            set
            {
                if (_storageCurveText != value)
                {
                    _storageCurveText = value;
                    OnPropertyChanged();
                }
            }
        }

        public string OutletStructureText
        {
            get => _outletStructureText;
            set
            {
                if (_outletStructureText != value)
                {
                    _outletStructureText = value;
                    OnPropertyChanged();
                }
            }
        }

    public ObservableCollection<PondStageStorageRowItem> StageStorageRows { get; }
        public ObservableCollection<PondOutletTableRowItem> CulvertOrificeRows { get; }
        public ObservableCollection<PondOutletTableRowItem> WeirRows { get; }

        public string TailwaterElevation
        {
            get => _tailwaterElevation;
            set
            {
                if (_tailwaterElevation != value)
                {
                    _tailwaterElevation = value;
                    OnPropertyChanged();
                }
            }
        }

        public string ExfiltrationRate
        {
            get => _exfiltrationRate;
            set
            {
                if (_exfiltrationRate != value)
                {
                    _exfiltrationRate = value;
                    OnPropertyChanged();
                }
            }
        }

        public string ExfiltrationApplyTo
        {
            get => _exfiltrationApplyTo;
            set
            {
                if (_exfiltrationApplyTo != value)
                {
                    _exfiltrationApplyTo = value;
                    OnPropertyChanged();
                }
            }
        }

        public string ExfiltrationExtractFromOutflowHyd
        {
            get => _exfiltrationExtractFromOutflowHyd;
            set
            {
                if (_exfiltrationExtractFromOutflowHyd != value)
                {
                    _exfiltrationExtractFromOutflowHyd = value;
                    OnPropertyChanged();
                }
            }
        }

        public static HydrographRowItem FromModel(HydrologyHydrograph row)
        {
            var item = new HydrographRowItem
            {
                Id = row.Id,
                Type = row.Type,
                BasinId = row.BasinId,
                InflowIdsText = string.Join(", ", row.InflowIds),
                DownstreamId = row.DownstreamId,
                Description = row.Description,
                PondName = GetParameter(row.Parameters, "pond_name"),
                StorageCurveText = GetParameter(row.Parameters, "storage_curve"),
                OutletStructureText = GetParameter(row.Parameters, "outlet_structure"),
                TailwaterElevation = GetParameter(row.Parameters, "tailwater_elevation", "0.00"),
                ExfiltrationRate = GetParameter(row.Parameters, "exfiltration_rate", "0.00"),
                ExfiltrationApplyTo = GetParameter(row.Parameters, "exfiltration_apply_to", "Wetted Area"),
                ExfiltrationExtractFromOutflowHyd = GetParameter(row.Parameters, "exfiltration_extract_from_outflow_hyd", "No")
            };
            item._suppressWeirDefaultUpdates = true;
            LoadStageStorageRows(item.StageStorageRows, row.Parameters);
            LoadOutletRows(item.CulvertOrificeRows, row.Parameters, "culverts_orifices");
            LoadOutletRows(item.WeirRows, row.Parameters, "weirs");
            item._suppressWeirDefaultUpdates = false;
            return item;
        }

        public HydrologyHydrograph ToModel()
        {
            var model = new HydrologyHydrograph
            {
                Id = Id.Trim(),
                Type = string.IsNullOrWhiteSpace(Type) ? "SCS" : Type.Trim(),
                BasinId = BasinId.Trim(),
                InflowIds = InflowIdsText
                    .Split(new[] { ',', ';' }, System.StringSplitOptions.RemoveEmptyEntries)
                    .Select(item => item.Trim())
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .ToList(),
                DownstreamId = DownstreamId.Trim(),
                Description = Description.Trim()
            };

            if (!string.IsNullOrWhiteSpace(PondName))
                model.Parameters["pond_name"] = PondName.Trim();
            if (!string.IsNullOrWhiteSpace(StorageCurveText))
                model.Parameters["storage_curve"] = StorageCurveText.Trim();
            if (!string.IsNullOrWhiteSpace(OutletStructureText))
                model.Parameters["outlet_structure"] = OutletStructureText.Trim();
            model.Parameters["tailwater_elevation"] = TailwaterElevation.Trim();
            model.Parameters["exfiltration_rate"] = ExfiltrationRate.Trim();
            model.Parameters["exfiltration_apply_to"] = ExfiltrationApplyTo.Trim();
            model.Parameters["exfiltration_extract_from_outflow_hyd"] = ExfiltrationExtractFromOutflowHyd.Trim();
            model.Parameters["stage_storage"] = StageStorageRows.Select(row => row.ToParameterDictionary()).ToList();
            model.Parameters["culverts_orifices"] = CulvertOrificeRows.Select(row => row.ToParameterDictionary()).ToList();
            model.Parameters["weirs"] = WeirRows.Select(row => row.ToParameterDictionary()).ToList();

            return model;
        }

        private static string GetParameter(Dictionary<string, object> parameters, string key, string defaultValue = "")
        {
            if (!parameters.TryGetValue(key, out object? value) || value == null)
                return defaultValue;

            return value is JsonElement element ? element.ToString() : value.ToString() ?? string.Empty;
        }

        private static List<PondStageStorageRowItem> CreateDefaultStageStorageRows()
        {
            return Enumerable.Range(1, 20)
                .Select(index => new PondStageStorageRowItem(index))
                .ToList();
        }

        private static List<PondOutletTableRowItem> CreateDefaultCulvertOrificeRows()
        {
            return new List<PondOutletTableRowItem>
            {
                new PondOutletTableRowItem("Rise (in)", string.Empty, string.Empty, string.Empty, "----", riserLabel: "Rise (in)"),
                new PondOutletTableRowItem("Span (in)", string.Empty, string.Empty, string.Empty, string.Empty, riserLabel: "Span (in)"),
                new PondOutletTableRowItem("No. Barrels", string.Empty, string.Empty, string.Empty, string.Empty, riserLabel: "No. Holes"),
                new PondOutletTableRowItem("Invert Elev. (ft)", string.Empty, string.Empty, string.Empty, string.Empty, riserLabel: "Invert Elev. (ft)"),
                new PondOutletTableRowItem("Length (ft)", string.Empty, string.Empty, string.Empty, string.Empty, riserLabel: "Height (ft)"),
                new PondOutletTableRowItem("Slope (%)", string.Empty, string.Empty, string.Empty, "----", riserLabel: "---------"),
                new PondOutletTableRowItem("N-Value", ".013", ".013", ".013", "----", riserLabel: "---------"),
                new PondOutletTableRowItem("Orifice Coeff.", ".60", ".60", ".60", ".60"),
                new PondOutletTableRowItem("Multi-Stage", "No", "No", "No", "No"),
                new PondOutletTableRowItem("Active", "Yes", "Yes", "Yes", "Yes")
            };
        }

        private static List<PondOutletTableRowItem> CreateDefaultWeirRows()
        {
            return new List<PondOutletTableRowItem>
            {
                new PondOutletTableRowItem("Weir Type", "Choose...", "Choose...", "Choose...", d: "Choose..."),
                new PondOutletTableRowItem("Crest Elev (ft)", string.Empty, string.Empty, string.Empty, d: string.Empty),
                new PondOutletTableRowItem("Crest Length (ft)", string.Empty, string.Empty, string.Empty, d: string.Empty),
                new PondOutletTableRowItem("Weir Coeff.", "3.33", "3.33", "3.33", d: "3.33"),
                new PondOutletTableRowItem("Multi-Stage", "No", "No", "No", d: "No"),
                new PondOutletTableRowItem("Active", "Yes", "Yes", "Yes", d: "Yes")
            };
        }

        private static void LoadOutletRows(ObservableCollection<PondOutletTableRowItem> targetRows, Dictionary<string, object> parameters, string key)
        {
            if (!parameters.TryGetValue(key, out object? value) || value is not JsonElement element || element.ValueKind != JsonValueKind.Array)
                return;

            foreach (JsonElement rowElement in element.EnumerateArray())
            {
                string label = rowElement.TryGetProperty("label", out JsonElement labelElement) ? labelElement.ToString() : string.Empty;
                PondOutletTableRowItem? targetRow = targetRows.FirstOrDefault(row => row.Label.Equals(label, System.StringComparison.OrdinalIgnoreCase));
                if (targetRow == null)
                    continue;

                targetRow.A = ReadOutletCell(rowElement, "a", targetRow.A);
                targetRow.B = ReadOutletCell(rowElement, "b", targetRow.B);
                targetRow.C = ReadOutletCell(rowElement, "c", targetRow.C);
                targetRow.Riser = ReadOutletCell(rowElement, "riser", targetRow.Riser);
                targetRow.D = ReadOutletCell(rowElement, "d", targetRow.D);
            }
        }

        private static string ReadOutletCell(JsonElement rowElement, string propertyName, string defaultValue)
        {
            return rowElement.TryGetProperty(propertyName, out JsonElement valueElement) ? valueElement.ToString() : defaultValue;
        }

        private static void LoadStageStorageRows(ObservableCollection<PondStageStorageRowItem> targetRows, Dictionary<string, object> parameters)
        {
            if (!parameters.TryGetValue("stage_storage", out object? value) || value is not JsonElement element || element.ValueKind != JsonValueKind.Array)
                return;

            int rowIndex = 0;
            foreach (JsonElement rowElement in element.EnumerateArray())
            {
                if (rowIndex >= targetRows.Count)
                    break;

                PondStageStorageRowItem targetRow = targetRows[rowIndex++];
                targetRow.Elevation = ReadOutletCell(rowElement, "elevation", targetRow.Elevation);
                targetRow.Volume = ReadOutletCell(rowElement, "volume", targetRow.Volume);
            }
        }

        private void OnWeirTypeRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_suppressWeirDefaultUpdates || sender is not PondOutletTableRowItem weirTypeRow)
                return;

            if (e.PropertyName == nameof(PondOutletTableRowItem.A))
                ApplyWeirTypeDefaults("a", weirTypeRow.A);
            else if (e.PropertyName == nameof(PondOutletTableRowItem.B))
                ApplyWeirTypeDefaults("b", weirTypeRow.B);
            else if (e.PropertyName == nameof(PondOutletTableRowItem.C))
                ApplyWeirTypeDefaults("c", weirTypeRow.C);
            else if (e.PropertyName == nameof(PondOutletTableRowItem.D))
                ApplyWeirTypeDefaults("d", weirTypeRow.D);
        }

        private void ApplyWeirTypeDefaults(string column, string weirType)
        {
            PondOutletTableRowItem? crestLengthRow = WeirRows.FirstOrDefault(row => row.Label.Equals("Crest Length (ft)", System.StringComparison.OrdinalIgnoreCase));
            PondOutletTableRowItem? coefficientRow = WeirRows.FirstOrDefault(row => row.Label.Equals("Weir Coeff.", System.StringComparison.OrdinalIgnoreCase));
            if (crestLengthRow == null || coefficientRow == null)
                return;

            string normalizedType = weirType.Trim();
            bool isVNotch = normalizedType.Contains("V-notch", System.StringComparison.OrdinalIgnoreCase);
            SetOutletColumn(crestLengthRow, column, isVNotch ? "n/a" : string.Empty);
            SetOutletColumn(coefficientRow, column, DefaultWeirCoefficient(normalizedType));
        }

        private static string DefaultWeirCoefficient(string weirType)
        {
            if (weirType.Equals("Riser", System.StringComparison.OrdinalIgnoreCase) ||
                weirType.Equals("Rectangular", System.StringComparison.OrdinalIgnoreCase) ||
                weirType.Equals("Cipoletti", System.StringComparison.OrdinalIgnoreCase))
                return "3.330";

            if (weirType.Equals("Broad Crested", System.StringComparison.OrdinalIgnoreCase) ||
                weirType.Equals("Broad crested", System.StringComparison.OrdinalIgnoreCase))
                return "2.600";

            int degrees = ParseVNotchDegrees(weirType);
            return degrees switch
            {
                5 => "0.111",
                10 => "0.222",
                15 => "0.334",
                20 => "0.448",
                25 => "0.563",
                30 => "0.681",
                35 => "0.801",
                40 => "0.924",
                45 => "1.052",
                50 => "1.184",
                55 => "1.322",
                60 => "1.466",
                65 => "1.618",
                70 => "1.779",
                75 => "1.949",
                80 => "2.131",
                85 => "2.327",
                90 => "2.540",
                95 => "2.772",
                100 => "3.027",
                105 => "3.310",
                110 => "3.627",
                115 => "3.987",
                120 => "4.399",
                _ => string.Empty
            };
        }

        private static int ParseVNotchDegrees(string weirType)
        {
            string degreeText = new string(weirType.TakeWhile(char.IsDigit).ToArray());
            return int.TryParse(degreeText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int degrees) ? degrees : 0;
        }

        private static void SetOutletColumn(PondOutletTableRowItem row, string column, string value)
        {
            switch (column)
            {
                case "a": row.A = value; break;
                case "b": row.B = value; break;
                case "c": row.C = value; break;
                case "d": row.D = value; break;
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class PondOutletTableRowItem : INotifyPropertyChanged
    {
        private static readonly ObservableCollection<string> WeirTypeOptionsWithRiser = new ObservableCollection<string>(BuildWeirTypeOptions(includeRiser: true));
        private static readonly ObservableCollection<string> WeirTypeOptionsWithoutRiser = new ObservableCollection<string>(BuildWeirTypeOptions(includeRiser: false));
        private string _a;
        private string _b;
        private string _c;
        private string _riser;
        private string _d;

        public PondOutletTableRowItem(string label, string a = "", string b = "", string c = "", string riser = "", string d = "", string? riserLabel = null)
        {
            Label = label;
            RiserLabel = riserLabel ?? label;
            _a = a;
            _b = b;
            _c = c;
            _riser = riser;
            _d = d;
        }

        public string Label { get; }
        public string RiserLabel { get; }
        public bool IsWeirTypeRow => Label.Equals("Weir Type", System.StringComparison.OrdinalIgnoreCase);
        public ObservableCollection<string> AWeirTypeOptions => WeirTypeOptionsWithRiser;
        public ObservableCollection<string> WeirTypeOptions => WeirTypeOptionsWithoutRiser;

        public string A
        {
            get => _a;
            set
            {
                if (_a != value)
                {
                    _a = value;
                    OnPropertyChanged();
                }
            }
        }

        public string B
        {
            get => _b;
            set
            {
                if (_b != value)
                {
                    _b = value;
                    OnPropertyChanged();
                }
            }
        }

        public string C
        {
            get => _c;
            set
            {
                if (_c != value)
                {
                    _c = value;
                    OnPropertyChanged();
                }
            }
        }

        public string Riser
        {
            get => _riser;
            set
            {
                if (_riser != value)
                {
                    _riser = value;
                    OnPropertyChanged();
                }
            }
        }

        public string D
        {
            get => _d;
            set
            {
                if (_d != value)
                {
                    _d = value;
                    OnPropertyChanged();
                }
            }
        }

        public Dictionary<string, string> ToParameterDictionary()
        {
            return new Dictionary<string, string>
            {
                ["label"] = Label,
                ["a"] = A,
                ["b"] = B,
                ["c"] = C,
                ["riser_label"] = RiserLabel,
                ["riser"] = Riser,
                ["d"] = D
            };
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private static IEnumerable<string> BuildWeirTypeOptions(bool includeRiser)
        {
            yield return "Choose...";

            if (includeRiser)
                yield return "Riser";

            yield return "Rectangular";
            yield return "Cipoletti";
            yield return "Broad Crested";

            for (int degrees = 5; degrees <= 120; degrees += 5)
                yield return $"{degrees}-deg V-notch";
        }

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class PondStageStorageRowItem : INotifyPropertyChanged
    {
        private string _elevation = string.Empty;
        private string _volume = string.Empty;

        public PondStageStorageRowItem(int rowNumber)
        {
            RowNumber = rowNumber;
        }

        public int RowNumber { get; }

        public string Elevation
        {
            get => _elevation;
            set
            {
                if (_elevation != value)
                {
                    _elevation = value;
                    OnPropertyChanged();
                }
            }
        }

        public string Volume
        {
            get => _volume;
            set
            {
                if (_volume != value)
                {
                    _volume = value;
                    OnPropertyChanged();
                }
            }
        }

        public Dictionary<string, string> ToParameterDictionary()
        {
            return new Dictionary<string, string>
            {
                ["row"] = RowNumber.ToString(CultureInfo.InvariantCulture),
                ["elevation"] = Elevation,
                ["volume"] = Volume
            };
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class HydrographInflowOptionItem : INotifyPropertyChanged
    {
        private bool _isSelected;

        public string Id { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;

        public string DisplayText => string.IsNullOrWhiteSpace(Description)
            ? $"{Id}  {Type}"
            : $"{Id}  {Type}  {Description}";

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

    public class HydrographBasinOptionItem
    {
        public string BasinId { get; set; } = string.Empty;
        public string DisplayText { get; set; } = string.Empty;

        public override string ToString() => DisplayText;
    }

    public class HydrographResultRowItem
    {
        public string HydNo { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string InflowHyds { get; set; } = string.Empty;
        public string Q1 { get; set; } = string.Empty;
        public string Q2 { get; set; } = string.Empty;
        public string Q5 { get; set; } = string.Empty;
        public string Q10 { get; set; } = string.Empty;
        public string Q25 { get; set; } = string.Empty;
        public string Q50 { get; set; } = string.Empty;
        public string Q100 { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
    }
}
