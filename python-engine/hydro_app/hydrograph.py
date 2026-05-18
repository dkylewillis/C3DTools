from __future__ import annotations

from dataclasses import dataclass
from functools import lru_cache
import math
from pathlib import Path
from typing import Iterable

import numpy as np

from hydro_app.curve_number import composite_curve_number
from hydro_app.network import route_map, topological_basin_order, upstream_map
from hydro_app.schema import Basin, HydrographDefinition, HydrologyModel, Storm
from hydro_app.tc import time_of_concentration_minutes


NRCS_TRIANGULAR_RUNOFF_DISTRIBUTION = np.array(
    [
        [0.0, 0.0],
        [1.0, 1.0],
        [2.67, 0.0],
    ]
)
NRCS_UNIT_HYDROGRAPH_SHAPE_FACTOR = 484.0
ACRES_PER_SQUARE_MILE = 640.0
SUPPORTED_HYDROGRAPH_TYPES = {"SCS", "COMBINE", "RESERVOIR"}
GRAVITY_FTPS2 = 32.174
TR55_TYPE_I_24HR_TABULAR = "TR55_TYPE_I_24HR_TABULAR"
TR55_TYPE_IA_24HR_TABULAR = "TR55_TYPE_IA_24HR_TABULAR"
TR55_TYPE_II_24HR_TABULAR = "TR55_TYPE_II_24HR_TABULAR"
TR55_TYPE_III_24HR_TABULAR = "TR55_TYPE_III_24HR_TABULAR"
TR55_24HR_TABULAR_FILES = {
    TR55_TYPE_I_24HR_TABULAR: "Type I.tbl",
    TR55_TYPE_IA_24HR_TABULAR: "Type IA.tbl",
    TR55_TYPE_II_24HR_TABULAR: "Type II.tbl",
    TR55_TYPE_III_24HR_TABULAR: "Type III.tbl",
}
TR55_24HR_TABULAR_ALIASES = {
    "SCS_TYPE_I": TR55_TYPE_I_24HR_TABULAR,
    "NRCS_TYPE_I": TR55_TYPE_I_24HR_TABULAR,
    "SCS_TYPE_IA": TR55_TYPE_IA_24HR_TABULAR,
    "NRCS_TYPE_IA": TR55_TYPE_IA_24HR_TABULAR,
    "SCS_TYPE_II": TR55_TYPE_II_24HR_TABULAR,
    "NRCS_TYPE_II": TR55_TYPE_II_24HR_TABULAR,
    "SCS_TYPE_III": TR55_TYPE_III_24HR_TABULAR,
    "NRCS_TYPE_III": TR55_TYPE_III_24HR_TABULAR,
}
BROAD_CRESTED_WEIR_HEADS_FT = (0.2, 0.4, 0.6, 0.8, 1.0, 1.2, 1.4, 1.6, 1.8, 2.0, 2.5, 3.0, 3.5, 4.0, 4.5, 5.0, 5.5)
BROAD_CRESTED_WEIR_BREADTHS_FT = (1.0, 2.0, 3.0, 5.0, 10.0, 15.0)
BROAD_CRESTED_WEIR_COEFFICIENTS = (
    (2.69, 2.54, 2.44, 2.34, 2.49, 2.68),
    (2.72, 2.61, 2.58, 2.50, 2.56, 2.70),
    (2.75, 2.61, 2.68, 2.70, 2.70, 2.70),
    (2.85, 2.60, 2.67, 2.68, 2.69, 2.64),
    (2.98, 2.66, 2.65, 2.68, 2.68, 2.63),
    (3.08, 2.70, 2.64, 2.66, 2.69, 2.64),
    (3.20, 2.77, 2.64, 2.65, 2.67, 2.64),
    (3.28, 2.89, 2.68, 2.65, 2.64, 2.63),
    (3.31, 2.88, 2.68, 2.65, 2.64, 2.63),
    (3.30, 2.85, 2.72, 2.65, 2.64, 2.63),
    (3.31, 3.07, 2.81, 2.67, 2.64, 2.63),
    (3.32, 3.20, 2.92, 2.66, 2.64, 2.63),
    (3.32, 3.32, 2.97, 2.68, 2.64, 2.63),
    (3.32, 3.32, 3.07, 2.70, 2.64, 2.63),
    (3.32, 3.32, 3.32, 2.74, 2.64, 2.63),
    (3.32, 3.32, 3.32, 2.79, 2.64, 2.63),
    (3.32, 3.32, 3.32, 2.88, 2.64, 2.63),
)
CONCRETE_PIPE_LENGTHS_FT = (10.0, 20.0, 30.0, 40.0, 50.0, 60.0, 80.0, 100.0, 120.0, 150.0)
CONCRETE_PIPE_DIAMETERS_IN = (12.0, 24.0, 36.0, 48.0, 60.0)
CONCRETE_PIPE_BEVELED_LIP_COEFFICIENTS = (
    (0.86, 0.91, 0.92, 0.93, 0.94),
    (0.79, 0.87, 0.90, 0.91, 0.92),
    (0.73, 0.83, 0.87, 0.89, 0.90),
    (0.68, 0.80, 0.85, 0.88, 0.89),
    (0.65, 0.77, 0.83, 0.86, 0.88),
    (0.61, 0.75, 0.81, 0.85, 0.87),
    (0.56, 0.71, 0.78, 0.82, 0.84),
    (0.52, 0.67, 0.74, 0.79, 0.82),
    (0.49, 0.64, 0.71, 0.77, 0.80),
    (0.45, 0.60, 0.68, 0.74, 0.77),
)
CONCRETE_PIPE_SQUARE_CORNERED_COEFFICIENTS = (
    (0.80, 0.80, 0.79, 0.77, 0.76),
    (0.74, 0.78, 0.77, 0.76, 0.75),
    (0.69, 0.75, 0.76, 0.75, 0.74),
    (0.65, 0.73, 0.74, 0.74, 0.74),
    (0.62, 0.71, 0.73, 0.73, 0.73),
    (0.59, 0.69, 0.72, 0.72, 0.72),
    (0.54, 0.65, 0.69, 0.70, 0.71),
    (0.51, 0.62, 0.67, 0.69, 0.70),
    (0.48, 0.60, 0.65, 0.67, 0.68),
    (0.44, 0.56, 0.62, 0.65, 0.60),
)


