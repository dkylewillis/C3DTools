# HydraFlow Hydrographs Extension – Outlet Structure Reference

This document describes the hydraulic equations, structure types, and configuration rules used by HydraFlow Hydrographs Extension for detention pond outlet design and stage-discharge calculations. Use this as a reference when building or verifying pond outlet structures.

---

## Detention Pond Overview

Before routing, a **stage-storage-discharge (S-S-D) relationship** must be defined. This is a table pairing stage (ft) with storage (cuft) and discharge (cfs), similar to a pump performance curve.

**Example S-S-D Table:**

| Stage (ft) | Storage (cuft) | Discharge (cfs) |
|-----------|---------------|----------------|
| 0.00      | 0             | 0.00           |
| 1.00      | 5,400         | 5.75           |
| 2.00      | 11,780        | 11.89          |
| 3.00      | 17,000        | 21.43          |

Discharge is computed as a function of stage (water surface elevation). Both partial and full flow conditions are evaluated, as well as inlet and outlet control.

---

## Available Outlet Structure Types

HydraFlow supports the following outlet components per pond:

- Up to **4 culvert/orifice structures** (labeled A, B, C, and Prf Riser)
- Up to **4 weir structures** (labeled Weir A, B, C, D)
- **1 exfiltration component**
- **User-defined known flow rates (Qs)**
- **1 tailwater elevation**

Each structure can operate independently or be configured as part of a **multi-stage structure**.

---

## Culverts / Orifices

### Equation

```
Q = Co * A * sqrt(2gh / k) * Nb
```

### Parameters

| Parameter | Inlet Control | Outlet Control |
|-----------|--------------|---------------|
| Q | Discharge (cfs) | Discharge (cfs) |
| A | Culvert area (sqft) | Culvert area (sqft) |
| h | Distance from water surface to centroid of barrel (1/2 flow depth during partial flow) (ft) | Distance between upstream and downstream water surface (ft) |
| Nb | Number of barrels | Number of barrels |
| Co | Orifice coefficient (user-specified) | 1 |
| k | 1 | 1.5 + [(29n²L) / R^1.33] |

**Outlet control k-value variables:**
- n = Manning's n-value
- L = Culvert length (ft)
- R = Area / wetted perimeter (ft)

### Inlet vs. Outlet Control Logic

HydraFlow evaluates both inlet control and outlet control at each stage. **The smaller discharge value governs** and is used in the S-S-D table. Results are labeled `ic` (inlet control) or `oc` (outlet control) in tabular output.

- **Inlet control:** Barrel shape, cross-sectional area, and inlet edge control discharge.
- **Outlet control:** Slope, length, and roughness of the barrel control discharge (flow enters faster than it exits).

> HydraFlow does **not** assume full flow when depth is partial.

### Tailwater Handling

When a non-zero tailwater (TW) elevation is entered, HydraFlow computes a tailwater head `hTW`. If `hTW < h`, then `h = hTW` is used in the discharge equation.

### Outlets Tab – Culvert/Orifice Input Fields

| Field | Description |
|-------|-------------|
| Rise (in) | Diameter of pipe or height of box section |
| Span (in) | Diameter of pipe or width of box section (required) |
| No. Holes | Total number of orifices (for perforated riser only) |
| No. Barrels | Number of barrels/pipes |
| Invert Elev. (ft) | Elevation at upstream invert |
| Height (ft) | Distance from invert of lowest hole to top of highest hole (perforated riser) |
| Length (ft) | Culvert length; enter 0 for a pure orifice |
| Slope (%) | Culvert slope; enter 0 for a pure orifice |
| N-Value | Manning's roughness coefficient |
| Entrance Type | Optional concrete pipe entrance table selection: `Beveled-Lip` or `Square Cornered` |
| Orifice Coeff. | Default = 0.60 |
| Multi-Stage | Check for orifices B, C, Prf Riser to route through culvert A |
| Active | Toggle structure on/off for scenario testing |

> If Rise = Span → circular section. If Rise ≠ Span → box/rectangular section.

### Concrete Pipe Entrance Coefficients

When `Entrance Type` is set to `Beveled-Lip` or `Square Cornered` for a circular concrete pipe, outlet control uses the Brater & King coefficient table by culvert length and diameter:

```
Qoc = C * A * sqrt(2gh) * Nb
```

If `Entrance Type` is blank, outlet control uses HydraFlow's k-value equation shown above.

---

## Perforated Riser

A perforated riser is a special orifice structure with a series of same-size holes distributed over a vertical height. HydraFlow uses the **McEnroe (1988)** formula:

### Equation

```
Q = Cp * (2A / 3Hs) * sqrt(2g) * H^1.5
```

### Parameters

| Symbol | Description |
|--------|-------------|
| Q | Discharge (cfs) |
| Cp | 0.61 (constant) |
| A | Cross-sectional area of all holes (sqft) |
| Hs | Distance from S/2 below lowest hole row to S/2 above top hole row (ft) |
| H | Head (ft) |

---

## Weirs

### Rectangular, Cipoletti, Broad Crested, and Riser Weirs

```
Q = Cw * L * H^1.5
```

**HEC-22 adjusted equation (end contractions):**
```
Q = Cw * (L - 0.2H) * H^1.5
```
> Note: This adjustment causes Q to decrease toward zero as H increases — use the standard equation unless end contractions are specifically required.

| Symbol | Description |
|--------|-------------|
| Q | Discharge over weir (cfs) |
| L | Length of weir crest (ft) |
| H | Distance between water surface and crest (ft) |
| Cw | Weir coefficient; HydraFlow defaults are 3.30 for rectangular, Cipoletti, and riser weirs, and 2.60 for broad-crested weirs |

