# Detention Pond Routing — Implementation Reference

This document defines the math and procedures needed to:
1. **Build a stage-discharge (S-D) table** from outlet structures (culverts/orifices, perforated risers, weirs, and exfiltration).
2. **Route an inflow hydrograph** through a pond using the Storage Indication method, given a stage-storage-discharge (S-S-D) table and an inflow hydrograph.

All equations and procedures follow HydraFlow Hydrographs Extension conventions.

---

## Part 1 — Stage-Discharge Computation

For each stage (water surface elevation) in the S-S-D table, compute the discharge contribution from every active outlet structure, then **sum them** to get the total discharge at that stage.

```
Q_total(stage) = Q_culvert_A + Q_culvert_B + Q_culvert_C + Q_prfRiser
               + Q_weirA + Q_weirB + Q_weirC + Q_weirD
               + Q_exfiltration
               + Q_user_defined
```

Each structure has a unique invert/crest elevation. **A structure contributes 0 cfs if the stage is below its invert/crest.**

---

### 1.1 Culverts / Orifices

**Equation:**
```
Q = Co * A * sqrt(2*g*h / k) * Nb
```
where `g = 32.2 ft/s²`.

**Two control modes are evaluated at every stage; the smaller Q wins.**

| Variable | Inlet Control (ic) | Outlet Control (oc) |
|----------|-------------------|---------------------|
| Q | Discharge (cfs) | Discharge (cfs) |
| A | Cross-sectional area (sqft) | Cross-sectional area (sqft) |
| h | Distance from water surface to centroid of barrel (use ½ flow depth during partial flow) (ft) | Distance between upstream and downstream water surface (ft) |
| Nb | Number of barrels | Number of barrels |
| Co | Orifice coefficient (default 0.60) | 1 |
| k | 1 | `1.5 + (29 * n² * L) / R^1.33` |

Outlet-control `k` variables:
- `n` = Manning's roughness
- `L` = culvert length (ft)
- `R` = A / wetted perimeter (ft)

**Algorithm at each stage:**
```
Q_ic = Co * A * sqrt(2*g*h_ic / 1) * Nb
k_oc = 1.5 + (29 * n^2 * L) / R^1.33
Q_oc = 1.0 * A * sqrt(2*g*h_oc / k_oc) * Nb
Q_culvert = min(Q_ic, Q_oc)   # tag result with 'ic' or 'oc'
```

**Tailwater handling:** If a non-zero TW elevation is set, compute `h_TW = TW - centroid_or_invert`. If `h_TW < h`, use `h = h_TW` in the equation.

**Important:** Do NOT assume full flow when depth is partial. Partial-flow geometry (circular segment area, wetted perimeter, etc.) must be used for A and R until the barrel is submerged.

**Pure orifice configuration:** Set `L = 0` and `Slope = 0`. Only the inlet-control branch (k = 1) is meaningful.

**Section type:** If `Rise == Span` → circular; otherwise rectangular/box.

**Concrete pipe entrance table:** If an outlet row includes `Entrance Type = Beveled-Lip` or `Entrance Type = Square Cornered` and `Rise == Span`, outlet control uses the Brater & King concrete pipe coefficient table by pipe length and diameter:
```
Q_oc = C * A * sqrt(2*g*h_oc) * Nb
```
If `Entrance Type` is blank, the k-value equation remains the default.

---

### 1.2 Perforated Riser

A vertical pipe/box with multiple same-size holes arranged in rows over a vertical height.

**Equation (McEnroe, 1988):**
```
Q = Cp * (2*A / (3*Hs)) * sqrt(2*g) * H^1.5
```

| Variable | Description |
|----------|-------------|
| Q | Discharge (cfs) |
| Cp | 0.61 (constant) |
| A | Total cross-sectional area of all holes (sqft) |
| Hs | Distance from S/2 below lowest hole row to S/2 above top hole row (ft), where S = vertical spacing between rows |
| H | Head above the bottom of the perforated zone (ft) |
| g | 32.2 ft/s² |

