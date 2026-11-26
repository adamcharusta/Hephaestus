#!/usr/bin/env python3
"""Fail the build when a NuGet package, direct or transitive, carries a license outside the allowed list (NFR-15).

Reads every `project.assets.json` that a restore leaves under the repository, so the whole resolved closure is covered,
test tooling included. A package's license is the SPDX expression in its `.nuspec`. A package that names no expression
needs an entry in `eng/license-overrides.txt`, with the reason the license is known to be free and open-source.

An override names the package and the exact version it was checked for, so a later version that changes license is
checked again: `<package> <version> <SPDX expression>  # <reason>`.

The allowed list is `eng/allowed-licenses.txt`: one SPDX identifier per line. Copyleft that would bind Hephaestus's own
MIT-licensed source is not on it.
"""

from __future__ import annotations

import argparse
import json
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

TOKEN = re.compile(r"\(|\)|[^\s()]+")


class ExpressionError(ValueError):
    """An SPDX expression could not be parsed."""


def read_list(text: str) -> list[tuple[str, str]]:
    """Entries of the form `<value>  # <reason>`; the reason may be empty. Comment and blank lines are skipped."""
    entries = []
    for raw in text.splitlines():
        line = raw.strip()
        if not line or line.startswith("#"):
            continue
        value, _, reason = line.partition("#")
        entries.append((value.strip(), reason.strip()))
    return entries


def parse_allowed(text: str) -> set[str]:
    return {value.casefold() for value, _ in read_list(text)}


def parse_overrides(text: str) -> dict[tuple[str, str], str]:
    """`<package id> <version> <SPDX expression>  # <reason>`, keyed by (package id, version). A reason is required."""
    overrides = {}
    for value, reason in read_list(text):
        parts = value.split(None, 2)
        if len(parts) < 3 or not reason:
            raise ExpressionError(f"override '{value}' needs '<package> <version> <SPDX expression>  # <reason>'")
        overrides[(parts[0].casefold(), parts[1].casefold())] = parts[2].strip()
    return overrides


def satisfied(expression: str, allowed: set[str]) -> bool:
    """True when the SPDX expression can be satisfied with allowed licenses only.

    `A OR B` needs one side, `A AND B` needs both, `A WITH exception` is judged by `A`, and a trailing `+` ("or later")
    is judged by the base identifier.
    """
    tokens = TOKEN.findall(expression)
    position = 0

    def peek() -> str | None:
        return tokens[position] if position < len(tokens) else None

    def take() -> str:
        nonlocal position
        if position >= len(tokens):
            raise ExpressionError(f"unexpected end of '{expression}'")
        position += 1
        return tokens[position - 1]

    def parse_or() -> bool:
        result = parse_and()
        while (peek() or "").upper() == "OR":
            take()
            result = parse_and() or result
        return result

    def parse_and() -> bool:
        result = parse_term()
        while (peek() or "").upper() == "AND":
            take()
            result = parse_term() and result
        return result

    def parse_term() -> bool:
        token = take()
        if token == "(":
            result = parse_or()
            if take() != ")":
                raise ExpressionError(f"missing ')' in '{expression}'")
            return result
        if token == ")" or token.upper() in {"AND", "OR", "WITH"}:
            raise ExpressionError(f"unexpected '{token}' in '{expression}'")
        if (peek() or "").upper() == "WITH":
            take()
            take()
        return token.rstrip("+").casefold() in allowed

    result = parse_or()
    if position != len(tokens):
        raise ExpressionError(f"unexpected '{tokens[position]}' in '{expression}'")
    return result


def nuspec_license(nuspec: Path) -> tuple[str | None, str | None]:
    """The (SPDX expression, legacy license URL or file name) a `.nuspec` declares."""
    root = ET.parse(nuspec).getroot()  # a malformed file raises ParseError; check() reports it against the package
    expression = url = None
    for element in root.iter():
        name = element.tag.rsplit("}", 1)[-1]
        if name == "license" and element.get("type") == "expression":
            expression = (element.text or "").strip()
        elif name == "license" and element.get("type") == "file":
            url = f"file:{(element.text or '').strip()}"
        elif name == "licenseUrl" and url is None:
            url = (element.text or "").strip()
    return expression, url


def packages_of(assets_file: Path) -> list[tuple[str, str, list[Path]]]:
    """(id, version, NuGet package folders) of every package the restore resolved."""
    data = json.loads(assets_file.read_text(encoding="utf-8"))
    folders = [Path(folder) for folder in data.get("packageFolders", {})]
    result = []
    for library, info in data.get("libraries", {}).items():
        if info.get("type") == "package":
            name, _, version = library.partition("/")
            result.append((name, version, folders))
    return result


def find_nuspec(name: str, version: str, folders: list[Path]) -> Path | None:
    for folder in folders:
        path = folder / name.lower() / version.lower() / f"{name.lower()}.nuspec"
        if path.is_file():
            return path
    return None


def check(assets_files: list[Path], allowed: set[str], overrides: dict[tuple[str, str], str]) -> tuple[list[str], int]:
    """Return the violations and the number of distinct packages checked."""
    violations: list[str] = []
    seen: set[tuple[str, str]] = set()
    for assets_file in assets_files:
        try:
            packages = packages_of(assets_file)
        except (OSError, ValueError) as error:
            violations.append(f"{assets_file}: unreadable restore result ({error})")
            continue
        for name, version, folders in packages:
            if (name, version) in seen:
                continue
            seen.add((name, version))
            nuspec = find_nuspec(name, version, folders)
            if nuspec is None:
                violations.append(f"{name} {version}: package not found in the NuGet cache; restore first")
                continue
            try:
                expression, legacy = nuspec_license(nuspec)
            except ET.ParseError as error:
                violations.append(f"{name} {version}: unreadable .nuspec ({error})")
                continue
            expression = expression or overrides.get((name.casefold(), version.casefold()))
            if not expression:
                violations.append(f"{name} {version}: no SPDX license expression (declares {legacy or 'nothing'}); add a reasoned override")
                continue
            try:
                ok = satisfied(expression, allowed)
            except ExpressionError as error:
                violations.append(f"{name} {version}: {error}")
                continue
            if not ok:
                violations.append(f"{name} {version}: license '{expression}' is not on the allowed list")
    return sorted(violations), len(seen)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--root", default=".", help="repository root; every obj/project.assets.json below it is read")
    parser.add_argument("--allowed", default="eng/allowed-licenses.txt")
    parser.add_argument("--overrides", default="eng/license-overrides.txt")
    args = parser.parse_args(argv)

    try:
        allowed = parse_allowed(Path(args.allowed).read_text(encoding="utf-8"))
        overrides_path = Path(args.overrides)
        overrides = parse_overrides(overrides_path.read_text(encoding="utf-8")) if overrides_path.is_file() else {}
    except (OSError, ExpressionError) as error:
        print(f"error: {error}", file=sys.stderr)
        return 2

    assets_files = sorted(Path(args.root).rglob("obj/project.assets.json"))
    if not assets_files:
        print("error: no project.assets.json found; restore the solution first", file=sys.stderr)
        return 2

    violations, count = check(assets_files, allowed, overrides)
    for violation in violations:
        print(f"error: {violation}", file=sys.stderr)
    if violations:
        return 1
    print(f"Licenses: {count} package(s) in {len(assets_files)} project(s), all on the allowed list.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
