from pathlib import Path
import math

import pytest

from hydro_app.curve_number import composite_curve_number
from hydro_app.hydrograph import OutletRatingPoint, StageStoragePoint, design_storm_cumulative_rainfall, generate_basin_hydrograph, hydrograph_volume_cuft, scs_peak_time_minutes, scs_runoff_depth_inches, scs_unit_hydrograph, _build_outlet_rating_curve, _outlet_discharge_cfs, _route_storage_indication, _standard_orifice_column_discharge, _table_by_label
from hydro_app.network import find_invalid_downstream_references, find_routing_cycles
from hydro_app.rainfall import atlas14_storm_from_csv, atlas14_storms_from_csv, parse_atlas14_depth_csv
from hydro_app.results import build_results
from hydro_app.schema import Basin, HydrologyModel, LandUseArea, Link, Storm
from hydro_app.validators import validate_model


ATLAS14_SAMPLE = Path(__file__).parents[2] / "data" / "PF_Depth_English_PDS.csv"


def test_composite_curve_number_uses_weighted_land_use_areas():
    basin = Basin(
        id="B-1",
        name="Basin 1",
        area_acres=3,
        land_uses=[
            LandUseArea(id="open", layer="OPEN", area_acres=2, curve_number=60),
            LandUseArea(id="imp", layer="IMP", area_acres=1, curve_number=98),
        ],
    )

    assert composite_curve_number(basin) == 72.67


def test_validator_requires_positive_basin_area():
    model = HydrologyModel(basins=[Basin(id="B-1", name="Basin 1")])

    report = validate_model(model)

    assert not report.is_valid
    assert "non-positive area_acres" in report.errors[0]


def test_network_reports_missing_downstream_basin():
    model = HydrologyModel(
        basins=[Basin(id="B-1", name="Basin 1", area_acres=1, downstream_id="B-2")]
    )

    assert find_invalid_downstream_references(model) == [("B-1", "B-2")]


def test_network_detects_routing_cycle():
    model = HydrologyModel(
        basins=[
            Basin(id="B-1", name="Basin 1", area_acres=1),
            Basin(id="B-2", name="Basin 2", area_acres=1),
        ],
        links=[
            Link(id="1", from_basin="B-1", to_basin="B-2"),
            Link(id="2", from_basin="B-2", to_basin="B-1"),
        ],
    )

    assert find_routing_cycles(model) == [["B-1", "B-2"]]


def test_atlas14_depth_csv_parses_attached_noaa_format():
    table = parse_atlas14_depth_csv(ATLAS14_SAMPLE)

    assert table.metadata["Location name (ESRI Maps)"] == "Peachtree City, Georgia, USA"
    assert table.ari_years[:4] == [1, 2, 5, 10]
    assert table.rainfall_depth("24-hr", 10) == 5.38
    assert table.rainfall_depth("60-min", 100) == 3.40


def test_atlas14_csv_creates_storm_metadata():
    storm = atlas14_storm_from_csv(ATLAS14_SAMPLE, "24-hr", 25)

    assert storm["id"] == "atlas14-25yr-24-hr"
    assert storm["rainfall_depth_inches"] == 6.44
    assert storm["duration_minutes"] == 1440
    assert storm["distribution"] == "ATLAS14_ALTERNATING_BLOCK"


def test_atlas14_csv_creates_available_storms_from_1yr_through_100yr():
    storms = atlas14_storms_from_csv(ATLAS14_SAMPLE, "24-hr", 1, 100)

    assert [storm["ari_years"] for storm in storms] == [1, 2, 5, 10, 25, 50, 100]
    assert storms[0]["rainfall_depth_inches"] == 3.37
    assert storms[-1]["rainfall_depth_inches"] == 8.22


