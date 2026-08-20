from __future__ import annotations

import json

import fuse_converter


def make_asset_pack(folder):
    folder.mkdir()
    (folder / "bundle").write_bytes(b"asset")
    (folder / "Catalog.json").write_text("{}", encoding="utf-8")
    (folder / "Definitions.json").write_text("{}", encoding="utf-8")


def test_asset_only_package_is_reported_for_install_without_fake_output(tmp_path):
    source = tmp_path / "Assets"
    make_asset_pack(source)
    out_root = tmp_path / "out"

    report = fuse_converter.convert_input(source, out_root, "auto", clean_output=True)

    assert report.status == "failed"
    assert report.detected_kind == "asset"
    assert not report.output.exists()
    report_file = out_root / "_conversion-reports" / "Assets.FUSE" / "conversion-report.json"
    assert report_file.exists()
    data = json.loads(report_file.read_text(encoding="utf-8"))
    assert "install this package" in data["entries"][0]["message"]


def test_map_tiles_and_code_are_not_reported_as_converted(tmp_path):
    tiles = tmp_path / "Tiles"
    tiles.mkdir()
    (tiles / "tile.data").write_bytes(b"tile")
    code = tmp_path / "Code"
    code.mkdir()
    (code / "Code.dll").write_bytes(b"assembly")
    schemas = code / "schemas"
    schemas.mkdir()
    (schemas / "example.json").write_text(
        json.dumps({"tracks": {"nodes": {}}}),
        encoding="utf-8",
    )

    assert fuse_converter.detect_kind(tiles, "auto") == "map_tile"
    assert fuse_converter.detect_kind(code, "auto") == "code"


def test_mixed_code_and_route_json_converts_the_data_portion(tmp_path):
    source = tmp_path / "Mixed"
    source.mkdir()
    (source / "Plugin.dll").write_bytes(b"assembly")
    (source / "tracks.json").write_text(
        json.dumps({"tracks": {"nodes": {"N1": {"position": {"x": 0, "y": 0, "z": 0}}}}}),
        encoding="utf-8",
    )

    assert fuse_converter.detect_kind(source, "auto") == "route"
