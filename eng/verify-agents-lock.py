#!/usr/bin/env python3
"""Fail the build unless `deploy/agents.lock` matches what the vendors publish and sign (Story 3.1, AD-8).

Usage: verify-agents-lock.py [lock-file]   (default: deploy/agents.lock; needs `gpg` and `cosign` on the PATH)

This is the check that runs when a pin changes; the host does not repeat it at start, where the digest in the lock is the authority.
  * Claude Code: the release manifest is signed by Anthropic's release key. The check verifies the signature with the key whose
    fingerprint is pinned below, then requires every checksum in the lock to be the one the signed manifest holds.
  * Codex CLI: the release publishes a digest for each archive and a sigstore signature for each executable, made by the release workflow
    of openai/codex at the release's tag. The check requires the archive digest in the lock to be the published one, extracts the
    executable and requires its digest to be the one in the lock, and verifies the signature over it with cosign.
Every url in the lock must be on the vendor's official channel, at the pinned version.
"""

from __future__ import annotations

import argparse
import hashlib
import io
import json
import os
import re
import shutil
import subprocess
import sys
import tarfile
import tempfile
import urllib.request
from dataclasses import dataclass
from pathlib import Path

REPOSITORY_ROOT = Path(__file__).resolve().parent.parent
LOCK_FILE = Path("deploy") / "agents.lock"

CLAUDE_RELEASES = "https://downloads.claude.ai/claude-code-releases/"
CLAUDE_KEY_URL = "https://downloads.claude.ai/keys/claude-code.asc"
CLAUDE_KEY_FINGERPRINT = "31DDDE24DDFAB679F42D7BD2BAA929FF1A7ECACE"
CODEX_API = "https://api.github.com/repos/openai/codex/releases/tags/rust-v{version}"
CODEX_DOWNLOADS = "https://github.com/openai/codex/releases/download/rust-v{version}/"
CODEX_IDENTITY = "https://github.com/openai/codex/.github/workflows/rust-release.yml@refs/tags/rust-v{version}"
OIDC_ISSUER = "https://token.actions.githubusercontent.com"
CODEX_TRIPLES = {"x64": "x86_64-unknown-linux-musl", "arm64": "aarch64-unknown-linux-musl"}
CODEX_ASSETS = {"codex": "codex", "codex-code-mode-host": "codex-code-mode-host"}
DIGEST = re.compile(r"^[0-9a-f]{64}$")


class LockError(ValueError):
    """The lock file cannot be read, or a pin does not match what the vendor publishes."""


@dataclass(frozen=True)
class Entry:
    agent: str
    version: str
    arch: str
    libc: str
    file: str
    sha256: str
    url: str
    archive_sha256: str | None
    line: int

    @property
    def name(self) -> str:
        return f"{self.agent}/{self.arch}-{self.libc}/{self.file}"


def parse_lock(text: str) -> list[Entry]:
    entries = []
    for number, raw in enumerate(text.splitlines(), start=1):
        line = raw.strip()
        if not line or line.startswith("#"):
            continue
        columns = line.split()
        if len(columns) not in (7, 8):
            raise LockError(f"line {number}: expected 7 or 8 columns, found {len(columns)}")
        agent, version, arch, libc, file, sha256, url, *archive = columns
        for label, digest in [("sha256", sha256), *[("archive-sha256", a) for a in archive]]:
            if not DIGEST.match(digest):
                raise LockError(f"line {number}: {label} is not a lower-case SHA-256")
        if not url.startswith("https://"):
            raise LockError(f"line {number}: url is not https")
        entries.append(Entry(agent, version, arch, libc, file, sha256, url, archive[0] if archive else None, number))
    if not entries:
        raise LockError("the lock pins nothing")
    return entries


