using System.Drawing.Drawing2D;
using RMQuickTune.Core;

namespace RMQuickTune.Controls;

/// <summary>圆角强调按钮。</summary>
public sealed class RoundButton : Control
{
    private bool _hover;
    private bool _down;

    public RoundButton(string text)
    {
        Text = text;
        SetStyle(ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.UserPaint
            | ControlStyles.ResizeRedraw
            | ControlStyles.SupportsTransparentBackColor, true);
        Size = new Size(104, 36);
        Cursor = Cursors.Hand;
        BackColor = Color.Transparent;
        Font = new Font(Theme.FontFamily, 9.5F, FontStyle.Bold);
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; _down = false; Invalidate(); }
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); _down = true; Invalidate(); }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); _down = false; Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        Color fill = _down
            ? ControlPaint.Dark(Theme.Accent, 0.05f)
            : _hover ? ControlPaint.Light(Theme.Accent, 0.08f) : Theme.Accent;

        using (var path = Theme.RoundedRect(rect, Height / 2))
        using (var b = new SolidBrush(fill))
            g.FillPath(b, path);

        TextRenderer.DrawText(g, Text, Font, rect, Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}
