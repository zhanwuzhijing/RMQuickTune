using System.Drawing.Drawing2D;
using RMQuickTune.Core;

namespace RMQuickTune.Controls;

/// <summary>
/// 单个程序运行状态卡片：左侧状态灯，中间程序名，右侧状态徽章。
/// 圆角、悬浮高亮、双缓冲防闪烁。
/// </summary>
public sealed class ProcessCard : Control
{
    private bool _isRunning;
    private int _instanceCount;
    private int? _pid;
    private bool _hover;

    public string ExeName { get; }

    public ProcessCard(string exeName)
    {
        ExeName = exeName;
        SetStyle(ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.UserPaint
            | ControlStyles.ResizeRedraw
            | ControlStyles.SupportsTransparentBackColor, true);
        Height = 64;
        BackColor = Color.Transparent;
        Cursor = Cursors.Default;
    }

    public void UpdateStatus(bool isRunning, int instanceCount, int? pid)
    {
        if (_isRunning == isRunning && _instanceCount == instanceCount && _pid == pid)
            return;
        _isRunning = isRunning;
        _instanceCount = instanceCount;
        _pid = pid;
        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _hover = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = false;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);

        // 卡片背景 + 边框
        using (var path = Theme.RoundedRect(rect, 10))
        {
            using (var bg = new SolidBrush(_hover ? Color.FromArgb(250, 251, 253) : Theme.CardBg))
                g.FillPath(bg, path);
            using (var pen = new Pen(_hover ? Theme.Accent : Theme.CardBorder, _hover ? 1.4f : 1f))
                g.DrawPath(pen, path);
        }

        var accent = _isRunning ? Theme.Running : Theme.Stopped;

        // 左侧色条
        using (var stripPath = Theme.RoundedRect(new Rectangle(0, 0, 8, Height - 1), 4))
        using (var strip = new SolidBrush(accent))
            g.FillPath(strip, stripPath);

        // 状态灯（圆点 + 柔光圈）
        int dotX = 26, dotY = Height / 2;
        using (var halo = new SolidBrush(_isRunning ? Theme.RunningSoft : Theme.StoppedSoft))
            g.FillEllipse(halo, dotX - 11, dotY - 11, 22, 22);
        using (var dot = new SolidBrush(accent))
            g.FillEllipse(dot, dotX - 5, dotY - 5, 10, 10);

        // 程序名
        var nameRect = new Rectangle(48, 10, Width - 48 - 110, 26);
        TextRenderer.DrawText(g, ExeName, Theme.CardTitle, nameRect,
            _isRunning ? Theme.TitleText : Theme.SubtleText,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        // 副信息：PID / 实例数
        string meta = _isRunning
            ? (_instanceCount > 1
                ? $"PID {_pid}   ·   {_instanceCount} 个实例"
                : $"PID {_pid}")
            : "未运行";
        var metaRect = new Rectangle(48, 34, Width - 48 - 110, 20);
        TextRenderer.DrawText(g, meta, Theme.CardMeta, metaRect, Theme.SubtleText,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        // 右侧状态徽章
        DrawBadge(g, _isRunning);
    }

    private void DrawBadge(Graphics g, bool running)
    {
        string text = running ? "运行中" : "已停止";
        var badgeColor = running ? Theme.Running : Theme.Stopped;
        var softColor = running ? Theme.RunningSoft : Theme.StoppedSoft;

        var size = TextRenderer.MeasureText(text, Theme.Badge);
        int w = size.Width + 24;
        int h = 24;
        var badgeRect = new Rectangle(Width - w - 16, (Height - h) / 2, w, h);

        using (var path = Theme.RoundedRect(badgeRect, h / 2))
        using (var bg = new SolidBrush(softColor))
            g.FillPath(bg, path);

        TextRenderer.DrawText(g, text, Theme.Badge, badgeRect, badgeColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}
