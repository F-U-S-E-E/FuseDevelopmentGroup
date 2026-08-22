"""Tests for fuse_installer: zip inspection, package detection, and install.

These exercise the bug-prone logic (path safety, root detection, atomic
install/rollback) and the bundled-FUSE behavior that makes a manual run install
FUSE while a drag-and-drop run installs only the dropped mods.
"""

from __future__ import annotations

import argparse
import json
import zipfile
from pathlib import Path

import pytest

import fuse_installer as fi


@pytest.mark.parametrize(
    ("zips", "with_fuse", "no_fuse", "available", "expected"),
    [
        ([], False, False, True, True),
        (["mod.zip"], False, False, True, False),
        (["mod.zip"], True, False, True, True),
        ([], False, True, True, False),
        ([], False, False, False, False),
    ],
)
def test_should_preselect_bundled_fuse_matches_launch_scope(
    zips: list[str],
    with_fuse: bool,
    no_fuse: bool,
    available: bool,
    expected: bool,
) -> None:
    args = argparse.Namespace(zips=zips, with_fuse=with_fuse, no_fuse=no_fuse)

    assert fi.should_preselect_bundled_fuse(args, available) is expected


@pytest.mark.parametrize(
    ("screen", "expected"),
    [
        ((1920, 1080), (960, 760)),
        ((956, 768), (908, 704)),
        ((800, 600), (752, 536)),
        ((40, 40), (1, 1)),
    ],
)
def test_installer_window_size_stays_inside_screen_margin(
    screen: tuple[int, int],
    expected: tuple[int, int],
) -> None:
    assert fi.installer_window_size(*screen) == expected


def make_zip(path: Path, files: dict[str, str | bytes]) -> Path:
    """Write a zip whose members are given as archive-name -> contents."""
    with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED) as archive:
        for name, content in files.items():
            data = content.encode("utf-8") if isinstance(content, str) else content
            archive.writestr(name, data)
    return path


def info(**fields) -> str:
    return json.dumps(fields)


def tree(base: Path) -> list[str]:
    if not base.exists():
        return []
    return sorted(
        str(p.relative_to(base)).replace("\\", "/") for p in base.rglob("*") if p.is_file()
    )


# --------------------------------------------------------------------------- #
# Path safety
# --------------------------------------------------------------------------- #

def test_normalize_zip_parts_accepts_normal():
    assert fi.normalize_zip_parts("Mod/Info.json") == ("Mod", "Info.json")
    assert fi.normalize_zip_parts("Mod\\sub\\file.txt") == ("Mod", "sub", "file.txt")


@pytest.mark.parametrize(
    "name",
    [
        "../escape.txt",
        "Mod/../../escape.txt",
        "/abs/path.txt",
        "C:/drive/path.txt",
        "Mod/bad\x00name.txt",
        "Mod/na:me.txt",
        "",
        "dir/",
    ],
)
def test_normalize_zip_parts_rejects_unsafe(name):
    assert fi.normalize_zip_parts(name) is None


def test_ensure_inside_blocks_escape(tmp_path):
    root = tmp_path / "dest"
    root.mkdir()
    fi.ensure_inside(root / "ok" / "file.txt", root)  # no raise
    with pytest.raises(RuntimeError):
        fi.ensure_inside(tmp_path / "outside.txt", root)


# --------------------------------------------------------------------------- #
# Name and id helpers
# --------------------------------------------------------------------------- #

def test_safe_folder_name_sanitizes_and_trims():
    assert fi.safe_folder_name('a<b>c:"d"|e?f*g') == "a-b-c-d-e-f-g"
    assert fi.safe_folder_name("  ...spaces...  ") == "spaces"
    assert fi.safe_folder_name("") == "InstalledPackage"


def test_safe_folder_name_reserved_windows_name():
    assert fi.safe_folder_name("nul") == "nul-package"
    assert fi.safe_folder_name("con") == "con-package"


def test_ensure_fuse_id_appends_suffix():
    assert fi.ensure_fuse_id("MyPack") == "MyPack.FUSE"
    assert fi.ensure_fuse_id("Already.FUSE") == "Already.FUSE"
    assert fi.ensure_fuse_id("") == "LegacyDataPackage.FUSE"


def test_parse_steam_library_paths_supports_current_vdf_format():
    text = r'''
    "libraryfolders"
    {
        "0" { "path" "C:\\Program Files (x86)\\Steam" }
        "1" { "path" "E:\\Games\\SteamLibrary" }
    }
    '''
    assert fi.parse_steam_library_paths(text) == [
        Path(r"C:\Program Files (x86)\Steam"),
        Path(r"E:\Games\SteamLibrary"),
    ]


# --------------------------------------------------------------------------- #
# Root detection
# --------------------------------------------------------------------------- #

def test_candidate_roots_prefers_root_level_manifest(tmp_path):
    zip_path = make_zip(tmp_path / "z.zip", {"Info.json": info(Id="X"), "extra.txt": "x"})
    with zipfile.ZipFile(zip_path) as archive:
        roots = fi.candidate_roots(fi.zip_file_parts(archive))
    assert roots == [()]


