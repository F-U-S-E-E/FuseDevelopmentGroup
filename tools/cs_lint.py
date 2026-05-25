#!/usr/bin/env python3
"""Lint tracked C# source for objective whitespace defects.

This script is the .NET counterpart to ``tools/json_lint.py``. Its scope is
deliberately narrow: it flags issues that are unambiguously wrong (broken
encoding markers, missing terminators, accidental trailing whitespace,
mixed-tab indentation in a space-indented file) without touching style
choices the codebase has consciously adopted (column alignment in object
initializers, explicit block braces under case labels, etc.).

The four checks, applied per file:

  1. **At most one leading UTF-8 BOM.** A small number of legacy .cs files
     in the tree start with a single ``EF BB BF`` BOM. That's accepted;
     duplicate or stacked BOMs (which the C# compiler ignores but which
     break some tools and confuse text searches) are rejected.

  2. **File ends with a newline.** A missing final newline is the canonical
     "looks fine in the editor but breaks ``cat``/diff" defect.

  3. **No trailing whitespace** on any line. CR is stripped before the
     regex match so CRLF-encoded files don't false-positive on the ``\\r``
     terminator. Hard tabs inside lines are allowed.

  4. **Indentation is space-only.** ``.editorconfig`` declares
     ``indent_style = space`` for the whole tree; any line whose
     indentation prefix contains a TAB character (U+0009) violates that.
     Tabs that appear inside the body of a line are allowed (rare, but
     legitimate inside string literals).

We deliberately do NOT run ``dotnet format`` here. Its whitespace ruleset
demands changes the FUSE codebase has chosen to push back on:

  * Collapsing multi-space alignment in object initializers like
    ``Position         = src.Position`` -> ``Position = src.Position``.
  * Forcing the C# default brace placement under ``case`` labels.

Those are style preferences, not defects, so they don't belong in a lint
gate. If the project ever wants a comprehensive formatter, it should be
introduced behind a feature flag with the codebase pre-reformatted in a
dedicated commit — not as a side effect of CI.

Designed to be runnable both from CI and locally (``python tools/cs_lint.py``
from the repository root). Exit status is the count of failed files, capped
at 255 by the OS.
"""

from __future__ import annotations

import re
import shutil
import subprocess
import sys
from pathlib import Path


_UTF8_BOM = b"\xef\xbb\xbf"


def repo_root() -> Path:
    """Return the repository root inferred from this script's location."""
    return Path(__file__).resolve().parent.parent


def find_git_executable() -> str | None:
    """Locate the git binary across the runner shapes we care about.

    Mirrors the same fallback chain as ``tools/json_lint.py``: env var
    override -> PATH lookup -> canonical Windows install locations. The
    self-hosted CI runner populates the workspace via the GitHub API and
    has no callable git binary on its service-account PATH, so a hard
    requirement on git would block CI — see ``tracked_cs_files`` below
    for the filesystem-walk fallback.
    """
    import os

    env_override = os.environ.get("GIT_EXECUTABLE")
    if env_override and Path(env_override).is_file():
        return env_override

    via_path = shutil.which("git")
    if via_path:
        return via_path

    windows_candidates = [
        r"C:\Program Files\Git\cmd\git.exe",
        r"C:\Program Files\Git\bin\git.exe",
        r"C:\Program Files (x86)\Git\cmd\git.exe",
        r"C:\Program Files (x86)\Git\bin\git.exe",
    ]
    for candidate in windows_candidates:
        if Path(candidate).is_file():
            return candidate

    return None


# Directories to skip in the filesystem-walk fallback. Same categories
# `git ls-files` would exclude via .gitignore or because they sit outside
# the index. Matched case-insensitively against any path component.
_FILESYSTEM_WALK_SKIP_DIRS = frozenset(
    name.lower() for name in (
        ".git",
        ".github",
        ".vs",
        ".vscode",
        ".idea",
        ".claude",
        "bin",
        "obj",
        "Library",
        "Temp",
        "Logs",
        "UserSettings",
        "PackageCache",
        "TestResults",
        "node_modules",
        "_work",
        "tmp",
        "Plugins",
    )
)


def filesystem_cs_files(root: Path) -> list[Path]:
    """Walk ``root`` and yield every .cs file outside our skip-list."""
    discovered: list[Path] = []
    for path in root.rglob("*.cs"):
        if path.is_dir():
            continue
        relative = path.relative_to(root)
        if any(part.lower() in _FILESYSTEM_WALK_SKIP_DIRS for part in relative.parts):
            continue
        discovered.append(relative)
    return sorted(discovered)