def check_channels(entries: list[Entry]) -> list[str]:
    """The urls of the lock must be the vendor's official ones at the pinned version, so a pin cannot point somewhere else."""
    problems = []
    for entry in entries:
        if entry.agent == "claude-code":
            platform = f"linux-{entry.arch}" + ("-musl" if entry.libc == "musl" else "")
            expected = f"{CLAUDE_RELEASES}{entry.version}/{platform}/claude"
            if entry.libc == "static" or entry.file != "claude" or entry.archive_sha256 is not None:
                problems.append(f"{entry.name}: Claude Code is one plain executable per C library")
        elif entry.agent == "codex-cli":
            triple = CODEX_TRIPLES.get(entry.arch)
            asset = CODEX_ASSETS.get(entry.file)
            if triple is None or asset is None or entry.libc != "static":
                problems.append(f"{entry.name}: not a Codex CLI executable")
                continue
            expected = f"{CODEX_DOWNLOADS.format(version=entry.version)}{asset}-{triple}.tar.gz"
            if entry.archive_sha256 is None:
                problems.append(f"{entry.name}: Codex CLI is an archive and the lock must pin its digest")
        else:
            problems.append(f"{entry.name}: unknown agent")
            continue
        if entry.url != expected:
            problems.append(f"{entry.name}: the url is {entry.url}, the vendor's channel is {expected}")
    for agent in {entry.agent for entry in entries}:
        if len({entry.version for entry in entries if entry.agent == agent}) != 1:
            problems.append(f"{agent}: more than one version is pinned")
    return problems


class Tools:
    """What the check needs from the world: the vendors' servers and the two verifiers. Tests replace it."""

    def fetch(self, url: str) -> bytes:
        request = urllib.request.Request(url, headers={"User-Agent": "hephaestus-verify-agents-lock"})
        token = os.environ.get("GITHUB_TOKEN")
        if token and url.startswith("https://api.github.com/"):
            # Not forwarded if the API ever redirects somewhere else.
            request.add_unredirected_header("Authorization", f"Bearer {token}")
        with urllib.request.urlopen(request, timeout=120) as response:
            return response.read()

    def gpg_verify(self, key: bytes, signature: bytes, manifest: bytes) -> str:
        """Returns the fingerprint of the key that made a good signature, or raises LockError."""
        if shutil.which("gpg") is None:
            raise LockError("gpg is not installed")
        with tempfile.TemporaryDirectory() as home:
            os.chmod(home, 0o700)
            env = {**os.environ, "GNUPGHOME": home}
            base = Path(home)
            (base / "key.asc").write_bytes(key)
            (base / "manifest.json").write_bytes(manifest)
            (base / "manifest.json.sig").write_bytes(signature)
            try:
                subprocess.run(["gpg", "--batch", "--import", "key.asc"], cwd=home, env=env, check=True, capture_output=True)
                fingerprints = subprocess.run(
                    ["gpg", "--batch", "--with-colons", "--fingerprint"], cwd=home, env=env, check=True, capture_output=True, text=True
                ).stdout
            except subprocess.CalledProcessError as error:
                raise LockError(f"the key at {CLAUDE_KEY_URL} cannot be imported: {error.stderr.decode(errors='replace').strip()}") from error
            if not any(line.startswith("fpr:") and line.split(":")[9] == CLAUDE_KEY_FINGERPRINT for line in fingerprints.splitlines()):
                raise LockError(f"the key at {CLAUDE_KEY_URL} does not have the pinned fingerprint {CLAUDE_KEY_FINGERPRINT}")
            result = subprocess.run(
                ["gpg", "--batch", "--status-fd", "1", "--verify", "manifest.json.sig", "manifest.json"],
                cwd=home, env=env, capture_output=True, text=True,
            )
            statuses = [line.split() for line in result.stdout.splitlines() if line.startswith("[GNUPG:]")]
            kinds = {parts[1] for parts in statuses if len(parts) > 1}
            if "GOODSIG" in kinds and not kinds & {"BADSIG", "ERRSIG", "REVKEYSIG"}:
                for parts in statuses:
                    if len(parts) > 2 and parts[1] == "VALIDSIG":
                        return parts[-1]
            raise LockError("the signature over the Claude Code manifest does not verify")

    def cosign_verify(self, bundle: bytes, executable: bytes, identity: str) -> None:
        if shutil.which("cosign") is None:
            raise LockError("cosign is not installed")
        with tempfile.TemporaryDirectory() as directory:
            (Path(directory) / "bundle").write_bytes(bundle)
            (Path(directory) / "executable").write_bytes(executable)
            result = subprocess.run(
                ["cosign", "verify-blob", "--bundle", "bundle", "--certificate-identity", identity,
                 "--certificate-oidc-issuer", OIDC_ISSUER, "executable"],
                cwd=directory, capture_output=True, text=True,
            )
            if result.returncode != 0:
                raise LockError(f"the sigstore signature does not verify: {result.stderr.strip()}")