def test_candidate_roots_multi_package(tmp_path):
    zip_path = make_zip(
        tmp_path / "z.zip",
        {
            "Mods/PkgA/Info.json": info(Id="PkgA"),
            "Mods/PkgB/Info.json": info(Id="PkgB"),
        },
    )
    with zipfile.ZipFile(zip_path) as archive:
        roots = fi.candidate_roots(fi.zip_file_parts(archive))
    assert ("Mods", "PkgA") in roots and ("Mods", "PkgB") in roots


# --------------------------------------------------------------------------- #
# Package detection
# --------------------------------------------------------------------------- #

def test_inspect_umm_package(tmp_path):
    zip_path = make_zip(
        tmp_path / "z.zip",
        {"MyMod/Info.json": info(Id="MyMod", DisplayName="My Mod", Version="1.2.3")},
    )
    packages, warnings = fi.inspect_zip(zip_path)
    assert warnings == []
    assert len(packages) == 1
    pkg = packages[0]
    assert pkg.kind == "umm"
    assert pkg.package_id == "MyMod"
    assert pkg.install_name == "MyMod"
    assert pkg.version == "1.2.3"


def test_inspect_fuse_data_package(tmp_path):
    zip_path = make_zip(
        tmp_path / "z.zip",
        {"Data/Info.json": info(Id="DataPack", Requirements=[{"Id": "FUSE"}])},
    )
    packages, _ = fi.inspect_zip(zip_path)
    assert packages[0].kind == "fuse-data"


def test_inspect_legacy_data_package(tmp_path):
    zip_path = make_zip(
        tmp_path / "z.zip",
        {
            "Legacy/Definition.json": info(id="OldPack", name="Old Pack"),
            "Legacy/world.json": json.dumps({"tracks": [{"id": 1}]}),
        },
    )
    packages, _ = fi.inspect_zip(zip_path)
    assert len(packages) == 1
    pkg = packages[0]
    assert pkg.kind == "railloader"
    assert pkg.package_id == "OldPack"


def test_inspect_code_only_railloader_package(tmp_path):
    zip_path = make_zip(
        tmp_path / "z.zip",
        {
            "SignalsEverywhere/Definition.json": info(
                manifestVersion=5,
                id="Joo.SignalsEverywhere",
                name="Signals Everywhere",
                assemblies=["SignalsEverywhere"],
            ),
            "SignalsEverywhere/SignalsEverywhere.dll": b"binary",
        },
    )
    packages, warnings = fi.inspect_zip(zip_path)
    assert warnings == []
    assert len(packages) == 1
    assert packages[0].kind == "railloader"
    assert packages[0].package_id == "Joo.SignalsEverywhere"
    assert packages[0].install_name == "Joo.SignalsEverywhere"


def test_inspect_unsupported_zip_warns(tmp_path):
    zip_path = make_zip(tmp_path / "z.zip", {"random/file.txt": "nothing here"})
    packages, warnings = fi.inspect_zip(zip_path)
    assert packages == []
    assert warnings


def test_inspect_malformed_manifest_reports_exact_member_and_location(tmp_path):
    zip_path = make_zip(
        tmp_path / "z.zip",
        {
            "Broken/Info.json": '{ "Id": "Broken", ',
            "Broken/track.fuse.json": "{}",
        },
    )
    packages, warnings = fi.inspect_zip(zip_path)
    assert warnings == []
    package = packages[0]
    assert package.kind == "invalid"
    assert package.errors
    assert "Broken/Info.json" in package.errors[0]
    assert "line 1" in package.errors[0]


def test_inspect_rejects_unsafe_archive_member_even_with_valid_package(tmp_path):
    zip_path = make_zip(
        tmp_path / "unsafe.zip",
        {
            "Good/Info.json": info(Id="Good"),
            "../outside.dll": b"unsafe",
        },
    )

    packages, warnings = fi.inspect_zip(zip_path)

    assert warnings == []
    assert packages[0].kind == "invalid"
    assert "unsafe member path" in packages[0].errors[0]
    assert "../outside.dll" in packages[0].errors[0]


def test_inspect_rejects_case_colliding_archive_members(tmp_path):
    zip_path = make_zip(
        tmp_path / "duplicate-members.zip",
        {
            "Good/Info.json": info(Id="Good"),
            "Good/Data.json": "{}",
            "Good/data.json": "{}",
        },
    )

    packages, _ = fi.inspect_zip(zip_path)

    assert packages[0].kind == "invalid"
    assert "case-colliding" in packages[0].errors[0]


def test_inspect_rejects_nested_manifest_layout_as_ambiguous(tmp_path):
    zip_path = make_zip(
        tmp_path / "ambiguous.zip",
        {
            "Info.json": info(Id="Outer"),
            "Nested/Info.json": info(Id="Inner"),
        },
    )

    packages, _ = fi.inspect_zip(zip_path)

    assert len(packages) == 1
    assert packages[0].kind == "invalid"
    assert "layout is ambiguous" in packages[0].errors[0]
    assert "Nested" in packages[0].errors[0]


