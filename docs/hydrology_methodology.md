# C3DTools Hydrology Methodology

This document describes the implemented calculation methodology for hydrograph generation, stage-storage-discharge table construction, and reservoir routing in C3DTools. It is intended to be detailed enough for independent checking against hand calculations, HydraFlow-style output tables, or Python regression tests.

The primary implementation is in `python-engine/hydro_app/hydrograph.py`. Civil 3D extracts drawing data, stores user-entered hydrograph and pond rows, and writes the JSON model. The Python engine performs the hydrology and hydraulics calculations.

## 1. Calculation Scope

The engine currently evaluates these hydrograph row types:

| Row type | Purpose | Implemented behavior |
| --- | --- | --- |
| `SCS` | Generate a runoff hydrograph for one basin | Uses local NRCS/SCS runoff and triangular unit-hydrograph functions with area, CN, Tc, and a cumulative rainfall distribution |
| `Combine` | Add upstream hydrographs | Sums flows from referenced hydrograph ids at matching time ordinates |
| `Reservoir` | Route one inflow hydrograph through a pond | Builds an outlet rating table from stage-storage and outlets, then routes by Storage Indication |

Unsupported row types currently return a zero hydrograph with an `unsupported_type` status. `Reach` and `Rational` are reserved concepts but are not yet implemented in the engine.

## 2. Units and Conventions

| Quantity | Unit |
| --- | --- |
| Basin area | acres |
| Rainfall depth | inches |
| Time | minutes in hydrograph points; seconds inside routing equations |
| Tc | minutes |
| Flow | cubic feet per second, cfs |
| Storage | cubic feet |
| Stage, elevation, length | feet |
| Outlet rise/span | inches in input, converted to feet internally |
| Acceleration of gravity | `g = 32.174 ft/s^2` |

Hydrograph point arrays use:

```json
{ "time_minutes": 0.0, "flow_cfs": 0.0 }
```

Reservoir rating rows use:

```json
{ "stage_ft": 0.0, "storage_cuft": 0.0, "discharge_cfs": 0.0 }
```

## 3. Model Inputs

The Python engine reads `basin_model.json`. Relevant inputs are:

### 3.1 Storm

Each storm contains:

| Field | Meaning |
| --- | --- |
| `rainfall_depth_inches` | Total design rainfall depth |
| `duration_minutes` | Design storm duration |
| `time_step_minutes` | Hydrograph and rainfall calculation interval |
| `distribution` | Optional TR-55 tabular distribution id |
| `duration_depths_inches` | NOAA Atlas 14 duration-depth values, stored in the raw storm payload |

### 3.2 Basin

Each basin contains:

| Field | Meaning |
| --- | --- |
| `area_acres` | Drainage area |
| `curve_number` | Direct CN, used when present |
| `land_uses` | Optional land-use areas and CNs, used to compute composite CN |
| `tc_minutes` | Time of concentration, currently manually entered |

### 3.3 Hydrograph Definitions

Each hydrograph row contains:

| Field | Meaning |
| --- | --- |
| `id` | Hydrograph row id, such as `H1` |
| `type` | `SCS`, `Combine`, or `Reservoir` |
| `basin_id` | Basin referenced by SCS rows |
| `inflow_ids` | Upstream hydrograph ids referenced by Combine or Reservoir rows |
| `parameters` | Pond and outlet parameters for Reservoir rows |

If the JSON model does not include explicit hydrograph rows, the engine automatically creates default SCS rows for basins and Combine rows where the basin routing graph has upstream accumulation.

## 4. Evaluation Order

Hydrograph rows are evaluated in their listed order. A row can only consume inflow rows that have already been evaluated.

The result for each row includes:

| Field | Method |
| --- | --- |
| `peak_flow_cfs` | Maximum flow ordinate |
| `time_to_peak_minutes` | Time at maximum flow ordinate |
| `time_interval_minutes` | Difference between the first two time ordinates |
| `volume_cuft` | Trapezoidal integration of flow over time |
| `status` | `ok` or semicolon-separated warning/status tokens |