@dataclass
class HydrographRowResult:
    id: str
    type: str
    description: str
    storm_id: str
    basin_id: str
    inflow_ids: list[str]
    downstream_id: str
    points: list[dict]
    peak_flow_cfs: float | None
    time_to_peak_minutes: float | None
    time_interval_minutes: float | None
    tc_minutes: float | None
    volume_cuft: float | None
    maximum_elevation_ft: float | None = None
    maximum_storage_cuft: float | None = None
    outlet_rating: list[dict] | None = None
    status: str = "ok"

    def to_dict(self) -> dict:
        return {
            "id": self.id,
            "type": self.type,
            "description": self.description,
            "storm_id": self.storm_id,
            "basin_id": self.basin_id,
            "inflow_ids": self.inflow_ids,
            "downstream_id": self.downstream_id,
            "peak_flow_cfs": self.peak_flow_cfs,
            "time_to_peak_minutes": self.time_to_peak_minutes,
            "time_interval_minutes": self.time_interval_minutes,
            "tc_minutes": self.tc_minutes,
            "volume_cuft": self.volume_cuft,
            "maximum_elevation_ft": self.maximum_elevation_ft,
            "maximum_storage_cuft": self.maximum_storage_cuft,
            "outlet_rating": self.outlet_rating or [],
            "points": self.points,
            "status": self.status,
        }


@dataclass
class BasinHydrographResult:
    basin_id: str
    storm_id: str
    local_hydrograph_id: str
    routed_hydrograph_id: str
    local_points: list[dict]
    routed_points: list[dict]
    local_peak_cfs: float | None
    routed_peak_cfs: float | None
    local_peak_time_minutes: float | None
    routed_peak_time_minutes: float | None


@dataclass(frozen=True)
class TabularDistributionCurve:
    duration_minutes: float
    source_times: tuple[float, ...]
    coefficients: tuple[tuple[float, float, float, float], ...]


@dataclass(frozen=True)
class StageStoragePoint:
    elevation_ft: float
    storage_cuft: float


@dataclass(frozen=True)
class OutletRatingPoint:
    elevation_ft: float
    storage_cuft: float
    discharge_cfs: float


def generate_hydrograph_rows(model: HydrologyModel, storm: Storm) -> dict[str, HydrographRowResult]:
    definitions = model.hydrographs or default_hydrograph_definitions(model)
    basins_by_id = {basin.id: basin for basin in model.basins}
    rows: dict[str, HydrographRowResult] = {}

    for definition in definitions:
        hydrograph_type = definition.type.upper()
        if hydrograph_type == "SCS":
            rows[definition.id] = _evaluate_scs_row(definition, basins_by_id, storm)
        elif hydrograph_type == "COMBINE":
            rows[definition.id] = _evaluate_combine_row(definition, rows, storm)
        elif hydrograph_type == "RESERVOIR":
            rows[definition.id] = _evaluate_reservoir_row(definition, rows, storm)
        else:
            rows[definition.id] = _unsupported_row(definition, storm)

    return rows


def generate_model_hydrographs(model: HydrologyModel, storm: Storm) -> dict[str, BasinHydrographResult]:
    rows = generate_hydrograph_rows(model, storm)
    definitions = model.hydrographs or default_hydrograph_definitions(model)
    local_ids = local_hydrograph_ids_by_basin(definitions)
    routed_ids = routed_hydrograph_ids_by_basin(model, definitions)
    results: dict[str, BasinHydrographResult] = {}

    for basin in model.basins:
        local_id = local_ids.get(basin.id, "")
        routed_id = routed_ids.get(basin.id, local_id)
        local = rows.get(local_id)
        routed = rows.get(routed_id)
        results[basin.id] = BasinHydrographResult(
            basin_id=basin.id,
            storm_id=storm.id,
            local_hydrograph_id=local_id,
            routed_hydrograph_id=routed_id,
            local_points=local.points if local else [],
            routed_points=routed.points if routed else [],
            local_peak_cfs=local.peak_flow_cfs if local else None,
            routed_peak_cfs=routed.peak_flow_cfs if routed else None,
            local_peak_time_minutes=local.time_to_peak_minutes if local else None,
            routed_peak_time_minutes=routed.time_to_peak_minutes if routed else None,
        )

    return results


def default_hydrograph_definitions(model: HydrologyModel) -> list[HydrographDefinition]:
    definitions: list[HydrographDefinition] = []
    local_ids: dict[str, str] = {}
    output_ids: dict[str, str] = {}
    upstream = upstream_map(model)
    routes = route_map(model)

    for index, basin in enumerate(model.basins, start=1):
        hydrograph_id = f"H{index}"
        local_ids[basin.id] = hydrograph_id
        output_ids[basin.id] = hydrograph_id
        definitions.append(
            HydrographDefinition(
                id=hydrograph_id,
                type="SCS",
                description=basin.name or basin.id,
                basin_id=basin.id,
                downstream_id=routes.get(basin.id, basin.downstream_id),
            )
        )

    next_index = len(definitions) + 1
    basin_order = topological_basin_order(model)
    for basin_id in basin_order:
        upstream_ids = [item for item in sorted(upstream.get(basin_id, [])) if item in output_ids]
        if not upstream_ids:
            continue

        hydrograph_id = f"H{next_index}"
        next_index += 1
        inflow_ids = [output_ids[item] for item in upstream_ids]
        if basin_id in local_ids:
            inflow_ids.append(local_ids[basin_id])
        output_ids[basin_id] = hydrograph_id
        definitions.append(
            HydrographDefinition(
                id=hydrograph_id,
                type="Combine",
                description=f"Combined hydrograph at {basin_id}",
                basin_id=basin_id,
                inflow_ids=inflow_ids,
                downstream_id=routes.get(basin_id, ""),
            )
        )

    return definitions


def local_hydrograph_ids_by_basin(definitions: list[HydrographDefinition]) -> dict[str, str]:
    return {
        definition.basin_id: definition.id
        for definition in definitions
        if definition.type.upper() in {"SCS", "RATIONAL"} and definition.basin_id
    }


def routed_hydrograph_ids_by_basin(model: HydrologyModel, definitions: list[HydrographDefinition]) -> dict[str, str]:
    local_ids = local_hydrograph_ids_by_basin(definitions)
    routed_ids = dict(local_ids)
    for definition in definitions:
        if definition.basin_id and definition.type.upper() in {"COMBINE", "REACH", "RESERVOIR"}:
            routed_ids[definition.basin_id] = definition.id
    return routed_ids


def generate_basin_hydrograph(basin: Basin, storm: Storm) -> list[tuple[float, float]]:
    curve_number = composite_curve_number(basin)
    tc_minutes = time_of_concentration_minutes(basin)
    rainfall_depth_inches = storm.rainfall_depth_inches
    if curve_number is None or tc_minutes is None or rainfall_depth_inches is None or basin.area_acres <= 0:
        return [(0.0, 0.0)]

    interval = float(storm.time_step_minutes or 2)
    rain_depths = design_storm_cumulative_rainfall(storm, interval)
    hydrograph = scs_flood_hydrograph(basin.area_acres, curve_number, tc_minutes, rain_depths, interval)
    return [(round(float(time), 6), round(max(float(flow), 0.0), 6)) for time, flow in hydrograph.tolist()]


