from __future__ import annotations

import re

from hydro_app.schema import Basin


def composite_curve_number(basin: Basin) -> float | None:
    if basin.curve_number is not None:
        return round(basin.curve_number, 2)

    weighted_sum = 0.0
    total_area = 0.0
    for land_use in basin.land_uses:
        curve_number = land_use.curve_number or infer_curve_number(land_use.layer)
        if curve_number is None or land_use.area_acres <= 0:
            continue
        weighted_sum += curve_number * land_use.area_acres
        total_area += land_use.area_acres

    if total_area <= 0:
        return None

    return round(weighted_sum / total_area, 2)


def impervious_percent(basin: Basin) -> float | None:
    if not basin.land_uses or basin.area_acres <= 0:
        return None

    impervious_area = sum(
        land_use.area_acres
        for land_use in basin.land_uses
        if (land_use.curve_number or infer_curve_number(land_use.layer) or 0) >= 95
    )
    return round((impervious_area / basin.area_acres) * 100, 2)


def infer_curve_number(layer: str) -> float | None:
    match = re.search(r"(?:^|[^0-9])CN[\s_-]*(\d{2})(?:[^0-9]|$)", layer, re.IGNORECASE)
    if match:
        return float(match.group(1))

    upper = layer.upper()
    if "IMP" in upper or "PAV" in upper or "ROOF" in upper:
        return 98.0
    if "OPEN" in upper or "OPNSP" in upper or "GRASS" in upper:
        return 61.0
    if "WOOD" in upper or "FOREST" in upper:
        return 55.0
    return None