Hydrograph volume is computed as:

```text
Volume = sum(((Q1 + Q2) / 2) * (t2 - t1) * 60)
```

where flow is in cfs and time is in minutes.

## 5. Rainfall Distribution

The SCS hydrograph routine first builds a cumulative rainfall array. The array contains time in minutes and cumulative rainfall depth in inches.

### 5.1 TR-55 Tabular Distributions

If `storm.distribution` matches a supported TR-55 tabular distribution, the engine loads the corresponding `.tbl` file from:

```text
python-engine/data/RainfallDistributions/
```

Supported aliases include:

| Alias examples | Internal distribution |
| --- | --- |
| `SCS_TYPE_I`, `NRCS_TYPE_I` | `TR55_TYPE_I_24HR_TABULAR` |
| `SCS_TYPE_IA`, `NRCS_TYPE_IA` | `TR55_TYPE_IA_24HR_TABULAR` |
| `SCS_TYPE_II`, `NRCS_TYPE_II` | `TR55_TYPE_II_24HR_TABULAR` |
| `SCS_TYPE_III`, `NRCS_TYPE_III` | `TR55_TYPE_III_24HR_TABULAR` |

The source values are cumulative rainfall fractions. The engine forces the first fraction to `0.0` and the last fraction to `1.0`, then builds a monotone cubic interpolation curve. The curve is sampled at the requested time step. The sampled fractions are multiplied by total rainfall depth.

Monotone cubic interpolation is used so the mass curve remains smooth while preserving nondecreasing cumulative rainfall behavior.

### 5.2 NOAA Atlas 14 Alternating-Block Distribution

If no TR-55 tabular distribution is selected, the engine uses `duration_depths_inches` from the storm. These values are treated as cumulative rainfall depths by duration.

Procedure:

1. Build known cumulative depth points from `(0, 0)`, the Atlas duration-depth pairs, and `(duration_minutes, total_depth)`.
2. Clamp known depths between `0` and the total depth.
3. Interpolate cumulative depths to the model time step.
4. Convert cumulative depths to incremental depths.
5. Sort incremental depths from largest to smallest.
6. Place the largest increment at the center of the storm.
7. Alternately place the remaining increments after and before the center block.
8. Rebuild cumulative rainfall from the arranged increments.
9. Force the final cumulative depth to equal the total rainfall depth.

This creates a centered alternating-block design storm from the supplied NOAA duration-depth table.

## 6. SCS Hydrograph Generation

An SCS row is evaluated from one basin and one storm.

### 6.1 Required Basin Inputs

The row returns a zero hydrograph if any of these are missing or invalid:

| Input | Requirement |
| --- | --- |
| Area | `area_acres > 0` |
| Curve number | Composite or direct CN must be available |
| Tc | `tc_minutes` must be available |
| Rainfall | `rainfall_depth_inches` must be available |

### 6.2 Composite Curve Number

The engine calls `composite_curve_number(basin)`. If basin land-use rows are present, their CN-weighted areas are used. If not, the basin-level `curve_number` is used.

### 6.3 Local NRCS/SCS Basin Hydrograph

The hydrograph is generated directly by C3DTools. No external hydrology package is used. The local routine uses:

| Argument | Source |
| --- | --- |
| `area_acres` | Basin area in acres |
| `curve_number` | Composite curve number |
| `tc_minutes` | Time of concentration in minutes |
| `cumulative_rainfall` | Design storm cumulative rainfall array |
| `interval_minutes` | Hydrograph time step |

The local triangular unit hydrograph shape is:

```text
time ratio, flow ratio
0.00, 0.00
1.00, 1.00
2.67, 0.00
```