def scs_flood_hydrograph(
    area_acres: float,
    curve_number: float,
    tc_minutes: float,
    cumulative_rainfall: np.ndarray,
    interval_minutes: float,
) -> np.ndarray:
    unit_hydrograph = scs_unit_hydrograph(area_acres, tc_minutes, interval_minutes)
    runoff_increments = list(scs_incremental_runoff_depths(cumulative_rainfall, curve_number, interval_minutes))
    runoff_increments.reverse()
    composite_length = len(unit_hydrograph) + len(runoff_increments)
    data: list[tuple[float, float]] = []

    for index in range(composite_length - 1):
        upper = index + 1
        lower = max(upper - len(runoff_increments), 0)
        flow = sum(
            runoff_increments[j - upper] * unit_hydrograph[j][1]
            for j in range(lower, upper)
            if j < len(unit_hydrograph)
        )
        data.append((index * interval_minutes, flow))

    return np.array(data)


def scs_unit_hydrograph(area_acres: float, tc_minutes: float, interval_minutes: float) -> np.ndarray:
    peak_time = scs_peak_time_minutes(tc_minutes, interval_minutes)
    peak_time_hours = peak_time / 60.0
    area_square_miles = area_acres / ACRES_PER_SQUARE_MILE
    peak_runoff = NRCS_UNIT_HYDROGRAPH_SHAPE_FACTOR * area_square_miles / peak_time_hours
    hydrograph = NRCS_TRIANGULAR_RUNOFF_DISTRIBUTION * np.array([peak_time, peak_runoff])
    return _resample_distribution(hydrograph, interval_minutes)


def scs_incremental_runoff_depths(cumulative_rainfall: np.ndarray, curve_number: float, interval_minutes: float) -> Iterable[float]:
    rainfall = _resample_distribution(cumulative_rainfall, interval_minutes).tolist()
    for left, right in zip(rainfall, rainfall[1:]):
        runoff_left = scs_runoff_depth_inches(left[1], curve_number)
        runoff_right = scs_runoff_depth_inches(right[1], curve_number)
        yield max(runoff_right - runoff_left, 0.0)


def scs_peak_time_minutes(tc_minutes: float, interval_minutes: float) -> float:
    return (tc_minutes + interval_minutes) / 1.7


def _resample_distribution(distribution: np.ndarray, interval_minutes: float) -> np.ndarray:
    x_values = distribution[:, 0]
    y_values = distribution[:, 1]
    x_new = np.arange(x_values[0], x_values[-1] + interval_minutes, interval_minutes)
    y_new = np.interp(x_new, x_values, y_values, left=y_values[0], right=y_values[-1])
    return np.column_stack((x_new, y_new))


def design_storm_cumulative_rainfall(storm: Storm, interval_minutes: float) -> np.ndarray:
    duration_minutes = float(storm.duration_minutes or interval_minutes)
    total_depth = float(storm.rainfall_depth_inches or 0.0)
    if duration_minutes <= 0 or total_depth <= 0:
        return np.array([[0.0, 0.0], [interval_minutes, 0.0]])

    distribution = (storm.distribution or "").upper()
    tabular_distribution = TR55_24HR_TABULAR_ALIASES.get(distribution, distribution)
    if tabular_distribution in TR55_24HR_TABULAR_FILES:
        return _tabular_mass_curve_rainfall(_load_24hr_tabular_distribution_curve(tabular_distribution), total_depth, interval_minutes)

    duration_depths = _duration_depths_from_storm(storm)
    incremental_depths = _alternating_block_incremental_depths(duration_minutes, total_depth, duration_depths, interval_minutes)
    cumulative = [0.0]
    for depth in incremental_depths:
        cumulative.append(cumulative[-1] + depth)

    times = [i * interval_minutes for i in range(len(cumulative))]
    cumulative[-1] = total_depth
    return np.array(list(zip(times, cumulative)))


@lru_cache(maxsize=None)
def _load_24hr_tabular_distribution_curve(distribution: str) -> TabularDistributionCurve:
    file_name = TR55_24HR_TABULAR_FILES[distribution]
    path = Path(__file__).resolve().parents[2] / "data" / "RainfallDistributions" / file_name
    lines = [line.strip() for line in path.read_text(encoding="utf-8").splitlines() if line.strip()]
    if not lines:
        raise ValueError(f"Rainfall distribution file is empty: {path}")

    header = lines[0].split()
    interval_hours = float(header[-1])
    values: list[float] = []

    for line in lines[1:]:
        values.extend(float(value) for value in line.split())

    if len(values) < 2:
        raise ValueError(f"Rainfall distribution file has no depth values: {path}")

    values[0] = 0.0
    values[-1] = 1.0
    interval_minutes = interval_hours * 60.0
    source_times = tuple(index * interval_minutes for index in range(len(values)))
    return TabularDistributionCurve(
        duration_minutes=source_times[-1],
        source_times=source_times,
        coefficients=_build_monotone_cubic_coefficients(source_times, tuple(values)),
    )


def _tabular_mass_curve_rainfall(curve: TabularDistributionCurve, total_depth: float, interval_minutes: float) -> np.ndarray:
    duration_minutes = curve.duration_minutes
    target_count = max(int(np.ceil(duration_minutes / interval_minutes)), 1)
    target_times = np.arange(target_count + 1, dtype=float) * interval_minutes
    target_times[-1] = duration_minutes
    cumulative_fractions = np.array([_evaluate_tabular_distribution_curve(curve, time) for time in target_times])
    cumulative_fractions[0] = 0.0
    cumulative_fractions[-1] = 1.0
    return np.column_stack((target_times, cumulative_fractions * total_depth))


def _build_monotone_cubic_coefficients(times: tuple[float, ...], values: tuple[float, ...]) -> tuple[tuple[float, float, float, float], ...]:
    x = np.array(times, dtype=float)
    y = np.array(values, dtype=float)
    h = np.diff(x)
    delta = np.diff(y) / h
    slopes = np.zeros_like(y)
    slopes[0] = delta[0]
    slopes[-1] = delta[-1]

    for index in range(1, len(y) - 1):
        left = delta[index - 1]
        right = delta[index]
        if left * right <= 0:
            slopes[index] = 0.0
            continue

        left_width = h[index - 1]
        right_width = h[index]
        weight_1 = 2.0 * right_width + left_width
        weight_2 = right_width + 2.0 * left_width
        slopes[index] = (weight_1 + weight_2) / ((weight_1 / left) + (weight_2 / right))

    coefficients: list[tuple[float, float, float, float]] = []
    for index in range(len(delta)):
        width = h[index]
        a = y[index]
        b = slopes[index]
        c = (3.0 * delta[index] - 2.0 * slopes[index] - slopes[index + 1]) / width
        d = (slopes[index] + slopes[index + 1] - 2.0 * delta[index]) / (width * width)
        coefficients.append((float(a), float(b), float(c), float(d)))

    return tuple(coefficients)


