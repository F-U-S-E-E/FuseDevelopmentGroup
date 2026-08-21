"""Build this repository's PDF manuals from the Markdown in docs/.

Usage:  python scripts/build_pdfs.py
Needs:  pip install reportlab
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from md2pdf import build  # noqa: E402

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def P(*parts):
    return os.path.join(REPO, *parts)


MANUALS = [
    dict(out=P("docs", "pdf", "FUSE-User-Manual.pdf"),
         title="FUSE User Manual",
         subtitle="A Unity Mod Manager modding layer for Railroader",
         sections=[("Getting Started", P("docs","GETTING_STARTED.md")),
                   ("Frequently Asked Questions", P("docs","FAQ.md")),
                   ("Migrating From Legacy Mods", P("docs","MIGRATION_FROM_LEGACY.md")),
                   ("Settings Reference", P("docs","SETTINGS.md")),
                   ("Performance Acceptance Testing", P("docs","PERFORMANCE_TESTING.md")),
                   ("Console Command Reference", P("docs","CONSOLE_COMMANDS.md")),
                   ("Troubleshooting", P("docs","TROUBLESHOOTING.md")),
                   ("Known Issues", P("docs","KNOWN_ISSUES.md")),
                   ("Installer", P("docs","FUSE_INSTALLER.md"))]),
    dict(out=P("docs", "pdf", "FUSE-Package-Author-Guide.pdf"),
         title="FUSE Package Author Guide",
         subtitle="Authoring and converting FUSE packages",
         sections=[("Package Author Guide", P("docs","PACKAGE_AUTHOR_GUIDE.md")),
                   ("JSON Schema Reference", P("schemas","FUSE_JSON_SCHEMA.md")),
                   ("Converter", P("docs","FUSE_CONVERTER.md")),
                   ("External Editor", P("docs","EXTERNAL_EDITOR.md")),
                   ("Migrating From Legacy Mods", P("docs","MIGRATION_FROM_LEGACY.md")),
                   ("Architecture", P("docs","ARCHITECTURE.md"))]),
]


def main():
    ok = 0
    for m in MANUALS:
        os.makedirs(os.path.dirname(m["out"]), exist_ok=True)
        for _, p in m["sections"]:
            if not os.path.isfile(p):
                print("  ! missing section source: %s" % p)
        try:
            path, n = build(m["out"], m["title"], m["subtitle"], m["sections"])
            print("OK  %-44s %2d sections  %6.1f KB"
                  % (os.path.basename(path), n, os.path.getsize(path) / 1024.0))
            ok += 1
        except Exception as e:
            print("FAIL %-44s %s: %s" % (os.path.basename(m["out"]), type(e).__name__, e))
    print("")
    print("%d/%d manuals built" % (ok, len(MANUALS)))
    return 0 if ok == len(MANUALS) else 1


if __name__ == "__main__":
    raise SystemExit(main())