The SCS unit hydrograph follows the HydraFlow triangular D-hour unit hydrograph form. The unit duration `D` is the model time interval. For this implementation, `Tc`, `D`, `Tp`, and `Tb` are stored in minutes, but the peak flow equation converts area to square miles and `Tp` to hours.

Peak time is:

```text
Tp = (Tc + D) / 1.7
```

The unit-hydrograph peak flow for 1 inch of excess precipitation is:

```text
Qp = 484 * A * Q / Tp
```

where:

| Symbol | Meaning | Unit used in equation |
| --- | --- | --- |
| `Qp` | Peak flow | cfs |
| `484` | Shape factor | dimensionless |
| `A` | Drainage area | square miles |
| `Q` | Excess precipitation for the unit hydrograph | inches, fixed at `1.0` |
| `Tp` | Time to peak | hours |

The time base is:

```text
Tb = 2.67 * Tp
```

The triangular distribution is scaled to points `(0, 0)`, `(Tp, Qp)`, and `(Tb, 0)`, then linearly resampled to the model time step.

The cumulative rainfall array is converted to cumulative runoff depth by the curve-number equation in Section 6.4. Incremental runoff depths are then convolved with the unit hydrograph:

```text
Q[t] = sum(incremental_runoff_depth[k] * unit_hydrograph_flow[t - k])
```

The returned hydrograph is rounded to six decimal places and negative flows are clamped to zero.

### 6.4 NRCS Curve-Number Excess Precipitation

The cumulative rainfall array is converted to cumulative excess precipitation with the standard NRCS curve-number equation:

```text
S = 1000 / CN - 10

if P <= 0.2S:
    Q = 0
else:
    Q = (P - 0.2S)^2 / (P + 0.8S)
```

where:

| Symbol | Meaning | Unit |
| --- | --- | --- |
| `Q` | Excess precipitation/runoff depth | inches |
| `P` | Accumulated precipitation depth | inches |
| `S` | Potential maximum retention, `(1000 / CN) - 10` | inches |
| `CN` | Curve number | dimensionless |

The engine applies this equation to accumulated precipitation at each rainfall ordinate, then differences successive accumulated `Q` values to get the excess precipitation increments used in the final hydrograph convolution.

## 7. Combine Hydrograph Rows

A Combine row adds one or more previously computed hydrographs.

Procedure:

1. Start with a zero hydrograph at time `0.0`.
2. For each `inflow_id`, retrieve the previously computed row.
3. Build the sorted union of time ordinates from the current combined series and the next inflow series.
4. Add flow values at matching time ordinates.
5. Missing flow ordinates are treated as `0.0`.

Important verification note: the current Combine implementation does not interpolate between differing time ordinates. It assumes inflow hydrographs share the same time step and time grid. This is normally true for rows produced in the same model run.

If an `inflow_id` is missing, the row still computes from available inflows but reports `missing_inflows:<ids>`.

## 8. Reservoir Row Overview

A Reservoir row requires exactly one inflow hydrograph. If the row has zero or multiple inflow ids, it returns a zero hydrograph with `invalid_inflow_count`.

Reservoir evaluation has three major phases:

1. Parse and validate the user stage-storage table.
2. Build a stage-storage-discharge rating table from storage and outlet inputs.
3. Route the inflow hydrograph through the rating table using Storage Indication.

The Reservoir result includes the routed outflow hydrograph plus:

| Field | Meaning |
| --- | --- |
| `maximum_elevation_ft` | Maximum routed pond elevation from Storage Indication lookup |
| `maximum_storage_cuft` | Maximum routed pond storage from Storage Indication lookup |
| `outlet_rating` | Full generated stage-storage-discharge table |

## 9. Stage-Storage Table

Reservoir rows read `parameters.stage_storage`, where each row contains:

| Accepted field | Meaning |
| --- | --- |
| `elevation` | Pond stage/elevation in ft |
| `volume`, `storage`, or `storage_cuft` | Pond storage in cubic feet |