def _evaluate_tabular_distribution_curve(curve: TabularDistributionCurve, time_minutes: float) -> float:
    if time_minutes <= 0:
        return 0.0
    if time_minutes >= curve.duration_minutes:
        return 1.0

    index = int(np.searchsorted(curve.source_times, time_minutes, side="right") - 1)
    index = min(max(index, 0), len(curve.coefficients) - 1)
    local_time = time_minutes - curve.source_times[index]
    a, b, c, d = curve.coefficients[index]
    value = a + (b * local_time) + (c * local_time * local_time) + (d * local_time * local_time * local_time)
    return min(max(float(value), 0.0), 1.0)


def basin_peak_cfs(basin: Basin, rainfall_depth_inches: float = 1.0) -> float | None:
    storm = Storm(
        id="peak-depth",
        name="Peak depth",
        rainfall_depth_inches=rainfall_depth_inches,
        duration_minutes=max((time_of_concentration_minutes(basin) or 2) * 4, 2),
        time_step_minutes=2,
    )
    series = generate_basin_hydrograph(basin, storm)
    return peak_flow(series)


def routed_peak_cfs(model: HydrologyModel, local_peaks: dict[str, float | None]) -> dict[str, float | None]:
    routed = {basin.id: local_peaks.get(basin.id) for basin in model.basins}
    routes = route_map(model)
    for basin_id in topological_basin_order(model):
        downstream_id = routes.get(basin_id, "")
        if downstream_id not in routed:
            continue
        upstream_peak = routed.get(basin_id)
        if upstream_peak is None:
            continue
        downstream_peak = routed.get(downstream_id) or 0.0
        routed[downstream_id] = round(downstream_peak + upstream_peak, 3)
    return routed


def scs_runoff_depth_inches(rainfall_depth_inches: float, curve_number: float) -> float:
    if curve_number <= 0:
        return 0.0
    potential_retention = (1000.0 / curve_number) - 10.0
    initial_abstraction = 0.2 * potential_retention
    if rainfall_depth_inches <= initial_abstraction:
        return 0.0
    return ((rainfall_depth_inches - initial_abstraction) ** 2.0) / (rainfall_depth_inches - initial_abstraction + potential_retention)


def add_hydrographs(left: list[tuple[float, float]], right: list[tuple[float, float]]) -> list[tuple[float, float]]:
    times = sorted({time for time, _ in left} | {time for time, _ in right})
    left_values = dict(left)
    right_values = dict(right)
    return [(time, round(left_values.get(time, 0.0) + right_values.get(time, 0.0), 6)) for time in times]


def series_to_points(series: Iterable[tuple[float, float]]) -> list[dict]:
    return [{"time_minutes": round(time, 3), "flow_cfs": round(flow, 3)} for time, flow in series]


def points_to_series(points: Iterable[dict]) -> list[tuple[float, float]]:
    return [(float(point["time_minutes"]), float(point["flow_cfs"])) for point in points]


def peak_flow(series: list[tuple[float, float]]) -> float | None:
    if not series:
        return None
    return round(max(flow for _, flow in series), 3)


def peak_time(series: list[tuple[float, float]]) -> float | None:
    if not series:
        return None
    time, _ = max(series, key=lambda item: item[1])
    return round(time, 3)


def hydrograph_volume_cuft(series: list[tuple[float, float]]) -> float | None:
    if len(series) < 2:
        return None
    volume = 0.0
    for (time_1, flow_1), (time_2, flow_2) in zip(series, series[1:]):
        volume += ((flow_1 + flow_2) / 2.0) * (time_2 - time_1) * 60.0
    return round(volume, 3)


def time_interval_minutes(series: list[tuple[float, float]]) -> float | None:
    if len(series) < 2:
        return None
    return round(series[1][0] - series[0][0], 3)


def _evaluate_scs_row(
    definition: HydrographDefinition,
    basins_by_id: dict[str, Basin],
    storm: Storm,
) -> HydrographRowResult:
    basin = basins_by_id.get(definition.basin_id)
    series = generate_basin_hydrograph(basin, storm) if basin else [(0.0, 0.0)]
    status = "ok" if basin else "missing_basin"
    return _row_result(definition, storm, series, time_of_concentration_minutes(basin) if basin else None, status)


def _evaluate_combine_row(
    definition: HydrographDefinition,
    rows: dict[str, HydrographRowResult],
    storm: Storm,
) -> HydrographRowResult:
    combined: list[tuple[float, float]] = [(0.0, 0.0)]
    missing_ids = [inflow_id for inflow_id in definition.inflow_ids if inflow_id not in rows]
    for inflow_id in definition.inflow_ids:
        inflow = rows.get(inflow_id)
        if inflow:
            combined = add_hydrographs(combined, points_to_series(inflow.points))

    status = "ok" if not missing_ids else f"missing_inflows:{','.join(missing_ids)}"
    return _row_result(definition, storm, combined, None, status)


def _evaluate_reservoir_row(
    definition: HydrographDefinition,
    rows: dict[str, HydrographRowResult],
    storm: Storm,
) -> HydrographRowResult:
    if len(definition.inflow_ids) != 1:
        return _row_result(definition, storm, [(0.0, 0.0)], None, "invalid_inflow_count")

    inflow = rows.get(definition.inflow_ids[0])
    if inflow is None:
        return _row_result(definition, storm, [(0.0, 0.0)], None, f"missing_inflow:{definition.inflow_ids[0]}")

    inflow_series = points_to_series(inflow.points)
    if len(inflow_series) < 2:
        return _row_result(definition, storm, inflow_series or [(0.0, 0.0)], None, "invalid_inflow_hydrograph")

    statuses: list[str] = []
    stage_storage = _parse_stage_storage(definition.parameters)
    if len(stage_storage) < 2:
        return _row_result(definition, storm, [(time, 0.0) for time, _ in inflow_series], None, "invalid_stage_storage")

    if any(right.elevation_ft <= left.elevation_ft or right.storage_cuft < left.storage_cuft for left, right in zip(stage_storage, stage_storage[1:])):
        return _row_result(definition, storm, [(time, 0.0) for time, _ in inflow_series], None, "invalid_stage_storage")

    rating = _build_outlet_rating_curve(stage_storage, definition.parameters, statuses)
    if not any(point.discharge_cfs > 0 for point in rating):
        return _row_result(definition, storm, [(time, 0.0) for time, _ in inflow_series], None, "no_active_outlets")

    routed_series, storage_series, elevation_series, route_statuses = _route_storage_indication(inflow_series, rating)
    statuses.extend(route_statuses)
    status = ";".join(dict.fromkeys(statuses)) if statuses else "ok"

    result = _row_result(definition, storm, routed_series, None, status)
    result.maximum_elevation_ft = round(max(elevation_series), 3) if elevation_series else None
    result.maximum_storage_cuft = round(max(storage_series), 3) if storage_series else None
    result.outlet_rating = [
        {
            "stage_ft": point.elevation_ft,
            "storage_cuft": point.storage_cuft,
            "discharge_cfs": point.discharge_cfs,
        }
        for point in rating
    ]
    return result


