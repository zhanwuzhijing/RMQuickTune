using System.Drawing.Drawing2D;
using RMQuickTune.Core;

namespace RMQuickTune.Controls;

/// <summary>侧边栏导航项：左侧选中指示条 + 悬浮/选中高亮。</summary>
public sealed class NavButton : Control
{
    private bool _active;
    private bool _hover;

    public NavButton(string text)
    {
        Text = text;
        SetStyle(ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.UserPaint
            | ControlStyles.ResizeRedraw, true);
        Height = 46;
        Dock = DockStyle.Top;
        Cursor = Cursors.Hand;
        BackColor = Theme.SidebarBg;
    }

    public bool Active
    {
        get => _active;
        set { if (_active != value) { _active = value; Invalidate(); } }
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _hover = true; Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = false; Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        Color bg = _active ? Theme.SidebarItemActiveBg
            : _hover ? Theme.SidebarItemHover
            : Theme.SidebarBg;
        using (var b = new SolidBrush(bg))
            g.FillRectangle(b, ClientRectangle);

        // 左侧选中指示条
        if (_active)
        {
            using var accent = new SolidBrush(Theme.Accent);
            g.FillRectangle(accent, 0, 8, 3, Height - 16);
        }

        Color textColor = _active ? Theme.SidebarItemActiveText : Theme.SidebarItemText;
        var textRect = new Rectangle(20, 0, Width - 24, Height);
        TextRenderer.DrawText(g, Text, Theme.NavItem, textRect, textColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
    }
}