**Behavior:**
- `H` is measured from S/2 below the lowest row.
- `Q = 0` when stage is below that reference point.
- When stage exceeds the top of the perforated zone (`H > Hs`), use `H = Hs` (riser fully submerged on the inside; treat as if submerged for the McEnroe form).

---

### 1.3 Weirs

#### Rectangular, Cipoletti, Broad-Crested, and Riser

**Standard equation:**
```
Q = Cw * L * H^1.5
```

| Variable | Description |
|----------|-------------|
| Q | Discharge (cfs) |
| L | Length of weir crest (ft); for a circular riser, L = circumference |
| H | Head = water surface elevation − crest elevation (ft); Q = 0 if H ≤ 0 |
| Cw | Weir coefficient; HydraFlow defaults are 3.30 for rectangular, Cipoletti, and riser weirs, and 2.60 for broad-crested weirs |

**Note:** HydraFlow uses the same equation for sharp-crested rectangular (with end contractions) and Cipoletti (no end contractions) weirs.

**HEC-22 end-contraction adjustment (alternative form):**
```
Q = Cw * (L - 0.2*H) * H^1.5
```
Use the standard form by default; this alternative drives Q toward zero as H grows large and is generally not recommended.

#### V-Notch

```
Q = 2.54 * tan(θ/2) * H^2.5
```

| Variable | Description |
|----------|-------------|
| θ | V-notch angle in degrees (valid 5°–120°); convert to radians before applying `tan()` |
| H | Head on the **apex** of the V-notch (ft); the crest elevation entered IS the apex |
| Q = 0 if H ≤ 0 | |

#### Submergence Adjustment (rectangular, V-notch, Cipoletti)

Applies when downstream water rises above the crest. Most commonly: in a multi-stage structure when the head produced by culvert A rises above a riser weir crest.

```
Qs = Qr * (1 - (H2/H1)^1.5)^0.385
```

| Variable | Description |
|----------|-------------|
| Qs | Submerged (reduced) flow (cfs) |
| Qr | Unsubmerged flow from the standard weir equation |
| H1 | Upstream head above crest (ft) |
| H2 | Downstream head above crest (ft) |
| Condition | Apply only when H2 > 0 AND H2 < H1. If H2 ≥ H1, Qs → 0. Tag result with suffix `s` in the output table. |

---

### 1.4 Exfiltration

```
Qex = (ER * SA) / (12 * 3600)
```
which simplifies to:
```
Qex = (ER * SA) / 43200
```

| Variable | Description |
|----------|-------------|
| Qex | Exfiltration outflow (cfs) |
| ER | Exfiltration rate (in/hr) |
| SA | Surface area at the current stage (sqft) — either wetted area or contour area depending on input setting |

**The `12 × 3600` constant** converts (in/hr × sqft) → cfs:
- ÷ 12 converts inches to feet
- ÷ 3600 converts hours to seconds

**Extraction option:** If "Extract from Outflow Hyd" = Yes, subtract Qex from the routed outflow ordinates after routing. If No, Qex remains as part of the outflow.

---

### 1.5 Stage-Discharge Table Assembly

For a stage array `[stage_0, stage_1, ..., stage_n]`:

```python
for stage in stages:
    Q_total = 0

    # Culverts/Orifices A, B, C
    for c in active_culverts:
        if stage > c.invert:
            Q_ic = inlet_control_Q(stage, c, TW)
            Q_oc = outlet_control_Q(stage, c, TW)
            Q_total += min(Q_ic, Q_oc)

    # Perforated Riser
    if prfRiser.active and stage > prfRiser.invert_reference:
        Q_total += perforated_riser_Q(stage, prfRiser)

    # Weirs A, B, C, D
    for w in active_weirs:
        if stage > w.crest_elev:
            Qr = weir_Q(stage, w)
            Qs = submergence_adjust(Qr, w, TW_or_riserHG)
            Q_total += Qs

    # Exfiltration
    if exfil.active:
        Q_total += exfiltration_Q(stage, SA(stage), exfil.rate)

    # User-defined Qs
    Q_total += user_Q(stage)

    table.append((stage, storage(stage), Q_total))
```