def test_inspect_native_package_reports_missing_declared_file(tmp_path):
    zip_path = make_zip(
        tmp_path / "z.zip",
        {"Broken/Info.json": info(Id="Broken", FuseDataFiles=["missing.fuse.json"])},
    )
    packages, _ = fi.inspect_zip(zip_path)
    assert packages[0].kind == "invalid"
    assert "missing.fuse.json" in packages[0].errors[0]


def test_inspect_native_package_reports_malformed_data_file(tmp_path):
    zip_path = make_zip(
        tmp_path / "z.zip",
        {
            "Broken/Info.json": info(Id="Broken", FuseDataFiles=["data/track.fuse.json"]),
            "Broken/data/track.fuse.json": '{ "tracks": }',
        },
    )
    packages, _ = fi.inspect_zip(zip_path)
    assert packages[0].kind == "invalid"
    assert "Broken/data/track.fuse.json" in packages[0].errors[0]


# --------------------------------------------------------------------------- #
# Dependency and duplicate preflight
# --------------------------------------------------------------------------- #

def package(
    tmp_path: Path,
    package_id: str,
    *,
    version: str = "",
    requirements: list[fi.PackageRequirement] | None = None,
    zip_name: str | None = None,
) -> fi.ZipPackage:
    return fi.ZipPackage(
        zip_path=tmp_path / (zip_name or f"{package_id}.zip"),
        root=(package_id,),
        kind="umm",
        package_id=package_id,
        display_name=package_id,
        version=version,
        install_name=package_id,
        manifest_member=f"{package_id}/Info.json",
        member_count=1,
        requirements=requirements or [],
    )


def test_inspect_parses_umm_and_railloader_requirement_bounds(tmp_path):
    zip_path = make_zip(
        tmp_path / "requirements.zip",
        {
            "Mods/Native/Info.json": info(
                Id="Native",
                Requirements=[{"Id": "FUSE", "NotBefore": "1.2.0"}],
                FuseRequires=["Shared.Data"],
            ),
            "Mods/Legacy/Definition.json": info(
                manifestVersion=7,
                id="Legacy",
                requires=[{"id": "Legacy.Base", "notAfter": "2.0.0"}],
            ),
        },
    )

    packages, warnings = fi.inspect_zip(zip_path)

    assert warnings == []
    by_id = {item.package_id: item for item in packages}
    assert by_id["Native"].requirements == [
        fi.PackageRequirement("FUSE", not_before="1.2.0"),
        fi.PackageRequirement("Shared.Data"),
    ]
    assert by_id["Legacy"].requirements == [
        fi.PackageRequirement("Legacy.Base", not_after="2.0.0")
    ]


def test_inspect_parses_umm_version_suffix_without_changing_package_id(tmp_path):
    zip_path = make_zip(
        tmp_path / "gp38.zip",
        {
            "GP38/Info.json": info(
                Id="GP38ATSF",
                Requirements=["GP38SoundMod-4.4.1"],
            ),
        },
    )

    packages, warnings = fi.inspect_zip(zip_path)

    assert warnings == []
    assert packages[0].requirements == [
        fi.PackageRequirement("GP38SoundMod", not_before="4.4.1")
    ]


def test_nexus_dependency_enrichment_requires_manifest_url_and_unique_version(tmp_path, monkeypatch):
    package = fi.ZipPackage(
        zip_path=tmp_path / "water-car.zip",
        root=("WaterCar",),
        kind="umm",
        package_id="WaterCar",
        display_name="Water Car",
        version="1.0",
        install_name="WaterCar",
        manifest_member="WaterCar/Info.json",
        member_count=1,
        homepage="https://www.nexusmods.com/games/railroader/mods/503",
    )

    def fake_get(path, api_key, timeout=15.0):
        assert api_key == "secret"
        if path == "games/railroader/mods/503":
            return {"data": {"id": "internal-source"}}
        if path == "mods/internal-source/files":
            return {"data": {"mod_files": [{"id": "file-a"}]}}
        if path == "mod-files/file-a/versions":
            return {"data": {"versions": [{"id": "version-a", "version": "1.0.0", "is_primary": True}]}}
        if path == "mod-file-versions/version-a/dependencies/ranges":
            return {
                "dependency_definitions": [{
                    "ranges": [{
                        "target_mod_file": {"mod": {"game_scoped_id": "712", "name": "Shared Scripts"}},
                        "min_version": {"version": "2.2.0"},
                        "max_version": None,
                    }],
                }],
                "dlc_dependency_definitions": [],
            }
        raise AssertionError(path)

    monkeypatch.setattr(fi, "nexus_api_get", fake_get)

    assert fi.enrich_package_from_nexus(package, "secret") is True
    assert package.requirements == [fi.PackageRequirement(
        "nexus:railroader:712",
        not_before="2.2.0",
        display_name="Shared Scripts",
        nexus_mod_id="712",
        source="nexus",
    )]
    assert package.nexus_source["gameScopedModId"] == "503"
    assert package.nexus_source["modFileVersionId"] == "version-a"


