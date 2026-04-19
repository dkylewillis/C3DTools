using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;

namespace C3DTools.Helpers
{
    public static class PolylineHelper
    {
        public static SelectionFilter GetPolylineFilter()
        {
            return new SelectionFilter(new[]
            {
                new TypedValue((int)DxfCode.Operator, "<OR"),
                new TypedValue((int)DxfCode.Start, "LWPOLYLINE"),
                new TypedValue((int)DxfCode.Start, "POLYLINE"),
                new TypedValue((int)DxfCode.Operator, "OR>")
            });
        }
    }
}
