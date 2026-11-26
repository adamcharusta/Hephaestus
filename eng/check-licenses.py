#!/usr/bin/env python3
"""Fail the build when a NuGet package, direct or transitive, carries a license outside the allowed list (NFR-15).

Takes no arguments. Reads every `project.assets.json` that a restore leaves under the repository, so the whole resolved closure is covered,
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

REPOSITORY_ROOT = Path(__file__).resolve().parent.parent
ALLOWED_FILE = Path("eng") / "allowed-licenses.txt"
OVERRIDES_FILE = Path("eng") / "license-overrides.txt"
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


class _Expression:
    """A recursive-descent reader of one SPDX expression: `OR` binds loosest, then `AND`, then `WITH`, then a license."""

    def __init__(self, expression: str, allowed: set[str]) -> None:
        self.expression = expression
        self.allowed = allowed
        self.tokens = TOKEN.findall(expression)
        self.position = 0

    def peek_is(self, word: str) -> bool:
        return self.position < len(self.tokens) and self.tokens[self.position].upper() == word

    def take(self) -> str:
        if self.position >= len(self.tokens):
            raise ExpressionError(f"unexpected end of '{self.expression}'")
        self.position += 1
        return self.tokens[self.position - 1]

    def evaluate(self) -> bool:
        result = self.parse_or()
        if self.position != len(self.tokens):
            raise ExpressionError(f"unexpected '{self.tokens[self.position]}' in '{self.expression}'")
        return result

    def parse_or(self) -> bool:
        result = self.parse_and()
        while self.peek_is("OR"):
            self.take()
            result = self.parse_and() or result
        return result

    def parse_and(self) -> bool:
        result = self.parse_term()
        while self.peek_is("AND"):
            self.take()
            result = self.parse_term() and result
        return result

    def parse_term(self) -> bool:
        token = self.take()
        if token == "(":
            result = self.parse_or()
            if self.take() != ")":
                raise ExpressionError(f"missing ')' in '{self.expression}'")
            return result
        if token == ")" or token.upper() in {"AND", "OR", "WITH"}:
            raise ExpressionError(f"unexpected '{token}' in '{self.expression}'")
        if self.peek_is("WITH"):
            self.take()
            self.take()
        return token.rstrip("+").casefold() in self.allowed


def satisfied(expression: str, allowed: set[str]) -> bool:
    """True when the SPDX expression can be satisfied with allowed licenses only.

    `A OR B` needs one side, `A AND B` needs both, `A WITH exception` is judged by `A`, and a trailing `+` ("or later")
    is judged by the base identifier.
    """
    return _Expression(expression, allowed).evaluate()


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


def license_problem(name: str, version: str, folders: list[Path], allowed: set[str], overrides: dict[tuple[str, str], str]) -> str | None:
    """Why one package's license is not acceptable, or None when it is."""
    nuspec = find_nuspec(name, version, folders)
    if nuspec is None:
        return "package not found in the NuGet cache; restore first"
    try:
        expression, legacy = nuspec_license(nuspec)
    except ET.ParseError as error:
        return f"unreadable .nuspec ({error})"
    expression = expression or overrides.get((name.casefold(), version.casefold()))
    if not expression:
        return f"no SPDX license expression (declares {legacy or 'nothing'}); add a reasoned override"
    try:
        acceptable = satisfied(expression, allowed)
    except ExpressionError as error:
        return str(error)
    return None if acceptable else f"license '{expression}' is not on the allowed list"


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
            problem = license_problem(name, version, folders, allowed, overrides)
            if problem:
                violations.append(f"{name} {version}: {problem}")
    return sorted(violations), len(seen)


def main(argv: list[str] | None = None, root: Path = REPOSITORY_ROOT) -> int:
    """Run the gate. `root` is the repository; it is fixed by where this script lives, never taken from the command line."""
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.parse_args(argv)

    try:
        allowed = parse_allowed((root / ALLOWED_FILE).read_text(encoding="utf-8"))
        overrides_path = root / OVERRIDES_FILE
        overrides = parse_overrides(overrides_path.read_text(encoding="utf-8")) if overrides_path.is_file() else {}
    except (OSError, ExpressionError) as error:
        print(f"error: {error}", file=sys.stderr)
        return 2

    assets_files = sorted(root.rglob("obj/project.assets.json"))
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
