import hashlib
import importlib.util
import io
import json
import os
import shutil
import subprocess
import sys
import tarfile
import tempfile
import unittest
from unittest import mock
from pathlib import Path

SPEC = importlib.util.spec_from_file_location("verify_agents_lock", Path(__file__).with_name("verify-agents-lock.py"))
verify = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = verify  # a dataclass looks its module up here
SPEC.loader.exec_module(verify)

LOCK = Path(__file__).resolve().parent.parent / "deploy" / "agents.lock"


def digest(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def archive_of(*files: tuple[str, bytes]) -> bytes:
    buffer = io.BytesIO()
    with tarfile.open(fileobj=buffer, mode="w:gz") as tar:
        for name, content in files:
            info = tarfile.TarInfo(name)
            info.size = len(content)
            tar.addfile(info, io.BytesIO(content))
    return buffer.getvalue()


class TheLockInTheRepository(unittest.TestCase):
    def setUp(self):
        self.entries = verify.parse_lock(LOCK.read_text(encoding="utf-8"))

    def test_it_pins_every_executable_per_architecture(self):
        self.assertEqual(
            sorted(entry.name for entry in self.entries),
            sorted(
                [f"claude-code/{arch}-{libc}/claude" for arch in ("x64", "arm64") for libc in ("glibc", "musl")]
                + [f"codex-cli/{arch}-static/{file}" for arch in ("x64", "arm64") for file in ("codex", "codex-code-mode-host")]
            ),
        )

    def test_every_url_is_on_the_vendors_official_channel(self):
        self.assertEqual(verify.check_channels(self.entries), [])

    def test_codex_is_pinned_by_archive_and_by_executable_and_claude_by_executable_alone(self):
        for entry in self.entries:
            self.assertEqual(entry.archive_sha256 is not None, entry.agent == "codex-cli", entry.name)
            self.assertNotEqual(entry.archive_sha256, entry.sha256)


class Parsing(unittest.TestCase):
    def test_a_line_with_the_wrong_number_of_columns_is_refused(self):
        with self.assertRaisesRegex(verify.LockError, "line 2"):
            verify.parse_lock("# comment\nclaude-code 1.0.0 x64 glibc claude\n")

    def test_a_digest_that_is_not_lower_case_hex_is_refused(self):
        with self.assertRaisesRegex(verify.LockError, "sha256"):
            verify.parse_lock("claude-code 1.0.0 x64 glibc claude " + "A" * 64 + " https://x.test/a\n")

    def test_a_url_that_is_not_https_is_refused(self):
        with self.assertRaisesRegex(verify.LockError, "https"):
            verify.parse_lock("claude-code 1.0.0 x64 glibc claude " + "a" * 64 + " http://x.test/a\n")

    def test_an_empty_lock_is_refused(self):
        with self.assertRaises(verify.LockError):
            verify.parse_lock("# nothing\n")


class Channels(unittest.TestCase):
    def setUp(self):
        self.entries = verify.parse_lock(LOCK.read_text(encoding="utf-8"))

    def replaced(self, name, **changes):
        return [verify.Entry(**{**entry.__dict__, **changes}) if entry.name == name else entry for entry in self.entries]

    def test_a_url_on_another_host_is_refused(self):
        entries = self.replaced("claude-code/x64-glibc/claude", url="https://evil.test/claude")
        self.assertTrue(any("claude-code/x64-glibc/claude" in problem for problem in verify.check_channels(entries)))

    def test_a_url_for_another_version_is_refused(self):
        entry = next(e for e in self.entries if e.name == "codex-cli/x64-static/codex")
        entries = self.replaced(entry.name, url=entry.url.replace("rust-v0.172.0", "rust-v0.1.0"))
        self.assertTrue(any("codex-cli/x64-static/codex" in problem for problem in verify.check_channels(entries)))

    def test_a_codex_pin_without_an_archive_digest_is_refused(self):
        entries = self.replaced("codex-cli/x64-static/codex", archive_sha256=None)
        self.assertTrue(any("archive" in problem for problem in verify.check_channels(entries)))

    def test_two_versions_of_one_agent_are_refused(self):
        entries = self.replaced("claude-code/x64-glibc/claude", version="9.9.9")
        self.assertTrue(any("more than one version" in problem for problem in verify.check_channels(entries)))


class FakeTools(verify.Tools):
    def __init__(self, files=None, signer=verify.CLAUDE_KEY_FINGERPRINT, cosign_fails=False):
        self.files = files or {}
        self.signer = signer
        self.cosign_fails = cosign_fails
        self.cosigned = []

    def fetch(self, url):
        return self.files[url]

    def gpg_verify(self, key, signature, manifest):
        if signature == b"bad":
            raise verify.LockError("the signature over the Claude Code manifest does not verify")
        return self.signer

    def cosign_verify(self, bundle, executable, identity):
        if self.cosign_fails:
            raise verify.LockError("the sigstore signature does not verify: no matching signatures")
        self.cosigned.append((bundle, digest(executable), identity))


def claude_entries(checksums):
    return [
        verify.Entry("claude-code", "1.0.0", arch, libc, "claude", checksums[(arch, libc)], f"{verify.CLAUDE_RELEASES}1.0.0/linux-{arch}{'-musl' if libc == 'musl' else ''}/claude", None, 1)
        for arch in ("x64", "arm64") for libc in ("glibc", "musl")
    ]


class ClaudeCode(unittest.TestCase):
    CHECKSUMS = {(a, l): digest(f"{a}{l}".encode()) for a in ("x64", "arm64") for l in ("glibc", "musl")}

    def tools(self, manifest_checksums=None, version="1.0.0", signature=b"good", **kwargs):
        checksums = manifest_checksums or self.CHECKSUMS
        manifest = {
            "version": version,
            "platforms": {f"linux-{a}{'-musl' if l == 'musl' else ''}": {"binary": "claude", "checksum": c} for (a, l), c in checksums.items()},
        }
        base = f"{verify.CLAUDE_RELEASES}1.0.0/"
        return FakeTools(
            {base + "manifest.json": json.dumps(manifest).encode(), base + "manifest.json.sig": signature, verify.CLAUDE_KEY_URL: b"key"},
            **kwargs,
        )

    def test_checksums_the_signed_manifest_holds_verify(self):
        self.assertEqual(verify.verify_claude(claude_entries(self.CHECKSUMS), self.tools()), [])

    def test_a_checksum_the_manifest_does_not_hold_is_reported_with_both(self):
        entries = claude_entries({**self.CHECKSUMS, ("x64", "musl"): digest(b"other")})
        problems = verify.verify_claude(entries, self.tools())
        self.assertEqual(len(problems), 1)
        self.assertIn("claude-code/x64-musl/claude", problems[0])
        self.assertIn(digest(b"other"), problems[0])

    def test_a_manifest_whose_signature_does_not_verify_is_reported(self):
        problems = verify.verify_claude(claude_entries(self.CHECKSUMS), self.tools(signature=b"bad"))
        self.assertEqual(len(problems), 1)
        self.assertIn("does not verify", problems[0])

    def test_a_manifest_signed_by_another_key_is_reported(self):
        problems = verify.verify_claude(claude_entries(self.CHECKSUMS), self.tools(signer="0" * 40))
        self.assertTrue(any("not by the pinned key" in problem for problem in problems))

    def test_a_manifest_for_another_version_is_reported(self):
        problems = verify.verify_claude(claude_entries(self.CHECKSUMS), self.tools(version="2.0.0"))
        self.assertTrue(any("2.0.0" in problem for problem in problems))

    def test_a_lock_without_claude_code_is_reported(self):
        self.assertEqual(verify.verify_claude([], FakeTools()), ["claude-code: nothing is pinned"])


class CodexCli(unittest.TestCase):
    EXECUTABLE = b"the executable"

    def setUp(self):
        self.archive = archive_of(("codex-x86_64-unknown-linux-musl", self.EXECUTABLE))
        self.base = verify.CODEX_DOWNLOADS.format(version="1.0.0")
        self.url = self.base + "codex-x86_64-unknown-linux-musl.tar.gz"
        self.entry = verify.Entry("codex-cli", "1.0.0", "x64", "static", "codex", digest(self.EXECUTABLE), self.url, digest(self.archive), 1)

    def tools(self, published=None, archive=None, **kwargs):
        assets = [
            {"name": "codex-x86_64-unknown-linux-musl.tar.gz", "digest": published or f"sha256:{digest(self.archive)}"},
            {"name": "codex-x86_64-unknown-linux-musl.sigstore", "digest": "sha256:" + "0" * 64},
        ]
        return FakeTools(
            {
                verify.CODEX_API.format(version="1.0.0"): json.dumps({"assets": assets}).encode(),
                self.url: archive if archive is not None else self.archive,
                self.base + "codex-x86_64-unknown-linux-musl.sigstore": b"bundle",
            },
            **kwargs,
        )

    def test_an_archive_and_an_executable_that_match_and_a_signature_that_verifies_pass(self):
        tools = self.tools()
        self.assertEqual(verify.verify_codex([self.entry], tools), [])
        self.assertEqual(tools.cosigned, [(b"bundle", digest(self.EXECUTABLE), verify.CODEX_IDENTITY.format(version="1.0.0"))])

    def test_an_archive_digest_the_release_does_not_publish_is_reported(self):
        problems = verify.verify_codex([self.entry], self.tools(published="sha256:" + "f" * 64))
        self.assertIn("the release publishes", problems[0])

    def test_a_download_that_is_not_the_published_archive_is_reported(self):
        problems = verify.verify_codex([self.entry], self.tools(archive=archive_of(("x", b"tampered"))))
        self.assertIn("not the archive the release published", problems[0])

    def test_an_executable_that_is_not_the_one_in_the_lock_is_reported(self):
        wrong = verify.Entry(**{**self.entry.__dict__, "sha256": digest(b"other")})
        problems = verify.verify_codex([wrong], self.tools())
        self.assertIn("the executable in the archive has digest", problems[0])

    def test_an_archive_with_two_files_is_reported(self):
        archive = archive_of(("a", b"1"), ("b", b"2"))
        entry = verify.Entry(**{**self.entry.__dict__, "archive_sha256": digest(archive)})
        problems = verify.verify_codex([entry], self.tools(published=f"sha256:{digest(archive)}", archive=archive))
        self.assertIn("exactly one file", problems[0])

    def test_a_signature_that_does_not_verify_is_reported(self):
        problems = verify.verify_codex([self.entry], self.tools(cosign_fails=True))
        self.assertIn("sigstore signature does not verify", problems[0])

    def test_a_release_without_the_signature_is_reported(self):
        tools = self.tools()
        release = json.loads(tools.files[verify.CODEX_API.format(version="1.0.0")])
        release["assets"] = release["assets"][:1]
        tools.files[verify.CODEX_API.format(version="1.0.0")] = json.dumps(release).encode()
        self.assertIn("publishes no signature", verify.verify_codex([self.entry], tools)[0])

    def test_a_lock_without_codex_is_reported(self):
        self.assertEqual(verify.verify_codex([], FakeTools()), ["codex-cli: nothing is pinned"])


@unittest.skipUnless(shutil.which("gpg"), "gpg is not installed")
class GpgSignature(unittest.TestCase):
    """The real verifier, against a key made for the test, whose fingerprint stands in for the pinned one."""

    @classmethod
    def setUpClass(cls):
        cls.home = tempfile.TemporaryDirectory()
        os.chmod(cls.home.name, 0o700)
        cls.manifest = b'{"version": "1.0.0"}'
        cls.key, cls.fingerprint, cls.signature = cls.make_key("Release <release@test.invalid>", cls.manifest)
        cls.other_key, cls.other_fingerprint, cls.other_signature = cls.make_key("Other <other@test.invalid>", cls.manifest)

    @classmethod
    def tearDownClass(cls):
        cls.home.cleanup()

    @classmethod
    def make_key(cls, user, manifest):
        home = tempfile.mkdtemp(dir=cls.home.name)
        os.chmod(home, 0o700)
        env = {**os.environ, "GNUPGHOME": home}

        def gpg(*arguments, data=None):
            return subprocess.run(["gpg", "--batch", "--yes", "--pinentry-mode", "loopback", "--passphrase", "", *arguments], env=env, input=data, check=True, capture_output=True).stdout

        gpg("--quick-generate-key", user, "ed25519", "sign", "never")
        fingerprint = next(line.split(":")[9] for line in gpg("--with-colons", "--fingerprint").decode().splitlines() if line.startswith("fpr:"))
        return gpg("--armor", "--export"), fingerprint, gpg("--detach-sign", data=manifest)

    def verify_with(self, key, signature, manifest, fingerprint):
        with mock.patch.object(verify, "CLAUDE_KEY_FINGERPRINT", fingerprint):
            return verify.Tools().gpg_verify(key, signature, manifest)

    def test_a_good_signature_by_the_pinned_key_returns_its_fingerprint(self):
        self.assertEqual(self.verify_with(self.key, self.signature, self.manifest, self.fingerprint), self.fingerprint)

    def test_a_manifest_that_was_changed_after_it_was_signed_is_refused(self):
        with self.assertRaisesRegex(verify.LockError, "does not verify"):
            self.verify_with(self.key, self.signature, self.manifest + b" ", self.fingerprint)

    def test_a_key_that_is_not_the_pinned_one_is_refused(self):
        with self.assertRaisesRegex(verify.LockError, "pinned fingerprint"):
            self.verify_with(self.other_key, self.other_signature, self.manifest, self.fingerprint)

    def test_a_signature_by_another_key_is_refused(self):
        with self.assertRaisesRegex(verify.LockError, "does not verify"):
            self.verify_with(self.key, self.other_signature, self.manifest, self.fingerprint)

    def test_a_key_that_cannot_be_imported_is_refused_without_a_traceback(self):
        with self.assertRaisesRegex(verify.LockError, "cannot be imported"):
            self.verify_with(b"not a key", self.signature, self.manifest, self.fingerprint)


class Cosign(unittest.TestCase):
    def run_with(self, returncode, stderr=""):
        calls = []

        def fake(arguments, **options):
            calls.append((arguments, Path(options["cwd"], "executable").read_bytes()))
            return subprocess.CompletedProcess(arguments, returncode, "", stderr)

        with mock.patch.object(shutil, "which", return_value="/usr/bin/cosign"), mock.patch.object(subprocess, "run", fake):
            verify.Tools().cosign_verify(b"bundle", b"executable", "identity")
        return calls

    def test_it_verifies_the_blob_against_the_bundle_the_identity_and_the_issuer(self):
        (arguments, blob), = self.run_with(0)
        self.assertEqual(arguments[:2], ["cosign", "verify-blob"])
        self.assertIn("identity", arguments)
        self.assertEqual(arguments[arguments.index("--certificate-oidc-issuer") + 1], verify.OIDC_ISSUER)
        self.assertEqual(blob, b"executable")

    def test_a_failure_is_reported_with_what_cosign_said(self):
        with self.assertRaisesRegex(verify.LockError, "no matching signatures"):
            self.run_with(1, "no matching signatures")  # the one call that raises

    def test_a_missing_cosign_is_reported(self):
        tools = verify.Tools()
        with mock.patch.object(shutil, "which", return_value=None):
            with self.assertRaisesRegex(verify.LockError, "cosign is not installed"):
                tools.cosign_verify(b"", b"", "identity")


class WholeRun(unittest.TestCase):
    def test_a_lock_off_the_vendors_channel_is_refused_before_anything_is_fetched(self):
        entries = verify.parse_lock(LOCK.read_text(encoding="utf-8").replace("downloads.claude.ai", "mirror.test"))
        lines = []
        problems = verify.verify(entries, FakeTools(), report=lines.append)
        self.assertTrue(problems)
        self.assertEqual(lines, [])

    def test_main_reports_a_lock_that_cannot_be_read(self):
        with mock.patch.object(verify, "REPOSITORY_ROOT", LOCK.parent.parent / "no-such-directory"):
            self.assertEqual(verify.main(), 1)


if __name__ == "__main__":
    unittest.main()
