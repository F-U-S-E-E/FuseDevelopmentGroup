#!/usr/bin/env python3
"""
fuse_installer.py - drag-and-drop zip installer for FUSE and UMM packages.

The installer reads zip structure and manifest JSON only. It never imports or
executes package code.
"""

from __future__ import annotations

import argparse
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


TOOL_VERSION = "0.2.0"
BUNDLED_FUSE_NAME = "bundled_fuse.zip"
MANIFEST_NAMES = {"info.json", "definition.json"}
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
    expected = path_key(root + (file_name,))
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


def read_zip_text(archive: zipfile.ZipFile, info: zipfile.ZipInfo) -> str:
    if info.file_size > MAX_JSON_BYTES:
        raise ValueError(f"JSON file is too large to inspect safely: {info.filename}")
    data = archive.read(info)
    try:
        return data.decode("utf-8-sig")
    except UnicodeDecodeError:
        return data.decode("utf-8-sig", errors="replace")


def read_zip_json(archive: zipfile.ZipFile, info: zipfile.ZipInfo) -> Any:
    return legacy_json.loads(read_zip_text(archive, info))


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

    if info_member is not None:
        try:
            loaded = read_zip_json(archive, info_member)
            if isinstance(loaded, dict):
                info_data = loaded
            else:
                notes.append("Info.json was not an object.")
        except Exception as exc:
            notes.append(f"Info.json could not be parsed: {exc}")

    if definition_member is not None:
        try:
            loaded = read_zip_json(archive, definition_member)
            if isinstance(loaded, dict):
                definition_data = loaded
            else:
                notes.append("Definition.json was not an object.")
        except Exception as exc:
            notes.append(f"Definition.json could not be parsed: {exc}")

    source_folder = root[-1] if root and root[-1].lower() != "mods" else zip_path.stem
    member_count = sum(1 for parts in files.values() if starts_with(parts, root))

    if info_member is not None:
        package_id = string_field(info_data, "Id", "id") or safe_folder_name(source_folder)
        display_name = string_field(info_data, "DisplayName", "Name", "name") or package_id
        version = string_field(info_data, "Version", "version")
        kind = "fuse-data" if has_fuse_data_marker(info_data) else "umm"
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
        )

    if definition_member is not None and looks_like_legacy_data(archive, files, root):
        legacy_id = string_field(definition_data, "id", "Id") or safe_folder_name(source_folder)
        package_id = ensure_fuse_id(legacy_id)
        display_name = string_field(definition_data, "name", "DisplayName", "Name") or legacy_id
        version = string_field(definition_data, "version", "Version")
        return ZipPackage(
            zip_path=zip_path,
            root=root,
            kind="legacy-data",
            package_id=package_id,
            display_name=display_name,
            version=version,
            install_name=safe_folder_name(package_id, safe_folder_name(source_folder)),
            manifest_member=definition_member.filename,
            member_count=member_count,
            notes=notes,
        )

    return None


def inspect_zip(zip_path: Path) -> tuple[list[ZipPackage], list[str]]:
    packages: list[ZipPackage] = []
    warnings: list[str] = []
    try:
        with zipfile.ZipFile(zip_path, "r") as archive:
            files = zip_file_parts(archive)
            roots = candidate_roots(files)
            for root in roots:
                package = package_from_root(zip_path, archive, files, root)
                if package is None:
                    label = "/".join(root) if root else "."
                    warnings.append(f"{zip_path.name}: unsupported package root '{label}'.")
                    continue
                packages.append(package)
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

    return InstallResult(package, "installed", destination, "", backup)