**Multi-stage structures:** When orifices B/C/PrfRiser or weirs are configured "multi-stage," all of them route through culvert A. The structure with the **least capacity at the current stage controls the final outflow**. Culvert A's computed head is used as the back-pressure (effective tailwater) against the upstream multi-stage components. Multi-stage orifice inverts must be ≥ 0.01 ft above pond bottom to prevent culvert A from controlling prematurely.

---

## Part 2 — Detention Pond Routing (Storage Indication Method)

Once the S-S-D table is built, route an inflow hydrograph through it. The routing **conserves volume** while reshaping the flow vs. time pattern (peak attenuation and lag).

### 2.1 Continuity Equation

```
I − O = dS/dt
```

| Variable | Description |
|----------|-------------|
| I | Inflow (cfs) |
| O | Outflow (cfs) |
| dS/dt | Rate of change of storage |

### 2.2 Discretized Form

Between successive time steps `i` (current) and `j = i+1` (next), using time step `Δt`:

```
(Ii + Ij) + (2*Si/Δt − Oi) = (2*Sj/Δt + Oj)
```

This is the **storage-indication equation**, rearranged so all known quantities are on the left and all unknowns on the right.

### 2.3 Build the Storage-Indication Curve

From the S-S-D table, precompute pairs of `(2S/Δt + O, O)` for every row:

```python
indication_curve = []
for stage, storage, discharge in SSD_table:
    indication_curve.append((2*storage/dt + discharge, discharge))
```

This gives a monotonic curve of **(2S/Δt + O) vs. O** that supports straight-line interpolation in the routing loop.

### 2.4 Routing Procedure

The procedure produces one outflow ordinate per time step. The columns below match the HydraFlow help table:

| Col | Symbol | Meaning |
|-----|--------|---------|
| 1 | t | Time (min) |
| 2 | Ii | Inflow at current step |
| 3 | Ij | Inflow at next step |
| 4 | 2S/Δt − Oi | Storage indication minus outflow (carried forward) |
| 5 | 2S/Δt + Oj | New storage indication (sum of columns 2 + 3 + 4) |
| 6 | Outflow | Looked up from the indication curve |

