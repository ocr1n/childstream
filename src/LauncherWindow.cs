using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

sealed class LauncherWindow : Form
{
    readonly Color ink = SystemInformation.HighContrast ? SystemColors.WindowText : Color.FromArgb(24, 40, 56);
    readonly Color muted = SystemInformation.HighContrast ? SystemColors.WindowText : Color.FromArgb(82, 101, 121);
    readonly Label sessionTitle, sessionDetail, hostTitle, hostDetail;
    readonly Button desktop, reconnect, startHost, settings, endSession, copyAddress, copySettings, openApp, refreshHosts, browseHost;
    readonly ComboBox hostChoice;
    readonly Label webLabel;
    readonly TextBox address, settingsAddress, pairingHelp;
    readonly Label placeholderTitle, placeholderBody;
    readonly Panel workspace, placeholder;
    readonly Action fitGuidance;
    readonly ToolStripStatusLabel footer;
    readonly ToolStripButton statusDetails;
    readonly ToolTip tips = new ToolTip { Active = false };
    readonly Button install, details;
    readonly HostInstallPolicy installation = new HostInstallPolicy();
    AppTheme theme = AppThemes.All[0];
    DiagnosticWindow diagnosticWindow;
    ContextMenuStrip installMenu;
    ThemePickerWindow themePicker;
    string diagnostic = "", lastNotice = "";
    public AppTheme Theme { get { return theme; } }
    public Func<string, bool> ConfirmInstall { get; set; }
    public event Action<AppTheme> ThemeChanged, ThemeRepainted;
    HostSnapshot host = new HostSnapshot(HostState.Waiting, "Start the child desktop to check its Sunshine instance.", 0);
    bool connected, busy, ending, showPairingHelp, setupReady = true;
    bool hasSelection;
    string hostName;
    public event EventHandler ConnectRequested, ReconnectRequested, StartHostRequested, SettingsRequested, EndSessionRequested, GuideRequested, MinimizeRequested;
    public event EventHandler RefreshHostsRequested, BrowseHostRequested, OpenAppRequested;
    public event Action<string> HostSelected, InstallRequested;
    public bool Preview { get; private set; }

    public LauncherWindow(bool preview)
    {
        Preview = preview;
        hasSelection = preview; hostName = preview ? "Sunshine" : "host";
        Tag = "canvas"; Text = preview ? "ChildStream - Preview" : "ChildStream";
        AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9.5f); ForeColor = ink;
        BackColor = SystemInformation.HighContrast ? SystemColors.Window : Color.FromArgb(239, 243, 248);
        ClientSize = new Size(1180, 760); MinimumSize = new Size(900, 640);
        StartPosition = FormStartPosition.CenterScreen;