def default_game_dir() -> Path:
    if getattr(sys, "frozen", False):
        return Path(sys.executable).resolve().parent
    return Path.cwd().resolve()


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


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description=(
            "Install FUSE and mod packages. Run it with no arguments to install "
            "the bundled FUSE framework; drag mod .zip files onto it (or pass them "
            "as arguments) to install those mods."
        ),
    )
    parser.add_argument("zips", nargs="*", help="Zip files to install. Drag-and-drop works on Windows.")
    parser.add_argument("--game-dir", default=None, help="Base folder. Default is this exe's folder, or the current directory when run as a script.")
    parser.add_argument("--mods-dir", default=None, help="Mods folder. Default is <base folder>\\Mods.")
    parser.add_argument("--inbox", default=None, help="Folder to scan for zip files when no zip arguments are passed.")
    parser.add_argument("--replace", action="store_true", help="Backup and replace existing mod folders.")
    parser.add_argument("--no-fuse", action="store_true", help="Do not install the bundled FUSE framework on a manual run; only process zip files.")
    parser.add_argument("--dry-run", action="store_true", help="Inspect zips and print planned installs without writing files.")
    parser.add_argument("--archive-zips", action="store_true", help="Move successfully processed zips to Mods\\FUSEInstaller\\InstalledZips.")
    parser.add_argument("--pause", action="store_true", help="Pause before closing.")
    parser.add_argument("--no-pause", action="store_true", help="Do not pause before closing, even when bundled as an exe.")
    parser.add_argument("--version", action="version", version=f"FUSE Installer {TOOL_VERSION}")
    return parser


def run(args: argparse.Namespace) -> int:
    game_dir = Path(args.game_dir).resolve() if args.game_dir else default_game_dir()
    mods_dir = Path(args.mods_dir).resolve() if args.mods_dir else (game_dir / "Mods").resolve()

    explicit = bool(args.zips)
    zips = find_input_zips(args, game_dir)

    # Manual run (no zip arguments): install the FUSE framework bundled into the
    # exe, alongside any loose zips found beside it. Dragging specific zips onto
    # the exe installs exactly those and never force-installs FUSE.
    bundle_on_disk = resolve_bundled_fuse()
    bundled_fuse = bundle_on_disk if (not explicit and not args.no_fuse) else None

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
    if args.replace:
        print("replace: enabled")
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

    all_results: list[InstallResult] = []
    scan_failures = 0
    for zip_path in targets:
        label = "FUSE" if zip_path == bundled_fuse else "ZIP"
        print(f"{label}: {zip_path}")
        if not zip_path.exists():
            print("  error: file does not exist")
            scan_failures += 1
            continue

        packages, warnings = inspect_zip(zip_path)
        for warning in warnings:
            print(f"  warning: {warning}")
        if not packages:
            scan_failures += 1
            continue

        for package in packages:
            print_package(package)
            try:
                result = install_package(package, mods_dir, args.replace, args.dry_run)
                all_results.append(result)
                print_result(result)
            except Exception as exc:
                result = InstallResult(package, "failed", mods_dir / package.install_name, str(exc))
                all_results.append(result)
                print_result(result)
        print()

        zip_results = [result for result in all_results if result.package.zip_path == zip_path]
        # Never archive the bundled FUSE payload: it lives inside the PyInstaller
        # extraction dir, not beside the exe.
        if args.archive_zips and zip_path != bundled_fuse and zip_results and not any(result.status == "failed" for result in zip_results):
            archived_to = archive_zip(zip_path, mods_dir, args.dry_run)
            verb = "Would archive" if args.dry_run else "Archived"
            print(f"{verb}: {zip_path} -> {archived_to}")

    installed = sum(1 for result in all_results if result.status == "installed")
    skipped = sum(1 for result in all_results if result.status == "skipped")
    failed_results = sum(1 for result in all_results if result.status == "failed")
    failures = scan_failures + failed_results
    print(f"Summary: installed={installed} skipped={skipped} failed={failures}")
    return 1 if failures else 0


def should_pause(args: argparse.Namespace) -> bool:
    if args.no_pause:
        return False
    if args.pause:
        return True
    return bool(getattr(sys, "frozen", False) and sys.platform.startswith("win"))


def main() -> int:
    parser = build_parser()
    args = parser.parse_args()
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