**Column update rules:**
- **Col 4 (next step's carry-over):** `(2S/Δt − O)_i = (2S/Δt + O)_i − 2 * O_i` → which equals **col 5 − 2 × col 6** at row i
- **Col 5 (current step's indication):** `(2S/Δt + O)_j = I_i + I_j + (2S/Δt − O)_i` → which equals **col 2 + col 3 + col 4** at row j
- **Col 6 (current step's outflow):** Look up col 5 on the precomputed curve `(2S/Δt + O) → O` using straight-line interpolation.

### 2.5 Reference Example (from HydraFlow help)

This is the canonical worked example used to validate any implementation:

| Time (min) | Ii (cfs) | Ij (cfs) | 2S/Δt−Oi | 2S/Δt+Oj | Outflow (cfs) |
|-----------:|---------:|---------:|---------:|---------:|--------------:|
| 0  | 0    | 24   | 0    | —     | 0   |
| 4  | 24   | 95   | 4    | 24    | 10  |
| 8  | 95   | 206  | 33   | 123   | 45  |
| 12 | 206  | 345  | 174  | 334   | 80  |
| 16 | 345  | 500  | 439  | 725   | 143 |
| 20 | 500  | 655  | 884  | 1284  | 200 |
| 24 | 655  | 794  | 1509 | 2039  | 265 |
| 28 | 794  | 905  | 2292 | 2958  | 333 |
| 32 | 905  | 976  | 3239 | 3991  | 376 |
| 36 | 976  | 1000 | 4310 | 5120  | 404 |
| 40 | 1000 | 976  | 5426 | 6286  | 430 |
| 44 | 976  | 905  | 6502 | 7402  | 450 |
| 48 | 905  | 848  | 7453 | 8383  | 465 |
| 52 | 848  | 736  | 8252 | 9206  | 477 |
| 56 | 736  | 638  | 8866 | 9836  | 485 |
| 60 | 638  | 554  | 9260 | 10240 | 490 |
| 64 | 554  | 480  | 9468 | 10452 | 492 |
| 68 | 480  | 417  | 9514 | 10502 | 494 |
| 72 | 417  | 0    | —    | 10411 | 491 |

Note: The routing equation requires `Δt` in seconds when storage is in cubic feet and discharge is in cfs. With Δt = 4 min, use `Δt = 240 s` in the `2S/Δt` term.

### 2.6 Reference Implementation

```python
def route_storage_indication(inflow, ssd_table, dt_seconds):
    """
    inflow:    list of (time, Q_in) tuples or just Q_in ordinates
    ssd_table: list of (stage, storage_cuft, discharge_cfs)
    dt_seconds: routing time step in seconds
    returns:   list of outflow ordinates aligned with inflow
    """
    # 1. Build indication curve: (2S/dt + O, O) pairs, sorted by indication
    curve = sorted([(2*s/dt_seconds + q, q) for (_, s, q) in ssd_table])
    indications = [pt[0] for pt in curve]
    outflows    = [pt[1] for pt in curve]

    def lookup_O(indication_value):
        # straight-line interpolation
        if indication_value <= indications[0]:
            return outflows[0]
        if indication_value >= indications[-1]:
            return outflows[-1]
        for k in range(len(indications) - 1):
            if indications[k] <= indication_value <= indications[k+1]:
                x0, x1 = indications[k], indications[k+1]
                y0, y1 = outflows[k], outflows[k+1]
                return y0 + (y1 - y0) * (indication_value - x0) / (x1 - x0)

    # 2. March through time
    O = [0.0] * len(inflow)
    storage_minus_O = 0.0   # (2S/dt - O) carried from previous step

    for i in range(len(inflow) - 1):
        I_i = inflow[i]
        I_j = inflow[i+1]
        indication_j = I_i + I_j + storage_minus_O   # col 5
        O_j = lookup_O(indication_j)                 # col 6
        O[i+1] = O_j
        storage_minus_O = indication_j - 2 * O_j     # col 4 for next iter

    return O
```

### 2.7 Validation Checks

After running a route, verify:
1. **Volume conservation:** `Σ(I) * Δt ≈ Σ(O) * Δt + final_storage − initial_storage` (within ~1% tolerance).
2. **Peak attenuation:** `max(O) ≤ max(I)` (always true for a pond with finite storage).
3. **Peak lag:** the outflow peak occurs at or after the inflow peak.
4. **Mass balance at falling limb:** outflow eventually returns to 0 as pond drains.

---

## Part 3 — Key Defaults and Constants

| Parameter | Default |
|-----------|---------|
| g | 32.2 ft/s² |
| Orifice coefficient Co | 0.60 |
| Weir coefficient Cw (rectangular/Cipoletti/riser) | 3.30 |
| Weir coefficient Cw (broad-crested) | 2.60 by default; Brater & King table lookup when breadth of crest is available |
| Perforated riser coefficient Cp | 0.61 |
| Submergence exponent | 0.385 |
| Exfiltration conversion constant | 12 × 3600 = 43,200 |
| Min multi-stage orifice offset above pond bottom | 0.01 ft |

---

## Part 4 — Implementation Workflow

1. **Input the pond geometry** → produce a stage-storage table (Storage Tab data).
2. **Define each outlet structure** with its invert/crest, dimensions, coefficients, and active flag.
3. **For each stage in the stage-storage table:**
   - Compute each active structure's discharge using the equations in Part 1.
   - Apply multi-stage back-pressure logic if applicable.
   - Sum into total discharge.
4. **Output the S-S-D table** with columns `(stage, storage, total_discharge)`.
5. **Build the storage-indication curve** `(2S/Δt + O, O)` from the S-S-D table.
6. **Route the inflow hydrograph** using the procedure in Part 2.6.
7. **Validate** using the checks in Part 2.7.
