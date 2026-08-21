#!/usr/bin/env python3
"""
fuse_installer.py - drag-and-drop zip installer for FUSE and UMM packages.

The installer reads zip structure and manifest JSON only. It never imports or
executes package code.
"""

from __future__ import annotations

import argparse
import contextlib
import io
import json
import os
import re
import shutil
import sys
import zipfile
from dataclasses import dataclass, field
from datetime import datetime
from pathlib import Path
from typing import Any, Iterable


SCRIPT_DIR = Path(__file__).resolve().parent
if str(SCRIPT_DIR) not in sys.path:
    sys.path.insert(0, str(SCRIPT_DIR))

import legacy_json  # noqa: E402


TOOL_VERSION = "0.7.0"
BUNDLED_FUSE_NAME = "bundled_fuse.zip"
MANIFEST_NAMES = {"info.json", "definition.json"}
LEGACY_MANAGED_FILES = (
    "Railloader.dll",
    "Railloader.Injector.dll",
    "Railloader.Interchange.dll",
    "StrangeCustoms.dll",
)
ASSETLOADER_ID = "AssetLoader"
ASSETLOADER_DLL = "AssetLoader.dll"
ASSETLOADER_COMPATIBILITY_MARKER = "FUSE.AssetLoaderCompatibility"
ASSETLOADER_COMPATIBILITY_MANIFEST = {
    "Id": ASSETLOADER_ID,
    "DisplayName": "AssetLoader Compatibility (provided by FUSE)",
    "Author": "FUSE",
    "Version": "1.0.1",
    "ManagerVersion": "0.27.10",
    "Requirements": ["FUSE"],
    "LoadAfter": ["FUSE"],
    "ContentType": "Compatibility",
    "FuseProvidedCompatibility": ASSETLOADER_COMPATIBILITY_MARKER,
}
# UMM reflects a code mod's assembly before calling its entry point. If that
# assembly references a loader contract FUSE replaces, FUSE must already be
# loaded so its AssemblyResolve bridge can provide the compatibility types.
# Detect the actual metadata reference in the DLL instead of maintaining a
# brittle list of third-party package names.
FUSE_LEGACY_STARTUP_ASSEMBLY_REFERENCES = (
    b"Railloader.Interchange",
    b"Railloader.Injector",
    b"StrangeCustoms",
)
LEGACY_DATA_KEYS = {
    "tracks",
    "loads",
    "areas",
    "industries",
    "turntables",
    "scenery",
    "splineys",
    "mandelas",
    "texts",
    "progression",
    "progressions",
    "mapFeatures",
    "simpleGraphs",
    "spawnPoint",
}
MAX_JSON_BYTES = 64 * 1024 * 1024
WINDOWS_RESERVED_NAMES = {
    "con",
    "prn",
    "aux",
    "nul",
    *(f"com{index}" for index in range(1, 10)),
    *(f"lpt{index}" for index in range(1, 10)),
}

# Keep this explicit and narrow, matching FuseReplacementCapabilityCatalog.
# These are compatibility contracts FUSE actually provides; unrelated Zamu/Joo
# package ids must still be installed normally.
FUSE_REPLACEMENT_IDS = {
    "fuse", "railroader", "railloader", "rail-loader",
    "railloader.injector", "railloader.interchange", "assetloader",
    "alinanova21.alinasmapmod", "alinasmapmod", "alinamapmod",
    "alinanova21.mapeditor", "mapeditor", "mmapeditor",
    "zamu.confusingsupplements", "zamu.foryourconvenience",
    "zamu.strangecustoms", "strangecustoms", "confusingsupplements",
    "foryourconvenience",
}


@dataclass(frozen=True)
class PackageRequirement:
    package_id: str
    not_before: str = ""
    not_after: str = ""


@dataclass
class ZipPackage:
    zip_path: Path
    root: tuple[str, ...]
    kind: str
    package_id: str
    display_name: str
    version: str
    install_name: str
    manifest_member: str
    member_count: int
    notes: list[str] = field(default_factory=list)
    errors: list[str] = field(default_factory=list)
    requirements: list[PackageRequirement] = field(default_factory=list)

    @property
    def root_label(self) -> str:
        return "/".join(self.root) if self.root else "."


@dataclass
class InstallResult:
    package: ZipPackage
    status: str
    destination: Path
    message: str = ""
    backup: Path | None = None


@dataclass
class CompatibilityAction:
    component: str
    status: str
    message: str
    source: Path | None = None
    destination: Path | None = None


def package_id_aliases(package_id: str) -> tuple[str, ...]:
    normalized = (package_id or "").strip().lower()
    if not normalized:
        return ()
    aliases = [normalized]
    if normalized.endswith(".fuse") or normalized.endswith(".rail"):
        aliases.append(normalized[:-5])
    return tuple(dict.fromkeys(aliases))


def replacement_id_key(package_id: str) -> str:
    normalized = (package_id or "").strip().lower()
    while normalized.endswith(".fuse") or normalized.endswith(".rail"):
        normalized = normalized[:-5]
    return "railloader" if normalized == "rail-loader" else normalized


def normalize_zip_parts(name: str) -> tuple[str, ...] | None:
    clean = (name or "").replace("\\", "/")
    if not clean or clean.endswith("/"):
        return None
    if clean.startswith("/") or re.match(r"^[A-Za-z]:", clean):
        return None

    parts: list[str] = []
    for part in clean.split("/"):
        if part in {"", "."}:
            continue
        if part == "..":
            return None
        if re.search(r'[<>:"|?*\x00-\x1f]', part):
            return None
        parts.append(part)

    return tuple(parts) if parts else None


def starts_with(parts: tuple[str, ...], prefix: tuple[str, ...]) -> bool:
    return len(parts) >= len(prefix) and parts[: len(prefix)] == prefix


def relative_name(parts: tuple[str, ...], prefix: tuple[str, ...]) -> str:
    return "/".join(parts[len(prefix) :])


def path_key(parts: tuple[str, ...]) -> tuple[str, ...]:
    return tuple(part.lower() for part in parts)


def zip_file_parts(archive: zipfile.ZipFile) -> dict[zipfile.ZipInfo, tuple[str, ...]]:
    result: dict[zipfile.ZipInfo, tuple[str, ...]] = {}
    for info in archive.infolist():
        if info.is_dir():
            continue
        parts = normalize_zip_parts(info.filename)
        if parts is None:
            continue
        if parts and parts[0] == "__MACOSX":
            continue
        result[info] = parts
    return result


def find_member(
    files: dict[zipfile.ZipInfo, tuple[str, ...]],
    root: tuple[str, ...],
    file_name: str,
) -> zipfile.ZipInfo | None:
    file_parts = normalize_zip_parts(file_name)
    if file_parts is None:
        return None
    expected = path_key(root + file_parts)
    for info, parts in files.items():
        if path_key(parts) == expected:
            return info
    return None


def candidate_roots(files: dict[zipfile.ZipInfo, tuple[str, ...]]) -> list[tuple[str, ...]]:
    roots: list[tuple[str, ...]] = []
    seen: set[tuple[str, ...]] = set()
    for _info, parts in files.items():
        if parts[-1].lower() not in MANIFEST_NAMES:
            continue
        root = parts[:-1]
        key = path_key(root)
        if key not in seen:
            seen.add(key)
            roots.append(root)

    roots.sort(key=lambda item: (len(item), path_key(item)))
    if any(len(root) == 0 for root in roots):
        return [()]

    selected: list[tuple[str, ...]] = []
    for root in roots:
        if any(starts_with(root, existing) for existing in selected):
            continue
        selected.append(root)
    return selected


def archive_layout_errors(
    archive: zipfile.ZipFile,
    files: dict[zipfile.ZipInfo, tuple[str, ...]],
) -> list[str]:
    errors: list[str] = []
    unsafe_members = [
        info.filename
        for info in archive.infolist()
        if not info.is_dir()
        and not info.filename.replace("\\", "/").startswith("__MACOSX/")
        and normalize_zip_parts(info.filename) is None
    ]
    if unsafe_members:
        preview = ", ".join(repr(name) for name in unsafe_members[:5])
        suffix = f" (+{len(unsafe_members) - 5} more)" if len(unsafe_members) > 5 else ""
        errors.append(
            "Archive contains unsafe member path(s) that cannot be installed: "
            f"{preview}{suffix}. Rebuild the ZIP without absolute paths, '..', drive prefixes, or invalid Windows characters."
        )

    by_path: dict[tuple[str, ...], list[str]] = {}
    for info, parts in files.items():
        by_path.setdefault(path_key(parts), []).append(info.filename)
    duplicate_paths = [names for names in by_path.values() if len(names) > 1]
    if duplicate_paths:
        preview = "; ".join(" / ".join(names) for names in duplicate_paths[:5])
        suffix = f" (+{len(duplicate_paths) - 5} more)" if len(duplicate_paths) > 5 else ""
        errors.append(
            "Archive contains duplicate or case-colliding member path(s): "
            f"{preview}{suffix}. Keep one file for each destination path."
        )

    manifest_roots = sorted(
        {
            path_key(parts[:-1]): parts[:-1]
            for parts in files.values()
            if parts[-1].lower() in MANIFEST_NAMES
        }.values(),
        key=lambda item: (len(item), path_key(item)),
    )
    nested_pairs: list[tuple[tuple[str, ...], tuple[str, ...]]] = []
    for index, parent in enumerate(manifest_roots):
        for child in manifest_roots[index + 1:]:
            if len(child) > len(parent) and starts_with(path_key(child), path_key(parent)):
                nested_pairs.append((parent, child))
    if nested_pairs:
        preview = "; ".join(
            f"{'/'.join(parent) or '.'} contains {'/'.join(child)}"
            for parent, child in nested_pairs[:5]
        )
        suffix = f" (+{len(nested_pairs) - 5} more)" if len(nested_pairs) > 5 else ""
        errors.append(
            "Archive layout is ambiguous because one package manifest contains another package manifest: "
            f"{preview}{suffix}. Put sibling packages under separate folders such as Mods/PackageA and Mods/PackageB."
        )
    return errors


