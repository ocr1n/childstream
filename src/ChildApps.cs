using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

enum ChildAppKind { Chrome, Edge, Steam, WallpaperEngine, Other }
enum AppLaunchState { Dispatched, EarlyExit, OtherSession, InspectionDenied, Failed, Canceled }
sealed class AppLaunchResult
{
    public readonly AppLaunchState State; public readonly string Message;
    public AppLaunchResult(AppLaunchState state, string message) { State = state; Message = HostInstallation.Limit(message, 512); }
}
sealed class ChildApp
{
    public readonly string Id, DisplayName, ExecutablePath, WorkingDirectory; public readonly ChildAppKind Kind; public readonly string[] Arguments;
    public ChildApp(string path, ChildAppKind kind) : this(path, kind, new string[0]) { }
    public ChildApp(string path, ChildAppKind kind, string[] arguments) : this(path, kind, arguments, null, null, false) { }
    ChildApp(string path, ChildAppKind kind, string[] arguments, string name, string directory, bool shortcut) {
        if (arguments == null || (!shortcut && arguments.Length != 0) || arguments.Length > 128 || arguments.Any(a => a == null || a.IndexOf('\0') >= 0) || arguments.Sum(a => a.Length) > 8192) throw new ArgumentException("Registered apps cannot supply command text or flags.");
        ExecutablePath = ChildAppCatalog.ValidatePath(path); Kind = kind; Arguments = (string[])arguments.Clone();
        WorkingDirectory = directory != null && Directory.Exists(directory) ? directory : Path.GetDirectoryName(ExecutablePath);
        if (string.IsNullOrWhiteSpace(name) && kind == ChildAppKind.Other) { var version = FileVersionInfo.GetVersionInfo(path); name = string.IsNullOrWhiteSpace(version.FileDescription) ? Path.GetFileNameWithoutExtension(path) : version.FileDescription; }
        DisplayName = HostInstallation.Limit(name ?? (kind == ChildAppKind.WallpaperEngine ? "Wallpaper Engine" : kind.ToString()), 80);
        var info = new FileInfo(ExecutablePath); Id = HostProfiles.Digest(ExecutablePath.ToUpperInvariant() + "|" + kind + "|" + info.Length + "|" + info.LastWriteTimeUtc.Ticks + "|" + NativeArguments.Join(Arguments) + "|" + WorkingDirectory.ToUpperInvariant());
    }
    public static ChildApp FromShortcut(string path, ChildAppKind kind, string[] arguments, string name, string directory = null) { return new ChildApp(path, kind, arguments, name, directory, true); }
    public bool IsCurrent() { return Id == new ChildApp(ExecutablePath, Kind, Arguments, DisplayName, WorkingDirectory, true).Id; }
}
interface IAppDiscoverySource { IEnumerable<ChildApp> Candidates(); }
static class ChildAppCatalog
{
    public const int MaxRegistered = 256, MaxApps = 4096;
    public static string ValidatePath(string path) {
        if (string.IsNullOrWhiteSpace(path) || WindowsInstallPaths.Executable(path) == null || !string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Choose an installed .exe file. Shortcuts and scripts are not supported.");
        path = Path.GetFullPath(path);
        string name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
        if (new[] { "cmd", "powershell", "pwsh", "wscript", "cscript", "mshta", "wsl", "bash", "rundll32", "regsvr32" }.Contains(name)) throw new ArgumentException("Choose the app itself, rather than a shell or script runner.");
        if (!File.Exists(path)) throw new FileNotFoundException("The selected app is no longer installed."); return path;
    }
    public static ChildApp Register(string path) { path = ValidatePath(path); return new ChildApp(path, NativeAppDiscovery.Classify(path)); }
    public static ChildApp[] Find(IAppDiscoverySource source) {
        var result = new List<ChildApp>(); var seen = new HashSet<string>();
        foreach (ChildApp app in source.Candidates()) { if (app != null && seen.Add(app.Id)) result.Add(app); if (result.Count >= MaxApps) break; }
        return result.ToArray();
    }
    public static string[] LoadRegistered(string path) {
        var paths = new List<string>();
        try {
            if (!File.Exists(path) || new FileInfo(path).Length > 4 * 1024 * 1024) return paths.ToArray();
            foreach (string line in File.ReadLines(path).Take(MaxRegistered)) {
                try { string decoded = new UTF8Encoding(false, true).GetString(Convert.FromBase64String(line)); if (decoded.Length <= 2048) paths.Add(decoded); }
                catch (FormatException) { } catch (DecoderFallbackException) { }
            }
        } catch (IOException) { } catch (UnauthorizedAccessException) { }
        return paths.ToArray();
    }
    public static void SaveRegistered(string path, IEnumerable<string> paths) {
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try {
            File.WriteAllLines(temporary, paths.Distinct(StringComparer.OrdinalIgnoreCase).Take(MaxRegistered).Select(p => Convert.ToBase64String(Encoding.UTF8.GetBytes(p))), new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
        } finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
sealed class NativeAppDiscovery : IAppDiscoverySource
{
    readonly string root; readonly Func<IEnumerable<string>> inventory; readonly bool native;
    ChildApp[] cached; readonly Stopwatch clock = Stopwatch.StartNew(); long cachedAt;
    public NativeAppDiscovery(string root = null, Func<IEnumerable<string>> inventory = null) { this.root = root ?? AppDomain.CurrentDomain.BaseDirectory; native = inventory == null; this.inventory = inventory ?? WindowsInstallPaths.NativeApps; }
    public void Refresh() { cached = null; }
    public static ChildAppKind Classify(string path) {
        var info = FileVersionInfo.GetVersionInfo(path); string branding = (info.ProductName ?? "") + " " + (info.FileDescription ?? "");
        if (branding.IndexOf("Google Chrome", StringComparison.OrdinalIgnoreCase) >= 0) return ChildAppKind.Chrome;
        if (branding.IndexOf("Microsoft Edge", StringComparison.OrdinalIgnoreCase) >= 0) return ChildAppKind.Edge;
        if (branding.IndexOf("Wallpaper Engine", StringComparison.OrdinalIgnoreCase) >= 0) return ChildAppKind.WallpaperEngine;
        if (string.Equals(Path.GetFileName(path), "steam.exe", StringComparison.OrdinalIgnoreCase) && branding.IndexOf("Steam", StringComparison.OrdinalIgnoreCase) >= 0) return ChildAppKind.Steam;
        return ChildAppKind.Other;
    }
    IEnumerable<ChildApp> ReadPaths(IEnumerable<string> paths) {
        foreach (string raw in paths.Distinct(StringComparer.OrdinalIgnoreCase)) {
            ChildApp app = null;
            try { string path = WindowsInstallPaths.Executable(raw); if (path != null && File.Exists(path)) app = ChildAppCatalog.Register(path); }
            catch (IOException) { } catch (UnauthorizedAccessException) { } catch (ArgumentException) { } catch (System.Security.SecurityException) { } catch (Win32Exception) { }
            if (app != null) yield return app;
        }
    }
    public IEnumerable<ChildApp> Candidates() {
        var saved = ReadPaths(ChildAppCatalog.LoadRegistered(Path.Combine(root, "child-apps.txt"))).ToArray();
        if (!native) return saved.Concat(ReadPaths(inventory())).ToArray();
        if (cached == null || clock.ElapsedMilliseconds - cachedAt > 30000) {
            var shortcuts = NativeAppShortcuts.Discover(new[] { Environment.GetFolderPath(Environment.SpecialFolder.Programs), Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms) });
            cached = shortcuts.Concat(ReadPaths(inventory())).Take(ChildAppCatalog.MaxApps).ToArray(); cachedAt = clock.ElapsedMilliseconds;
        }
        return saved.Concat(cached);
    }
}
sealed class AppLaunchSpec
{
    public readonly string Executable, WorkingDirectory; public readonly string[] Arguments; public readonly ChildAppKind Kind; public readonly bool RunAsAdministrator;
    public AppLaunchSpec(string executable, string[] arguments, string directory, ChildAppKind kind, bool runAsAdministrator = false) { Executable = executable; Arguments = (string[])arguments.Clone(); WorkingDirectory = directory; Kind = kind; RunAsAdministrator = runAsAdministrator; }
}
static class ChildAppLauncher
{
    public static AppLaunchSpec Build(ChildApp app, string localAppData, bool runAsAdministrator = false) {
        ChildAppCatalog.ValidatePath(app.ExecutablePath);
        if (!app.IsCurrent()) throw new IOException("The selected app changed. Refresh and choose it again.");
        string[] arguments = app.Arguments;
        if (app.Kind == ChildAppKind.Chrome || app.Kind == ChildAppKind.Edge) {
            string profile = Path.Combine(Path.GetFullPath(localAppData), "ChildStream", app.Kind + "-Child"); Directory.CreateDirectory(profile); arguments = new[] { "--user-data-dir=" + profile };
        }
        return new AppLaunchSpec(app.ExecutablePath, arguments, app.WorkingDirectory, app.Kind, runAsAdministrator);
    }
    public static AppLaunchResult Ownership(AppLaunchSpec spec, int session, HostProcess[] processes) {
        if (spec.Kind != ChildAppKind.Steam) return null;
        foreach (var process in processes) {
            if (process.InspectionDenied) return new AppLaunchResult(AppLaunchState.InspectionDenied, "Windows could not inspect an existing Steam process. Close Steam yourself if needed, then retry. No app was stopped.");
            if (process.SessionId != session && HostController.SamePath(process.ExecutablePath, spec.Executable)) return new AppLaunchResult(AppLaunchState.OtherSession, "Steam is running on another desktop. Exit Steam there, then open it in the child. Games started by main-desktop Steam can send their windows and administrator prompts there.");
        }
        return null;
    }
    public static AppLaunchResult Launch(AppLaunchSpec spec, uint approvedChild, Func<uint> currentChild, Func<AppLaunchSpec, AppLaunchResult> start) {
        try { uint current = currentChild(); if (approvedChild == 0 || approvedChild > int.MaxValue || current != approvedChild) return new AppLaunchResult(AppLaunchState.Failed, "The child desktop changed. Reconnect and choose the app again."); return start(spec); }
        catch (Win32Exception ex) { return ex.NativeErrorCode == 1223 ? new AppLaunchResult(AppLaunchState.Canceled, "Administrator request canceled. No fallback launch was attempted.") : new AppLaunchResult(AppLaunchState.Failed, "Could not open the child app: " + ex.Message); }
        catch (Exception ex) { return new AppLaunchResult(AppLaunchState.Failed, "Could not open the child app: " + ex.Message); }
    }
    public static ProcessStartInfo StartInfo(AppLaunchSpec spec) { return new ProcessStartInfo(spec.Executable, NativeArguments.Join(spec.Arguments)) { UseShellExecute = true, Verb = spec.RunAsAdministrator ? "runas" : "", WorkingDirectory = spec.WorkingDirectory }; }
    public static AppLaunchResult NativeStart(AppLaunchSpec spec) {
        var ownership = Ownership(spec, HostNative.CurrentSession(), HostNative.Processes(spec.Executable)); if (ownership != null) return ownership;
        ChildAppCatalog.ValidatePath(spec.Executable);
        using (var process = Process.Start(StartInfo(spec))) {
            if (process == null) return new AppLaunchResult(AppLaunchState.Dispatched, "Windows handed the request to the app. Check its window in the child desktop.");
            if (process.WaitForExit(600)) return new AppLaunchResult(AppLaunchState.EarlyExit, "The app launcher exited (code " + process.ExitCode + "). Check the child desktop. The app may use an existing instance or require its own launcher.");
            if (process.SessionId != HostNative.CurrentSession()) return new AppLaunchResult(AppLaunchState.Failed, "The app did not remain in the child desktop.");
            return new AppLaunchResult(AppLaunchState.Dispatched, spec.Kind == ChildAppKind.WallpaperEngine ? "Wallpaper Engine was launched in the child desktop. Check its monitor mapping; settings may be shared." : "The app was launched in the child desktop. Check its window there; rendering, sign-in and game compatibility depend on the app.");
        }
    }
}

sealed class AppLaunchProgress
{
    public readonly bool Pending; public readonly AppLaunchResult Result;
    public AppLaunchProgress(bool pending, AppLaunchResult result) { Pending = pending; Result = result; }
}
sealed class AppLaunchOperation
{
    readonly object gate = new object();
    bool pending; AppLaunchResult result;
    public AppLaunchProgress Snapshot { get { lock (gate) return new AppLaunchProgress(pending, result); } }
    public bool TryStart(Func<AppLaunchResult> launch) {
        lock (gate) {
            if (pending) return false;
            pending = true; result = null;
            var worker = new System.Threading.Thread(delegate() {
                AppLaunchResult completed;
                try { completed = launch(); }
                catch (Exception ex) { completed = new AppLaunchResult(AppLaunchState.Failed, ex.Message); }
                lock (gate) { result = completed; pending = false; }
            });
            worker.IsBackground = true; worker.SetApartmentState(System.Threading.ApartmentState.STA);
            try { worker.Start(); }
            catch { pending = false; throw; }
            return true;
        }
    }
}
