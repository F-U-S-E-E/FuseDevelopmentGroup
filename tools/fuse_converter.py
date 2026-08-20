#!/usr/bin/env python3
"""
fuse_converter.py - official drag-and-drop legacy-to-FUSE converter.

This is the day-to-day wrapper around FUSE's conversion helpers. It accepts
legacy folders, zip files, and single JSON files, detects the package family,
and writes a .FUSE output folder plus conversion reports.

The converter reads legacy data documents only. It does not import or execute
legacy mod code.
"""

from __future__ import annotations

import argparse
import json
import re
import shutil
import sys
import tempfile
import zipfile
from dataclasses import dataclass, field
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Iterable


SCRIPT_DIR = Path(__file__).resolve().parent
if str(SCRIPT_DIR) not in sys.path:
    sys.path.insert(0, str(SCRIPT_DIR))

import fuse_convert  # noqa: E402
import convert_fuse_audio  # noqa: E402
import legacy_json  # noqa: E402


TOOL_VERSION = "0.2.1"
DEFAULT_STEAM_MODS = Path(r"C:\Steam\steamapps\common\Railroader\Mods")
DEFAULT_MANAGER_VERSION = "0.27.10"
JSON_MANIFEST_NAMES = {"definition.json", "info.json"}
BATCH_SKIP_DIR_NAMES = {"fuseconverted", "converted", "dist", "__pycache__"}
LEGACY_DATA_KEYS = {
    "tracks",
    "areas",
    "industries",
    "loads",
    "turntables",
    "scenery",
    "splineys",
    "mandelas",
    "texts",
    "simpleGraphs",
    "progression",
    "progressions",
    "mapFeatures",
}
SPECIAL_SPLINE_HANDLERS = {
    fuse_convert.TURNTABLE_HANDLER,
    *fuse_convert.LOADER_HANDLERS,
    *fuse_convert.STATION_HANDLERS,
    fuse_convert.MAP_LABEL_HANDLER,
    *fuse_convert.TELEGRAPH_POLE_MOVER_HANDLERS,
}


@dataclass
class ReportEntry:
    level: str
    message: str
    file: str = ""
    concept: str = ""


@dataclass
class FileSummary:
    source_file: str
    output_file: str
    counts: dict[str, int] = field(default_factory=dict)


@dataclass
class ConversionReport:
    source: Path
    output: Path
    detected_kind: str
    status: str = "pending"
    package_id: str = ""
    display_name: str = ""
    generated_files: list[str] = field(default_factory=list)
    counts: dict[str, int] = field(default_factory=dict)
    file_summaries: list[FileSummary] = field(default_factory=list)
    entries: list[ReportEntry] = field(default_factory=list)

    def add(self, level: str, message: str, file: Path | str = "", concept: str = "") -> None:
        self.entries.append(ReportEntry(level=level, message=message, file=str(file), concept=concept))

    def count(self, key: str, amount: int) -> None:
        if amount:
            self.counts[key] = self.counts.get(key, 0) + int(amount)

    def add_file_summary(self, source_file: str | Path, output_file: str | Path, counts: dict[str, int] | None = None) -> None:
        clean_counts = {key: int(value) for key, value in (counts or {}).items() if value}
        self.file_summaries.append(
            FileSummary(source_file=str(source_file), output_file=str(output_file), counts=clean_counts)
        )

    @property
    def warnings(self) -> int:
        return sum(1 for entry in self.entries if entry.level.upper() == "WARN")

    @property
    def errors(self) -> int:
        return sum(1 for entry in self.entries if entry.level.upper() == "ERROR")

    def finish(self) -> None:
        if self.errors:
            self.status = "failed"
        elif self.warnings:
            self.status = "converted_with_warnings"
        else:
            self.status = "converted"

    def outcome_buckets(self) -> dict[str, int]:
        infrastructure_counts = {"assetPackSources", "assetPacks", "mapTileFiles"}
        converted_entries = sum(
            int(value) for key, value in self.counts.items() if key not in infrastructure_counts
        )
        buckets = {
            "convertedEntries": converted_entries,
            "generatedFiles": len(self.generated_files),
            "sourceFileSummaries": len(self.file_summaries),
            "repairedEntries": 0,
            "preservedEntries": 0,
            "unresolvedEntries": 0,
            "unsupportedEntries": 0,
            "dependencyRequiredEntries": 0,
            "warningEntries": self.warnings,
            "errorEntries": self.errors,
        }

        for entry in self.entries:
            concept = (entry.concept or "").lower()
            message = (entry.message or "").lower()

            if "script-binary" in concept or "unsupported" in concept:
                buckets["unsupportedEntries"] += 1
            elif (
                "external" in concept
                or "dependency" in concept
                or "optional" in concept
                or "track-group-not-auto-enabled" in concept
                or "missing mixinto" in message
            ):
                buckets["dependencyRequiredEntries"] += 1
            elif any(token in concept for token in ("repaired", "alias", "overflow", "underflow", "empty-id")):
                buckets["repairedEntries"] += 1
            elif "unresolved" in concept or "unknown" in concept or "crossed" in concept:
                buckets["unresolvedEntries"] += 1
            elif "preserv" in concept or "preserv" in message or "spanless" in concept:
                buckets["preservedEntries"] += 1

        return buckets

    def to_dict(self) -> dict[str, Any]:
        return {
            "tool": "FUSE Official Converter",
            "toolVersion": TOOL_VERSION,
            "createdUtc": datetime.now(timezone.utc).isoformat(),
            "source": str(self.source),
            "output": str(self.output),
            "detectedKind": self.detected_kind,
            "status": self.status,
            "packageId": self.package_id,
            "displayName": self.display_name,
            "generatedFiles": self.generated_files,
            "counts": dict(sorted(self.counts.items())),
            "outcomeBuckets": self.outcome_buckets(),
            "fileSummaries": [
                {
                    "sourceFile": summary.source_file,
                    "outputFile": summary.output_file,
                    "counts": dict(sorted(summary.counts.items())),
                }
                for summary in self.file_summaries
            ],
            "warnings": self.warnings,
            "errors": self.errors,
            "entries": [entry.__dict__ for entry in self.entries],
        }


def read_json(path: Path, lenient: bool = True) -> Any:
    if lenient:
        return legacy_json.read_json(path)
    return json.loads(path.read_text(encoding="utf-8-sig"))


def write_json(path: Path, data: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(data, indent=2, ensure_ascii=False), encoding="utf-8")


