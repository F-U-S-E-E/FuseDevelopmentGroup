#!/usr/bin/env python3
"""Small tolerant JSON reader for legacy Railroader mod data files.

Legacy Strange Customs/RailLoader packages in the wild often contain JSONC
comments, trailing commas, and occasionally files truncated after the last real
entry. This reader keeps the recovery narrow: it never executes code, it only
removes comments/trailing commas and, when requested, appends missing closing
brackets/braces based on the remaining structural stack.
"""

from __future__ import annotations

import json
import re
from pathlib import Path
from typing import Any


def read_json(path: Path, repair: bool = True) -> Any:
    text = Path(path).read_text(encoding="utf-8-sig")
    return loads(text, repair=repair)


def loads(text: str, repair: bool = True) -> Any:
    cleaned = strip_comments(text)
    cleaned = remove_trailing_commas(cleaned)
    if repair:
        cleaned = close_unbalanced_json(cleaned)
    return json.loads(cleaned)


def strip_comments(text: str) -> str:
    out: list[str] = []
    in_string = False
    escaped = False
    index = 0
    length = len(text)

    while index < length:
        char = text[index]

        if in_string:
            out.append(char)
            if escaped:
                escaped = False
            elif char == "\\":
                escaped = True
            elif char == '"':
                in_string = False
            index += 1
            continue

        if char == '"':
            in_string = True
            out.append(char)
            index += 1
            continue

        if char == "/" and index + 1 < length and text[index + 1] == "/":
            index += 2
            while index < length and text[index] not in "\r\n":
                index += 1
            continue

        if char == "/" and index + 1 < length and text[index + 1] == "*":
            index += 2
            while index + 1 < length and not (text[index] == "*" and text[index + 1] == "/"):
                index += 1
            index = min(index + 2, length)
            continue

        out.append(char)
        index += 1

    return "".join(out)


def remove_trailing_commas(text: str) -> str:
    previous = None
    current = text
    while previous != current:
        previous = current
        current = re.sub(r",\s*([}\]])", r"\1", current)
    return current


def close_unbalanced_json(text: str) -> str:
    stack: list[str] = []
    in_string = False
    escaped = False

    for char in text:
        if in_string:
            if escaped:
                escaped = False
            elif char == "\\":
                escaped = True
            elif char == '"':
                in_string = False
            continue

        if char == '"':
            in_string = True
        elif char == "{":
            stack.append("}")
        elif char == "[":
            stack.append("]")
        elif char in "}]":
            if stack and stack[-1] == char:
                stack.pop()

    if not stack:
        return text

    suffix = "".join(reversed(stack))
    return text.rstrip() + "\n" + suffix + "\n"