def test_results_include_peak_flow_rows_for_each_storm_and_basin():
    storms = atlas14_storms_from_csv(ATLAS14_SAMPLE, "24-hr", 1, 100)
    model = HydrologyModel.from_dict(
        {
            "storms": storms,
            "basins": [
                {
                    "id": "B-1",
                    "name": "Basin 1",
                    "area_acres": 3,
                    "curve_number": 75,
                    "tc_minutes": 15,
                    "land_uses": [],
                    "tc_segments": [],
                }
            ],
        }
    )

    results = build_results(model, validate_model(model))

    assert len(results["peak_flows"]) == 7
    assert results["peak_flows"][0]["ari_years"] == 1
    assert results["peak_flows"][-1]["ari_years"] == 100
    assert results["active_storm_id"] == "atlas14-100yr-24-hr"


def test_results_include_scs_hydrographs_and_routed_peaks():
    model = HydrologyModel.from_dict(
        {
            "storms": [
                {
                    "id": "design-10yr",
                    "name": "10 year design storm",
                    "rainfall_depth_inches": 5.0,
                    "duration_minutes": 60,
                    "time_step_minutes": 5,
                    "duration_depths_inches": {"5-min": 0.5, "60-min": 5.0},
                }
            ],
            "basins": [
                {"id": "B-1", "name": "Basin 1", "area_acres": 2, "curve_number": 80, "tc_minutes": 10},
                {"id": "B-2", "name": "Basin 2", "area_acres": 3, "curve_number": 82, "tc_minutes": 15},
            ],
            "links": [{"id": "R-1", "type": "basin_route", "from_basin": "B-1", "to_basin": "B-2"}],
        }
    )

    results = build_results(model, validate_model(model))
    upstream = next(basin for basin in results["basins"] if basin["id"] == "B-1")
    downstream = next(basin for basin in results["basins"] if basin["id"] == "B-2")

    assert upstream["local_hydrograph"]
    assert upstream["routed_hydrograph"]
    assert upstream["local_hydrograph_id"] == "H1"
    assert downstream["routed_hydrograph_id"] == "H3"
    assert [hydrograph["type"] for hydrograph in results["hydrographs"]] == ["SCS", "SCS", "Combine"]
    assert upstream["local_peak_cfs"] == max(point["flow_cfs"] for point in upstream["local_hydrograph"])
    assert downstream["routed_peak_cfs"] >= downstream["local_peak_cfs"]
    assert results["peak_flows"][0]["local_peak_time_minutes"] is not None


def test_scs_hydrograph_volume_matches_runoff_depth_units():
    basin = Basin(id="B-1", name="Basin 1", area_acres=10, curve_number=80, tc_minutes=20)
    storm = Storm.from_dict(
        {
            "id": "design",
            "rainfall_depth_inches": 4.0,
            "duration_minutes": 120,
            "time_step_minutes": 5,
            "duration_depths_inches": {"5-min": 0.2, "120-min": 4.0},
        }
    )

    series = generate_basin_hydrograph(basin, storm)
    actual_volume = hydrograph_volume_cuft(series)
    expected_volume = scs_runoff_depth_inches(4.0, 80) * basin.area_acres * 43560.0 / 12.0

    assert actual_volume is not None
    assert actual_volume == pytest.approx(expected_volume, rel=0.03)


def test_scs_hydrograph_defaults_to_two_minute_time_step():
    basin = Basin(id="B-1", name="Basin 1", area_acres=2, curve_number=80, tc_minutes=10)
    storm = Storm.from_dict(
        {
            "id": "design",
            "rainfall_depth_inches": 4.0,
            "duration_minutes": 60,
            "duration_depths_inches": {"5-min": 0.2, "60-min": 4.0},
        }
    )

    series = generate_basin_hydrograph(basin, storm)

    assert series[1][0] - series[0][0] == pytest.approx(2.0)


def test_scs_unit_hydrograph_uses_hydraflow_peak_time_equation():
    assert scs_peak_time_minutes(20.0, 5.0) == pytest.approx((20.0 + 5.0) / 1.7)