def write_text(path: Path, text: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(text, encoding="utf-8")


def markdown_cell(value: Any) -> str:
    text = str(value)
    return text.replace("\\", "\\\\").replace("|", "\\|").replace("\r", " ").replace("\n", " ")


def slug(value: str, fallback: str = "fuse-package") -> str:
    text = re.sub(r"[^A-Za-z0-9._-]+", "-", value or "").strip("-._")
    return text or fallback


def output_name_for(source: Path, explicit_name: str | None = None) -> str:
    name = explicit_name or source.stem if source.is_file() else explicit_name or source.name
    name = slug(name)
    lower = name.lower()
    if lower.endswith(".fuse"):
        return name
    if lower.endswith(".rail"):
        name = name[:-5]
    return f"{name}.FUSE"


def default_output_root() -> Path:
    return DEFAULT_STEAM_MODS if DEFAULT_STEAM_MODS.exists() else Path.cwd() / "converted"


def ensure_safe_delete(path: Path, root: Path) -> None:
    resolved_path = path.resolve()
    resolved_root = root.resolve()
    if resolved_path == resolved_root or resolved_root not in resolved_path.parents:
        raise RuntimeError(f"refusing to delete outside output root: {resolved_path}")
    if resolved_path.exists():
        shutil.rmtree(resolved_path)


def is_json_file(path: Path) -> bool:
    return path.is_file() and path.suffix.lower() == ".json"


def iter_json_files(source: Path) -> Iterable[Path]:
    if is_json_file(source):
        yield source
        return
    if not source.is_dir():
        return
    for path in sorted(source.rglob("*.json"), key=lambda item: str(item).lower()):
        if path.name.lower().endswith(".bak"):
            continue
        yield path


def iter_direct_json_files(source: Path) -> Iterable[Path]:
    if is_json_file(source):
        yield source
        return
    if not source.is_dir():
        return
    try:
        children = sorted(source.iterdir(), key=lambda item: item.name.lower())
    except OSError:
        return
    for child in children:
        if child.is_file() and child.suffix.lower() == ".json" and not child.name.lower().endswith(".bak"):
            yield child


def has_case_file(folder: Path, name: str) -> bool:
    wanted = name.lower()
    try:
        return any(child.is_file() and child.name.lower() == wanted for child in folder.iterdir())
    except OSError:
        return False


def is_asset_pack_folder(folder: Path) -> bool:
    return (
        folder.is_dir()
        and has_case_file(folder, "bundle")
        and has_case_file(folder, "Catalog.json")
        and has_case_file(folder, "Definitions.json")
    )


def has_direct_asset_pack_children(folder: Path) -> bool:
    try:
        return any(is_asset_pack_folder(child) for child in folder.iterdir() if child.is_dir())
    except OSError:
        return False


def iter_asset_pack_folders(folder: Path) -> Iterable[Path]:
    if not folder.is_dir():
        return

    if is_asset_pack_folder(folder):
        yield folder
        return

    try:
        children = sorted(folder.rglob("*"), key=lambda item: str(item).lower())
    except OSError:
        return

    for child in children:
        if child.is_dir() and is_asset_pack_folder(child):
            yield child


def has_asset_packs_recursive(folder: Path) -> bool:
    return any(True for _ in iter_asset_pack_folders(folder))


def count_asset_packs(folder: Path) -> int:
    return sum(1 for _ in iter_asset_pack_folders(folder))


def find_asset_pack_sources(source: Path) -> list[str]:
    if not source.is_dir():
        return []

    sources: list[str] = []
    if is_asset_pack_folder(source):
        return ["."]

    sc_asset_packs = source / "SCAssetPacks"
    if has_asset_packs_recursive(sc_asset_packs):
        sources.append("SCAssetPacks")

    if has_direct_asset_pack_children(source):
        sources.append(".")

    try:
        for child in sorted(source.iterdir(), key=lambda item: item.name.lower()):
            if not child.is_dir() or child.name.lower() == "scassetpacks":
                continue
            if has_asset_packs_recursive(child):
                sources.append(child.name)
    except OSError:
        pass

    seen = set()
    unique = []
    for item in sources:
        key = item.lower()
        if key in seen:
            continue
        seen.add(key)
        unique.append(item)
    return unique


def find_map_tile_sources(source: Path) -> list[Path]:
    if not source.is_dir():
        return []

    def contains_tiles(folder: Path) -> bool:
        try:
            return any(tile.is_file() and tile.suffix.lower() == ".data" for tile in folder.iterdir())
        except OSError:
            return False

    result: list[Path] = []
    if contains_tiles(source):
        result.append(source)

    maps_root = source / "Maps"
    if maps_root.is_dir():
        if contains_tiles(maps_root):
            result.append(maps_root)

        try:
            for child in sorted(maps_root.iterdir(), key=lambda item: item.name.lower()):
                if child.is_dir() and contains_tiles(child):
                    result.append(child)
        except OSError:
            pass

    try:
        for child in sorted(source.iterdir(), key=lambda item: item.name.lower()):
            if child.is_dir() and child.name.lower() != "maps" and contains_tiles(child):
                result.append(child)
    except OSError:
        pass

    seen = set()
    unique: list[Path] = []
    for item in result:
        key = str(item.resolve()).lower()
        if key in seen:
            continue
        seen.add(key)
        unique.append(item)
    return unique


def detect_audio_json(path: Path) -> str:
    try:
        data = read_json(path)
    except Exception:
        return ""

    if not isinstance(data, list):
        return ""
    entries = [item for item in data if isinstance(item, dict)]
    if not entries:
        return ""

    lower_name = path.name.lower()
    if any(isinstance(item.get("layers"), list) for item in entries):
        return "horns"
    if any(item.get("clip") for item in entries):
        return "whistles"
    if "hellsbell" in lower_name or "bell" in lower_name or any("indexTimes" in item for item in entries):
        return "bells"
    return ""


def detects_direct_audio(source: Path) -> bool:
    manifest, _ = read_manifest(source)
    mixintos = manifest.get("mixintos") or manifest.get("Mixintos") or {}
    if isinstance(mixintos, dict) and any(str(key).lower() in ("whistles", "horns", "bells", "hellsbells") for key in mixintos):
        return True
    return any(detect_audio_json(path) for path in iter_direct_json_files(source) if path.name.lower() not in JSON_MANIFEST_NAMES)


def read_manifest(source: Path) -> tuple[dict[str, Any], Path | None]:
    if source.is_file():
        folder = source.parent
    else:
        folder = source

    for name in ("Definition.json", "definition.json", "Info.json", "info.json"):
        path = folder / name
        if path.exists():
            try:
                data = read_json(path)
                if isinstance(data, dict):
                    return data, path
            except Exception:
                return {}, path
    return {}, None


def detects_audio(source: Path) -> bool:
    manifest, _ = read_manifest(source)
    mixintos = manifest.get("mixintos") or manifest.get("Mixintos") or {}
    if isinstance(mixintos, dict) and any(str(key).lower() in ("whistles", "horns", "bells", "hellsbells") for key in mixintos):
        return True
    return any(detect_audio_json(path) for path in iter_json_files(source) if path.name.lower() not in JSON_MANIFEST_NAMES)


def detects_route_data(source: Path) -> bool:
    for path in iter_json_files(source):
        if path.name.lower() in JSON_MANIFEST_NAMES:
            continue
        try:
            data = read_json(path)
        except Exception:
            continue
        if isinstance(data, dict) and any(key in data for key in LEGACY_DATA_KEYS):
            return True
    return False


def detects_direct_route_data(source: Path) -> bool:
    for path in iter_direct_json_files(source):
        if path.name.lower() in JSON_MANIFEST_NAMES:
            continue
        try:
            data = read_json(path)
        except Exception:
            continue
        if isinstance(data, dict) and any(key in data for key in LEGACY_DATA_KEYS):
            return True
    return False


def has_direct_convertible_json(source: Path) -> bool:
    return any(path.name.lower() not in JSON_MANIFEST_NAMES for path in iter_direct_json_files(source))


def has_direct_route_data(folder: Path) -> bool:
    if not folder.is_dir():
        return False
    try:
        files = [path for path in folder.iterdir() if path.is_file() and path.suffix.lower() == ".json"]
    except OSError:
        return False

    for path in files:
        if path.name.lower() in JSON_MANIFEST_NAMES or path.name.lower().endswith(".bak"):
            continue
        try:
            data = read_json(path)
        except Exception:
            continue
        if isinstance(data, dict) and any(key in data for key in LEGACY_DATA_KEYS):
            return True
    return False


def has_direct_map_tile_sources(source: Path) -> bool:
    if not source.is_dir():
        return False

    def contains_tiles(folder: Path) -> bool:
        try:
            return any(tile.is_file() and tile.suffix.lower() == ".data" for tile in folder.iterdir())
        except OSError:
            return False

    if contains_tiles(source):
        return True

    maps_root = source / "Maps"
    if maps_root.is_dir():
        if contains_tiles(maps_root):
            return True
        try:
            if any(child.is_dir() and contains_tiles(child) for child in maps_root.iterdir()):
                return True
        except OSError:
            return False

    return False


def has_direct_asset_pack_sources(source: Path) -> bool:
    if not source.is_dir():
        return False
    if is_asset_pack_folder(source):
        return True
    if has_asset_packs_recursive(source / "SCAssetPacks"):
        return True
    return has_direct_asset_pack_children(source)


def has_direct_native_fuse_data(source: Path) -> bool:
    if not source.is_dir():
        return False
    try:
        return any(path.is_file() and path.name.lower().endswith(".fuse.json") for path in source.iterdir())
    except OSError:
        return False


def has_direct_compiled_plugin(source: Path) -> bool:
    if not source.is_dir():
        return False
    try:
        return any(path.is_file() and path.suffix.lower() == ".dll" for path in source.iterdir())
    except OSError:
        return False


def detect_direct_kind(source: Path, requested: str) -> str:
    if source.is_file():
        if source.suffix.lower() == ".zip":
            return requested if requested != "auto" else "archive"
        if source.suffix.lower() == ".json" and source.name.lower() not in JSON_MANIFEST_NAMES:
            if requested != "auto":
                return requested
            if detects_direct_audio(source) and not detects_direct_route_data(source):
                return "audio"
            return "route"
        return "unknown"

    if not source.is_dir():
        return "unknown"

    direct_audio = detects_direct_audio(source)
    direct_route = detects_direct_route_data(source)
    direct_tiles = has_direct_map_tile_sources(source)
    direct_assets = has_direct_asset_pack_sources(source)
    direct_json = has_direct_convertible_json(source)

    if has_direct_native_fuse_data(source):
        return "native"

    if requested == "route":
        return "route" if direct_route or direct_tiles or direct_json else "unknown"
    if requested == "audio":
        return "audio" if direct_audio or direct_json else "unknown"
    if direct_audio and not direct_route:
        return "audio"
    if direct_route:
        return "route"
    if has_direct_compiled_plugin(source):
        return "code"
    if direct_tiles:
        return "map_tile"
    if direct_assets:
        return "asset"
    return "unknown"


def find_route_root(source: Path) -> Path:
    if source.is_file():
        return source
    if has_direct_route_data(source):
        return source

    candidates = []
    for folder in source.rglob("*"):
        if folder.is_dir() and has_direct_route_data(folder):
            candidates.append(folder)
    if not candidates:
        return source
    return sorted(candidates, key=lambda item: (len(item.parts), str(item).lower()))[0]


def detect_kind(source: Path, requested: str) -> str:
    if requested != "auto":
        return requested

    if source.is_dir() and has_direct_native_fuse_data(source):
        return "native"
    contains_compiled_plugin = source.is_dir() and has_direct_compiled_plugin(source)
    if (
        contains_compiled_plugin
        and not detects_direct_route_data(source)
        and not detects_direct_audio(source)
    ):
        return "code"
    route_data = detects_route_data(source)
    if detects_audio(source) and not route_data:
        return "audio"
    if route_data:
        return "route"
    if find_map_tile_sources(source):
        return "map_tile"
    if find_asset_pack_sources(source):
        return "asset"
    if is_json_file(source):
        return "route"
    return "unknown"


def copy_asset_sources(source: Path, output: Path, roots: list[str]) -> None:
    for root in roots:
        if root == ".":
            if is_asset_pack_folder(source):
                for child in source.iterdir():
                    destination = output / child.name
                    if child.is_dir():
                        shutil.copytree(child, destination, dirs_exist_ok=True)
                    else:
                        shutil.copy2(child, destination)
                continue
            for child in source.iterdir():
                if is_asset_pack_folder(child):
                    shutil.copytree(child, output / child.name, dirs_exist_ok=True)
            continue

        source_root = source / root
        if source_root.exists():
            shutil.copytree(source_root, output / root, dirs_exist_ok=True)


def update_info_json(output: Path, update: dict[str, Any]) -> None:
    info_path = output / "Info.json"
    info = {}
    if info_path.exists():
        try:
            loaded = read_json(info_path)
            if isinstance(loaded, dict):
                info = loaded
        except Exception:
            info = {}
    info.update(update)
    write_json(info_path, info)


def write_reports(report: ConversionReport) -> None:
    report.finish()
    report_folder = report_output_folder(report)
    write_json(report_folder / "conversion-report.json", report.to_dict())

    lines = [
        f"# FUSE Conversion Report - {report.output.name}",
        "",
        f"- Source: `{report.source}`",
        f"- Output: `{report.output}`",
        f"- Detected kind: `{report.detected_kind}`",
        f"- Status: `{report.status}`",
        f"- Package id: `{report.package_id}`",
        f"- Warnings: {report.warnings}",
        f"- Errors: {report.errors}",
        "",
        "## Counts",
        "",
        "| Item | Count |",
        "| --- | ---: |",
    ]
    for key, value in sorted(report.counts.items()):
        lines.append(f"| `{key}` | {value} |")
    if not report.counts:
        lines.append("| _(none)_ | 0 |")

    lines.extend(["", "## Outcome Buckets", "", "| Bucket | Count |", "| --- | ---: |"])
    for key, value in sorted(report.outcome_buckets().items()):
        lines.append(f"| `{key}` | {value} |")

    lines.extend(["", "## File Summaries", "", "| Source File | Output File | Counts |", "| --- | --- | --- |"])
    for summary in report.file_summaries:
        counts = ", ".join(f"{key}={value}" for key, value in sorted(summary.counts.items())) or "0"
        lines.append(
            f"| `{markdown_cell(summary.source_file)}` | `{markdown_cell(summary.output_file)}` | {markdown_cell(counts)} |"
        )
    if not report.file_summaries:
        lines.append("| _(none)_ | _(none)_ | 0 |")

    lines.extend(["", "## Generated Files", "", "| File |", "| --- |"])
    for file in report.generated_files:
        lines.append(f"| `{file}` |")
    if not report.generated_files:
        lines.append("| _(none)_ |")

    lines.extend(["", "## Messages", "", "| Level | Concept | File | Message |", "| --- | --- | --- | --- |"])
    for entry in report.entries:
        lines.append(
            f"| {entry.level} | `{markdown_cell(entry.concept)}` | `{markdown_cell(entry.file)}` | {markdown_cell(entry.message)} |"
        )
    if not report.entries:
        lines.append("| OK |  |  | No warnings or errors. |")
    write_text(report_folder / "conversion-report.md", "\n".join(lines) + "\n")


def report_output_folder(report: ConversionReport) -> Path:
    if report.status != "failed":
        return report.output
    return report.output.parent / "_conversion-reports" / report.output.name


def write_batch_reports(source_root: Path, out_root: Path, reports: list[ConversionReport]) -> None:
    out_root.mkdir(parents=True, exist_ok=True)
    audit_dir = out_root / "conversion-reports"
    audit_dir.mkdir(parents=True, exist_ok=True)

    package_rows = []
    aggregate_counts: dict[str, int] = {}
    aggregate_buckets: dict[str, int] = {}
    copied_reports = []

    for index, report in enumerate(reports, start=1):
        for key, value in report.counts.items():
            aggregate_counts[key] = aggregate_counts.get(key, 0) + int(value)
        for key, value in report.outcome_buckets().items():
            aggregate_buckets[key] = aggregate_buckets.get(key, 0) + int(value)

        report_name = slug(report.package_id or report.output.name or f"report-{index}", f"report-{index}")
        report_stem = f"{index:03d}-{report_name}"
        source_report_folder = report_output_folder(report)
        json_source = source_report_folder / "conversion-report.json"
        md_source = source_report_folder / "conversion-report.md"
        json_dest = audit_dir / f"{report_stem}.json"
        md_dest = audit_dir / f"{report_stem}.md"
        if json_source.exists():
            shutil.copy2(json_source, json_dest)
            copied_reports.append(str(json_dest.relative_to(out_root)).replace("\\", "/"))
        if md_source.exists():
            shutil.copy2(md_source, md_dest)
            copied_reports.append(str(md_dest.relative_to(out_root)).replace("\\", "/"))

        package_rows.append({
            "source": str(report.source),
            "output": str(report.output),
            "packageId": report.package_id,
            "displayName": report.display_name,
            "detectedKind": report.detected_kind,
            "status": report.status,
            "warnings": report.warnings,
            "errors": report.errors,
            "generatedFiles": len(report.generated_files),
            "sourceFileSummaries": len(report.file_summaries),
        })

    failed = sum(1 for report in reports if report.status == "failed")
    batch_data = {
        "tool": "FUSE Official Converter",
        "toolVersion": TOOL_VERSION,
        "createdUtc": datetime.now(timezone.utc).isoformat(),
        "sourceRoot": str(source_root),
        "outputRoot": str(out_root),
        "converted": len(reports) - failed,
        "failed": failed,
        "warnings": sum(report.warnings for report in reports),
        "errors": sum(report.errors for report in reports),
        "counts": dict(sorted(aggregate_counts.items())),
        "outcomeBuckets": dict(sorted(aggregate_buckets.items())),
        "copiedReports": copied_reports,
        "packages": package_rows,
    }
    write_json(out_root / "conversion-batch-report.json", batch_data)

    lines = [
        "# FUSE Batch Conversion Report",
        "",
        f"- Source root: `{source_root}`",
        f"- Output root: `{out_root}`",
        f"- Converted: {batch_data['converted']}/{len(reports)}",
        f"- Warnings: {batch_data['warnings']}",
        f"- Errors: {batch_data['errors']}",
        "",
        "## Packages",
        "",
        "| Status | Kind | Package | Source | Files | Warnings | Errors |",
        "| --- | --- | --- | --- | ---: | ---: | ---: |",
    ]
    for row in package_rows:
        lines.append(
            "| "
            f"{markdown_cell(row['status'])} | "
            f"{markdown_cell(row['detectedKind'])} | "
            f"`{markdown_cell(row['packageId'] or row['displayName'] or row['output'])}` | "
            f"`{markdown_cell(row['source'])}` | "
            f"{row['sourceFileSummaries']} | "
            f"{row['warnings']} | "
            f"{row['errors']} |"
        )

    lines.extend(["", "## Outcome Buckets", "", "| Bucket | Count |", "| --- | ---: |"])
    for key, value in sorted(aggregate_buckets.items()):
        lines.append(f"| `{key}` | {value} |")

    lines.extend(["", "## Copied Reports", "", "| File |", "| --- |"])
    for path in copied_reports:
        lines.append(f"| `{markdown_cell(path)}` |")
    if not copied_reports:
        lines.append("| _(none)_ |")

    write_text(out_root / "conversion-batch-report.md", "\n".join(lines) + "\n")


def scan_legacy_warnings(source: Path, report: ConversionReport) -> None:
    seen_runtime_ids: dict[tuple[str, str], Path] = {}
    for path in iter_json_files(source):
        if path.name.lower() in JSON_MANIFEST_NAMES:
            continue
        try:
            data = read_json(path)
        except Exception as exc:
            report.add("WARN", f"Could not inspect JSON for warnings: {exc}", path)
            continue
        scan_value_for_duplicate_runtime_ids(data, report, path, seen_runtime_ids)
        scan_value_for_warnings(data, report, path)

    if source.is_dir():
        for path in source.rglob("*"):
            if path.suffix.lower() in (".dll", ".pdb"):
                report.add("WARN", "Script/debug binary copied or ignored as data only; FUSE does not convert executable plugin behavior.", path, "script-binary")


def scan_value_for_duplicate_runtime_ids(
    value: Any,
    report: ConversionReport,
    file: Path,
    seen_runtime_ids: dict[tuple[str, str], Path],
    path: str = "",
) -> None:
    if isinstance(value, dict):
        if path.endswith("splineys"):
            for object_id, item in value.items():
                if not isinstance(item, dict):
                    continue

                runtime_kind = runtime_kind_for_spliney_handler(item.get("handler"))
                if runtime_kind:
                    record_runtime_id(runtime_kind, object_id, report, file, seen_runtime_ids)

        for key, item in value.items():
            scan_value_for_duplicate_runtime_ids(item, report, file, seen_runtime_ids, f"{path}.{key}" if path else str(key))
    elif isinstance(value, list):
        for index, item in enumerate(value):
            scan_value_for_duplicate_runtime_ids(item, report, file, seen_runtime_ids, f"{path}[{index}]")


def runtime_kind_for_spliney_handler(handler: Any) -> str | None:
    if handler == fuse_convert.TURNTABLE_HANDLER:
        return "turntable"
    if handler in fuse_convert.LOADER_HANDLERS:
        return "loader"
    if handler in fuse_convert.STATION_HANDLERS:
        return "station"
    if handler == fuse_convert.MAP_LABEL_HANDLER:
        return "map label"
    return None


def record_runtime_id(
    runtime_kind: str,
    object_id: Any,
    report: ConversionReport,
    file: Path,
    seen_runtime_ids: dict[tuple[str, str], Path],
) -> None:
    if not object_id:
        return

    key = (runtime_kind, str(object_id).lower())
    first_file = seen_runtime_ids.get(key)
    if first_file is None:
        seen_runtime_ids[key] = file
        return

    if first_file == file:
        return

    report.add(
        "INFO",
        f"Duplicate legacy {runtime_kind} id '{object_id}' also appeared in '{first_file.name}'. "
        "FUSE keeps one output file per source file and preserves the same runtime id so later mixinto files update/replace the earlier object.",
        file,
        f"duplicate-{runtime_kind}-id",
    )


def scan_value_for_warnings(value: Any, report: ConversionReport, file: Path, path: str = "") -> None:
    if isinstance(value, dict):
        if "formula" in value and not is_supported_formula(value.get("formula")):
            report.add("WARN", "Legacy formula field could not be recognized as a FUSE formulaic industry component.", file, "formula")
        if "interchangeTransfers" in value and not isinstance(value.get("interchangeTransfers"), dict):
            report.add("WARN", "Legacy interchangeTransfers must be an object mapping source interchange id to destination interchange id.", file, "interchangeTransfers")
        handler = value.get("handler")
        if isinstance(handler, str):
            known_handler = handler in fuse_convert.HANDLER_MAP or handler in SPECIAL_SPLINE_HANDLERS or handler.lower() in fuse_convert.RR_CROSSING_HANDLERS
            if not known_handler:
                report.add("WARN", f"Unknown spline/object handler preserved in extensions where possible: {handler}", file, "handler")
        component_type = value.get("type")
        if isinstance(component_type, str) and "." in component_type:
            normalized = fuse_convert.normalize_component_type(component_type)
            if normalized == component_type and not fuse_convert.is_supported_custom_component_type(normalized):
                report.add("WARN", f"Unknown industry component type may need manual schema support: {component_type}", file, "component-type")
        for key, item in value.items():
            scan_value_for_warnings(item, report, file, f"{path}.{key}" if path else str(key))
    elif isinstance(value, list):
        for index, item in enumerate(value):
            scan_value_for_warnings(item, report, file, f"{path}[{index}]")


def is_supported_formula(value: Any) -> bool:
    if value is None:
        return True
    if not isinstance(value, dict):
        return False
    component_type = fuse_convert.normalize_component_type(value.get("type") or "formula")
    return component_type == "formulaic"


def conversion_meta(source: Path, fallback_id: str) -> tuple[str, str, str, str]:
    folder = source.parent if source.is_file() else source
    mod_id, name, version, author = fuse_convert.meta(folder)
    if mod_id == folder.name and source.is_file():
        mod_id = fallback_id
        name = source.stem
    return str(mod_id), str(name), str(version), str(author)


def convert_single_json(source: Path, output: Path, clean_output: bool, report: ConversionReport) -> None:
    if clean_output:
        ensure_safe_delete(output, output.parent)
    output.mkdir(parents=True, exist_ok=True)

    mod_id, name, version, author = conversion_meta(source, slug(source.stem))
    fragment = slug(source.stem, "fragment")
    rail = fuse_convert.skeleton(mod_id, name, version, author, fragment)
    fuse_convert.convert_source(read_json(source), rail, source_name=source.name)

    written = []
    data_name = f"{fragment}.fuse.json"
    fuse_convert.save_json(output / data_name, rail)
    written.append(data_name)
    counts = fuse_convert.count_content(rail)
    for key, count in counts.items():
        report.count(key, count)
    report.add_file_summary(source.name, data_name, counts)

    info = fuse_info(mod_id, f"{name} (FUSE)", author, version)
    info["FuseDataFiles"] = sorted(written, key=fuse_convert.rail_data_file_order)
    write_json(output / "Info.json", info)

    report.package_id = info["Id"]
    report.display_name = info["DisplayName"]
    report.generated_files.extend(["Info.json", *written])


def fuse_info(mod_id: str, display_name: str, author: str, version: str) -> dict[str, Any]:
    package_id = str(mod_id or "fuse-package").strip() or "fuse-package"
    lower = package_id.lower()
    if lower.endswith(".rail"):
        package_id = package_id[:-5]
        lower = package_id.lower()
    if not lower.endswith(".fuse"):
        package_id = f"{package_id}.FUSE"

    return {
        "$schema": ".\\schemas\\umm-info.schema.json",
        "Id": package_id,
        "DisplayName": display_name,
        "Author": author or "",
        "Version": str(version or "1.0.0"),
        "ManagerVersion": DEFAULT_MANAGER_VERSION,
        "Requirements": ["FUSE"],
        "LoadAfter": ["FUSE"],
    }


def convert_route(source: Path, output: Path, clean_output: bool, report: ConversionReport) -> None:
    if source.is_file():
        convert_single_json(source, output, clean_output, report)
        return

    if find_map_tile_sources(source) and not detects_route_data(source):
        convert_map_tiles(source, output, clean_output, report)
        return

    route_source = find_route_root(source)
    if route_source != source:
        report.add("INFO", f"Using nested legacy data folder: {route_source}", route_source, "route-root")

    if clean_output:
        ensure_safe_delete(output, output.parent)

    fuse_convert.reset_validation_state()
    try:
        summaries = fuse_convert.convert_mod(route_source.resolve(), output.resolve())
    except SystemExit as exc:
        raise RuntimeError(str(exc)) from exc
    finally:
        for entry in fuse_convert.drain_validation_warnings():
            report.add(
                entry.get("level", "WARN"),
                entry.get("message", ""),
                entry.get("file", ""),
                entry.get("concept", ""),
            )

    info, _ = read_manifest(output)
    report.package_id = str(info.get("Id") or f"{source.name}.FUSE")
    report.display_name = str(info.get("DisplayName") or output.name)
    report.generated_files.extend(["Info.json"])

    for source_name, output_name, counts in summaries:
        report.generated_files.append(output_name)
        for key, count in counts.items():
            report.count(key, count)
        report.add_file_summary(source_name, output_name, counts)

    asset_roots = find_asset_pack_sources(route_source) or find_asset_pack_sources(source)
    if asset_roots:
        copy_asset_sources(route_source if find_asset_pack_sources(route_source) else source, output, asset_roots)
        update_info_json(output, {"FuseAssetPacks": asset_roots})
        report.generated_files.append("asset-pack folders")
        report.count("assetPackSources", len(asset_roots))


def convert_map_tiles(source: Path, output: Path, clean_output: bool, report: ConversionReport) -> None:
    tile_sources = find_map_tile_sources(source)
    if not tile_sources:
        raise RuntimeError("no map tile folders found")

    if clean_output:
        ensure_safe_delete(output, output.parent)
    output.mkdir(parents=True, exist_ok=True)

    mod_id, name, version, author = conversion_meta(source, slug(source.name))
    rail = fuse_convert.skeleton(mod_id, name, version, author, "mapTiles")
    for index, tile_folder in enumerate(tile_sources):
        folder_name = tile_folder.name
        if tile_folder == source or (tile_folder.name.lower() == "maps" and tile_folder.parent == source):
            folder_name = source.name
        destination = output / "Maps" / folder_name
        shutil.copytree(tile_folder, destination, dirs_exist_ok=True)
        key = f"{slug(mod_id)}.{slug(folder_name)}"
        rail["world"]["mapTiles"][key] = {
            "directory": folder_name,
            "sourceFolder": f"Maps/{folder_name}",
            "priority": 100 + index,
        }
        tile_count = sum(1 for tile in tile_folder.iterdir() if tile.is_file() and tile.suffix.lower() == ".data")
        report.count("mapTileFiles", tile_count)
        report.add_file_summary(tile_folder, f"Maps/{folder_name}", {"mapTileFiles": tile_count})

    data_name = "mapTiles.fuse.json"
    write_json(output / data_name, rail)
    info = fuse_info(mod_id, f"{name} (FUSE Map Tiles)", author, version)
    info["FuseDataFiles"] = [data_name]
    write_json(output / "Info.json", info)

    report.package_id = info["Id"]
    report.display_name = info["DisplayName"]
    report.generated_files.extend(["Info.json", data_name, "Maps/"])
    report.count("world.mapTiles", len(rail["world"]["mapTiles"]))


def convert_asset(source: Path, output: Path, clean_output: bool, report: ConversionReport) -> None:
    asset_roots = find_asset_pack_sources(source)
    if not asset_roots:
        raise RuntimeError("no asset pack folders found")

    if clean_output:
        ensure_safe_delete(output, output.parent)
    output.mkdir(parents=True, exist_ok=True)

    if source.is_dir():
        for child in source.iterdir():
            if child.name.lower() == "info.json":
                continue
            destination = output / child.name
            if child.is_dir():
                shutil.copytree(child, destination, dirs_exist_ok=True)
            else:
                shutil.copy2(child, destination)

    mod_id, name, version, author = conversion_meta(source, slug(source.name))
    info = fuse_info(mod_id, f"{name} (FUSE Assets)", author, version)
    info["FuseAssetPacks"] = asset_roots
    write_json(output / "Info.json", info)

    report.package_id = info["Id"]
    report.display_name = info["DisplayName"]
    report.generated_files.extend(["Info.json", *asset_roots])
    report.count("assetPackSources", len(asset_roots))
    for root in asset_roots:
        root_path = output if root == "." else output / root
        if root_path.exists():
            asset_pack_count = count_asset_packs(root_path)
            report.count("assetPacks", asset_pack_count)
            report.add_file_summary(source / root if root != "." else source, root, {"assetPacks": asset_pack_count})


def convert_audio(source: Path, output: Path, clean_output: bool, report: ConversionReport) -> None:
    if clean_output:
        ensure_safe_delete(output, output.parent)
    output.mkdir(parents=True, exist_ok=True)

    mod_id, name, version, author, manifest, _manifest_path = convert_fuse_audio.package_info(source if source.is_dir() else source.parent)
    rail = convert_fuse_audio.fuse_skeleton(mod_id, name, version, author)
    messages: list[str] = []

    converted_any = False
    mixintos = manifest.get("mixintos") or manifest.get("Mixintos") or {}
    if isinstance(mixintos, dict) and mixintos:
        for key, value in mixintos.items():
            source_file = convert_fuse_audio.resolve_source_file(source if source.is_dir() else source.parent, value)
            if not source_file.exists():
                report.add("WARN", f"Missing mixinto file for {key}: {value}", source_file, "audio")
                continue
            lower = str(key).lower()
            if lower == "whistles":
                convert_fuse_audio.convert_whistles(source if source.is_dir() else source.parent, output, mod_id, source_file, rail, messages)
                converted_any = True
            elif lower == "horns":
                convert_fuse_audio.convert_horns(source if source.is_dir() else source.parent, output, mod_id, source_file, rail, messages)
                converted_any = True
            elif lower in ("bells", "hellsbells"):
                convert_fuse_audio.convert_bells(source if source.is_dir() else source.parent, output, mod_id, source_file, rail, messages)
                converted_any = True

    if not converted_any:
        json_files = [source] if is_json_file(source) else [path for path in iter_json_files(source) if path.name.lower() not in JSON_MANIFEST_NAMES]
        for json_file in json_files:
            audio_kind = detect_audio_json(json_file)
            if audio_kind == "whistles":
                convert_fuse_audio.convert_whistles(json_file.parent, output, mod_id, json_file, rail, messages)
                converted_any = True
            elif audio_kind == "horns":
                convert_fuse_audio.convert_horns(json_file.parent, output, mod_id, json_file, rail, messages)
                converted_any = True
            elif audio_kind == "bells":
                convert_fuse_audio.convert_bells(json_file.parent, output, mod_id, json_file, rail, messages)
                converted_any = True

    for message in messages:
        level = "WARN" if "[WARN]" in message else "INFO"
        report.add(level, message, concept="audio")

    if not converted_any or not any(rail["audio"][bucket] for bucket in ("whistles", "horns", "bells")):
        raise RuntimeError("no horn/whistle/bell entries found")

    write_json(output / "audio.fuse.json", rail)

    info = fuse_info(mod_id, f"{name} (FUSE Audio)", author, version)
    info["FuseDataFiles"] = ["audio.fuse.json"]
    asset_roots = find_asset_pack_sources(source if source.is_dir() else source.parent)
    if asset_roots:
        copy_asset_sources(source if source.is_dir() else source.parent, output, asset_roots)
        info["FuseAssetPacks"] = asset_roots
        report.count("assetPackSources", len(asset_roots))
    write_json(output / "Info.json", info)

    report.package_id = info["Id"]
    report.display_name = info["DisplayName"]
    report.generated_files.extend(["Info.json", "audio.fuse.json"])
    audio_counts = {}
    for bucket in ("whistles", "horns", "bells"):
        count = len(rail["audio"][bucket])
        report.count(f"audio.{bucket}", count)
        audio_counts[f"audio.{bucket}"] = count
    report.add_file_summary(source, "audio.fuse.json", audio_counts)


def choose_zip_root(extract_root: Path) -> Path:
    children = [child for child in extract_root.iterdir() if child.name != "__MACOSX"]
    dirs = [child for child in children if child.is_dir()]
    files = [child for child in children if child.is_file()]
    root_has_manifest = any(file.name.lower() in JSON_MANIFEST_NAMES for file in files)
    root_has_json = any(file.suffix.lower() == ".json" for file in files)
    if len(dirs) == 1 and not root_has_manifest and not root_has_json:
        child = dirs[0]
        if child.name.lower() == "mods":
            mod_children = [item for item in child.iterdir() if item.is_dir()]
            mod_files = [item for item in child.iterdir() if item.is_file()]
            mods_has_manifest = any(file.name.lower() in JSON_MANIFEST_NAMES for file in mod_files)
            mods_has_json = any(file.suffix.lower() == ".json" for file in mod_files)
            if len(mod_children) == 1 and not mods_has_manifest and not mods_has_json:
                return mod_children[0]
        return child
    return extract_root


def zip_internal_source(zip_path: Path, extract_root: Path, candidate: Path) -> Path:
    try:
        internal = candidate.relative_to(extract_root).as_posix()
    except ValueError:
        internal = candidate.name
    return Path(f"{zip_path}!{internal}")


def convert_input(input_path: Path, out_root: Path, kind: str, clean_output: bool) -> ConversionReport:
    original = input_path.resolve()
    if not original.exists():
        output = out_root / output_name_for(original)
        report = ConversionReport(original, output, "unknown", status="failed")
        report.add("ERROR", "Input path does not exist.", original)
        write_reports(report)
        return report

    with tempfile.TemporaryDirectory(prefix="fuse-convert-") as temp_dir:
        working_source = original
        explicit_output_name = None
        if original.is_file() and original.suffix.lower() == ".zip":
            extract_root = Path(temp_dir) / "zip"
            extract_root.mkdir(parents=True, exist_ok=True)
            with zipfile.ZipFile(original, "r") as archive:
                archive.extractall(extract_root)
            working_source = choose_zip_root(extract_root)
            explicit_output_name = working_source.name if working_source != extract_root else original.stem

        output = out_root / output_name_for(original, explicit_output_name)
        detected = detect_kind(working_source, kind)
        report = ConversionReport(original, output, detected)

        try:
            out_root.mkdir(parents=True, exist_ok=True)
            if detected == "route":
                convert_route(working_source, output, clean_output, report)
            elif detected == "audio":
                convert_audio(working_source, output, clean_output, report)
            elif detected == "asset":
                report.add(
                    "ERROR",
                    "Asset-pack-only package detected. FUSE loads supported asset packs directly; install this package with the FUSE installer instead of converting it.",
                    working_source,
                    "unsupported-asset-package",
                )
            elif detected == "map_tile":
                report.add(
                    "ERROR",
                    "Legacy map-tile package detected. FUSE's Alina compatibility loads supported tile data directly; install this package with the FUSE installer instead of converting it.",
                    working_source,
                    "unsupported-map-tile-package",
                )
            elif detected == "native":
                report.add(
                    "ERROR",
                    "This package already contains FUSE-native data. No conversion is needed; install the original package with the FUSE installer.",
                    working_source,
                    "unsupported-already-native",
                )
            elif detected == "code":
                report.add(
                    "ERROR",
                    "Compiled code mod detected. The converter only translates RailLoader JSON data and cannot reproduce DLL behavior; install a compatible code mod directly or ask its author for a FUSE-native version.",
                    working_source,
                    "unsupported-code-package",
                )
            else:
                raise RuntimeError("no convertible RailLoader JSON data detected; use the FUSE installer for code, asset, map-tile, or native packages")

            scan_legacy_warnings(working_source, report)
        except Exception as exc:
            report.add("ERROR", str(exc), working_source, detected)

        write_reports(report)
        return report


def is_batch_candidate(path: Path, kind: str) -> bool:
    name = path.name
    lower = name.lower()
    if name.startswith("."):
        return False
    if lower in BATCH_SKIP_DIR_NAMES:
        return False
    if path.is_dir() and (lower.endswith(".fuse") or lower.endswith(".rail")):
        return False
    if path.is_file() and path.suffix.lower() not in {".zip", ".json"}:
        return False
    if path.is_file() and path.name.lower() in JSON_MANIFEST_NAMES:
        return False
    detected = detect_direct_kind(path, kind)
    return detected != "unknown"


def is_within(path: Path, root: Path) -> bool:
    return path == root or root in path.parents


def should_skip_batch_path(path: Path, resolved_out_root: Path | None) -> bool:
    name = path.name
    lower = name.lower()
    if name.startswith("."):
        return True
    if path.is_dir() and (lower in BATCH_SKIP_DIR_NAMES or lower.endswith(".fuse") or lower.endswith(".rail")):
        return True
    if resolved_out_root is not None:
        try:
            if is_within(path.resolve(), resolved_out_root):
                return True
        except OSError:
            return True
    return False


def iter_batch_candidates(folder: Path, kind: str, out_root: Path | None = None) -> Iterable[Path]:
    resolved_out_root = out_root.resolve() if out_root else None

    def walk(current: Path) -> Iterable[Path]:
        try:
            children = sorted(current.iterdir(), key=lambda item: item.name.lower())
        except OSError:
            return
        for child in children:
            if should_skip_batch_path(child, resolved_out_root):
                continue
            if is_batch_candidate(child, kind):
                yield child
                continue
            if child.is_dir():
                yield from walk(child)

    yield from walk(folder)


def convert_batch_zip(zip_path: Path, out_root: Path, kind: str, clean_output: bool) -> list[ConversionReport]:
    with tempfile.TemporaryDirectory(prefix="fuse-batch-zip-") as temp_dir:
        extract_root = Path(temp_dir) / "zip"
        extract_root.mkdir(parents=True, exist_ok=True)
        with zipfile.ZipFile(zip_path, "r") as archive:
            archive.extractall(extract_root)

        if detect_direct_kind(extract_root, kind) != "unknown":
            return [convert_input(zip_path, out_root, kind, clean_output)]

        candidates = list(iter_batch_candidates(extract_root, kind, out_root=None))
        if len(candidates) <= 1:
            return [convert_input(zip_path, out_root, kind, clean_output)]

        reports: list[ConversionReport] = []
        for candidate in candidates:
            report = convert_input(candidate, out_root, kind, clean_output)
            report.source = zip_internal_source(zip_path, extract_root, candidate)
            report.add("INFO", f"Converted from archive '{zip_path}'.", zip_path, "batch-zip")
            write_reports(report)
            reports.append(report)
        return reports


def convert_batch_folder(folder: Path, out_root: Path, kind: str, clean_output: bool) -> list[ConversionReport]:
    if not folder.is_dir():
        report = ConversionReport(folder, out_root / output_name_for(folder), "unknown", status="failed")
        report.add("ERROR", "Batch input is not a folder.", folder, "batch")
        return [report]

    out_root.mkdir(parents=True, exist_ok=True)
    reports: list[ConversionReport] = []
    for candidate in iter_batch_candidates(folder, kind, out_root):
        if candidate.is_file() and candidate.suffix.lower() == ".zip":
            reports.extend(convert_batch_zip(candidate, out_root, kind, clean_output))
            continue
        reports.append(convert_input(candidate, out_root, kind, clean_output))
    return reports


def print_summary(report: ConversionReport) -> None:
    status = report.status.upper()
    print(f"{status}: {report.source}")
    print(f"  kind: {report.detected_kind}")
    print(f"  out:  {report.output}")
    if report.package_id:
        print(f"  id:   {report.package_id}")
    if report.counts:
        counts = ", ".join(f"{key}={value}" for key, value in sorted(report.counts.items()))
        print(f"  counts: {counts}")
    if report.warnings or report.errors:
        print(f"  warnings={report.warnings} errors={report.errors}")
    print(f"  report: {report_output_folder(report) / 'conversion-report.md'}")


def main() -> int:
    parser = argparse.ArgumentParser(description="Convert legacy Railroader mods to FUSE packages.")
    parser.add_argument("inputs", nargs="+", help="Legacy folder, zip file, or JSON file. Drag-and-drop works on Windows.")
    parser.add_argument("--out", default=None, help="Output root. Default is Railroader Mods if found, or FUSEConverted for --batch.")
    parser.add_argument("--kind", choices=("auto", "route", "audio"), default="auto", help="Force a JSON package type.")
    parser.add_argument("--clean", action="store_true", help="Replace existing .FUSE output folders under the output root.")
    parser.add_argument("--batch", action="store_true", help="Treat each input folder as a container and recursively convert every recognized child mod, zip, or JSON in it.")
    args = parser.parse_args()

    reports: list[ConversionReport] = []
    if args.batch:
        for item in args.inputs:
            folder = Path(item).resolve()
            out_root = Path(args.out).resolve() if args.out else (folder / "FUSEConverted").resolve()
            batch_reports = convert_batch_folder(folder, out_root, args.kind, args.clean)
            if not batch_reports:
                empty_report = ConversionReport(folder, out_root, "batch", status="converted")
                empty_report.add("WARN", "No convertible child folders, zip files, or JSON files were found.", folder, "batch")
                write_reports(empty_report)
                batch_reports = [empty_report]
            write_batch_reports(folder, out_root, batch_reports)
            reports.extend(batch_reports)
    else:
        out_root = Path(args.out).resolve() if args.out else default_output_root().resolve()
        reports = [convert_input(Path(item), out_root, args.kind, args.clean) for item in args.inputs]

    for report in reports:
        print_summary(report)

    failed = sum(1 for report in reports if report.status == "failed")
    converted = len(reports) - failed
    print(f"Converted {converted}/{len(reports)} input(s).")
    return 1 if failed else 0


if __name__ == "__main__":
    raise SystemExit(main())
