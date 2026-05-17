from __future__ import annotations

import argparse
import json
from pathlib import Path

from hydro_app.engines.pyflo_engine import PyfloEngine
from hydro_app.rainfall import atlas14_storm_from_csv, atlas14_storms_from_csv
from hydro_app.reports.excel_report import write_excel_report
from hydro_app.schema import HydrologyModel, Storm
from hydro_app.validators import validate_model


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(prog="hydro")
    subparsers = parser.add_subparsers(dest="command", required=True)

    run_parser = subparsers.add_parser("run")
    run_parser.add_argument("model_path")
    run_parser.add_argument("--out", default="results.json")
    run_parser.add_argument("--excel")
    run_parser.add_argument("--validation", default="validation_report.json")
    run_parser.add_argument("--atlas14-depths", help="NOAA Atlas 14 precipitation depth CSV to use as rainfall input")
    run_parser.add_argument("--atlas14-duration", default="24-hr", help="Atlas 14 duration label to select, such as 15-min, 60-min, or 24-hr")
    run_parser.add_argument("--atlas14-ari", type=int, help="Atlas 14 ARI year to select. If omitted, all available ARIs in --atlas14-ari-range are used.")
    run_parser.add_argument("--atlas14-ari-range", default="1-100", help="Atlas 14 ARI range to include when --atlas14-ari is omitted")
    run_parser.add_argument("--distribution", default="ATLAS14_ALTERNATING_BLOCK", help="Rainfall distribution label for generated storm metadata")
    run_parser.add_argument("--time-step-minutes", type=int, default=2, help="Hydrograph time step for generated storm metadata")

    args = parser.parse_args(argv)
    if args.command == "run":
        return run(args)
    return 1


def run(args: argparse.Namespace) -> int:
    model_path = Path(args.model_path)
    with model_path.open("r", encoding="utf-8") as file:
        raw_model = json.load(file)

    model = HydrologyModel.from_dict(raw_model)
    if args.atlas14_depths:
        try:
            if args.atlas14_ari is not None:
                storms = [
                    Storm.from_dict(
                        atlas14_storm_from_csv(
                            args.atlas14_depths,
                            args.atlas14_duration,
                            args.atlas14_ari,
                            args.distribution,
                            args.time_step_minutes,
                        )
                    )
                ]
            else:
                min_ari, max_ari = parse_ari_range(args.atlas14_ari_range)
                storms = [
                    Storm.from_dict(storm)
                    for storm in atlas14_storms_from_csv(
                        args.atlas14_depths,
                        args.atlas14_duration,
                        min_ari,
                        max_ari,
                        args.distribution,
                        args.time_step_minutes,
                    )
                ]
        except ValueError as error:
            print(f"Atlas 14 rainfall input failed: {error}")
            return 2

        model.storms = [*model.storms, *storms]
    validation = validate_model(model)
    results = PyfloEngine().run_model(model)

    out_path = Path(args.out)
    out_path.parent.mkdir(parents=True, exist_ok=True)
    out_path.write_text(json.dumps(results, indent=2), encoding="utf-8")

    validation_path = Path(args.validation)
    validation_path.parent.mkdir(parents=True, exist_ok=True)
    validation_path.write_text(json.dumps(validation.to_dict(), indent=2), encoding="utf-8")

    if args.excel:
        write_excel_report(results, args.excel)

    print(f"Processed {len(model.basins)} basin(s); wrote {out_path}.")
    return 0 if validation.is_valid else 2


def parse_ari_range(value: str) -> tuple[int, int]:
    parts = [part.strip() for part in value.split("-", 1)]
    if len(parts) == 1:
        ari = int(parts[0])
        return ari, ari
    return int(parts[0]), int(parts[1])


if __name__ == "__main__":
    raise SystemExit(main())