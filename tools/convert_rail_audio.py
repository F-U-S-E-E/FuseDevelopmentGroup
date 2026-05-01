#!/usr/bin/env python3
"""
convert_rail_audio.py - convert loose horn/whistle/bell packs into RAIL audio packages.

This is a clean-room importer: it reads legacy JSON documents as input data only
and writes Rail-native package files. It does not import legacy code.
"""

import argparse
import json
import re
import shutil
import sys
from pathlib import Path


RAIL_SCHEMA_VERSION = "1.0"
AUDIO_EXTENSIONS = {".wav", ".mp3", ".ogg", ".aiff", ".aif"}


def read_json(path: Path, lenient: bool = False):
    text = path.read_text(encoding="utf-8-sig")
    if lenient:
        text = re.sub(r",\s*([}\]])", r"\1", text)
    return json.loads(text)


def write_json(path: Path, data):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(data, indent=2, ensure_ascii=False), encoding="utf-8")


def file_ref(value: str) -> str:
    if not isinstance(value, str):
        return ""
    text = value.strip()
    if text.lower().startswith("file(") and text.endswith(")"):
        text = text[5:-1]
    return text.strip().strip("\"'")


def slug(value: str, fallback: str) -> str:
    text = re.sub(r"[^A-Za-z0-9]+", "-", value or "").strip("-").lower()
    return text or fallback


def package_info(source: Path):
    for name in ("Definition.json", "definition.json", "Info.json", "info.json"):
        path = source / name
        if not path.exists():
            continue
        try:
            data = read_json(path, lenient=True)
        except Exception:
            continue
        mod_id = data.get("id") or data.get("Id") or slug(source.name, "rail-audio")
        name = data.get("name") or data.get("DisplayName") or source.name
        version = data.get("version") or data.get("Version") or "1.0.0"
        author = data.get("author") or data.get("Author") or "Unknown"
        return mod_id, name, version, author, data, path
    return slug(source.name, "rail-audio"), source.name, "1.0.0", "Unknown", {}, None


def resolve_source_file(source_root: Path, spec: str) -> Path:
    ref = file_ref(spec)
    if not ref:
        return Path()
    path = Path(ref)
    if not path.is_absolute():
        path = source_root / path
    return path.resolve()


def copy_audio(source_root: Path, output_root: Path, spec: str, kind: str, report: list) -> str:
    source_file = resolve_source_file(source_root, spec)
    if not source_file.exists():
        report.append(f"[WARN] missing audio file: {spec}")
        return file_ref(spec)

    if source_file.suffix.lower() not in AUDIO_EXTENSIONS:
        report.append(f"[WARN] unsupported audio extension: {source_file}")

    dest = output_root / "Audio" / kind / source_file.name
    dest.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(source_file, dest)
    return str(dest.relative_to(output_root)).replace("\\", "/")


def rail_skeleton(mod_id: str, mod_name: str, version: str, author: str):
    return {
        "$schema": ".\\schemas\\rail-mod.schema.json",
        "schemaVersion": RAIL_SCHEMA_VERSION,
        "id": f"{mod_id}.RAIL.Audio",
        "name": f"{mod_name} (RAIL Audio)",
        "author": author,
        "modVersion": str(version),
        "coordinateSpace": "world",
        "tracks": {
            "nodes": {},
            "segments": {},
            "spans": {},
            "areas": {},
            "removals": {"nodes": [], "segments": [], "spans": []},
        },
        "operations": {
            "loads": {},
            "industries": {},
            "loaders": {},
            "turntables": {},
            "stations": {},
        },
        "world": {
            "scenery": {},
            "spawnPoints": [],
            "splineys": {},
            "telegraphPoles": {},
            "telegraphPoleMovements": [],
            "mapLabels": {},
            "mapMasks": {},
            "mapTiles": {},
            "sceneClones": {},
            "suppressBaseScenePaths": [],
            "suppressBaseTrackGroups": [],
            "suppressBaseAreas": [],
            "removals": {
                "scenery": [],
                "splineys": [],
                "telegraphPoles": [],
                "mapLabels": [],
                "mapMasks": [],
                "sceneClones": [],
            },
        },
        "audio": {
            "whistles": {},
            "horns": {},
            "bells": {},
        },
        "progression": {
            "progressions": {},
            "sections": [],
            "mapFeatures": {},
        },
        "extensions": {},
    }


