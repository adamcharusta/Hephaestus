import contextlib
import importlib.util
import io
import json
import tempfile
import unittest
from pathlib import Path

SPEC = importlib.util.spec_from_file_location("check_licenses", Path(__file__).with_name("check-licenses.py"))
check_licenses = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(check_licenses)

ALLOWED = {"mit", "apache-2.0", "bsd-3-clause"}


class Expressions(unittest.TestCase):
    def test_a_single_allowed_license(self):
        self.assertTrue(check_licenses.satisfied("MIT", ALLOWED))

    def test_matching_ignores_case(self):
        self.assertTrue(check_licenses.satisfied("apache-2.0", ALLOWED))

    def test_a_license_off_the_list_is_refused(self):
        self.assertFalse(check_licenses.satisfied("GPL-3.0-only", ALLOWED))

    def test_or_needs_one_allowed_side(self):
        self.assertTrue(check_licenses.satisfied("GPL-3.0-only OR MIT", ALLOWED))
        self.assertFalse(check_licenses.satisfied("GPL-3.0-only OR AGPL-3.0-only", ALLOWED))

    def test_and_needs_both_sides(self):
        self.assertTrue(check_licenses.satisfied("MIT AND Apache-2.0", ALLOWED))
        self.assertFalse(check_licenses.satisfied("MIT AND GPL-3.0-only", ALLOWED))

    def test_and_binds_tighter_than_or(self):
        self.assertTrue(check_licenses.satisfied("GPL-3.0-only AND MIT OR Apache-2.0", ALLOWED))
        self.assertFalse(check_licenses.satisfied("GPL-3.0-only AND (MIT OR Apache-2.0)", ALLOWED))

    def test_parentheses_group(self):
        self.assertTrue(check_licenses.satisfied("(MIT OR GPL-3.0-only) AND Apache-2.0", ALLOWED))

    def test_a_with_exception_is_judged_by_its_license(self):
        self.assertTrue(check_licenses.satisfied("Apache-2.0 WITH LLVM-exception", ALLOWED))
        self.assertFalse(check_licenses.satisfied("GPL-2.0-only WITH Classpath-exception-2.0", ALLOWED))

    def test_or_later_is_judged_by_the_base_identifier(self):
        self.assertTrue(check_licenses.satisfied("Apache-2.0+", ALLOWED))

    def test_a_malformed_expression_raises(self):
        for expression in ["MIT AND", "(MIT", "MIT)", "OR MIT", "MIT MIT", ""]:
            with self.subTest(expression=expression), self.assertRaises(check_licenses.ExpressionError):
                check_licenses.satisfied(expression, ALLOWED)


class Lists(unittest.TestCase):
    def test_allowed_skips_comments_and_blank_lines(self):
        self.assertEqual(check_licenses.parse_allowed("# note\n\nMIT\nApache-2.0  # fine\n"), {"mit", "apache-2.0"})

    def test_an_override_needs_a_reason(self):
        with self.assertRaises(check_licenses.ExpressionError):
            check_licenses.parse_overrides("Some.Package 1.0.0 MIT\n")

    def test_an_override_needs_a_version_and_an_expression(self):
        for line in ["Some.Package  # reason\n", "Some.Package 1.0.0  # reason\n", "Some.Package MIT  # reason\n"]:
            with self.subTest(line=line), self.assertRaises(check_licenses.ExpressionError):
                check_licenses.parse_overrides(line)

    def test_an_override_is_keyed_by_package_and_version_without_regard_to_case(self):
        parsed = check_licenses.parse_overrides("Some.Package 1.0.0-Beta MIT AND Apache-2.0  # the repository says so\n")
        self.assertEqual(parsed, {("some.package", "1.0.0-beta"): "MIT AND Apache-2.0"})

    def test_the_shipped_allowed_list_refuses_copyleft_and_proprietary_licenses(self):
        shipped = check_licenses.parse_allowed(Path(__file__).with_name("allowed-licenses.txt").read_text(encoding="utf-8"))
        self.assertTrue(check_licenses.satisfied("MIT", shipped))
        for license_id in ["GPL-2.0-only", "GPL-3.0-or-later", "AGPL-3.0-only", "LGPL-3.0-only", "SSPL-1.0", "LicenseRef-Proprietary"]:
            with self.subTest(license=license_id):
                self.assertFalse(check_licenses.satisfied(license_id, shipped))