def _unsupported_row(definition: HydrographDefinition, storm: Storm) -> HydrographRowResult:
    return _row_result(definition, storm, [(0.0, 0.0)], None, f"unsupported_type:{definition.type}")


def _row_result(
    definition: HydrographDefinition,
    storm: Storm,
    series: list[tuple[float, float]],
    tc_minutes: float | None,
    status: str,
) -> HydrographRowResult:
    return HydrographRowResult(
        id=definition.id,
        type=definition.type,
        description=definition.description,
        storm_id=storm.id,
        basin_id=definition.basin_id,
        inflow_ids=definition.inflow_ids,
        downstream_id=definition.downstream_id,
        points=series_to_points(series),
        peak_flow_cfs=peak_flow(series),
        time_to_peak_minutes=peak_time(series),
        time_interval_minutes=time_interval_minutes(series),
        tc_minutes=tc_minutes,
        volume_cuft=hydrograph_volume_cuft(series),
        status=status,
    )


def _parse_stage_storage(parameters: dict) -> list[StageStoragePoint]:
    points: list[StageStoragePoint] = []
    for row in _parameter_rows(parameters.get("stage_storage")):
        elevation = _to_float(row.get("elevation"))
        storage = _to_float(row.get("volume") or row.get("storage") or row.get("storage_cuft"))
        if elevation is None or storage is None:
            continue
        points.append(StageStoragePoint(elevation, max(storage, 0.0)))
    return sorted(points, key=lambda point: point.elevation_ft)


def _build_outlet_rating_curve(
    stage_storage: list[StageStoragePoint],
    parameters: dict,
    statuses: list[str],
) -> list[OutletRatingPoint]:
    _append_reservoir_method_statuses(parameters, statuses)
    rating_storage_points = _densify_stage_storage_points(stage_storage)
    return [
        OutletRatingPoint(
            elevation_ft=point.elevation_ft,
            storage_cuft=point.storage_cuft,
            discharge_cfs=max(_outlet_discharge_cfs(point.elevation_ft, parameters, statuses), 0.0),
        )
        for point in rating_storage_points
    ]


def _densify_stage_storage_points(stage_storage: list[StageStoragePoint]) -> list[StageStoragePoint]:
    if len(stage_storage) < 2:
        return stage_storage

    result: list[StageStoragePoint] = [stage_storage[0]]
    for left, right in zip(stage_storage, stage_storage[1:]):
        stage_span = right.elevation_ft - left.elevation_ft
        storage_span = right.storage_cuft - left.storage_cuft
        if stage_span <= 0:
            continue

        step_count = max(int(math.ceil(stage_span / 0.1)), 1)
        for step in range(1, step_count):
            fraction = step / step_count
            result.append(
                StageStoragePoint(
                    elevation_ft=round(left.elevation_ft + (stage_span * fraction), 6),
                    storage_cuft=left.storage_cuft + (storage_span * fraction),
                )
            )
        result.append(right)

    return result


def _append_reservoir_method_statuses(parameters: dict, statuses: list[str]) -> None:
    if _has_yes_row_value(parameters.get("weirs"), "Multi-Stage") or _has_yes_row_value(parameters.get("culverts_orifices"), "Multi-Stage"):
        statuses.append("multi_stage_culvert_a_backpressure")
    if (_to_float(parameters.get("exfiltration_rate")) or 0.0) > 0:
        statuses.append("exfiltration_not_applied")


def _outlet_discharge_cfs(stage_elevation: float, parameters: dict, statuses: list[str]) -> float:
    tailwater = _to_float(parameters.get("tailwater_elevation")) or 0.0
    has_multi_stage = _has_yes_row_value(parameters.get("weirs"), "Multi-Stage") or _has_yes_row_value(parameters.get("culverts_orifices"), "Multi-Stage")
    if not has_multi_stage:
        total = 0.0
        total += _weir_discharge_cfs(stage_elevation, tailwater, parameters, statuses)
        total += _culvert_orifice_discharge_cfs(stage_elevation, tailwater, parameters, statuses)
        return total

    total = 0.0
    total += _weir_discharge_cfs(stage_elevation, tailwater, parameters, statuses, require_multi_stage=False)
    total += _culvert_orifice_discharge_cfs(stage_elevation, tailwater, parameters, statuses, columns=("b", "c"), include_riser=True, require_multi_stage=False)
    total += _multi_stage_culvert_a_discharge(stage_elevation, tailwater, parameters, statuses)
    return total


def _weir_discharge_cfs(
    stage_elevation: float,
    tailwater_elevation: float,
    parameters: dict,
    statuses: list[str],
    columns: tuple[str, ...] = ("a", "b", "c", "d"),
    require_multi_stage: bool | None = None,
) -> float:
    rows = _table_by_label(parameters.get("weirs"))
    total = 0.0
    for column in columns:
        if require_multi_stage is not None and _is_yes(_cell(rows, "Multi-Stage", column, "No")) != require_multi_stage:
            continue
        if not _is_yes(_cell(rows, "Active", column, "Yes")):
            continue

        weir_type = _cell(rows, "Weir Type", column).strip()
        if not weir_type or weir_type.lower().startswith("choose"):
            continue
        if column != "a" and weir_type.lower() == "riser":
            statuses.append("ignored_weir_riser_not_a")
            continue

        crest = _to_float(_cell(rows, "Crest Elev (ft)", column))
        if crest is None:
            statuses.append("ignored_incomplete_weir")
            continue
        head = max(stage_elevation - crest, 0.0)
        if head <= 0:
            continue

        if "v-notch" in weir_type.lower():
            angle = _parse_v_notch_degrees(weir_type)
            if angle <= 0:
                statuses.append("ignored_invalid_v_notch")
                continue
            discharge = 2.54 * math.tan(math.radians(angle) / 2.0) * (head ** 2.5)
        else:
            length = _to_float(_cell(rows, "Crest Length (ft)", column))
            coeff = _weir_coefficient(weir_type, rows, column, head)
            if length is None or length <= 0:
                statuses.append("ignored_incomplete_weir")
                continue
            discharge = coeff * length * (head ** 1.5)

        downstream_head = max(tailwater_elevation - crest, 0.0)
        if downstream_head > 0 and downstream_head < head and weir_type.lower() in {"rectangular", "cipoletti", "riser"} or (downstream_head > 0 and downstream_head < head and "v-notch" in weir_type.lower()):
            ratio = min(max(downstream_head / head, 0.0), 0.999999)
            discharge *= (1.0 - (ratio ** 1.5)) ** 0.385
            statuses.append("weir_submergence_applied")
        elif downstream_head >= head and head > 0:
            discharge = 0.0
            statuses.append("weir_submerged")

        total += max(discharge, 0.0)
    return total