def tracked_cs_files(root: Path) -> list[Path]:
    """Return every .cs file under ``root`` worth linting, sorted.

    Prefers ``git ls-files *.cs`` when git is available; falls back to a
    filesystem walk when git is missing. Matches the behaviour of
    ``json_lint.tracked_json_files`` so the two linters behave the same
    way on the CI runner that lacks a callable git binary.
    """
    git_executable = find_git_executable()
    if git_executable is not None:
        try:
            result = subprocess.run(
                [git_executable, "ls-files", "*.cs"],
                cwd=root,
                check=True,
                capture_output=True,
                text=True,
            )
        except subprocess.CalledProcessError as exc:
            print(
                f"cs_lint: git ls-files failed ({exc.returncode}); "
                f"falling back to filesystem walk."
            )
        else:
            paths = [Path(line) for line in result.stdout.splitlines() if line.strip()]
            return sorted(paths)
    else:
        print(
            "cs_lint: git executable not found; falling back to filesystem walk "
            "(set GIT_EXECUTABLE if a specific binary should be preferred)."
        )

    return filesystem_cs_files(root)


def check_file(root: Path, relative_path: Path) -> list[str]:
    """Apply every lint check to one file. Returns a list of failure strings."""
    full_path = root / relative_path
    try:
        raw = full_path.read_bytes()
    except OSError as exc:
        return [f"could not read file: {exc}"]

    failures: list[str] = []

    # 1. At most one leading UTF-8 BOM.
    bom_count = 0
    cursor = 0
    while raw.startswith(_UTF8_BOM, cursor):
        bom_count += 1
        cursor += len(_UTF8_BOM)
    if bom_count > 1:
        failures.append(
            f"file begins with {bom_count} stacked UTF-8 BOM markers; "
            f"keep at most one"
        )

    # 2. File ends with a newline.
    if raw and not raw.endswith(b"\n"):
        failures.append("file does not end with a newline (insert_final_newline)")

    # Decode for the line-oriented checks. Bail with a clear error rather
    # than a UnicodeDecodeError stacktrace if the file isn't valid UTF-8.
    try:
        text = raw.decode("utf-8-sig")
    except UnicodeDecodeError as exc:
        failures.append(f"not valid UTF-8: {exc}")
        return failures

    # Split on \n so CR endings inside a CRLF pair don't fragment lines.
    lines = text.split("\n")
    # If the file ends with a trailing newline, the split leaves an empty
    # final element; trim it so we don't false-flag it for trailing
    # whitespace (it's not actually a line).
    if lines and lines[-1] == "":
        lines = lines[:-1]

    trailing_whitespace_re = re.compile(r"[ \t]+$")
    leading_tab_re = re.compile(r"^[ \t]*\t")  # any tab within the leading whitespace

    trailing_violations: list[int] = []
    tab_indent_violations: list[int] = []

    for index, line in enumerate(lines, start=1):
        # Drop a trailing CR so CRLF files don't have CR counted as
        # "trailing whitespace" on every line.
        stripped = line[:-1] if line.endswith("\r") else line

        if trailing_whitespace_re.search(stripped):
            trailing_violations.append(index)

        # Indentation = the run of whitespace at the start of the line.
        leading_match = re.match(r"^[ \t]*", stripped)
        if leading_match is not None and "\t" in leading_match.group(0):
            tab_indent_violations.append(index)

    if trailing_violations:
        rendered = ", ".join(str(n) for n in trailing_violations[:5])
        suffix = (
            f" and {len(trailing_violations) - 5} more"
            if len(trailing_violations) > 5
            else ""
        )
        failures.append(
            f"trailing whitespace on line(s) {rendered}{suffix} "
            f"(trim_trailing_whitespace)"
        )

    if tab_indent_violations:
        rendered = ", ".join(str(n) for n in tab_indent_violations[:5])
        suffix = (
            f" and {len(tab_indent_violations) - 5} more"
            if len(tab_indent_violations) > 5
            else ""
        )
        failures.append(
            f"TAB character in indentation on line(s) {rendered}{suffix} "
            f"(indent_style = space)"
        )

    return failures


def main() -> int:
    root = repo_root()
    files = tracked_cs_files(root)
    if not files:
        print("cs_lint: no tracked .cs files found; nothing to do.")
        return 0

    print(f"cs_lint: checking {len(files)} tracked .cs file(s) under {root}")

    failed = 0
    for path in files:
        failures = check_file(root, path)
        if not failures:
            print(f"  OK   {path}")
            continue
        failed += 1
        print(f"  FAIL {path}:")
        for failure in failures:
            print(f"         {failure}")

    if failed == 0:
        print("cs_lint: all checks passed.")
        return 0

    print(f"cs_lint: {failed} file(s) failed.")
    return min(failed, 255)


if __name__ == "__main__":
    sys.exit(main())
