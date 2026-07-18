#!/usr/bin/env python3
"""Validate Unity .meta GUIDs and asset pairing under Assets/ and Packages/."""

from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path

GUID_RE = re.compile(r"^guid:\s*(\S+)\s*$")
VALID_GUID_RE = re.compile(r"^[0-9a-f]{32}$")
SKIPPED_PARTS = {".git", "Library", "Logs", "Obj", "Temp"}


def repo_root_from_script() -> Path:
    path = Path(__file__).resolve().parent
    for candidate in (path, *path.parents):
        if (candidate / "Assets").is_dir() and (candidate / "Packages").is_dir():
            return candidate
    raise RuntimeError("Could not find Unity project root (Assets/ + Packages/)")


def is_skipped(path: Path) -> bool:
    return any(part in SKIPPED_PARTS or part.endswith("~") for part in path.parts)


def collect_meta_files(roots: list[Path]) -> list[Path]:
    files: set[Path] = set()
    for root in roots:
        if root.is_file() and root.name.endswith(".meta"):
            files.add(root.resolve())
        elif root.is_dir():
            files.update(path.resolve() for path in root.rglob("*.meta") if not is_skipped(path))
    return sorted(files)


def parse_guid(meta_path: Path) -> tuple[str | None, str | None]:
    try:
        lines = meta_path.read_text(encoding="utf-8", errors="replace").splitlines()
    except OSError as exc:
        return None, f"cannot read: {exc}"
    for line in lines[:12]:
        match = GUID_RE.match(line)
        if match:
            return match.group(1), None
    return None, "missing guid: line"


def asset_path_for_meta(meta_path: Path) -> Path:
    return meta_path.with_name(meta_path.name[: -len(".meta")])


def check_pairing(meta_files: list[Path], roots: list[Path]) -> tuple[list[str], list[str]]:
    errors: list[str] = []
    warnings: list[str] = []
    for meta_path in meta_files:
        asset = asset_path_for_meta(meta_path)
        if not asset.exists():
            errors.append(f"{meta_path.as_posix()}: orphan (no asset)")
    for root in roots:
        if not root.is_dir():
            continue
        for asset in root.rglob("*"):
            if is_skipped(asset) or asset.name.endswith(".meta") or asset.name.endswith("~"):
                continue
            if not Path(f"{asset}.meta").exists():
                warnings.append(f"{asset.as_posix()}: missing .meta (let Unity import)")
    return errors, warnings


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("paths", nargs="*", help="Files or folders (default: Assets Packages)")
    parser.add_argument("--no-pairing", action="store_true")
    parser.add_argument("--strict-warnings", action="store_true")
    args = parser.parse_args()

    root = repo_root_from_script()
    scan_roots = (
        [Path(path).resolve() if Path(path).is_absolute() else (root / path).resolve() for path in args.paths]
        if args.paths
        else [root / "Assets", root / "Packages"]
    )
    meta_files = collect_meta_files(scan_roots)
    if not meta_files:
        print("[validate_unity_meta] no .meta files found", file=sys.stderr)
        return 1

    errors: list[str] = []
    warnings: list[str] = []
    guid_to_meta: dict[str, Path] = {}
    for meta_path in meta_files:
        guid, parse_error = parse_guid(meta_path)
        if parse_error:
            errors.append(f"{meta_path.as_posix()}: {parse_error}")
            continue
        assert guid is not None
        if not VALID_GUID_RE.fullmatch(guid):
            errors.append(f"{meta_path.as_posix()}: GUID must be 32 lowercase hex characters")
            continue
        previous = guid_to_meta.get(guid)
        if previous:
            errors.append(f"duplicate GUID {guid}: {previous.as_posix()} and {meta_path.as_posix()}")
        else:
            guid_to_meta[guid] = meta_path

    if not args.no_pairing:
        pair_errors, pair_warnings = check_pairing(meta_files, scan_roots)
        errors.extend(pair_errors)
        warnings.extend(pair_warnings)

    for warning in warnings:
        print(f"[warn] {warning}")
    for error in errors:
        print(f"[FAIL] {error}")
    if errors:
        print(f"[validate_unity_meta] {len(errors)} error(s)", file=sys.stderr)
        return 1
    if warnings and args.strict_warnings:
        print(f"[validate_unity_meta] {len(warnings)} warning(s) in strict mode", file=sys.stderr)
        return 1
    print(f"[ok] {len(meta_files)} .meta file(s) checked")
    return 0


if __name__ == "__main__":
    sys.exit(main())
