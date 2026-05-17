from __future__ import annotations

from hydro_app.results import build_results
from hydro_app.schema import Basin, HydrologyModel, Storm
from hydro_app.validators import validate_model


class PyfloEngine:
    def run_basin(self, basin: Basin, storm: Storm | None = None) -> dict:
        model = HydrologyModel(basins=[basin], storms=[storm] if storm else [])
        validation = validate_model(model)
        return build_results(model, validation)["basins"][0]

    def run_model(self, model: HydrologyModel) -> dict:
        validation = validate_model(model)
        return build_results(model, validation)