def test_scs_unit_hydrograph_uses_hydraflow_peak_flow_and_time_base():
    area_acres = 40.0
    tc_minutes = 37.5
    interval_minutes = 5.0

    unit_hydrograph = scs_unit_hydrograph(area_acres, tc_minutes, interval_minutes)
    peak_time = (tc_minutes + interval_minutes) / 1.7
    peak_flow = 484.0 * (area_acres / 640.0) / (peak_time / 60.0)
    time_base = 2.67 * peak_time

    assert max(flow for _, flow in unit_hydrograph) == pytest.approx(peak_flow)
    assert unit_hydrograph[-1][0] >= time_base
    assert unit_hydrograph[-1][0] < time_base + interval_minutes


def test_atlas14_alternating_block_remains_selectable_distribution():
    storm = Storm.from_dict(
        {
            "id": "design",
            "rainfall_depth_inches": 4.0,
            "duration_minutes": 60,
            "time_step_minutes": 5,
            "distribution": "ATLAS14_ALTERNATING_BLOCK",
            "duration_depths_inches": {"5-min": 0.2, "60-min": 4.0},
        }
    )

    rainfall = design_storm_cumulative_rainfall(storm, 5)

    assert rainfall[-1][1] == pytest.approx(4.0)
    assert max(rainfall[index + 1][1] - rainfall[index][1] for index in range(len(rainfall) - 1)) > 0.2


def test_type_ii_24hr_tabular_distribution_uses_tbl_curve():
    storm = Storm.from_dict(
        {
            "id": "type-ii",
            "rainfall_depth_inches": 5.0,
            "duration_minutes": 1440,
            "time_step_minutes": 6,
            "distribution": "TR55_TYPE_II_24HR_TABULAR",
        }
    )

    rainfall = design_storm_cumulative_rainfall(storm, 6)

    assert rainfall[0][1] == pytest.approx(0.0)
    assert rainfall[-1][0] == pytest.approx(1440.0)
    assert rainfall[-1][1] == pytest.approx(5.0)
    assert rainfall[120][1] == pytest.approx(5.0 * 0.6630)


def test_type_ii_24hr_tabular_distribution_uses_polynomial_at_two_minutes():
    storm = Storm.from_dict(
        {
            "id": "type-ii",
            "rainfall_depth_inches": 5.0,
            "duration_minutes": 1440,
            "time_step_minutes": 2,
            "distribution": "TR55_TYPE_II_24HR_TABULAR",
        }
    )

    rainfall = design_storm_cumulative_rainfall(storm, 2)
    increments = [rainfall[index + 1][1] - rainfall[index][1] for index in range(len(rainfall) - 1)]

    assert rainfall[1][0] == pytest.approx(2.0)
    assert rainfall[360][1] == pytest.approx(5.0 * 0.6630)
    assert rainfall[-1][0] == pytest.approx(1440.0)
    assert rainfall[-1][1] == pytest.approx(5.0)
    assert min(increments) >= -1e-12


def test_type_iii_24hr_tabular_distribution_uses_matching_tbl_curve():
    storm = Storm.from_dict(
        {
            "id": "type-iii",
            "rainfall_depth_inches": 5.0,
            "duration_minutes": 1440,
            "time_step_minutes": 6,
            "distribution": "TR55_TYPE_III_24HR_TABULAR",
        }
    )

    rainfall = design_storm_cumulative_rainfall(storm, 6)

    assert rainfall[0][1] == pytest.approx(0.0)
    assert rainfall[-1][0] == pytest.approx(1440.0)
    assert rainfall[-1][1] == pytest.approx(5.0)
    assert rainfall[120][1] == pytest.approx(5.0 * 0.5)


