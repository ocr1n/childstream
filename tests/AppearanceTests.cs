using System;
using System.Drawing;
using System.Reflection;
using System.IO;
using System.Collections.Generic;
using System.Windows.Forms;

static class AppearanceTests
{
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    static T Field<T>(object target, string name) { return (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target); }
    static double Linear(byte value) { double x = value / 255.0; return x <= .04045 ? x / 12.92 : Math.Pow((x + .055) / 1.055, 2.4); }
    static double Lum(Color c) { return .2126 * Linear(c.R) + .7152 * Linear(c.G) + .0722 * Linear(c.B); }
    static double Contrast(Color a, Color b) { return (Math.Max(Lum(a), Lum(b)) + .05) / (Math.Min(Lum(a), Lum(b)) + .05); }
    public static void Run(Action<string, Action> check)
    {
        check("app picker preserves its catalog and busy state through session polling", delegate {
            using (var picker = new ChildAppWindow()) {
                picker.ShowInTaskbar = false; picker.StartPosition = FormStartPosition.Manual; picker.Location = new Point(-20000, -20000); picker.Show();
                picker.SetApps(new[] { new AgentApp { Id = "fixture", Name = "Game", Path = @"C:\Games\Game.exe" } }, "Ready");
                var list = Field<ListView>(picker, "list"); list.Items[0].Selected = true; Application.DoEvents();
                var busy = typeof(ChildAppWindow).GetMethod("SetBusy"); Assert(busy != null, "app picker does not gate actions while requests are busy");
                int launches = 0; picker.LaunchRequested += id => launches++;
                busy.Invoke(picker, new object[] { true }); picker.SetAvailable(true);
                Field<Button>(picker, "open").PerformClick();
                Assert(!list.Enabled && !Field<Button>(picker, "browse").Enabled && !Field<Button>(picker, "refresh").Enabled && launches == 0, "poll reenabled actions during a request");
                Assert(list.Items.Count == 1 && list.SelectedItems.Count == 1, "busy state discarded the catalog or selection");
                busy.Invoke(picker, new object[] { false });
                Assert(list.Enabled && Field<Button>(picker, "open").Enabled, "completed request did not restore actions");
                busy.Invoke(picker, new object[] { true }); picker.SetAvailable(false); busy.Invoke(picker, new object[] { false });
                Assert(!list.Enabled && !Field<Button>(picker, "open").Enabled, "request completion reenabled a disconnected picker");
            }
        });
        check("disabled themed buttons keep visible text on dark surfaces", delegate {
            using (var window = LauncherPreview.Create(HostState.Unavailable)) {
                window.SetTheme(AppThemes.Find("dark")); var button = Field<Button>(window, "startHost");
                using (var bitmap = new Bitmap(button.Width, button.Height)) {
                    button.DrawToBitmap(bitmap, button.ClientRectangle); int lightPixels = 0;
                    for (int x = 4; x < bitmap.Width - 4; x++) for (int y = 4; y < bitmap.Height - 4; y++) { var color = bitmap.GetPixel(x, y); if (color.R > 130 && color.G > 130 && color.B > 130) lightPixels++; }
                    Assert(lightPixels > 15, "disabled text is black on dark grey");
                }
            }
        });
        check("six distinct themes retain readable text actions and semantic status", delegate {
            Assert(AppThemes.All.Length == 6, "wrong number of themes"); var ids = new HashSet<string>();
            foreach (var theme in AppThemes.All) {
                Assert(ids.Add(theme.Id), "duplicate theme");
                foreach (Color surface in new[] { theme.Surface, theme.Canvas }) {
                    Assert(Contrast(theme.Text, surface) >= 4.5, theme.Name + " body contrast");
                    Assert(Contrast(theme.Muted, surface) >= 4.5, theme.Name + " secondary contrast");
                    Assert(Contrast(theme.Success, surface) >= 3 && Contrast(theme.Error, surface) >= 3, theme.Name + " status contrast");
                }
                Assert(Contrast(theme.OnAccent, theme.Accent) >= 4.5, theme.Name + " action contrast");
                Assert(Contrast(theme.Muted, theme.Quiet) >= 4.5, theme.Name + " subdued install is unreadable");
                Assert(Contrast(theme.Focus, theme.Surface) >= 3, theme.Name + " keyboard focus contrast");
                Assert(Contrast(theme.Text, theme.Inset) >= 4.5, theme.Name + " field text contrast");
            }
        });
        check("dark primary actions and text avoid the previous glare", delegate {
            var theme = AppThemes.Find("dark");
            Assert(Contrast(theme.Accent, theme.Surface) < 3.5, "primary fill is a glaring bright block");
            Assert(Lum(theme.Text) < .8 && Contrast(theme.Text, theme.Surface) >= 4.5, "dark text should be softened while remaining readable");
        });
        check("retired palette preferences migrate to their supported replacements", delegate {
            foreach (var pair in new[] { new[] { "current", "light" }, new[] { "whales", "ocean" }, new[] { "cherry", "sunset" }, new[] { "mint", "forest" }, new[] { "amethyst", "rose" } })
                Assert(AppThemes.Find(pair[0]).Id == pair[1], "old preference not migrated: " + pair[0]);
        });
        check("theme choices preview a miniature app rather than tiny swatches", delegate {
            using (var window = new LauncherWindow(false)) using (var picker = window.CreateThemePicker()) {
                var colors = new HashSet<int>(); int count = 0;
                foreach (var theme in AppThemes.All) {
                    var choice = (ThemeChoiceButton)picker.Controls.Find("Theme-" + theme.Id, true)[0]; count++;
                    Assert(choice.TabStop && choice.AccessibleRole == AccessibleRole.RadioButton, "theme is not keyboard accessible");
                    using (var bitmap = new Bitmap(180, 112)) {
                        using (var graphics = Graphics.FromImage(bitmap)) ThemeThumbnail.Draw(graphics, new Rectangle(0, 0, 180, 112), theme);
                        Assert(bitmap.GetPixel(15, 45).ToArgb() == theme.Surface.ToArgb(), "thumbnail sidebar does not match theme");
                        Assert(bitmap.GetPixel(100, 85).ToArgb() == theme.Inset.ToArgb(), "thumbnail address field does not match theme");
                        colors.Add(bitmap.GetPixel(170, 29).ToArgb());
                    }
                }
                Assert(count == 6 && colors.Count == 6, "previews are missing or identical");
            }
        });
        check("theme preference persists safely and invalid data falls back", delegate {
            string root = Path.Combine(Path.GetTempPath(), "childstream-theme-test-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            try {
                Assert(AppThemes.Load(root).Id == "light", "missing preference failed");
                AppThemes.Save(root, AppThemes.Find("whales")); Assert(AppThemes.Load(root).Id == "ocean", "new preference lost");
                AppThemes.Save(root, AppThemes.Find("sunset")); Assert(AppThemes.Load(root).Id == "sunset", "existing preference not replaced");
                File.WriteAllText(Path.Combine(root, "appearance.cfg"), "untrusted-theme"); Assert(AppThemes.Load(root).Id == "light", "invalid preference accepted");
                File.WriteAllText(Path.Combine(root, "appearance.cfg"), new string('x', 2000)); Assert(AppThemes.Load(root).Id == "light", "oversized preference accepted");
                Assert(Directory.GetFiles(root).Length == 1, "temporary preference leaked");
            } finally { Directory.Delete(root, true); }
        });
        check("failed theme save preserves the old preference and cleans staging files", delegate {
            string root = Path.Combine(Path.GetTempPath(), "childstream-theme-test-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            try {
                AppThemes.Save(root, AppThemes.Find("whales")); bool failed = false;
                using (var locked = new FileStream(Path.Combine(root, "appearance.cfg"), FileMode.Open, FileAccess.Read, FileShare.None)) {
                    try { AppThemes.Save(root, AppThemes.Find("sunset")); } catch (IOException) { failed = true; }
                }
                Assert(failed && AppThemes.Load(root).Id == "ocean", "failed save damaged existing preference");
                Assert(Directory.GetFiles(root).Length == 1, "failed save leaked a staging file");
            } finally { Directory.Delete(root, true); }
        });
        check("Windows high contrast overrides every custom palette", delegate {
            var value = AppTheme.Effective(AppThemes.Find("amethyst"), true);
            Assert(value.Text == SystemColors.WindowText && value.Surface == SystemColors.Window && value.Accent == SystemColors.Highlight && value.OnAccent == SystemColors.HighlightText, "custom colors override Windows high contrast");
        });
        check("selecting a preview theme updates the UI once without changing session actions", delegate {
            using (var window = LauncherPreview.Create(HostState.Running)) using (var picker = window.CreateThemePicker()) {
                picker.ShowInTaskbar = false; picker.StartPosition = FormStartPosition.Manual; picker.Location = new Point(-20000, -20000); picker.Show(); Application.DoEvents();
                int changes = 0; window.ThemeChanged += value => changes++;
                var choice = (ThemeChoiceButton)picker.Controls.Find("Theme-dark", true)[0]; choice.PerformClick();
                Assert(window.Theme.Id == "dark" && changes == 1 && choice.SelectedChoice, "theme click not applied and selected once");
                Assert((choice.AccessibilityObject.State & AccessibleStates.Checked) != 0, "selected theme is not announced as checked");
                Assert(Field<Button>(window, "copyAddress").Enabled, "theme revoked a live host action");
                Assert(Field<TextBox>(window, "address").BackColor == AppThemes.Find("dark").Inset, "address remains bright in dark mode");
                Assert(picker.CancelButton != null, "picker cannot be closed with Escape");
            }
        });
        check("guidance title stays visible when pairing expands at the minimum", delegate {
            using (var window = LauncherPreview.Create(HostState.Running)) {
                window.ShowInTaskbar = false; window.StartPosition = FormStartPosition.Manual; window.Location = new Point(-20000, -20000); window.Size = window.MinimumSize; window.Show();
                ((Button)window.Controls.Find("PairingSteps", true)[0]).PerformClick(); Application.DoEvents();
                var title = Field<Label>(window, "placeholderTitle"); var panel = Field<Panel>(window, "placeholder");
                var bounds = panel.RectangleToClient(title.RectangleToScreen(title.ClientRectangle));
                Assert(panel.ClientRectangle.Contains(bounds), "guidance title is pushed below its viewport");
                var body = Field<Label>(window, "placeholderBody"); panel.ScrollControlIntoView(body); Application.DoEvents();
                var bodyBounds = panel.RectangleToClient(body.RectangleToScreen(body.ClientRectangle));
                Assert(bodyBounds.Bottom <= panel.ClientSize.Height, "last guidance line cannot be scrolled into view");
                int oldHeight = panel.Controls[0].Height;
                window.ShowGuidance("Updated setup guidance", string.Join(" ", new string[24]).Replace(" ", "More guidance to check wrapping. ")); Application.DoEvents();
                Assert(panel.Controls[0].Height > oldHeight && panel.VerticalScroll.Visible, "changed guidance did not recalculate scrollable content height"); window.Close();
            }
        });
        check("lower address rows remain aligned and readable at the outer minimum", delegate {
            using (var window = LauncherPreview.Create(HostState.Running)) {
                window.SetTheme(AppThemes.Find("dark")); window.ShowInTaskbar = false; window.StartPosition = FormStartPosition.Manual; window.Location = new Point(-20000, -20000); window.Size = window.MinimumSize; window.Show(); Application.DoEvents();
                var first = Field<TextBox>(window, "address"); var second = Field<TextBox>(window, "settingsAddress");
                var copy = Field<Button>(window, "copyAddress"); var copyUrl = Field<Button>(window, "copySettings");
                Assert(copy.Width == copyUrl.Width && copy.Height == copyUrl.Height, "copy controls have mismatched proportions");
                Assert(first.Parent.Width == second.Parent.Width && first.Parent.Width >= 120, "address column collapsed or changed widths");
                Assert(window.ClientRectangle.Contains(window.RectangleToClient(copyUrl.RectangleToScreen(copyUrl.ClientRectangle))), "bottom copy control clips");
                window.Close();
            }
        });
        check("install menu delivers each selected vendor before close and supports reopening", delegate {
            using (var window = new LauncherWindow(false)) using (var menu = window.CreateInstallMenu()) {
                window.ShowInTaskbar = false; window.StartPosition = FormStartPosition.Manual; window.Location = new Point(-20000, -20000); window.Show();
                int clicks = 0; string vendor = null; window.InstallRequested += value => { clicks++; vendor = value; };
                menu.Show(window, new Point(0, 0)); Application.DoEvents(); var first = menu.Items[0]; first.PerformClick();
                Assert(clicks == 1 && vendor == "Sunshine", "vendor click was lost while closing the menu");
                menu.Show(window, new Point(0, 0)); Application.DoEvents(); menu.Items[1].PerformClick();
                Assert(clicks == 2 && vendor == "Apollo", "reopened menu did not deliver exactly one vendor click"); window.Close();
            }
        });
        check("installed-host confirmation identifies hosts and respects decline", delegate {
            using (var window = new LauncherWindow(false)) {
                window.SetDiscovery(new AgentReply { Hosts = new[] { new AgentHost { Name = "Apollo", Id = "a" }, new AgentHost { Name = "Sunshine", Id = "s" } }, Status = new HostSnapshot(HostState.Stopped, "off", 48989) });
                int asks = 0; window.ConfirmInstall = text => { asks++; Assert(text.Contains("Apollo") && text.Contains("Sunshine"), "wrong detected host in confirmation"); return false; };
                Assert(!window.ApproveInstall() && asks == 1, "decline ignored"); window.ConfirmInstall = text => true; Assert(window.ApproveInstall(), "approval ignored");
                window.SetDiscovery(new AgentReply { Status = new HostSnapshot(HostState.Missing, "none", 0) });
                window.ConfirmInstall = text => { throw new Exception("no-host flow unexpectedly asks for another host"); }; Assert(window.ApproveInstall(), "missing host cannot install");
            }
        });
        check("diagnostic detail is wrapped scrollable and preserves the full error", delegate {
            string text = new string('x', 5000);
            using (var dialog = new DiagnosticWindow("Details", text, AppThemes.Find("dark"))) {
                var body = Field<TextBox>(dialog, "body"); Assert(body.Text == text && body.ReadOnly && body.WordWrap && body.ScrollBars == ScrollBars.Vertical, "detail is lost or not readable");
                Assert(dialog.CancelButton != null, "details cannot be dismissed with Escape");
            }
        });
        check("host installation becomes quiet but remains clickable with installed hosts", delegate {
            using (var window = new LauncherWindow(false)) {
                window.SetDiscovery(new AgentReply { Hosts = new[] { new AgentHost { Id = "sun", Name = "Sunshine" } }, SelectionId = "sun", Status = new HostSnapshot(HostState.Stopped, "off", 48989) });
                var buttons = window.Controls.Find("InstallHost", true);
                Assert(buttons.Length == 1, "no detection-aware install control");
                var install = (Button)buttons[0]; Assert(install.Enabled && install.Text.Contains("another"), "installed host still gets first-install emphasis");
                window.SetDiscovery(new AgentReply { Status = new HostSnapshot(HostState.Unavailable, "timeout", 0) });
                Assert(install.Text.Contains("another"), "helper failure erased known installed host");
                window.SetDiscovery(new AgentReply { Status = new HostSnapshot(HostState.Missing, "no host", 0) });
                Assert(install.Text == "Install a host...", "clean no-host scan did not restore installation action");
            }
        });
        check("full helper diagnostics never become a native hover strip", delegate {
            using (var window = new LauncherWindow(false)) {
                string raw = "Child helper unavailable for C:\\Very long folder\\" + new string('x', 1500) + " operation timed out";
                window.SetHost(new HostSnapshot(HostState.Unavailable, raw, 0));
                var label = Field<Label>(window, "hostDetail"); var tips = Field<ToolTip>(window, "tips");
                Assert(string.IsNullOrEmpty(tips.GetToolTip(label)), "raw diagnostic still goes to Windows tooltip");
                Assert(label.Text.Length < 180 && !label.Text.Contains("xxxxxxxx"), "technical dump still dominates status");
                Assert(window.Controls.Find("HostDetails", true).Length == 1, "no readable details action");
            }
        });
        check("themes stay reachable outside the scrolling companion column", delegate {
            using (var window = new LauncherWindow(false)) {
                var themes = window.Controls.Find("Themes", true); Assert(themes.Length == 1, "no Themes button");
                Assert(themes[0] is Button && themes[0].AccessibleName == "Themes", "theme control has no keyboard label");
            }
        });
        check("very long notices remain bounded with setup always reachable", delegate {
            using (var window = new LauncherWindow(false)) {
                window.SetNotice(new string('x', 3000));
                Assert(Field<ToolStripStatusLabel>(window, "footer").Text.Length <= 160, "status bar still renders an unbounded error strip");
            }
        });
    }
}