def _culvert_orifice_discharge_cfs(
    stage_elevation: float,
    tailwater_elevation: float,
    parameters: dict,
    statuses: list[str],
    columns: tuple[str, ...] = ("a", "b", "c"),
    include_riser: bool = True,
    require_multi_stage: bool | None = None,
) -> float:
    rows = _table_by_label(parameters.get("culverts_orifices"))
    total = 0.0
    for column in columns:
        if require_multi_stage is not None and _is_yes(_cell(rows, "Multi-Stage", column, "No")) != require_multi_stage:
            continue
        if not _is_yes(_cell(rows, "Active", column, "Yes")):
            continue
        total += _standard_orifice_column_discharge(stage_elevation, tailwater_elevation, rows, column, statuses)
    if include_riser and (require_multi_stage is None or _is_yes(_cell(rows, "Multi-Stage", "riser", "No")) == require_multi_stage):
        total += _perforated_riser_discharge(stage_elevation, tailwater_elevation, rows, statuses)
    return total


def _weir_coefficient(weir_type: str, rows: dict[str, dict[str, str]], column: str, head: float) -> float:
    explicit_coefficient = _to_float(_cell(rows, "Weir Coeff.", column))
    if explicit_coefficient is not None:
        return explicit_coefficient

    if _is_broad_crested_weir(weir_type):
        breadth = _to_float(_cell(rows, "Breadth of Crest (ft)", column))
        if breadth is None:
            breadth = _to_float(_cell(rows, "Crest Breadth (ft)", column))
        if breadth is None:
            breadth = _to_float(_cell(rows, "Breadth (ft)", column))
        if breadth is not None and breadth > 0:
            return _broad_crested_weir_coefficient(head, breadth)
        return 2.60

    if weir_type.lower() in {"rectangular", "cipoletti", "riser"}:
        return 3.30

    return 3.30


def _is_broad_crested_weir(weir_type: str) -> bool:
    normalized = weir_type.strip().lower().replace("-", " ")
    return normalized == "broad crested"


def _broad_crested_weir_coefficient(head: float, breadth: float) -> float:
    head_index_low, head_index_high, head_fraction = _bracketing_indices(BROAD_CRESTED_WEIR_HEADS_FT, head)
    breadth_index_low, breadth_index_high, breadth_fraction = _bracketing_indices(BROAD_CRESTED_WEIR_BREADTHS_FT, breadth)

    lower_head_value = _interpolate_linear(
        BROAD_CRESTED_WEIR_COEFFICIENTS[head_index_low][breadth_index_low],
        BROAD_CRESTED_WEIR_COEFFICIENTS[head_index_low][breadth_index_high],
        breadth_fraction,
    )
    upper_head_value = _interpolate_linear(
        BROAD_CRESTED_WEIR_COEFFICIENTS[head_index_high][breadth_index_low],
        BROAD_CRESTED_WEIR_COEFFICIENTS[head_index_high][breadth_index_high],
        breadth_fraction,
    )
    return _interpolate_linear(lower_head_value, upper_head_value, head_fraction)


def _bracketing_indices(values: tuple[float, ...], value: float) -> tuple[int, int, float]:
    if value <= values[0]:
        return 0, 0, 0.0
    if value >= values[-1]:
        last_index = len(values) - 1
        return last_index, last_index, 0.0

    for index, upper in enumerate(values[1:], start=1):
        lower = values[index - 1]
        if value <= upper:
            fraction = (value - lower) / (upper - lower)
            return index - 1, index, fraction

    last_index = len(values) - 1
    return last_index, last_index, 0.0


def _interpolate_linear(left: float, right: float, fraction: float) -> float:
    return left + ((right - left) * fraction)


def _multi_stage_culvert_a_discharge(stage_elevation: float, tailwater_elevation: float, parameters: dict, statuses: list[str]) -> float:
    rows = _table_by_label(parameters.get("culverts_orifices"))
    if not _is_yes(_cell(rows, "Active", "a", "Yes")):
        statuses.append("ignored_multistage_missing_culvert_a")
        return 0.0

    culvert_a_invert = _to_float(_cell(rows, "Invert Elev. (ft)", "a"))
    lower_head = max(tailwater_elevation, culvert_a_invert or tailwater_elevation)
    upper_head = max(stage_elevation, lower_head)
    if upper_head <= lower_head:
        return 0.0

    def upstream_capacity(internal_head: float, status_sink: list[str]) -> float:
        total = 0.0
        total += _weir_discharge_cfs(stage_elevation, internal_head, parameters, status_sink, require_multi_stage=True)
        total += _culvert_orifice_discharge_cfs(stage_elevation, internal_head, parameters, status_sink, columns=("b", "c"), include_riser=True, require_multi_stage=True)
        return total

    def culvert_a_capacity(internal_head: float) -> float:
        return _standard_orifice_column_discharge(internal_head, tailwater_elevation, rows, "a", [])

    low_balance = upstream_capacity(lower_head, []) - culvert_a_capacity(lower_head)
    high_balance = upstream_capacity(upper_head, []) - culvert_a_capacity(upper_head)

    if low_balance <= 0:
        return max(upstream_capacity(lower_head, statuses), 0.0)
    if high_balance >= 0:
        statuses.append("multi_stage_culvert_a_controls")
        return max(culvert_a_capacity(upper_head), 0.0)

    low = lower_head
    high = upper_head
    for _ in range(60):
        mid = (low + high) / 2.0
        balance = upstream_capacity(mid, []) - culvert_a_capacity(mid)
        if abs(balance) < 1e-6:
            low = high = mid
            break
        if balance > 0:
            low = mid
        else:
            high = mid

    internal_head = (low + high) / 2.0
    upstream_q = upstream_capacity(internal_head, statuses)
    culvert_a_q = culvert_a_capacity(internal_head)
    return max(min(upstream_q, culvert_a_q), 0.0)


