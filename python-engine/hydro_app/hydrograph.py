from __future__ import annotations

from dataclasses import dataclass
from functools import lru_cache
from pathlib import Path
from typing import Iterable

import numpy as np
from pyflo.nrcs.hydrology import Basin as PyfloNrcsBasin

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
NRCS_PEAK_FACTOR = 484.0 * 60.0 / 640.0
SUPPORTED_HYDROGRAPH_TYPES = {"SCS", "COMBINE"}
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
    pyflo_basin = PyfloNrcsBasin(
        area=basin.area_acres,
        cn=curve_number,
        tc=tc_minutes,
        runoff_dist=NRCS_TRIANGULAR_RUNOFF_DISTRIBUTION,
        peak_factor=NRCS_PEAK_FACTOR,
    )
    hydrograph = pyflo_basin.flood_hydrograph(rain_depths, interval)
    return [(round(float(time), 6), round(max(float(flow), 0.0), 6)) for time, flow in hydrograph.tolist()]


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
    pyflo_basin = PyfloNrcsBasin(
        area=1.0,
        cn=curve_number,
        tc=5.0,
        runoff_dist=NRCS_TRIANGULAR_RUNOFF_DISTRIBUTION,
        peak_factor=NRCS_PEAK_FACTOR,
    )
    return pyflo_basin.runoff_depth(rainfall_depth_inches)


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
