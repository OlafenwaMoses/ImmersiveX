#!/usr/bin/env python3
"""Fast repository checks that need no Unity licence. CI runs them; run them locally before pushing:

    python3 tools/ci/check_repo.py

Checks:
  1. Every asset under Unity-managed folders has a .meta file, and every .meta file has its asset.
     (A missing .meta makes Unity generate a new GUID on another machine and silently breaks references.)
  2. Every .json / .asmdef / .asmref file parses.
  3. Unity's generated folders (Library, Temp, Logs, UserSettings, obj) are not committed.
"""
import json
import os
import subprocess
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))

# Folders whose contents Unity imports and therefore pairs with .meta files.
META_ROOTS = ["Assets", "Packages", "Platforms"]
GENERATED = ("Library/", "Temp/", "Logs/", "UserSettings/", "obj/")


def tracked_or_unignored_files():
    """Files git would commit: tracked plus untracked-but-not-ignored."""
    out = subprocess.run(
        ["git", "ls-files", "--cached", "--others", "--exclude-standard", "-z"],
        cwd=ROOT, check=True, capture_output=True,
    ).stdout.decode()
    return sorted({f for f in out.split("\0") if f and os.path.exists(os.path.join(ROOT, f))})


def unity_ignores(path):
    """Unity skips hidden entries and names ending in '~' (e.g. Documentation~, Samples~)."""
    return any(part.startswith(".") or part.endswith("~") for part in path.split("/"))


def needs_meta(path):
    parts = path.split("/")
    if parts[0] not in META_ROOTS or unity_ignores(path):
        return False
    if parts[0] == "Packages" and len(parts) == 2:
        return False  # manifest.json / packages-lock.json at Packages/ root
    if parts[0] == "Platforms" and len(parts) == 2:
        return False  # README.md etc. directly under Platforms/ (not inside a package)
    if parts[0] == "Platforms" and parts[1].startswith("_"):
        return False  # copy-me templates are never installed; a copy gets fresh GUIDs when Unity imports it
    return True


def check_meta_files(files):
    problems = []
    fileset = set(files)
    folders = set()
    for f in files:
        if f.endswith(".meta") or not needs_meta(f):
            continue
        if f + ".meta" not in fileset:
            problems.append(f"missing .meta: {f}")
        # every parent folder inside a package or Assets needs a .meta too
        parts = f.split("/")
        start = 1 if parts[0] == "Assets" else 2  # skip Assets/, Packages/<pkg>/, Platforms/<pkg>/
        for i in range(start + 1, len(parts)):
            folders.add("/".join(parts[:i]))
    for d in sorted(folders):
        if needs_meta(d) and d + ".meta" not in fileset:
            problems.append(f"missing folder .meta: {d}")
    for f in files:
        if f.endswith(".meta") and f.split("/")[0] in META_ROOTS and not unity_ignores(f[:-5]):
            asset = f[:-5]
            if not os.path.exists(os.path.join(ROOT, asset)):
                problems.append(f"orphan .meta (asset deleted?): {f}")
    return problems


def check_json(files):
    problems = []
    for f in files:
        if f.endswith((".json", ".asmdef", ".asmref")):
            try:
                with open(os.path.join(ROOT, f), encoding="utf-8") as handle:
                    json.load(handle)
            except (ValueError, UnicodeDecodeError) as error:
                problems.append(f"invalid JSON: {f}: {error}")
    return problems


def check_generated(files):
    return [f"generated folder committed: {f}" for f in files if f.startswith(GENERATED)]


def main():
    files = tracked_or_unignored_files()
    problems = check_generated(files) + check_meta_files(files) + check_json(files)
    for problem in problems:
        print(f"::error::{problem}" if os.environ.get("GITHUB_ACTIONS") else f"✗ {problem}")
    if problems:
        print(f"\n{len(problems)} problem(s) found.")
        return 1
    print(f"✓ Repository checks passed ({len(files)} files).")
    return 0


if __name__ == "__main__":
    sys.exit(main())
