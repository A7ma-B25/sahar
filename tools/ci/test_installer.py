"""The installer's promises to the owner (roadmap item: installer + auto-update), pinned where a typo would break
them silently: per-user, no admin, %LocalAppData%\\Programs\\Sahar, Arabic first, «سحر» in the Start menu, start with
Windows (on by default, removed when off), Sahar closed with her own quit (never by Restart Manager), data kept unless
you say yes; the Inno Setup used is the pinned official build; both workflows build it and the release carries the
setup and its .sha256 beside the zip."""
import os
import re
import unittest

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))


def raw(*parts):
    with open(os.path.join(ROOT, *parts), "rb") as f:
        return f.read()


def read(*parts):
    return raw(*parts).decode("utf-8-sig")


ISS = ("packaging", "installer", "Sahar.iss")


class InstallerScriptTests(unittest.TestCase):
    def setUp(self):
        self.iss = read(*ISS)

    def directive(self, name):
        m = re.search(r"^" + name + r"=(.*)$", self.iss, re.M)
        self.assertIsNotNone(m, name)
        return m.group(1).strip()

    def test_is_utf8_with_bom_for_the_arabic_text(self):
        self.assertTrue(raw(*ISS).startswith(b"\xef\xbb\xbf"))

    def test_per_user_no_admin_in_local_app_data_programs(self):
        self.assertEqual("lowest", self.directive("PrivilegesRequired"))
        self.assertEqual(r"{userpf}\Sahar", self.directive("DefaultDirName"))
        self.assertEqual("yes", self.directive("DisableDirPage"))

    def test_a_fixed_app_id_so_upgrades_find_the_old_install(self):
        self.assertRegex(self.directive("AppId"), r"^\{\{[0-9A-F]{8}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{12}\}$")

    def test_arabic_first_then_english(self):
        langs = re.findall(r'^Name: "(\w+)"; MessagesFile: "([^"]+)"', self.iss, re.M)
        self.assertEqual([("arabic", r"compiler:Languages\Arabic.isl"), ("english", "compiler:Default.isl")], langs)
        self.assertEqual("no", self.directive("ShowLanguageDialog"))
        for key in ("StartWithWindows", "LaunchSahar", "StillRunning", "RemoveData"):
            self.assertRegex(self.iss, r"(?m)^arabic\." + key + "=")
            self.assertRegex(self.iss, r"(?m)^english\." + key + "=")

    def test_start_menu_shortcut_is_sahar_in_arabic(self):
        self.assertIn(r'Name: "{userprograms}\سحر"; Filename: "{app}\Sahar.Engine.exe"', self.iss)

    def test_start_with_windows_on_by_default_and_removed_when_off(self):
        task = re.search(r'^Name: "startup";.*$', self.iss, re.M).group(0)
        self.assertNotIn("unchecked", task)
        run = r'Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; '
        self.assertIn(run + r'ValueType: string; ValueName: "Sahar"; ValueData: """{app}\Sahar.Engine.exe"""; Tasks: startup; Flags: uninsdeletevalue', self.iss)
        self.assertIn(run + 'ValueType: none; ValueName: "Sahar"; Tasks: not startup; Flags: deletevalue', self.iss)

    def test_closes_sahar_with_her_own_quit_never_restart_manager(self):
        self.assertEqual("no", self.directive("CloseApplications"))
        self.assertIn("'--quit'", self.iss)
        self.assertIn(r"Local\Sahar.Engine", self.iss)
        self.assertRegex(self.iss, r"function PrepareToInstall\(")
        self.assertRegex(self.iss, r"function InitializeUninstall\(")
        self.assertNotRegex(self.iss, r"(?i)taskkill|TerminateProcess")

    def test_starts_her_after_install_even_when_silent(self):
        run = re.search(r'^Filename: "\{app\}\\Sahar.Engine.exe";.*$', self.iss, re.M).group(0)
        self.assertIn("postinstall", run)
        self.assertNotIn("skipifsilent", run)

    def test_uninstall_asks_before_removing_data_default_keep(self):
        self.assertIn("MB_YESNO or MB_DEFBUTTON2", self.iss)
        self.assertIn("not UninstallSilent", self.iss)
        self.assertIn(r"{userappdata}\Sahar", self.iss)
        self.assertIn(r"{localappdata}\Sahar", self.iss)


    def test_the_app_id_is_the_one_the_engine_reads(self):
        cs = read("src", "Sahar.Core", "Update", "UpdateRules.cs")
        app_id = re.search(r'InstallerAppId = "(\{[0-9A-F-]+\})"', cs).group(1)
        self.assertEqual("{" + app_id, self.directive("AppId"))
        self.assertIn(app_id + "_is1", self.iss)

    def test_review_a_setup_that_does_not_finish_starts_her_again(self):
        self.assertIn("--quit --remember", self.iss)
        self.assertRegex(self.iss, r"procedure DeinitializeSetup\(\);")
        self.assertIn("QuitByUs and not Finished", self.iss)
        self.assertIn("CurStep = ssPostInstall then Finished := True", self.iss)

    def test_review_an_older_setup_asks_before_overwriting_default_no(self):
        self.assertRegex(self.iss, r"function InitializeSetup\(\): Boolean;")
        self.assertIn("'DisplayVersion'", self.iss)
        self.assertIn("MB_YESNO or MB_DEFBUTTON2, IDNO) = IDYES", self.iss)
        for lang in ("arabic", "english"):
            self.assertRegex(self.iss, r"(?m)^" + lang + r"\.Downgrade=.*%1.*%2")

    def test_review_the_icon_and_the_update_downloads(self):
        self.assertEqual(r"{#IconFile}", self.directive("SetupIconFile"))
        self.assertIn(r'"..\..\src\Sahar.Engine\assets\sahar.ico"', self.iss)
        self.assertIn(r'Type: filesandordirs; Name: "{%TEMP}\Sahar-update"', self.iss)
        self.assertIn(r"<ApplicationIcon>assets\sahar.ico</ApplicationIcon>", read("src", "Sahar.Engine", "Sahar.Engine.csproj"))