Parsing rules:

1. Rows with missing elevation or storage are ignored.
2. Storage is clamped to a minimum of zero.
3. Points are sorted by elevation.
4. At least two valid points are required.
5. Elevations must be strictly increasing.
6. Storage must be nondecreasing.

If validation fails, the row returns a zero hydrograph with `invalid_stage_storage`.

### 9.1 Stage-Storage Densification

Before computing discharge, the stage-storage table is densified. For each pair of adjacent user-entered stage-storage points, the engine inserts intermediate points so the maximum stage increment is approximately `0.1 ft`.

For each interval:

```text
step_count = ceil(stage_span / 0.1)
fraction = step / step_count
stage = left_stage + stage_span * fraction
storage = left_storage + storage_span * fraction
```

Storage is linearly interpolated between the user-entered storage rows. Discharge is not interpolated at this stage; discharge is recomputed from outlet equations at every densified stage.

This densification is important for small low-flow outlets, such as a 3-inch bottom orifice, because discharge changes rapidly in the first few tenths of a foot.

## 10. Stage-Discharge Table Construction

The engine computes discharge at each densified stage. Total discharge is the sum of active outlet components, subject to multi-stage routing rules.

The outlet parameter tables are stored as rows with labels and columns:

| Table | Columns |
| --- | --- |
| `culverts_orifices` | `a`, `b`, `c`, `riser` |
| `weirs` | `a`, `b`, `c`, `d` |

Rows are looked up by label, case-insensitively. Cells with `n/a`, `na`, `----`, `---`, or `Choose...` are treated as blank.

An outlet component is skipped when its `Active` row is false. Accepted true values are `Yes`, `Y`, `True`, `1`, and `Active`.

## 11. Culverts and Orifices

Each standard culvert/orifice column uses these input rows:

| Row label | Meaning |
| --- | --- |
| `Rise (in)` | Pipe diameter or box height |
| `Span (in)` | Pipe diameter or box width |
| `No. Barrels` | Number of barrels/openings; default `1` |
| `Invert Elev. (ft)` | Upstream invert elevation |
| `Length (ft)` | Barrel length; `0` for pure orifice behavior |
| `Slope (%)` | Barrel slope, percent |
| `N-Value` | Manning roughness; default `0.013` |
| `Orifice Coeff.` | Inlet/orifice coefficient; default `0.60` |
| `Entrance Type` | Optional concrete pipe table selection |

If invert, rise, span, or barrel count are invalid, the component contributes `0.0 cfs`.

### 11.1 Opening Geometry

The current water depth above the invert is:

```text
depth = stage - invert
flow_depth = min(depth, rise)
```

If `Rise == Span`, the opening is treated as circular. For partial circular flow:

```text
r = rise / 2
theta = 2 * acos((r - flow_depth) / r)
A = 0.5 * r^2 * (theta - sin(theta))
P_wet = r * theta
```

For full circular flow:

```text
A = pi * r^2
P_wet = pi * rise
```

If `Rise != Span`, the opening is treated as rectangular/box. For partial rectangular flow:

```text
A = span * flow_depth
P_wet = span + 2 * flow_depth
```

For full rectangular flow:

```text
A = rise * span
P_wet = 2 * (rise + span)
```

The hydraulic radius for outlet control is:

```text
R = A / P_wet
```

### 11.2 Inlet-Control Discharge

HydraFlow-style inlet control uses one-half of the flow depth during partial flow. The implementation uses:

```text
centroid_elevation = invert + flow_depth / 2
h_inlet = stage - centroid_elevation
Q_inlet = Co * A * sqrt(2 * g * h_inlet) * barrels
```

For full flow, `flow_depth == rise`, so the same expression places the centroid at mid-height.

If a nonzero tailwater elevation is entered and the tailwater head is smaller than the inlet head, the smaller head is used:

