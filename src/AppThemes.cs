using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

sealed class AppTheme
{
    public readonly string Id, Name;
    public readonly Color Canvas, Surface, Text, Muted, Accent, OnAccent, Border, Quiet, Success, Error, Inset, Divider, Focus;
    public AppTheme(string id, string name, string canvas, string surface, string text, string muted, string accent, string onAccent, string border, string quiet, string success, string error, string inset, string divider, string focus)
    {
        Id = id; Name = name; Canvas = Hex(canvas); Surface = Hex(surface); Text = Hex(text); Muted = Hex(muted);
        Accent = Hex(accent); OnAccent = Hex(onAccent); Border = Hex(border); Quiet = Hex(quiet); Success = Hex(success); Error = Hex(error); Inset = Hex(inset); Divider = Hex(divider); Focus = Hex(focus);
    }
    static Color Hex(string value) { return ColorTranslator.FromHtml("#" + value); }
    AppTheme(AppTheme source) { Id = source.Id; Name = source.Name; Canvas = Surface = Quiet = Inset = SystemColors.Window; Text = Muted = Success = Error = SystemColors.WindowText; Accent = SystemColors.Highlight; OnAccent = SystemColors.HighlightText; Border = Divider = Focus = SystemColors.WindowText; }
    public static AppTheme Effective(AppTheme theme, bool highContrast) { return highContrast ? new AppTheme(theme) : theme; }
}