def test_explicit_combine_hydrograph_rows_create_study_point_output():
    model = HydrologyModel.from_dict(
        {
            "storms": [
                {
                    "id": "design-10yr",
                    "rainfall_depth_inches": 4.0,
                    "duration_minutes": 60,
                    "time_step_minutes": 5,
                    "duration_depths_inches": {"5-min": 0.4, "60-min": 4.0},
                }
            ],
            "basins": [
                {"id": "B-1", "name": "Basin 1", "area_acres": 1.5, "curve_number": 80, "tc_minutes": 10},
                {"id": "B-2", "name": "Basin 2", "area_acres": 2.0, "curve_number": 82, "tc_minutes": 12},
            ],
            "hydrographs": [
                {"id": "H1", "type": "SCS", "basin_id": "B-1", "description": "Basin 1"},
                {"id": "H2", "type": "SCS", "basin_id": "B-2", "description": "Basin 2"},
                {"id": "H3", "type": "Combine", "description": "Study Point A", "inflow_ids": ["H1", "H2"]},
            ],
        }
    )

    results = build_results(model, validate_model(model))
    combine = next(hydrograph for hydrograph in results["hydrographs"] if hydrograph["id"] == "H3")
    h1 = next(hydrograph for hydrograph in results["hydrographs"] if hydrograph["id"] == "H1")
    h2 = next(hydrograph for hydrograph in results["hydrographs"] if hydrograph["id"] == "H2")

    assert combine["type"] == "Combine"
    assert combine["inflow_ids"] == ["H1", "H2"]
    assert combine["peak_flow_cfs"] >= max(h1["peak_flow_cfs"], h2["peak_flow_cfs"])
    assert combine["volume_cuft"] > h1["volume_cuft"]
    assert len(results["hydrograph_peak_flows"]) == 3


def test_reservoir_hydrograph_routes_inflow_through_stage_storage_and_weir():
    model = HydrologyModel.from_dict(
        {
            "storms": [
                {
                    "id": "design-10yr",
                    "rainfall_depth_inches": 4.0,
                    "duration_minutes": 60,
                    "time_step_minutes": 5,
                    "duration_depths_inches": {"5-min": 0.4, "60-min": 4.0},
                }
            ],
            "basins": [
                {"id": "B-1", "name": "Basin 1", "area_acres": 2.0, "curve_number": 82, "tc_minutes": 12},
            ],
            "hydrographs": [
                {"id": "H1", "type": "SCS", "basin_id": "B-1", "description": "Basin 1"},
                {
                    "id": "H2",
                    "type": "Reservoir",
                    "description": "Pond 1",
                    "inflow_ids": ["H1"],
                    "parameters": {
                        "stage_storage": [
                            {"elevation": "0", "volume": "0"},
                            {"elevation": "1", "volume": "5000"},
                            {"elevation": "2", "volume": "10000"},
                            {"elevation": "3", "volume": "20000"},
                            {"elevation": "4", "volume": "35000"},
                        ],
                        "weirs": [
                            {"label": "Weir Type", "a": "Rectangular"},
                            {"label": "Crest Elev (ft)", "a": "0.5"},
                            {"label": "Crest Length (ft)", "a": "2.0"},
                            {"label": "Weir Coeff.", "a": "3.33"},
                            {"label": "Active", "a": "Yes"},
                        ],
                    },
                },
            ],
        }
    )

    results = build_results(model, validate_model(model))
    inflow = next(hydrograph for hydrograph in results["hydrographs"] if hydrograph["id"] == "H1")
    reservoir = next(hydrograph for hydrograph in results["hydrographs"] if hydrograph["id"] == "H2")

    assert reservoir["status"] == "ok"
    assert reservoir["peak_flow_cfs"] < inflow["peak_flow_cfs"]
    assert reservoir["time_to_peak_minutes"] >= inflow["time_to_peak_minutes"]
    assert reservoir["maximum_elevation_ft"] > 1.0
    assert reservoir["maximum_storage_cuft"] > 0
    assert len(results["hydrograph_peak_flows"]) == 2


