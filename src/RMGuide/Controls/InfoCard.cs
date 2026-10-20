using System.Drawing.Drawing2D;
using RMGuide.Core;

namespace RMGuide.Controls;

/// <summary>
/// 通用信息卡片：左侧色条 + 标题 + 若干行说明 + 右上状态徽章。
/// 说明行自动换行、卡片高度按内容自适应，不截断（不显示省略号）。
/// </summary>
public sealed class InfoCard : Control
{
    private const int TextX = 24;
    private const int TopPad = 14;
    private const int TitleH = 24;
    private const int LineGap = 2;
    private const int BottomPad = 10;

    private const TextFormatFlags WrapFlags =
        TextFormatFlags.Left | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding;

    private string _title = string.Empty;
    private string _badge = string.Empty;
    private Color _badgeColor = Theme.SubtleText;
    private Color _badgeSoft = Theme.StoppedSoft;
    private Color _stripe = Theme.Stopped;
    private readonly List<string> _lines = new();
    private bool _hover;

    public InfoCard()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.UserPaint
            | ControlStyles.ResizeRedraw
            | ControlStyles.SupportsTransparentBackColor, true);
        Height = 76;
        MinimumSize = new Size(0, 56);
        BackColor = Color.Transparent;
    }

    /// <summary>设置卡片内容。</summary>
    public void SetContent(string title, string badge, Color badgeColor, Color badgeSoft, Color stripe,
        IEnumerable<string> lines)
    {
        var newLines = lines.Where(l => !string.IsNullOrWhiteSpace(l)).ToList();

        if (_title == title && _badge == badge
            && _badgeColor == badgeColor && _stripe == stripe
            && _lines.SequenceEqual(newLines))
        {
            return;
        }

        _title = title;
        _badge = badge;
        _badgeColor = badgeColor;
        _badgeSoft = badgeSoft;
        _stripe = stripe;
        _lines.Clear();
        _lines.AddRange(newLines);

        AutoFit();
        Invalidate();
    }

    /// <summary>按当前宽度重新计算高度，保证所有说明行完整显示。</summary>
    public void AutoFit()
    {
        int h = MeasureDesiredHeight();
        if (Height != h) Height = h;
    }

    private int ContentWidth => Math.Max(80, Width - TextX - 24);

    private int MeasureDesiredHeight()
    {
        int y = TopPad + TitleH + 4;
        int w = ContentWidth;

        foreach (var line in _lines)
        {
            var size = TextRenderer.MeasureText(line, Theme.CardMeta, new Size(w, int.MaxValue), WrapFlags);
            y += Math.Max(18, size.Height) + LineGap;
        }

        return Math.Max(56, y + BottomPad - LineGap);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        AutoFit();
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);

        using (var path = Theme.RoundedRect(rect, 10))
        {
            using (var bg = new SolidBrush(_hover ? Color.FromArgb(250, 251, 253) : Theme.CardBg))
                g.FillPath(bg, path);
            using (var pen = new Pen(_hover ? Theme.Accent : Theme.CardBorder, _hover ? 1.4f : 1f))
                g.DrawPath(pen, path);
        }

        // 左侧色条
        using (var stripPath = Theme.RoundedRect(new Rectangle(0, 0, 8, Height - 1), 4))
        using (var strip = new SolidBrush(_stripe))
            g.FillPath(strip, stripPath);

        // 徽章先算宽度，标题让位
        int badgeWidth = DrawBadge(g);

        var titleRect = new Rectangle(TextX, TopPad, Math.Max(40, Width - TextX - badgeWidth - 32), TitleH);
        TextRenderer.DrawText(g, _title, Theme.CardTitle, titleRect,
            Theme.TitleText, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        // 说明行：自动换行，完整显示
        int y = TopPad + TitleH + 4;
        int w = ContentWidth;
        foreach (var line in _lines)
        {
            var size = TextRenderer.MeasureText(line, Theme.CardMeta, new Size(w, int.MaxValue), WrapFlags);
            int lineH = Math.Max(18, size.Height);
            if (y + lineH > Height - BottomPad) break;

            var lineRect = new Rectangle(TextX, y, w, lineH);
            TextRenderer.DrawText(g, line, Theme.CardMeta, lineRect, Theme.SubtleText, WrapFlags);
            y += lineH + LineGap;
        }
    }

    /// <summary>绘制右上角徽章，返回占用宽度。</summary>
    private int DrawBadge(Graphics g)
    {
        if (string.IsNullOrEmpty(_badge)) return 0;

        var size = TextRenderer.MeasureText(_badge, Theme.Badge);
        int w = size.Width + 24;
        int h = 26;
        var badgeRect = new Rectangle(Width - w - 16, TopPad, w, h);

        using (var path = Theme.RoundedRect(badgeRect, h / 2))
        using (var bg = new SolidBrush(_badgeSoft))
            g.FillPath(bg, path);

        TextRenderer.DrawText(g, _badge, Theme.Badge, badgeRect, _badgeColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

        return w;
    }
}
