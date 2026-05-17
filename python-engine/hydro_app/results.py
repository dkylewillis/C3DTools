from __future__ import annotations

from hydro_app.curve_number import composite_curve_number, impervious_percent
from hydro_app.hydrograph import generate_hydrograph_rows, generate_model_hydrographs
from hydro_app.network import route_map, upstream_map
from hydro_app.schema import HydrologyModel
from hydro_app.tc import time_of_concentration_minutes
from hydro_app.validators import ValidationReport


def build_results(model: HydrologyModel, validation: ValidationReport) -> dict:
    basin_results = []
    storms_with_depth = [storm for storm in model.storms if storm.rainfall_depth_inches is not None]
    active_storm = storms_with_depth[-1] if storms_with_depth else None
    active_hydrographs = generate_model_hydrographs(model, active_storm) if active_storm else {}
    active_hydrograph_rows = generate_hydrograph_rows(model, active_storm) if active_storm else {}
    routes = route_map(model)
    upstream = upstream_map(model)
    peak_flow_results = build_peak_flow_results(model)

    for basin in model.basins:
        curve_number = composite_curve_number(basin)
        tc_minutes = time_of_concentration_minutes(basin)
        basin_results.append(
            {
                "id": basin.id,
                "name": basin.name,
                "handle": basin.handle,
                "condition": basin.condition,
                "area_acres": basin.area_acres,
                "curve_number": curve_number,
                "tc_minutes": tc_minutes,
                "impervious_percent": impervious_percent(basin),
                "land_use_count": len(basin.land_uses),
                "downstream_id": routes.get(basin.id, basin.downstream_id),
                "upstream_ids": sorted(upstream.get(basin.id, [])),
                "local_hydrograph_id": active_hydrographs.get(basin.id).local_hydrograph_id if basin.id in active_hydrographs else "",
                "routed_hydrograph_id": active_hydrographs.get(basin.id).routed_hydrograph_id if basin.id in active_hydrographs else "",
                "local_peak_cfs": active_hydrographs.get(basin.id).local_peak_cfs if basin.id in active_hydrographs else None,
                "routed_peak_cfs": active_hydrographs.get(basin.id).routed_peak_cfs if basin.id in active_hydrographs else None,
                "local_peak_time_minutes": active_hydrographs.get(basin.id).local_peak_time_minutes if basin.id in active_hydrographs else None,
                "routed_peak_time_minutes": active_hydrographs.get(basin.id).routed_peak_time_minutes if basin.id in active_hydrographs else None,
                "local_hydrograph": active_hydrographs.get(basin.id).local_points if basin.id in active_hydrographs else [],
                "routed_hydrograph": active_hydrographs.get(basin.id).routed_points if basin.id in active_hydrographs else [],
                "status": "ok" if curve_number is not None else "needs_curve_number",
            }
        )

    return {
        "project": {
            "name": model.project.name,
            "source": model.project.source,
        },
        "summary": {
            "basin_count": len(model.basins),
            "total_area_acres": round(sum(basin.area_acres for basin in model.basins), 6),
            "error_count": len(validation.errors),
            "warning_count": len(validation.warnings),
        },
        "routing": {
            "link_count": len([link for link in model.links if link.type.lower() == "basin_route"]),
            "routed_basin_count": len([basin_id for basin_id, downstream in routes.items() if downstream]),
        },
        "storms": [storm.to_dict() for storm in model.storms],
        "active_storm_id": active_storm.id if active_storm else "default-1in",
        "hydrographs": [row.to_dict() for row in active_hydrograph_rows.values()],
        "peak_flows": peak_flow_results,
        "hydrograph_peak_flows": build_hydrograph_peak_flow_results(model),
        "basins": basin_results,
        "validation": validation.to_dict(),
    }


def build_peak_flow_results(model: HydrologyModel) -> list[dict]:
    storms = [storm for storm in model.storms if storm.rainfall_depth_inches is not None]
    results: list[dict] = []

    for storm in storms:
        hydrographs = generate_model_hydrographs(model, storm)
        for basin in model.basins:
            hydrograph = hydrographs.get(basin.id)
            results.append(
                {
                    "storm_id": storm.id,
                    "storm_name": storm.name,
                    "ari_years": storm.ari_years,
                    "duration_minutes": storm.duration_minutes,
                    "rainfall_depth_inches": storm.rainfall_depth_inches,
                    "basin_id": basin.id,
                    "local_peak_cfs": hydrograph.local_peak_cfs if hydrograph else None,
                    "routed_peak_cfs": hydrograph.routed_peak_cfs if hydrograph else None,
                    "local_peak_time_minutes": hydrograph.local_peak_time_minutes if hydrograph else None,
                    "routed_peak_time_minutes": hydrograph.routed_peak_time_minutes if hydrograph else None,
                }
            )

    return results


def build_hydrograph_peak_flow_results(model: HydrologyModel) -> list[dict]:
    storms = [storm for storm in model.storms if storm.rainfall_depth_inches is not None]
    results: list[dict] = []

    for storm in storms:
        hydrographs = generate_hydrograph_rows(model, storm)
        for hydrograph in hydrographs.values():
            results.append(
                {
                    "storm_id": storm.id,
                    "storm_name": storm.name,
                    "ari_years": storm.ari_years,
                    "duration_minutes": storm.duration_minutes,
                    "rainfall_depth_inches": storm.rainfall_depth_inches,
                    "hydrograph_id": hydrograph.id,
                    "hydrograph_type": hydrograph.type,
                    "basin_id": hydrograph.basin_id,
                    "inflow_ids": hydrograph.inflow_ids,
                    "peak_flow_cfs": hydrograph.peak_flow_cfs,
                    "time_to_peak_minutes": hydrograph.time_to_peak_minutes,
                    "volume_cuft": hydrograph.volume_cuft,
                    "maximum_elevation_ft": hydrograph.maximum_elevation_ft,
                    "maximum_storage_cuft": hydrograph.maximum_storage_cuft,
                    "status": hydrograph.status,
                }
            )

    return results