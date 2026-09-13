#!/usr/bin/env python3
"""Render build/package identity from the single hand-edited mod.json source."""
from __future__ import annotations
import argparse
import html
import json
from pathlib import Path
import re
import sys

ROOT = Path(__file__).resolve().parents[1]
META = ROOT / "mod.json"


def load() -> dict[str, object]:
    data = json.loads(META.read_text(encoding="utf-8"))
    required = {"schema_version", "slug", "display_name", "identifier", "author", "plugin_guid", "version", "description", "repository_url", "thunderstore_dependency"}
    if set(data) != required:
        raise ValueError(f"mod.json keys must be exactly {sorted(required)}")
    if data["schema_version"] != 1:
        raise ValueError("unsupported mod.json schema_version")
    checks = {
        "slug": r"[a-z][a-z0-9-]{1,62}",
        "identifier": r"[A-Za-z_][A-Za-z0-9_]{1,63}",
        "author": r"[A-Za-z0-9_]{1,64}",
        "plugin_guid": r"[A-Za-z0-9][A-Za-z0-9_.-]{2,127}",
        "version": r"(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)",
        "thunderstore_dependency": r"[A-Za-z0-9_]+-[A-Za-z0-9_]+-\d+\.\d+\.\d+",
    }
    for key, pattern in checks.items():
        value = str(data[key])
        if re.fullmatch(pattern, value) is None:
            raise ValueError(f"invalid {key}: {value!r}")
    for key in ("display_name", "description", "repository_url"):
        value = str(data[key])
        if not value.strip() or "\n" in value or "\r" in value:
            raise ValueError(f"{key} must be non-empty and single-line")
    if not str(data["repository_url"]).startswith("https://github.com/"):
        raise ValueError("repository_url must be an HTTPS GitHub URL")
    return data


def outputs(data: dict[str, object]) -> dict[Path, bytes]:
    identifier = str(data["identifier"])
    version = str(data["version"])
    props = f'''<!-- Generated from mod.json by scripts/render_metadata.py. Do not edit. -->
<Project>
  <PropertyGroup>
    <ModIdentifier>{html.escape(identifier)}</ModIdentifier>
    <ModDisplayName>{html.escape(str(data["display_name"]))}</ModDisplayName>
    <ModAuthor>{html.escape(str(data["author"]))}</ModAuthor>
    <ModGuid>{html.escape(str(data["plugin_guid"]))}</ModGuid>
    <ModVersion>{version}</ModVersion>
    <Version>{version}</Version>
    <AssemblyVersion>{version}.0</AssemblyVersion>
    <FileVersion>{version}.0</FileVersion>
    <InformationalVersion>{version}</InformationalVersion>
    <Authors>{html.escape(str(data["author"]))}</Authors>
    <RepositoryUrl>{html.escape(str(data["repository_url"]))}</RepositoryUrl>
  </PropertyGroup>
</Project>
'''.encode()
    manifest = (json.dumps({
        "name": identifier,
        "version_number": version,
        "website_url": data["repository_url"],
        "description": data["description"],
        "dependencies": [data["thunderstore_dependency"]],
    }, indent=2, ensure_ascii=False) + "\n").encode()
    return {
        ROOT / "build" / "Generated.Mod.props": props,
        ROOT / "packages" / identifier / "manifest.json": manifest,
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    rendered = outputs(load())
    drift = []
    for path, content in rendered.items():
        if args.check:
            if not path.is_file() or path.read_bytes() != content:
                drift.append(str(path.relative_to(ROOT)))
        else:
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(content)
    if drift:
        print("derived metadata is stale: " + ", ".join(drift), file=sys.stderr)
        return 1
    print("metadata checked" if args.check else "metadata rendered")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as exc:
        print(f"metadata failed: {exc}", file=sys.stderr)
        raise SystemExit(1)
