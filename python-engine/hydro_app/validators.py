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

    for hydrograph_id in sorted(duplicate_ids):
        report.errors.append(f"Hydrograph id {hydrograph_id} appears more than once.")

    for hydrograph in model.hydrographs:
        for inflow_id in hydrograph.inflow_ids:
            if inflow_id not in hydrograph_ids:
                report.errors.append(f"Hydrograph {hydrograph.id} references missing inflow hydrograph {inflow_id}.")