def test_dependency_metadata_cache_is_offline_and_preserves_other_packages(tmp_path):
    mods = tmp_path / "Mods"
    existing_dir = mods / fi.DEPENDENCY_METADATA_DIR
    existing_dir.mkdir(parents=True)
    cache = existing_dir / fi.DEPENDENCY_METADATA_FILE
    cache.write_text(json.dumps({
        "schemaVersion": 1,
        "packages": [{"folder": "Existing", "id": "Existing"}],
    }), encoding="utf-8")
    package = fi.ZipPackage(
        zip_path=tmp_path / "car.zip",
        root=("Car",),
        kind="umm",
        package_id="Car",
        display_name="Rail Car",
        version="1.0.0",
        install_name="Car",
        manifest_member="Car/Info.json",
        member_count=1,
        requirements=[fi.PackageRequirement("Scripts", not_before="2.0.0")],
    )
    result = fi.InstallResult(package, "installed", mods / "Car")

    path = fi.write_dependency_metadata_cache(mods, [result], dry_run=False)

    data = json.loads(path.read_text(encoding="utf-8"))
    by_folder = {item["folder"]: item for item in data["packages"]}
    assert set(by_folder) == {"Car", "Existing"}
    assert by_folder["Car"]["requirements"][0]["id"] == "Scripts"
    assert by_folder["Car"]["requirements"][0]["minimumVersion"] == "2.0.0"


def test_dependency_preflight_reports_missing_id_bounds_and_requester(tmp_path):
    dependent = package(
        tmp_path,
        "Dependent",
        requirements=[fi.PackageRequirement("Missing.Base", not_before="2.4")],
    )

    fi.validate_batch_dependencies([dependent], tmp_path / "Mods", fuse_available=False)

    assert len(dependent.errors) == 1
    assert "Missing.Base" in dependent.errors[0]
    assert "not before 2.4" in dependent.errors[0]
    assert "Dependent" in dependent.errors[0]


def test_dependency_preflight_accepts_forward_reference_in_same_batch(tmp_path):
    dependent = package(
        tmp_path,
        "Dependent",
        requirements=[fi.PackageRequirement("Base", not_before="1.5")],
    )
    base = package(tmp_path, "Base.FUSE", version="2.0", zip_name="later.zip")

    fi.validate_batch_dependencies([dependent, base], tmp_path / "Mods", fuse_available=False)

    assert dependent.errors == []
    assert base.errors == []


def test_dependency_preflight_uses_installed_package_version(tmp_path):
    mods = tmp_path / "Mods"
    installed = mods / "Base"
    installed.mkdir(parents=True)
    (installed / "Info.json").write_text(
        info(Id="Base", Version="2.3.0"),
        encoding="utf-8",
    )
    dependent = package(
        tmp_path,
        "Dependent",
        requirements=[fi.PackageRequirement("Base", not_before="2.0", not_after="3.0")],
    )

    fi.validate_batch_dependencies([dependent], mods, fuse_available=False)

    assert dependent.errors == []


@pytest.mark.parametrize(
    ("available", "requirement"),
    [
        ("1.9", fi.PackageRequirement("Base", not_before="2.0")),
        ("3.1", fi.PackageRequirement("Base", not_after="3.0")),
    ],
)
def test_dependency_preflight_rejects_incompatible_versions(tmp_path, available, requirement):
    base = package(tmp_path, "Base", version=available)
    dependent = package(tmp_path, "Dependent", requirements=[requirement])

    fi.validate_batch_dependencies([base, dependent], tmp_path / "Mods", fuse_available=False)

    assert len(dependent.errors) == 1
    assert "Dependency version conflict" in dependent.errors[0]
    assert available in dependent.errors[0]


def test_dependency_preflight_uses_only_explicit_fuse_replacements(tmp_path):
    replaced = package(
        tmp_path,
        "ReplacedDependent",
        requirements=[fi.PackageRequirement("Zamu.StrangeCustoms.FUSE", not_before="99.0")],
    )
    still_required = package(
        tmp_path,
        "HostedDependent",
        requirements=[fi.PackageRequirement("Zamu.SomeKindOfMadness")],
    )

    fi.validate_batch_dependencies(
        [replaced, still_required],
        tmp_path / "Mods",
        fuse_available=True,
    )

    assert replaced.errors == []
    assert len(still_required.errors) == 1
    assert "Zamu.SomeKindOfMadness" in still_required.errors[0]


def test_dependency_preflight_rejects_duplicate_package_ids(tmp_path):
    first = package(tmp_path, "Same.Id", zip_name="first.zip")
    second = package(tmp_path, "Same.Id.FUSE", zip_name="second.zip")

    fi.validate_batch_dependencies([first, second], tmp_path / "Mods", fuse_available=False)

    assert "first.zip" in first.errors[0]
    assert "second.zip" in first.errors[0]
    assert len(second.errors) == 1