class BuildScriptTests(unittest.TestCase):
    def test_review_retries_the_inno_download(self):
        ps = read("packaging", "installer", "build.ps1")
        self.assertIn("$try -ge 3", ps)

    def test_pins_the_official_inno_setup_and_checks_its_hash(self):
        ps = read("packaging", "installer", "build.ps1")
        self.assertIn("https://github.com/jrsoftware/issrc/releases/download/is-6_7_4/innosetup-6.7.4.exe", ps)
        self.assertIn("a549929319f9120716677c685535ca4671e4b937b5fdc87e2585f32d8eb30e8c", ps)
        self.assertIn("Get-FileHash", ps)
        self.assertIn(".sha256", ps)


class WorkflowTests(unittest.TestCase):
    def test_both_workflows_build_the_installer(self):
        for wf in ("ui-test.yml", "test-build.yml"):
            self.assertIn("packaging/installer/build.ps1", read(".github", "workflows", wf), wf)

    def test_ui_jobs_get_the_installer(self):
        yml = read(".github", "workflows", "ui-test.yml")
        self.assertEqual(2, len(re.findall(r"name: installer\n", yml)), "uploaded by build, downloaded by ui")

    def test_review_only_main_signs_and_publishes(self):
        yml = read(".github", "workflows", "test-build.yml")
        sign = re.search(r"- name: Sign the update manifest[\s\S]*?(?=\n      - )", yml).group(0)
        self.assertIn("SAHAR_UPDATE_KEY: ${{ secrets.SAHAR_UPDATE_KEY }}", sign)
        self.assertIn("if ($env:GITHUB_REF -ne 'refs/heads/main') { throw", sign)
        self.assertIn("packaging/installer/manifest.ps1", sign)
        publish = re.search(r"- name: Publish the \"test-build\" pre-release[\s\S]*", yml).group(0)
        self.assertIn("if ($env:GITHUB_REF -ne 'refs/heads/main') { throw", publish)
        self.assertNotIn("SAHAR_UPDATE_KEY", yml.replace(sign, ""))   # the key reaches the signing step only

    def test_release_carries_setup_and_checksum_beside_the_zip(self):
        yml = read(".github", "workflows", "test-build.yml")
        create = re.search(r"gh release create test-build[^\n]*", yml).group(0)
        for f in ("Sahar-$env:SAHAR_VERSION-test-win-x64.zip", "Sahar-Setup-$env:SAHAR_VERSION.exe", "Sahar-Setup-$env:SAHAR_VERSION.exe.sha256",
                  "artifacts/installer/Sahar-update.json", "artifacts/installer/Sahar-update.json.sig"):
            self.assertIn(f, create)


if __name__ == "__main__":
    unittest.main()
