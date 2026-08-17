"""Test configuration for the tools/ Python utilities.

Puts tools/ on sys.path so tests can `import fuse_installer` / `import
legacy_json` the same way the installer imports its sibling module at runtime.
"""

from __future__ import annotations

import sys
from pathlib import Path

TOOLS_DIR = Path(__file__).resolve().parent.parent
if str(TOOLS_DIR) not in sys.path:
    sys.path.insert(0, str(TOOLS_DIR))
