#!/usr/bin/env python3
"""Create and audit a deterministic five-file Thunderstore package. No upload code."""
from __future__ import annotations
import argparse
import datetime as dt
import hashlib
import json
from pathlib import Path
import re
import struct
import subprocess
import sys
import zipfile

ROOT = Path(__file__).resolve().parents[1]


def git(*args: str) -> str:
    return subprocess.check_output(["git", *args], cwd=ROOT, text=True, stderr=subprocess.STDOUT).strip()


def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def metadata() -> dict[str, object]:
    return json.loads((ROOT / "mod.json").read_text(encoding="utf-8"))


def expected_paths(meta: dict[str, object]) -> tuple[tuple[str, ...], dict[str, Path]]:
    identifier = str(meta["identifier"])
    package = ROOT / "packages" / identifier
    expected = (
        "manifest.json",
        "icon.png",
        "README.md",
        "CHANGELOG.md",
        f"plugins/{identifier}/{identifier}.dll",
    )
    return expected, {
        "manifest.json": package / "manifest.json",
        "icon.png": package / "icon.png",
        "README.md": package / "README.md",
        "CHANGELOG.md": ROOT / "CHANGELOG.md",
        f"plugins/{identifier}/{identifier}.dll": ROOT / "src" / identifier / "bin" / "Release" / f"{identifier}.dll",
    }


def validate_png(data: bytes) -> None:
    if len(data) < 33 or data[:8] != b"\x89PNG\r\n\x1a\n" or data[12:16] != b"IHDR":
        raise ValueError("icon.png is not a valid PNG")
    length = struct.unpack(">I", data[8:12])[0]
    width, height, bit_depth, color_type = struct.unpack(">IIBB", data[16:26])
    if length != 13 or (width, height, bit_depth, color_type) != (256, 256, 8, 6):
        raise ValueError("icon.png must be 256x256 8-bit RGBA")


def validate_manifest(data: bytes, meta: dict[str, object]) -> None:
    actual = json.loads(data.decode("utf-8"))
    expected = {
        "name": meta["identifier"],
        "version_number": meta["version"],
        "website_url": meta["repository_url"],
        "description": meta["description"],
        "dependencies": [meta["thunderstore_dependency"]],
    }
    if actual != expected:
        raise ValueError("manifest.json is stale or differs from mod.json")


def validate_dll(data: bytes, meta: dict[str, object], head: str) -> None:
    identifier = str(meta["identifier"])
    markers = (identifier, str(meta["plugin_guid"]), str(meta["version"]), head)
    if len(data) < 0x40 or data[:2] != b"MZ" or b"PE\x00\x00" not in data[:1024]:
        raise ValueError(f"{identifier}.dll is not a Windows PE assembly")
    for marker in markers:
        raw = marker.encode("utf-8")
        if raw not in data and marker.encode("utf-16le") not in data:
            raise ValueError(f"plugin DLL is stale or missing identity marker: {marker}")
    lower = data.lower()
    for leaked in (b"/home/hatch", b"c:\\users\\", b"stackmasterreferences", b"valheimreferences"):
        if leaked in lower:
            raise ValueError("plugin DLL contains a private/local path marker")


def validate_files(files: dict[str, bytes], meta: dict[str, object], head: str) -> None:
    expected, _ = expected_paths(meta)
    if tuple(files) != expected:
        raise ValueError(f"unexpected package paths/order: {tuple(files)!r}")
    if any(not content for content in files.values()):
        raise ValueError("package contains an empty file")
    validate_manifest(files["manifest.json"], meta)
    validate_png(files["icon.png"])
    identifier = str(meta["identifier"])
    validate_dll(files[f"plugins/{identifier}/{identifier}.dll"], meta, head)
    for name in files:
        lower = name.lower()
        if lower.endswith((".pdb", ".cs", ".csproj", ".sln", ".lock")):
            raise ValueError(f"development file forbidden in package: {name}")
        if any(token in lower for token in ("bepinex.dll", "harmony", "unity", "assembly_valheim", ".core.dll", "test")):
            raise ValueError(f"private/runtime/test artifact forbidden in package: {name}")