def read_zip_text(archive: zipfile.ZipFile, info: zipfile.ZipInfo) -> str:
    if info.file_size > MAX_JSON_BYTES:
        raise ValueError(f"JSON file is too large to inspect safely: {info.filename}")
    data = archive.read(info)
    try:
        return data.decode("utf-8-sig")
    except UnicodeDecodeError:
        return data.decode("utf-8-sig", errors="replace")


def read_zip_json(archive: zipfile.ZipFile, info: zipfile.ZipInfo) -> Any:
    # Comments and trailing commas remain tolerated for old RailLoader files,
    # but the installer must not silently invent missing closing braces. A mod
    # author needs the real file/line failure before the package reaches game.
    return legacy_json.loads(read_zip_text(archive, info), repair=False)


def describe_json_error(member_name: str, exc: Exception) -> str:
    line = getattr(exc, "lineno", 0)
    column = getattr(exc, "colno", 0)
    location = f" line {line}, column {column}" if line else ""
    return f"{member_name}{location}: {exc}"


def string_field(data: dict[str, Any] | None, *names: str) -> str:
    if not isinstance(data, dict):
        return ""
    for name in names:
        value = data.get(name)
        if value is None:
            continue
        text = str(value).strip()
        if text:
            return text
    return ""


def has_fuse_reference(value: Any) -> bool:
    if isinstance(value, str):
        return value.strip().lower() == "fuse"
    if isinstance(value, list):
        return any(has_fuse_reference(item) for item in value)
    if isinstance(value, dict):
        return string_field(value, "Id", "id").lower() == "fuse"
    return False


def has_fuse_data_marker(info: dict[str, Any]) -> bool:
    if not isinstance(info, dict):
        return False
    return (
        bool(info.get("FuseDataFile"))
        or bool(info.get("FuseDataFiles"))
        or bool(info.get("FuseAssetPacks"))
        or has_fuse_reference(info.get("Requirements"))
        or has_fuse_reference(info.get("LoadAfter"))
    )


def looks_like_legacy_data(archive: zipfile.ZipFile, files: dict[zipfile.ZipInfo, tuple[str, ...]], root: tuple[str, ...]) -> bool:
    for info, parts in files.items():
        if not starts_with(parts, root):
            continue
        if not parts[-1].lower().endswith(".json"):
            continue
        if parts[-1].lower() in MANIFEST_NAMES or parts[-1].lower().endswith(".bak"):
            continue
        if "signal" in parts[-1].lower():
            continue
        try:
            data = read_zip_json(archive, info)
        except Exception:
            continue
        if isinstance(data, dict) and any(key in data for key in LEGACY_DATA_KEYS):
            return True
    return False


def is_railloader_manifest(data: dict[str, Any]) -> bool:
    """Recognize both data-only and code-only RailLoader packages.

    Code-only packages commonly contain only Definition.json, an ``assemblies``
    list, and DLLs. Requiring a data JSON file made the installer reject exactly
    the hosted compatibility packages FUSE is intended to run.
    """
    if not isinstance(data, dict):
        return False
    package_id = string_field(data, "id", "Id")
    if not package_id:
        return False
    return any(
        key in data
        for key in (
            "manifestVersion",
            "ManifestVersion",
            "assemblies",
            "Assemblies",
            "mixintos",
            "Mixintos",
            "requires",
            "Requires",
        )
    )


def parse_requirements(*values: Any) -> list[PackageRequirement]:
    result: list[PackageRequirement] = []
    seen: set[tuple[str, str, str]] = set()
    for value in values:
        entries = value if isinstance(value, list) else [value] if value else []
        for entry in entries:
            if isinstance(entry, str):
                requirement = PackageRequirement(entry.strip())
            elif isinstance(entry, dict):
                requirement = PackageRequirement(
                    string_field(entry, "Id", "id"),
                    string_field(entry, "NotBefore", "notBefore", "MinimumVersion", "minimumVersion"),
                    string_field(entry, "NotAfter", "notAfter", "MaximumVersion", "maximumVersion"),
                )
            else:
                continue
            if not requirement.package_id:
                continue
            key = (
                requirement.package_id.lower(),
                requirement.not_before,
                requirement.not_after,
            )
            if key not in seen:
                seen.add(key)
                result.append(requirement)
    return result


def ensure_fuse_id(value: str) -> str:
    text = (value or "").strip() or "LegacyDataPackage"
    return text if text.lower().endswith(".fuse") else f"{text}.FUSE"


def safe_folder_name(value: str, fallback: str = "InstalledPackage") -> str:
    text = re.sub(r'[<>:"/\\|?*\x00-\x1f]+', "-", value or "").strip(" .-_")
    text = re.sub(r"\s+", " ", text).strip()
    if not text:
        text = fallback
    stem = text.split(".", 1)[0].lower()
    if stem in WINDOWS_RESERVED_NAMES:
        text = f"{text}-package"
    return text[:120].rstrip(" .") or fallback


def package_from_root(
    zip_path: Path,
    archive: zipfile.ZipFile,
    files: dict[zipfile.ZipInfo, tuple[str, ...]],
    root: tuple[str, ...],
) -> ZipPackage | None:
    info_member = find_member(files, root, "Info.json")
    definition_member = find_member(files, root, "Definition.json")
    info_data: dict[str, Any] = {}
    definition_data: dict[str, Any] = {}
    notes: list[str] = []
    errors: list[str] = []

    if info_member is not None:
        try:
            loaded = read_zip_json(archive, info_member)
            if isinstance(loaded, dict):
                info_data = loaded
            else:
                notes.append("Info.json was not an object.")
        except Exception as exc:
            errors.append("Info.json could not be parsed: " + describe_json_error(info_member.filename, exc))

    if definition_member is not None:
        try:
            loaded = read_zip_json(archive, definition_member)
            if isinstance(loaded, dict):
                definition_data = loaded
            else:
                notes.append("Definition.json was not an object.")
        except Exception as exc:
            errors.append("Definition.json could not be parsed: " + describe_json_error(definition_member.filename, exc))

    source_folder = root[-1] if root and root[-1].lower() != "mods" else zip_path.stem
    member_count = sum(1 for parts in files.values() if starts_with(parts, root))

    if info_member is not None:
        package_id = string_field(info_data, "Id", "id") or safe_folder_name(source_folder)
        display_name = string_field(info_data, "DisplayName", "Name", "name") or package_id
        version = string_field(info_data, "Version", "version")
        kind = "invalid" if errors else ("fuse-data" if has_fuse_data_marker(info_data) else "umm")
        if not errors and kind == "fuse-data":
            errors.extend(validate_fuse_data_members(archive, files, root, info_data))
            if errors:
                kind = "invalid"
        install_name = safe_folder_name(package_id, safe_folder_name(source_folder))
        return ZipPackage(
            zip_path=zip_path,
            root=root,
            kind=kind,
            package_id=package_id,
            display_name=display_name,
            version=version,
            install_name=install_name,
            manifest_member=info_member.filename,
            member_count=member_count,
            notes=notes,
            errors=errors,
            requirements=parse_requirements(
                info_data.get("Requirements"),
                info_data.get("requirements"),
                info_data.get("FuseRequires"),
            ),
        )

    if definition_member is not None and (
        errors or
        is_railloader_manifest(definition_data) or
        looks_like_legacy_data(archive, files, root)
    ):
        legacy_id = string_field(definition_data, "id", "Id") or safe_folder_name(source_folder)
        display_name = string_field(definition_data, "name", "DisplayName", "Name") or legacy_id
        version = string_field(definition_data, "version", "Version")
        has_legacy_data = looks_like_legacy_data(archive, files, root)
        if has_legacy_data:
            notes.append("RailLoader data will be converted in memory and applied by FUSE.")
        if definition_data.get("assemblies") or definition_data.get("Assemblies"):
            notes.append("RailLoader plugin assemblies will be hosted by FUSE compatibility.")
        if not errors:
            errors.extend(validate_railloader_data_members(archive, files, root))
        return ZipPackage(
            zip_path=zip_path,
            root=root,
            kind="invalid" if errors else "railloader",
            package_id=legacy_id,
            display_name=display_name,
            version=version,
            install_name=safe_folder_name(legacy_id, safe_folder_name(source_folder)),
            manifest_member=definition_member.filename,
            member_count=member_count,
            notes=notes,
            errors=errors,
            requirements=parse_requirements(
                definition_data.get("requires"),
                definition_data.get("Requires"),
            ),
        )

    return None


def data_file_names(info: dict[str, Any]) -> tuple[list[str], list[str]]:
    value = info.get("FuseDataFiles", info.get("FuseDataFile"))
    if value is None:
        return [], []
    values = [value] if isinstance(value, str) else value if isinstance(value, list) else []
    errors: list[str] = []
    if not values:
        errors.append("Info.json FuseDataFile/FuseDataFiles must be a file name or a non-empty list of file names.")
        return [], errors
    names: list[str] = []
    for index, item in enumerate(values):
        if not isinstance(item, str) or not item.strip():
            errors.append(f"Info.json FuseDataFiles[{index}] must be a non-empty string.")
            continue
        parts = normalize_zip_parts(item.strip())
        if parts is None:
            errors.append(f"Info.json FuseDataFiles[{index}] contains an unsafe path: {item!r}.")
            continue
        names.append("/".join(parts))
    return names, errors