def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def verify_claude(entries: list[Entry], tools: Tools) -> list[str]:
    mine = [entry for entry in entries if entry.agent == "claude-code"]
    if not mine:
        return ["claude-code: nothing is pinned"]
    version = mine[0].version
    base = f"{CLAUDE_RELEASES}{version}/"
    manifest_bytes = tools.fetch(base + "manifest.json")
    try:
        signer = tools.gpg_verify(tools.fetch(CLAUDE_KEY_URL), tools.fetch(base + "manifest.json.sig"), manifest_bytes)
    except LockError as error:
        return [f"claude-code {version}: {error}"]
    problems = []
    if signer != CLAUDE_KEY_FINGERPRINT:
        problems.append(f"claude-code {version}: the manifest was signed by {signer}, not by the pinned key")
    manifest = json.loads(manifest_bytes)
    if manifest.get("version") != version:
        problems.append(f"claude-code: the manifest is for {manifest.get('version')}, the lock pins {version}")
    for entry in mine:
        platform = f"linux-{entry.arch}" + ("-musl" if entry.libc == "musl" else "")
        published = manifest.get("platforms", {}).get(platform, {}).get("checksum")
        if published != entry.sha256:
            problems.append(f"{entry.name}: the lock has {entry.sha256}, the signed manifest has {published}")
    return problems


def single_file(archive: bytes) -> bytes:
    with tarfile.open(fileobj=io.BytesIO(archive), mode="r:gz") as tar:
        members = tar.getmembers()
        if len(members) != 1 or not members[0].isfile():
            raise LockError("the archive does not hold exactly one file")
        extracted = tar.extractfile(members[0])
        assert extracted is not None
        return extracted.read()


def verify_codex(entries: list[Entry], tools: Tools) -> list[str]:
    mine = [entry for entry in entries if entry.agent == "codex-cli"]
    if not mine:
        return ["codex-cli: nothing is pinned"]
    version = mine[0].version
    release = json.loads(tools.fetch(CODEX_API.format(version=version)))
    published = {asset["name"]: asset.get("digest") for asset in release.get("assets", [])}
    problems = []
    for entry in mine:
        archive_name = entry.url.rsplit("/", 1)[-1]
        sigstore_name = archive_name.removesuffix(".tar.gz") + ".sigstore"
        if published.get(archive_name) != f"sha256:{entry.archive_sha256}":
            problems.append(f"{entry.name}: the lock has archive digest {entry.archive_sha256}, the release publishes {published.get(archive_name)}")
            continue
        if sigstore_name not in published:
            problems.append(f"{entry.name}: the release publishes no signature {sigstore_name}")
            continue
        try:
            archive = tools.fetch(entry.url)
            if sha256(archive) != entry.archive_sha256:
                raise LockError(f"the download is not the archive the release published ({sha256(archive)})")
            executable = single_file(archive)
            if sha256(executable) != entry.sha256:
                raise LockError(f"the executable in the archive has digest {sha256(executable)}, the lock has {entry.sha256}")
            tools.cosign_verify(tools.fetch(CODEX_DOWNLOADS.format(version=version) + sigstore_name), executable, CODEX_IDENTITY.format(version=version))
        except (LockError, tarfile.TarError) as error:
            problems.append(f"{entry.name}: {error}")
    return problems


def verify(entries: list[Entry], tools: Tools, report=print) -> list[str]:
    problems = check_channels(entries)
    if problems:
        return problems
    for label, check in (("Claude Code", verify_claude), ("Codex CLI", verify_codex)):
        found = check(entries, tools)
        report(f"{label}: " + ("verified" if not found else f"{len(found)} problem(s)"))
        problems += found
    return problems


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("lock", nargs="?", default=str(REPOSITORY_ROOT / LOCK_FILE))
    arguments = parser.parse_args(argv)
    try:
        entries = parse_lock(Path(arguments.lock).read_text(encoding="utf-8"))
        problems = verify(entries, Tools())
    except (LockError, OSError, ValueError, KeyError, TypeError, subprocess.SubprocessError) as error:
        problems = [f"{type(error).__name__}: {error}" if not isinstance(error, LockError) else str(error)]
    for problem in problems:
        print(f"error: {problem}", file=sys.stderr)
    if not problems:
        print("agents.lock matches what the vendors publish and the signatures verify.")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
