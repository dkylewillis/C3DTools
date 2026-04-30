using Autodesk.AutoCAD.ApplicationServices;
using C3DTools.Core.Models;
using C3DTools.Core.PipeContracts;
using C3DTools.Services;
using System.Text.Json;

namespace C3DTools.Infrastructure
{
    /// <summary>
    /// Dispatches incoming pipe requests to the appropriate service method.
    /// All handlers run on a background thread; AutoCAD API calls must be
    /// marshalled to the document context via Document.SendStringToExecute or
    /// executed under a DocumentLock.
    /// </summary>
    internal static class CommandRouter
    {
        private static readonly BasinDataService _basinService = new();

        public static PipeResponse Handle(PipeRequest request)
        {
            try
            {
                Document? doc = Application.DocumentManager.MdiActiveDocument;

                return request.Command switch
                {
                    "list-basins" => ListBasins(doc),
                    "get-basin"   => GetBasin(doc, request.Args),
                    _             => new PipeResponse(false, null, $"Unknown command: {request.Command}")
                };
            }
            catch (Exception ex)
            {
                return new PipeResponse(false, null, ex.Message);
            }
        }

        private static PipeResponse ListBasins(Document? doc)
        {
            if (doc == null)
                return new PipeResponse(false, null, "No active drawing.");

            var basins = _basinService.GetAllBasins(doc);
            var dtos = basins.Select(b => new BasinDto
            {
                BasinId    = b.BasinId,
                Layer      = b.Layer,
                Boundary   = b.Boundary,
                Development = b.Development
            }).ToList();

            return new PipeResponse(true, JsonSerializer.Serialize(dtos), null);
        }

        private static PipeResponse GetBasin(Document? doc, Dictionary<string, string> args)
        {
            if (doc == null)
                return new PipeResponse(false, null, "No active drawing.");

            if (!args.TryGetValue("basin", out var basinId))
                return new PipeResponse(false, null, "Missing required argument: --basin");

            var basins = _basinService.GetAllBasins(doc);
            var match  = basins.FirstOrDefault(b =>
                string.Equals(b.BasinId, basinId, StringComparison.OrdinalIgnoreCase));

            if (match == null)
                return new PipeResponse(false, null, $"Basin '{basinId}' not found.");

            var dto = new BasinDto
            {
                BasinId     = match.BasinId,
                Layer       = match.Layer,
                Boundary    = match.Boundary,
                Development = match.Development
            };

            return new PipeResponse(true, JsonSerializer.Serialize(dto), null);
        }
    }
}
