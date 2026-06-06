using System.Drawing.Drawing2D;
using RMQuickTune.Core;

namespace RMQuickTune.Controls;

/// <summary>圆角按钮，支持实心与描边（ghost）两种样式。</summary>
public sealed class RoundButton : Control
{
    private bool _hover;
    private bool _down;

    /// <summary>按钮主色。</summary>
    public Color BaseColor { get; set; } = Theme.Accent;

    /// <summary>是否使用描边（镂空）样式：透明底 + 彩色边框/文字，悬浮时填充。</summary>
    public bool Ghost { get; set; }

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

        if (Ghost)
        {
            // 描边样式：默认透明，悬浮/按下时填充主色
            if (_hover || _down)
            {
                Color fill = _down ? ControlPaint.Dark(BaseColor, 0.05f) : BaseColor;
                using var path0 = Theme.RoundedRect(rect, Height / 2);
                using var b0 = new SolidBrush(fill);
                g.FillPath(b0, path0);
                TextRenderer.DrawText(g, Text, Font, rect, Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            else
            {
                using var path0 = Theme.RoundedRect(rect, Height / 2);
                using var pen = new Pen(BaseColor, 1.4f);
                g.DrawPath(pen, path0);
                TextRenderer.DrawText(g, Text, Font, rect, BaseColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            return;
        }

        Color solid = _down
            ? ControlPaint.Dark(BaseColor, 0.05f)
            : _hover ? ControlPaint.Light(BaseColor, 0.08f) : BaseColor;

        using (var path = Theme.RoundedRect(rect, Height / 2))
        using (var b = new SolidBrush(solid))
            g.FillPath(b, path);

        TextRenderer.DrawText(g, Text, Font, rect, Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}
