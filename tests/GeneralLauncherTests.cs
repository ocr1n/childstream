using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

static class GeneralLauncherTests
{
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    static Type Required(string name) { var type = typeof(Program).Assembly.GetType(name); Assert(type != null, name + " is missing"); return type; }
    static object Call(string type, string method, params object[] args) {
        var action = Required(type).GetMethod(method, BindingFlags.Public | BindingFlags.Static);
        Assert(action != null, method + " is missing");
        try { return action.Invoke(null, args); } catch (TargetInvocationException ex) { throw ex.InnerException; }
    }
    static void Set(object value, string name, object field) {
        var property = value.GetType().GetField(name); Assert(property != null, name + " is missing"); property.SetValue(value, field);
    }
    static T Field<T>(object value, string name) {
        var field = value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert(field != null, name + " is missing"); return (T)field.GetValue(value);
    }
    static string Fixture(string root, string name) { string path = Path.Combine(root, name); File.WriteAllText(path, "fixture"); return path; }
    static double Lum(Color color) { Func<byte, double> channel = value => value / 255.0 <= .04045 ? value / 255.0 / 12.92 : Math.Pow((value / 255.0 + .055) / 1.055, 2.4); return .2126 * channel(color.R) + .7152 * channel(color.G) + .0722 * channel(color.B); }
    static double Contrast(Color a, Color b) { return (Math.Max(Lum(a), Lum(b)) + .05) / (Math.Min(Lum(a), Lum(b)) + .05); }
    public static void Run(Action<string, Action> check) {
        check("general app registrations discover Blender and Office in arbitrary folders", delegate {
            string record = @"Software\ChildStream\Tests\" + Guid.NewGuid().ToString("N");
            try { using (var software = Registry.CurrentUser.CreateSubKey(record)) {
                var expected = new[] { @"D:\Creative suite\blender.exe", @"E:\Office\root\Office16\WINWORD.EXE", @"E:\Office\root\Office16\EXCEL.EXE" };
                foreach (string path in expected) using (var key = software.CreateSubKey(@"Microsoft\Windows\CurrentVersion\App Paths\" + Path.GetFileName(path))) key.SetValue("", path);
                var found = WindowsInstallPaths.RegistryApps(software);
                Assert(expected.All(path => found.Contains(path)), "discovery still depends on a short app whitelist");
            } } finally { Registry.CurrentUser.DeleteSubKeyTree(record, false); }
        });
        check("Start menu discovery preserves trusted app arguments and skips command runners", delegate {
            string root = Path.Combine(Path.GetTempPath(), "childstream-shortcuts-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            object shell = null;
            try {
                string exe = Fixture(root, "blender.exe"), command = Fixture(root, "cmd.exe"), working = Path.Combine(root, "Custom working folder"); Directory.CreateDirectory(working);
                shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")); dynamic creator = shell;
                foreach (var pair in new[] { new[] { "Blender", exe }, new[] { "Command runner", command } }) {
                    object linkObject = creator.CreateShortcut(Path.Combine(root, pair[0] + ".lnk"));
                    try { dynamic link = linkObject; link.TargetPath = pair[1]; link.WorkingDirectory = working; link.Arguments = "--open \"C:\\Documents with spaces\\scene.blend\""; link.Save(); }
                    finally { Marshal.FinalReleaseComObject(linkObject); }
                }
                var apps = (ChildApp[])Call("NativeAppShortcuts", "Discover", new object[] { new[] { root } });
                Assert(apps.Length == 1 && apps[0].ExecutablePath == exe && apps[0].DisplayName == "Blender", "valid app shortcut lost or shell offered");
                Assert(apps[0].Arguments.SequenceEqual(new[] { "--open", @"C:\Documents with spaces\scene.blend" }), "shortcut arguments changed");
                var spec = ChildAppLauncher.Build(apps[0], root);
                Assert(spec.Arguments.SequenceEqual(apps[0].Arguments) && spec.WorkingDirectory == working, "discovered app arguments or working directory lost at launch");
                Assert(ChildAppLauncher.StartInfo(spec).WorkingDirectory == working, "native dispatch lost the shortcut working directory");
            } finally { if (shell != null) Marshal.FinalReleaseComObject(shell); Directory.Delete(root, true); }
        });
        check("search and bounded pages reach all apps beyond the old sixteen-item limit", delegate {
            string root = Path.Combine(Path.GetTempPath(), "childstream-all-apps-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            try {
                var candidates = Enumerable.Range(0, 43).Select(i => new ChildApp(Fixture(root, "Application 日本語 " + i + ".exe"), ChildAppKind.Other)).ToArray();
                var service = new AgentService(root, 7, new EmptyHosts(), path => new HostProcess[0], profile => { throw new Exception("unused"); }, () => true, new Apps(candidates));
                var search = new AgentRequest { Command = "LIST_APPS" }; Set(search, "Query", "Application 日本語 42");
                var reply = service.Handle(search); Assert(reply.Apps.Length == 1 && reply.Apps[0].Path.EndsWith("42.exe"), "search could not reach the last installed app");
                var seen = new HashSet<string>(); int offset = 0, pages = 0;
                do {
                    var request = new AgentRequest { Command = "LIST_APPS" }; Set(request, "Offset", offset);
                    reply = service.Handle(request); AgentCodec.Encode(reply);
                    Assert(reply.Apps.Length > 0 && Field<int>(reply, "AppOffset") == offset && Field<int>(reply, "AppTotal") == 43, "page metadata is incorrect");
                    foreach (var app in reply.Apps) Assert(seen.Add(app.Path), "app duplicated between pages");
                    int next = Field<int>(reply, "AppNext"); Assert(next == -1 || next == offset + reply.Apps.Length, "byte-limited page skipped apps");
                    offset = next; pages++;
                } while (offset >= 0 && pages < 100);
                Assert(offset == -1 && seen.Count == 43 && pages > 1, "apps disappeared behind the old display cap");
                var stalePage = service.Handle(new AgentRequest { Command = "LIST_APPS", Offset = 99 });
                Assert(stalePage.AppOffset == 0 && stalePage.Apps.Length > 0, "a removed app page became an empty reversed result range");
            } finally { Directory.Delete(root, true); }
        });
        check("app lookup rejects non-app query fields negative offsets and malformed search payloads", delegate {
            var valid = new AgentRequest { Command = "LIST_APPS" }; Set(valid, "Query", "Blender"); Set(valid, "Offset", 16);
            var decoded = AgentCodec.DecodeRequest(AgentCodec.Encode(valid)); Assert(Field<string>(decoded, "Query") == "Blender", "query did not survive the wire");
            foreach (var pair in new[] { new[] { "STATUS", "Blender" }, new[] { "LIST_APPS", new string('x', 129) } }) {
                var invalid = new AgentRequest { Command = pair[0] }; Set(invalid, "Query", pair[1]); bool rejected = false;
                try { AgentCodec.Encode(invalid); } catch (InvalidDataException) { rejected = true; } Assert(rejected, "invalid query accepted");
            }
            Set(valid, "Offset", -1); bool refused = false; try { AgentCodec.Encode(valid); } catch (InvalidDataException) { refused = true; } Assert(refused, "negative app offset accepted");
        });
        check("administrator launch is bound to the child and uses the Windows runas verb", delegate {
            string root = Path.Combine(Path.GetTempPath(), "childstream-admin-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            try {
                string exe = Fixture(root, "blender.exe"); AppLaunchSpec captured = null; bool child = true;
                var constructor = typeof(AgentService).GetConstructors().FirstOrDefault(c => c.GetParameters().Length == 8); Assert(constructor != null, "app launch boundary cannot be verified independently");
                var service = (AgentService)constructor.Invoke(new object[] { root, 7, new EmptyHosts(), new Func<string, HostProcess[]>(path => new HostProcess[0]), new Func<ChildHostProfile, HostLaunchReceipt>(profile => { throw new Exception("unused"); }), new Func<bool>(() => child), new Apps(new[] { new ChildApp(exe, ChildAppKind.Other) }), new Func<AppLaunchSpec, AppLaunchResult>(spec => { captured = spec; return new AppLaunchResult(AppLaunchState.Dispatched, "sent"); }) });
                var reply = service.Handle(new AgentRequest { Command = "LIST_APPS" });
                service.Handle(new AgentRequest { Command = "LAUNCH_APP_ADMIN", AppId = reply.Apps[0].Id });
                Assert(System.Threading.SpinWait.SpinUntil(() => captured != null, 2000) && Field<bool>(captured, "RunAsAdministrator"), "admin choice was lost");
                var start = (ProcessStartInfo)Call("ChildAppLauncher", "StartInfo", captured);
                Assert(start.UseShellExecute && start.Verb == "runas" && start.FileName == exe, "elevation was not requested through the child shell");
                captured = null; child = false;
                service.Handle(new AgentRequest { Command = "LAUNCH_APP_ADMIN", AppId = reply.Apps[0].Id });
                Assert(captured == null, "admin request escaped its approved child session");
            } finally { Directory.Delete(root, true); }
        });
        check("cancelled elevation stays cancelled without another launch attempt", delegate {
            int attempts = 0;
            var spec = new AppLaunchSpec("fixture.exe", new string[0], Path.GetTempPath(), ChildAppKind.Other);
            var result = ChildAppLauncher.Launch(spec, 7, () => 7, value => { attempts++; throw new Win32Exception(1223, "cancelled"); });
            Assert(attempts == 1 && result.State.ToString() == "Canceled", "cancelled consent became a retry or generic failure");
        });
        check("pending elevation stays responsive and cannot dispatch a second app", delegate {
            string root = Path.Combine(Path.GetTempPath(), "childstream-pending-app-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            using (var entered = new System.Threading.ManualResetEvent(false)) using (var release = new System.Threading.ManualResetEvent(false)) {
                try {
                    int launches = 0; string exe = Fixture(root, "blender.exe");
                    var service = new AgentService(root, 7, new EmptyHosts(), path => new HostProcess[0], profile => { throw new Exception("unused"); }, () => true, new Apps(new[] { new ChildApp(exe, ChildAppKind.Other) }), spec => { System.Threading.Interlocked.Increment(ref launches); entered.Set(); release.WaitOne(5000); return new AppLaunchResult(AppLaunchState.Canceled, "Consent canceled"); });
                    var reply = service.Handle(new AgentRequest { Command = "LIST_APPS" }); string id = reply.Apps[0].Id;
                    reply = service.Handle(new AgentRequest { Command = "LAUNCH_APP_ADMIN", AppId = id });
                    Assert(entered.WaitOne(2000) && Field<bool>(reply, "AppLaunchPending"), "consent blocked the request handler");
                    reply = service.Handle(new AgentRequest { Command = "LIST_APPS", Query = "Blender" });
                    Assert(Field<bool>(reply, "AppLaunchPending") && reply.Apps.Length == 1, "pending consent froze discovery");
                    service.Handle(new AgentRequest { Command = "LAUNCH_APP_ADMIN", AppId = id }); Assert(launches == 1, "pending consent dispatched twice");
                    release.Set();
                    Assert(System.Threading.SpinWait.SpinUntil(() => !Field<bool>(service.Handle(new AgentRequest { Command = "LIST_APPS" }), "AppLaunchPending"), 2000), "settled launch stayed pending");
                    Assert(service.Handle(new AgentRequest { Command = "LIST_APPS" }).Diagnostic == "Consent canceled" && launches == 1, "canceled launch was retried or lost");
                } finally { release.Set(); Directory.Delete(root, true); }
            }
        });
        check("app picker search and paging reject stale results while requests are busy", delegate {
            using (var picker = new ChildAppWindow()) {
                picker.ShowInTaskbar = false; picker.StartPosition = FormStartPosition.Manual; picker.Location = new Point(-20000, -20000); picker.Show();
                var entries = new[] { new AgentApp { Id = new string('a', 64), Name = "Blender", Path = @"D:\Creative\blender.exe" }, new AgentApp { Id = new string('b', 64), Name = "Word", Path = @"E:\Office\winword.exe" } };
                picker.SetPage(new AgentReply { Apps = entries, AppTotal = 4, AppNext = 2 }, "", 0);
                int offset = -1, lookups = 0; string query = null;
                picker.LookupRequested += (text, page, rescan) => { query = text; offset = page; lookups++; };
                Field<Button>(picker, "next").PerformClick(); Assert(offset == 2 && lookups == 1, "next page was not requested");
                picker.SetPage(new AgentReply { Apps = new[] { entries[0] }, AppTotal = 4, AppNext = 1 }, "", 0);
                Assert(Field<ListView>(picker, "list").Items.Count == 2, "an old page replaced the current request");
                picker.SetPage(new AgentReply { Apps = entries, AppOffset = 2, AppTotal = 4 }, "", 2);
                Field<Button>(picker, "previous").PerformClick(); Assert(offset == 0, "previous page did not use actual byte-limited history");
                picker.SetPage(new AgentReply { Apps = entries, AppTotal = 4, AppNext = 2 }, "", 0);
                picker.SetBusy(true); var search = Field<TextBox>(picker, "search"); search.Text = "Office"; search.Text = "Blender";
                Assert(search.Enabled && !Field<Button>(picker, "open").Enabled, "search typing was blocked or stale launch stayed enabled");
                Assert(System.Threading.SpinWait.SpinUntil(() => { Application.DoEvents(); return lookups >= 3; }, 1500), "search was lost during another request");
                Assert(query == "Blender" && offset == 0 && lookups == 3, "search was not debounced or pagination did not reset");
                picker.SetPage(new AgentReply { Apps = entries, AppTotal = 2 }, "", 0);
                Assert(!Field<Button>(picker, "open").Enabled, "obsolete query enabled launching");
                picker.SetBusy(false); picker.SetPage(new AgentReply { Apps = new[] { entries[0] }, AppTotal = 1 }, "Blender", 0);
                var list = Field<ListView>(picker, "list"); list.Items[0].Selected = true; Application.DoEvents();
                int admins = 0; picker.AdministratorRequested += id => admins++;
                picker.SetPage(new AgentReply { Apps = new[] { entries[0] }, AppTotal = 1, AppLaunchPending = true }, "Blender", 0);
                Field<Button>(picker, "administrator").PerformClick(); Assert(admins == 0 && !Field<Button>(picker, "open").Enabled, "pending consent allowed a second launch");
                picker.SetPage(new AgentReply { Apps = new[] { entries[0] }, AppTotal = 1 }, "Blender", 0);
                Field<Button>(picker, "administrator").PerformClick(); Assert(admins == 1, "explicit administrator action was lost after completion");
                picker.SetAvailable(false); picker.SetBusy(false);
                Assert(!search.Enabled && !Field<Button>(picker, "administrator").Enabled, "disconnected search or elevation stayed enabled");
            }
        });
        check("app picker refreshes pending consent status after reconnect", delegate {
            using (var picker = new ChildAppWindow()) {
                var entry = new AgentApp { Id = new string('a', 64), Name = "Blender", Path = @"D:\Creative\blender.exe" };
                picker.SetPage(new AgentReply { Apps = new[] { entry }, AppTotal = 1, AppLaunchPending = true }, "", 0);
                int lookups = 0; picker.LookupRequested += (query, page, refresh) => lookups++;
                picker.SetAvailable(false); picker.SetBusy(false); picker.SetAvailable(true);
                Assert(lookups == 1, "restored connection did not resume pending app status");
                picker.SetPage(new AgentReply { Apps = new[] { entry }, AppTotal = 1 }, "", 0);
                Assert(Field<Button>(picker, "refresh").Enabled && Field<Button>(picker, "browse").Enabled, "completed consent remained stuck after reconnect");
            }
        });
        check("compact toolbar keeps all actions in one row without retaining empty space", delegate {
            using (var form = LauncherPreview.Create(HostState.Stopped)) {
                form.ShowInTaskbar = false; form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-20000, -20000); form.Show(); Application.DoEvents();
                form.Size = form.MinimumSize; Application.DoEvents();
                var open = Field<Button>(form, "openApp"); var tools = open.Parent;
                Assert(open.Left == 0 && tools.Height <= open.Height + open.Margin.Vertical + 4, "compact controls retained a blank title slot or row");
                form.ClientSize = new Size(1400, 900); Application.DoEvents();
                Assert(tools.Height <= open.Height + open.Margin.Vertical + 4, "toolbar kept its wrapped height after expanding");
            }
        });
        check("every button role keeps readable text in idle hover press and disabled states", delegate {
            foreach (var theme in AppThemes.All) foreach (object role in new object[] { true, false, "quiet" }) using (var button = new ThemedButton { Tag = role }) {
                ThemeStyle.Button(button, theme);
                Assert(Contrast(button.ForeColor, button.BackColor) >= 4.5, theme.Name + " idle control contrast");
                Assert(Contrast(button.HotTextColor, button.FlatAppearance.MouseOverBackColor) >= 4.5, theme.Name + " hover control contrast");
                Assert(Contrast(button.HotTextColor, button.FlatAppearance.MouseDownBackColor) >= 4.5, theme.Name + " pressed control contrast");
                button.Enabled = false; ThemeStyle.Button(button, theme);
                Assert(Contrast(button.ForeColor, button.BackColor) >= 4.5, theme.Name + " disabled control contrast");
            }
        });
        check("app query generations survive host changes and expire on actual reconnect", delegate {
            var gate = new SessionRequestGate(); gate.Update(7, true);
            int connection = (int)gate.GetType().GetProperty("ConnectionGeneration").GetValue(gate, null), host = gate.Generation;
            gate.Select("new-host-profile");
            var accepts = gate.GetType().GetMethod("AcceptsConnection");
            Assert((bool)accepts.Invoke(gate, new object[] { connection, (uint)7 }) && !gate.Accepts(host, 7), "host selection canceled the queued app lookup or kept a stale host approval");
            gate.Update(7, false); gate.Update(7, true);
            Assert(!(bool)accepts.Invoke(gate, new object[] { connection, (uint)7 }), "reused child ID accepted a pre-disconnect app lookup");
            connection = (int)gate.GetType().GetProperty("ConnectionGeneration").GetValue(gate, null); gate.Close();
            Assert(!(bool)accepts.Invoke(gate, new object[] { connection, (uint)7 }), "closed viewer accepted app results");
        });
        check("odd viewport widths are rounded to an even RDP monitor width", delegate {
            int width = 0, height = 0;
            var sizing = new RdpViewport((w, h) => { width = w; height = h; }, text => { throw new Exception(text); }, () => 0);
            sizing.Fit(7, true, 2237, 1163);
            Assert(width == 2236 && height == 1163, "an odd monitor width was sent to Windows");
        });
        check("oversized viewports retain aspect ratio within Windows display bounds", delegate {
            int width = 0, height = 0;
            var sizing = new RdpViewport((w, h) => { width = w; height = h; }, text => { throw new Exception(text); }, () => 0);
            sizing.Fit(7, true, 16384, 9216);
            Assert(width == 8192 && height == 4608, "large viewport was stretched or exceeded maximum display dimensions");
        });
        check("old helpers and malformed app paging metadata are rejected explicitly", delegate {
            foreach (string json in new[] {
                "{\"Version\":2,\"Command\":\"LIST_APPS\"}",
                "{\"Version\":3,\"Command\":\"LIST_APPS\",\"RefreshCatalog\":1}" }) {
                bool rejected = false; try { AgentCodec.DecodeRequest(json); } catch (InvalidDataException) { rejected = true; }
                Assert(rejected, "old or malformed request was accepted");
            }
            foreach (string fields in new[] { "\"AppLaunchPending\":\"true\"", "\"AppOffset\":0,\"AppNext\":0,\"AppTotal\":20", "\"AppOffset\":5,\"AppTotal\":0" }) {
                bool rejected = false;
                try { AgentCodec.DecodeReply("{\"Version\":3,\"State\":\"Waiting\",\"Port\":0," + fields + "}"); } catch (InvalidDataException) { rejected = true; }
                Assert(rejected, "malformed app reply was accepted");
            }
        });
        check("viewport sizing follows maximize reconnect and child changes without resize loops", delegate {
            int width = 0, height = 0, updates = 0;
            object controller = Activator.CreateInstance(Required("RdpViewport"), new object[] { new Action<int, int>((w, h) => { width = w; height = h; updates++; }), new Action<string>(text => { throw new Exception(text); }), new Func<long>(() => 0) });
            var fit = controller.GetType().GetMethod("Fit"); Assert(fit != null, "viewport fitting is missing");
            Action<uint, bool, int, int> size = (session, connected, w, h) => fit.Invoke(controller, new object[] { session, connected, w, h });
            size(7, true, 1024, 600); size(7, true, 1024, 600); Assert(updates == 1, "unchanged viewport was sent repeatedly");
            size(7, true, 2236, 1163); Assert(width == 2236 && height == 1163 && updates == 2, "maximize retained the old desktop dimensions");
            size(7, false, 2236, 1163); size(7, true, 2236, 1163); Assert(updates == 3, "reconnected desktop inherited the old resize state");
            size(8, true, 2236, 1163); Assert(updates == 4, "new child inherited another desktop's size state");
            size(0, true, 1600, 900); size(8, true, 0, 0); Assert(updates == 4, "missing or minimized desktop was resized");
        });
        check("unsupported viewport updates back off and report failure without disconnecting", delegate {
            int updates = 0, notices = 0; long time = 0;
            object controller = Activator.CreateInstance(Required("RdpViewport"), new object[] { new Action<int, int>((w, h) => { updates++; throw new IOException("unsupported"); }), new Action<string>(text => notices++), new Func<long>(() => time) });
            var fit = controller.GetType().GetMethod("Fit");
            for (int i = 0; i < 12; i++) { fit.Invoke(controller, new object[] { (uint)7, true, 1920, 1080 }); time += 1000; }
            Assert(updates <= 3 && notices == 1, "unsupported control was hammered or failure was silent");
        });
        check("enabled primary controls expose visible hover and press feedback in every theme", delegate {
            foreach (var theme in AppThemes.All) using (var button = new ThemedButton { Text = "Start desktop", Tag = true, Size = new Size(180, 42) }) {
                ThemeStyle.Button(button, theme); button.CreateControl();
                Func<Color> pixel = () => { using (var bitmap = new Bitmap(button.Width, button.Height)) { button.DrawToBitmap(bitmap, button.ClientRectangle); return bitmap.GetPixel(12, button.Height / 2); } };
                Color idle = pixel(); button.GetType().GetMethod("OnMouseEnter", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(button, new object[] { EventArgs.Empty }); Color hover = pixel();
                Assert(idle.ToArgb() != hover.ToArgb(), theme.Name + " has no hover feedback");
                button.GetType().GetMethod("OnMouseDown", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(button, new object[] { new MouseEventArgs(MouseButtons.Left, 1, 12, 12, 0) });
                Assert(pixel().ToArgb() != hover.ToArgb(), theme.Name + " has no press feedback");
            }
        });
    }
    sealed class Apps : IAppDiscoverySource {
        readonly ChildApp[] apps; public Apps(ChildApp[] apps) { this.apps = apps; }
        public IEnumerable<ChildApp> Candidates() { return apps; }
    }
    sealed class EmptyHosts : IHostDiscoverySource {
        public IEnumerable<HostCandidate> Candidates(string root) { return new HostCandidate[0]; }
        public bool Exists(string path) { return false; }
        public HostFileMetadata Inspect(string path) { throw new Exception("unused"); }
    }
}