static class AppThemes
{
    // Coordinated surfaces, softened text, quiet structure and readable focus are independent roles.
    public static readonly AppTheme[] All = {
        new AppTheme("light", "Light", "F2F4F5", "FBFCFC", "303A41", "56636C", "446A80", "F4F7F9", "BAC4CB", "E5EAED", "3F7059", "9A5058", "EDF0F2", "DCE2E6", "446A80"),
        new AppTheme("dark", "Dark", "14191F", "202832", "DCE3E9", "A6B4C2", "496A82", "F3F7FA", "64788A", "2A3541", "99BAA6", "D6A2A3", "293541", "3A4856", "A0C2D8"),
        new AppTheme("ocean", "Ocean", "16242C", "1C303A", "CBD8DD", "9DB3BC", "3E6577", "EDF1F2", "4A6673", "29424E", "8EAF9D", "C99692", "233A44", "344C58", "7FABB9"),
        new AppTheme("sunset", "Sunset", "E8DFD0", "F1EBDD", "4A4035", "695846", "88643D", "F7F0E5", "BAA78D", "E2D5BD", "506948", "92554B", "E3D7C4", "D7C8B0", "8A633F"),
        new AppTheme("forest", "Forest", "1B2722", "25372D", "E0E8D9", "B0BEA7", "4F6B43", "F3F7EC", "6C8068", "304335", "ACC99B", "D5AD97", "2D4035", "455C46", "B0C99C"),
        new AppTheme("rose", "Rose", "E6DDE1", "F0E9ED", "503F4C", "65505E", "805A73", "F8F1F6", "BAA2B1", "DECDD7", "576D5F", "96505C", "E1D3DC", "D3BEC9", "805A73")
    };
    public static AppTheme Find(string id) {
        switch (id) { case "current": id = "light"; break; case "whales": id = "ocean"; break; case "cherry": id = "sunset"; break; case "mint": id = "forest"; break; case "amethyst": id = "rose"; break; }
        foreach (var theme in All) if (theme.Id == id) return theme; return All[0];
    }
    public static AppTheme Load(string root)
    {
        try { string path = Path.Combine(root, "appearance.cfg"); if (new FileInfo(path).Length <= 128) return Find(File.ReadAllText(path).Trim()); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        return All[0];
    }
    public static void Save(string root, AppTheme theme)
    {
        string path = Path.Combine(root, "appearance.cfg"), temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, Find(theme.Id).Id + Environment.NewLine); if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

static class ThemeStyle
{
    public static void Apply(Control control, AppTheme selected)
    {
        if (control is AxHost) return; // The remote desktop belongs to Windows, not to our palette.
        var theme = AppTheme.Effective(selected, SystemInformation.HighContrast);
        string role = control.Tag as string;
        control.BackColor = control is Label ? Color.Transparent : role == "canvas" ? theme.Canvas : role == "divider" ? theme.Divider : role == "outline" ? theme.Border : role == "plain" ? theme.Surface : control is TextBox || role == "inset" ? theme.Inset : theme.Surface;
        control.ForeColor = role == "muted" ? theme.Muted : theme.Text;
        var button = control as Button;
        if (button != null) Button(button, selected);
        var strip = control as ToolStrip;
        if (strip != null) Menu(strip, selected);
        foreach (Control child in control.Controls) Apply(child, selected);
    }
    public static void Button(Button button, AppTheme selected)
    {
        var theme = AppTheme.Effective(selected, SystemInformation.HighContrast);
        if (SystemInformation.HighContrast) { button.FlatStyle = FlatStyle.Standard; button.UseVisualStyleBackColor = true; button.ForeColor = SystemColors.ControlText; return; }
        bool primary = button.Tag is bool && (bool)button.Tag;
        bool quiet = (button.Tag as string) == "quiet";
        button.FlatStyle = FlatStyle.Flat; button.UseVisualStyleBackColor = false;
        button.BackColor = !button.Enabled || quiet ? theme.Quiet : primary ? theme.Accent : theme.Inset;
        button.ForeColor = !button.Enabled || quiet ? theme.Muted : primary ? theme.OnAccent : theme.Text;
        button.FlatAppearance.BorderColor = primary && button.Enabled ? theme.Accent : theme.Border;
        var painted = button as ThemedButton; if (painted != null) { painted.FocusColor = theme.Focus; painted.HotTextColor = primary ? theme.OnAccent : theme.Text; }
        button.FlatAppearance.MouseOverBackColor = primary ? Mix(theme.Accent, Color.Black, .10) : Mix(theme.Quiet, theme.Accent, .12);
        button.FlatAppearance.MouseDownBackColor = primary ? Mix(theme.Accent, Color.Black, .22) : Mix(theme.Quiet, theme.Accent, .25);
        button.Invalidate();
    }
    public static Color Mix(Color a, Color b, double amount) { return Color.FromArgb((int)(a.R + (b.R - a.R) * amount), (int)(a.G + (b.G - a.G) * amount), (int)(a.B + (b.B - a.B) * amount)); }
    public static void Menu(ToolStrip strip, AppTheme selected)
    {
        var theme = AppTheme.Effective(selected, SystemInformation.HighContrast);
        strip.ShowItemToolTips = false; strip.BackColor = theme.Surface; strip.ForeColor = theme.Text;
        strip.Renderer = SystemInformation.HighContrast ? (ToolStripRenderer)new ToolStripSystemRenderer() : new ThemeMenuRenderer(theme);
        foreach (ToolStripItem item in strip.Items) { item.ForeColor = theme.Text; item.BackColor = theme.Surface; }
    }
    sealed class ThemeMenuRenderer : ToolStripProfessionalRenderer
    {
        readonly AppTheme theme;
        public ThemeMenuRenderer(AppTheme value) : base(new MenuColors(value)) { theme = value; RoundedEdges = false; }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e) { e.TextColor = e.Item.Selected ? theme.OnAccent : theme.Text; base.OnRenderItemText(e); }
    }
    sealed class MenuColors : ProfessionalColorTable
    {
        readonly AppTheme theme; public MenuColors(AppTheme value) { theme = value; UseSystemColors = false; }
        public override Color ToolStripDropDownBackground { get { return theme.Surface; } }
        public override Color ImageMarginGradientBegin { get { return theme.Surface; } }
        public override Color ImageMarginGradientMiddle { get { return theme.Surface; } }
        public override Color ImageMarginGradientEnd { get { return theme.Surface; } }
        public override Color MenuItemSelected { get { return theme.Accent; } }
        public override Color MenuItemBorder { get { return theme.Accent; } }
        public override Color MenuBorder { get { return theme.Border; } }
        public override Color ToolStripGradientBegin { get { return theme.Surface; } }
        public override Color ToolStripGradientMiddle { get { return theme.Surface; } }
        public override Color ToolStripGradientEnd { get { return theme.Surface; } }
        public override Color StatusStripGradientBegin { get { return theme.Surface; } }
        public override Color StatusStripGradientEnd { get { return theme.Surface; } }
    }
}

sealed class DiagnosticWindow : Form
{
    readonly TextBox body;
    public DiagnosticWindow(string title, string text, AppTheme theme)
    {
        Text = title; Font = new Font("Segoe UI", 9.5f); AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(620, 380); MinimumSize = new Size(420, 280); StartPosition = FormStartPosition.CenterParent;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 1, RowCount = 2 };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); Controls.Add(layout);
        body = new TextBox { Multiline = true, ReadOnly = true, WordWrap = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Text = text, AccessibleName = "Full diagnostic details", SelectionStart = 0, SelectionLength = 0 }; layout.Controls.Add(body, 0, 0);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, 12, 0, 0) }; layout.Controls.Add(actions, 0, 1);
        var copy = new ThemedButton { Text = "Copy details", AccessibleName = "Copy details", AutoSize = true, MinimumSize = new Size(100, 40) };
        copy.Click += delegate { try { Clipboard.SetText(body.Text); copy.Text = "Copied"; } catch (Exception) { copy.Text = "Select text to copy"; } }; actions.Controls.Add(copy);
        var close = new ThemedButton { Text = "Close", AutoSize = true, MinimumSize = new Size(90, 40), DialogResult = DialogResult.Cancel }; actions.Controls.Add(close); CancelButton = close;
        ThemeStyle.Apply(this, theme); Shown += delegate { body.Select(0, 0); };
    }
    public void SetText(string text) { if (body.Text != text) body.Text = text; }
}

