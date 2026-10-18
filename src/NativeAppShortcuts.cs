using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

static class NativeAppShortcuts
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr CommandLineToArgvW(string command, out int count);
    [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr memory);
    static string[] Arguments(string text) {
        if (string.IsNullOrWhiteSpace(text)) return new string[0];
        if (text.Length > 8192 || text.IndexOf('\0') >= 0) throw new ArgumentException("Shortcut arguments are too long.");
        int count; IntPtr memory = CommandLineToArgvW("app.exe " + text, out count);
        if (memory == IntPtr.Zero) throw new IOException("Windows could not read the app shortcut.");
        try { if (count > 129) throw new ArgumentException("Too many shortcut arguments."); return Enumerable.Range(1, count - 1).Select(i => Marshal.PtrToStringUni(Marshal.ReadIntPtr(memory, i * IntPtr.Size))).ToArray(); }
        finally { LocalFree(memory); }
    }
    public static ChildApp[] Discover(string[] folders) {
        var result = new List<ChildApp>(); var files = new List<string>(); var pending = new Queue<string>();
        foreach (string folder in folders) if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder)) pending.Enqueue(folder);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (pending.Count > 0 && files.Count < 4096 && seen.Count < 2048) {
            string folder = pending.Dequeue(); if (!seen.Add(folder)) continue;
            try {
                if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0) continue;
                files.AddRange(Directory.GetFiles(folder, "*.lnk").Take(4096 - files.Count));
                foreach (string child in Directory.GetDirectories(folder)) pending.Enqueue(child);
            } catch (IOException) { } catch (UnauthorizedAccessException) { } catch (System.Security.SecurityException) { }
        }
        if (files.Count == 0) return result.ToArray();
        object shellObject = null;
        try {
            shellObject = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")); dynamic shell = shellObject;
            foreach (string file in files) {
                object linkObject = null;
                try {
                    string name = Path.GetFileNameWithoutExtension(file);
                    if (name.IndexOf("uninstall", StringComparison.OrdinalIgnoreCase) >= 0 || name.StartsWith("remove ", StringComparison.OrdinalIgnoreCase)) continue;
                    if (new FileInfo(file).Length > 1024 * 1024) continue;
                    linkObject = shell.CreateShortcut(file); dynamic link = linkObject;
                    string target = WindowsInstallPaths.Executable((string)link.TargetPath); if (target == null) continue;
                    ChildAppCatalog.ValidatePath(target); var kind = NativeAppDiscovery.Classify(target);
                    // Browser shortcuts may name a normal profile or PWA. Child browsing uses its own data.
                    string[] args = kind == ChildAppKind.Chrome || kind == ChildAppKind.Edge ? new string[0] : Arguments((string)link.Arguments);
                    result.Add(ChildApp.FromShortcut(target, kind, args, name, WindowsInstallPaths.DirectoryPath((string)link.WorkingDirectory)));
                } catch (Exception ex) {
                    if (!(ex is IOException || ex is ArgumentException || ex is UnauthorizedAccessException || ex is COMException || ex is System.Security.SecurityException || ex is System.ComponentModel.Win32Exception)) throw;
                } finally { if (linkObject != null && Marshal.IsComObject(linkObject)) Marshal.FinalReleaseComObject(linkObject); }
            }
        } catch (COMException) { } catch (System.Security.SecurityException) { } catch (UnauthorizedAccessException) { }
        finally { if (shellObject != null && Marshal.IsComObject(shellObject)) Marshal.FinalReleaseComObject(shellObject); }
        return result.ToArray();
    }
}