def validate_json_member(archive: zipfile.ZipFile, member: zipfile.ZipInfo) -> list[str]:
    try:
        value = read_zip_json(archive, member)
    except Exception as exc:
        return ["JSON could not be parsed: " + describe_json_error(member.filename, exc)]
    if not isinstance(value, dict):
        return [f"{member.filename}: FUSE/RailLoader data JSON must contain an object at the root."]
    return []


def validate_fuse_data_members(
    archive: zipfile.ZipFile,
    files: dict[zipfile.ZipInfo, tuple[str, ...]],
    root: tuple[str, ...],
    info: dict[str, Any],
) -> list[str]:
    names, errors = data_file_names(info)
    members: list[zipfile.ZipInfo] = []
    if names:
        for name in names:
            member = find_member(files, root, name)
            if member is None:
                errors.append(f"Info.json declares missing FuseDataFile '{name}' in package root '{'/'.join(root) or '.'}'.")
            else:
                members.append(member)
    else:
        members.extend(
            member
            for member, parts in files.items()
            if starts_with(parts, root)
            and len(parts) == len(root) + 1
            and parts[-1].lower().endswith(".fuse.json")
        )
    for member in dict.fromkeys(members):
        errors.extend(validate_json_member(archive, member))
    return errors


def validate_railloader_data_members(
    archive: zipfile.ZipFile,
    files: dict[zipfile.ZipInfo, tuple[str, ...]],
    root: tuple[str, ...],
) -> list[str]:
    errors: list[str] = []
    for member, parts in files.items():
        if not starts_with(parts, root) or len(parts) != len(root) + 1:
            continue
        name = parts[-1].lower()
        if not name.endswith(".json") or name in MANIFEST_NAMES or name.endswith("report.json"):
            continue
        errors.extend(validate_json_member(archive, member))
    return errors


def inspect_zip(zip_path: Path) -> tuple[list[ZipPackage], list[str]]:
    packages: list[ZipPackage] = []
    warnings: list[str] = []
    try:
        with zipfile.ZipFile(zip_path, "r") as archive:
            files = zip_file_parts(archive)
            layout_errors = archive_layout_errors(archive, files)
            roots = candidate_roots(files)
            for root in roots:
                package = package_from_root(zip_path, archive, files, root)
                if package is None:
                    label = "/".join(root) if root else "."
                    warnings.append(f"{zip_path.name}: unsupported package root '{label}'.")
                    continue
                package.errors.extend(layout_errors)
                if layout_errors:
                    package.kind = "invalid"
                packages.append(package)
            if layout_errors and not packages:
                warnings.extend(f"{zip_path.name}: {error}" for error in layout_errors)
    except zipfile.BadZipFile as exc:
        warnings.append(f"{zip_path.name}: not a readable zip file: {exc}")
    except OSError as exc:
        warnings.append(f"{zip_path.name}: could not be inspected: {exc}")

    if not packages and not warnings:
        warnings.append(f"{zip_path.name}: no package manifest was found.")
    return packages, warnings


def ensure_inside(path: Path, root: Path) -> Path:
    resolved = path.resolve()
    resolved_root = root.resolve()
    if resolved != resolved_root and resolved_root not in resolved.parents:
        raise RuntimeError(f"refusing to write outside destination root: {resolved}")
    return resolved


def extract_package(package: ZipPackage, destination: Path) -> None:
    destination.mkdir(parents=True, exist_ok=True)
    destination_root = destination.resolve()
    with zipfile.ZipFile(package.zip_path, "r") as archive:
        files = zip_file_parts(archive)
        for info, parts in files.items():
            if not starts_with(parts, package.root):
                continue
            relative = relative_name(parts, package.root)
            if not relative:
                continue
            target = destination / Path(*relative.split("/"))
            ensure_inside(target, destination_root)
            target.parent.mkdir(parents=True, exist_ok=True)
            with archive.open(info, "r") as source, target.open("wb") as output:
                shutil.copyfileobj(source, output)


def unique_path(path: Path) -> Path:
    if not path.exists():
        return path
    for index in range(2, 1000):
        candidate = path.with_name(f"{path.name}-{index}")
        if not candidate.exists():
            return candidate
    raise RuntimeError(f"could not find available path for {path}")


def install_package(package: ZipPackage, mods_dir: Path, replace: bool, dry_run: bool) -> InstallResult:
    if package.errors:
        raise ValueError("; ".join(package.errors))
    destination = mods_dir / package.install_name
    backup: Path | None = None

    if destination.exists() and not replace:
        return InstallResult(package, "skipped", destination, "destination already exists")

    if dry_run:
        return InstallResult(package, "installed", destination, "dry run", None)

    mods_dir.mkdir(parents=True, exist_ok=True)
    # A hidden staging root that safe_folder_name() can never produce (it strips
    # leading dots), so it can never equal a package's own install destination —
    # e.g. a package whose id is "FUSEInstaller".
    staging_root = mods_dir / ".fuse-installer-staging"
    staging_root.mkdir(parents=True, exist_ok=True)
    staging = unique_path(staging_root / package.install_name)

    # Extract fully into a staging directory first. If any member fails to
    # write, discard the partial staging copy and re-raise: an existing install
    # is never touched, and a fresh one is never left half-written for the game
    # to load.
    try:
        extract_package(package, staging)
    except BaseException:
        shutil.rmtree(staging, ignore_errors=True)
        raise

    # Extraction succeeded. Back up any existing install, then swap staging into
    # place with a rename on the same filesystem (atomic on Windows and POSIX).
    if destination.exists():
        timestamp = datetime.now().strftime("%Y%m%d-%H%M%S")
        backup_root = mods_dir / "ModBackups" / "FUSEInstaller" / timestamp
        backup = unique_path(backup_root / destination.name)
        backup.parent.mkdir(parents=True, exist_ok=True)
        shutil.move(str(destination), str(backup))

    try:
        shutil.move(str(staging), str(destination))
    except BaseException:
        # The swap failed after the old install was moved aside; restore it so
        # the mod is never left uninstalled, and drop the staging copy.
        shutil.rmtree(staging, ignore_errors=True)
        if backup is not None and not destination.exists():
            shutil.move(str(backup), str(destination))
            backup = None
        raise

    status = "updated" if backup is not None else "installed"
    return InstallResult(package, status, destination, "", backup)


def validate_game_dir(game_dir: Path) -> list[str]:
    errors: list[str] = []
    if not (game_dir / "Railroader.exe").is_file():
        errors.append(f"Railroader.exe was not found in {game_dir}")

    managed = game_dir / "Railroader_Data" / "Managed"
    if not managed.is_dir():
        errors.append(f"Railroader_Data\\Managed was not found in {game_dir}")
    return errors


def unity_mod_manager_installed(game_dir: Path) -> bool:
    managed = game_dir / "Railroader_Data" / "Managed"
    candidates = (
        managed / "UnityModManager" / "UnityModManager.dll",
        managed / "UnityModManager" / "UnityModManagerNet.dll",
        managed / "UnityModManager.dll",
        managed / "UnityModManagerNet.dll",
    )
    return any(candidate.is_file() for candidate in candidates)


def find_legacy_managed_files(game_dir: Path) -> list[Path]:
    managed = game_dir / "Railroader_Data" / "Managed"
    if not managed.is_dir():
        return []
    by_name = {item.name.lower(): item for item in managed.iterdir() if item.is_file()}
    return [
        by_name[name.lower()]
        for name in LEGACY_MANAGED_FILES
        if name.lower() in by_name
    ]


def backup_legacy_managed_files(files: Iterable[Path], mods_dir: Path, dry_run: bool) -> list[tuple[Path, Path]]:
    existing = [Path(path).resolve() for path in files if Path(path).is_file()]
    if not existing:
        return []

    timestamp = datetime.now().strftime("%Y%m%d-%H%M%S")
    backup_root = mods_dir / "ModBackups" / "FUSEInstaller" / f"LegacyLoader-{timestamp}" / "Managed"
    moved: list[tuple[Path, Path]] = []
    for source in existing:
        destination = unique_path(backup_root / source.name)
        moved.append((source, destination))
        if dry_run:
            continue
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.move(str(source), str(destination))
    return moved


def read_loose_info_json(folder: Path) -> dict[str, Any] | None:
    """Read a UMM manifest for install-state checks without executing code."""
    for name in ("Info.json", "info.json"):
        path = folder / name
        if not path.is_file():
            continue
        try:
            value = legacy_json.loads(path.read_text(encoding="utf-8-sig"), repair=False)
            return value if isinstance(value, dict) else None
        except (OSError, UnicodeError, ValueError, json.JSONDecodeError):
            return None
    return None


def find_loose_info_path(folder: Path) -> Path | None:
    """Return the existing UMM manifest path without changing its casing."""
    for name in ("Info.json", "info.json"):
        path = folder / name
        if path.is_file():
            return path
    return None


def manifest_contains_dependency(value: Any, package_id: str) -> bool:
    expected = (package_id or "").strip().lower()
    if not expected:
        return False
    if isinstance(value, str):
        return value.strip().lower() == expected
    if isinstance(value, dict):
        actual = value.get("Id", value.get("id", ""))
        return str(actual or "").strip().lower() == expected
    if isinstance(value, list):
        return any(manifest_contains_dependency(item, package_id) for item in value)
    return False


