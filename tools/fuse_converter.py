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


TOOL_VERSION = "0.2.0"
DEFAULT_STEAM_MODS = Path(r"C:\Steam\steamapps\common\Railroader\Mods")
DEFAULT_MANAGER_VERSION = "0.27.10"
JSON_MANIFEST_NAMES = {"definition.json", "info.json"}
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
class ConversionReport:
    source: Path
    output: Path
    detected_kind: str
    status: str = "pending"
    package_id: str = ""
    display_name: str = ""
    generated_files: list[str] = field(default_factory=list)
    counts: dict[str, int] = field(default_factory=dict)
    entries: list[ReportEntry] = field(default_factory=list)

    def add(self, level: str, message: str, file: Path | str = "", concept: str = "") -> None:
        self.entries.append(ReportEntry(level=level, message=message, file=str(file), concept=concept))

    def count(self, key: str, amount: int) -> None:
        if amount:
            self.counts[key] = self.counts.get(key, 0) + int(amount)

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


def has_asset_pack_children(folder: Path) -> bool:
    try:
        return any(is_asset_pack_folder(child) for child in folder.iterdir() if child.is_dir())
    except OSError:
        return False


def find_asset_pack_sources(source: Path) -> list[str]:
    if not source.is_dir():
        return []

    sources: list[str] = []
    if is_asset_pack_folder(source):
        return ["."]

    sc_asset_packs = source / "SCAssetPacks"
    if has_asset_pack_children(sc_asset_packs):
        sources.append("SCAssetPacks")

    if has_asset_pack_children(source):
        sources.append(".")

    try:
        for child in sorted(source.iterdir(), key=lambda item: item.name.lower()):
            if not child.is_dir() or child.name.lower() == "scassetpacks":
                continue
            if has_asset_pack_children(child):
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

    if detects_audio(source) and not detects_route_data(source):
        return "audio"
    if detects_route_data(source):
        return "route"
    if find_map_tile_sources(source):
        return "route"
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
    write_json(report.output / "conversion-report.json", report.to_dict())

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

    lines.extend(["", "## Generated Files", "", "| File |", "| --- |"])
    for file in report.generated_files:
        lines.append(f"| `{file}` |")
    if not report.generated_files:
        lines.append("| _(none)_ |")

    lines.extend(["", "## Messages", "", "| Level | Concept | File | Message |", "| --- | --- | --- | --- |"])
    for entry in report.entries:
        lines.append(f"| {entry.level} | `{entry.concept}` | `{entry.file}` | {entry.message} |")
    if not report.entries:
        lines.append("| OK |  |  | No warnings or errors. |")
    write_text(report.output / "conversion-report.md", "\n".join(lines) + "\n")


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
        "WARN",
        f"Duplicate legacy {runtime_kind} id '{object_id}' also appeared in '{first_file.name}'. "
        "FUSE keeps one output file per source file; differing duplicates are renamed during conversion when safe.",
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
            if normalized == component_type:
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
    for key, count in fuse_convert.count_content(rail).items():
        report.count(key, count)

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

    for _source_name, output_name, counts in summaries:
        report.generated_files.append(output_name)
        for key, count in counts.items():
            report.count(key, count)

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
        report.count("mapTileFiles", sum(1 for tile in tile_folder.iterdir() if tile.is_file() and tile.suffix.lower() == ".data"))

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
            if is_asset_pack_folder(root_path):
                report.count("assetPacks", 1)
            else:
                report.count("assetPacks", sum(1 for child in root_path.iterdir() if child.is_dir() and is_asset_pack_folder(child)))


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
    for bucket in ("whistles", "horns", "bells"):
        report.count(f"audio.{bucket}", len(rail["audio"][bucket]))


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


def convert_input(input_path: Path, out_root: Path, kind: str, clean_output: bool) -> ConversionReport:
    original = input_path.resolve()
    if not original.exists():
        output = out_root / output_name_for(original)
        report = ConversionReport(original, output, "unknown", status="failed")
        report.add("ERROR", "Input path does not exist.", original)
        output.mkdir(parents=True, exist_ok=True)
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
                convert_asset(working_source, output, clean_output, report)
            else:
                raise RuntimeError("could not detect package type; use --kind route, --kind audio, or --kind asset")

            scan_legacy_warnings(working_source, report)
        except Exception as exc:
            output.mkdir(parents=True, exist_ok=True)
            report.add("ERROR", str(exc), working_source, detected)

        write_reports(report)
        return report


def is_batch_candidate(path: Path, kind: str) -> bool:
    name = path.name
    lower = name.lower()
    if name.startswith("."):
        return False
    if lower in {"fuseconverted", "converted", "dist", "__pycache__"}:
        return False
    if path.is_dir() and (lower.endswith(".fuse") or lower.endswith(".rail")):
        return False
    if path.is_file() and path.suffix.lower() not in {".zip", ".json"}:
        return False
    if path.is_file() and path.name.lower() in JSON_MANIFEST_NAMES:
        return False
    if kind != "auto":
        return path.is_dir() or path.suffix.lower() in {".zip", ".json"}
    if path.is_file() and path.suffix.lower() == ".zip":
        return True
    detected = detect_kind(path, "auto")
    return detected != "unknown"


def iter_batch_candidates(folder: Path, kind: str) -> Iterable[Path]:
    for child in sorted(folder.iterdir(), key=lambda item: item.name.lower()):
        if is_batch_candidate(child, kind):
            yield child


def convert_batch_folder(folder: Path, out_root: Path, kind: str, clean_output: bool) -> list[ConversionReport]:
    if not folder.is_dir():
        report = ConversionReport(folder, out_root / output_name_for(folder), "unknown", status="failed")
        report.add("ERROR", "Batch input is not a folder.", folder, "batch")
        return [report]

    out_root.mkdir(parents=True, exist_ok=True)
    reports: list[ConversionReport] = []
    for candidate in iter_batch_candidates(folder, kind):
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
    print(f"  report: {report.output / 'conversion-report.md'}")


def main() -> int:
    parser = argparse.ArgumentParser(description="Convert legacy Railroader mods to FUSE packages.")
    parser.add_argument("inputs", nargs="+", help="Legacy folder, zip file, or JSON file. Drag-and-drop works on Windows.")
    parser.add_argument("--out", default=None, help="Output root. Default is Railroader Mods if found, or FUSEConverted for --batch.")
    parser.add_argument("--kind", choices=("auto", "route", "audio", "asset"), default="auto", help="Force a package type.")
    parser.add_argument("--clean", action="store_true", help="Replace existing .FUSE output folders under the output root.")
    parser.add_argument("--batch", action="store_true", help="Treat each input folder as a container and convert every child mod/zip/json in it.")
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
