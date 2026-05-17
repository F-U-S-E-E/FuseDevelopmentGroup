#!/usr/bin/env python3
"""Repo-root helper for folder-mode FUSE conversion."""

from __future__ import annotations

import sys
from pathlib import Path


ROOT = Path(__file__).resolve().parent
TOOLS = ROOT / "tools"
if str(TOOLS) not in sys.path:
    sys.path.insert(0, str(TOOLS))

import fuse_convert_folder  # noqa: E402


if __name__ == "__main__":
    raise SystemExit(fuse_convert_folder.main())