def append_manifest_dependency(manifest: dict[str, Any], field_name: str, package_id: str) -> bool:
    current = manifest.get(field_name)
    if manifest_contains_dependency(current, package_id):
        return False
    if current is None:
        manifest[field_name] = [package_id]
    elif isinstance(current, list):
        current.append(package_id)
    else:
        manifest[field_name] = [current, package_id]
    return True


def references_fuse_replaced_startup_assembly(assembly_path: Path) -> bool:
    """Inspect CLR metadata strings without importing or executing the DLL."""
    try:
        data = assembly_path.read_bytes()
    except OSError:
        return False
    return any(reference in data for reference in FUSE_LEGACY_STARTUP_ASSEMBLY_REFERENCES)


def find_legacy_umm_startup_dependency_manifests(mods_dir: Path) -> list[tuple[Path, Path, dict[str, Any]]]:
    """Find code mods that need FUSE loaded before UMM reflects their DLL."""
    if not mods_dir.is_dir():
        return []

    candidates: list[tuple[Path, Path, dict[str, Any]]] = []
    for folder in sorted(mods_dir.iterdir(), key=lambda item: item.name.lower()):
        if not folder.is_dir() or folder.name.lower() in {"fuse", "fuseinstaller", "modbackups"}:
            continue
        info_path = find_loose_info_path(folder)
        manifest = read_loose_info_json(folder)
        if info_path is None or manifest is None:
            continue
        if str(manifest.get("Id", manifest.get("id", ""))).strip().lower() == "fuse":
            continue
        assembly_name = str(manifest.get("AssemblyName", manifest.get("assemblyName", "")) or "").strip()
        if not assembly_name:
            continue
        assembly_path = folder / assembly_name
        if not assembly_path.is_file() or not references_fuse_replaced_startup_assembly(assembly_path):
            continue
        if (
            manifest_contains_dependency(manifest.get("Requirements"), "FUSE")
            and manifest_contains_dependency(manifest.get("LoadAfter"), "FUSE")
        ):
            continue
        candidates.append((folder, info_path, manifest))
    return candidates


def repair_legacy_umm_startup_dependencies(mods_dir: Path, dry_run: bool) -> list[CompatibilityAction]:
    """Make legacy code mods load after FUSE, with recoverable manifest backups."""
    candidates = find_legacy_umm_startup_dependency_manifests(mods_dir)
    if not candidates:
        return []

    timestamp = datetime.now().strftime("%Y%m%d-%H%M%S")
    backup_root = mods_dir / "ModBackups" / "FUSEInstaller" / f"CompatibilityManifests-{timestamp}"
    actions: list[CompatibilityAction] = []
    for folder, info_path, manifest in candidates:
        package_id = str(manifest.get("Id", manifest.get("id", folder.name)) or folder.name)
        backup_path = unique_path(backup_root / folder.name / info_path.name)
        try:
            append_manifest_dependency(manifest, "Requirements", "FUSE")
            append_manifest_dependency(manifest, "LoadAfter", "FUSE")
            if not dry_run:
                backup_path.parent.mkdir(parents=True, exist_ok=True)
                shutil.copy2(info_path, backup_path)
                temporary_path = info_path.with_name(info_path.name + ".fuse-installer-new")
                temporary_path.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
                os.replace(temporary_path, info_path)
            actions.append(CompatibilityAction(
                component=package_id,
                status="planned" if dry_run else "updated",
                message=(
                    "Would add FUSE to UMM Requirements/LoadAfter so FUSE's legacy API bridge is active "
                    "before this assembly is reflected."
                    if dry_run else
                    "Added FUSE to UMM Requirements/LoadAfter so FUSE's legacy API bridge is active "
                    "before this assembly is reflected; the original manifest was backed up."
                ),
                source=info_path,
                destination=backup_path,
            ))
        except Exception as exc:
            actions.append(CompatibilityAction(
                component=package_id,
                status="failed",
                message=f"Could not repair legacy UMM startup order: {exc}",
                source=info_path,
                destination=backup_path,
            ))
    return actions


def folder_contains_file_named(folder: Path, filename: str) -> bool:
    if not folder.is_dir():
        return False
    expected = filename.lower()
    try:
        return any(item.is_file() and item.name.lower() == expected for item in folder.iterdir())
    except OSError:
        return False


def is_fuse_assetloader_compatibility(folder: Path) -> bool:
    info = read_loose_info_json(folder)
    if not info:
        return False
    marker = info.get("FuseProvidedCompatibility")
    package_id = info.get("Id", info.get("id"))
    return (
        str(package_id or "").lower() == ASSETLOADER_ID.lower()
        and marker == ASSETLOADER_COMPATIBILITY_MARKER
        and not folder_contains_file_named(folder, ASSETLOADER_DLL)
    )


def find_fuse_assetloader_compatibility(mods_dir: Path) -> Path | None:
    if not mods_dir.is_dir():
        return None
    for child in sorted(mods_dir.iterdir(), key=lambda item: item.name.lower()):
        if child.is_dir() and is_fuse_assetloader_compatibility(child):
            return child
    return None


def find_installed_mod_by_id(mods_dir: Path, package_id: str) -> Path | None:
    if not mods_dir.is_dir() or not package_id:
        return None
    for child in sorted(mods_dir.iterdir(), key=lambda item: item.name.lower()):
        if not child.is_dir():
            continue
        info = read_loose_info_json(child)
        installed_id = str((info or {}).get("Id", (info or {}).get("id", "")))
        if installed_id.lower() == package_id.lower():
            return child
    return None


def read_installed_package_versions(mods_dir: Path) -> dict[str, str]:
    result: dict[str, str] = {}
    if not mods_dir.is_dir():
        return result
    for child in sorted(mods_dir.iterdir(), key=lambda item: item.name.lower()):
        if not child.is_dir():
            continue
        manifest = read_loose_info_json(child)
        if manifest is None:
            definition = child / "Definition.json"
            if definition.is_file():
                try:
                    value = legacy_json.loads(
                        definition.read_text(encoding="utf-8-sig"),
                        repair=False,
                    )
                    manifest = value if isinstance(value, dict) else None
                except (OSError, UnicodeError, ValueError, json.JSONDecodeError):
                    manifest = None
        package_id = string_field(manifest, "Id", "id")
        if package_id:
            version = string_field(manifest, "Version", "version")
            for alias in package_id_aliases(package_id):
                result.setdefault(alias, version)
        result.setdefault(child.name.lower(), string_field(manifest, "Version", "version"))
    return result


def version_numbers(value: str) -> tuple[int, ...] | None:
    text = (value or "").strip()
    if not text:
        return None
    matches = re.findall(r"\d+", text)
    return tuple(int(item) for item in matches) if matches else None


def compare_versions(left: str, right: str) -> int | None:
    first = version_numbers(left)
    second = version_numbers(right)
    if first is None or second is None:
        return None
    width = max(len(first), len(second))
    first += (0,) * (width - len(first))
    second += (0,) * (width - len(second))
    return (first > second) - (first < second)


def validate_batch_dependencies(
    packages: Iterable[ZipPackage],
    mods_dir: Path,
    fuse_available: bool,
) -> None:
    package_list = list(packages)
    by_id: dict[str, list[ZipPackage]] = {}
    for package in package_list:
        for alias in package_id_aliases(package.package_id):
            by_id.setdefault(alias, []).append(package)
    duplicate_groups: dict[str, list[ZipPackage]] = {}
    for package in package_list:
        aliases = package_id_aliases(package.package_id)
        duplicate_key = aliases[-1] if aliases else package.package_id.lower()
        duplicate_groups.setdefault(duplicate_key, []).append(package)
    for package_id, duplicates in duplicate_groups.items():
        if len(duplicates) <= 1:
            continue
        sources = ", ".join(
            f"{item.zip_path.name}:{item.root_label}" for item in duplicates
        )
        for package in duplicates:
            package.errors.append(
                f"Duplicate package id '{package.package_id}' appears more than once in this batch ({sources}). "
                "Remove the duplicate archive/root so installation order cannot overwrite it."
            )

    installed = read_installed_package_versions(mods_dir)
    installed_fuse_available = fuse_available or "fuse" in installed

    # Repeat until stable so a package whose own preflight failed cannot satisfy
    # another package merely because both happened to be present in the batch.
    # Existing installed providers remain valid fallbacks when an update ZIP is
    # bad, and forward references within a healthy batch are supported.
    changed = True
    while changed:
        changed = False
        fuse_provider_available = installed_fuse_available or any(
            "fuse" in package_id_aliases(candidate.package_id)
            and not candidate.errors
            for candidate in package_list
        )
        for package in package_list:
            if package.errors:
                continue
            for requirement in package.requirements:
                required_id = requirement.package_id.lower()
                replacement_id = replacement_id_key(requirement.package_id)
                if fuse_provider_available and replacement_id in FUSE_REPLACEMENT_IDS:
                    continue

                provider_versions: list[str] = []
                if required_id in installed:
                    provider_versions.append(installed[required_id])
                provider_versions.extend(
                    candidate.version
                    for candidate in by_id.get(required_id, [])
                    if not candidate.errors
                    and candidate.package_id.lower() != ASSETLOADER_ID.lower()
                )
                if not provider_versions:
                    bounds = requirement_bounds(requirement)
                    batch_provider_failed = required_id in by_id
                    if batch_provider_failed:
                        message = (
                            f"Dependency '{requirement.package_id}'{bounds} required by "
                            f"'{package.package_id}' is in this batch but failed preflight. "
                            "Fix or remove the failed dependency package, then retry the batch."
                        )
                    else:
                        message = (
                            f"Missing dependency '{requirement.package_id}'{bounds} required by "
                            f"'{package.package_id}'. {dependency_install_hint(requirement.package_id)}"
                        )
                    package.errors.append(message)
                    changed = True
                    break

                if not requirement.not_before and not requirement.not_after:
                    continue
                if any(version_numbers(version) is None for version in provider_versions):
                    note = (
                        f"Dependency '{requirement.package_id}' is present but has no readable version; "
                        f"the installer could not verify {requirement_bounds(requirement).strip()}."
                    )
                    if note not in package.notes:
                        package.notes.append(note)
                    continue
                if any(
                    requirement_version_matches(version, requirement)
                    for version in provider_versions
                ):
                    continue

                available_versions = ", ".join(sorted(set(provider_versions)))
                package.errors.append(
                    f"Dependency version conflict for '{requirement.package_id}': "
                    f"'{package.package_id}' requires{requirement_bounds(requirement)}, "
                    f"but available version(s) are {available_versions}."
                )
                changed = True
                break


