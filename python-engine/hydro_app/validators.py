from __future__ import annotations

from dataclasses import dataclass, field

from hydro_app.network import find_invalid_downstream_references, find_routing_cycles
from hydro_app.schema import HydrologyModel


SOURCE_HYDROGRAPH_TYPES = {"SCS", "RATIONAL"}
SINGLE_INFLOW_HYDROGRAPH_TYPES = {"REACH", "RESERVOIR"}
MULTI_INFLOW_HYDROGRAPH_TYPES = {"COMBINE"}
SUPPORTED_HYDROGRAPH_TYPES = SOURCE_HYDROGRAPH_TYPES | SINGLE_INFLOW_HYDROGRAPH_TYPES | MULTI_INFLOW_HYDROGRAPH_TYPES


@dataclass
class ValidationReport:
    errors: list[str] = field(default_factory=list)
    warnings: list[str] = field(default_factory=list)
    info: list[str] = field(default_factory=list)

    @property
    def is_valid(self) -> bool:
        return not self.errors

    def to_dict(self) -> dict[str, list[str]]:
        return {
            "errors": self.errors,
            "warnings": self.warnings,
            "info": self.info,
        }


def validate_model(model: HydrologyModel) -> ValidationReport:
    report = ValidationReport()

    if not model.basins:
        report.errors.append("Model contains no basins.")
        return report

    seen_ids: set[str] = set()
    duplicate_ids: set[str] = set()
    for basin in model.basins:
        if not basin.id:
            report.errors.append("A basin is missing an id.")
            continue

        if basin.id in seen_ids:
            duplicate_ids.add(basin.id)
        seen_ids.add(basin.id)

        if basin.area_acres <= 0:
            report.errors.append(f"Basin {basin.id} has non-positive area_acres.")

        land_use_area = sum(item.area_acres for item in basin.land_uses)
        if not basin.land_uses:
            report.warnings.append(f"Basin {basin.id} has no land use areas.")
        elif basin.area_acres > 0 and land_use_area > basin.area_acres * 1.05:
            report.warnings.append(f"Basin {basin.id} land use area exceeds basin area by more than 5%.")

        if basin.curve_number is not None and not 30 <= basin.curve_number <= 100:
            report.errors.append(f"Basin {basin.id} has invalid curve_number {basin.curve_number}.")

        for land_use in basin.land_uses:
            if land_use.area_acres <= 0:
                report.warnings.append(f"Basin {basin.id} land use {land_use.layer} has non-positive area.")
            if land_use.curve_number is not None and not 30 <= land_use.curve_number <= 100:
                report.errors.append(f"Basin {basin.id} land use {land_use.layer} has invalid curve_number {land_use.curve_number}.")

        if basin.tc_minutes is not None and basin.tc_minutes <= 0:
            report.errors.append(f"Basin {basin.id} has invalid tc_minutes {basin.tc_minutes}.")

    for basin_id in sorted(duplicate_ids):
        report.warnings.append(f"Basin id {basin_id} appears more than once; handles or conditions should distinguish drawing pieces.")

    for upstream_id, downstream_id in find_invalid_downstream_references(model):
        report.errors.append(f"Basin {upstream_id} routes to missing downstream basin {downstream_id}.")

    for cycle in find_routing_cycles(model):
        report.errors.append(f"Routing cycle detected: {' -> '.join(cycle)} -> {cycle[0]}.")

    validate_hydrographs(model, report)

    report.info.append(f"Validated {len(model.basins)} basin(s).")
    return report


def validate_hydrographs(model: HydrologyModel, report: ValidationReport) -> None:
    if not model.hydrographs:
        return

    basin_ids = {basin.id for basin in model.basins}
    hydrograph_ids: set[str] = set()
    duplicate_ids: set[str] = set()

    for hydrograph in model.hydrographs:
        if not hydrograph.id:
            report.errors.append("A hydrograph row is missing an id.")
            continue

        if hydrograph.id in hydrograph_ids:
            duplicate_ids.add(hydrograph.id)
        hydrograph_ids.add(hydrograph.id)

        hydrograph_type = hydrograph.type.upper()
        if hydrograph_type not in SUPPORTED_HYDROGRAPH_TYPES:
            report.errors.append(f"Hydrograph {hydrograph.id} has unsupported type {hydrograph.type}.")

        if hydrograph_type in SOURCE_HYDROGRAPH_TYPES and hydrograph.basin_id not in basin_ids:
            report.errors.append(f"Hydrograph {hydrograph.id} references missing basin {hydrograph.basin_id}.")

        if hydrograph_type in SOURCE_HYDROGRAPH_TYPES and hydrograph.inflow_ids:
            report.errors.append(f"Hydrograph {hydrograph.id} type {hydrograph.type} does not accept inflow hydrographs.")

        if hydrograph_type in MULTI_INFLOW_HYDROGRAPH_TYPES and len(hydrograph.inflow_ids) < 2:
            report.errors.append(f"Hydrograph {hydrograph.id} type {hydrograph.type} requires at least two inflow hydrographs.")

        if hydrograph_type in SINGLE_INFLOW_HYDROGRAPH_TYPES and len(hydrograph.inflow_ids) != 1:
            report.errors.append(f"Hydrograph {hydrograph.id} type {hydrograph.type} requires exactly one inflow hydrograph.")

        if hydrograph_type == "RESERVOIR":
            validate_reservoir_parameters(hydrograph, report)

    for hydrograph_id in sorted(duplicate_ids):
        report.errors.append(f"Hydrograph id {hydrograph_id} appears more than once.")

    for hydrograph in model.hydrographs:
        for inflow_id in hydrograph.inflow_ids:
            if inflow_id not in hydrograph_ids:
                report.errors.append(f"Hydrograph {hydrograph.id} references missing inflow hydrograph {inflow_id}.")


def validate_reservoir_parameters(hydrograph, report: ValidationReport) -> None:
    stage_storage = []
    for row in hydrograph.parameters.get("stage_storage", []):
        if not isinstance(row, dict):
            continue
        elevation = _optional_float(row.get("elevation"))
        storage = _optional_float(row.get("volume") or row.get("storage") or row.get("storage_cuft"))
        if elevation is not None and storage is not None:
            stage_storage.append((elevation, storage))

    stage_storage.sort(key=lambda item: item[0])
    if len(stage_storage) < 2:
        report.errors.append(f"Hydrograph {hydrograph.id} type Reservoir requires at least two stage-storage rows.")
        return

    for left, right in zip(stage_storage, stage_storage[1:]):
        if right[0] <= left[0] or right[1] < left[1]:
            report.errors.append(f"Hydrograph {hydrograph.id} type Reservoir has non-monotonic stage-storage rows.")
            return

    if not _has_active_outlet(hydrograph.parameters):
        report.warnings.append(f"Hydrograph {hydrograph.id} type Reservoir has no active outlet rows.")


def _has_active_outlet(parameters: dict) -> bool:
    for key in ("weirs", "culverts_orifices"):
        rows = parameters.get(key, [])
        if not isinstance(rows, list):
            continue
        for row in rows:
            if isinstance(row, dict) and str(row.get("label", "")).strip().lower() == "active":
                if any(str(row.get(column, "")).strip().lower() in {"yes", "y", "true", "1"} for column in ("a", "b", "c", "d", "riser")):
                    return True
    return False


def _optional_float(value) -> float | None:
    if value is None:
        return None
    text = str(value).strip().replace(",", "")
    if not text or text.lower() in {"n/a", "na", "----", "---"}:
        return None
    try:
        return float(text)
    except ValueError:
        return None