#!/usr/bin/env python3
"""Validate every tracked .json file in the repository.

Two checks per file:

  1. Strict JSON parse via the standard library. The standard parser rejects
     comments, trailing commas, control characters, and other non-spec
     extensions. FUSE's legacy data converter intentionally accepts those
     in third-party mod data; this script applies the stricter rule to
     FIRST-PARTY files (everything tracked by git in this repo) so the
     schemas and manifest do not start carrying extension syntax by
     accident.

  2. JSON Schema validation for the canonical example/manifest files
     against their declared schemas:
         Info.json                       -> schemas/umm-info.schema.json
         schemas/fuse-mod.example.json   -> schemas/fuse-mod.schema.json
     Catches schema drift between the example/manifest and the schema
     that publishes against them.

The script always processes every file before exiting so a single broken
file does not hide other problems. Exit status is the count of failed
files (capped at 255 by the OS). Designed to be runnable both from CI and
locally (``python tools/json_lint.py`` from the repository root).
"""

from __future__ import annotations

import json
import shutil
import subprocess
import sys
from pathlib import Path

try:
    # Imported lazily so a missing optional dependency only matters when a
    # schema-validation file is on the list. Syntax-only linting still
    # works without jsonschema installed.
    import jsonschema
except ImportError:  # pragma: no cover - reported at use-site below
    jsonschema = None


# Map of repo-relative instance path -> repo-relative schema path. Every
# entry here is validated structurally against its schema in addition to
# the syntax pass.
SCHEMA_PAIRS: dict[str, str] = {
    "Info.json": "schemas/umm-info.schema.json",
    "schemas/fuse-mod.example.json": "schemas/fuse-mod.schema.json",
}


def repo_root() -> Path:
    """Return the repository root inferred from this script's location."""
    return Path(__file__).resolve().parent.parent


def tracked_json_files(root: Path) -> list[Path]:
    """Return every .json file tracked by git, repo-relative paths sorted."""
    # ls-files is the right primitive here: it ignores build artifacts in
    # bin/obj, leftover files under .git/worktrees, and anything in
    # .gitignore. Avoids accidentally linting machine-generated files.
    #
    # subprocess.run on Windows uses CreateProcess directly and does NOT
    # walk PATH the way a shell would, so a bare "git" argv0 fails on the
    # CI runner with "The system cannot find the file specified" even
    # though git is plainly on the runner's PATH. shutil.which resolves
    # the executable's full path against PATH/PATHEXT first; passing the
    # absolute path then succeeds on Windows, macOS, and Linux without
    # falling back to shell=True (which would re-introduce quoting risk
    # on the glob argument).
    git_executable = shutil.which("git")
    if git_executable is None:
        raise RuntimeError(
            "git executable not found on PATH; install git or run this "
            "script from an environment where git is available."
        )
    result = subprocess.run(
        [git_executable, "ls-files", "*.json"],
        cwd=root,
        check=True,
        capture_output=True,
        text=True,
    )
    paths = [Path(line) for line in result.stdout.splitlines() if line.strip()]
    return sorted(paths)


def syntax_check(root: Path, relative_path: Path) -> str | None:
    """Strict-parse one file; return an error string on failure, None on success."""
    full_path = root / relative_path
    try:
        with full_path.open("r", encoding="utf-8") as handle:
            json.load(handle)
    except UnicodeDecodeError as exc:
        return f"not valid UTF-8: {exc}"
    except json.JSONDecodeError as exc:
        return f"line {exc.lineno} col {exc.colno}: {exc.msg}"
    return None


def schema_check(root: Path, instance_rel: str, schema_rel: str) -> str | None:
    """Validate one instance file against its schema. Returns error or None."""
    if jsonschema is None:
        return (
            "jsonschema package is not installed; install with "
            "`pip install jsonschema` to enable schema validation"
        )

    instance_path = root / instance_rel
    schema_path = root / schema_rel
    try:
        with schema_path.open("r", encoding="utf-8") as handle:
            schema = json.load(handle)
        with instance_path.open("r", encoding="utf-8") as handle:
            instance = json.load(handle)
    except (OSError, json.JSONDecodeError) as exc:
        # Syntax issues are already reported by the syntax pass; surface
        # the load failure here for completeness without re-running the
        # check itself.
        return f"could not load instance or schema for validation: {exc}"

    # Pick the validator class matching the schema's $schema URI so
    # Draft 2020-12 (used by FUSE's schemas) is honoured rather than
    # silently downgraded to the default draft.
    try:
        validator_cls = jsonschema.validators.validator_for(schema)
        validator_cls.check_schema(schema)
        validator = validator_cls(schema)
    except jsonschema.exceptions.SchemaError as exc:
        return f"schema '{schema_rel}' is itself invalid: {exc.message}"

    errors = sorted(validator.iter_errors(instance), key=lambda e: list(e.absolute_path))
    if not errors:
        return None
    # Cap the rendered error list so the CI log stays readable on a wide
    # regression; the first few errors are nearly always sufficient to
    # diagnose the root cause.
    rendered = []
    for error in errors[:5]:
        location = "/".join(str(segment) for segment in error.absolute_path) or "<root>"
        rendered.append(f"  - {location}: {error.message}")
    suffix = "" if len(errors) <= 5 else f"\n  ... and {len(errors) - 5} more"
    return f"{len(errors)} schema validation error(s):\n" + "\n".join(rendered) + suffix


def main() -> int:
    root = repo_root()
    files = tracked_json_files(root)
    if not files:
        print("json_lint: no tracked .json files found; nothing to do.")
        return 0

    print(f"json_lint: checking {len(files)} tracked .json file(s) under {root}")

    syntax_failures = 0
    for path in files:
        error = syntax_check(root, path)
        if error is None:
            print(f"  OK   {path}")
        else:
            print(f"  FAIL {path}: {error}")
            syntax_failures += 1

    schema_failures = 0
    for instance_rel, schema_rel in SCHEMA_PAIRS.items():
        if not (root / instance_rel).exists():
            print(f"  SKIP schema check '{instance_rel}' (file missing)")
            continue
        if not (root / schema_rel).exists():
            print(f"  SKIP schema check '{instance_rel}' (schema '{schema_rel}' missing)")
            continue
        error = schema_check(root, instance_rel, schema_rel)
        if error is None:
            print(f"  OK   {instance_rel} (validated against {schema_rel})")
        else:
            print(f"  FAIL {instance_rel} (against {schema_rel}): {error}")
            schema_failures += 1

    total = syntax_failures + schema_failures
    if total == 0:
        print("json_lint: all checks passed.")
        return 0

    print(
        f"json_lint: {total} failure(s) "
        f"(syntax={syntax_failures}, schema={schema_failures})."
    )
    return min(total, 255)


if __name__ == "__main__":
    sys.exit(main())