def requirement_version_matches(version: str, requirement: PackageRequirement) -> bool:
    if requirement.not_before:
        comparison = compare_versions(version, requirement.not_before)
        if comparison is not None and comparison < 0:
            return False
    if requirement.not_after:
        comparison = compare_versions(version, requirement.not_after)
        if comparison is not None and comparison > 0:
            return False
    return True


def dependency_install_hint(package_id: str) -> str:
    if replacement_id_key(package_id) in FUSE_REPLACEMENT_IDS:
        return (
            "Install or update FUSE (or include FUSE in this batch); FUSE provides this "
            "legacy dependency contract, so do not reinstall the replaced legacy DLL."
        )
    return "Install/enable that package in Mods, or include it in this batch."


def requirement_bounds(requirement: PackageRequirement) -> str:
    parts = []
    if requirement.not_before:
        parts.append("not before " + requirement.not_before)
    if requirement.not_after:
        parts.append("not after " + requirement.not_after)
    return " (" + ", ".join(parts) + ")" if parts else ""


def find_legacy_assetloader_paths(mods_dir: Path) -> list[Path]:
    """Find old AssetLoader runtime files while excluding FUSE's data-only alias."""
    if not mods_dir.is_dir():
        return []

    found: list[Path] = []
    for child in sorted(mods_dir.iterdir(), key=lambda item: item.name.lower()):
        if child.is_file():
            lower_name = child.name.lower()
            if lower_name == "assetloader.zip" or lower_name == "assetloader.dll":
                found.append(child)
            continue

        if not child.is_dir() or is_fuse_assetloader_compatibility(child):
            continue

        info = read_loose_info_json(child)
        package_id = str((info or {}).get("Id", (info or {}).get("id", "")))
        has_runtime = folder_contains_file_named(child, ASSETLOADER_DLL)
        if has_runtime or package_id.lower() == ASSETLOADER_ID.lower():
            found.append(child)

    return found


def backup_legacy_assetloader_paths(
    paths: Iterable[Path],
    mods_dir: Path,
    dry_run: bool,
) -> list[tuple[Path, Path]]:
    existing = [Path(path).resolve() for path in paths if Path(path).exists()]
    if not existing:
        return []

    timestamp = datetime.now().strftime("%Y%m%d-%H%M%S")
    backup_root = mods_dir / "ModBackups" / "FUSEInstaller" / f"AssetLoader-{timestamp}"
    moved: list[tuple[Path, Path]] = []
    for source in existing:
        destination = unique_path(backup_root / source.name)
        moved.append((source, destination))
        if dry_run:
            continue
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.move(str(source), str(destination))
    return moved


def should_repair_assetloader(args: argparse.Namespace, paths: list[Path]) -> bool:
    if not paths:
        return True
    if getattr(args, "repair_asset_loader", False) or getattr(args, "repair_legacy_loader", False):
        return True
    if args.dry_run or not sys.stdin.isatty():
        return False

    print("\nFUSE now replaces the old AssetLoader runtime. These paths remain installed:")
    for path in paths:
        print(f"  {path}")
    print("They will be moved to a dated backup and replaced by a data-only dependency alias.")
    try:
        answer = input("Back up and replace old AssetLoader now? [Y/n]: ").strip().lower()
    except EOFError:
        return False
    return answer in {"", "y", "yes"}


def install_assetloader_compatibility_alias(mods_dir: Path, dry_run: bool) -> Path:
    existing = find_fuse_assetloader_compatibility(mods_dir)
    if existing is not None:
        destination = existing
    else:
        preferred = mods_dir / ASSETLOADER_ID
        destination = preferred if not preferred.exists() else mods_dir / ASSETLOADER_COMPATIBILITY_MARKER

    if dry_run:
        return destination

    destination.mkdir(parents=True, exist_ok=True)
    manifest_path = destination / "Info.json"
    temporary_path = destination / "Info.json.fuse-installer-new"
    temporary_path.write_text(
        json.dumps(ASSETLOADER_COMPATIBILITY_MANIFEST, indent=2) + "\n",
        encoding="utf-8",
    )
    os.replace(temporary_path, manifest_path)
    return destination


def should_repair_legacy_files(args: argparse.Namespace, files: list[Path]) -> bool:
    if not files:
        return False
    if args.repair_legacy_loader:
        return True
    if args.dry_run or not sys.stdin.isatty():
        return False

    print("\nFUSE cannot safely run while these old managed loader files remain:")
    for path in files:
        print(f"  {path}")
    print("They will be moved to a dated backup, not deleted.")
    try:
        answer = input("Back up and remove the old loader files now? [Y/n]: ").strip().lower()
    except EOFError:
        return False
    return answer in {"", "y", "yes"}


def default_game_dir() -> Path:
    start = Path(sys.executable).resolve().parent if getattr(sys, "frozen", False) else Path.cwd().resolve()
    # FUSE-Complete keeps utilities under ``Tools``. Let that copy work when
    # the complete archive itself was extracted into the Railroader folder.
    for candidate in (start, start.parent):
        if (candidate / "Railroader.exe").is_file():
            return candidate
    discovered = discover_game_dirs()
    return discovered[0] if discovered else start


def parse_steam_library_paths(text: str) -> list[Path]:
    """Read Steam's old or current libraryfolders.vdf path entries."""
    paths: list[Path] = []
    seen: set[str] = set()
    for match in re.finditer(r'"path"\s+"([^"]+)"', text or "", flags=re.IGNORECASE):
        value = match.group(1).replace("\\\\", "\\").strip()
        if not value:
            continue
        key = os.path.normcase(os.path.abspath(value))
        if key in seen:
            continue
        seen.add(key)
        paths.append(Path(value))
    return paths


def steam_roots() -> list[Path]:
    candidates: list[Path] = []
    if sys.platform.startswith("win"):
        try:
            import winreg

            for hive, key_name, value_name in (
                (winreg.HKEY_CURRENT_USER, r"Software\Valve\Steam", "SteamPath"),
                (winreg.HKEY_LOCAL_MACHINE, r"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath"),
                (winreg.HKEY_LOCAL_MACHINE, r"SOFTWARE\Valve\Steam", "InstallPath"),
            ):
                try:
                    with winreg.OpenKey(hive, key_name) as key:
                        value, _kind = winreg.QueryValueEx(key, value_name)
                        if value:
                            candidates.append(Path(str(value)))
                except OSError:
                    continue
        except (ImportError, OSError):
            pass

    for environment_name in ("ProgramFiles(x86)", "ProgramFiles"):
        value = os.environ.get(environment_name, "").strip()
        if value:
            candidates.append(Path(value) / "Steam")
    candidates.extend((Path(r"C:\Steam"), Path(r"D:\Steam"), Path(r"D:\SteamLibrary")))

    roots: list[Path] = []
    seen: set[str] = set()
    for candidate in candidates:
        try:
            resolved = candidate.expanduser().resolve()
        except OSError:
            continue
        key = os.path.normcase(str(resolved))
        if key in seen or not resolved.exists():
            continue
        seen.add(key)
        roots.append(resolved)
    return roots


def discover_game_dirs() -> list[Path]:
    libraries: list[Path] = []
    for root in steam_roots():
        libraries.append(root)
        vdf = root / "steamapps" / "libraryfolders.vdf"
        try:
            if vdf.is_file():
                libraries.extend(parse_steam_library_paths(vdf.read_text(encoding="utf-8", errors="replace")))
        except OSError:
            continue

    games: list[Path] = []
    seen: set[str] = set()
    for library in libraries:
        candidate = library / "steamapps" / "common" / "Railroader"
        try:
            resolved = candidate.resolve()
        except OSError:
            continue
        key = os.path.normcase(str(resolved))
        if key in seen or not (resolved / "Railroader.exe").is_file():
            continue
        seen.add(key)
        games.append(resolved)
    return games


def resolve_bundled_fuse() -> Path | None:
    """Locate the FUSE mod zip bundled into the exe (or supplied for testing).

    Resolution order:
      1. FUSE_INSTALLER_BUNDLED_ZIP env var (dev/testing and advanced use).
      2. PyInstaller's extraction dir (sys._MEIPASS) for the frozen exe.
      3. A bundled_fuse.zip sitting beside this script.
    """
    override = os.environ.get("FUSE_INSTALLER_BUNDLED_ZIP", "").strip()
    if override:
        candidate = Path(override).expanduser()
        return candidate.resolve() if candidate.exists() else None

    meipass = getattr(sys, "_MEIPASS", None)
    if meipass:
        candidate = Path(meipass) / BUNDLED_FUSE_NAME
        if candidate.exists():
            return candidate.resolve()

    sibling = SCRIPT_DIR / BUNDLED_FUSE_NAME
    if sibling.exists():
        return sibling.resolve()

    return None


