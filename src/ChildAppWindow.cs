using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

sealed class ChildAppWindow : Form
{
    bool available = true, busy, awaitingSearch, suppressSearch, launchPending;
    AppTheme theme = AppThemes.All[0];
    public void SetTheme(AppTheme value) { theme = value; ThemeStyle.Apply(this, value); }
    readonly ListView list; readonly Button open, administrator, browse, refresh, previous, next; readonly TextBox detail, search;
    readonly Label count; readonly Timer searchTimer = new Timer { Interval = 300 }, launchTimer = new Timer { Interval = 1500 };
    readonly Stack<int> pages = new Stack<int>();
    int offset, nextOffset = -1;
    public string Query { get { return search.Text; } }
    public int Offset { get { return offset; } }
    public event Action<string> LaunchRequested, AdministratorRequested;
    public event Action<string, int, bool> LookupRequested;
    public event EventHandler BrowseRequested, RefreshRequested;
    public ChildAppWindow() {
        Text = "Open in child desktop"; Font = new Font("Segoe UI", 9.5f); AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96, 96);
        ClientSize = new Size(760, 480); MinimumSize = new Size(560, 440); StartPosition = FormStartPosition.CenterParent;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 6 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int row = 0; row < 6; row++) layout.RowStyles.Add(new RowStyle(row == 2 ? SizeType.Percent : row == 4 ? SizeType.Absolute : SizeType.AutoSize, row == 2 ? 100 : row == 4 ? 72 : 0));
        Controls.Add(layout);
        layout.Controls.Add(new Label { Text = "Installed apps", Font = new Font(Font, FontStyle.Bold), AutoSize = true, Margin = new Padding(0, 0, 0, 10) }, 0, 0);
        var searchRow = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Margin = new Padding(0, 0, 0, 12) };
        searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        searchRow.Controls.Add(new Label { Text = "&Search", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 12, 0) }, 0, 0);
        search = new TextBox { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, MaxLength = 128, AccessibleName = "Search installed apps", Margin = Padding.Empty };
        var searchSurface = new Panel { Dock = DockStyle.Fill, Height = search.PreferredHeight + 16, Padding = new Padding(10, 8, 10, 6), Margin = Padding.Empty, Tag = "inset" };
        searchSurface.Controls.Add(search); searchSurface.Paint += delegate(object sender, PaintEventArgs e) { var colors = AppTheme.Effective(theme, SystemInformation.HighContrast); using (var pen = new Pen(search.Focused ? colors.Focus : colors.Border)) e.Graphics.DrawRectangle(pen, 0, 0, searchSurface.Width - 1, searchSurface.Height - 1); };
        search.Enter += delegate { searchSurface.Invalidate(); }; search.Leave += delegate { searchSurface.Invalidate(); }; searchRow.Controls.Add(searchSurface, 1, 0); layout.Controls.Add(searchRow, 0, 1);
        list = new ListView { View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false, Dock = DockStyle.Fill, AccessibleName = "Installed child apps", Margin = Padding.Empty, BorderStyle = BorderStyle.None, OwnerDraw = true };
        list.DrawColumnHeader += delegate(object sender, DrawListViewColumnHeaderEventArgs e) {
            if (SystemInformation.HighContrast) { e.DrawDefault = true; return; }
            using (var brush = new SolidBrush(theme.Quiet)) e.Graphics.FillRectangle(brush, e.Bounds);
            TextRenderer.DrawText(e.Graphics, e.Header.Text, list.Font, Rectangle.Inflate(e.Bounds, -6, 0), theme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        };
        list.DrawItem += delegate(object sender, DrawListViewItemEventArgs e) { e.DrawDefault = true; };
        list.DrawSubItem += delegate(object sender, DrawListViewSubItemEventArgs e) { e.DrawDefault = true; };
        list.Columns.Add("App", 190); list.Columns.Add("Location", 480);
        var listSurface = new Panel { Dock = DockStyle.Fill, Padding = new Padding(1), Margin = Padding.Empty, Tag = "outline" }; listSurface.Controls.Add(list); layout.Controls.Add(listSurface, 0, 2);
        list.Resize += delegate { list.Columns[1].Width = Math.Max(120, list.ClientSize.Width - list.Columns[0].Width - 6); };
        var navigation = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, Margin = new Padding(0, 6, 0, 6), WrapContents = true };
        previous = MakeButton("Previous", delegate { if (pages.Count == 0) return; offset = pages.Pop(); Lookup(false); });
        next = MakeButton("Next", delegate { if (nextOffset < 0) return; pages.Push(offset); offset = nextOffset; Lookup(false); });
        count = new Label { Text = "Checking installed apps...", AutoSize = true, Margin = new Padding(6, 13, 0, 0), Tag = "muted" };
        navigation.Controls.Add(previous); navigation.Controls.Add(next); navigation.Controls.Add(count); layout.Controls.Add(navigation, 0, 3);
        detail = new TextBox { Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical, AccessibleName = "Child app result", Tag = "plain", Text = "Apps open in the child session. Browsers use separate data. Some apps share settings or require their main-desktop instance to close." };
        layout.Controls.Add(detail, 0, 4);
        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true, Margin = Padding.Empty }; layout.Controls.Add(actions, 0, 5);
        open = MakeButton("Open in child", delegate { Launch(false); }); open.Tag = true;
        administrator = MakeButton("Run as administrator", delegate { Launch(true); });
        browse = MakeButton("Browse app...", delegate { if (BrowseRequested != null) BrowseRequested(this, EventArgs.Empty); });
        refresh = MakeButton("Refresh", delegate { pages.Clear(); offset = 0; if (RefreshRequested != null) RefreshRequested(this, EventArgs.Empty); Lookup(true); });
        foreach (var action in new[] { open, administrator, browse, refresh }) actions.Controls.Add(action);
        list.SelectedIndexChanged += delegate { UpdateActions(); };
        list.DoubleClick += delegate { if (open.Enabled) Launch(false); };
        search.TextChanged += delegate {
            if (suppressSearch) return; pages.Clear(); offset = 0; awaitingSearch = true; searchTimer.Stop(); searchTimer.Start(); UpdateActions();
        };
        searchTimer.Tick += delegate { searchTimer.Stop(); Lookup(false); };
        launchTimer.Tick += delegate { if (available && !busy && !awaitingSearch) Lookup(false); };
        AcceptButton = open; UpdateActions(); SetTheme(theme);
    }
    Button MakeButton(string text, EventHandler click) {
        var button = new ThemedButton { Text = text, AccessibleName = text, AutoSize = true, MinimumSize = new Size(0, 40), Margin = new Padding(0, 4, 8, 0), Padding = new Padding(10, 3, 10, 3) };
        button.Click += click; button.EnabledChanged += delegate { ThemeStyle.Button(button, theme); }; return button;
    }
    void Launch(bool elevated) {
        if (!available || busy || awaitingSearch || launchPending || list.SelectedItems.Count != 1) return;
        var handler = elevated ? AdministratorRequested : LaunchRequested;
        if (handler != null) handler((string)list.SelectedItems[0].Tag);
    }
    void Lookup(bool rescan) { awaitingSearch = true; UpdateActions(); if (LookupRequested != null) LookupRequested(Query, offset, rescan); }
    void UpdateActions() {
        list.Enabled = available && !busy && !awaitingSearch;
        browse.Enabled = refresh.Enabled = list.Enabled && !launchPending;
        search.Enabled = available; open.Enabled = administrator.Enabled = list.Enabled && !launchPending && list.SelectedItems.Count == 1;
        previous.Enabled = list.Enabled && pages.Count > 0; next.Enabled = list.Enabled && nextOffset >= 0;
    }
    public void SetBusy(bool value) { busy = value; UpdateActions(); }
    public void SetMessage(string message) { detail.Text = message; }
    public void SetAvailable(bool value) {
        bool restored = !available && value; available = value;
        if (!value) { searchTimer.Stop(); launchTimer.Stop(); awaitingSearch = false; detail.Text = "Connect the child desktop before opening an app."; }
        UpdateActions(); if (restored) Lookup(false);
    }
    public void LookupFailed(string message) { awaitingSearch = false; SetMessage(message); UpdateActions(); }
    public void ResetSearch() { searchTimer.Stop(); suppressSearch = true; search.Text = ""; suppressSearch = false; pages.Clear(); offset = 0; }
    public bool Matches(string query, int page) { return Query == query && offset == page; }
    public void SetPage(AgentReply reply, string query, int requestedOffset) {
        if (!Matches(query, requestedOffset)) return;
        if (reply.AppOffset != requestedOffset) pages.Clear();
        offset = reply.AppOffset; nextOffset = reply.AppNext; launchPending = reply.AppLaunchPending;
        if (launchPending) launchTimer.Start(); else launchTimer.Stop();
        count.Text = reply.AppTotal == 0 ? "No matching apps" : (offset + 1) + "–" + (offset + reply.Apps.Length) + " of " + reply.AppTotal;
        SetApps(reply.Apps, reply.Status.State == HostState.Unavailable ? reply.Status.Message : reply.Diagnostic);
    }
    public void SetApps(AgentApp[] apps, string message) {
        awaitingSearch = false;
        if (count.Text == "Checking installed apps...") count.Text = apps.Length + (apps.Length == 1 ? " app" : " apps");
        string selected = list.SelectedItems.Count == 1 ? (string)list.SelectedItems[0].Tag : null; list.BeginUpdate(); list.Items.Clear();
        foreach (AgentApp app in apps) { var item = new ListViewItem(app.Name) { Tag = app.Id }; item.SubItems.Add(app.Path); list.Items.Add(item); if (app.Id == selected) item.Selected = true; }
        list.EndUpdate();
        detail.Text = !string.IsNullOrEmpty(message) ? message : apps.Length == 0 ? "No matching apps. Clear Search, Refresh, or Browse to the app's installed EXE." : "Open normally, or choose Run as administrator. Windows consent may appear on the main desktop. Existing app instances and game restrictions still apply.";
        UpdateActions();
    }
    protected override void Dispose(bool disposing) { if (disposing) { searchTimer.Dispose(); launchTimer.Dispose(); } base.Dispose(disposing); }
}
