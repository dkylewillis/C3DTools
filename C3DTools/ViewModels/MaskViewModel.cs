using C3DTools.Models;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace C3DTools.ViewModels
{
    /// <summary>
    /// Wraps a <see cref="MaskDefinition"/> for display and editing in the Masks palette tab.
    /// </summary>
    public class MaskViewModel : INotifyPropertyChanged
    {
        private readonly MaskDefinition _model;

        public MaskViewModel(MaskDefinition model)
        {
            _model = model;
        }

        public MaskDefinition Model => _model;

        public string Name
        {
            get => _model.Name;
            set
            {
                _model.Name = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(InsideLayerName));
                OnPropertyChanged(nameof(OutsideLayerName));
                OnPropertyChanged(nameof(DisplaySummary));
            }
        }

        public string PolylineHandle => _model.PolylineHandle;

        public string InsideLabel
        {
            get => _model.InsideLabel;
            set
            {
                _model.InsideLabel = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(InsideLayerName));
                OnPropertyChanged(nameof(DisplaySummary));
            }
        }

        public string OutsideLabel
        {
            get => _model.OutsideLabel;
            set
            {
                _model.OutsideLabel = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(OutsideLayerName));
                OnPropertyChanged(nameof(DisplaySummary));
            }
        }

        public string LayerPrefix
        {
            get => _model.LayerPrefix;
            set
            {
                _model.LayerPrefix = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(InsideLayerName));
                OnPropertyChanged(nameof(OutsideLayerName));
                OnPropertyChanged(nameof(DisplaySummary));
            }
        }

        public bool GenerateGeometry
        {
            get => _model.GenerateGeometry;
            set { _model.GenerateGeometry = value; OnPropertyChanged(); }
        }

        /// <summary>e.g. "CALC-BASN-SITE_BNDY_IN"</summary>
        public string InsideLayerName  => _model.InsideLayerName;

        /// <summary>e.g. "CALC-BASN-SITE_BNDY_OUT"</summary>
        public string OutsideLayerName => _model.OutsideLayerName;

        public string DisplaySummary => $"{InsideLayerName}  /  {OutsideLayerName}";

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