def find_input_zips(args: argparse.Namespace, default_inbox: Path) -> list[Path]:
    if args.zips:
        return [Path(item).resolve() for item in args.zips]

    inbox = Path(args.inbox).resolve() if args.inbox else default_inbox.resolve()
    if not inbox.exists() or not inbox.is_dir():
        return []
    return sorted((path.resolve() for path in inbox.glob("*.zip")), key=lambda item: item.name.lower())


def archive_zip(zip_path: Path, mods_dir: Path, dry_run: bool) -> Path:
    timestamp = datetime.now().strftime("%Y%m%d-%H%M%S")
    destination_dir = mods_dir / "FUSEInstaller" / "InstalledZips" / timestamp
    destination = unique_path(destination_dir / zip_path.name)
    if not dry_run:
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.move(str(zip_path), str(destination))
    return destination


def print_package(package: ZipPackage) -> None:
    version = f" version={package.version}" if package.version else ""
    print(f"  {package.kind}: {package.display_name} id={package.package_id}{version}")
    print(f"    root: {package.root_label}")
    print(f"    manifest: {package.manifest_member}")
    if package.notes:
        for note in package.notes:
            print(f"    note: {note}")
    if package.errors:
        for error in package.errors:
            print(f"    error: {error}")


def print_result(result: InstallResult) -> None:
    prefix = result.status.upper()
    print(f"{prefix}: {result.package.display_name}")
    print(f"  kind: {result.package.kind}")
    print(f"  from: {result.package.zip_path}")
    print(f"  to:   {result.destination}")
    if result.backup is not None:
        print(f"  backup: {result.backup}")
    if result.message:
        print(f"  note: {result.message}")


def write_install_report(
    game_dir: Path,
    mods_dir: Path,
    targets: list[Path],
    results: list[InstallResult],
    scan_failures: int,
    dry_run: bool,
    compatibility_actions: list[CompatibilityAction] | None = None,
) -> Path | None:
    if dry_run:
        return None
    report_root = mods_dir / "FUSEInstaller" / "Reports"
    timestamp = datetime.now().strftime("%Y%m%d-%H%M%S")
    report_path = unique_path(report_root / f"install-{timestamp}.json")
    compatibility_actions = compatibility_actions or []
    report = {
        "installerVersion": TOOL_VERSION,
        "generated": datetime.now().astimezone().isoformat(),
        "dryRun": dry_run,
        "gameDirectory": str(game_dir),
        "modsDirectory": str(mods_dir),
        "archives": [str(path) for path in targets],
        "scanFailures": scan_failures,
        "summary": {
            "installed": sum(1 for item in results if item.status == "installed"),
            "updated": sum(1 for item in results if item.status == "updated"),
            "skipped": sum(1 for item in results if item.status == "skipped"),
            "failed": (
                scan_failures
                + sum(1 for item in results if item.status == "failed")
                + sum(1 for item in compatibility_actions if item.status == "failed")
            ),
        },
        "packages": [
            {
                "status": item.status,
                "kind": item.package.kind,
                "id": item.package.package_id,
                "name": item.package.display_name,
                "version": item.package.version,
                "sourceArchive": str(item.package.zip_path),
                "sourceRoot": item.package.root_label,
                "manifest": item.package.manifest_member,
                "destination": str(item.destination),
                "backup": str(item.backup) if item.backup else None,
                "message": item.message,
                "notes": list(item.package.notes),
                "errors": list(item.package.errors),
                "requirements": [
                    {
                        "id": requirement.package_id,
                        "notBefore": requirement.not_before or None,
                        "notAfter": requirement.not_after or None,
                    }
                    for requirement in item.package.requirements
                ],
            }
            for item in results
        ],
        "compatibilityActions": [
            {
                "component": item.component,
                "status": item.status,
                "message": item.message,
                "source": str(item.source) if item.source else None,
                "destination": str(item.destination) if item.destination else None,
            }
            for item in compatibility_actions
        ],
    }
    try:
        report_path.parent.mkdir(parents=True, exist_ok=True)
        report_path.write_text(json.dumps(report, indent=2), encoding="utf-8")
        return report_path
    except OSError as exc:
        print(f"WARNING: could not write install report: {exc}")
        return None


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description=(
            "Install FUSE and mod packages. Run it with no arguments to install "
            "the bundled FUSE framework; drag mod .zip files onto it (or pass them "
            "as arguments) to install those mods."
        ),
    )
    parser.add_argument("zips", nargs="*", help="Zip files to install. Drag-and-drop accepts multiple files on Windows.")
    parser.add_argument("--game-dir", default=None, help="Base folder. Default is this exe's folder, or the current directory when run as a script.")
    parser.add_argument("--mods-dir", default=None, help="Mods folder. Default is <base folder>\\Mods.")
    parser.add_argument("--inbox", default=None, help="Folder to scan for zip files when no zip arguments are passed.")
    parser.add_argument("--replace", action="store_true", help="Backup and replace existing mod folders (this is now the default).")
    parser.add_argument("--skip-existing", action="store_true", help="Leave existing mod folders unchanged instead of updating them.")
    parser.add_argument("--repair-legacy-loader", action="store_true", help="Back up conflicting legacy loader DLLs from Railroader_Data\\Managed before installing.")
    parser.add_argument("--repair-asset-loader", action="store_true", help="Back up the old Mods\\AssetLoader runtime and install FUSE's data-only AssetLoader dependency alias.")
    parser.add_argument("--with-fuse", action="store_true", help="Install/update the bundled FUSE framework along with explicitly supplied mod zips.")
    parser.add_argument("--no-fuse", action="store_true", help="Do not install the bundled FUSE framework on a manual run; only process zip files.")
    parser.add_argument("--dry-run", action="store_true", help="Inspect zips and print planned installs without writing files.")
    parser.add_argument("--archive-zips", action="store_true", help="Move successfully processed zips to Mods\\FUSEInstaller\\InstalledZips.")
    parser.add_argument("--pause", action="store_true", help="Pause before closing.")
    parser.add_argument("--no-pause", action="store_true", help="Do not pause before closing, even when bundled as an exe.")
    parser.add_argument("--cli", action="store_true", help="Use command-line mode instead of the graphical installer.")
    parser.add_argument("--gui", action="store_true", help="Use the graphical installer when running the Python script.")
    parser.add_argument("--version", action="version", version=f"FUSE Installer {TOOL_VERSION}")
    return parser


def should_preselect_bundled_fuse(args: argparse.Namespace, bundled_available: bool) -> bool:
    """Return the GUI default while preserving drag-and-drop input scope.

    A manual launch is the FUSE installation flow, so the bundled framework is
    selected. Dragging explicit archives onto the executable processes only
    those archives unless ``--with-fuse`` was explicitly supplied.
    """
    return bool(
        bundled_available
        and not args.no_fuse
        and (not args.zips or args.with_fuse)
    )