def convert_whistles(source_root: Path, output_root: Path, mod_id: str, path: Path, target: dict, report: list):
    entries = read_json(path, lenient=True)
    for index, item in enumerate(entries if isinstance(entries, list) else []):
        name = item.get("name") or f"Whistle {index + 1}"
        entry_id = f"{mod_id}.whistle.{slug(name, str(index + 1))}"
        model = item.get("model") if isinstance(item.get("model"), dict) else None
        target["audio"]["whistles"][entry_id] = {
            "name": name,
            "clip": copy_audio(source_root, output_root, item.get("clip"), "whistles", report),
        }
        if model:
            target["audio"]["whistles"][entry_id]["model"] = {
                "assetPackIdentifier": model.get("assetPackIdentifier") or "",
                "assetIdentifier": model.get("assetIdentifier") or "",
            }


def convert_horns(source_root: Path, output_root: Path, mod_id: str, path: Path, target: dict, report: list):
    entries = read_json(path, lenient=True)
    for index, item in enumerate(entries if isinstance(entries, list) else []):
        name = item.get("name") or f"Horn {index + 1}"
        entry_id = f"{mod_id}.horn.{slug(name, str(index + 1))}"
        layers = []
        for layer in item.get("layers") or []:
            layers.append({
                "file": copy_audio(source_root, output_root, layer.get("file"), "horns", report),
                "keyframes": [
                    {
                        "t": float(keyframe.get("t", 0)),
                        "value": float(keyframe.get("value", 0)),
                    }
                    for keyframe in (layer.get("keyframes") or [])
                    if isinstance(keyframe, dict)
                ],
            })
        target["audio"]["horns"][entry_id] = {
            "name": name,
            "layers": layers,
        }


def convert_bells(source_root: Path, output_root: Path, mod_id: str, path: Path, target: dict, report: list):
    entries = read_json(path, lenient=True)
    for index, item in enumerate(entries if isinstance(entries, list) else []):
        name = item.get("name") or f"Bell {index + 1}"
        entry_id = f"{mod_id}.bell.{slug(name, str(index + 1))}"
        target["audio"]["bells"][entry_id] = {
            "name": name,
            "file": copy_audio(source_root, output_root, item.get("file"), "bells", report),
            "indexTimes": [float(value) for value in (item.get("indexTimes") or [])],
        }


def has_asset_pack_children(source: Path) -> bool:
    for child in source.iterdir():
        if not child.is_dir():
            continue
        if (child / "Bundle").exists() and (child / "Catalog.json").exists() and (child / "Definitions.json").exists():
            return True
    return False


def copy_asset_pack_wrapper(source: Path, mods_out: Path, clean: bool):
    mod_id, name, version, author, _meta, _path = package_info(source)
    output = mods_out / f"{source.name}.RAIL"
    if clean and output.exists():
        ensure_under(output, mods_out)
        shutil.rmtree(output)
    shutil.copytree(source, output, dirs_exist_ok=True, ignore=shutil.ignore_patterns("Info.json"))
    info = {
        "$schema": ".\\schemas\\umm-info.schema.json",
        "Id": f"{mod_id}.RAIL",
        "DisplayName": f"{name} (RAIL)",
        "Author": author,
        "Version": str(version),
        "ManagerVersion": "0.27.10",
        "Requirements": ["RAIL"],
        "LoadAfter": ["RAIL"],
        "RailAssetPacks": ["."],
    }
    write_json(output / "Info.json", info)
    return output, "[OK] wrapped asset-pack audio"