class Restore(unittest.TestCase):
    """A fake restore: a package cache holding .nuspec files and a project.assets.json that points at it."""

    def setUp(self):
        self._directory = tempfile.TemporaryDirectory()
        self.addCleanup(self._directory.cleanup)
        self.root = Path(self._directory.name)
        self.cache = self.root / "cache"
        self.assets = self.root / "src" / "A" / "obj" / "project.assets.json"
        self.assets.parent.mkdir(parents=True)
        self.libraries: dict[str, dict[str, str]] = {}

    def package(self, name: str, version: str, license_xml: str, kind: str = "package"):
        self.libraries[f"{name}/{version}"] = {"type": kind}
        folder = self.cache / name.lower() / version
        folder.mkdir(parents=True)
        (folder / f"{name.lower()}.nuspec").write_text(
            f'<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd"><metadata>{license_xml}</metadata></package>'
        )

    def write_assets(self):
        self.assets.write_text(json.dumps({"libraries": self.libraries, "packageFolders": {str(self.cache): {}}}))

    def check(self, overrides: dict[str, str] | None = None):
        self.write_assets()
        return check_licenses.check([self.assets], ALLOWED, overrides or {})

    def test_allowed_packages_pass(self):
        self.package("Good.One", "1.0.0", '<license type="expression">MIT</license>')
        self.package("Good.Two", "2.0.0", '<license type="expression">Apache-2.0</license>')
        violations, count = self.check()
        self.assertEqual((violations, count), ([], 2))

    def test_a_package_outside_the_list_is_named_with_its_license(self):
        self.package("Bad.One", "1.0.0", '<license type="expression">GPL-3.0-only</license>')
        violations, _ = self.check()
        self.assertEqual(violations, ["Bad.One 1.0.0: license 'GPL-3.0-only' is not on the allowed list"])

    def test_a_project_library_is_not_a_package(self):
        self.libraries["Hephaestus.Core/1.0.0"] = {"type": "project"}
        self.assertEqual(self.check(), ([], 0))

    def test_a_legacy_license_url_needs_an_override(self):
        self.package("Old.One", "1.0.0", "<licenseUrl>https://example.org/license</licenseUrl>")
        violations, _ = self.check()
        self.assertIn("Old.One 1.0.0: no SPDX license expression (declares https://example.org/license)", violations[0])

    def test_an_override_supplies_the_expression(self):
        self.package("Old.One", "1.0.0", "<licenseUrl>https://example.org/license</licenseUrl>")
        self.assertEqual(self.check({("old.one", "1.0.0"): "MIT"}), ([], 1))

    def test_an_override_for_another_version_does_not_apply(self):
        self.package("Old.One", "2.0.0", "<licenseUrl>https://example.org/license</licenseUrl>")
        violations, _ = self.check({("old.one", "1.0.0"): "MIT"})
        self.assertEqual(len(violations), 1)

    def test_an_override_does_not_beat_a_declared_expression(self):
        self.package("Bad.One", "1.0.0", '<license type="expression">GPL-3.0-only</license>')
        violations, _ = self.check({("bad.one", "1.0.0"): "MIT"})
        self.assertEqual(len(violations), 1)

    def test_a_prerelease_version_is_found_in_the_lowercase_cache_folder(self):
        self.libraries["Pre.One/1.0.0-Beta.1"] = {"type": "package"}
        folder = self.cache / "pre.one" / "1.0.0-beta.1"
        folder.mkdir(parents=True)
        (folder / "pre.one.nuspec").write_text('<package><metadata><license type="expression">MIT</license></metadata></package>')
        self.assertEqual(self.check(), ([], 1))

    def test_a_malformed_nuspec_is_reported_against_its_package(self):
        self.package("Broken.One", "1.0.0", "")
        (self.cache / "broken.one" / "1.0.0" / "broken.one.nuspec").write_text("<package><metadata>")
        violations, _ = self.check()
        self.assertIn("Broken.One 1.0.0: unreadable .nuspec", violations[0])

    def test_a_malformed_restore_result_is_reported(self):
        self.assets.write_text("{ not json")
        violations, _ = check_licenses.check([self.assets], ALLOWED, {})
        self.assertIn("unreadable restore result", violations[0])

    def test_a_license_file_without_an_expression_needs_an_override(self):
        self.package("File.One", "1.0.0", '<license type="file">LICENSE.txt</license>')
        violations, _ = self.check()
        self.assertIn("declares file:LICENSE.txt", violations[0])

    def test_a_package_missing_from_the_cache_fails(self):
        self.libraries["Ghost/1.0.0"] = {"type": "package"}
        violations, _ = self.check()
        self.assertIn("Ghost 1.0.0: package not found in the NuGet cache", violations[0])

    def test_a_malformed_expression_is_reported(self):
        self.package("Odd.One", "1.0.0", '<license type="expression">MIT AND</license>')
        violations, _ = self.check()
        self.assertIn("Odd.One 1.0.0: unexpected end", violations[0])

    def test_a_package_used_by_two_projects_is_checked_once(self):
        self.package("Good.One", "1.0.0", '<license type="expression">MIT</license>')
        self.write_assets()
        _, count = check_licenses.check([self.assets, self.assets], ALLOWED, {})
        self.assertEqual(count, 1)


class Main(unittest.TestCase):
    def setUp(self):
        self._directory = tempfile.TemporaryDirectory()
        self.addCleanup(self._directory.cleanup)
        self.root = Path(self._directory.name)
        (self.root / "allowed.txt").write_text("MIT\n")

    def run_main(self, *extra: str) -> tuple[int, str]:
        err = io.StringIO()
        with contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(err):
            code = check_licenses.main(["--root", str(self.root), "--allowed", str(self.root / "allowed.txt"),
                                        "--overrides", str(self.root / "none.txt"), *extra])
        return code, err.getvalue()

    def test_no_restore_fails(self):
        code, err = self.run_main()
        self.assertEqual(code, 2)
        self.assertIn("restore the solution first", err)

    def test_an_unreadable_allowed_list_fails(self):
        (self.root / "allowed.txt").unlink()
        self.assertEqual(self.run_main()[0], 2)

    def test_an_empty_restore_passes(self):
        assets = self.root / "src" / "A" / "obj" / "project.assets.json"
        assets.parent.mkdir(parents=True)
        assets.write_text(json.dumps({"libraries": {}}))
        self.assertEqual(self.run_main()[0], 0)

    def test_a_violation_exits_non_zero(self):
        cache = self.root / "cache" / "bad" / "1.0.0"
        cache.mkdir(parents=True)
        (cache / "bad.nuspec").write_text('<package><metadata><license type="expression">GPL-3.0-only</license></metadata></package>')
        assets = self.root / "src" / "A" / "obj" / "project.assets.json"
        assets.parent.mkdir(parents=True)
        assets.write_text(json.dumps({"libraries": {"Bad/1.0.0": {"type": "package"}}, "packageFolders": {str(self.root / "cache"): {}}}))
        code, err = self.run_main()
        self.assertEqual(code, 1)
        self.assertIn("GPL-3.0-only", err)


if __name__ == "__main__":
    unittest.main()
