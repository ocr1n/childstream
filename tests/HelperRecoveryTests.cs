using System;
using System.IO;
using System.Reflection;

static class HelperRecoveryTests
{
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    static Type Required(string name) { var type = typeof(Program).Assembly.GetType(name); Assert(type != null, name + " is missing"); return type; }
    static dynamic Recovery(Func<uint> child, Action<uint> start, Func<long> time) { return Activator.CreateInstance(Required("HelperRecovery"), new object[] { child, start, time }); }
    public static void Run(Action<string, Action> check)
    {
        check("missing helper recovery dispatches only to the current child", delegate {
            uint launched = 0; dynamic recovery = Recovery(() => 7, id => launched = id, () => 0);
            Assert(!recovery.TryStart((uint)8) && launched == 0, "replacement child received a helper");
            Assert(!recovery.TryStart((uint)0) && launched == 0, "session zero received a helper");
            Assert(recovery.TryStart((uint)7) && launched == 7, "missing child helper was not started");
        });
        check("helper recovery backs off repeated failures but permits later recovery", delegate {
            long now = 0; int starts = 0; dynamic recovery = Recovery(() => 7, id => { starts++; throw new IOException("task unavailable"); }, () => now);
            try { recovery.TryStart((uint)7); } catch (IOException) { }
            Assert(!recovery.TryStart((uint)7) && starts == 1, "failing recovery spawned every poll");
            now = 16000; try { recovery.TryStart((uint)7); } catch (IOException) { }
            Assert(starts == 2, "later recovery was permanently disabled");
        });
        check("new child recovery does not inherit another session cooldown", delegate {
            uint current = 7; int starts = 0; dynamic recovery = Recovery(() => current, id => starts++, () => 0);
            Assert(recovery.TryStart((uint)7), "first child not dispatched"); current = 8;
            Assert(recovery.TryStart((uint)8) && starts == 2, "new child inherited a stale cooldown");
        });
        check("helper recovery rechecks the child at the launch boundary", delegate {
            int queries = 0, starts = 0; dynamic recovery = Recovery(() => ++queries == 1 ? (uint)7 : 8, id => starts++, () => 0);
            Assert(!recovery.TryStart((uint)7) && starts == 0, "child changed while recovery was preparing");
        });
        check("session-bound helper startup rejects wrong console and malformed targets", delegate {
            MethodInfo method = typeof(SessionAgent).GetMethod("CanStart"); Assert(method != null, "session-bound helper entry is missing");
            Func<string[], int, uint, bool> allowed = (args, session, console) => (bool)method.Invoke(null, new object[] { args, session, console });
            Assert(allowed(new[] { "-agent" }, 1, 1), "existing idle startup stopped working");
            Assert(allowed(new[] { "-agent", "7" }, 7, 1), "bound child helper refused");
            Assert(!allowed(new[] { "-agent", "7" }, 1, 1) && !allowed(new[] { "-agent", "1" }, 1, 1), "helper started on main desktop");
            Assert(!allowed(new[] { "-agent", "bad" }, 7, 1) && !allowed(new[] { "-agent", "0" }, 0, 1) && !allowed(new[] { "-agent", "7", "extra" }, 7, 1), "malformed startup accepted");
        });
        check("browser desktop shortcut uses child profile only in its bound child session", delegate {
            string root = Path.Combine(Path.GetTempPath(), "childstream-browser-route-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            try {
                string exe = Path.Combine(root, "chrome.exe"); File.WriteAllText(exe, "fixture"); var app = new ChildApp(exe, ChildAppKind.Chrome);
                MethodInfo build = Required("ChildBrowser").GetMethod("Build"); Assert(build != null, "browser session routing is missing");
                var main = (AppLaunchSpec)build.Invoke(null, new object[] { app, root, 1, (uint)1, (uint)7 });
                Assert(main.Arguments.Length == 0 && !Directory.Exists(Path.Combine(root, "ChildStream")), "main browser was redirected to the child profile");
                var child = (AppLaunchSpec)build.Invoke(null, new object[] { app, root, 7, (uint)1, (uint)7 });
                Assert(child.Arguments.Length == 1 && child.Arguments[0] == "--user-data-dir=" + Path.Combine(root, "ChildStream", "Chrome-Child"), "child browser uses main profile");
                var unrelated = (AppLaunchSpec)build.Invoke(null, new object[] { app, root, 8, (uint)1, (uint)7 });
                Assert(unrelated.Arguments.Length == 0, "unrelated session inherited the child profile");
            } finally { Directory.Delete(root, true); }
        });
    }
}