def test_reservoir_v_notch_weir_uses_angle_based_discharge():
    model = HydrologyModel.from_dict(
        {
            "storms": [
                {
                    "id": "design-10yr",
                    "rainfall_depth_inches": 3.5,
                    "duration_minutes": 60,
                    "time_step_minutes": 5,
                    "duration_depths_inches": {"5-min": 0.3, "60-min": 3.5},
                }
            ],
            "basins": [
                {"id": "B-1", "name": "Basin 1", "area_acres": 1.5, "curve_number": 80, "tc_minutes": 10},
            ],
            "hydrographs": [
                {"id": "H1", "type": "SCS", "basin_id": "B-1"},
                {
                    "id": "H2",
                    "type": "Reservoir",
                    "inflow_ids": ["H1"],
                    "parameters": {
                        "stage_storage": [
                            {"elevation": "0", "volume": "0"},
                            {"elevation": "1", "volume": "5000"},
                            {"elevation": "2", "volume": "10000"},
                            {"elevation": "3", "volume": "20000"},
                        ],
                        "weirs": [
                            {"label": "Weir Type", "a": "90-deg V-notch"},
                            {"label": "Crest Elev (ft)", "a": "0.3"},
                            {"label": "Crest Length (ft)", "a": "n/a"},
                            {"label": "Weir Coeff.", "a": "2.540"},
                            {"label": "Active", "a": "Yes"},
                        ],
                    },
                },
            ],
        }
    )

    results = build_results(model, validate_model(model))
    reservoir = next(hydrograph for hydrograph in results["hydrographs"] if hydrograph["id"] == "H2")

    assert reservoir["status"] == "ok"
    assert reservoir["peak_flow_cfs"] > 0
    assert reservoir["maximum_elevation_ft"] > 0.3


def test_broad_crested_weir_blank_coefficient_uses_hydraflow_default():
    parameters = {
        "weirs": [
            {"label": "Weir Type", "a": "Broad Crested"},
            {"label": "Crest Elev (ft)", "a": "0"},
            {"label": "Crest Length (ft)", "a": "10"},
            {"label": "Weir Coeff.", "a": ""},
            {"label": "Active", "a": "Yes"},
        ],
        "culverts_orifices": [],
    }

    discharge = _outlet_discharge_cfs(1.0, parameters, [])

    assert discharge == pytest.approx(26.0)


def test_broad_crested_weir_uses_brater_king_table_when_breadth_is_available():
    parameters = {
        "weirs": [
            {"label": "Weir Type", "a": "Broad Crested"},
            {"label": "Crest Elev (ft)", "a": "0"},
            {"label": "Crest Length (ft)", "a": "10"},
            {"label": "Breadth of Crest (ft)", "a": "1"},
            {"label": "Weir Coeff.", "a": ""},
            {"label": "Active", "a": "Yes"},
        ],
        "culverts_orifices": [],
    }

    discharge = _outlet_discharge_cfs(4.0, parameters, [])

    assert discharge == pytest.approx(3.32 * 10.0 * (4.0 ** 1.5))


def test_rectangular_weir_blank_coefficient_uses_hydraflow_default():
    parameters = {
        "weirs": [
            {"label": "Weir Type", "a": "Rectangular"},
            {"label": "Crest Elev (ft)", "a": "0"},
            {"label": "Crest Length (ft)", "a": "10"},
            {"label": "Weir Coeff.", "a": ""},
            {"label": "Active", "a": "Yes"},
        ],
        "culverts_orifices": [],
    }

    discharge = _outlet_discharge_cfs(1.0, parameters, [])

    assert discharge == pytest.approx(33.0)


def test_culvert_outlet_control_uses_downstream_water_surface_head():
    rows = {
        "rise (in)": {"a": "24"},
        "span (in)": {"a": "24"},
        "no. barrels": {"a": "1"},
        "invert elev. (ft)": {"a": "100"},
        "length (ft)": {"a": "100"},
        "slope (%)": {"a": "0"},
        "n-value": {"a": ".013"},
        "orifice coeff.": {"a": "1.00"},
    }

    discharge = _standard_orifice_column_discharge(102.0, 0.0, rows, "a", [])

    area = math.pi
    radius = 0.5
    k = 1.5 + ((29.0 * (0.013 ** 2.0) * 100.0) / (radius ** 1.33))
    expected_outlet_control = area * math.sqrt((2.0 * 32.174 * 2.0) / k)
    assert discharge == pytest.approx(expected_outlet_control)


