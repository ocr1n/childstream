using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security;
using System.Text.RegularExpressions;
using Microsoft.Win32;

static class WindowsInstallPaths
{
    public static string DirectoryPath(string value) {
        try {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 32768) return null;
            // RegistryKey.GetValue already expands REG_EXPAND_SZ. Other strings are literal paths.
            string path = value.Trim().Trim('"').Replace('/', '\\');
            if (path.Length < 3 || !char.IsLetter(path[0]) || path[1] != ':' || path[2] != '\\' || path.Substring(2).Contains(":") || path.IndexOfAny(new[] { '\0', '\r', '\n', '"' }) >= 0) return null;
            return Path.GetFullPath(path);
        } catch (ArgumentException) { return null; } catch (NotSupportedException) { return null; } catch (IOException) { return null; }
    }
    public static string Executable(string value) {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 32768) return null;
        Match match = Regex.Match(value.Trim(), "^(?:\"([^\"]+\\.exe)\"|([^\"\\r\\n]+\\.exe))(?:,\\s*-?\\d+)?$", RegexOptions.IgnoreCase);
        return match.Success ? DirectoryPath(match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value) : null;
    }
    static void AddFile(List<string> result, string value) { string path = Executable(value); if (path != null) result.Add(path); }
    static void AddDirectory(List<string> result, string value, params string[] names) {
        string directory = DirectoryPath(value); if (directory == null) return;
        foreach (string name in names) result.Add(Path.Combine(directory, name));
    }
    public static string[] RegistryExecutables(RegistryKey software, string[] names) {
        var result = new List<string>(); if (software == null) return result.ToArray();
        foreach (string name in names) {
            try { using (var key = software.OpenSubKey(@"Microsoft\Windows\CurrentVersion\App Paths\" + name)) if (key != null) AddFile(result, key.GetValue("") as string); }
            catch (SecurityException) { } catch (UnauthorizedAccessException) { } catch (IOException) { }
        }
        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
    public static string[] RegistryApps(RegistryKey software) {
        var result = new List<string>(RegistryExecutables(software, new[] { "chrome.exe", "msedge.exe", "steam.exe", "wallpaper64.exe", "wallpaper32.exe" }));
        if (software == null) return result.ToArray();
        try { using (var steam = software.OpenSubKey(@"Valve\Steam")) if (steam != null) {
            AddFile(result, steam.GetValue("SteamExe") as string);
            AddDirectory(result, steam.GetValue("SteamPath") as string, "steam.exe");
            AddDirectory(result, steam.GetValue("InstallPath") as string, "steam.exe");
        } } catch (SecurityException) { } catch (UnauthorizedAccessException) { } catch (IOException) { }
        try { using (var uninstall = software.OpenSubKey(@"Microsoft\Windows\CurrentVersion\Uninstall")) if (uninstall != null) foreach (string name in uninstall.GetSubKeyNames()) {
            try { using (var app = uninstall.OpenSubKey(name)) {
                if (app == null) continue; string display = app.GetValue("DisplayName") as string ?? "";
                string[] names;
                if (Regex.IsMatch(display, @"^Google Chrome(?:\s|$)", RegexOptions.IgnoreCase)) names = new[] { "chrome.exe", @"Application\chrome.exe" };
                else if (Regex.IsMatch(display, @"^Microsoft Edge(?:\s|$)", RegexOptions.IgnoreCase)) names = new[] { "msedge.exe", @"Application\msedge.exe" };
                else if (string.Equals(display, "Steam", StringComparison.OrdinalIgnoreCase)) names = new[] { "steam.exe" };
                else if (Regex.IsMatch(display, @"^Wallpaper Engine(?:\s|$)", RegexOptions.IgnoreCase)) names = new[] { "wallpaper64.exe", "wallpaper32.exe" };
                else continue;
                // DisplayIcon frequently points at uninstall.exe or a WebView runtime.
                // Only the actual supported app image is a launch candidate here.
                string icon = Executable(app.GetValue("DisplayIcon") as string);
                if (icon != null && names.Any(file => string.Equals(Path.GetFileName(file), Path.GetFileName(icon), StringComparison.OrdinalIgnoreCase))) result.Add(icon);
                AddDirectory(result, app.GetValue("InstallLocation") as string, names);
            } } catch (SecurityException) { } catch (UnauthorizedAccessException) { } catch (IOException) { }
        } } catch (SecurityException) { } catch (UnauthorizedAccessException) { } catch (IOException) { }
        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
    static string SmallText(string path) {
        try { return File.Exists(path) && new FileInfo(path).Length <= 1024 * 1024 ? File.ReadAllText(path) : ""; }
        catch (IOException) { return ""; } catch (UnauthorizedAccessException) { return ""; } catch (SecurityException) { return ""; }
    }
    public static string[] SteamApps(string steamExecutable) {
        string exe = Executable(steamExecutable); if (exe == null) return new string[0];
        string steam = Path.GetDirectoryName(exe); var libraries = new List<string> { steam }; var result = new List<string>();
        foreach (Match match in Regex.Matches(SmallText(Path.Combine(steam, "steamapps", "libraryfolders.vdf")), "\"path\"\\s*\"([^\"]+)\"")) {
            string path = DirectoryPath(match.Groups[1].Value.Replace(@"\\", @"\")); if (path != null) libraries.Add(path);
        }
        foreach (string library in libraries.Distinct(StringComparer.OrdinalIgnoreCase)) {
            string manifest = SmallText(Path.Combine(library, "steamapps", "appmanifest_431960.acf"));
            Match match = Regex.Match(manifest, "\"installdir\"\\s*\"([^\"]+)\"");
            string directory = match.Success ? match.Groups[1].Value : "wallpaper_engine";
            if (directory == "." || directory == ".." || directory.IndexOfAny(new[] { '\\', '/', ':', '\0' }) >= 0) continue;
            AddDirectory(result, Path.Combine(library, "steamapps", "common", directory), "wallpaper64.exe", "wallpaper32.exe");
        }
        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
    public static IEnumerable<string> NativeApps() {
        var paths = new List<string>();
        foreach (RegistryHive hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 }) {
            try { using (var baseKey = RegistryKey.OpenBaseKey(hive, view)) using (var software = baseKey.OpenSubKey("Software")) paths.AddRange(RegistryApps(software)); }
            catch (SecurityException) { } catch (UnauthorizedAccessException) { } catch (IOException) { }
        }
        foreach (string name in new[] { "chrome", "msedge", "steam", "wallpaper64", "wallpaper32" }) {
            foreach (var process in Process.GetProcessesByName(name)) using (process) {
                try { AddFile(paths, HostNative.ProcessPath((uint)process.Id)); }
                catch (System.ComponentModel.Win32Exception) { } catch (InvalidOperationException) { } catch (ArgumentException) { }
            }
        }
        // Standard OS folders are fallbacks, never a developer's absolute install paths.
        foreach (string folder in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) }) {
            AddDirectory(paths, folder, @"Google\Chrome\Application\chrome.exe", @"Microsoft\Edge\Application\msedge.exe", @"Steam\steam.exe");
        }
        foreach (string steam in paths.Where(path => string.Equals(Path.GetFileName(path), "steam.exe", StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()) paths.AddRange(SteamApps(steam));
        return paths.Distinct(StringComparer.OrdinalIgnoreCase).Take(256).ToArray();
    }
}
