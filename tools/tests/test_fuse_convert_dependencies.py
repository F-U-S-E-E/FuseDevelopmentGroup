from __future__ import annotations

import json

import fuse_convert


def test_conditional_mixinto_requirement_adds_advisory_load_after(tmp_path):
    (tmp_path / "Definition.json").write_text(
        json.dumps(
            {
                "id": "Conditional.Mixinto",
                "mixintos": {
                    "game-graph": {
                        "mixinto": "file(optional.json)",
                        "requires": ["Optional.Base"],
                    }
                },
            }
        ),
        encoding="utf-8",
    )

    required, load_after = fuse_convert.legacy_dependencies(tmp_path)

    assert required == []
    assert load_after == ["Optional.Base.FUSE"]


def test_nested_mixinto_arrays_preserve_order_and_deduplicate(tmp_path):
    (tmp_path / "Definition.json").write_text(
        json.dumps(
            {
                "mixintos": {
                    "game-graph": [
                        {
                            "mixinto": "file(a.json)",
                            "requires": ["Base.A", "Base.Shared"],
                        },
                        {
                            "mixinto": "file(b.json)",
                            "requires": ["base.shared", "Base.B"],
                        },
                    ]
                }
            }
        ),
        encoding="utf-8",
    )

    required, load_after = fuse_convert.legacy_dependencies(tmp_path)

    assert required == []
    assert load_after == ["Base.A.FUSE", "Base.Shared.FUSE", "Base.B.FUSE"]


def test_conflicts_with_is_preserved_in_manifest_and_conditional_mixinto(tmp_path):
    (tmp_path / "Definition.json").write_text(
        json.dumps(
            {
                "conflictsWith": [
                    {"id": "Other.Route", "notBefore": "2.0", "notAfter": "3.0"},
                    "Zamu.StrangeCustoms",
                ],
                "mixintos": {
                    "game-graph": {
                        "mixinto": "file(optional.json)",
                        "conflictsWith": ["Conditional.Other"],
                    }
                },
            }
        ),
        encoding="utf-8",
    )

    manifest_conflicts = fuse_convert.legacy_conflicts_with(tmp_path)
    metadata, _ = fuse_convert.mixinto_metadata(tmp_path)

    assert manifest_conflicts == [
        {"Id": "Other.Route", "NotBefore": "2.0", "NotAfter": "3.0"},
        {"Id": "Zamu.StrangeCustoms"},
    ]
    assert metadata["optional.json"]["conflictsWith"] == [
        {"id": "Conditional.Other"}
    ]