def convert_loose_package(source: Path, mods_out: Path, clean: bool):
    mod_id, name, version, author, meta, _meta_path = package_info(source)
    mixintos = meta.get("mixintos") or meta.get("Mixintos") or {}
    output = mods_out / f"{source.name}.RAIL"
    if clean and output.exists():
        ensure_under(output, mods_out)
        shutil.rmtree(output)
    output.mkdir(parents=True, exist_ok=True)

    report = []
    rail = rail_skeleton(mod_id, name, version, author)
    for key, value in mixintos.items():
        source_file = resolve_source_file(source, value)
        if not source_file.exists():
            report.append(f"[WARN] missing mixinto file for {key}: {value}")
            continue
        lower = str(key).lower()
        if lower == "whistles":
            convert_whistles(source, output, mod_id, source_file, rail, report)
        elif lower == "horns":
            convert_horns(source, output, mod_id, source_file, rail, report)
        elif lower in ("bells", "hellsbells"):
            convert_bells(source, output, mod_id, source_file, rail, report)

    if not any(rail["audio"][bucket] for bucket in ("whistles", "horns", "bells")):
        return None, "[SKIP] no horn/whistle/bell entries"

    write_json(output / "audio.rail.json", rail)
    info = {
        "$schema": ".\\schemas\\umm-info.schema.json",
        "Id": f"{mod_id}.RAIL",
        "DisplayName": f"{name} (RAIL Audio)",
        "Author": author,
        "Version": str(version),
        "ManagerVersion": "0.27.10",
        "Requirements": ["RAIL"],
        "LoadAfter": ["RAIL"],
        "RailDataFiles": ["audio.rail.json"],
    }
    write_json(output / "Info.json", info)
    counts = " ".join(f"{key}={len(rail['audio'][key])}" for key in ("whistles", "horns", "bells"))
    suffix = "" if not report else " " + " ".join(report)
    return output, f"[OK] converted loose audio {counts}.{suffix}"


def ensure_under(path: Path, root: Path):
    path = path.resolve()
    root = root.resolve()
    if root != path and root not in path.parents:
        raise RuntimeError(f"refusing to delete outside output root: {path}")


def find_candidates(source_root: Path):
    for source in sorted([item for item in source_root.iterdir() if item.is_dir()], key=lambda p: p.name.lower()):
        _mod_id, _name, _version, _author, meta, _path = package_info(source)
        mixintos = meta.get("mixintos") or meta.get("Mixintos") or {}
        if any(str(key).lower() in ("whistles", "horns", "bells", "hellsbells") for key in mixintos):
            yield source
            continue
        if has_asset_pack_children(source) and re.search(r"whistle|horn|bell", source.name, re.IGNORECASE):
            yield source


def main():
    parser = argparse.ArgumentParser(description="Convert horn/whistle/bell packs to RAIL.")
    parser.add_argument("--source", default=r"C:\Steam\steamapps\common\Railroader\Mods.bck")
    parser.add_argument("--out", default=r"C:\Steam\steamapps\common\Railroader\Mods")
    parser.add_argument("--clean", action="store_true", help="Replace existing converted output folders.")
    args = parser.parse_args()

    source_root = Path(args.source)
    mods_out = Path(args.out)
    if not source_root.is_dir():
        print(f"source folder not found: {source_root}", file=sys.stderr)
        return 1
    mods_out.mkdir(parents=True, exist_ok=True)

    converted = 0
    for source in find_candidates(source_root):
        try:
            if has_asset_pack_children(source):
                output, message = copy_asset_pack_wrapper(source, mods_out, args.clean)
            else:
                output, message = convert_loose_package(source, mods_out, args.clean)
            print(f"{source.name}: {message}")
            if output is not None:
                print(f"  -> {output}")
                converted += 1
        except Exception as exc:
            print(f"{source.name}: [ERROR] {exc}", file=sys.stderr)

    print(f"Converted/wrapped {converted} audio package(s).")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
