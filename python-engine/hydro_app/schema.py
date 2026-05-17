from __future__ import annotations

from dataclasses import dataclass, field
from typing import Any


@dataclass
class LandUseArea:
    id: str
    layer: str
    area_acres: float
    curve_number: float | None = None

    @classmethod
    def from_dict(cls, data: dict[str, Any]) -> "LandUseArea":
        return cls(
            id=str(data.get("id") or data.get("layer") or ""),
            layer=str(data.get("layer") or data.get("id") or ""),
            area_acres=float(data.get("area_acres") or 0),
            curve_number=_optional_float(data.get("curve_number")),
        )

    def to_dict(self) -> dict[str, Any]:
        return {
            "id": self.id,
            "layer": self.layer,
            "area_acres": self.area_acres,
            "curve_number": self.curve_number,
        }


@dataclass
class TcSegment:
    type: str
    length_ft: float | None = None
    slope: float | None = None
    manning_n: float | None = None

    @classmethod
    def from_dict(cls, data: dict[str, Any]) -> "TcSegment":
        return cls(
            type=str(data.get("type") or ""),
            length_ft=_optional_float(data.get("length_ft")),
            slope=_optional_float(data.get("slope")),
            manning_n=_optional_float(data.get("manning_n")),
        )


@dataclass
class Basin:
    id: str
    name: str
    handle: str = ""
    condition: str = ""
    boundary: str = ""
    development: str = ""
    area_acres: float = 0.0
    curve_number: float | None = None
    tc_minutes: float | None = None
    outlet_node: str = ""
    downstream_id: str = ""
    land_uses: list[LandUseArea] = field(default_factory=list)
    tc_segments: list[TcSegment] = field(default_factory=list)
    raw: dict[str, Any] = field(default_factory=dict)

    @classmethod
    def from_dict(cls, data: dict[str, Any]) -> "Basin":
        return cls(
            id=str(data.get("id") or ""),
            name=str(data.get("name") or data.get("id") or ""),
            handle=str(data.get("handle") or data.get("source_handle") or ""),
            condition=str(data.get("condition") or ""),
            boundary=str(data.get("boundary") or ""),
            development=str(data.get("development") or ""),
            area_acres=float(data.get("area_acres") or 0),
            curve_number=_optional_float(data.get("curve_number")),
            tc_minutes=_optional_float(data.get("tc_minutes")),
            outlet_node=str(data.get("outlet_node") or ""),
            downstream_id=str(data.get("downstream_id") or ""),
            land_uses=[LandUseArea.from_dict(item) for item in data.get("land_uses", [])],
            tc_segments=[TcSegment.from_dict(item) for item in data.get("tc_segments", [])],
            raw=data,
        )


@dataclass
class Link:
    id: str
    type: str = "basin_route"
    from_basin: str = ""
    to_basin: str = ""
    from_node: str = ""
    to_node: str = ""

    @classmethod
    def from_dict(cls, data: dict[str, Any]) -> "Link":
        return cls(
            id=str(data.get("id") or ""),
            type=str(data.get("type") or "basin_route"),
            from_basin=str(data.get("from_basin") or data.get("from") or ""),
            to_basin=str(data.get("to_basin") or data.get("to") or ""),
            from_node=str(data.get("from_node") or ""),
            to_node=str(data.get("to_node") or ""),
        )


@dataclass
class HydrographDefinition:
    id: str
    type: str
    description: str = ""
    basin_id: str = ""
    inflow_ids: list[str] = field(default_factory=list)
    downstream_id: str = ""
    parameters: dict[str, Any] = field(default_factory=dict)
    raw: dict[str, Any] = field(default_factory=dict)

    @classmethod
    def from_dict(cls, data: dict[str, Any]) -> "HydrographDefinition":
        return cls(
            id=str(data.get("id") or ""),
            type=str(data.get("type") or ""),
            description=str(data.get("description") or data.get("name") or ""),
            basin_id=str(data.get("basin_id") or ""),
            inflow_ids=[str(item) for item in data.get("inflow_ids", [])],
            downstream_id=str(data.get("downstream_id") or ""),
            parameters=dict(data.get("parameters", {})),
            raw=data,
        )

    def to_dict(self) -> dict[str, Any]:
        result = dict(self.raw)
        result.update(
            {
                "id": self.id,
                "type": self.type,
                "description": self.description,
                "basin_id": self.basin_id,
                "inflow_ids": self.inflow_ids,
                "downstream_id": self.downstream_id,
                "parameters": self.parameters,
            }
        )
        return result


@dataclass
class Storm:
    id: str
    name: str = ""
    rainfall_depth_inches: float | None = None
    duration_minutes: float | None = None
    ari_years: int | None = None
    distribution: str = ""
    time_step_minutes: int | None = None
    source: str = ""
    raw: dict[str, Any] = field(default_factory=dict)

    @classmethod
    def from_dict(cls, data: dict[str, Any]) -> "Storm":
        return cls(
            id=str(data.get("id") or ""),
            name=str(data.get("name") or data.get("id") or ""),
            rainfall_depth_inches=_optional_float(data.get("rainfall_depth_inches")),
            duration_minutes=_optional_float(data.get("duration_minutes")),
            ari_years=_optional_int(data.get("ari_years")),
            distribution=str(data.get("distribution") or ""),
            time_step_minutes=_optional_int(data.get("time_step_minutes")),
            source=str(data.get("source") or ""),
            raw=data,
        )

    def to_dict(self) -> dict[str, Any]:
        result = dict(self.raw)
        result.update(
            {
                "id": self.id,
                "name": self.name,
                "rainfall_depth_inches": self.rainfall_depth_inches,
                "duration_minutes": self.duration_minutes,
                "ari_years": self.ari_years,
                "distribution": self.distribution,
                "time_step_minutes": self.time_step_minutes,
                "source": self.source,
            }
        )
        return result


@dataclass
class Project:
    name: str = ""
    drawing_path: str = ""
    source: str = ""

    @classmethod
    def from_dict(cls, data: dict[str, Any]) -> "Project":
        return cls(
            name=str(data.get("name") or ""),
            drawing_path=str(data.get("drawing_path") or ""),
            source=str(data.get("source") or ""),
        )


@dataclass
class HydrologyModel:
    project: Project = field(default_factory=Project)
    storms: list[Storm] = field(default_factory=list)
    basins: list[Basin] = field(default_factory=list)
    hydrographs: list[HydrographDefinition] = field(default_factory=list)
    nodes: list[dict[str, Any]] = field(default_factory=list)
    links: list[Link] = field(default_factory=list)

    @classmethod
    def from_dict(cls, data: dict[str, Any]) -> "HydrologyModel":
        return cls(
            project=Project.from_dict(data.get("project", {})),
            storms=[Storm.from_dict(item) for item in data.get("storms", [])],
            basins=[Basin.from_dict(item) for item in data.get("basins", [])],
            hydrographs=[HydrographDefinition.from_dict(item) for item in data.get("hydrographs", [])],
            nodes=list(data.get("nodes", [])),
            links=[Link.from_dict(item) for item in data.get("links", [])],
        )


def _optional_float(value: Any) -> float | None:
    if value is None or value == "":
        return None
    return float(value)


def _optional_int(value: Any) -> int | None:
    if value is None or value == "":
        return None
    return int(float(value))