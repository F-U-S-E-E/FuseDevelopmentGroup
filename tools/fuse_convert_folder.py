#!/usr/bin/env python3
"""
Portable folder-mode entrypoint for FUSE converter bundles.

If run with no positional input, it scans the folder where this script/archive
lives and writes converted packages into FUSEConverted.
"""

from __future__ import annotations

import sys
from pathlib import Path

import fuse_converter


def main() -> int:
    script_path = Path(sys.argv[0]).resolve()
    scan_folder = script_path.parent
    args = sys.argv[1:]
    if "--batch" in args:
        pass
    elif not args or args[0].startswith("-"):
        args = ["--batch", str(scan_folder), *args]
    else:
        args = ["--batch", *args]

    sys.argv = ["fuse_converter.py", *args]
    return fuse_converter.main()


if __name__ == "__main__":
    raise SystemExit(main())
