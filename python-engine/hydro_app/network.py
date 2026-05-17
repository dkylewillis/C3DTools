from __future__ import annotations

from collections import defaultdict, deque

from hydro_app.schema import Basin, HydrologyModel, Link


TERMINAL_IDS = {"", "OUTLET", "OUTFALL", "NONE"}


def route_map(model: HydrologyModel) -> dict[str, str]:
    routes: dict[str, str] = {}
    for basin in model.basins:
        downstream = basin.downstream_id.strip()
        if downstream:
            routes[basin.id] = downstream

    for link in model.links:
        if link.type.lower() != "basin_route":
            continue
        if link.from_basin:
            routes[link.from_basin] = link.to_basin

    return routes


def upstream_map(model: HydrologyModel) -> dict[str, list[str]]:
    upstream: dict[str, list[str]] = defaultdict(list)
    for upstream_id, downstream_id in route_map(model).items():
        if downstream_id.upper() not in TERMINAL_IDS:
            upstream[downstream_id].append(upstream_id)
    return dict(upstream)


def find_invalid_downstream_references(model: HydrologyModel) -> list[tuple[str, str]]:
    basin_ids = {basin.id for basin in model.basins}
    invalid: list[tuple[str, str]] = []
    for upstream_id, downstream_id in route_map(model).items():
        if downstream_id.upper() in TERMINAL_IDS:
            continue
        if downstream_id not in basin_ids:
            invalid.append((upstream_id, downstream_id))
    return invalid


def find_routing_cycles(model: HydrologyModel) -> list[list[str]]:
    routes = route_map(model)
    cycles: list[list[str]] = []
    basin_ids = {basin.id for basin in model.basins}

    for start_id in sorted(basin_ids):
        seen_order: list[str] = []
        seen_index: dict[str, int] = {}
        current = start_id

        while current in routes:
            if current in seen_index:
                cycle = seen_order[seen_index[current]:]
                if cycle and not _cycle_already_recorded(cycles, cycle):
                    cycles.append(cycle)
                break

            seen_index[current] = len(seen_order)
            seen_order.append(current)
            downstream = routes[current]
            if downstream.upper() in TERMINAL_IDS or downstream not in basin_ids:
                break
            current = downstream

    return cycles


def topological_basin_order(model: HydrologyModel) -> list[str]:
    basin_ids = {basin.id for basin in model.basins}
    upstream = upstream_map(model)
    indegree = {basin_id: 0 for basin_id in basin_ids}
    for downstream_id, upstream_ids in upstream.items():
        if downstream_id in indegree:
            indegree[downstream_id] += len([item for item in upstream_ids if item in basin_ids])

    queue = deque(sorted([basin_id for basin_id, degree in indegree.items() if degree == 0]))
    order: list[str] = []
    routes = route_map(model)

    while queue:
        basin_id = queue.popleft()
        order.append(basin_id)
        downstream_id = routes.get(basin_id, "")
        if downstream_id in indegree:
            indegree[downstream_id] -= 1
            if indegree[downstream_id] == 0:
                queue.append(downstream_id)

    remaining = sorted([basin_id for basin_id in basin_ids if basin_id not in order])
    return order + remaining


def _cycle_already_recorded(cycles: list[list[str]], cycle: list[str]) -> bool:
    cycle_set = set(cycle)
    return any(set(existing) == cycle_set for existing in cycles)