def _standard_orifice_column_discharge(stage_elevation: float, tailwater_elevation: float, rows: dict[str, dict], column: str, statuses: list[str]) -> float:
    invert = _to_float(_cell(rows, "Invert Elev. (ft)", column))
    rise_in = _to_float(_cell(rows, "Rise (in)", column))
    span_in = _to_float(_cell(rows, "Span (in)", column))
    barrels = _to_float(_cell(rows, "No. Barrels", column)) or 1.0
    coeff = _to_float(_cell(rows, "Orifice Coeff.", column)) or 0.60
    if invert is None or rise_in is None or span_in is None or rise_in <= 0 or span_in <= 0 or barrels <= 0:
        return 0.0

    depth = stage_elevation - invert
    if depth <= 0:
        return 0.0

    rise_ft = rise_in / 12.0
    span_ft = span_in / 12.0
    flow_depth = min(depth, rise_ft)
    area = _partial_opening_area_sqft(rise_ft, span_ft, flow_depth)
    if area <= 0:
        return 0.0

    centroid_elevation = invert + (flow_depth / 2.0 if flow_depth < rise_ft else rise_ft / 2.0)
    inlet_head = max(stage_elevation - centroid_elevation, 0.0)
    tailwater_head = max(stage_elevation - tailwater_elevation, 0.0) if tailwater_elevation > 0 else inlet_head
    if tailwater_elevation > 0 and tailwater_head < inlet_head:
        inlet_head = tailwater_head
    if inlet_head <= 0:
        return 0.0

    inlet_q = coeff * area * math.sqrt(2.0 * GRAVITY_FTPS2 * inlet_head) * barrels
    length = _to_float(_cell(rows, "Length (ft)", column)) or 0.0
    slope_percent = _to_float(_cell(rows, "Slope (%)", column)) or 0.0
    n_value = _to_float(_cell(rows, "N-Value", column)) or 0.013
    wetted_perimeter = _opening_wetted_perimeter_ft(rise_ft, span_ft, flow_depth)
    radius = area / wetted_perimeter if wetted_perimeter > 0 else 0.0
    if length > 0 and radius > 0:
        downstream_invert = invert - (length * slope_percent / 100.0)
        downstream_water_surface = tailwater_elevation if tailwater_elevation > 0 else downstream_invert
        outlet_head = max(stage_elevation - downstream_water_surface, 0.0)
        if outlet_head <= 0:
            return 0.0

        pipe_coefficient = _concrete_pipe_entrance_coefficient(rows, column, length, rise_in, span_in)
        if pipe_coefficient is not None:
            outlet_q = pipe_coefficient * area * math.sqrt(2.0 * GRAVITY_FTPS2 * outlet_head) * barrels
        else:
            k = 1.5 + ((29.0 * (n_value ** 2.0) * length) / (radius ** 1.33))
            outlet_q = area * math.sqrt((2.0 * GRAVITY_FTPS2 * outlet_head) / k) * barrels
        return max(min(inlet_q, outlet_q), 0.0)
    return max(inlet_q, 0.0)


def _concrete_pipe_entrance_coefficient(rows: dict[str, dict], column: str, length_ft: float, rise_in: float, span_in: float) -> float | None:
    entrance_type = _cell(rows, "Entrance Type", column).strip()
    if not entrance_type:
        entrance_type = _cell(rows, "Pipe Entrance", column).strip()
    if not entrance_type or length_ft <= 0 or abs(rise_in - span_in) > 1e-6:
        return None

    normalized = entrance_type.lower().replace("-", " ").replace("_", " ")
    if "bevel" in normalized:
        table = CONCRETE_PIPE_BEVELED_LIP_COEFFICIENTS
    elif "square" in normalized:
        table = CONCRETE_PIPE_SQUARE_CORNERED_COEFFICIENTS
    else:
        return None

    length_index_low, length_index_high, length_fraction = _bracketing_indices(CONCRETE_PIPE_LENGTHS_FT, length_ft)
    diameter_index_low, diameter_index_high, diameter_fraction = _bracketing_indices(CONCRETE_PIPE_DIAMETERS_IN, rise_in)
    lower_length_value = _interpolate_linear(
        table[length_index_low][diameter_index_low],
        table[length_index_low][diameter_index_high],
        diameter_fraction,
    )
    upper_length_value = _interpolate_linear(
        table[length_index_high][diameter_index_low],
        table[length_index_high][diameter_index_high],
        diameter_fraction,
    )
    return _interpolate_linear(lower_length_value, upper_length_value, length_fraction)


def _perforated_riser_discharge(stage_elevation: float, tailwater_elevation: float, rows: dict[str, dict], statuses: list[str]) -> float:
    if not _is_yes(_cell(rows, "Active", "riser", "Yes")):
        return 0.0
    invert = _to_float(_cell(rows, "Invert Elev. (ft)", "riser"))
    rise_in = _to_float(_cell(rows, "Rise (in)", "riser"))
    span_in = _to_float(_cell(rows, "Span (in)", "riser"))
    holes = _to_float(_cell(rows, "No. Barrels", "riser") or _cell(rows, "No. Holes", "riser")) or 0.0
    height = _to_float(_cell(rows, "Length (ft)", "riser") or _cell(rows, "Height (ft)", "riser"))
    if invert is None or rise_in is None or span_in is None or height is None or rise_in <= 0 or span_in <= 0 or holes <= 0 or height <= 0:
        return 0.0
    controlling_stage = min(stage_elevation, tailwater_elevation) if tailwater_elevation > 0 else stage_elevation
    head = max(controlling_stage - invert, 0.0)
    if head <= 0:
        return 0.0
    hole_area = _full_opening_area_sqft(rise_in / 12.0, span_in / 12.0)
    total_area = hole_area * holes
    return max(0.61 * ((2.0 * total_area) / (3.0 * height)) * math.sqrt(2.0 * GRAVITY_FTPS2) * (head ** 1.5), 0.0)


def _route_storage_indication(inflow: list[tuple[float, float]], rating: list[OutletRatingPoint]) -> tuple[list[tuple[float, float]], list[float], list[float], list[str]]:
    statuses: list[str] = []
    current_storage = rating[0].storage_cuft
    current_outflow = rating[0].discharge_cfs
    routed = [(inflow[0][0], round(current_outflow, 6))]
    storages = [current_storage]
    elevations = [rating[0].elevation_ft]

    for (time_1, inflow_1), (time_2, inflow_2) in zip(inflow, inflow[1:]):
        dt_seconds = max((time_2 - time_1) * 60.0, 0.0)
        if dt_seconds <= 0:
            statuses.append("invalid_time_interval")
            continue

        storage_minus_outflow = (2.0 * current_storage / dt_seconds) - current_outflow
        indication = inflow_1 + inflow_2 + storage_minus_outflow
        next_outflow, next_storage, next_elevation, exceeded_table = _lookup_storage_indication(rating, indication, dt_seconds)
        if exceeded_table:
            statuses.append("storage_exceeded_table")

        current_outflow = next_outflow
        current_storage = next_storage
        routed.append((time_2, round(current_outflow, 6)))
        storages.append(current_storage)
        elevations.append(next_elevation)

    return routed, storages, elevations, statuses


def _lookup_storage_indication(rating: list[OutletRatingPoint], indication: float, dt_seconds: float) -> tuple[float, float, float, bool]:
    curve = sorted(
        (
            ((2.0 * point.storage_cuft / dt_seconds) + point.discharge_cfs, point.discharge_cfs, point.storage_cuft, point.elevation_ft)
            for point in rating
        ),
        key=lambda item: item[0],
    )
    if indication <= curve[0][0]:
        _, outflow, storage, elevation = curve[0]
        return outflow, storage, elevation, False
    if indication >= curve[-1][0]:
        _, outflow, storage, elevation = curve[-1]
        tolerance = max(1e-6, abs(curve[-1][0]) * 0.001)
        return outflow, storage, elevation, indication > curve[-1][0] + tolerance

    for left, right in zip(curve, curve[1:]):
        left_indication, left_outflow, left_storage, left_elevation = left
        right_indication, right_outflow, right_storage, right_elevation = right
        if indication <= right_indication:
            span = right_indication - left_indication
            fraction = 0.0 if span <= 0 else (indication - left_indication) / span
            outflow = left_outflow + fraction * (right_outflow - left_outflow)
            storage = left_storage + fraction * (right_storage - left_storage)
            elevation = left_elevation + fraction * (right_elevation - left_elevation)
            return outflow, storage, elevation, False

    _, outflow, storage, elevation = curve[-1]
    return outflow, storage, elevation, True


