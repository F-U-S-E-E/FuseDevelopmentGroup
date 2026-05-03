#!/usr/bin/env python3
"""Build the portable FUSEConvertFolder.pyz archive."""

from __future__ import annotations

import shutil
import tempfile
import zipapp
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
TOOLS = ROOT / "tools"
DIST = ROOT / "dist"


def main() -> int:
    DIST.mkdir(parents=True, exist_ok=True)
    output = DIST / "FUSEConvertFolder.pyz"
    with tempfile.TemporaryDirectory(prefix="fuse-folder-pyz-") as temp:
        temp_root = Path(temp)
        for name in ("fuse_converter.py", "fuse_convert.py", "convert_fuse_audio.py"):
            shutil.copy2(TOOLS / name, temp_root / name)
        shutil.copy2(TOOLS / "fuse_convert_folder.py", temp_root / "__main__.py")
        zipapp.create_archive(temp_root, target=output, interpreter="/usr/bin/env python3", compressed=True)
    print(output)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
