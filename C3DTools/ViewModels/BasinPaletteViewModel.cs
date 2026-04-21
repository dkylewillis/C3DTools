using Autodesk.AutoCAD.ApplicationServices;
using C3DTools.Models;
using C3DTools.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace C3DTools.ViewModels
{
    /// <summary>
    /// ViewModel for the Basin Tools Palette.
    /// </summary>
    public class BasinPaletteViewModel : INotifyPropertyChanged
    {
        private readonly BasinDataService _dataService;
        private BasinInfo? _selectedBasin;
        private BasinInfo? _selectedTaggedBasin;
        private BasinInfo? _selectedUntaggedBasin;
        private string _newBasinId = string.Empty;
        private string _selectedBoundary = "None";
        private string _selectedDevelopment = string.Empty;
        private string _selectedLayer = "All Layers";
        private List<BasinInfo> _allUntaggedBasins = new List<BasinInfo>();

        public BasinPaletteViewModel()
        {
            _dataService = new BasinDataService();
            TaggedBasins = new ObservableCollection<BasinInfo>();
            UntaggedBasins = new ObservableCollection<BasinInfo>();
            AvailableLayers = new ObservableCollection<string>();
            BoundaryOptions = new ObservableCollection<string> { "", "ONSITE", "OFFSITE" };
            DevelopmentOptions = new ObservableCollection<string> { "", "Pre", "Post" };

            TagBasinCommand = new RelayCommand(ExecuteTagBasin, CanExecuteTagBasin);
            SelectBasinItemCommand = new RelayCommand<BasinInfo>(ExecuteSelectBasinItem);
            RefreshCommand = new RelayCommand(ExecuteRefresh);

            RefreshData();
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public ObservableCollection<BasinInfo> TaggedBasins { get; }
        public ObservableCollection<BasinInfo> UntaggedBasins { get; }
        public ObservableCollection<string> AvailableLayers { get; }
        public ObservableCollection<string> BoundaryOptions { get; }
        public ObservableCollection<string> DevelopmentOptions { get; }

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
                _selectedTaggedBasin = value;
                OnPropertyChanged();
            }
        }

        public BasinInfo? SelectedUntaggedBasin
        {
            get => _selectedUntaggedBasin;
            set
            {
                _selectedUntaggedBasin = value;
                OnPropertyChanged();
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
}
