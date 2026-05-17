using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using C3DTools.Helpers;
using C3DTools.Models;
using C3DTools.Services;
using System.IO;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace C3DTools.Commands
{
    public class HydrologyCommands
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private const string AppNameBasin = "C3DTools_Basin";
        private const string AppNameHydroInputs = "C3DTools_HydroInputs";
        private const string AppNameHydroResults = "C3DTools_HydroResults";
        private const string AppNameHydroResultLabels = "C3DTools_HydroResultLabels";

        [CommandMethod("EXPORT_HYDRO_MODEL")]
        public void ExportHydroModel()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;

            string? modelPath = PromptForModelPath(ed, doc.Database.Filename);
            if (string.IsNullOrWhiteSpace(modelPath))
                return;

            try
            {
                WriteModel(doc, modelPath);
                ed.WriteMessage($"\nHydrology model exported to: {modelPath}");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nEXPORT_HYDRO_MODEL failed: {ex.Message}");
            }
        }

        [CommandMethod("RUN_HYDRO_MODEL")]
        public void RunHydroModel()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;

            string runDirectory = GetHydrologyRunDirectory(doc.Database.Filename);
            string modelPath = Path.Combine(runDirectory, "basin_model.json");
            string resultsPath = Path.Combine(runDirectory, "results.json");
            string excelPath = Path.Combine(runDirectory, "report.xlsx");
            string validationPath = Path.Combine(runDirectory, "validation_report.json");
            BasinSettings settings = new SettingsResolver().Resolve(doc.Database);
            string stormDistribution = string.IsNullOrWhiteSpace(settings.StormDistribution)
                ? "ATLAS14_ALTERNATING_BLOCK"
                : settings.StormDistribution;
            int hydrographTimeStepMinutes = settings.HydrographTimeStepMinutes > 0
                ? settings.HydrographTimeStepMinutes
                : 2;
            ed.WriteMessage($"\nHydrology outputs will be written to: {runDirectory}");
            ed.WriteMessage($"\nStorm distribution: {stormDistribution}");
            ed.WriteMessage($"\nHydrograph time step: {hydrographTimeStepMinutes} min");
            string? atlas14DepthsPath = PromptForAtlas14DepthPath(ed, doc.Database.Filename);
            string atlas14Duration = "24-hr";
            if (!string.IsNullOrWhiteSpace(atlas14DepthsPath))
            {
                string? promptedDuration = PromptForAtlas14Duration(ed);
                if (promptedDuration == null)
                    return;

                atlas14Duration = promptedDuration;
            }

            try
            {
                Directory.CreateDirectory(runDirectory);
                WriteModel(doc, modelPath);
                ed.WriteMessage($"\nHydrology model exported to: {modelPath}");
                HydrologyRunResult result = new HydrologyEngineRunner().Run(modelPath, resultsPath, excelPath, validationPath, atlas14DepthsPath, atlas14Duration, stormDistribution, hydrographTimeStepMinutes);

                if (!string.IsNullOrWhiteSpace(result.Output))
                    ed.WriteMessage($"\n{result.Output.Trim()}");

                if (result.ExitCode == 0)
                {
                    ed.WriteMessage($"\nHydrology results written to: {result.ResultsPath}");
                    ed.WriteMessage($"\nHydrology validation report written to: {result.ValidationPath}");
                    if (!string.IsNullOrWhiteSpace(result.ExcelPath))
                        ed.WriteMessage($"\nHydrology report written to: {result.ExcelPath}");
                    if (!string.IsNullOrWhiteSpace(atlas14DepthsPath))
                        ed.WriteMessage($"\nAtlas 14 rainfall source: {atlas14DepthsPath} ({atlas14Duration})");
                    ed.WriteMessage($"\nStorm distribution: {stormDistribution}");
                    ed.WriteMessage($"\nHydrograph time step: {hydrographTimeStepMinutes} min");
                }
                else
                {
                    ed.WriteMessage($"\nRUN_HYDRO_MODEL failed with exit code {result.ExitCode}.");
                    if (!string.IsNullOrWhiteSpace(result.Error))
                        ed.WriteMessage($"\n{result.Error.Trim()}");
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nRUN_HYDRO_MODEL failed: {ex.Message}");
            }
        }

        [CommandMethod("IMPORT_HYDRO_RESULTS")]
        public void ImportHydroResults()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;

            string? resultsPath = PromptForResultsPath(ed, doc.Database.Filename);
            if (string.IsNullOrWhiteSpace(resultsPath))
                return;

            try
            {
                HydrologyResultsFile? results = JsonSerializer.Deserialize<HydrologyResultsFile>(File.ReadAllText(resultsPath), JsonOptions);
                if (results == null || results.Basins.Count == 0)
                {
                    ed.WriteMessage("\nNo basin results found in the selected file.");
                    return;
                }

                HydrologyImportSummary summary = ImportResultsToDrawing(doc, results, resultsPath);
                ed.WriteMessage($"\nImported hydrology results for {summary.UpdatedCount} basin object(s).");
                if (summary.UnmatchedCount > 0)
                    ed.WriteMessage($"\n{summary.UnmatchedCount} basin result(s) could not be matched to a drawing polyline.");
                if (summary.AmbiguousCount > 0)
                    ed.WriteMessage($"\n{summary.AmbiguousCount} basin result(s) matched multiple polylines by id and were skipped.");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nIMPORT_HYDRO_RESULTS failed: {ex.Message}");
            }
        }

        [CommandMethod("LABEL_HYDRO_RESULTS")]
        public void LabelHydroResults()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;

            try
            {
                HydrologyLabelSummary summary = LabelImportedResults(doc);
                ed.WriteMessage($"\nCreated {summary.CreatedCount} hydrology result label(s).");
                if (summary.RemovedCount > 0)
                    ed.WriteMessage($"\nRemoved {summary.RemovedCount} previous hydrology result label(s).");
                if (summary.SkippedCount > 0)
                    ed.WriteMessage($"\nSkipped {summary.SkippedCount} basin object(s) without imported hydrology results.");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nLABEL_HYDRO_RESULTS failed: {ex.Message}");
            }
        }

        [CommandMethod("SET_BASIN_TC")]
        public void SetBasinTc()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;

            PromptEntityResult entityResult = PromptForBasinPolyline(ed, "\nSelect basin polyline for manual Tc: ");
            if (entityResult.Status != PromptStatus.OK)
                return;

            var tcOptions = new PromptDoubleOptions("\nEnter Tc in minutes: ")
            {
                AllowNegative = false,
                AllowZero = false,
                AllowNone = false
            };
            PromptDoubleResult tcResult = ed.GetDouble(tcOptions);
            if (tcResult.Status != PromptStatus.OK)
                return;

            try
            {
                UpdateHydrologyInputs(doc, entityResult.ObjectId, tcResult.Value, null);
                ed.WriteMessage($"\nStored Tc = {tcResult.Value:0.##} min on selected basin.");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nSET_BASIN_TC failed: {ex.Message}");
            }
        }

        [CommandMethod("SET_BASIN_ROUTE")]
        public void SetBasinRoute()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;

            PromptEntityResult entityResult = PromptForBasinPolyline(ed, "\nSelect upstream basin polyline: ");
            if (entityResult.Status != PromptStatus.OK)
                return;

            var routeOptions = new PromptStringOptions("\nEnter downstream basin ID or OUTLET: ")
            {
                AllowSpaces = false
            };
            PromptResult routeResult = ed.GetString(routeOptions);
            if (routeResult.Status != PromptStatus.OK)
                return;

            string downstreamId = string.IsNullOrWhiteSpace(routeResult.StringResult)
                ? "OUTLET"
                : routeResult.StringResult.Trim();

            try
            {
                UpdateHydrologyInputs(doc, entityResult.ObjectId, null, downstreamId);
                ed.WriteMessage($"\nStored downstream route = {downstreamId} on selected basin.");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nSET_BASIN_ROUTE failed: {ex.Message}");
            }
        }

        private static void WriteModel(Document doc, string modelPath)
        {
            var model = new HydrologyModelExporter().Export(doc);
            string? directory = Path.GetDirectoryName(modelPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(modelPath, JsonSerializer.Serialize(model, JsonOptions));
        }

        private static string? PromptForModelPath(Editor ed, string drawingPath)
        {
            string initialDirectory = string.IsNullOrWhiteSpace(drawingPath)
                ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                : Path.GetDirectoryName(drawingPath) ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

            var options = new PromptSaveFileOptions("\nSave basin_model.json as")
            {
                DialogCaption = "Export Hydrology Model",
                Filter = "JSON (*.json)|*.json|All files (*.*)|*.*",
                InitialDirectory = initialDirectory,
                InitialFileName = "basin_model.json"
            };

            PromptFileNameResult result = ed.GetFileNameForSave(options);
            return result.Status == PromptStatus.OK ? result.StringResult : null;
        }

        private static string? PromptForAtlas14DepthPath(Editor ed, string drawingPath)
        {
            string initialDirectory = string.IsNullOrWhiteSpace(drawingPath)
                ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                : Path.GetDirectoryName(drawingPath) ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

            var options = new PromptOpenFileOptions("\nOptional rainfall input: select an existing NOAA Atlas 14 precipitation depth CSV, or press Esc to use storms already in the model")
            {
                DialogCaption = "Optional Atlas 14 Rainfall Input - Open Existing CSV",
                Filter = "CSV (*.csv)|*.csv|All files (*.*)|*.*",
                InitialDirectory = initialDirectory,
                InitialFileName = "PF_Depth_English_PDS.csv"
            };

            PromptFileNameResult result = ed.GetFileNameForOpen(options);
            return result.Status == PromptStatus.OK ? result.StringResult : null;
        }

        private static string? PromptForAtlas14Duration(Editor ed)
        {
            var options = new PromptStringOptions("\nEnter Atlas 14 duration label <24-hr>: ")
            {
                AllowSpaces = false
            };

            PromptResult result = ed.GetString(options);
            if (result.Status == PromptStatus.Cancel)
                return null;
            if (result.Status == PromptStatus.None || string.IsNullOrWhiteSpace(result.StringResult))
                return "24-hr";

            return result.StringResult.Trim();
        }

        private static string GetHydrologyRunDirectory(string drawingPath)
        {
            if (!string.IsNullOrWhiteSpace(drawingPath))
            {
                string? drawingDirectory = Path.GetDirectoryName(drawingPath);
                if (!string.IsNullOrWhiteSpace(drawingDirectory))
                    return Path.Combine(drawingDirectory, "hydrology-run");
            }

            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "C3DTools", "hydrology-run");
        }

        private static string? PromptForResultsPath(Editor ed, string drawingPath)
        {
            string initialDirectory = string.IsNullOrWhiteSpace(drawingPath)
                ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                : Path.GetDirectoryName(drawingPath) ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

            var options = new PromptOpenFileOptions("\nSelect results.json to import")
            {
                DialogCaption = "Import Hydrology Results",
                Filter = "JSON (*.json)|*.json|All files (*.*)|*.*",
                InitialDirectory = initialDirectory,
                InitialFileName = "results.json"
            };

            PromptFileNameResult result = ed.GetFileNameForOpen(options);
            return result.Status == PromptStatus.OK ? result.StringResult : null;
        }

        private static HydrologyImportSummary ImportResultsToDrawing(Document doc, HydrologyResultsFile results, string resultsPath)
        {
            var summary = new HydrologyImportSummary();

            using (DocumentLock docLock = doc.LockDocument())
            using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
            {
                EnsureRegApp(doc.Database, tr, AppNameHydroResults);

                BlockTable bt = (BlockTable)tr.GetObject(doc.Database.BlockTableId, OpenMode.ForRead);
                BlockTableRecord ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                var basinsByHandle = new Dictionary<string, ObjectId>(StringComparer.OrdinalIgnoreCase);
                var basinsById = new Dictionary<string, List<ObjectId>>(StringComparer.OrdinalIgnoreCase);

                foreach (ObjectId oid in ms)
                {
                    if (!oid.ObjectClass.IsDerivedFrom(RXObject.GetClass(typeof(Polyline))))
                        continue;

                    Polyline? pline = tr.GetObject(oid, OpenMode.ForRead) as Polyline;
                    if (pline == null)
                        continue;

                    string basinId = ReadBasinId(pline);
                    if (string.IsNullOrWhiteSpace(basinId))
                        continue;

                    basinsByHandle[oid.Handle.ToString()] = oid;
                    if (!basinsById.TryGetValue(basinId, out List<ObjectId>? ids))
                    {
                        ids = new List<ObjectId>();
                        basinsById[basinId] = ids;
                    }
                    ids.Add(oid);
                }

                foreach (HydrologyBasinResult basinResult in results.Basins)
                {
                    ObjectId? targetId = ResolveTargetObjectId(basinResult, basinsByHandle, basinsById, summary);
                    if (targetId == null)
                        continue;

                    Polyline pline = (Polyline)tr.GetObject(targetId.Value, OpenMode.ForWrite);
                    using ResultBuffer resultBuffer = BuildMergedXData(pline, basinResult, resultsPath);
                    pline.XData = resultBuffer;
                    summary.UpdatedCount++;
                }

                tr.Commit();
            }

            return summary;
        }

        private static void UpdateHydrologyInputs(Document doc, ObjectId basinObjectId, double? tcMinutes, string? downstreamId)
        {
            using (DocumentLock docLock = doc.LockDocument())
            using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
            {
                EnsureRegApp(doc.Database, tr, AppNameHydroInputs);

                Polyline pline = (Polyline)tr.GetObject(basinObjectId, OpenMode.ForWrite);
                HydrologyStoredInputs existing = ReadHydrologyInputs(pline);
                double? nextTc = tcMinutes ?? existing.TcMinutes;
                string nextDownstream = downstreamId ?? existing.DownstreamId;

                using ResultBuffer resultBuffer = BuildMergedHydrologyInputsXData(pline, nextTc, nextDownstream, existing.CurveNumber);
                pline.XData = resultBuffer;
                tr.Commit();
            }
        }

        private static HydrologyLabelSummary LabelImportedResults(Document doc)
        {
            var summary = new HydrologyLabelSummary();
            Database db = doc.Database;

            using (DocumentLock docLock = doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                EnsureRegApp(db, tr, AppNameHydroResultLabels);

                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                summary.RemovedCount = RemoveExistingHydrologyLabels(ms, tr);
                var labelsToCreate = new List<MText>();

                foreach (ObjectId oid in ms)
                {
                    if (!oid.ObjectClass.IsDerivedFrom(RXObject.GetClass(typeof(Polyline))))
                        continue;

                    Polyline? pline = tr.GetObject(oid, OpenMode.ForRead) as Polyline;
                    if (pline == null)
                        continue;

                    HydrologyStoredResult? result = ReadHydrologyResult(pline);
                    if (result == null)
                    {
                        if (!string.IsNullOrWhiteSpace(ReadBasinId(pline)))
                            summary.SkippedCount++;
                        continue;
                    }

                    MText label = CreateHydrologyLabel(db, pline, result);
                    labelsToCreate.Add(label);
                }

                foreach (MText label in labelsToCreate)
                {
                    ms.AppendEntity(label);
                    tr.AddNewlyCreatedDBObject(label, true);
                }

                summary.CreatedCount = labelsToCreate.Count;

                tr.Commit();
            }

            return summary;
        }

        private static int RemoveExistingHydrologyLabels(BlockTableRecord ms, Transaction tr)
        {
            var labelsToErase = new List<ObjectId>();

            foreach (ObjectId oid in ms)
            {
                if (!oid.ObjectClass.IsDerivedFrom(RXObject.GetClass(typeof(MText))) &&
                    !oid.ObjectClass.IsDerivedFrom(RXObject.GetClass(typeof(DBText))))
                    continue;

                Entity? entity = tr.GetObject(oid, OpenMode.ForRead) as Entity;
                if (entity == null)
                    continue;

                ResultBuffer? rb = entity.GetXDataForApplication(AppNameHydroResultLabels);
                if (rb == null)
                    continue;

                rb.Dispose();
                labelsToErase.Add(oid);
            }

            foreach (ObjectId oid in labelsToErase)
            {
                Entity entity = (Entity)tr.GetObject(oid, OpenMode.ForWrite);
                entity.Erase();
            }

            return labelsToErase.Count;
        }

        private static MText CreateHydrologyLabel(Database db, Polyline pline, HydrologyStoredResult result)
        {
            double textHeight = db.Textsize > 0 ? db.Textsize : 2.5;
            var label = new MText
            {
                Location = GetLabelLocation(pline),
                TextHeight = textHeight,
                Attachment = AttachmentPoint.MiddleCenter,
                Layer = pline.Layer,
                Contents = BuildHydrologyLabelContents(result)
            };

            using ResultBuffer rb = new ResultBuffer(
                new TypedValue((int)DxfCode.ExtendedDataRegAppName, AppNameHydroResultLabels),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, "1"),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, ToXDataString(result.BasinId)),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, ToXDataString(result.Handle))
            );
            label.XData = rb;

            return label;
        }

        private static string BuildHydrologyLabelContents(HydrologyStoredResult result)
        {
            var parts = new List<string>
            {
                EscapeMText(result.BasinId),
                $"Area: {FormatDisplayNumber(result.AreaAcres)} ac",
                $"CN: {FormatDisplayNumber(result.CurveNumber)}",
                $"Tc: {FormatDisplayNumber(result.TcMinutes)} min",
                $"Imp: {FormatDisplayNumber(result.ImperviousPercent)}%",
                $"Qlocal: {FormatDisplayNumber(result.LocalPeakCfs)} cfs",
                $"Qrouted: {FormatDisplayNumber(result.RoutedPeakCfs)} cfs"
            };

            if (!string.IsNullOrWhiteSpace(result.DownstreamId))
                parts.Add($"To: {EscapeMText(result.DownstreamId)}");

            if (!string.Equals(result.Status, "ok", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(result.Status))
                parts.Add(EscapeMText(result.Status));

            return string.Join("\\P", parts);
        }

        private static Point3d GetLabelLocation(Polyline pline)
        {
            NetTopologySuite.Geometries.Geometry? geometry = GeometryConverter.PolylineToNts(pline);
            if (geometry != null && !geometry.IsEmpty)
            {
                NetTopologySuite.Geometries.Coordinate coordinate = geometry.Centroid.Coordinate;
                return new Point3d(coordinate.X, coordinate.Y, 0);
            }

            try
            {
                Extents3d extents = pline.GeometricExtents;
                return new Point3d(
                    (extents.MinPoint.X + extents.MaxPoint.X) / 2.0,
                    (extents.MinPoint.Y + extents.MaxPoint.Y) / 2.0,
                    0);
            }
            catch
            {
                return Point3d.Origin;
            }
        }

        private static ObjectId? ResolveTargetObjectId(
            HydrologyBasinResult basinResult,
            Dictionary<string, ObjectId> basinsByHandle,
            Dictionary<string, List<ObjectId>> basinsById,
            HydrologyImportSummary summary)
        {
            if (!string.IsNullOrWhiteSpace(basinResult.Handle) && basinsByHandle.TryGetValue(basinResult.Handle, out ObjectId handleMatch))
                return handleMatch;

            if (!string.IsNullOrWhiteSpace(basinResult.Id) && basinsById.TryGetValue(basinResult.Id, out List<ObjectId>? idMatches))
            {
                if (idMatches.Count == 1)
                    return idMatches[0];

                summary.AmbiguousCount++;
                return null;
            }

            summary.UnmatchedCount++;
            return null;
        }

        private static void EnsureRegApp(Database db, Transaction tr, string appName)
        {
            RegAppTable rat = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
            if (rat.Has(appName))
                return;

            rat.UpgradeOpen();
            RegAppTableRecord ratr = new RegAppTableRecord { Name = appName };
            rat.Add(ratr);
            tr.AddNewlyCreatedDBObject(ratr, true);
        }

        private static string ReadBasinId(Polyline pline)
        {
            ResultBuffer? rb = pline.GetXDataForApplication(AppNameBasin);
            if (rb == null)
                return string.Empty;

            try
            {
                TypedValue[] values = rb.AsArray();
                if (values.Length > 1 && values[1].TypeCode == (int)DxfCode.ExtendedDataAsciiString)
                    return values[1].Value?.ToString() ?? string.Empty;

                return string.Empty;
            }
            finally
            {
                rb.Dispose();
            }
        }

        private static HydrologyStoredInputs ReadHydrologyInputs(Polyline pline)
        {
            ResultBuffer? rb = pline.GetXDataForApplication(AppNameHydroInputs);
            if (rb == null)
                return new HydrologyStoredInputs();

            try
            {
                TypedValue[] values = rb.AsArray();
                return new HydrologyStoredInputs
                {
                    TcMinutes = ParseNullableDouble(ReadXDataString(values, 2)),
                    DownstreamId = ReadXDataString(values, 3),
                    CurveNumber = ParseNullableDouble(ReadXDataString(values, 4))
                };
            }
            finally
            {
                rb.Dispose();
            }
        }

        private static HydrologyStoredResult? ReadHydrologyResult(Polyline pline)
        {
            ResultBuffer? rb = pline.GetXDataForApplication(AppNameHydroResults);
            if (rb == null)
                return null;

            try
            {
                TypedValue[] values = rb.AsArray();
                if (values.Length < 9)
                    return null;

                return new HydrologyStoredResult
                {
                    BasinId = ReadXDataString(values, 2),
                    Handle = ReadXDataString(values, 3),
                    AreaAcres = ParseNullableDouble(ReadXDataString(values, 4)),
                    CurveNumber = ParseNullableDouble(ReadXDataString(values, 5)),
                    TcMinutes = ParseNullableDouble(ReadXDataString(values, 6)),
                    ImperviousPercent = ParseNullableDouble(ReadXDataString(values, 7)),
                    Status = ReadXDataString(values, 8),
                    LocalPeakCfs = ParseNullableDouble(ReadXDataString(values, 9)),
                    RoutedPeakCfs = ParseNullableDouble(ReadXDataString(values, 10)),
                    DownstreamId = ReadXDataString(values, 11)
                };
            }
            finally
            {
                rb.Dispose();
            }
        }

        private static string ReadXDataString(TypedValue[] values, int index)
        {
            if (values.Length <= index || values[index].TypeCode != (int)DxfCode.ExtendedDataAsciiString)
                return string.Empty;

            return values[index].Value?.ToString() ?? string.Empty;
        }

        private static double? ParseNullableDouble(string value)
        {
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
                ? parsed
                : null;
        }

        private static ResultBuffer BuildHydrologyResultXData(HydrologyBasinResult result, string resultsPath)
        {
            string importedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
            return new ResultBuffer(
                new TypedValue((int)DxfCode.ExtendedDataRegAppName, AppNameHydroResults),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, "1"),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, ToXDataString(result.Id)),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, ToXDataString(result.Handle)),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, FormatNumber(result.AreaAcres)),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, FormatNumber(result.CurveNumber)),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, FormatNumber(result.TcMinutes)),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, FormatNumber(result.ImperviousPercent)),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, ToXDataString(result.Status)),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, FormatNumber(result.LocalPeakCfs)),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, FormatNumber(result.RoutedPeakCfs)),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, ToXDataString(result.DownstreamId)),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, ToXDataString(resultsPath)),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, importedAtUtc)
            );
        }

        private static ResultBuffer BuildMergedXData(Polyline pline, HydrologyBasinResult result, string resultsPath)
        {
            var values = new List<TypedValue>();
            ResultBuffer? existing = pline.XData;
            if (existing != null)
            {
                try
                {
                    values.AddRange(RemoveRegAppGroup(existing.AsArray(), AppNameHydroResults));
                }
                finally
                {
                    existing.Dispose();
                }
            }

            using ResultBuffer hydroResults = BuildHydrologyResultXData(result, resultsPath);
            values.AddRange(hydroResults.AsArray());
            return new ResultBuffer(values.ToArray());
        }

        private static ResultBuffer BuildMergedHydrologyInputsXData(Polyline pline, double? tcMinutes, string downstreamId, double? curveNumber)
        {
            var values = new List<TypedValue>();
            ResultBuffer? existing = pline.XData;
            if (existing != null)
            {
                try
                {
                    values.AddRange(RemoveRegAppGroup(existing.AsArray(), AppNameHydroInputs));
                }
                finally
                {
                    existing.Dispose();
                }
            }

            values.AddRange(new[]
            {
                new TypedValue((int)DxfCode.ExtendedDataRegAppName, AppNameHydroInputs),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, "1"),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, FormatNumber(tcMinutes)),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, ToXDataString(downstreamId)),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, FormatNumber(curveNumber))
            });

            return new ResultBuffer(values.ToArray());
        }

        private static PromptEntityResult PromptForBasinPolyline(Editor ed, string message)
        {
            var options = new PromptEntityOptions(message);
            options.SetRejectMessage("\nMust be a polyline.");
            options.AddAllowedClass(typeof(Polyline), true);
            return ed.GetEntity(options);
        }

        private static IEnumerable<TypedValue> RemoveRegAppGroup(TypedValue[] values, string appName)
        {
            bool skipCurrentGroup = false;
            foreach (TypedValue value in values)
            {
                if (value.TypeCode == (int)DxfCode.ExtendedDataRegAppName)
                    skipCurrentGroup = string.Equals(value.Value?.ToString(), appName, StringComparison.OrdinalIgnoreCase);

                if (!skipCurrentGroup)
                    yield return value;
            }
        }

        private static string FormatNumber(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);

        private static string FormatNumber(double? value) => value.HasValue ? FormatNumber(value.Value) : string.Empty;

        private static string FormatDisplayNumber(double? value) => value.HasValue ? value.Value.ToString("0.##", CultureInfo.InvariantCulture) : "--";

        private static string EscapeMText(string value)
        {
            return value
                .Replace("\\", "\\\\")
                .Replace("{", "\\{")
                .Replace("}", "\\}");
        }

        private static string ToXDataString(string? value)
        {
            const int MaxXDataStringLength = 255;
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return value.Length <= MaxXDataStringLength ? value : value.Substring(0, MaxXDataStringLength);
        }

        private sealed class HydrologyImportSummary
        {
            public int UpdatedCount { get; set; }
            public int UnmatchedCount { get; set; }
            public int AmbiguousCount { get; set; }
        }

        private sealed class HydrologyLabelSummary
        {
            public int CreatedCount { get; set; }
            public int RemovedCount { get; set; }
            public int SkippedCount { get; set; }
        }

        private sealed class HydrologyStoredResult
        {
            public string BasinId { get; set; } = string.Empty;
            public string Handle { get; set; } = string.Empty;
            public double? AreaAcres { get; set; }
            public double? CurveNumber { get; set; }
            public double? TcMinutes { get; set; }
            public double? ImperviousPercent { get; set; }
            public double? LocalPeakCfs { get; set; }
            public double? RoutedPeakCfs { get; set; }
            public string DownstreamId { get; set; } = string.Empty;
            public string Status { get; set; } = string.Empty;
        }

        private sealed class HydrologyStoredInputs
        {
            public double? TcMinutes { get; set; }
            public string DownstreamId { get; set; } = string.Empty;
            public double? CurveNumber { get; set; }
        }
    }
}