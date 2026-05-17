from __future__ import annotations

from hydro_app.schema import Basin, TcSegment


def time_of_concentration_minutes(basin: Basin) -> float | None:
    if basin.tc_minutes is not None:
        return round(basin.tc_minutes, 2)

    if not basin.tc_segments:
        return None

    total = 0.0
    for segment in basin.tc_segments:
        value = segment_minutes(segment)
        if value is None:
            return None
        total += value
    return round(total, 2)


def segment_minutes(segment: TcSegment) -> float | None:
    if segment.length_ft is None or segment.length_ft <= 0:
        return None

    slope = segment.slope or 0
    if slope <= 0:
        return None

    segment_type = segment.type.lower()
    if segment_type == "sheet":
        roughness = segment.manning_n or 0.24
        return (0.007 * roughness * segment.length_ft) / (slope**0.4)
    if segment_type in {"shallow", "shallow_concentrated"}:
        velocity_fps = 16.1345 * (slope**0.5)
        return segment.length_ft / velocity_fps / 60
    if segment_type == "channel":
        roughness = segment.manning_n or 0.035
        hydraulic_radius = 1.0
        velocity_fps = (1.49 / roughness) * (hydraulic_radius ** (2 / 3)) * (slope**0.5)
        return segment.length_ft / velocity_fps / 60
    return None