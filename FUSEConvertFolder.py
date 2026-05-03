#!/usr/bin/env python3
"""
Drop this script into a folder of legacy Railroader mods and run it.

It converts every immediate child folder, .zip, and data .json it recognizes,
writing output into ./FUSEConverted by default.

When kept in the FUSE repo, it uses tools/fuse_converter.py. For a single-file
portable Linux/Windows handoff, build/use dist/FUSEConvertFolder.pyz.
"""

from __future__ import annotations

import sys
from pathlib import Path


def add_converter_path(script_dir: Path) -> None:
    candidates = [
        script_dir / "tools",
        script_dir,
        Path.cwd() / "tools",
    ]
    for candidate in candidates:
        if (candidate / "fuse_converter.py").exists():
            sys.path.insert(0, str(candidate))
            return
    raise SystemExit(
        "Could not find fuse_converter.py. Run this from the FUSE repo, or use "
        "the portable dist/FUSEConvertFolder.pyz bundle."
    )


def main() -> int:
    script_dir = Path(__file__).resolve().parent
    add_converter_path(script_dir)
    import fuse_converter

    args = sys.argv[1:]
    if "--batch" in args:
        pass
    elif not args or args[0].startswith("-"):
        args = ["--batch", str(script_dir), *args]
    else:
        args = ["--batch", *args]

    sys.argv = ["fuse_converter.py", *args]
    return fuse_converter.main()


if __name__ == "__main__":
    raise SystemExit(main())