```text
h_tailwater = stage - tailwater_elevation
h_inlet = min(h_inlet, h_tailwater)
```

### 11.3 Outlet-Control Discharge, k-Value Form

If `Length (ft) > 0`, outlet control is also evaluated. The downstream invert is:

```text
downstream_invert = invert - length * slope_percent / 100
```

If a tailwater elevation is entered, the downstream water surface is the tailwater elevation. Otherwise it is the downstream invert.

```text
h_outlet = stage - downstream_water_surface
```

When `Entrance Type` is blank, outlet-control discharge uses:

```text
k = 1.5 + (29 * n^2 * length) / R^1.33
Q_outlet = A * sqrt((2 * g * h_outlet) / k) * barrels
```

### 11.4 Outlet-Control Discharge, Brater and King Concrete Pipe Tables

When `Entrance Type` is supplied and the pipe is circular (`Rise == Span`), the engine can use a Brater and King concrete pipe coefficient table.

Accepted entrance type text:

| Text contains | Table used |
| --- | --- |
| `bevel` | Concrete Pipe, Beveled-Lip Entrance |
| `square` | Concrete Pipe, Square Cornered Entrance |

The equation is:

```text
Q_outlet = C * A * sqrt(2 * g * h_outlet) * barrels
```

The coefficient `C` is bilinearly interpolated by pipe length and diameter from the implemented tables. Table bounds clamp to the nearest edge value.

If `Entrance Type` is blank or the section is not circular, the engine uses the k-value form instead.

### 11.5 Governing Culvert/Orifice Discharge

For culverts with length:

```text
Q = min(Q_inlet, Q_outlet)
```

For pure orifices with no length:

```text
Q = Q_inlet
```

All negative or invalid discharges are clamped to zero.

## 12. Perforated Riser

The `riser` culvert/orifice column is treated as a perforated riser when active.

Inputs:

| Row label | Meaning |
| --- | --- |
| `Rise (in)` | Hole height or diameter |
| `Span (in)` | Hole width or diameter |
| `No. Barrels` or `No. Holes` | Number of holes |
| `Invert Elev. (ft)` | Bottom elevation of perforated zone |
| `Length (ft)` or `Height (ft)` | Height of perforated zone |

The implemented equation is:

```text
Q = 0.61 * ((2 * A_total) / (3 * Hs)) * sqrt(2 * g) * H^1.5
```

where:

```text
A_total = one_hole_area * number_of_holes
Hs = perforated height
H = stage - invert
```

If tailwater is nonzero, the controlling stage for the riser head is the smaller of pond stage and tailwater elevation.

## 13. Weirs

Weir columns are `a`, `b`, `c`, and `d`. Only Weir A may be a `Riser` type; riser weirs in other columns are ignored and report `ignored_weir_riser_not_a`.

Input rows:

| Row label | Meaning |
| --- | --- |
| `Weir Type` | `Rectangular`, `Cipoletti`, `Broad Crested`, `Riser`, or V-notch type |
| `Crest Elev (ft)` | Crest elevation, or V-notch apex elevation |
| `Crest Length (ft)` | Crest length for non-V-notch weirs |
| `Weir Coeff.` | Optional explicit coefficient |
| `Breadth of Crest (ft)` | Optional broad-crested table breadth |
| `Multi-Stage` | Whether this weir routes through culvert A in multi-stage logic |
| `Active` | Whether the weir is active |

### 13.1 Rectangular, Cipoletti, Riser, and Broad-Crested Weirs

For non-V-notch weirs:

```text
H = stage - crest
Q = Cw * L * H^1.5
```

Default coefficients when `Weir Coeff.` is blank:

| Weir type | Default Cw |
| --- | --- |
| Rectangular | 3.30 |
| Cipoletti | 3.30 |
| Riser | 3.30 |
| Broad Crested | 2.60 |