def _bisect_storage(function, low: float, high: float) -> float:
    low_value = function(low)
    high_value = function(high)
    if low_value >= 0:
        return low
    if high_value <= 0:
        return high
    for _ in range(80):
        mid = (low + high) / 2.0
        mid_value = function(mid)
        if abs(mid_value) < 1e-6:
            return mid
        if mid_value > 0:
            high = mid
        else:
            low = mid
    return (low + high) / 2.0


def _discharge_at_storage(rating: list[OutletRatingPoint], storage: float) -> float:
    return _interpolate_by_storage(rating, storage, "discharge_cfs")


def _elevation_at_storage(rating: list[OutletRatingPoint], storage: float) -> float:
    return _interpolate_by_storage(rating, storage, "elevation_ft")


def _interpolate_by_storage(rating: list[OutletRatingPoint], storage: float, attribute: str) -> float:
    if storage <= rating[0].storage_cuft:
        return float(getattr(rating[0], attribute))
    for left, right in zip(rating, rating[1:]):
        if storage <= right.storage_cuft:
            span = right.storage_cuft - left.storage_cuft
            fraction = 0.0 if span <= 0 else (storage - left.storage_cuft) / span
            return float(getattr(left, attribute)) + fraction * (float(getattr(right, attribute)) - float(getattr(left, attribute)))
    left, right = rating[-2], rating[-1]
    span = right.storage_cuft - left.storage_cuft
    fraction = 0.0 if span <= 0 else (storage - left.storage_cuft) / span
    return float(getattr(left, attribute)) + fraction * (float(getattr(right, attribute)) - float(getattr(left, attribute)))


def _partial_opening_area_sqft(rise_ft: float, span_ft: float, depth_ft: float) -> float:
    if depth_ft >= rise_ft:
        return _full_opening_area_sqft(rise_ft, span_ft)
    if abs(rise_ft - span_ft) < 1e-9:
        radius = rise_ft / 2.0
        depth = min(max(depth_ft, 0.0), rise_ft)
        theta = 2.0 * math.acos((radius - depth) / radius)
        return 0.5 * (radius ** 2.0) * (theta - math.sin(theta))
    return span_ft * min(max(depth_ft, 0.0), rise_ft)


def _full_opening_area_sqft(rise_ft: float, span_ft: float) -> float:
    if abs(rise_ft - span_ft) < 1e-9:
        return math.pi * (rise_ft / 2.0) ** 2.0
    return rise_ft * span_ft


def _opening_wetted_perimeter_ft(rise_ft: float, span_ft: float, depth_ft: float) -> float:
    if depth_ft >= rise_ft:
        if abs(rise_ft - span_ft) < 1e-9:
            return math.pi * rise_ft
        return 2.0 * (rise_ft + span_ft)
    if abs(rise_ft - span_ft) < 1e-9:
        radius = rise_ft / 2.0
        depth = min(max(depth_ft, 0.0), rise_ft)
        theta = 2.0 * math.acos((radius - depth) / radius)
        return radius * theta
    return span_ft + (2.0 * min(max(depth_ft, 0.0), rise_ft))


def _table_by_label(value: object) -> dict[str, dict]:
    result: dict[str, dict] = {}
    for row in _parameter_rows(value):
        label = str(row.get("label", "")).strip().lower()
        if label:
            result[label] = row
    return result


def _cell(rows: dict[str, dict], label: str, column: str, default: str = "") -> str:
    row = rows.get(label.lower())
    if not row:
        return default
    if column == "riser":
        direct = row.get("riser")
        if direct not in (None, ""):
            return str(direct)
    return str(row.get(column, default) or default)


def _parameter_rows(value: object) -> list[dict]:
    if not isinstance(value, list):
        return []
    return [row for row in value if isinstance(row, dict)]


def _has_yes_row_value(value: object, label: str) -> bool:
    for row in _parameter_rows(value):
        if str(row.get("label", "")).strip().lower() != label.lower():
            continue
        if any(_is_yes(row.get(column)) for column in ("a", "b", "c", "d", "riser")):
            return True
    return False


def _to_float(value: object) -> float | None:
    if value is None:
        return None
    text = str(value).strip().replace(",", "")
    if not text or text.lower() in {"n/a", "na", "----", "---", "choose..."}:
        return None
    try:
        return float(text)
    except ValueError:
        return None


def _is_yes(value: object) -> bool:
    return str(value).strip().lower() in {"yes", "y", "true", "1", "active"}


def _parse_v_notch_degrees(weir_type: str) -> int:
    digits = "".join(character for character in weir_type.strip() if character.isdigit())
    return int(digits) if digits else 0


def _duration_depths_from_storm(storm: Storm) -> dict[float, float]:
    raw_depths = storm.raw.get("duration_depths_inches", {})
    result: dict[float, float] = {}
    for duration, depth in raw_depths.items():
        minutes = _duration_label_to_minutes(str(duration))
        if minutes is not None:
            result[minutes] = float(depth)
    return result


def _alternating_block_incremental_depths(
    duration_minutes: float,
    total_depth: float,
    duration_depths: dict[float, float],
    interval_minutes: float,
) -> list[float]:
    step_count = max(int(round(duration_minutes / interval_minutes)), 1)
    cumulative_by_duration = [(0.0, 0.0), *sorted(duration_depths.items()), (duration_minutes, total_depth)]
    unique: dict[float, float] = {}
    for duration, depth in cumulative_by_duration:
        if duration <= duration_minutes:
            unique[duration] = min(max(depth, 0.0), total_depth)
    durations, depths = zip(*sorted(unique.items()))

    uniform_cumulative = np.interp(
        [i * interval_minutes for i in range(step_count + 1)],
        durations,
        depths,
    )
    uniform_cumulative[-1] = total_depth
    increments = np.diff(uniform_cumulative)
    increments = np.maximum(increments, 0.0).tolist()
    increments.sort(reverse=True)

    arranged = [0.0] * step_count
    center = step_count // 2
    offsets = [0]
    for offset in range(1, step_count):
        offsets.extend([offset, -offset])

    for depth, offset in zip(increments, offsets):
        index = center + offset
        if 0 <= index < step_count:
            arranged[index] = float(depth)

    return arranged


def _duration_label_to_minutes(label: str) -> float | None:
    value = label.strip().lower().replace(" ", "-").replace(":", "")
    if value.endswith("-min"):
        return float(value.removesuffix("-min"))
    if value.endswith("-hr"):
        return float(value.removesuffix("-hr")) * 60.0
    if value.endswith("-day"):
        return float(value.removesuffix("-day")) * 24.0 * 60.0
    return None
