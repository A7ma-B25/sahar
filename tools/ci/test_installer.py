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


class Release014Tests(unittest.TestCase):
    """0.14.0: the setup carries everything the 0.12-0.14 test build does — the settings app, her brain's process and
    the pinned llama.cpp runtime (Vulkan + CPU), never a model — and an uninstall also removes Claude Code's hooks."""

    def setUp(self):
        self.iss = read(*ISS).replace("\r\n", "\n")

    def test_uninstall_removes_claude_codes_hooks(self):
        self.assertIn('[UninstallRun]\nFilename: "{app}\\Sahar.Engine.exe"; Parameters: "--unlink-claude"; Flags: runhidden; RunOnceId: "SaharUnlinkClaude"', self.iss)
        self.assertIn('args.Contains("--unlink-claude")) return Activities.HookMode.Unlink();', read("src", "Sahar.Engine", "Program.cs"))

    def test_installs_the_settings_app_the_brain_and_the_runtime(self):
        files = re.search(r"(?ms)^\[Files\]\n(.*?)^\[", self.iss).group(1)
        for line in (
            r'Source: "{#EngineDir}\Sahar.Mind.exe"; DestDir: "{app}"; Flags: ignoreversion',
            r'Source: "{#EngineDir}\Sahar.Settings\*"; DestDir: "{app}\Sahar.Settings"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs',
            r'Source: "{#RuntimeDir}\vulkan\*"; DestDir: "{app}\runtime\vulkan"; Flags: ignoreversion recursesubdirs createallsubdirs',
            r'Source: "{#RuntimeDir}\cpu\*"; DestDir: "{app}\runtime\cpu"; Flags: ignoreversion recursesubdirs createallsubdirs',
            r'Source: "{#ThirdPartyFile}"; DestDir: "{app}"; Flags: ignoreversion',
        ):
            self.assertIn(line, files)
        self.assertNotRegex(files, r"(?i)gguf|models|cuda")   # models are downloaded in the app; CUDA on his yes

    def test_build_checks_every_part_and_says_the_size(self):
        ps = read("packaging", "installer", "build.ps1")
        for part in ("Sahar.Mind.exe", r"Sahar.Settings\Sahar.Settings.exe", r"Sahar.Settings\web\index.html",
                     r"vulkan\llama-server.exe", r"cpu\llama-server.exe", "THIRD-PARTY.txt", "/DRuntimeDir=", "/DThirdPartyFile="):
            self.assertIn(part, ps)
        self.assertIn("*.gguf", ps)
        self.assertIn("setup size:", ps)

    def test_ui_test_builds_the_setup_with_both_runtimes(self):
        yml = read(".github", "workflows", "ui-test.yml")
        self.assertIn("-Runtime artifacts/mind/runtime", yml)

    def test_the_settings_app_is_closed_too_on_upgrade_and_uninstall(self):
        self.assertIn(r"SettingsMutex = 'Local\Sahar.Settings';", self.iss)
        for fn in ("function PrepareToInstall(", "function QuitSahar("):
            body = self.iss[self.iss.index(fn):]
            body = body[:body.index("\nend;")]
            self.assertIn("EngineMutex + ',' + SettingsMutex", body, fn)
        cs = read("src", "Sahar.Engine", "Host", "QuitRunning.cs")
        self.assertIn('"SaharSettingsWindow"', cs)
        self.assertIn(r'@"Local\Sahar.Settings"', cs)
        self.assertIn('"Sahar.Mind"', cs)
        self.assertNotRegex(cs, r"\.Kill\(")
        self.assertIn('ClassName = "SaharSettingsWindow"', read("src", "Sahar.Settings", "HostWindow.cs"))

    def test_signing_is_skipped_unless_publishing_and_fails_loudly_without_the_key(self):
        yml = read(".github", "workflows", "test-build.yml")
        sign = re.search(r"- name: Sign the update manifest[\s\S]*?(?=\n      - )", yml).group(0)
        self.assertIn("if: startsWith(github.ref, 'refs/tags/') || inputs.publish", sign)
        self.assertIn("if (-not $env:SAHAR_UPDATE_KEY) { throw", sign)
        self.assertIn("throw 'SAHAR_UPDATE_KEY is not set", read("packaging", "installer", "manifest.ps1"))


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