def test_dependency_preflight_propagates_failed_batch_dependency(tmp_path):
    top = package(
        tmp_path,
        "Top",
        requirements=[fi.PackageRequirement("Middle")],
    )
    middle = package(
        tmp_path,
        "Middle",
        requirements=[fi.PackageRequirement("Missing.Bottom")],
    )

    fi.validate_batch_dependencies([top, middle], tmp_path / "Mods", fuse_available=False)

    assert "Missing.Bottom" in middle.errors[0]
    assert "failed preflight" in top.errors[0]


def test_failed_fuse_package_cannot_supply_replacement_dependency(tmp_path):
    dependent = package(
        tmp_path,
        "Dependent",
        requirements=[fi.PackageRequirement("Zamu.StrangeCustoms")],
    )
    broken_fuse = package(
        tmp_path,
        "FUSE",
        requirements=[fi.PackageRequirement("Missing.Framework.Dependency")],
    )

    fi.validate_batch_dependencies(
        [dependent, broken_fuse],
        tmp_path / "Mods",
        fuse_available=False,
    )

    assert "Missing.Framework.Dependency" in broken_fuse.errors[0]
    assert "Install or update FUSE" in dependent.errors[0]


# --------------------------------------------------------------------------- #
# Install / skip / replace / dry-run
# --------------------------------------------------------------------------- #

def install_one(zip_path: Path, mods_dir: Path, replace=False, dry_run=False) -> fi.InstallResult:
    packages, _ = fi.inspect_zip(zip_path)
    assert len(packages) == 1
    return fi.install_package(packages[0], mods_dir, replace=replace, dry_run=dry_run)


def test_install_writes_files(tmp_path):
    mods = tmp_path / "Mods"
    zip_path = make_zip(
        tmp_path / "z.zip",
        {"MyMod/Info.json": info(Id="MyMod"), "MyMod/mod.dll": b"\x00binary"},
    )
    result = install_one(zip_path, mods)
    assert result.status == "installed"
    assert (mods / "MyMod" / "Info.json").exists()
    assert (mods / "MyMod" / "mod.dll").read_bytes() == b"\x00binary"


def test_install_skips_existing(tmp_path):
    mods = tmp_path / "Mods"
    zip_path = make_zip(tmp_path / "z.zip", {"MyMod/Info.json": info(Id="MyMod")})
    install_one(zip_path, mods)
    result = install_one(zip_path, mods)
    assert result.status == "skipped"


def test_replace_backs_up_then_installs(tmp_path):
    mods = tmp_path / "Mods"
    dest = mods / "MyMod"
    dest.mkdir(parents=True)
    (dest / "old.txt").write_text("old content")

    zip_path = make_zip(
        tmp_path / "z.zip",
        {"MyMod/Info.json": info(Id="MyMod"), "MyMod/new.txt": "new content"},
    )
    result = install_one(zip_path, mods, replace=True)
    assert (dest / "new.txt").read_text() == "new content"
    assert not (dest / "old.txt").exists()  # replaced, not merged
    assert result.backup is not None
    assert (result.backup / "old.txt").read_text() == "old content"
    assert result.status == "updated"


def test_game_preflight_and_legacy_loader_detection(tmp_path):
    game = tmp_path / "Railroader"
    managed = game / "Railroader_Data" / "Managed"
    umm = managed / "UnityModManager"
    umm.mkdir(parents=True)
    (game / "Railroader.exe").write_bytes(b"exe")
    (umm / "UnityModManager.dll").write_bytes(b"umm")
    conflicts = [managed / name for name in fi.LEGACY_MANAGED_FILES]
    for conflict in conflicts:
        conflict.write_bytes(b"old")

    assert fi.validate_game_dir(game) == []
    assert fi.unity_mod_manager_installed(game)
    assert fi.find_legacy_managed_files(game) == conflicts

    moved = fi.backup_legacy_managed_files(conflicts, game / "Mods", dry_run=False)
    assert len(moved) == len(conflicts)
    assert all(not conflict.exists() for conflict in conflicts)
    assert all(destination.read_bytes() == b"old" for _, destination in moved)


def test_assetloader_runtime_detection_backup_and_dependency_alias(tmp_path):
    mods = tmp_path / "Mods"
    old = mods / "AssetLoader"
    old.mkdir(parents=True)
    (old / "Info.json").write_text(info(Id="AssetLoader"), encoding="utf-8")
    (old / "AssetLoader.dll").write_bytes(b"old-runtime")
    old_zip = mods / "AssetLoader.zip"
    old_zip.write_bytes(b"old-archive")

    assert set(fi.find_legacy_assetloader_paths(mods)) == {old, old_zip}

    moved = fi.backup_legacy_assetloader_paths(
        fi.find_legacy_assetloader_paths(mods),
        mods,
        dry_run=False,
    )
    assert len(moved) == 2
    assert not old.exists()
    assert not old_zip.exists()

    alias = fi.install_assetloader_compatibility_alias(mods, dry_run=False)
    manifest = json.loads((alias / "Info.json").read_text(encoding="utf-8"))
    assert manifest["Id"] == "AssetLoader"
    assert manifest["Requirements"] == ["FUSE"]
    assert manifest["FuseProvidedCompatibility"] == fi.ASSETLOADER_COMPATIBILITY_MARKER
    assert not (alias / "AssetLoader.dll").exists()
    assert fi.find_legacy_assetloader_paths(mods) == []
    assert fi.find_fuse_assetloader_compatibility(mods) == alias


