using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Valid;
using System.Collections.Generic;

namespace C3DTools.Helpers
{
    public static class GeometryValidator
    {
        public static bool ValidateAndFixGeometries(List<Geometry> geometries, List<Polyline> polylines, Editor ed, BlockTableRecord modelSpace, Transaction tr)
        {
            var invalidIndices = new List<int>();
            var invalidMessages = new List<string>();

            // Check each geometry for validity
            for (int i = 0; i < geometries.Count; i++)
            {
                var validator = new IsValidOp(geometries[i]);
                if (!validator.IsValid)
                {
                    invalidIndices.Add(i);
                    invalidMessages.Add(validator.ValidationError.Message);
                }
            }

            // If all geometries are valid, proceed
            if (invalidIndices.Count == 0)
                return true;

            // Report invalid geometries
            ed.WriteMessage($"\n{invalidIndices.Count} invalid geometr{(invalidIndices.Count == 1 ? "y" : "ies")} detected:");
            for (int i = 0; i < invalidIndices.Count; i++)
            {
                ed.WriteMessage($"\n  Polyline {invalidIndices[i] + 1}: {invalidMessages[i]}");
            }

            // Prompt user for action
            var fixOpts = new PromptKeywordOptions("\nFix invalid geometries automatically?");
            fixOpts.Keywords.Add("Yes");
            fixOpts.Keywords.Add("No");
            fixOpts.Keywords.Add("Cancel");
            fixOpts.Keywords.Default = "Yes";
            fixOpts.AllowNone = true;

            var fixResult = ed.GetKeywords(fixOpts);
            if (fixResult.Status != PromptStatus.OK && fixResult.Status != PromptStatus.None)
                return false;

            string choice = fixResult.Status == PromptStatus.None ? "Yes" : fixResult.StringResult;

            if (choice == "Cancel")
            {
                ed.WriteMessage("\nOperation cancelled.");
                return false;
            }

            if (choice == "No")
            {
                ed.WriteMessage("\nProceeding with invalid geometries (may produce unexpected results).");
                return true;
            }

            // Fix invalid geometries
            ed.WriteMessage("\nFixing geometries...");
            int fixedCount = 0;

            foreach (int idx in invalidIndices)
            {
                var fixedGeom = NetTopologySuite.Geometries.Utilities.GeometryFixer.Fix(geometries[idx]);
                if (fixedGeom != null && !fixedGeom.IsEmpty)
                {
                    geometries[idx] = fixedGeom;
                    fixedCount++;
                }
            }

            ed.WriteMessage($"\n{fixedCount} geometr{(fixedCount == 1 ? "y" : "ies")} fixed. Proceeding with operation.");
            return true;
        }
    }
}
