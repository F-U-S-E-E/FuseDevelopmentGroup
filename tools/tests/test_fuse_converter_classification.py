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

    out_root = tmp_path / "out"
    tile_report = fuse_converter.convert_input(tiles, out_root, "auto", clean_output=True)
    code_report = fuse_converter.convert_input(code, out_root, "auto", clean_output=True)

    for report, expected_kind in ((tile_report, "map_tile"), (code_report, "code")):
        assert report.status == "failed"
        assert report.detected_kind == expected_kind
        assert report.errors > 0
        assert not report.output.exists()
        report_file = out_root / "_conversion-reports" / report.output.name / "conversion-report.json"
        assert report_file.exists()

    assert all(entry.concept != "script-binary" for entry in code_report.entries)


def test_unknown_package_uses_the_common_unsupported_error(tmp_path):
    source = tmp_path / "Unknown"
    source.mkdir()
    (source / "readme.txt").write_text("not convertible", encoding="utf-8")
    out_root = tmp_path / "out"

    report = fuse_converter.convert_input(source, out_root, "auto", clean_output=True)

    assert report.status == "failed"
    assert report.detected_kind == "unknown"
    assert any(entry.level == "ERROR" and entry.concept == "unsupported-package" for entry in report.entries)
    assert not report.output.exists()
    report_file = out_root / "_conversion-reports" / "Unknown.FUSE" / "conversion-report.json"
    assert report_file.exists()


def test_mixed_code_and_route_json_converts_the_data_portion(tmp_path):
    source = tmp_path / "Mixed"
    source.mkdir()
    (source / "Plugin.dll").write_bytes(b"assembly")
    (source / "tracks.json").write_text(
        json.dumps({"tracks": {"nodes": {"N1": {"position": {"x": 0, "y": 0, "z": 0}}}}}),
        encoding="utf-8",
    )

    out_root = tmp_path / "out"
    report = fuse_converter.convert_input(source, out_root, "auto", clean_output=True)

    assert report.detected_kind == "route"
    assert report.errors == 0
    assert report.output.exists()
    assert (report.output / "Info.json").exists()
    fragment_path = next(report.output.glob("*.fuse.json"))
    fragment = json.loads(fragment_path.read_text(encoding="utf-8"))
    assert len(fragment["tracks"]["nodes"]) == 1


def test_mixed_code_and_audio_json_is_detected_as_audio(tmp_path):
    source = tmp_path / "MixedAudio"
    source.mkdir()
    (source / "Plugin.dll").write_bytes(b"assembly")
    (source / "horns.json").write_text(
        json.dumps([{"layers": ["a.wav"]}]),
        encoding="utf-8",
    )

    assert fuse_converter.detect_kind(source, "auto") == "audio"