def test_legacy_umm_startup_dependency_repair_backs_up_and_orders_after_fuse(tmp_path):
    mods = tmp_path / "Mods"
    alina = mods / "AlinasUtils"
    alina.mkdir(parents=True)
    original = {
        "Id": "AlinaNova21.AlinasUtils",
        "AssemblyName": "AlinasUtils.dll",
        "EntryMethod": "AlinasUtils.UMM.Mod.Load",
    }
    (alina / "Info.json").write_text(json.dumps(original), encoding="utf-8")
    (alina / "AlinasUtils.dll").write_bytes(
        b"CLR metadata before Railloader.Interchange after"
    )

    actions = fi.repair_legacy_umm_startup_dependencies(mods, dry_run=False)

    assert len(actions) == 1
    assert actions[0].component == "AlinaNova21.AlinasUtils"
    assert actions[0].status == "updated"
    repaired = json.loads((alina / "Info.json").read_text(encoding="utf-8"))
    assert repaired["Requirements"] == ["FUSE"]
    assert repaired["LoadAfter"] == ["FUSE"]
    assert actions[0].destination is not None
    backup = json.loads(actions[0].destination.read_text(encoding="utf-8"))
    assert backup == original


def test_legacy_umm_startup_dependency_repair_preserves_existing_dependency_shapes(tmp_path):
    mods = tmp_path / "Mods"
    plugin = mods / "SignalsEverywhere"
    plugin.mkdir(parents=True)
    manifest = {
        "Id": "Joo.SignalsEverywhere",
        "AssemblyName": "SignalsEverywhere.dll",
        "Requirements": [{"Id": "SomeLibrary"}],
        "LoadAfter": "OtherMod",
    }
    (plugin / "info.json").write_text(json.dumps(manifest), encoding="utf-8")
    (plugin / "SignalsEverywhere.dll").write_bytes(b"xxStrangeCustomsxx")

    actions = fi.repair_legacy_umm_startup_dependencies(mods, dry_run=False)

    assert len(actions) == 1
    repaired = json.loads((plugin / "info.json").read_text(encoding="utf-8"))
    assert repaired["Requirements"] == [{"Id": "SomeLibrary"}, "FUSE"]
    assert repaired["LoadAfter"] == ["OtherMod", "FUSE"]


def test_legacy_umm_startup_dependency_repair_skips_unrelated_and_already_ordered_mods(tmp_path):
    mods = tmp_path / "Mods"
    unrelated = mods / "Unrelated"
    unrelated.mkdir(parents=True)
    (unrelated / "Info.json").write_text(
        info(Id="Unrelated", AssemblyName="Unrelated.dll"), encoding="utf-8"
    )
    (unrelated / "Unrelated.dll").write_bytes(b"ordinary code")

    ready = mods / "Ready"
    ready.mkdir(parents=True)
    (ready / "Info.json").write_text(
        info(
            Id="Ready",
            AssemblyName="Ready.dll",
            Requirements=[{"Id": "FUSE"}],
            LoadAfter=["FUSE"],
        ),
        encoding="utf-8",
    )
    (ready / "Ready.dll").write_bytes(b"Railloader.Interchange")

    assert fi.find_legacy_umm_startup_dependency_manifests(mods) == []
    assert fi.repair_legacy_umm_startup_dependencies(mods, dry_run=False) == []
    assert not (mods / "ModBackups").exists()


def test_install_name_colliding_with_staging_dir(tmp_path):
    # A package whose id is "FUSEInstaller" must still install to Mods/FUSEInstaller
    # and not collide with the installer's internal staging directory.
    mods = tmp_path / "Mods"
    zip_path = make_zip(
        tmp_path / "z.zip",
        {"FUSEInstaller/Info.json": info(Id="FUSEInstaller"), "FUSEInstaller/x.txt": "x"},
    )
    result = install_one(zip_path, mods)
    assert result.status == "installed"
    assert (mods / "FUSEInstaller" / "Info.json").exists()
    assert (mods / "FUSEInstaller" / "x.txt").read_text() == "x"


def test_dry_run_writes_nothing(tmp_path):
    mods = tmp_path / "Mods"
    zip_path = make_zip(tmp_path / "z.zip", {"MyMod/Info.json": info(Id="MyMod")})
    result = install_one(zip_path, mods, dry_run=True)
    assert result.status == "installed"
    assert not mods.exists()


def test_install_report_records_each_package_result(tmp_path):
    game = tmp_path / "game"
    mods = game / "Mods"
    package = fi.ZipPackage(
        zip_path=tmp_path / "one.zip",
        root=("One",),
        kind="umm",
        package_id="One",
        display_name="One Mod",
        version="1.0",
        install_name="One",
        manifest_member="One/Info.json",
        member_count=2,
    )
    result = fi.InstallResult(package, "installed", mods / "One")

    path = fi.write_install_report(game, mods, [package.zip_path], [result], 0, dry_run=False)

    assert path is not None and path.exists()
    data = json.loads(path.read_text(encoding="utf-8"))
    assert data["summary"] == {"installed": 1, "updated": 0, "skipped": 0, "failed": 0}
    assert data["packages"][0]["id"] == "One"
    assert data["packages"][0]["status"] == "installed"
    assert data["compatibilityActions"] == []