> HydraFlow uses the same equation for rectangular (sharp-crested, with end contractions) and Cipoletti (no end contractions) weirs.

### V-Notch Weir

```
Q = 2.54 * tan(θ/2) * H^2.5
```

| Symbol | Description |
|--------|-------------|
| Q | Discharge (cfs) |
| θ | Angle of V-notch (degrees, 5–120°) |
| H | Head on apex of V-notch (ft) |

> For V-notch weirs, **crest elevation is entered at the apex (bottom of the V)**, not the top.

### Submergence Reduction

Applies to rectangular, V-notch, and Cipoletti weirs when tailwater rises above the crest. Commonly occurs in multi-stage structures when the riser hydraulic grade line (HG) exceeds the riser crest elevation due to head losses through culvert A.

```
Qs = Qr * (1 - (H2/H1)^1.5)^0.385
```

| Symbol | Description |
|--------|-------------|
| Qs | Submerged flow (cfs) |
| Qr | Unsubmerged flow from standard weir equation (cfs) |
| H1 | Upstream head above crest (ft) |
| H2 | Downstream head above crest (ft) |

> Submerged values are marked with the suffix `s` in the stage-discharge table.

### Outlets Tab – Weir Input Fields

| Field | Description |
|-------|-------------|
| Weir Type | Rectangular, Cipoletti, Broad Crested, Riser, or V-Notch (5–120°) |
| Crest Elev (ft) | Elevation of weir crest (V-notch: bottom/apex elevation) |
| Crest Length (ft) | Weir crest length; for standpipe = circumference; for V-notch = 0 |
| Weir Coeff. | Default provided by HydraFlow based on weir type; use defaults unless there's a reason to override |
| Multi-Stage | Route weir flows through culvert A |
| Active | Toggle on/off for scenario testing |

> Only **Weir A** can be configured as a Riser type.

---

## Exfiltration

Exfiltration represents seepage loss from the pond bottom/sides into the surrounding soil.

### Equation

```
Qex = (ER × SA) / (12 × 3600)
```

| Symbol | Description |
|--------|-------------|
| Qex | Exfiltration outflow (cfs) |
| ER | Exfiltration rate (in/hr) |
| SA | Surface area — wetted or contour (sqft) |

### Outlets Tab – Exfiltration Input Fields

| Field | Description |
|-------|-------------|
| Rate (in/hr) | Exfiltration rate |
| Apply to | Contour area or Wetted Surface Area |
| Extract from Outflow Hyd (y/n) | If Yes, exfiltration is removed from the routed outflow hydrograph. If No, it remains in the outflow and must be diverted manually downstream. |

---

## Multi-Stage Structures

Multi-stage structures route all flows from orifices B, C, Prf Riser, and weirs A–D **through culvert A**, which serves as the final controlling outlet.

### Concept

- **Culvert A** is always the terminal outflow structure (no Multi-Stage checkbox — it's always the exit).
- Orifices B, C, Prf Riser, and Weirs A–D can be set to Multi-Stage, directing their flow into the upstream end of culvert A.
- The structure with the **least capacity at any given stage controls the final outflow**.
- HydraFlow uses culvert A's computed head as a **back-pressure (tailwater) on all upstream multi-stage components**. As that head increases, upstream orifice and weir flows decrease. When the culvert A head equals the current stage, culvert A becomes the sole controlling structure.

### Typical Configuration

A standpipe riser (Weir A – Riser type) connected at its base to an orifice (Culvert B or C), all discharging through Culvert A:

```
[Pond] → Orifice (low stage) → Riser Weir (high stage) → Culvert A → [Outfall]
```

### Important Rules

- Multi-stage orifice invert elevations must be set **at least 0.01 ft above the pond bottom (stage 0)** to prevent culvert A from taking control prematurely.
- Only Weir A can be a Riser type.
- Multiple orifices and weirs at different elevations are allowed.

---

## Hydrograph Diversion

After routing, the outflow hydrograph can be split into two separate hydrographs using one of four methods:

| Method | Description |
|--------|-------------|
| Divide by constant Q | Splits at a threshold flow value. Flows above the threshold → Diversion 1; flows at or below → Diversion 2. |
| Divide by flow ratio | Splits by a decimal ratio (e.g., 0.25 → one hydrograph at 25%, one at 75%). |
| Divide by first flush volume | Strips an initial volume off the front of the hydrograph (useful for forebay modeling). |
| Divide by pond structure | Splits based on a specific outlet structure's computed flow component from the routed S-S-D curve. Use after routing is complete. |

Output hydrographs are labeled **Diversion1** and **Diversion2**.

**Common use cases:**
- Separating emergency spillway flows from normal outlet flows
- Isolating exfiltration as a separate hydrograph for downstream processing
- Redirecting weir overflow to a different receiving channel

---

## Workflow Summary

1. Define pond geometry → build **stage-storage table** (Storage Tab).
2. Enter outlet structures → compute **stage-discharge table** (Outlets Tab → Compute).
3. Route inflow hydrograph through pond → produces **outflow hydrograph**.
4. If needed, divert outflow hydrograph using one of the four diversion methods.
5. Review tabular and graphical results (Table Tab, Graphs Tab).

---

## Key Defaults and Constants

| Parameter | Default Value |
|-----------|--------------|
| Orifice coefficient (Co) | 0.60 |
| Weir coefficient (Cw) – rectangular/Cipoletti/riser | 3.30 |
| Weir coefficient (Cw) – broad-crested | 2.60 by default; Brater & King table lookup when breadth of crest is available |
| Perforated riser discharge coefficient (Cp) | 0.61 |
| Submergence exponent | 0.385 |
| Exfiltration unit conversion denominator | 12 × 3600 = 43,200 |
