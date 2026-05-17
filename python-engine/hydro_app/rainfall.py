from __future__ import annotations

import csv
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any


@dataclass
class Atlas14DepthTable:
    metadata: dict[str, str] = field(default_factory=dict)
    ari_years: list[int] = field(default_factory=list)
    depths_by_duration: dict[str, dict[int, float]] = field(default_factory=dict)

    def rainfall_depth(self, duration: str, ari_years: int) -> float:
        normalized_duration = normalize_duration_label(duration)
        for label, depths in self.depths_by_duration.items():
            if normalize_duration_label(label) == normalized_duration:
                if ari_years not in depths:
                    raise ValueError(f"Atlas 14 CSV does not contain ARI {ari_years} years.")
                return depths[ari_years]

        available = ", ".join(self.depths_by_duration.keys())
        raise ValueError(f"Atlas 14 CSV does not contain duration '{duration}'. Available durations: {available}.")

    def to_dict(self) -> dict[str, Any]:
        return {
            "metadata": self.metadata,
            "ari_years": self.ari_years,
            "depths_by_duration": {
                duration: {str(ari): depth for ari, depth in depths.items()}
                for duration, depths in self.depths_by_duration.items()
            },
        }


def parse_atlas14_depth_csv(path: str | Path) -> Atlas14DepthTable:
    csv_path = Path(path)
    rows = list(csv.reader(csv_path.read_text(encoding="utf-8-sig").splitlines()))
    table = Atlas14DepthTable()
    current_section = "metadata"

    for row in rows:
        if not row:
            continue

        cells = [cell.strip() for cell in row]
        first = cells[0]
        if not first:
            continue

        if first.upper() == "PRECIPITATION FREQUENCY ESTIMATES":
            current_section = "frequency"
            continue

        if current_section == "metadata":
            _read_metadata_row(table.metadata, cells)
            continue

        if first.startswith("by duration for ARI"):
            table.ari_years = [int(float(cell)) for cell in cells[1:] if cell]
            continue

        if first.endswith(":") and table.ari_years:
            duration = first[:-1]
            depths = [float(cell) for cell in cells[1:] if cell]
            table.depths_by_duration[duration] = dict(zip(table.ari_years, depths))

    if not table.ari_years or not table.depths_by_duration:
        raise ValueError(f"File does not look like a NOAA Atlas 14 precipitation depth CSV: {csv_path}")

    return table


def atlas14_storm_from_csv(
    path: str | Path,
    duration: str,
    ari_years: int,
    distribution: str = "ATLAS14_ALTERNATING_BLOCK",
    time_step_minutes: int = 2,
) -> dict[str, Any]:
    table = parse_atlas14_depth_csv(path)
    return _atlas14_storm_from_table(table, path, duration, ari_years, distribution, time_step_minutes)


def atlas14_storms_from_csv(
    path: str | Path,
    duration: str,
    min_ari_years: int = 1,
    max_ari_years: int = 100,
    distribution: str = "ATLAS14_ALTERNATING_BLOCK",
    time_step_minutes: int = 2,
) -> list[dict[str, Any]]:
    table = parse_atlas14_depth_csv(path)
    ari_years = [ari for ari in table.ari_years if min_ari_years <= ari <= max_ari_years]
    if not ari_years:
        raise ValueError(f"Atlas 14 CSV does not contain ARIs between {min_ari_years} and {max_ari_years} years.")

    return [
        _atlas14_storm_from_table(table, path, duration, ari, distribution, time_step_minutes)
        for ari in ari_years
    ]


def _atlas14_storm_from_table(
    table: Atlas14DepthTable,
    path: str | Path,
    duration: str,
    ari_years: int,
    distribution: str,
    time_step_minutes: int,
) -> dict[str, Any]:
    depth = table.rainfall_depth(duration, ari_years)
    normalized_duration = normalize_duration_label(duration)
    location = table.metadata.get("Location name (ESRI Maps)", "")
    selected_duration_minutes = duration_to_minutes(duration)
    duration_depths = {
        label: depths[ari_years]
        for label, depths in table.depths_by_duration.items()
        if ari_years in depths and duration_to_minutes(label) <= selected_duration_minutes
    }

    return {
        "id": f"atlas14-{ari_years}yr-{normalized_duration}",
        "name": f"Atlas 14 {ari_years}-year {duration}",
        "source": "NOAA Atlas 14",
        "source_file": str(Path(path)),
        "location": location,
        "ari_years": ari_years,
        "duration": duration,
        "duration_minutes": selected_duration_minutes,
        "rainfall_depth_inches": depth,
        "duration_depths_inches": duration_depths,
        "distribution": distribution,
        "time_step_minutes": time_step_minutes,
    }


def normalize_duration_label(duration: str) -> str:
    return duration.strip().lower().replace(" ", "-").replace(":", "")


def duration_to_minutes(duration: str) -> int:
    value = normalize_duration_label(duration)
    if value.endswith("-min"):
        return int(float(value.removesuffix("-min")))
    if value.endswith("-hr"):
        return int(float(value.removesuffix("-hr")) * 60)
    if value.endswith("-day"):
        return int(float(value.removesuffix("-day")) * 24 * 60)
    raise ValueError(f"Unsupported Atlas 14 duration label: {duration}")


def _read_metadata_row(metadata: dict[str, str], cells: list[str]) -> None:
    text = cells[0]
    if ":" in text:
        key, value = text.split(":", 1)
        parts = [value.strip(), *[cell.strip() for cell in cells[1:] if cell.strip()]]
        metadata[key.strip()] = ", ".join(part for part in parts if part)
    elif text.startswith("NOAA Atlas"):
        metadata["Atlas version"] = text
    elif text.startswith("Point precipitation"):
        metadata["Report title"] = text