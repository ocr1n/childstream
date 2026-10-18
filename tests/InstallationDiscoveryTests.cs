using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Win32;

static class InstallationDiscoveryTests
{
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    static object Call(string type, string method, params object[] args) {
        Type target = typeof(Program).Assembly.GetType(type); Assert(target != null, type + " is missing");
        MethodInfo action = target.GetMethod(method, BindingFlags.Public | BindingFlags.Static); Assert(action != null, method + " is missing");
        try { return action.Invoke(null, args); } catch (TargetInvocationException ex) { throw ex.InnerException; }
    }
    static void Value(RegistryKey software, string subkey, string name, string value) {
        using (var key = software.CreateSubKey(subkey)) key.SetValue(name, value);
    }
    public static void Run(Action<string, Action> check) {
        check("install path parsing supports quotes Unicode and icon indexes without expanding literal folders", delegate {
            string expected = Path.Combine(Path.GetTempPath(), "Tools, games", "日本語 %SystemRoot%", "browser.exe");
            Assert((string)Call("WindowsInstallPaths", "Executable", "\"" + expected + "\",-2") == expected, "custom registered executable changed");
            foreach (string input in new[] { "chrome.exe", "\"C:\\browser.exe\" --flag", @"\\server\share\chrome.exe", @"C:\bad.exe:stream", "https://example.com/app.exe" })
                Assert(Call("WindowsInstallPaths", "Executable", input) == null, "invalid installed path accepted: " + input);
        });
        check("registry discovery reads custom browser Steam and host locations without touching real registrations", delegate {
            string record = @"Software\ChildStream\Tests\" + Guid.NewGuid().ToString("N");
            string root = Path.Combine(Path.GetTempPath(), "childstream-registry-" + Guid.NewGuid().ToString("N"));
            try { using (var software = Registry.CurrentUser.CreateSubKey(record)) {
                string chrome = Path.Combine(root, "Browser warehouse", "chrome.exe"), steam = Path.Combine(root, "Games depot", "steam.exe"), host = Path.Combine(root, "My host", "sunshine.exe");
                Value(software, @"Microsoft\Windows\CurrentVersion\App Paths\chrome.exe", "", "\"" + chrome + "\"");
                Value(software, @"Valve\Steam", "SteamExe", steam.Replace('\\', '/'));
                Value(software, @"Microsoft\Windows\CurrentVersion\App Paths\sunshine.exe", "", host);
                var apps = (string[])Call("WindowsInstallPaths", "RegistryApps", software);
                Assert(apps.Contains(chrome) && apps.Contains(steam), "registered custom browser/Steam location missing");
                var hosts = (HostCandidate[])Call("NativeHostDiscovery", "DiscoverRegistry", software);
                Assert(hosts.Any(item => item.ExecutablePath == host), "host App Paths registration ignored");
            } } finally { Registry.CurrentUser.DeleteSubKeyTree(record, false); }
        });
        check("expandable registry values resolve environment variables exactly once", delegate {
            string record = @"Software\ChildStream\Tests\" + Guid.NewGuid().ToString("N");
            string variable = "CHILDSTREAM_PATH_" + Guid.NewGuid().ToString("N");
            string folder = Path.Combine(Path.GetTempPath(), "Browser %SystemRoot%");
            Environment.SetEnvironmentVariable(variable, folder);
            try { using (var software = Registry.CurrentUser.CreateSubKey(record)) {
                using (var key = software.CreateSubKey(@"Microsoft\Windows\CurrentVersion\App Paths\chrome.exe")) key.SetValue("", "%" + variable + "%\\chrome.exe", RegistryValueKind.ExpandString);
                var paths = (string[])Call("WindowsInstallPaths", "RegistryApps", software);
                Assert(paths.Contains(Path.Combine(folder, "chrome.exe")), "registry expansion changed a literal token in the resolved folder");
            } } finally { Environment.SetEnvironmentVariable(variable, null); Registry.CurrentUser.DeleteSubKeyTree(record, false); }
        });
        check("specific host registry branding wins over a generic App Paths registration", delegate {
            string record = @"Software\ChildStream\Tests\" + Guid.NewGuid().ToString("N");
            string path = @"C:\Custom streaming host\sunshine.exe";
            try { using (var software = Registry.CurrentUser.CreateSubKey(record)) {
                Value(software, @"Microsoft\Windows\CurrentVersion\App Paths\sunshine.exe", "", path);
                Value(software, @"Microsoft\Windows\CurrentVersion\Uninstall\Host", "DisplayName", "VibePollo");
                Value(software, @"Microsoft\Windows\CurrentVersion\Uninstall\Host", "DisplayIcon", path + ",0");
                var candidates = (HostCandidate[])Call("NativeHostDiscovery", "DiscoverRegistry", software);
                var result = HostDiscovery.Find(Path.GetTempPath(), new FixedHosts(path, candidates));
                Assert(result.Installations.Length == 1 && result.Installations[0].Kind == HostKind.VibePollo, "generic App Paths record replaced the fork's identity");
            } } finally { Registry.CurrentUser.DeleteSubKeyTree(record, false); }
        });
        check("uninstall display icons and install directories support nonstandard application locations", delegate {
            string record = @"Software\ChildStream\Tests\" + Guid.NewGuid().ToString("N");
            string root = Path.Combine(Path.GetTempPath(), "childstream-locations-" + Guid.NewGuid().ToString("N"));
            try { using (var software = Registry.CurrentUser.CreateSubKey(record)) {
                string chrome = Path.Combine(root, "User browser", "chrome.exe"), edge = Path.Combine(root, "Another browser", "msedge.exe");
                string baseKey = @"Microsoft\Windows\CurrentVersion\Uninstall\";
                Value(software, baseKey + "Browser", "DisplayName", "Google Chrome");
                Value(software, baseKey + "Browser", "DisplayIcon", "\"" + chrome + "\",0");
                Value(software, baseKey + "Edge", "DisplayName", "Microsoft Edge");
                Value(software, baseKey + "Edge", "InstallLocation", Path.GetDirectoryName(edge));
                Value(software, baseKey + "Bad", "DisplayName", "Google Chrome");
                Value(software, baseKey + "Bad", "DisplayIcon", "\"C:\\bad.exe\" --execute");
                var paths = (string[])Call("WindowsInstallPaths", "RegistryApps", software);
                Assert(paths.Contains(chrome) && paths.Contains(edge) && !paths.Contains(@"C:\bad.exe"), "uninstall location parsing failed");
            } } finally { Registry.CurrentUser.DeleteSubKeyTree(record, false); }
        });
        check("uninstall icons never promote an uninstaller or WebView runtime to a launchable browser", delegate {
            string record = @"Software\ChildStream\Tests\" + Guid.NewGuid().ToString("N");
            try { using (var software = Registry.CurrentUser.CreateSubKey(record)) {
                string baseKey = @"Microsoft\Windows\CurrentVersion\Uninstall\";
                Value(software, baseKey + "Steam", "DisplayName", "Steam"); Value(software, baseKey + "Steam", "DisplayIcon", @"C:\Games depot\uninstall.exe");
                Value(software, baseKey + "WebView", "DisplayName", "Microsoft Edge WebView2 Runtime"); Value(software, baseKey + "WebView", "DisplayIcon", @"C:\Runtime\msedgewebview2.exe");
                var paths = (string[])Call("WindowsInstallPaths", "RegistryApps", software);
                Assert(!paths.Any(path => path.EndsWith("uninstall.exe", StringComparison.OrdinalIgnoreCase) || path.EndsWith("msedgewebview2.exe", StringComparison.OrdinalIgnoreCase)), "installer/runtime icon became an application");
            } } finally { Registry.CurrentUser.DeleteSubKeyTree(record, false); }
        });
        check("Steam libraries and app manifests resolve moved Wallpaper Engine without traversing directories", delegate {
            string root = Path.Combine(Path.GetTempPath(), "childstream-libraries-" + Guid.NewGuid().ToString("N"));
            string steam = Path.Combine(root, "Steam depot"), library = Path.Combine(root, "Other library %SystemRoot% 日本語");
            Directory.CreateDirectory(Path.Combine(steam, "steamapps")); Directory.CreateDirectory(Path.Combine(library, "steamapps"));
            try {
                File.WriteAllText(Path.Combine(steam, "steamapps", "libraryfolders.vdf"), "\"libraryfolders\" { \"1\" { \"path\" \"" + library.Replace(@"\", @"\\") + "\" } }");
                string manifest = Path.Combine(library, "steamapps", "appmanifest_431960.acf");
                File.WriteAllText(manifest, "\"AppState\" { \"installdir\" \"My wallpapers\" }");
                string wallpaper = Path.Combine(library, "steamapps", "common", "My wallpapers", "wallpaper64.exe");
                var paths = (string[])Call("WindowsInstallPaths", "SteamApps", Path.Combine(steam, "steam.exe"));
                Assert(paths.Contains(wallpaper), "Steam library or manifest location ignored");
                File.WriteAllText(manifest, "\"installdir\" \"..\\outside\"");
                paths = (string[])Call("WindowsInstallPaths", "SteamApps", Path.Combine(steam, "steam.exe"));
                Assert(!paths.Any(path => path.Contains("outside")), "manifest escaped its Steam library");
            } finally { Directory.Delete(root, true); }
        });
        check("portable app discovery reuses browsed paths and skips removed or invalid candidates", delegate {
            string root = Path.Combine(Path.GetTempPath(), "childstream-portable-apps-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            try {
                string saved = Path.Combine(root, "Portable browser.exe"), found = Path.Combine(root, "Custom app.exe");
                File.WriteAllText(saved, "fixture"); File.WriteAllText(found, "fixture");
                ChildAppCatalog.SaveRegistered(Path.Combine(root, "child-apps.txt"), new[] { saved });
                var ctor = typeof(NativeAppDiscovery).GetConstructor(new[] { typeof(string), typeof(Func<IEnumerable<string>>) });
                Assert(ctor != null, "discovery has no installation-scoped saved-path source");
                var source = (IAppDiscoverySource)ctor.Invoke(new object[] { root, new Func<IEnumerable<string>>(() => new[] { Path.Combine(root, "gone.exe"), "bad.exe", found, found.ToUpperInvariant() }) });
                var paths = source.Candidates().Select(app => app.ExecutablePath).ToArray();
                Assert(paths.Contains(saved) && paths.Contains(found) && paths.Length == 2, "saved/custom app missing or invalid duplicate included");
            } finally { Directory.Delete(root, true); }
        });
        check("literal percent tokens in saved running and managed installation paths stay literal", delegate {
            string root = Path.Combine(Path.GetTempPath(), "childstream-literal-%SystemRoot%-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            try {
                string saved = Path.Combine(root, "Saved app.exe"), running = Path.Combine(root, "Running app.exe");
                File.WriteAllText(saved, "fixture"); File.WriteAllText(running, "fixture");
                ChildAppCatalog.SaveRegistered(Path.Combine(root, "child-apps.txt"), new[] { saved });
                var ctor = typeof(NativeAppDiscovery).GetConstructor(new[] { typeof(string), typeof(Func<IEnumerable<string>>) });
                Assert(ctor != null, "installation-scoped discovery is missing");
                var source = (IAppDiscoverySource)ctor.Invoke(new object[] { root, new Func<IEnumerable<string>>(() => new[] { running }) });
                var paths = source.Candidates().Select(app => app.ExecutablePath).ToArray();
                Assert(paths.Contains(saved) && paths.Contains(running), "literal path was expanded as an environment variable");
                string managed = Path.Combine(root, "Sunshine", "Sunshine", "sunshine.exe");
                Assert(new NativeHostDiscovery().Candidates(root).Any(item => item.Source == "managed" && item.ExecutablePath == managed), "managed host root was environment-expanded");
            } finally { Directory.Delete(root, true); }
        });
        check("browser shortcut resolution finds a custom browser beyond the app list display limit", delegate {
            string root = Path.Combine(Path.GetTempPath(), "childstream-browser-discovery-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            try {
                var apps = new List<ChildApp>();
                for (int i = 0; i < 17; i++) { string path = Path.Combine(root, "app-" + i + ".exe"); File.WriteAllText(path, "fixture"); apps.Add(new ChildApp(path, ChildAppKind.Other)); }
                string browser = Path.Combine(root, "My Chrome.exe"); File.WriteAllText(browser, "fixture"); apps.Add(new ChildApp(browser, ChildAppKind.Chrome));
                var result = (ChildApp)Call("ChildBrowser", "Resolve", ChildAppKind.Chrome, new FixedApps(apps));
                Assert(result != null && result.ExecutablePath == browser, "custom browser hidden by the app display cap");
            } finally { Directory.Delete(root, true); }
        });
        check("host choices resolve official GitHub repositories for manual setup only", delegate {
            foreach (var pair in new[] { new[] { "Sunshine", "https://github.com/LizardByte/Sunshine" }, new[] { "Apollo", "https://github.com/ClassicOldSong/Apollo" }, new[] { "VibePollo", "https://github.com/Nonary/Vibepollo" } })
                Assert((string)Call("HostInstallPolicy", "GithubUrl", pair[0]) == pair[1], "wrong vendor repository: " + pair[0]);
            Assert(Call("HostInstallPolicy", "GithubUrl", "https://example.com") == null, "arbitrary installation target accepted");
        });
    }
    sealed class FixedHosts : IHostDiscoverySource {
        readonly string path; readonly HostCandidate[] candidates;
        public FixedHosts(string path, HostCandidate[] candidates) { this.path = path; this.candidates = candidates; }
        public IEnumerable<HostCandidate> Candidates(string root) { return candidates; }
        public bool Exists(string value) { return HostController.SamePath(path, value); }
        public HostFileMetadata Inspect(string value) { return new HostFileMetadata("Sunshine", "Sunshine", "1", "fixture"); }
    }
    sealed class FixedApps : IAppDiscoverySource {
        readonly IEnumerable<ChildApp> apps;
        public FixedApps(IEnumerable<ChildApp> apps) { this.apps = apps; }
        public IEnumerable<ChildApp> Candidates() { return apps; }
    }
}