def run(args: argparse.Namespace) -> int:
    game_dir = Path(args.game_dir).resolve() if args.game_dir else default_game_dir()
    mods_dir = Path(args.mods_dir).resolve() if args.mods_dir else (game_dir / "Mods").resolve()

    preflight_errors = validate_game_dir(game_dir)
    if not unity_mod_manager_installed(game_dir):
        preflight_errors.append(
            "Unity Mod Manager is not installed for this Railroader copy. "
            "Install UMM first; FUSE itself is loaded by UMM."
        )
    if preflight_errors:
        print(f"FUSE Installer {TOOL_VERSION}")
        print("FAILED: Railroader installation preflight")
        for error in preflight_errors:
            print(f"  {error}")
        print("  Put FUSE-Installer.exe beside Railroader.exe, or pass --game-dir to the correct folder.")
        return 1

    legacy_files = find_legacy_managed_files(game_dir)
    if legacy_files:
        if should_repair_legacy_files(args, legacy_files):
            moved = backup_legacy_managed_files(legacy_files, mods_dir, args.dry_run)
            verb = "Would move" if args.dry_run else "Moved"
            for source, destination in moved:
                print(f"{verb} legacy loader: {source} -> {destination}")
            print("Legacy loader preflight: CLEAN")
        else:
            print(f"FUSE Installer {TOOL_VERSION}")
            print("FAILED: conflicting legacy loader files were found:")
            for path in legacy_files:
                print(f"  {path}")
            print("Run again and approve the backup prompt, or pass --repair-legacy-loader.")
            print("Steam Verify can restore modified game files, but it may not remove extra DLLs; the files above must be moved too.")
            return 1

    explicit = bool(args.zips)
    zips = find_input_zips(args, game_dir)

    # Manual run (no zip arguments): install the FUSE framework bundled into the
    # exe, alongside any loose zips found beside it. Dragging specific zips onto
    # the exe installs exactly those and never force-installs FUSE.
    bundle_on_disk = resolve_bundled_fuse()
    bundled_fuse = bundle_on_disk if ((not explicit or args.with_fuse) and not args.no_fuse) else None

    targets: list[Path] = []
    seen: set[Path] = set()
    for candidate in ([bundled_fuse] if bundled_fuse else []) + zips:
        if candidate in seen:
            continue
        seen.add(candidate)
        targets.append(candidate)

    print(f"FUSE Installer {TOOL_VERSION}")
    print(f"base: {game_dir}")
    print(f"mods: {mods_dir}")
    if args.dry_run:
        print("mode: dry run")
    replace_existing = not args.skip_existing or args.replace
    print(f"existing mods: {'backup and update' if replace_existing else 'skip'}")
    if bundled_fuse is not None:
        print(f"bundled FUSE: {bundled_fuse.name}")
    print()

    if not targets:
        if not explicit and not args.no_fuse and bundle_on_disk is None:
            print("No zip files were found, and this build has no bundled FUSE payload.")
        else:
            print("No zip files were found.")
        return 1

    if not args.dry_run:
        mods_dir.mkdir(parents=True, exist_ok=True)

    scan_cache: dict[Path, tuple[list[ZipPackage], list[str]]] = {}
    scanned_packages: list[ZipPackage] = []
    for target in targets:
        if not target.exists():
            continue
        scan_cache[target] = inspect_zip(target)
        scanned_packages.extend(scan_cache[target][0])
    fuse_available_for_dependencies = find_installed_mod_by_id(mods_dir, "FUSE") is not None
    validate_batch_dependencies(
        scanned_packages,
        mods_dir,
        fuse_available_for_dependencies,
    )

    all_results: list[InstallResult] = []
    scan_failures = 0
    for zip_path in targets:
        label = "FUSE" if zip_path == bundled_fuse else "ZIP"
        print(f"{label}: {zip_path}")
        if not zip_path.exists():
            print("  error: file does not exist")
            scan_failures += 1
            continue

        packages, warnings = (
            scan_cache[zip_path]
            if zip_path in scan_cache
            else inspect_zip(zip_path)
        )
        for warning in warnings:
            print(f"  warning: {warning}")
        if not packages:
            scan_failures += 1
            continue

        for package in packages:
            print_package(package)
            if package.package_id.lower() == ASSETLOADER_ID.lower():
                result = InstallResult(
                    package,
                    "skipped",
                    mods_dir / package.install_name,
                    "Old AssetLoader is replaced by FUSE; the installer will create a data-only dependency alias.",
                )
                all_results.append(result)
                print_result(result)
                continue
            if package.errors:
                result = InstallResult(
                    package,
                    "failed",
                    mods_dir / package.install_name,
                    "; ".join(package.errors),
                )
                all_results.append(result)
                print_result(result)
                continue
            try:
                result = install_package(package, mods_dir, replace_existing, args.dry_run)
                all_results.append(result)
                print_result(result)
            except Exception as exc:
                result = InstallResult(package, "failed", mods_dir / package.install_name, str(exc))
                all_results.append(result)
                print_result(result)
        print()

        zip_results = [result for result in all_results if result.package.zip_path == zip_path]
        assetloader_without_replacement = (
            any(result.package.package_id.lower() == ASSETLOADER_ID.lower() for result in zip_results)
            and find_installed_mod_by_id(mods_dir, "FUSE") is None
            and not any(
                result.package.package_id.lower() == "fuse" and result.status != "failed"
                for result in all_results
            )
            and bundled_fuse is None
        )
        # Never archive the bundled FUSE payload: it lives inside the PyInstaller
        # extraction dir, not beside the exe.
        if (
            args.archive_zips
            and zip_path != bundled_fuse
            and zip_results
            and not assetloader_without_replacement
            and not any(result.status == "failed" for result in zip_results)
        ):
            archived_to = archive_zip(zip_path, mods_dir, args.dry_run)
            verb = "Would archive" if args.dry_run else "Archived"
            print(f"{verb}: {zip_path} -> {archived_to}")

    compatibility_actions: list[CompatibilityAction] = []
    fuse_available = (
        find_installed_mod_by_id(mods_dir, "FUSE") is not None
        or any(
            result.package.package_id.lower() == "fuse"
            and result.status != "failed"
            for result in all_results
        )
    )
    assetloader_package_results = [
        result
        for result in all_results
        if result.package.package_id.lower() == ASSETLOADER_ID.lower()
    ]
    if assetloader_package_results and not fuse_available:
        for result in assetloader_package_results:
            result.status = "failed"
            result.message = (
                "AssetLoader is replaced by FUSE, but FUSE is not installed. "
                "Install FUSE first or include --with-fuse."
            )
        compatibility_actions.append(CompatibilityAction(
            ASSETLOADER_ID,
            "blocked",
            "The old AssetLoader package was not installed because its replacement, FUSE, is unavailable.",
        ))
    if fuse_available:
        legacy_assetloader_paths = find_legacy_assetloader_paths(mods_dir)
        if legacy_assetloader_paths and not should_repair_assetloader(args, legacy_assetloader_paths):
            print("FAILED: old AssetLoader runtime remains installed:")
            for path in legacy_assetloader_paths:
                print(f"  {path}")
            print("Run again and approve migration, or pass --repair-asset-loader.")
            compatibility_actions.append(CompatibilityAction(
                ASSETLOADER_ID,
                "failed",
                "Old AssetLoader runtime remains installed; approve migration or pass --repair-asset-loader. "
                "Paths: " + "; ".join(str(path) for path in legacy_assetloader_paths),
            ))
        else:
            try:
                moved = backup_legacy_assetloader_paths(
                    legacy_assetloader_paths,
                    mods_dir,
                    args.dry_run,
                )
                verb = "Would move" if args.dry_run else "Moved"
                for source, destination in moved:
                    print(f"{verb} old AssetLoader: {source} -> {destination}")
                    compatibility_actions.append(CompatibilityAction(
                        ASSETLOADER_ID,
                        "planned" if args.dry_run else "backed-up",
                        "Old AssetLoader runtime was moved to a recoverable backup."
                        if not args.dry_run else
                        "Old AssetLoader runtime would be moved to a recoverable backup.",
                        source,
                        destination,
                    ))

                remaining = [] if args.dry_run else find_legacy_assetloader_paths(mods_dir)
                if remaining:
                    print("FAILED: AssetLoader cleanup verification found remaining runtime paths:")
                    for path in remaining:
                        print(f"  {path}")
                    compatibility_actions.append(CompatibilityAction(
                        ASSETLOADER_ID,
                        "failed",
                        "Cleanup verification found remaining AssetLoader runtime paths: "
                        + "; ".join(str(path) for path in remaining),
                    ))
                else:
                    alias_path = install_assetloader_compatibility_alias(mods_dir, args.dry_run)
                    verb = "Would install" if args.dry_run else "Installed"
                    print(f"{verb} FUSE AssetLoader dependency alias: {alias_path}")
                    print("AssetLoader runtime verification: CLEAN")
                    compatibility_actions.append(CompatibilityAction(
                        ASSETLOADER_ID,
                        "planned" if args.dry_run else "installed",
                        "FUSE's data-only AssetLoader dependency alias is present; no AssetLoader DLL is installed."
                        if not args.dry_run else
                        "FUSE's data-only AssetLoader dependency alias would be installed.",
                        destination=alias_path,
                    ))
            except Exception as exc:
                print(f"FAILED: AssetLoader migration: {exc}")
                compatibility_actions.append(CompatibilityAction(
                    ASSETLOADER_ID,
                    "failed",
                    f"AssetLoader migration failed: {exc}",
                ))

        startup_actions = repair_legacy_umm_startup_dependencies(mods_dir, args.dry_run)
        for action in startup_actions:
            verb = "Would repair" if action.status == "planned" else (
                "Repaired" if action.status == "updated" else "FAILED"
            )
            print(f"{verb} legacy startup order for {action.component}: {action.message}")
        compatibility_actions.extend(startup_actions)

    print("Install results:")
    for result in all_results:
        detail = result.message or str(result.destination)
        print(f"  [{result.status.upper()}] {result.package.display_name} ({result.package.kind}) - {detail}")

    installed = sum(1 for result in all_results if result.status == "installed")
    updated = sum(1 for result in all_results if result.status == "updated")
    skipped = sum(1 for result in all_results if result.status == "skipped")
    failed_results = sum(1 for result in all_results if result.status == "failed")
    compatibility_failures = sum(1 for item in compatibility_actions if item.status == "failed")
    failures = scan_failures + failed_results + compatibility_failures
    print(f"Summary: installed={installed} updated={updated} skipped={skipped} failed={failures}")
    report_path = write_install_report(
        game_dir,
        mods_dir,
        targets,
        all_results,
        scan_failures,
        args.dry_run,
        compatibility_actions,
    )
    if report_path is not None:
        print(f"Report: {report_path}")
    return 1 if failures else 0


def installer_window_size(screen_width: int, screen_height: int) -> tuple[int, int]:
    """Fit the preferred installer window inside a desktop-sized margin."""
    available_width = max(1, screen_width - 48)
    available_height = max(1, screen_height - 64)
    return min(960, available_width), min(760, available_height)


