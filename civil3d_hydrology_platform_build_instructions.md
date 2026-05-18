
# Civil 3D Hydrology Modeling Platform
## AI Agent Build Instructions

# Overview

This project extends the existing C3DTools Civil 3D plugin to create a drawing-driven hydrology modeling platform.

The goal is NOT to recreate Hydraflow Hydrographs directly.

The goal is to allow a Civil 3D basin map to become the source of truth for hydrology modeling.

The workflow is:

```text
Civil 3D Basin Map
    ↓
C3DTools extracts geometry + metadata
    ↓
basin_model.json
    ↓
Python hydrology engine
    ↓
local NRCS/SCS calculations
    ↓
results.json + Excel reports
    ↓
Results imported back into Civil 3D
```

The system should prioritize:

- deterministic calculations
- reproducible models
- drawing-driven workflows
- separation of UI and calculation logic
- modular architecture
- future extensibility

---

# Existing Repository

Repository:

```text
https://github.com/dkylewillis/C3DTools
```

Existing capabilities already include:

- Civil 3D .NET plugin structure
- basin palette UI
- basin tagging
- XData workflows
- polygon repair
- NetTopologySuite geometry operations
- land use analysis
- AutoCAD geometry conversion
- hatch/polygon analysis

DO NOT recreate these systems.

Extend them.

---

# Core Design Principles

## 1. Civil 3D Is the Data Source

Civil 3D owns:

- basin geometry
- Tc paths
- land use polygons
- routing relationships
- labels
- user interaction

Civil 3D DOES NOT own hydrology calculations.

---

## 2. Python Owns Hydrology Logic

Python owns:

- validation
- curve number calculations
- Tc calculations
- local NRCS/SCS hydrograph generation
- hydrograph generation
- reports
- result generation

Python must NOT depend on Civil 3D APIs.

---

## 3. JSON Is the Contract

All communication between Civil 3D and Python occurs through JSON files.

Primary files:

```text
basin_model.json
results.json
validation_report.json
```

This is a strict architectural requirement.

---

# Repository Structure

```text
C3DTools/
│
├── src/
│   ├── C3DTools.Plugin/
│   ├── C3DTools.Basins/
│   ├── C3DTools.Geometry/
│   ├── C3DTools.Hydrology/
│   ├── C3DTools.Core/
│
├── python-engine/
│   ├── pyproject.toml
│   ├── hydro_app/
│   │   ├── schema.py
│   │   ├── validators.py
│   │   ├── geometry.py
│   │   ├── curve_number.py
│   │   ├── tc.py
│   │   ├── rainfall.py
│   │   ├── network.py
│   │   ├── results.py
│   │   ├── cli.py
│   │   │
│   │   ├── engines/
│   │   │   └── hydrology_engine.py
│   │   │
│   │   └── reports/
│   │       └── excel_report.py
│   │
│   └── tests/
│
├── schemas/
│   ├── basin_model.schema.json
│   └── results.schema.json
│
├── samples/
│   ├── basin_model.json
│   ├── results.json
│   ├── report.xlsx
│   └── example_drawings/
│
└── docs/
    ├── workflow.md
    ├── drawing-standards.md
    ├── json-schema.md
    └── development-roadmap.md
```

---

# C# Project Responsibilities

## C3DTools.Plugin

Responsible for:

- plugin startup
- ribbon commands
- command registration
- Civil 3D integration
- launching Python processes

Must remain thin.

NO hydrology calculations.

---

## C3DTools.Basins

Responsible for:

- basin palette UI
- basin tagging
- basin management
- basin metadata editing
- land use assignment UI
- Tc path UI
- basin summaries

This is the primary user interaction layer.

---

## C3DTools.Geometry

Responsible for:

- NetTopologySuite operations
- polygon repair
- polygon intersection
- geometry conversion
- area calculations
- AutoCAD geometry extraction

---

## C3DTools.Hydrology

Responsible for:

- exporting basin_model.json
- importing results.json
- validation integration
- Python process execution
- result synchronization
- label/table updates

