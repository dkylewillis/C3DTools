using Autodesk.AutoCAD.ApplicationServices;
using C3DTools.Infrastructure;
using C3DTools.Models;
using C3DTools.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace C3DTools.ViewModels
{
    /// <summary>
    /// ViewModel for the Masks tab in the Basin Tools palette.
    /// Manages a list of named mask polylines stored in the drawing's NOD.
    /// Each mask can optionally generate split geometry via SPLITBASINS.
    /// </summary>
    public class MasksPaletteViewModel : INotifyPropertyChanged
    {
        private readonly MaskService _maskService = new();
        private MaskViewModel? _selectedMask;

        // ── New mask form fields ─────────────────────────────────────────────────
        private string _newMaskName         = string.Empty;
        private string _newInsideLabel      = "IN";
        private string _newOutsideLabel     = "OUT";
        private string _newLayerPrefix      = "CALC-BASN";
        private bool   _newGenerateGeometry = false;
        private string _pendingHandle       = string.Empty;

        public MasksPaletteViewModel()
        {
            Masks = new ObservableCollection<MaskViewModel>();

            PickPolylineCommand    = new RelayCommand(ExecutePickPolyline);
            AddMaskCommand         = new RelayCommand(ExecuteAddMask,        CanAddMask);
            RemoveMaskCommand      = new RelayCommand(ExecuteRemoveMask,     () => SelectedMask != null);
            SaveMaskChangesCommand = new RelayCommand(ExecuteSaveMaskChanges, () => SelectedMask != null);
            RefreshCommand         = new RelayCommand(RefreshMasks);

            RefreshMasks();
        }

        // ── Collections ──────────────────────────────────────────────────────────

        public ObservableCollection<MaskViewModel> Masks { get; }

        // ── Selected mask ─────────────────────────────────────────────────────────

        public MaskViewModel? SelectedMask
        {
            get => _selectedMask;
            set
            {
                _selectedMask = value;
                OnPropertyChanged();
                ((RelayCommand)RemoveMaskCommand).RaiseCanExecuteChanged();
                ((RelayCommand)SaveMaskChangesCommand).RaiseCanExecuteChanged();
            }
        }

        // ── New mask form ─────────────────────────────────────────────────────────

        public string NewMaskName
        {
            get => _newMaskName;
            set
            {
                _newMaskName = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(NewInsideLayerPreview));
                OnPropertyChanged(nameof(NewOutsideLayerPreview));
                ((RelayCommand)AddMaskCommand).RaiseCanExecuteChanged();
            }
        }

        public string NewInsideLabel
        {
            get => _newInsideLabel;
            set
            {
                _newInsideLabel = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(NewInsideLayerPreview));
            }
        }

        public string NewOutsideLabel
        {
            get => _newOutsideLabel;
            set
            {
                _newOutsideLabel = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(NewOutsideLayerPreview));
            }
        }

        public string NewLayerPrefix
        {
            get => _newLayerPrefix;
            set
            {
                _newLayerPrefix = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(NewInsideLayerPreview));
                OnPropertyChanged(nameof(NewOutsideLayerPreview));
            }
        }

        public bool NewGenerateGeometry
        {
            get => _newGenerateGeometry;
            set { _newGenerateGeometry = value; OnPropertyChanged(); }
        }

        /// <summary>Live preview of the resulting layer names as the user types.</summary>
        public string NewInsideLayerPreview => string.IsNullOrWhiteSpace(_newMaskName)
            ? "—"
            : $"{_newLayerPrefix}-{_newMaskName}_{_newInsideLabel}";

        public string NewOutsideLayerPreview => string.IsNullOrWhiteSpace(_newMaskName)
            ? "—"
            : $"{_newLayerPrefix}-{_newMaskName}_{_newOutsideLabel}";

        /// <summary>Human-readable status of the currently pending pick.</summary>
        public string PendingHandleDisplay => string.IsNullOrEmpty(_pendingHandle)
            ? "No polyline picked"
            : $"Handle: {_pendingHandle}";

        // ── Commands ──────────────────────────────────────────────────────────────

        public ICommand PickPolylineCommand    { get; }
        public ICommand AddMaskCommand         { get; }
        public ICommand RemoveMaskCommand      { get; }
        public ICommand SaveMaskChangesCommand { get; }
        public ICommand RefreshCommand         { get; }

        // ── Command implementations ───────────────────────────────────────────────

        /// <summary>
        /// Fires MASKPICK on the CAD side (picks a closed polyline) then reads the
        /// resulting handle back via Document.CommandEnded once the command finishes.
        /// </summary>
        private void ExecutePickPolyline()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            MaskPickState.PendingHandle = null;

            doc.CommandEnded     += OnPickCommandEnded;
            doc.CommandCancelled += OnPickCommandCancelled;
            doc.CommandFailed    += OnPickCommandCancelled;

            doc.SendStringToExecute("MASKPICK\n", true, false, false);
        }

        private void OnPickCommandEnded(object sender, Autodesk.AutoCAD.ApplicationServices.CommandEventArgs e)
        {
            if (!string.Equals(e.GlobalCommandName, "MASKPICK", System.StringComparison.OrdinalIgnoreCase))
                return;

            UnsubscribePickEvents((Autodesk.AutoCAD.ApplicationServices.Document)sender);

            if (MaskPickState.PendingHandle != null)
            {
                _pendingHandle = MaskPickState.PendingHandle;
                MaskPickState.PendingHandle = null;
                OnPropertyChanged(nameof(PendingHandleDisplay));
                ((RelayCommand)AddMaskCommand).RaiseCanExecuteChanged();
            }
        }

        private void OnPickCommandCancelled(object sender, Autodesk.AutoCAD.ApplicationServices.CommandEventArgs e)
        {
            if (!string.Equals(e.GlobalCommandName, "MASKPICK", System.StringComparison.OrdinalIgnoreCase))
                return;

            UnsubscribePickEvents((Autodesk.AutoCAD.ApplicationServices.Document)sender);
        }

        private void UnsubscribePickEvents(Autodesk.AutoCAD.ApplicationServices.Document doc)
        {
            doc.CommandEnded     -= OnPickCommandEnded;
            doc.CommandCancelled -= OnPickCommandCancelled;
            doc.CommandFailed    -= OnPickCommandCancelled;
        }

        private bool CanAddMask()
            => !string.IsNullOrWhiteSpace(_newMaskName) && !string.IsNullOrWhiteSpace(_pendingHandle);

        private void ExecuteAddMask()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            var mask = new MaskDefinition
            {
                Name             = _newMaskName.Trim(),
                PolylineHandle   = _pendingHandle,
                InsideLabel      = _newInsideLabel,
                OutsideLabel     = _newOutsideLabel,
                LayerPrefix      = _newLayerPrefix,
                GenerateGeometry = _newGenerateGeometry
            };

            using (doc.LockDocument())
                _maskService.SaveMask(doc.Database, mask);

            // Reset form
            NewMaskName         = string.Empty;
            NewInsideLabel      = "IN";
            NewOutsideLabel     = "OUT";
            NewLayerPrefix      = "CALC-BASN";
            NewGenerateGeometry = false;
            _pendingHandle      = string.Empty;
            OnPropertyChanged(nameof(PendingHandleDisplay));

            RefreshMasks();
        }

        private void ExecuteRemoveMask()
        {
            if (SelectedMask == null) return;
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            using (doc.LockDocument())
                _maskService.DeleteMask(doc.Database, SelectedMask.Name);

            RefreshMasks();
        }

        /// <summary>Saves in-place edits made to the currently selected mask row.</summary>
        private void ExecuteSaveMaskChanges()
        {
            if (SelectedMask == null) return;
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            using (doc.LockDocument())
                _maskService.SaveMask(doc.Database, SelectedMask.Model);
        }

        public void RefreshMasks()
        {
            Masks.Clear();
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var db = doc.Database;

            var allMasks = _maskService.GetMasks(db);
            var staleMasks = new List<string>();

            foreach (var mask in allMasks)
            {
                if (!_maskService.IsPolylineValid(db, mask.PolylineHandle))
                {
                    staleMasks.Add(mask.Name);
                }
                else
                {
                    Masks.Add(new MaskViewModel(mask));
                }
            }

            if (staleMasks.Count > 0)
            {
                using (doc.LockDocument())
                {
                    foreach (var name in staleMasks)
                        _maskService.DeleteMask(db, name);
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