def run_gui(args: argparse.Namespace) -> int:
    import tkinter as tk
    from tkinter import filedialog, messagebox
    from tkinter.scrolledtext import ScrolledText

    root = tk.Tk()
    root.title(f"FUSE Mod Installer {TOOL_VERSION}")
    screen_width = max(1, root.winfo_screenwidth())
    screen_height = max(1, root.winfo_screenheight())
    window_width, window_height = installer_window_size(
        screen_width,
        screen_height,
    )
    window_x = max(0, (screen_width - window_width) // 2)
    window_y = max(0, (screen_height - window_height) // 2)
    root.geometry(
        f"{window_width}x{window_height}+{window_x}+{window_y}")
    root.minsize(min(760, window_width), min(560, window_height))

    game_var = tk.StringVar(value=str(Path(args.game_dir).resolve()) if args.game_dir else str(default_game_dir()))
    bundled_fuse_available = resolve_bundled_fuse() is not None
    install_fuse_var = tk.BooleanVar(
        value=should_preselect_bundled_fuse(args, bundled_fuse_available))
    update_var = tk.BooleanVar(value=not args.skip_existing)
    archive_var = tk.BooleanVar(value=bool(args.archive_zips))
    status_var = tk.StringVar(value="Ready. Select the Railroader folder and packages, then click Install.")

    outer = tk.Frame(root, padx=16, pady=14)
    outer.pack(fill="both", expand=True)

    tk.Label(outer, text="FUSE Suite & Mod Installer", font=("Segoe UI", 17, "bold"), anchor="w").pack(fill="x")
    tk.Label(
        outer,
        text="Installs Native FUSE, Unity Mod Manager, and hosted RailLoader-format mod packages. "
             "Every package is inspected before an existing install is replaced.",
        justify="left",
        wraplength=840,
        anchor="w",
    ).pack(fill="x", pady=(2, 12))

    game_row = tk.Frame(outer)
    game_row.pack(fill="x", pady=(0, 10))
    tk.Label(game_row, text="Railroader folder", width=18, anchor="w").pack(side="left")
    tk.Entry(game_row, textvariable=game_var).pack(side="left", fill="x", expand=True, padx=(0, 6))

    def browse_game() -> None:
        selected = filedialog.askdirectory(title="Select the folder containing Railroader.exe", initialdir=game_var.get())
        if selected:
            game_var.set(selected)

    tk.Button(game_row, text="Browse...", command=browse_game, width=11).pack(side="right")

    package_frame = tk.LabelFrame(outer, text="Mod zip files", padx=8, pady=8)
    package_frame.pack(fill="both", expand=False)
    zip_list = tk.Listbox(package_frame, height=7, selectmode=tk.EXTENDED)
    zip_list.pack(side="left", fill="both", expand=True)
    for item in args.zips:
        zip_list.insert(tk.END, str(Path(item).resolve()))

    package_buttons = tk.Frame(package_frame)
    package_buttons.pack(side="right", fill="y", padx=(8, 0))

    def add_zips() -> None:
        selected = filedialog.askopenfilenames(
            title="Select one or more mod zip files",
            filetypes=(("Zip packages", "*.zip"), ("All files", "*.*")),
        )
        existing = {zip_list.get(index) for index in range(zip_list.size())}
        for item in selected:
            resolved = str(Path(item).resolve())
            if resolved not in existing:
                zip_list.insert(tk.END, resolved)
                existing.add(resolved)

    def remove_zips() -> None:
        for index in reversed(zip_list.curselection()):
            zip_list.delete(index)

    tk.Button(package_buttons, text="Add zips...", command=add_zips, width=13).pack(fill="x")
    tk.Button(package_buttons, text="Remove", command=remove_zips, width=13).pack(fill="x", pady=(6, 0))

    options = tk.Frame(outer)
    options.pack(fill="x", pady=(10, 8))
    fuse_check = tk.Checkbutton(
        options,
        text="Install/update the bundled FUSE framework",
        variable=install_fuse_var,
        state=(tk.NORMAL if bundled_fuse_available else tk.DISABLED),
    )
    fuse_check.pack(anchor="w")
    tk.Checkbutton(options, text="Back up and update existing mod folders", variable=update_var).pack(anchor="w")
    tk.Checkbutton(options, text="Archive successfully installed source zips", variable=archive_var).pack(anchor="w")

    # Reserve the action and status rows before the expanding result pane. Tk's
    # packer allocates widgets in packing order; allowing the result pane to
    # claim the cavity first can push the Install button below a short desktop.
    action_row = tk.Frame(outer)
    action_row.pack(side="bottom", fill="x", pady=(8, 0))

    status_label = tk.Label(outer, textvariable=status_var, anchor="w", justify="left", wraplength=840)
    status_label.pack(side="bottom", fill="x")

    result_box = ScrolledText(outer, height=9, wrap="word", font=("Consolas", 9), state="disabled")
    result_box.pack(fill="both", expand=True, pady=(6, 8))
    result_box.tag_configure("success", foreground="#167a2f")
    result_box.tag_configure("failure", foreground="#b42318")

    def set_result(text: str, success: bool) -> None:
        result_box.configure(state="normal")
        result_box.delete("1.0", tk.END)
        result_box.insert(tk.END, text, "success" if success else "failure")
        result_box.configure(state="disabled")
        result_box.see(tk.END)

    def copy_results() -> None:
        root.clipboard_clear()
        root.clipboard_append(result_box.get("1.0", tk.END).rstrip())
        status_var.set("Copied the install results to the clipboard.")

    def open_mods() -> None:
        path = Path(game_var.get()).expanduser() / "Mods"
        path.mkdir(parents=True, exist_ok=True)
        try:
            os.startfile(str(path))
        except (AttributeError, OSError):
            messagebox.showinfo("Mods folder", str(path))

    def install() -> None:
        game_dir = Path(game_var.get()).expanduser().resolve()
        errors = validate_game_dir(game_dir)
        if errors:
            messagebox.showerror("Railroader folder not found", "\n".join(errors))
            return
        if not unity_mod_manager_installed(game_dir):
            messagebox.showerror(
                "Unity Mod Manager is required",
                "Unity Mod Manager is not installed in this Railroader copy. Install UMM first, then run this installer again.",
            )
            return

        selected_zips = [zip_list.get(index) for index in range(zip_list.size())]
        if not selected_zips and not install_fuse_var.get():
            messagebox.showinfo("Nothing selected", "Add one or more mod zips, or enable the bundled FUSE framework.")
            return

        legacy_files = find_legacy_managed_files(game_dir)
        repair_legacy = False
        if legacy_files:
            listed = "\n".join(str(path) for path in legacy_files)
            repair_legacy = messagebox.askyesno(
                "Old RailLoader files found",
                "These exact legacy loader files conflict with FUSE:\n\n" + listed +
                "\n\nMove them to a dated backup and continue? Nothing will be deleted.",
            )
            if not repair_legacy:
                status_var.set("Install cancelled: old managed RailLoader files must be backed up first.")
                return

        mods_dir = game_dir / "Mods"
        legacy_assetloader_paths = find_legacy_assetloader_paths(mods_dir)
        repair_asset_loader = False
        fuse_will_be_available = (
            install_fuse_var.get()
            or find_installed_mod_by_id(mods_dir, "FUSE") is not None
        )
        if fuse_will_be_available and legacy_assetloader_paths:
            listed = "\n".join(str(path) for path in legacy_assetloader_paths)
            repair_asset_loader = messagebox.askyesno(
                "Old AssetLoader runtime found",
                "FUSE replaces the old AssetLoader runtime. These paths remain installed:\n\n" +
                listed +
                "\n\nMove them to a dated backup and install FUSE's data-only dependency alias? "
                "Nothing will be deleted.",
            )
            if not repair_asset_loader:
                status_var.set("Install cancelled: old AssetLoader must be migrated first.")
                return

        run_args = argparse.Namespace(**vars(args))
        run_args.game_dir = str(game_dir)
        run_args.mods_dir = None
        run_args.zips = selected_zips
        # Prevent the GUI's empty package list from implicitly installing every
        # zip sitting beside Railroader.exe. Only listed files and the checked
        # bundled framework are in scope.
        run_args.inbox = str(game_dir / ".fuse-installer-no-inbox-scan")
        run_args.with_fuse = install_fuse_var.get()
        run_args.no_fuse = not install_fuse_var.get()
        run_args.skip_existing = not update_var.get()
        run_args.replace = update_var.get()
        run_args.archive_zips = archive_var.get()
        run_args.repair_legacy_loader = repair_legacy
        run_args.repair_asset_loader = repair_asset_loader
        run_args.no_pause = True
        run_args.pause = False

        status_var.set("Installing... Existing mods are backed up before replacement.")
        root.update_idletasks()
        output = io.StringIO()
        try:
            with contextlib.redirect_stdout(output), contextlib.redirect_stderr(output):
                exit_code = run(run_args)
        except Exception as exc:
            exit_code = 1
            output.write("\nUNEXPECTED INSTALLER ERROR: " + str(exc))

        text = output.getvalue().strip()
        success = exit_code == 0
        set_result(text, success)
        status_var.set(
            "Install complete. Review the package-by-package results above."
            if success else
            "One or more packages failed. Successful packages were kept; failed packages did not replace existing installs."
        )
        if success:
            messagebox.showinfo("FUSE install complete", "All selected packages installed successfully.")
        else:
            messagebox.showwarning("FUSE install finished with errors", "Review the failed package entries and report path in the installer window.")

    tk.Button(action_row, text="Install", command=install, width=16).pack(side="left")
    tk.Button(action_row, text="Open Mods Folder", command=open_mods, width=16).pack(side="left", padx=(8, 0))
    tk.Button(action_row, text="Copy Results", command=copy_results, width=14).pack(side="left", padx=(8, 0))
    tk.Button(action_row, text="Close", command=root.destroy, width=12).pack(side="right")

    root.mainloop()
    return 0


def should_pause(args: argparse.Namespace) -> bool:
    if args.no_pause:
        return False
    if args.pause:
        return True
    return bool(getattr(sys, "frozen", False) and sys.platform.startswith("win"))


def main() -> int:
    parser = build_parser()
    args = parser.parse_args()
    if args.gui or (getattr(sys, "frozen", False) and sys.platform.startswith("win") and not args.cli):
        return run_gui(args)
    try:
        return run(args)
    finally:
        if should_pause(args):
            try:
                input("\nPress Enter to close...")
            except EOFError:
                pass


if __name__ == "__main__":
    raise SystemExit(main())
