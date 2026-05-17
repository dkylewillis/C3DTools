# Hydrology Workflow

1. Tag basin polylines in Civil 3D with `TAGBASIN` or the Basin Tools palette.
2. Configure land-use hatch layer patterns in the Basin Tools palette settings.
3. Run `SET_BASIN_TC` on each basin that needs a manual Tc value.
4. Run `SET_BASIN_ROUTE` on routed basins, using the downstream basin ID or `OUTLET`.
5. Run `EXPORT_HYDRO_MODEL` to create `basin_model.json`.
6. Run the Python engine with `hydro run basin_model.json --out results.json --excel report.xlsx`, or use `RUN_HYDRO_MODEL` from Civil 3D when Python is available on `PATH`.
7. Review `results.json`, `validation_report.json`, and the Excel workbook outside Civil 3D.

Civil 3D owns drawing extraction. The Python engine owns validation and hydrology calculations.