// Native keyboard and accessibility behavior, with consistent states across all palettes.
sealed class ThemedButton : Button
{
    bool hovered, mousePressed, keyPressed;
    public Color FocusColor = SystemColors.Highlight, HotTextColor = SystemColors.ControlText;
    public ThemedButton() { SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true); }
    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hovered = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hovered = mousePressed = false; Invalidate(); }
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) mousePressed = true; Invalidate(); }
    protected override void OnMouseUp(MouseEventArgs e) { mousePressed = false; base.OnMouseUp(e); Invalidate(); }
    protected override void OnMouseCaptureChanged(EventArgs e) { mousePressed = false; base.OnMouseCaptureChanged(e); Invalidate(); }
    protected override void OnKeyDown(KeyEventArgs e) { base.OnKeyDown(e); if (e.KeyCode == Keys.Space) keyPressed = true; Invalidate(); }
    protected override void OnKeyUp(KeyEventArgs e) { keyPressed = false; base.OnKeyUp(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { keyPressed = mousePressed = false; base.OnLostFocus(e); Invalidate(); }
    protected override void OnEnabledChanged(EventArgs e) { if (!Enabled) hovered = mousePressed = keyPressed = false; base.OnEnabledChanged(e); Invalidate(); }
    static System.Drawing.Drawing2D.GraphicsPath Rounded(RectangleF rect, float radius) {
        var path = new System.Drawing.Drawing2D.GraphicsPath(); float diameter = radius * 2;
        path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90); path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90); path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90); path.CloseFigure(); return path;
    }
    protected override void OnPaint(PaintEventArgs e) {
        if (SystemInformation.HighContrast) { base.OnPaint(e); return; }
        if (Width < 4 || Height < 4) return;
        bool hot = Enabled && (hovered || keyPressed), down = Enabled && (mousePressed || keyPressed);
        Color fill = down ? FlatAppearance.MouseDownBackColor : hot ? FlatAppearance.MouseOverBackColor : BackColor;
        e.Graphics.Clear(Parent == null ? BackColor : Parent.BackColor);
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using (var path = Rounded(new RectangleF(1, 1, Width - 3, Height - 3), 5)) {
            using (var brush = new SolidBrush(fill)) e.Graphics.FillPath(brush, path);
            using (var pen = new Pen(Enabled && (hot || (Focused && ShowFocusCues)) ? FocusColor : FlatAppearance.BorderColor, Enabled && (Focused && ShowFocusCues) ? 2 : 1)) e.Graphics.DrawPath(pen, path);
        }
        var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;
        if (!ShowKeyboardCues) flags |= TextFormatFlags.HidePrefix;
        TextRenderer.DrawText(e.Graphics, Text, Font, Rectangle.Inflate(ClientRectangle, -8, -2), hot ? HotTextColor : ForeColor, flags);
    }
}