def test_concrete_pipe_beveled_lip_entrance_uses_brater_king_table():
    rows = {
        "rise (in)": {"a": "24"},
        "span (in)": {"a": "24"},
        "no. barrels": {"a": "1"},
        "invert elev. (ft)": {"a": "100"},
        "length (ft)": {"a": "100"},
        "slope (%)": {"a": "0"},
        "n-value": {"a": ".013"},
        "orifice coeff.": {"a": "1.00"},
        "entrance type": {"a": "Beveled-Lip"},
    }

    discharge = _standard_orifice_column_discharge(102.0, 0.0, rows, "a", [])

    area = math.pi
    expected_outlet_control = 0.67 * area * math.sqrt(2.0 * 32.174 * 2.0)
    assert discharge == pytest.approx(expected_outlet_control)


def test_concrete_pipe_square_cornered_entrance_uses_brater_king_table():
    rows = {
        "rise (in)": {"a": "24"},
        "span (in)": {"a": "24"},
        "no. barrels": {"a": "1"},
        "invert elev. (ft)": {"a": "100"},
        "length (ft)": {"a": "100"},
        "slope (%)": {"a": "0"},
        "n-value": {"a": ".013"},
        "orifice coeff.": {"a": "1.00"},
        "entrance type": {"a": "Square Cornered"},
    }

    discharge = _standard_orifice_column_discharge(102.0, 0.0, rows, "a", [])

    area = math.pi
    expected_outlet_control = 0.62 * area * math.sqrt(2.0 * 32.174 * 2.0)
    assert discharge == pytest.approx(expected_outlet_control)


def test_rating_curve_adds_intermediate_stages_for_three_inch_bottom_orifice():
    parameters = {
        "culverts_orifices": [
            {"label": "Rise (in)", "a": "3"},
            {"label": "Span (in)", "a": "3"},
            {"label": "No. Barrels", "a": "1"},
            {"label": "Invert Elev. (ft)", "a": "0"},
            {"label": "Length (ft)", "a": "0"},
            {"label": "Slope (%)", "a": "0"},
            {"label": "N-Value", "a": ".013"},
            {"label": "Orifice Coeff.", "a": ".60"},
            {"label": "Multi-Stage", "a": "n/a"},
            {"label": "Active", "a": "Yes"},
        ],
        "weirs": [],
    }

    rating = _build_outlet_rating_curve(
        [StageStoragePoint(0.0, 0.0), StageStoragePoint(1.0, 10000.0)],
        parameters,
        [],
    )

    assert len(rating) == 11
    assert rating[1].elevation_ft == pytest.approx(0.1)
    assert rating[1].discharge_cfs < rating[-1].discharge_cfs * 0.2


def test_storage_indication_routing_matches_hydraflow_reference_table():
    dt_seconds = 240.0
    times = list(range(0, 73, 4))
    inflows = [0, 24, 95, 206, 345, 500, 655, 794, 905, 976, 1000, 976, 905, 848, 736, 638, 554, 480, 417]
    reference_indications = [0, 24, 123, 334, 725, 1284, 2039, 2958, 3991, 5120, 6286, 7402, 8383, 9206, 9836, 10240, 10452, 10502, 10411]
    reference_outflows = [0, 10, 45, 80, 143, 200, 265, 333, 376, 404, 430, 450, 465, 477, 485, 490, 492, 494, 491]
    rating = [
        OutletRatingPoint(
            elevation_ft=float(index),
            storage_cuft=((indication - outflow) * dt_seconds / 2.0),
            discharge_cfs=float(outflow),
        )
        for index, (indication, outflow) in enumerate(zip(reference_indications, reference_outflows))
    ]

    routed, _, _, statuses = _route_storage_indication(list(zip(times, inflows)), rating)

    assert statuses == []
    assert [round(flow) for _, flow in routed] == [0, *reference_outflows[1:]]