If a broad-crested weir has `Breadth of Crest (ft)`, `Crest Breadth (ft)`, or `Breadth (ft)`, the engine uses the Brater and King broad-crested coefficient table by head and breadth. The coefficient is bilinearly interpolated. Table bounds clamp to the nearest edge value.

### 13.2 V-Notch Weirs

For V-notch weirs, the angle is parsed from the weir type text, such as `90-deg V-notch`.

```text
Q = 2.54 * tan(angle / 2) * H^2.5
```

The crest elevation is the V-notch apex elevation.

### 13.3 Weir Submergence

For rectangular, Cipoletti, riser, and V-notch weirs, if the downstream head is above the crest but below the upstream head, the engine applies:

```text
ratio = H_downstream / H_upstream
Q_submerged = Q_free * (1 - ratio^1.5)^0.385
```

If downstream head is greater than or equal to upstream head, discharge is set to zero and status `weir_submerged` is reported.

## 14. Multi-Stage Outlet Logic

If no outlet row has `Multi-Stage = Yes`, total outlet discharge is the direct sum of all active weirs, culverts/orifices, and the perforated riser.

If any weir or culvert/orifice row has `Multi-Stage = Yes`, the engine uses HydraFlow-style culvert A backpressure logic:

1. Non-multi-stage weirs discharge directly to the tailwater.
2. Non-multi-stage culverts/orifices B and C and the riser discharge directly to the tailwater.
3. Multi-stage weirs and multi-stage culverts/orifices B, C, and riser discharge into an internal structure head.
4. Culvert/orifice A conveys the internal structure head to the tailwater.
5. The engine solves for the internal head where upstream multi-stage capacity equals culvert A capacity.

The internal head search bounds are:

```text
lower_head = max(tailwater_elevation, culvert_A_invert)
upper_head = max(pond_stage, lower_head)
```

At a trial internal head:

```text
upstream_capacity = multi_stage_weirs(stage, internal_head)
                  + multi_stage_culverts_B_C_riser(stage, internal_head)

culvert_A_capacity = culvert_A(internal_head, tailwater)

balance = upstream_capacity - culvert_A_capacity
```

If the balance changes sign between the lower and upper bounds, the internal head is found by bisection. The loop runs up to 60 iterations or until the balance magnitude is below `1e-6`.

The multi-stage discharge is:

```text
Q_multi_stage = min(upstream_capacity, culvert_A_capacity)
```

If culvert A controls even at the upper bound, the row reports `multi_stage_culvert_a_controls`.

If multi-stage is requested but culvert A is inactive, the multi-stage contribution is zero and status `ignored_multistage_missing_culvert_a` is reported.

## 15. Stage-Storage-Discharge Rating Table

For each densified stage-storage point, the engine computes:

```text
discharge = max(total_outlet_discharge(stage), 0)
```

The resulting rating row is:

```text
stage_ft, storage_cuft, discharge_cfs
```

This table is included in Reservoir output as `outlet_rating`. It is the primary table to compare against HydraFlow's stage-discharge or S-S-D table.

If no rating row has positive discharge, the Reservoir row returns a zero hydrograph with `no_active_outlets`.

## 16. Reservoir Routing by Storage Indication

The engine routes Reservoir rows with the Storage Indication method.

### 16.1 Rating Curve Transform

For each rating point and each routing time step, build:

```text
SI = (2 * S / dt) + O
```

where:

| Symbol | Meaning |
| --- | --- |
| `SI` | Storage indication value |
| `S` | Storage, cuft |
| `O` | Outflow, cfs |
| `dt` | Routing time interval, seconds |

The transformed curve is sorted by `SI`.

### 16.2 Routing Recurrence

The initial condition is the first rating point:

```text
S0 = rating[0].storage_cuft
O0 = rating[0].discharge_cfs
```

For each inflow interval from `i` to `j`:

```text
dt = (time_j - time_i) * 60
carry = (2 * S_i / dt) - O_i
SI_j = I_i + I_j + carry
```

