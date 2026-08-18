"""Tests for fuse_installer: zip inspection, package detection, and install.

These exercise the bug-prone logic (path safety, root detection, atomic
install/rollback) and the bundled-FUSE behavior that makes a manual run install
FUSE while a drag-and-drop run installs only the dropped mods.
"""

from __future__ import annotations

import json
import zipfile
from pathlib import Path

import pytest

import fuse_installer as fi


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
    assert pkg.kind == "legacy-data"
    assert pkg.package_id == "OldPack.FUSE"


def test_inspect_unsupported_zip_warns(tmp_path):
    zip_path = make_zip(tmp_path / "z.zip", {"random/file.txt": "nothing here"})
    packages, warnings = fi.inspect_zip(zip_path)
    assert packages == []
    assert warnings


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
    assert result.status == "installed"
    assert (dest / "new.txt").read_text() == "new content"
    assert not (dest / "old.txt").exists()  # replaced, not merged
    assert result.backup is not None
    assert (result.backup / "old.txt").read_text() == "old content"


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


def run_installer(argv):
    args = fi.build_parser().parse_args(argv)
    return fi.run(args)


def test_run_no_args_installs_bundled_fuse(tmp_path, monkeypatch):
    game = tmp_path / "game"
    game.mkdir()
    monkeypatch.setenv("FUSE_INSTALLER_BUNDLED_ZIP", str(make_fuse_payload(tmp_path)))

    rc = run_installer(["--no-pause", "--game-dir", str(game)])
    assert rc == 0
    assert (game / "Mods" / "FUSE" / "FUSE.dll").exists()


def test_run_drag_zip_installs_only_that_mod(tmp_path, monkeypatch):
    game = tmp_path / "game"
    game.mkdir()
    monkeypatch.setenv("FUSE_INSTALLER_BUNDLED_ZIP", str(make_fuse_payload(tmp_path)))
    mod = make_zip(tmp_path / "MyMod.zip", {"MyMod/Info.json": info(Id="MyMod")})

    rc = run_installer([str(mod), "--no-pause", "--game-dir", str(game)])
    assert rc == 0
    assert (game / "Mods" / "MyMod").exists()
    assert not (game / "Mods" / "FUSE").exists()  # drag-drop never force-installs FUSE


def test_run_no_fuse_flag_skips_bundled_fuse(tmp_path, monkeypatch):
    game = tmp_path / "game"
    game.mkdir()
    monkeypatch.setenv("FUSE_INSTALLER_BUNDLED_ZIP", str(make_fuse_payload(tmp_path)))

    rc = run_installer(["--no-fuse", "--no-pause", "--game-dir", str(game)])
    assert rc == 1  # nothing to do
    assert not (game / "Mods" / "FUSE").exists()
