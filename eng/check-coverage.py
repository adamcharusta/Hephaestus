#!/usr/bin/env python3
"""Fail the build when line coverage over all .NET code is below a threshold (NFR-18, AR-76).

Reads the Cobertura reports that `dotnet test --coverlet` writes (one per test project), merges them by source line, drops
the files listed in the exclusions file, and compares the result with the threshold.

Every project under `src/` must appear in the reports: a run that collected no coverage at all fails instead of passing
because there was nothing to measure. Exclusions live in one file, `eng/coverage-exclusions.txt`. Each line is `<glob>  # <reason>`; an entry without a reason is
an error, so every exclusion says why it exists.
"""

from __future__ import annotations

import argparse
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

DEFAULT_THRESHOLD = 80.0


class ExclusionError(ValueError):
    """The exclusions file is malformed."""


class ReportError(ValueError):
    """A report is unreadable, or an input path does not exist."""


def glob_to_regex(pattern: str) -> re.Pattern[str]:
    """Translate a path glob: `*` stays inside a directory, `**` crosses directories, `?` is one character."""
    out = []
    i = 0
    while i < len(pattern):
        char = pattern[i]
        if pattern.startswith("**/", i):
            out.append("(?:.*/)?")
            i += 3
            continue
        if char == "*":
            if pattern.startswith("**", i):
                out.append(".*")
                i += 2
                continue
            out.append("[^/]*")
        elif char == "?":
            out.append("[^/]")
        else:
            out.append(re.escape(char))
        i += 1
    return re.compile("".join(out) + r"\Z")


def parse_exclusions(text: str) -> list[tuple[re.Pattern[str], str, str]]:
    """Return (compiled glob, glob, reason) for each entry."""
    entries = []
    for number, raw in enumerate(text.splitlines(), start=1):
        line = raw.strip()
        if not line or line.startswith("#"):
            continue
        pattern, separator, reason = line.partition("#")
        pattern = pattern.strip()
        reason = reason.strip()
        if not separator or not reason:
            raise ExclusionError(f"line {number}: exclusion '{pattern}' has no reason; write '<glob>  # <reason>'")
        entries.append((glob_to_regex(pattern), pattern, reason))
    return entries


def relative_path(filename: str, root: Path) -> str:
    """A source path relative to the repository root, with forward slashes.

    Coverlet writes an absolute path, or, for a continuous-integration build with deterministic paths, the same path
    without its leading slash; both are made relative to the root. A path outside the root is returned unchanged.
    """
    text = filename.replace("\\", "/")
    prefix = root.resolve().as_posix().rstrip("/") + "/"
    for candidate in (text, "/" + text.lstrip("/")):
        if candidate.startswith(prefix):
            return candidate[len(prefix):]
    return text


def collect(reports: list[Path], root: Path) -> tuple[dict[tuple[str, int], int], set[str]]:
    """Merge reports: hits per (file, line), and the names of the packages (assemblies) the reports list."""
    hits: dict[tuple[str, int], int] = {}
    packages: set[str] = set()
    for report in reports:
        try:
            tree = ET.parse(report)
        except ET.ParseError as error:
            raise ReportError(f"{report}: not a readable Cobertura file ({error})") from error
        for package in tree.getroot().iter("package"):
            packages.add(package.get("name", ""))
            for cls in package.iter("class"):
                filename = relative_path(cls.get("filename", ""), root)
                for line in cls.iter("line"):
                    key = (filename, int(line.get("number", "0")))
                    hits[key] = hits.get(key, 0) + int(line.get("hits", "0"))
    return hits, packages


def find_reports(inputs: list[str]) -> list[Path]:
    reports: list[Path] = []
    for item in inputs:
        path = Path(item)
        if path.is_dir():
            reports.extend(sorted(path.rglob("*.cobertura*.xml")))
        elif path.is_file():
            reports.append(path)
        else:
            raise ReportError(f"{item}: no such file or directory")
    return reports


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("reports", nargs="+", help="Cobertura files, or directories searched for them")
    parser.add_argument("--threshold", type=float, default=DEFAULT_THRESHOLD, help="minimum line coverage in percent")
    parser.add_argument("--exclusions", default="eng/coverage-exclusions.txt", help="the one file of exclusions")
    parser.add_argument("--root", default=".", help="repository root, to make source paths relative")
    parser.add_argument(
        "--require-package",
        action="append",
        default=[],
        help="an extra assembly that must appear in the reports",
    )
    args = parser.parse_args(argv)

    root = Path(args.root)
    try:
        exclusions = parse_exclusions(Path(args.exclusions).read_text(encoding="utf-8"))
    except (OSError, ExclusionError) as error:
        print(f"error: {args.exclusions}: {error}", file=sys.stderr)
        return 2

    try:
        reports = find_reports(args.reports)
        if not reports:
            print("error: no Cobertura report found", file=sys.stderr)
            return 2
        hits, packages = collect(reports, root)
    except ReportError as error:
        print(f"error: {error}", file=sys.stderr)
        return 2

    # Every project under src/ must appear in the reports, so a run that collected nothing cannot pass vacuously.
    required = sorted({project.stem for project in (root / "src").glob("*/*.csproj")} | set(args.require_package))
    missing = [name for name in required if name not in packages]
    if missing:
        print(f"error: not in any report: {', '.join(missing)}", file=sys.stderr)
        return 2

    excluded_files = sorted({f for f, _ in hits if any(regex.match(f) for regex, _, _ in exclusions)})
    counted = {key: value for key, value in hits.items() if key[0] not in excluded_files}
    valid = len(counted)
    covered = sum(1 for value in counted.values() if value > 0)

    for filename in excluded_files:
        print(f"excluded: {filename}")
    if valid == 0:
        print(f"Line coverage: no coverable lines in {len(reports)} report(s); nothing to hold to {args.threshold:g}%.")
        return 0

    percent = 100.0 * covered / valid
    print(f"Line coverage: {percent:.2f}% ({covered}/{valid} lines, {len({f for f, _ in counted})} files); threshold {args.threshold:g}%.")
    if percent < args.threshold:
        print(f"error: line coverage {percent:.2f}% is below {args.threshold:g}%", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