The engine then looks up `SI_j` on the transformed rating curve. Linear interpolation returns:

```text
O_j, S_j, elevation_j
```

Those values become the current state for the next interval.

### 16.3 Interpolation and Bounds

For a target storage indication between two table rows, the engine linearly interpolates outflow, storage, and elevation by the fraction along the storage-indication axis.

If the target is below the first transformed row, the first row is used.

If the target is above the last transformed row, the last row is used. Status `storage_exceeded_table` is reported when the target exceeds the last row by more than:

```text
max(1e-6, abs(last_SI) * 0.001)
```

Invalid nonpositive routing time intervals are skipped and status `invalid_time_interval` is reported.

## 17. Output Checks

The following outputs are useful for checking the methodology:

| Output | Check |
| --- | --- |
| SCS row points | Compare peak and volume against the local NRCS/SCS runoff and unit-hydrograph equations |
| Combine row points | Verify ordinate-by-ordinate flow summation |
| Reservoir `outlet_rating` | Compare stage, storage, and discharge against HydraFlow S-S-D table |
| Reservoir points | Compare routed outflow hydrograph against Storage Indication table |
| `maximum_elevation_ft` | Maximum interpolated elevation reached during routing |
| `maximum_storage_cuft` | Maximum interpolated storage reached during routing |
| `status` | Check for method warnings or invalid inputs |

The most useful Reservoir audit sequence is:

1. Compare stage-storage input rows.
2. Compare the generated `outlet_rating` row by row.
3. Compare the Storage Indication transform `(2S/dt + O)`.
4. Compare each routing time step: `I_i`, `I_j`, `2S_i/dt - O_i`, `2S_j/dt + O_j`, `O_j`, `S_j`, and elevation.

## 18. Current Limitations and Known Differences

These items are intentional current implementation limits and should be considered during review:

| Area | Current behavior |
| --- | --- |
| Manual Tc | Tc is currently entered manually; detailed Tc segment computation is not the focus of this implementation |
| Combine interpolation | Combine rows sum matching ordinates and treat missing ordinates as zero; they do not interpolate between different time grids |
| Stage-storage geometry | The engine consumes user-entered stage-storage rows; it does not compute storage from pond contours or shape primitives |
| Rating resolution | Stage-storage rows are densified to about 0.1 ft; very small outlets may still need close comparison to HydraFlow's internal stage increments |
| Exfiltration | Exfiltration is detected but not applied; status `exfiltration_not_applied` is reported |
| User-defined known flows | Reserved but not currently included in total outlet discharge |
| Diversions | Outflow diversion methods are documented concepts but not implemented in routing output |
| Reach routing | Not implemented |
| Rational hydrographs | Not implemented |

## 19. Implementation Cross-Reference

| Methodology section | Implementation function |
| --- | --- |
| Rainfall distribution | `design_storm_cumulative_rainfall` |
| TR-55 table loading | `_load_24hr_tabular_distribution_curve` |
| NOAA alternating block | `_alternating_block_incremental_depths` |
| SCS hydrograph | `generate_basin_hydrograph`, `_evaluate_scs_row` |
| Combine row | `_evaluate_combine_row`, `add_hydrographs` |
| Reservoir row | `_evaluate_reservoir_row` |
| Stage-storage parsing | `_parse_stage_storage` |
| Stage-storage densification | `_densify_stage_storage_points` |
| Outlet rating | `_build_outlet_rating_curve`, `_outlet_discharge_cfs` |
| Weirs | `_weir_discharge_cfs`, `_weir_coefficient` |
| Culverts/orifices | `_standard_orifice_column_discharge` |
| Perforated riser | `_perforated_riser_discharge` |
| Multi-stage balancing | `_multi_stage_culvert_a_discharge` |
| Storage Indication routing | `_route_storage_indication`, `_lookup_storage_indication` |