def test_multi_stage_orifice_routes_through_culvert_a_capacity():
    parameters = {
        "tailwater_elevation": "0",
        "culverts_orifices": [
            {"label": "Rise (in)", "a": "12", "b": "24"},
            {"label": "Span (in)", "a": "12", "b": "24"},
            {"label": "No. Barrels", "a": "1", "b": "1"},
            {"label": "Invert Elev. (ft)", "a": "0", "b": "0.5"},
            {"label": "Length (ft)", "a": "100", "b": "0"},
            {"label": "Slope (%)", "a": "0", "b": "0"},
            {"label": "N-Value", "a": ".013", "b": ".013"},
            {"label": "Orifice Coeff.", "a": ".60", "b": ".60"},
            {"label": "Multi-Stage", "a": "n/a", "b": "Yes"},
            {"label": "Active", "a": "Yes", "b": "Yes"},
        ],
        "weirs": [],
    }
    rows = _table_by_label(parameters["culverts_orifices"])

    multi_stage_q = _outlet_discharge_cfs(3.0, parameters, [])
    direct_b_q = _standard_orifice_column_discharge(3.0, 0.0, rows, "b", [])
    culvert_a_full_capacity = _standard_orifice_column_discharge(3.0, 0.0, rows, "a", [])

    assert multi_stage_q < direct_b_q
    assert multi_stage_q <= culvert_a_full_capacity
    assert multi_stage_q > 0


def test_reservoir_with_invalid_stage_storage_returns_status_without_crashing():
    model = HydrologyModel.from_dict(
        {
            "storms": [
                {"id": "design-10yr", "rainfall_depth_inches": 3.0, "duration_minutes": 60, "time_step_minutes": 5}
            ],
            "basins": [
                {"id": "B-1", "name": "Basin 1", "area_acres": 1.0, "curve_number": 80, "tc_minutes": 10},
            ],
            "hydrographs": [
                {"id": "H1", "type": "SCS", "basin_id": "B-1"},
                {
                    "id": "H2",
                    "type": "Reservoir",
                    "inflow_ids": ["H1"],
                    "parameters": {
                        "stage_storage": [{"elevation": "0", "volume": "0"}],
                        "weirs": [{"label": "Active", "a": "Yes"}],
                    },
                },
            ],
        }
    )

    results = build_results(model, validate_model(model))
    reservoir = next(hydrograph for hydrograph in results["hydrographs"] if hydrograph["id"] == "H2")

    assert reservoir["status"] == "invalid_stage_storage"
    assert reservoir["peak_flow_cfs"] == 0


def test_reservoir_validation_requires_stage_storage_rows():
    model = HydrologyModel.from_dict(
        {
            "basins": [
                {"id": "B-1", "name": "Basin 1", "area_acres": 1.0, "curve_number": 80, "tc_minutes": 10},
            ],
            "hydrographs": [
                {"id": "H1", "type": "SCS", "basin_id": "B-1"},
                {"id": "H2", "type": "Reservoir", "inflow_ids": ["H1"], "parameters": {"stage_storage": []}},
            ],
        }
    )

    report = validate_model(model)

    assert "Hydrograph H2 type Reservoir requires at least two stage-storage rows." in report.errors


def test_hydrograph_validation_enforces_inflow_cardinality_by_type():
    model = HydrologyModel.from_dict(
        {
            "basins": [
                {"id": "B-1", "name": "Basin 1", "area_acres": 1.5, "curve_number": 80, "tc_minutes": 10},
                {"id": "B-2", "name": "Basin 2", "area_acres": 2.0, "curve_number": 82, "tc_minutes": 12},
            ],
            "hydrographs": [
                {"id": "H1", "type": "SCS", "basin_id": "B-1", "inflow_ids": ["H2"]},
                {"id": "H2", "type": "SCS", "basin_id": "B-2"},
                {"id": "H3", "type": "Combine", "inflow_ids": ["H1"]},
                {"id": "H4", "type": "Reach", "inflow_ids": ["H1", "H2"]},
                {"id": "H5", "type": "Reservoir", "inflow_ids": []},
            ],
        }
    )

    report = validate_model(model)

    assert "Hydrograph H1 type SCS does not accept inflow hydrographs." in report.errors
    assert "Hydrograph H3 type Combine requires at least two inflow hydrographs." in report.errors
    assert "Hydrograph H4 type Reach requires exactly one inflow hydrograph." in report.errors
    assert "Hydrograph H5 type Reservoir requires exactly one inflow hydrograph." in report.errors