def load_sources(meta: dict[str, object]) -> dict[str, bytes]:
    expected, source = expected_paths(meta)
    missing = [str(source[name]) for name in expected if not source[name].is_file()]
    if missing:
        raise FileNotFoundError("missing package inputs: " + ", ".join(missing))
    return {name: source[name].read_bytes() for name in expected}


def timestamp_for_head(head: str) -> tuple[int, int, int, int, int, int]:
    epoch = int(git("show", "-s", "--format=%ct", head))
    value = dt.datetime.fromtimestamp(epoch, tz=dt.timezone.utc)
    if value.year < 1980:
        value = value.replace(year=1980)
    return value.year, value.month, value.day, value.hour, value.minute, value.second // 2 * 2


def verify_remote_exact(head: str) -> None:
    origin = git("remote", "get-url", "origin")
    if re.fullmatch(r"(?:git@github\.com:|https://github\.com/)[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+(?:\.git)?", origin) is None:
        raise RuntimeError("origin must be a GitHub repository")
    branch = git("branch", "--show-current")
    if not branch:
        raise RuntimeError("release requires a named branch")
    lines = git("ls-remote", "--heads", "origin", f"refs/heads/{branch}").splitlines()
    remote = lines[0].split()[0] if lines else ""
    if remote != head:
        raise RuntimeError(f"origin/{branch} does not equal local HEAD; push must succeed before bundle creation")


def verify_zip(path: Path, meta: dict[str, object], head: str) -> dict[str, bytes]:
    expected, _ = expected_paths(meta)
    expected_time = timestamp_for_head(head)
    with zipfile.ZipFile(path) as archive:
        if archive.comment:
            raise ValueError("ZIP archive comment is forbidden")
        infos = archive.infolist()
        if tuple(info.filename for info in infos) != expected:
            raise ValueError("ZIP paths/order differ from exact allowlist")
        for info in infos:
            if info.is_dir() or info.date_time != expected_time or info.compress_type != zipfile.ZIP_DEFLATED:
                raise ValueError(f"non-deterministic ZIP metadata for {info.filename}")
            if info.extra or info.comment or info.create_system != 3 or (info.external_attr >> 16) != 0o100644:
                raise ValueError(f"unexpected ZIP attributes for {info.filename}")
        if archive.testzip() is not None:
            raise ValueError("ZIP CRC validation failed")
        files = {info.filename: archive.read(info) for info in infos}
    validate_files(files, meta, head)
    return files


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--channel", choices=("test", "final"), default="test")
    parser.add_argument("--output", type=Path)
    parser.add_argument("--verify-only", type=Path)
    args = parser.parse_args()
    meta = metadata()
    head = git("rev-parse", "HEAD")

    if args.verify_only:
        verified = verify_zip(args.verify_only, meta, head)
        print(f"verified {args.verify_only}")
        for name, data in verified.items(): print(f"{sha256(data)}  {name}")
        print(f"{sha256(args.verify_only.read_bytes())}  {args.verify_only.name}")
        return 0

    if args.channel == "final":
        if git("status", "--porcelain", "--untracked-files=normal"):
            raise RuntimeError("final packaging requires a clean committed tree")
        verify_remote_exact(head)

    files = load_sources(meta)
    validate_files(files, meta, head)
    identifier, author, version = (str(meta[key]) for key in ("identifier", "author", "version"))
    suffix = "-test" if args.channel == "test" else ""
    output = args.output or ROOT / "artifacts" / ("test" if args.channel == "test" else "release") / f"{author}-{identifier}-{version}{suffix}.zip"
    output.parent.mkdir(parents=True, exist_ok=True)
    output.unlink(missing_ok=True)
    date_time = timestamp_for_head(head)
    with zipfile.ZipFile(output, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        for name, data in files.items():
            info = zipfile.ZipInfo(name, date_time=date_time)
            info.compress_type = zipfile.ZIP_DEFLATED
            info.create_system = 3
            info.external_attr = 0o100644 << 16
            archive.writestr(info, data, compress_type=zipfile.ZIP_DEFLATED, compresslevel=9)
    verified = verify_zip(output, meta, head)
    print(f"source commit: {head}")
    print(f"created and verified: {output}")
    for name, data in verified.items(): print(f"{sha256(data)}  {name}")
    print(f"{sha256(output.read_bytes())}  {output.name}")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as exc:
        print(f"package failed: {exc}", file=sys.stderr)
        raise SystemExit(1)
