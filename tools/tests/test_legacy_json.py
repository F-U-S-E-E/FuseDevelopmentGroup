"""Tests for legacy_json, the tolerant JSON reader for legacy mod data."""

from __future__ import annotations

import json

import legacy_json


def test_plain_json_round_trips():
    assert legacy_json.loads('{"a": 1, "b": [2, 3]}') == {"a": 1, "b": [2, 3]}


def test_strip_line_and_block_comments():
    text = """
    {
        // a line comment
        "a": 1, /* inline block */
        "b": 2
        /* trailing
           block */
    }
    """
    assert legacy_json.loads(text) == {"a": 1, "b": 2}


def test_comment_markers_inside_strings_are_preserved():
    # A // or /* inside a string value must survive untouched.
    text = '{"url": "http://example.com/x", "path": "a/*not a comment*/b"}'
    assert legacy_json.loads(text) == {
        "url": "http://example.com/x",
        "path": "a/*not a comment*/b",
    }


def test_remove_trailing_commas_object_and_array():
    assert legacy_json.remove_trailing_commas('{"a": 1,}') == '{"a": 1}'
    assert legacy_json.remove_trailing_commas("[1, 2, 3, ]") == "[1, 2, 3]"


def test_trailing_commas_loads():
    assert legacy_json.loads('{"a": [1, 2, 3,], "b": 4,}') == {"a": [1, 2, 3], "b": 4}


def test_close_unbalanced_truncated_object():
    # Truncated after the last real entry, with no closing brace.
    repaired = legacy_json.close_unbalanced_json('{"id": "Trunc", "name": "T"')
    assert json.loads(repaired) == {"id": "Trunc", "name": "T"}


def test_close_unbalanced_nested_stack():
    repaired = legacy_json.close_unbalanced_json('{"a": {"b": [1, 2')
    assert json.loads(repaired) == {"a": {"b": [1, 2]}}


def test_loads_repairs_truncation_end_to_end():
    # Comments + trailing comma + truncation together, as seen in the wild.
    text = '{\n  // legacy\n  "id": "Trunc",\n  "tracks": [1, 2,'
    assert legacy_json.loads(text) == {"id": "Trunc", "tracks": [1, 2]}


def test_repair_disabled_raises_on_truncation():
    try:
        legacy_json.loads('{"id": "Trunc"', repair=False)
    except json.JSONDecodeError:
        pass
    else:
        raise AssertionError("expected JSONDecodeError with repair disabled")