def test_install_report_records_compatibility_failure(tmp_path):
    game = tmp_path / "game"
    mods = game / "Mods"
    action = fi.CompatibilityAction(
        component="AssetLoader",
        status="failed",
        message="Old runtime remains installed.",
        source=mods / "AssetLoader",
    )

    path = fi.write_install_report(
        game,
        mods,
        [],
        [],
        0,
        dry_run=False,
        compatibility_actions=[action],
    )

    assert path is not None and path.exists()
    data = json.loads(path.read_text(encoding="utf-8"))
    assert data["summary"]["failed"] == 1
    assert data["compatibilityActions"] == [{
        "component": "AssetLoader",
        "status": "failed",
        "message": "Old runtime remains installed.",
        "source": str(mods / "AssetLoader"),
        "destination": None,
    }]


# --------------------------------------------------------------------------- #
# Atomic extraction / rollback
# --------------------------------------------------------------------------- #

def _fail_on_second_write(monkeypatch):
    """Let the first member write, then fail — simulating a mid-extract error."""
    real = fi.shutil.copyfileobj
    calls = {"n": 0}

    def flaky(src, dst, *a, **k):
        calls["n"] += 1
        if calls["n"] >= 2:
            raise RuntimeError("simulated write failure")
        return real(src, dst, *a, **k)

    monkeypatch.setattr(fi.shutil, "copyfileobj", flaky)


def test_failed_fresh_install_leaves_no_partial_folder(tmp_path, monkeypatch):
    mods = tmp_path / "Mods"
    zip_path = make_zip(
        tmp_path / "z.zip",
        {"MyMod/Info.json": info(Id="MyMod"), "MyMod/a.txt": "a", "MyMod/b.txt": "b"},
    )
    packages, _ = fi.inspect_zip(zip_path)
    _fail_on_second_write(monkeypatch)

    with pytest.raises(RuntimeError):
        fi.install_package(packages[0], mods, replace=False, dry_run=False)

    # No half-written mod folder for the game to load, and no staging leftovers.
    assert not (mods / "MyMod").exists()
    assert tree(mods) == []


def test_failed_replace_preserves_existing_install(tmp_path, monkeypatch):
    mods = tmp_path / "Mods"
    dest = mods / "MyMod"
    dest.mkdir(parents=True)
    (dest / "keep.txt").write_text("original")

    zip_path = make_zip(
        tmp_path / "z.zip",
        {"MyMod/Info.json": info(Id="MyMod"), "MyMod/a.txt": "a", "MyMod/b.txt": "b"},
    )
    packages, _ = fi.inspect_zip(zip_path)
    _fail_on_second_write(monkeypatch)

    with pytest.raises(RuntimeError):
        fi.install_package(packages[0], mods, replace=True, dry_run=False)

    # The existing install must survive a failed reinstall untouched.
    assert (dest / "keep.txt").read_text() == "original"


# --------------------------------------------------------------------------- #
# Bundled FUSE resolution
# --------------------------------------------------------------------------- #

def test_resolve_bundled_fuse_env_override(tmp_path, monkeypatch):
    payload = make_zip(tmp_path / "FUSE.zip", {"FUSE/Info.json": info(Id="FUSE")})
    monkeypatch.setenv("FUSE_INSTALLER_BUNDLED_ZIP", str(payload))
    assert fi.resolve_bundled_fuse() == payload.resolve()


def test_resolve_bundled_fuse_absent(tmp_path, monkeypatch):
    monkeypatch.delenv("FUSE_INSTALLER_BUNDLED_ZIP", raising=False)
    monkeypatch.setattr(fi, "SCRIPT_DIR", tmp_path)  # no sibling bundled_fuse.zip
    monkeypatch.delattr(fi.sys, "_MEIPASS", raising=False)
    assert fi.resolve_bundled_fuse() is None


# --------------------------------------------------------------------------- #
# End-to-end run(): the two headline behaviors
# --------------------------------------------------------------------------- #

def make_fuse_payload(tmp_path: Path) -> Path:
    return make_zip(
        tmp_path / "FUSE-vX.zip",
        {"FUSE/Info.json": info(Id="FUSE", DisplayName="FUSE"), "FUSE/FUSE.dll": b"dll"},
    )


def make_game_dir(path: Path) -> Path:
    managed = path / "Railroader_Data" / "Managed" / "UnityModManager"
    managed.mkdir(parents=True)
    (path / "Railroader.exe").write_bytes(b"exe")
    (managed / "UnityModManager.dll").write_bytes(b"umm")
    return path


def run_installer(argv):
    args = fi.build_parser().parse_args(argv)
    return fi.run(args)


