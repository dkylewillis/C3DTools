using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Windows;
using C3DTools.UI;
using System;

namespace C3DTools.Commands
{
    public class BasinPaletteCommand
    {
        private static PaletteSet? _paletteSet;
        private static BasinPaletteView? _paletteView;

        [CommandMethod("BASINPALETTE")]
        public void ShowBasinPalette()
        {
            if (_paletteSet == null)
            {
                _paletteSet = new PaletteSet("Basin Tools", new Guid("7A8F2E3D-4B1C-4F6E-9A2D-8C3E5F7A9B1D"));
                _paletteSet.Style = PaletteSetStyles.ShowCloseButton 
                                  | PaletteSetStyles.ShowAutoHideButton 
                                  | PaletteSetStyles.Snappable;
                _paletteSet.MinimumSize = new System.Drawing.Size(280, 400);
                _paletteSet.DockEnabled = DockSides.Left | DockSides.Right;

                _paletteView = new BasinPaletteView();
                _paletteSet.AddVisual("Basins", _paletteView);

                // Hook up events to refresh data
                Application.DocumentManager.DocumentActivated += OnDocumentActivated;
                Application.DocumentManager.DocumentToBeDestroyed += OnDocumentToBeDestroyed;

                // Hook selection changed event
                var doc = Application.DocumentManager.MdiActiveDocument;
                if (doc != null)
                {
                    doc.ImpliedSelectionChanged += OnImpliedSelectionChanged;
                }

                _paletteSet.StateChanged += OnPaletteStateChanged;
            }

            _paletteSet.Visible = !_paletteSet.Visible;

            // Refresh data when shown
            if (_paletteSet.Visible && _paletteView != null)
            {
                _paletteView.ViewModel.RefreshData();
            }
        }

        private static void OnDocumentActivated(object sender, DocumentCollectionEventArgs e)
        {
            if (_paletteView != null && _paletteSet?.Visible == true)
            {
                _paletteView.ViewModel.RefreshData();
            }

            // Subscribe to new document's selection events
            if (e.Document != null)
            {
                e.Document.ImpliedSelectionChanged += OnImpliedSelectionChanged;
            }
        }

        private static void OnDocumentToBeDestroyed(object sender, DocumentCollectionEventArgs e)
        {
            // Unsubscribe from document events
            if (e.Document != null)
            {
                e.Document.ImpliedSelectionChanged -= OnImpliedSelectionChanged;
            }
        }

        private static void OnImpliedSelectionChanged(object sender, EventArgs e)
        {
            if (_paletteView != null && _paletteSet?.Visible == true)
            {
                _paletteView.ViewModel.UpdateSelection();
            }
        }

        private static void OnPaletteStateChanged(object sender, PaletteSetStateEventArgs e)
        {
            // Refresh when palette becomes visible
            if (e.NewState == StateEventIndex.Show && _paletteView != null)
            {
                _paletteView.ViewModel.RefreshData();
            }
        }
    }
}