This project acts as the bridge between Civil 3D and Python.

---

# Python Engine Responsibilities

The Python engine owns all hydrology calculations.

It must operate entirely independent of Civil 3D.

---

# Python Engine Architecture

## schema.py

Defines all core model objects.

Examples:

```python
Project
StormEvent
Basin
TcSegment
LandUseArea
Node
Reach
Pond
HydrographResult
```

---

## validators.py

Performs model validation.

Validation categories:

### Geometry

- unclosed basins
- overlapping basins
- disconnected Tc paths

### Hydrology

- invalid CN
- invalid Tc
- missing land use
- missing outlet node

### Network

- disconnected routing
- circular routing
- invalid downstream references

Validation output:

```json
{
  "errors": [],
  "warnings": [],
  "info": []
}
```

---

## curve_number.py

Responsible for:

- composite CN calculations
- impervious percentage
- weighted averaging

---

## tc.py

Responsible for:

- sheet flow calculations
- shallow concentrated flow
- channel flow
- travel time calculations

Must support segmented Tc paths.

---

## rainfall.py

Responsible for:

- NOAA Atlas 14 support
- storm distributions
- rainfall depths
- temporal distributions

Initially allow manual storm input.

---

## engines/hydrology_engine.py

Wrapper around C3DTools hydrology calculations.

DO NOT expose third-party hydrology packages directly to the application.

Required structure:

```python
class HydrologyEngine:
    def run_basin(self, basin, storm):
        pass

    def run_model(self, model):
        pass
```

---

## reports/excel_report.py

Generates Excel reports.

Initial reports:

- Basin Summary
- Land Use Summary
- Composite CN Table
- Tc Worksheet
- Peak Flow Summary
- Validation Report

Use openpyxl.

---

# JSON Schema Requirements

## basin_model.json

This file is the core contract.

Minimum schema:

```json
{
  "project": {},
  "storms": [],
  "basins": [],
  "nodes": [],
  "links": []
}
```

---

## Basin Object

```json
{
  "id": "B-1",
  "name": "Basin 1",
  "condition": "Proposed",
  "area_acres": 4.21,
  "curve_number": 78,
  "tc_minutes": 18.4,
  "outlet_node": "J-1",
  "land_uses": [],
  "tc_segments": []
}
```

---

## Tc Segment

```json
{
  "type": "sheet",
  "length_ft": 120,
  "slope": 0.02,
  "manning_n": 0.24
}
```

---

# Initial Commands

## EXPORT_HYDRO_MODEL

Responsibilities:

- collect basin geometry
- collect land use data
- collect Tc path data
- export basin_model.json

---

## RUN_HYDRO_MODEL

Responsibilities:

1. export model
2. call Python engine
3. wait for completion
4. load results.json
5. display summary

---

## IMPORT_HYDRO_RESULTS

Responsibilities:

- update labels
- update object data
- update property sets
- create summary tables

---

# Python CLI

```bash
hydro run basin_model.json --out results.json --excel report.xlsx
```

The CLI must work without Civil 3D.

---

# MVP Scope

The MVP should ONLY support:

## Inputs

- basin polygons
- land use polygons
- Tc paths

## Calculations

- area
- composite CN
- Tc

## Outputs

- Excel report
- results.json

DO NOT build routing, ponds, or full hydrographs initially.

---

# Immediate Build Order

## Step 1

Build:

```text
basin_model.json exporter
```

## Step 2

Build:

```text
Python schema + validators
```

## Step 3

Build:

```text
Composite CN calculations
```

## Step 4

Build:

```text
Tc calculations
```

## Step 5

Build:

```text
Excel report generation
```

## Step 6

Build:

```text
local NRCS/SCS hydrograph generation
```

---

# Success Criteria

The application is successful when:

1. An engineer can create a basin map in Civil 3D.
2. The plugin exports a valid hydrology model.
3. Python deterministically calculates hydrology results.
4. Results can be reviewed externally.
5. Results synchronize back into Civil 3D.
6. The workflow is reproducible and auditable.