        var statusBar = new StatusStrip { SizingGrip = true };
        footer = new ToolStripStatusLabel(preview ? "Preview mode - synthetic data, no system changes" : "Ready. Your physical desktop stays available.") { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        statusBar.Items.Add(footer); Controls.Add(statusBar);
        statusDetails = new ToolStripButton("Details") { AccessibleName = "Status details", Visible = false }; statusDetails.Click += (s, e) => ShowDetails(); statusBar.Items.Add(statusDetails);
        var repair = new ToolStripButton("Setup / repair") { AccessibleName = "Setup / repair" }; repair.Click += (s, e) => Raise(GuideRequested); statusBar.Items.Add(repair);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = BackColor, Tag = "canvas" };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Controls.Add(layout); layout.BringToFront();
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = SystemColors.Window, Margin = Padding.Empty };
        var rail = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, Padding = new Padding(24, 22, 24, 22), Margin = Padding.Empty };
        rail.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        scroll.Controls.Add(rail); layout.Controls.Add(scroll, 0, 0);
        Action<Control> add = control => { int row = rail.RowCount++; rail.RowStyles.Add(new RowStyle(SizeType.AutoSize)); rail.Controls.Add(control, 0, row); };
        add(Label("ChildStream", 21, FontStyle.Bold, ink, 0, 22));
        add(Label("Second desktop", 10, FontStyle.Bold, muted, 0, 5));
        sessionTitle = Label("Not connected", 14, FontStyle.Bold, ink, 0, 6); add(sessionTitle);
        sessionDetail = Label("Start a separate desktop for your stream. Keep this viewer attached while playing.", 9.5f, FontStyle.Regular, muted, 0, 12); add(sessionDetail);
        desktop = Button("&Start desktop", true, (s, e) => Raise(ConnectRequested)); add(desktop);
        reconnect = Button("&Reconnect", false, (s, e) => Raise(ReconnectRequested)); reconnect.Enabled = false; add(reconnect);
        add(new Panel { Tag = "divider", Height = 1, Dock = DockStyle.Top, BackColor = SystemInformation.HighContrast ? SystemColors.WindowText : Color.FromArgb(217, 226, 236), Margin = new Padding(0, 17, 0, 17) });
        add(Label("Streaming host", 10, FontStyle.Bold, muted, 0, 5));
        hostTitle = Label("Waiting for desktop", 14, FontStyle.Bold, ink, 0, 6); add(hostTitle);
        hostDetail = Label(host.Message, 9.5f, FontStyle.Regular, muted, 0, 12);
        hostChoice = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top, Width = 240, DropDownWidth = 640, AccessibleName = "Streaming host installation", Margin = new Padding(0, 0, 6, 8), Visible = false };
        hostChoice.SelectionChangeCommitted += delegate { var choice = hostChoice.SelectedItem as HostChoice; if (choice != null && HostSelected != null) HostSelected(choice.Id); }; add(hostChoice);
        var hostTools = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, WrapContents = true, Margin = Padding.Empty };
        refreshHosts = Button("Refresh", false, (s, e) => Raise(RefreshHostsRequested)); refreshHosts.AccessibleName = "Refresh hosts";
        browseHost = Button("Browse...", false, (s, e) => Raise(BrowseHostRequested)); browseHost.AccessibleName = "Browse host";
        hostTools.Controls.Add(refreshHosts); hostTools.Controls.Add(browseHost);
        install = Button("Install a host...", false, null); install.Name = "InstallHost";
        install.Click += delegate {
            if (!ApproveInstall()) return;
            if (installMenu == null || installMenu.IsDisposed) installMenu = CreateInstallMenu();
            ThemeStyle.Menu(installMenu, theme); installMenu.Show(install, new Point(0, install.Height));
        };
        startHost = Button("Start &Sunshine", true, (s, e) => Raise(StartHostRequested)); add(startHost);
        settings = Button("Open Sunshine settings", false, (s, e) => Raise(SettingsRequested)); add(settings);
        add(hostDetail);
        details = Button("Details...", false, (s, e) => ShowDetails()); details.Name = "HostDetails"; details.AccessibleName = "Host details"; details.Visible = false; add(details);
        add(hostTools); add(install);
        address = new TextBox { ReadOnly = true, Text = "Available after host check", Width = 210, BorderStyle = BorderStyle.FixedSingle, BackColor = SystemColors.Window, ForeColor = ink, Margin = new Padding(8, 5, 8, 0), AccessibleName = "Client pairing address" };
        address.AccessibleDescription = "Pair from Moonlight or a compatible client using this address.";
        copyAddress = Button("Copy address", false, (s, e) => {
            if (!HostConfiguration.IsValidPort(host.BasePort)) return;
            try { Clipboard.SetText(address.Text); SetNotice("Pairing address copied."); }
            catch (Exception) { SetNotice("Clipboard is busy. Select and copy the address manually."); }
        }); copyAddress.Dock = DockStyle.None;
        settingsAddress = new TextBox { ReadOnly = true, Text = "Available when Sunshine is running", Width = 250, BorderStyle = BorderStyle.FixedSingle, BackColor = SystemColors.Window, ForeColor = ink, Margin = new Padding(8, 5, 8, 0), AccessibleName = "Sunshine web UI URL" };
        settingsAddress.AccessibleDescription = "Open this URL on your main desktop to enter the client PIN.";
        copySettings = Button("Copy settings URL", false, (s, e) => {
            string url = LauncherPolicy.SettingsUrl(host);
            if (!connected || url == null) return;
            try { Clipboard.SetText(url); SetNotice(LauncherPolicy.SettingsFeedback(true)); }
            catch (Exception) { SetNotice("Clipboard is busy. Select and copy the Host web UI URL manually."); }
        }); copySettings.Dock = DockStyle.None;

        var right = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Padding = new Padding(24), Margin = Padding.Empty, Tag = "canvas" };
        right.RowStyles.Add(new RowStyle(SizeType.AutoSize)); right.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); right.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.Controls.Add(right, 1, 0);
        var tools = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Tag = "canvas", Margin = new Padding(0, 0, 0, 12) };
        var workspaceHeading = Label("Desktop preview", 11, FontStyle.Bold, ink, 8, 8); tools.Controls.Add(workspaceHeading);
        openApp = Button("Open &app", false, (s, e) => Raise(OpenAppRequested)); tools.Controls.Add(openApp);
        tools.Controls.Add(Button("Minimize to tray", false, (s, e) => Raise(MinimizeRequested)));
        var themes = Button("&Themes", false, (s, e) => ShowThemes((Control)s)); themes.Name = "Themes"; tools.Controls.Add(themes);
        endSession = Button("End session...", false, (s, e) => Raise(EndSessionRequested)); tools.Controls.Add(endSession); right.Controls.Add(tools, 0, 0);
        tools.Resize += delegate {
            int actionsWidth = 0;
            foreach (Control action in tools.Controls) if (action != workspaceHeading) actionsWidth += action.GetPreferredSize(Size.Empty).Width + action.Margin.Horizontal;
            workspaceHeading.Visible = tools.ClientSize.Width >= actionsWidth + workspaceHeading.GetPreferredSize(Size.Empty).Width + workspaceHeading.Margin.Horizontal;
        };
        workspace = new Panel { Name = "DesktopWorkspace", AccessibleName = "Child desktop workspace", Dock = DockStyle.Fill, BackColor = SystemColors.Window, Margin = Padding.Empty };
        right.Controls.Add(workspace, 0, 1);
        placeholder = new Panel { Dock = DockStyle.Fill, Padding = new Padding(38), BackColor = SystemColors.Window, AutoScroll = true };
        var center = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 1, RowCount = 3 };
        center.RowStyles.Add(new RowStyle(SizeType.Percent, 50)); center.RowStyles.Add(new RowStyle(SizeType.AutoSize)); center.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        var message = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Dock = DockStyle.Top };
        placeholderTitle = Label("Your stream's own desktop", 24, FontStyle.Bold, ink, 0, 14);
        placeholderBody = Label("Start a second Windows desktop for games and apps. Sunshine shares that desktop with your client while you keep using this one.", 11, FontStyle.Regular, muted, 0, 0);
        center.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        message.Controls.Add(placeholderTitle); message.Controls.Add(placeholderBody); center.Controls.Add(message, 0, 1); placeholder.Controls.Add(center); workspace.Controls.Add(placeholder);
        fitGuidance = delegate {
            int width = Math.Max(200, placeholder.ClientSize.Width - placeholder.Padding.Horizontal - 8);
            placeholderTitle.MaximumSize = placeholderBody.MaximumSize = new Size(width, 0);
            message.MaximumSize = new Size(width, 0);
            center.Height = Math.Max(Math.Max(0, placeholder.ClientSize.Height - placeholder.Padding.Vertical), message.GetPreferredSize(new Size(width, 0)).Height + message.Margin.Vertical);
        };
        placeholder.Resize += delegate { fitGuidance(); }; Shown += delegate { fitGuidance(); };
        var pairing = new TableLayoutPanel { Name = "PairingPanel", Tag = "canvas", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top, ColumnCount = 3, RowCount = 4, Margin = new Padding(0, 12, 0, 0) };
        pairing.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110)); pairing.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); pairing.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
        pairing.RowStyles.Add(new RowStyle(SizeType.Absolute, 40)); pairing.RowStyles.Add(new RowStyle(SizeType.Absolute, 40)); pairing.RowStyles.Add(new RowStyle(SizeType.AutoSize)); pairing.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var clientLabel = Label("Client address", 9, FontStyle.Regular, muted, 0, 0); clientLabel.MaximumSize = Size.Empty; clientLabel.Anchor = AnchorStyles.Left; clientLabel.Margin = new Padding(0, 0, 8, 6);
        webLabel = Label("Host web UI", 9, FontStyle.Regular, muted, 0, 0); webLabel.MaximumSize = Size.Empty; webLabel.Anchor = AnchorStyles.Left; webLabel.Margin = new Padding(0, 0, 8, 6);
        copyAddress.Text = copySettings.Text = "Copy";
        foreach (var copy in new[] { copyAddress, copySettings }) { copy.AutoSize = false; copy.Dock = DockStyle.Fill; copy.Margin = new Padding(0, 0, 0, 6); }
        pairing.Controls.Add(clientLabel, 0, 0); pairing.Controls.Add(AddressSurface(address), 1, 0); pairing.Controls.Add(copyAddress, 2, 0);
        pairing.Controls.Add(webLabel, 0, 1); pairing.Controls.Add(AddressSurface(settingsAddress), 1, 1); pairing.Controls.Add(copySettings, 2, 1);
        pairingHelp = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, WordWrap = true, MinimumSize = new Size(0, 160), Dock = DockStyle.Top, BackColor = SystemColors.Window, ForeColor = ink, Margin = new Padding(0, 10, 0, 0), Visible = false, AccessibleName = "Pairing instructions" };
        var steps = Button("Show pairing steps", false, null); steps.Dock = DockStyle.None; steps.Anchor = AnchorStyles.Right; steps.Margin = new Padding(0, 4, 0, 0); steps.Name = "PairingSteps";
        steps.Click += delegate { showPairingHelp = !showPairingHelp; pairingHelp.Visible = showPairingHelp; steps.Text = showPairingHelp ? "Hide pairing steps" : "Show pairing steps"; steps.AccessibleName = steps.Text; };
        pairing.Controls.Add(steps, 0, 2); pairing.SetColumnSpan(steps, 3); pairing.Controls.Add(pairingHelp, 0, 3); pairing.SetColumnSpan(pairingHelp, 3);
        pairing.Resize += delegate { pairingHelp.MaximumSize = new Size(Math.Max(200, pairing.ClientSize.Width), 160); };
        right.Controls.Add(pairing, 0, 2);
        SetHost(host); UpdateActions(); SetTheme(theme);
        footer.AutoToolTip = false; footer.ToolTipText = "";
    }

    Panel AddressSurface(TextBox field) {
        var surface = new Panel { Tag = "inset", Dock = DockStyle.Fill, Margin = new Padding(0, 0, 8, 6), Padding = new Padding(10, 0, 10, 0) };
        field.BorderStyle = BorderStyle.None; field.Dock = DockStyle.None; field.Margin = Padding.Empty; field.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        surface.Controls.Add(field);
        surface.Resize += delegate { field.SetBounds(10, Math.Max(0, (surface.ClientSize.Height - field.PreferredHeight) / 2), Math.Max(0, surface.ClientSize.Width - 20), field.PreferredHeight); };
        return surface;
    }

    Label Label(string text, float size, FontStyle style, Color color, int top, int bottom)
    {
        return new Label { Text = text, AutoSize = true, MaximumSize = new Size(252, 0), Font = new Font("Segoe UI", size, style), ForeColor = color, Margin = new Padding(0, top, 0, bottom), UseMnemonic = false, Tag = color == muted ? "muted" : null };
    }
    Button Button(string text, bool primary, EventHandler action)
    {
        var button = new ThemedButton { Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(0, primary ? 38 : 32), Dock = DockStyle.Top, Padding = new Padding(10, 3, 10, 3), Margin = new Padding(0, 0, 6, 6), FlatStyle = SystemInformation.HighContrast ? FlatStyle.Standard : FlatStyle.Flat, UseVisualStyleBackColor = SystemInformation.HighContrast, AccessibleName = text.Replace("&", "") };
        button.Tag = primary;
        button.EnabledChanged += delegate { StyleButton(button); };
        StyleButton(button);
        button.Click += action; return button;
    }
    void StyleButton(Button button)
    {
        ThemeStyle.Button(button, theme);
    }

    void Raise(EventHandler action) { if (action != null) action(this, EventArgs.Empty); }
    void UpdateActions()
    {
        desktop.Enabled = setupReady && !ending; reconnect.Enabled = connected && !ending;
        startHost.Visible = hasSelection && host.State != HostState.Running && host.State != HostState.Waiting && host.State != HostState.Checking;
        startHost.Enabled = hasSelection && !busy && !ending && LauncherPolicy.CanStartHost(host, connected);
        settings.Visible = host.State == HostState.Running;
        settings.Enabled = connected && !busy && !ending && LauncherPolicy.SettingsUrl(host) != null;
        copyAddress.Enabled = connected && !ending && host.State == HostState.Running && HostConfiguration.IsValidPort(host.BasePort);
        copySettings.Enabled = connected && !ending && LauncherPolicy.SettingsUrl(host) != null;
        desktop.Tag = !connected; StyleButton(desktop);
        endSession.Enabled = connected && !ending && !Preview;
        openApp.Enabled = connected && !busy && !ending;
        refreshHosts.Enabled = browseHost.Enabled = hostChoice.Enabled = connected && !busy && !ending;
    }
    public void SetBusy(bool value) { busy = value; UpdateActions(); }
    public void SetEnding(bool value) { ending = value; UpdateActions(); }
    public void SetNotice(string message) {
        lastNotice = message ?? "";
        string prefix = Preview ? "Preview - " : "";
        footer.Text = prefix + Brief(lastNotice, 100 - prefix.Length);
        statusDetails.Visible = lastNotice.Length > 100 - prefix.Length;
        footer.AccessibleDescription = statusDetails.Visible ? "Use Status details for the full message." : "Current status";
        if (diagnosticWindow != null && !diagnosticWindow.IsDisposed) diagnosticWindow.SetText(FullDetails());
    }
    public void SetSession(string title, string detail, bool attached, bool ready)
    {
        connected = attached; setupReady = ready; sessionTitle.Text = title; sessionDetail.Text = detail;
        desktop.Text = attached ? "Show &desktop" : "&Start desktop";
        desktop.AccessibleName = desktop.Text.Replace("&", "");
        UpdateActions();
    }
    public void SetHost(HostSnapshot value)
    {
        host = value;
        hostTitle.Text = value.State == HostState.Running ? hostName + " running" : value.State == HostState.Stopped ? hostName + " is off" : value.State == HostState.Missing ? "No host detected" : value.State == HostState.Unavailable ? "Helper unavailable" : value.State == HostState.Starting ? "Starting " + hostName : value.State == HostState.Checking ? "Checking " + hostName : value.State == HostState.Error ? "Needs attention" : value.State == HostState.Unknown ? "Status unknown" : "Waiting for desktop";
        var colors = AppTheme.Effective(theme, SystemInformation.HighContrast);
        hostTitle.ForeColor = value.State == HostState.Running ? colors.Success : value.State == HostState.Error ? colors.Error : colors.Text;
        hostDetail.Text = value.State == HostState.Unavailable ? "The child helper is not responding. ChildStream will retry automatically. If it stays unavailable, use Setup / repair." : Brief(value.Message, 180);
        hostDetail.AccessibleDescription = "Host status. Use Host details for the full message.";
        details.Visible = value.State == HostState.Unavailable || value.State == HostState.Error || value.State == HostState.Unknown || (value.Message ?? "").Length > 180 || diagnostic.Length > 0;
        installation.UpdateState(value.State); UpdateInstall();
        if (diagnosticWindow != null && !diagnosticWindow.IsDisposed) diagnosticWindow.SetText(FullDetails());
        address.Text = value.State == HostState.Running && HostConfiguration.IsValidPort(value.BasePort) ? (Preview ? "gaming-pc" : Environment.MachineName) + ":" + value.BasePort : "Available when host is running";
        settingsAddress.Text = LauncherPolicy.SettingsUrl(value) ?? "Available when host is running";
        pairingHelp.Text = LauncherPolicy.PairingInstructions(Preview ? "gaming-pc" : Environment.MachineName, value, hostName);
        UpdateActions();
    }
    sealed class HostChoice { public string Id, Text; public override string ToString() { return Text; } }
    public void SetDiscovery(AgentReply reply) {
        diagnostic = reply.Diagnostic ?? ""; installation.Update(reply);
        hasSelection = !string.IsNullOrEmpty(reply.SelectionId); hostName = "host"; hostChoice.BeginUpdate(); hostChoice.Items.Clear();
        foreach (AgentHost item in reply.Hosts) {
            var choice = new HostChoice { Id = item.Id, Text = item.Name + (string.IsNullOrEmpty(item.Version) ? "" : " (" + item.Version + ")") + " — " + Brief(item.Path, 80) }; hostChoice.Items.Add(choice);
            if (item.Id == reply.SelectionId) { hostChoice.SelectedItem = choice; hostName = item.Name; hostChoice.AccessibleDescription = "Selected installation. Full paths are in Host details."; }
        }
        hostChoice.Visible = reply.Hosts.Length > 1; hostChoice.EndUpdate();
        startHost.Text = "Start " + hostName; startHost.AccessibleName = startHost.Text;
        settings.Text = "Open " + hostName + " settings"; settings.AccessibleName = settings.Text; settingsAddress.AccessibleName = hostName == "host" ? "Host web UI URL" : hostName + " web UI URL";
        SetHost(reply.Status); if (!string.IsNullOrEmpty(reply.Diagnostic)) SetNotice(reply.Diagnostic);
    }
    public void ShowGuidance(string title, string body)
    {
        placeholderTitle.Text = title; placeholderBody.Text = body; fitGuidance(); placeholder.BringToFront();
    }
    public void AttachViewer(Control viewer)
    {
        viewer.Dock = DockStyle.Fill; workspace.Controls.Add(viewer); viewer.BringToFront();
    }
    static string Brief(string text, int length) {
        text = (text ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
        return text.Length > length ? text.Substring(0, length - 3) + "..." : text;
    }
    void UpdateInstall() {
        install.Text = installation.HasHosts ? "Install another host..." : "Install a host...";
        install.AccessibleName = install.Text; install.Tag = installation.Prominent ? (object)true : "quiet";
        StyleButton(install);
    }
    internal ContextMenuStrip CreateInstallMenu() {
        var vendors = new ContextMenuStrip();
        foreach (string name in new[] { "Sunshine", "Apollo", "VibePollo" }) { string vendor = name; vendors.Items.Add(vendor, null, (s, e) => { if (InstallRequested != null) InstallRequested(vendor); }); }
        ThemeStyle.Menu(vendors, theme);
        return vendors;
    }
    internal bool ApproveInstall() {
        string question = installation.Confirmation;
        return question == null || (ConfirmInstall != null ? ConfirmInstall(question) : MessageBox.Show(this, question, "Install another host?", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes);
    }
    string FullDetails() { return hostTitle.Text + Environment.NewLine + Environment.NewLine + (host.Message ?? "") + (diagnostic.Length == 0 ? "" : Environment.NewLine + Environment.NewLine + diagnostic) + Environment.NewLine + Environment.NewLine + installation.Locations + Environment.NewLine + lastNotice; }
    internal void ShowDetails() {
        if (diagnosticWindow != null && !diagnosticWindow.IsDisposed) { diagnosticWindow.Activate(); return; }
        diagnosticWindow = new DiagnosticWindow("ChildStream details", FullDetails(), theme); diagnosticWindow.Show(this);
    }
    public void SetTheme(AppTheme selected) {
        theme = selected ?? AppThemes.All[0]; ThemeStyle.Apply(this, theme);
        var colors = AppTheme.Effective(theme, SystemInformation.HighContrast);
        hostTitle.ForeColor = host.State == HostState.Running ? colors.Success : host.State == HostState.Error ? colors.Error : colors.Text;
        if (diagnosticWindow != null && !diagnosticWindow.IsDisposed) ThemeStyle.Apply(diagnosticWindow, theme);
        if (installMenu != null && !installMenu.IsDisposed) ThemeStyle.Menu(installMenu, theme);
        footer.ForeColor = colors.Muted;
        if (themePicker != null && !themePicker.IsDisposed) themePicker.SetTheme(theme);
        if (ThemeRepainted != null) ThemeRepainted(theme);
    }
    internal ThemePickerWindow CreateThemePicker() {
        var picker = new ThemePickerWindow(theme);
        picker.ThemeSelected += choice => { SetTheme(choice); picker.SetTheme(choice); if (ThemeChanged != null) ThemeChanged(choice); };
        return picker;
    }
    void ShowThemes(Control anchor) {
        if (themePicker != null && !themePicker.IsDisposed) { themePicker.Activate(); return; }
        themePicker = CreateThemePicker(); themePicker.Show(this);
    }
    protected override void WndProc(ref Message m) {
        base.WndProc(ref m);
        if ((m.Msg == 0x0015 || m.Msg == 0x001A) && hostTitle != null && IsHandleCreated && !IsDisposed) SetTheme(theme);
    }
    protected override void Dispose(bool disposing) {
        if (disposing) { tips.Dispose(); if (themePicker != null) themePicker.Dispose(); if (installMenu != null) installMenu.Dispose(); if (diagnosticWindow != null) diagnosticWindow.Dispose(); }
        base.Dispose(disposing);
    }
}

static class LauncherPreview
{
    public static LauncherWindow Create(HostState state)
    {
        var form = new LauncherWindow(true);
        form.SetSession("Connected", "Preview of a separate Windows desktop. No real session is connected.", true, true);
        form.SetHost(new HostSnapshot(state, state == HostState.Running ? "Sunshine is running in the child desktop. Pair your compatible client with this host." : state == HostState.Stopped ? "Sunshine is off in the child desktop. Start it to make this desktop available to your client." : state == HostState.Checking ? "Checking Sunshine in the attached child desktop..." : "Child helper unavailable. Run setup and reconnect a newly signed-in child desktop, or start the helper inside the existing child desktop.", state == HostState.Unavailable || state == HostState.Checking ? 0 : 48989));
        form.ShowGuidance("Preview mode", "This is a synthetic GUI preview. No Windows session, credentials, streaming host, or system settings are accessed.");
        form.StartHostRequested += delegate { form.SetHost(new HostSnapshot(HostState.Running, "Preview: Sunshine would be started only after confirmation in the real launcher.", 48989)); };
        form.ConnectRequested += delegate { form.SetNotice("The desktop action is simulated."); };
        form.ReconnectRequested += delegate { form.SetNotice("Reconnect is simulated."); };
        form.SettingsRequested += delegate { form.SetNotice("Settings would open on the validated loopback URL."); };
        form.GuideRequested += delegate { form.SetNotice("Setup is separate and requires administrator rights."); };
        form.MinimizeRequested += delegate { form.WindowState = FormWindowState.Minimized; };
        return form;
    }
    public static int Capture(string directory)
    {
        Directory.CreateDirectory(directory);
        foreach (HostState state in new[] { HostState.Stopped, HostState.Running, HostState.Unavailable, HostState.Checking, HostState.Missing })
        using (var form = Create(state)) {
            if (state == HostState.Missing) form.SetDiscovery(new AgentReply { Status = new HostSnapshot(HostState.Missing, "Install Sunshine, Apollo or VibePollo to stream. You can keep using the child desktop.", 0) });
            if (state == HostState.Running) form.SetNotice(LauncherPolicy.SettingsFeedback(true));
            form.ShowInTaskbar = false; form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-20000, -20000);
            form.Show(); Application.DoEvents();
            using (var bitmap = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size)); bitmap.Save(Path.Combine(directory, state.ToString().ToLowerInvariant() + ".png"), ImageFormat.Png); }
            if (state == HostState.Running) {
                ((Button)form.Controls.Find("PairingSteps", true)[0]).PerformClick(); Application.DoEvents();
                using (var bitmap = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size)); bitmap.Save(Path.Combine(directory, "pairing.png"), ImageFormat.Png); }
                form.ClientSize = new Size(900, 640); Application.DoEvents();
                using (var bitmap = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size)); bitmap.Save(Path.Combine(directory, "pairing-compact.png"), ImageFormat.Png); }
                form.Size = form.MinimumSize; Application.DoEvents(); Save(form, directory, "pairing-minimum");
            }
            if (state == HostState.Stopped) {
                string selected = new string('a', 64);
                form.SetDiscovery(new AgentReply { SelectionId = selected, Hosts = new[] { new AgentHost { Id = selected, Name = "Apollo", Path = @"C:\Program Files\Apollo\sunshine.exe", Version = "0.4" }, new AgentHost { Id = new string('b', 64), Name = "Sunshine", Path = @"E:\Streaming host with a long folder name\Sunshine\sunshine.exe", Version = "2026.914" } }, Status = new HostSnapshot(HostState.Stopped, "The child host is off. Start it when you want to stream this desktop.", 48989) });
                form.SetNotice(LauncherPolicy.SettingsFeedback(false));
                form.ClientSize = new Size(900, 640); Application.DoEvents();
                using (var bitmap = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size)); bitmap.Save(Path.Combine(directory, "compact.png"), ImageFormat.Png); }
                form.Scale(new SizeF(1.5f, 1.5f)); Application.DoEvents();
                using (var bitmap = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size)); bitmap.Save(Path.Combine(directory, "large-layout.png"), ImageFormat.Png); }
            }
            form.Close();
        }
        using (var apps = new ChildAppWindow()) {
            apps.ShowInTaskbar = false; apps.StartPosition = FormStartPosition.Manual; apps.Location = new Point(-20000, -20000); apps.SetApps(new[] { new AgentApp { Id = new string('a', 64), Name = "Chrome", Path = @"C:\Program Files\Google\Chrome\Application\chrome.exe" }, new AgentApp { Id = new string('b', 64), Name = "Wallpaper Engine", Path = @"C:\Program Files (x86)\Steam\steamapps\common\wallpaper_engine\wallpaper64.exe" } }, "Browsers use a separate child profile. Check the app's window in the child desktop after opening it."); apps.Show(); Application.DoEvents();
            using (var bitmap = new Bitmap(apps.Width, apps.Height)) { apps.DrawToBitmap(bitmap, new Rectangle(Point.Empty, apps.Size)); bitmap.Save(Path.Combine(directory, "apps.png"), ImageFormat.Png); }
            apps.Close();
        }
        foreach (var theme in AppThemes.All) {
            using (var form = Create(HostState.Stopped)) {
                string selected = new string('a', 64);
                form.SetDiscovery(new AgentReply { SelectionId = selected, Hosts = new[] { new AgentHost { Id = selected, Name = "Sunshine", Path = @"C:\Program Files\Sunshine\sunshine.exe", Version = "2026.1017" } }, Status = new HostSnapshot(HostState.Stopped, "Sunshine is off in the child desktop. Start it when you want to stream.", 48989) });
                form.SetTheme(theme); CaptureForm(form, directory, "theme-" + theme.Id);
                form.Size = form.MinimumSize; Application.DoEvents(); Save(form, directory, "theme-" + theme.Id + "-compact");
            }
        }
        using (var form = Create(HostState.Unavailable)) {
            form.SetTheme(AppThemes.Find("dark")); form.SetHost(new HostSnapshot(HostState.Unavailable, "Child helper unavailable for C:\\Users\\Example\\Downloads\\" + new string('x', 2000) + ". The operation has timed out.", 0));
            form.SetNotice("A long error with a path: C:\\" + new string('y', 2000)); form.Size = form.MinimumSize; CaptureForm(form, directory, "long-error-compact");
        }
        using (var form = new DiagnosticWindow("ChildStream details - synthetic preview", "Helper unavailable\r\n\r\nA diagnostic with a long installation path: C:\\" + new string('x', 2000) + "\r\n\r\nThe operation has timed out.\r\nUse Setup / repair if automatic recovery does not restore the helper.", AppThemes.Find("dark"))) CaptureForm(form, directory, "details");
        using (var form = new ChildAppWindow()) {
            form.SetTheme(AppThemes.Find("dark")); form.SetApps(new[] { new AgentApp { Id = "preview", Name = "Chrome", Path = @"C:\Program Files\Google\Chrome\Application\chrome.exe" } }, "Synthetic preview. No app is launched."); CaptureForm(form, directory, "apps-dark");
        }
        using (var form = Create(HostState.Running)) using (var picker = form.CreateThemePicker()) {
            CaptureForm(picker, directory, "theme-picker");
            picker.Size = picker.MinimumSize; picker.Show(); Application.DoEvents(); Save(picker, directory, "theme-picker-compact"); picker.Close();
        }
        return 0;
    }
    static void CaptureForm(Form form, string directory, string name) {
        form.ShowInTaskbar = false; form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-20000, -20000); form.Show(); Application.DoEvents(); Save(form, directory, name); form.Hide();
    }
    static void Save(Control form, string directory, string name) {
        using (var bitmap = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size)); bitmap.Save(Path.Combine(directory, name + ".png"), ImageFormat.Png); }
    }
}
