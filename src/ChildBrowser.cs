using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

static class ChildBrowser
{
    public static AppLaunchSpec Build(ChildApp app, string localData, int session, uint console, uint expectedChild)
    {
        if (app.Kind != ChildAppKind.Chrome && app.Kind != ChildAppKind.Edge) throw new ArgumentException("Choose Chrome or Edge.");
        if (session == expectedChild && PeerIdentity.IsHelperSession(session, session, console)) return ChildAppLauncher.Build(app, localData);
        ChildAppCatalog.ValidatePath(app.ExecutablePath);
        if (new ChildApp(app.ExecutablePath, app.Kind).Id != app.Id) throw new IOException("The browser changed. Choose it again.");
        return new AppLaunchSpec(app.ExecutablePath, new string[0], Path.GetDirectoryName(app.ExecutablePath), app.Kind);
    }
    public static int Open(string[] args)
    {
        uint expected;
        ChildAppKind kind;
        if (args.Length != 3 || !Enum.TryParse(args[1], out kind) || (kind != ChildAppKind.Chrome && kind != ChildAppKind.Edge) ||
            !uint.TryParse(args[2], NumberStyles.None, CultureInfo.InvariantCulture, out expected) || expected == 0 || expected > int.MaxValue) return 2;
        try {
            ChildApp app = ChildAppCatalog.Find(new NativeAppDiscovery()).FirstOrDefault(a => a.Kind == kind);
            if (app == null) throw new IOException(kind + " is not installed. Install it or choose it from ChildStream's Apps menu.");
            var spec = Build(app, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), HostNative.CurrentSession(), HostNative.WTSGetActiveConsoleSessionId(), expected);
            AppLaunchResult result = ChildAppLauncher.NativeStart(spec);
            if (result.State == AppLaunchState.Failed || result.State == AppLaunchState.InspectionDenied || result.State == AppLaunchState.OtherSession) throw new IOException(result.Message);
            return 0;
        } catch (Exception ex) { Program.Log("browser launch failed: " + ex.Message); MessageBox.Show(ex.Message, "ChildStream browser", MessageBoxButtons.OK, MessageBoxIcon.Error); return 1; }
    }
    public static void InstallShortcuts(string root, uint session)
    {
        string exe = Path.Combine(root, "ChildStream.exe");
        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        foreach (ChildApp app in ChildAppCatalog.Find(new NativeAppDiscovery()).Where(a => a.Kind == ChildAppKind.Chrome || a.Kind == ChildAppKind.Edge)) {
            string name = app.Kind + " (child desktop).lnk";
            string arguments = NativeArguments.Join(new[] { "-browser", app.Kind.ToString(), session.ToString(CultureInfo.InvariantCulture) });
            WriteShortcut(Path.Combine(root, name), exe, arguments, app.ExecutablePath);
            if (!string.IsNullOrEmpty(desktop)) WriteShortcut(Path.Combine(desktop, name), exe, arguments, app.ExecutablePath);
        }
    }
    static void WriteShortcut(string path, string exe, string arguments, string icon)
    {
        object shellObject = null, shortcutObject = null;
        try {
            shellObject = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")); dynamic shell = shellObject;
            shortcutObject = shell.CreateShortcut(path); dynamic shortcut = shortcutObject;
            shortcut.TargetPath = exe; shortcut.Arguments = arguments; shortcut.WorkingDirectory = Path.GetDirectoryName(exe);
            shortcut.Description = "Open an independent browser in the ChildStream desktop"; shortcut.IconLocation = icon + ",0"; shortcut.Save();
        } finally {
            if (shortcutObject != null && Marshal.IsComObject(shortcutObject)) Marshal.FinalReleaseComObject(shortcutObject);
            if (shellObject != null && Marshal.IsComObject(shellObject)) Marshal.FinalReleaseComObject(shellObject);
        }
    }
}