using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

static class ThemeThumbnail
{
    // A simplified window, using exactly the roles used by the launcher. No raster assets to go stale.
    public static void Draw(Graphics g, Rectangle bounds, AppTheme selected)
    {
        var t = AppTheme.Effective(selected, SystemInformation.HighContrast);
        var saved = g.Save(); g.TranslateTransform(bounds.X, bounds.Y); g.ScaleTransform(bounds.Width / 240f, bounds.Height / 148f);
        Fill(g, t.Canvas, 0, 0, 240, 148); Fill(g, t.Quiet, 0, 0, 240, 16);
        using (var font = new Font("Segoe UI", 6.5f)) using (var brush = new SolidBrush(t.Muted)) g.DrawString("ChildStream", font, brush, 6, 2);
        using (var pen = new Pen(t.Muted, 1)) { g.DrawLine(pen, 207, 9, 212, 9); g.DrawRectangle(pen, 218, 5, 5, 5); g.DrawLine(pen, 230, 5, 235, 10); g.DrawLine(pen, 235, 5, 230, 10); }
        Fill(g, t.Surface, 0, 16, 68, 132);
        Fill(g, t.Text, 9, 28, 47, 3); Fill(g, t.Muted, 9, 40, 35, 2); Fill(g, t.Muted, 9, 47, 44, 2);
        Fill(g, t.Divider, 9, 63, 49, 1); Fill(g, t.Text, 9, 73, 35, 2);
        Fill(g, t.Accent, 9, 84, 49, 14); Fill(g, t.OnAccent, 18, 90, 30, 2);
        Fill(g, t.Quiet, 9, 105, 49, 13); Fill(g, t.Muted, 20, 111, 27, 1);
        Fill(g, t.Muted, 9, 130, 38, 1);
        Fill(g, t.Quiet, 78, 25, 34, 10); Fill(g, t.Quiet, 117, 25, 34, 10); Fill(g, t.Quiet, 156, 25, 26, 10);
        Fill(g, t.Surface, 78, 43, 152, 59);
        Fill(g, t.Text, 123, 65, 54, 2); Fill(g, t.Muted, 113, 74, 74, 1);
        Fill(g, t.Muted, 78, 113, 27, 1); Fill(g, t.Inset, 111, 108, 91, 10); Fill(g, t.Quiet, 207, 108, 23, 10);
        Fill(g, t.Muted, 78, 131, 27, 1); Fill(g, t.Inset, 111, 126, 91, 10); Fill(g, t.Quiet, 207, 126, 23, 10);
        using (var pen = new Pen(t.Border, 1)) g.DrawRectangle(pen, .5f, .5f, 239, 147);
        g.Restore(saved);
    }
    static void Fill(Graphics graphics, Color color, float x, float y, float width, float height) { using (var brush = new SolidBrush(color)) graphics.FillRectangle(brush, x, y, width, height); }
}

sealed class ThemeChoiceButton : Button
{
    public readonly AppTheme PreviewTheme;
    AppTheme chrome;
    bool selected;
    public bool SelectedChoice { get { return selected; } }
    public ThemeChoiceButton(AppTheme preview)
    {
        PreviewTheme = preview; chrome = preview; Name = "Theme-" + preview.Id; Text = preview.Name; AccessibleName = preview.Name + " theme";
        AccessibleRole = AccessibleRole.RadioButton; DoubleBuffered = true; FlatStyle = FlatStyle.Flat; Margin = new Padding(0, 0, 10, 12);
    }
    public void SetSelection(AppTheme active) {
        chrome = AppTheme.Effective(active, SystemInformation.HighContrast); selected = active.Id == PreviewTheme.Id;
        AccessibleDescription = selected ? "Selected theme. Applied and saved automatically." : "Apply this theme immediately."; Invalidate();
    }
    protected override void OnPaint(PaintEventArgs e) {
        e.Graphics.Clear(chrome.Surface);
        var preview = new Rectangle(4, 4, Math.Max(1, Width - 8), Math.Max(1, Height - Font.Height - 22));
        ThemeThumbnail.Draw(e.Graphics, preview, PreviewTheme);
        if (selected || (Focused && ShowFocusCues)) using (var pen = new Pen(chrome.Focus, 2)) e.Graphics.DrawRectangle(pen, 1, 1, Width - 3, Height - 3);
        TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(7, preview.Bottom + 7, Width - 30, Font.Height + 4), chrome.Text, TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        if (selected) using (var brush = new SolidBrush(chrome.Focus)) e.Graphics.FillEllipse(brush, Width - 18, preview.Bottom + 11, 7, 7);
    }
    protected override AccessibleObject CreateAccessibilityInstance() { return new ChoiceAccessibility(this); }
    sealed class ChoiceAccessibility : ControlAccessibleObject {
        readonly ThemeChoiceButton button;
        public ChoiceAccessibility(ThemeChoiceButton value) : base(value) { button = value; }
        public override AccessibleStates State { get { return base.State | (button.SelectedChoice ? AccessibleStates.Checked : AccessibleStates.None); } }
        public override string DefaultAction { get { return "Apply theme"; } }
        public override void DoDefaultAction() { button.PerformClick(); }
    }
}

sealed class ThemePickerWindow : Form
{
    readonly FlowLayoutPanel choices;
    public event Action<AppTheme> ThemeSelected;
    public ThemePickerWindow(AppTheme selected) {
        Text = "Themes"; Font = new Font("Segoe UI", 9.5f); AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(680, 490); MinimumSize = new Size(420, 360); StartPosition = FormStartPosition.CenterParent;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 3 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); Controls.Add(layout);
        var intro = new Label { Text = "Choose a look. Changes apply and save immediately.", AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 16) }; layout.Controls.Add(intro, 0, 0);
        choices = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = true, Margin = Padding.Empty, AccessibleName = "Theme previews" }; layout.Controls.Add(choices, 0, 1);
        foreach (var theme in AppThemes.All) {
            var choice = new ThemeChoiceButton(theme);
            choice.Click += delegate { if (ThemeSelected != null) ThemeSelected(choice.PreviewTheme); };
            choices.Controls.Add(choice);
        }
        choices.Resize += delegate { FitChoices(); };
        var close = new ThemedButton { Text = "Close", AccessibleName = "Close themes", AutoSize = true, MinimumSize = new Size(90, 32), Anchor = AnchorStyles.Right, Margin = new Padding(0, 12, 0, 0), DialogResult = DialogResult.Cancel };
        close.Click += delegate { Close(); }; CancelButton = close; layout.Controls.Add(close, 0, 2);
        SetTheme(selected); Shown += delegate { FitChoices(); foreach (ThemeChoiceButton button in choices.Controls) if (button.SelectedChoice) { button.Select(); break; } };
    }
    void FitChoices() {
        int available = Math.Max(180, choices.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 4);
        int columns = Math.Max(1, Math.Min(3, available / 164)); int width = available / columns - 10;
        foreach (Control button in choices.Controls) button.Size = new Size(width, (int)(width * .61f) + button.Font.Height + 22);
    }
    public void SetTheme(AppTheme selected) { ThemeStyle.Apply(this, selected); foreach (ThemeChoiceButton choice in choices.Controls) choice.SetSelection(selected); }
}