def test_run_no_args_installs_bundled_fuse(tmp_path, monkeypatch):
    game = make_game_dir(tmp_path / "game")
    monkeypatch.setenv("FUSE_INSTALLER_BUNDLED_ZIP", str(make_fuse_payload(tmp_path)))

    rc = run_installer(["--no-pause", "--game-dir", str(game)])
    assert rc == 0
    assert (game / "Mods" / "FUSE" / "FUSE.dll").exists()
    alias = game / "Mods" / "AssetLoader" / "Info.json"
    assert alias.exists()
    assert json.loads(alias.read_text(encoding="utf-8"))["Requirements"] == ["FUSE"]


def test_run_migrates_old_assetloader_when_fuse_is_installed(tmp_path, monkeypatch):
    game = make_game_dir(tmp_path / "game")
    old = game / "Mods" / "AssetLoader"
    old.mkdir(parents=True)
    (old / "Info.json").write_text(info(Id="AssetLoader"), encoding="utf-8")
    (old / "AssetLoader.dll").write_bytes(b"old-runtime")
    monkeypatch.setenv("FUSE_INSTALLER_BUNDLED_ZIP", str(make_fuse_payload(tmp_path)))

    rc = run_installer([
        "--no-pause",
        "--repair-asset-loader",
        "--game-dir",
        str(game),
    ])

    assert rc == 0
    assert (game / "Mods" / "FUSE" / "FUSE.dll").exists()
    assert (game / "Mods" / "AssetLoader" / "Info.json").exists()
    assert not (game / "Mods" / "AssetLoader" / "AssetLoader.dll").exists()
    backups = list((game / "Mods" / "ModBackups" / "FUSEInstaller").rglob("AssetLoader.dll"))
    assert len(backups) == 1
    reports = list((game / "Mods" / "FUSEInstaller" / "Reports").glob("install-*.json"))
    assert len(reports) == 1
    report = json.loads(reports[0].read_text(encoding="utf-8"))
    statuses = [item["status"] for item in report["compatibilityActions"]]
    assert statuses == ["backed-up", "installed"]
    assert report["summary"]["failed"] == 0


def test_run_rejects_old_assetloader_package_when_fuse_is_unavailable(tmp_path, monkeypatch):
    game = make_game_dir(tmp_path / "game")
    monkeypatch.delenv("FUSE_INSTALLER_BUNDLED_ZIP", raising=False)
    old_assetloader = make_zip(
        tmp_path / "AssetLoader.zip",
        {
            "AssetLoader/Info.json": info(Id="AssetLoader"),
            "AssetLoader/AssetLoader.dll": b"old-runtime",
        },
    )

    rc = run_installer([
        str(old_assetloader),
        "--no-pause",
        "--archive-zips",
        "--game-dir",
        str(game),
    ])

    assert rc == 1
    assert old_assetloader.exists()
    assert not (game / "Mods" / "AssetLoader").exists()
    reports = list((game / "Mods" / "FUSEInstaller" / "Reports").glob("install-*.json"))
    assert len(reports) == 1
    report = json.loads(reports[0].read_text(encoding="utf-8"))
    assert report["packages"][0]["status"] == "failed"
    assert "FUSE is not installed" in report["packages"][0]["message"]


def test_run_drag_zip_installs_only_that_mod(tmp_path, monkeypatch):
    game = make_game_dir(tmp_path / "game")
    monkeypatch.setenv("FUSE_INSTALLER_BUNDLED_ZIP", str(make_fuse_payload(tmp_path)))
    mod = make_zip(tmp_path / "MyMod.zip", {"MyMod/Info.json": info(Id="MyMod")})

    rc = run_installer([str(mod), "--no-pause", "--game-dir", str(game)])
    assert rc == 0
    assert (game / "Mods" / "MyMod").exists()
    assert not (game / "Mods" / "FUSE").exists()  # drag-drop never force-installs FUSE


def test_run_multi_package_zip_isolates_bad_json_and_installs_good_package(tmp_path, monkeypatch):
    game = make_game_dir(tmp_path / "game")
    monkeypatch.delenv("FUSE_INSTALLER_BUNDLED_ZIP", raising=False)
    bundle = make_zip(
        tmp_path / "Mods.zip",
        {
            "Mods/Good/Info.json": info(Id="Good"),
            "Mods/Good/Good.dll": b"good",
            "Mods/Bad/Info.json": '{ "Id": "Bad", ',
            "Mods/Bad/bad.fuse.json": "{}",
        },
    )

    rc = run_installer([str(bundle), "--no-pause", "--game-dir", str(game)])

    assert rc == 1
    assert (game / "Mods" / "Good" / "Good.dll").exists()
    assert not (game / "Mods" / "Bad").exists()


def test_run_no_fuse_flag_skips_bundled_fuse(tmp_path, monkeypatch):
    game = make_game_dir(tmp_path / "game")
    monkeypatch.setenv("FUSE_INSTALLER_BUNDLED_ZIP", str(make_fuse_payload(tmp_path)))

    rc = run_installer(["--no-fuse", "--no-pause", "--game-dir", str(game)])
    assert rc == 1  # nothing to do
    assert not (game / "Mods" / "FUSE").exists()
