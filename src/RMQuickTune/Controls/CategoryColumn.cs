using System.Drawing.Drawing2D;
using RMQuickTune.Core;

namespace RMQuickTune.Controls;

/// <summary>
/// 单个分类栏：顶部为分类标题 + 运行计数徽章，下方为该分类的程序卡片列表（可滚动）。
/// </summary>
public sealed class CategoryColumn : Panel
{
    private readonly ProcessCategory _category;
    private readonly Label _titleLabel;
    private readonly CountBadge _countBadge;
    private readonly RoundButton _closeBtn;
    private readonly FlowLayoutPanel _cardArea;

    /// <summary>点击「一键关闭」按钮时触发，参数为本栏分类。</summary>
    public event Action<ProcessCategory>? CloseRequested;

    /// <summary>该栏内 ExeName -> 卡片 的映射。</summary>
    public IReadOnlyDictionary<string, ProcessCard> Cards { get; }

    public ProcessCategory Category => _category;

    public CategoryColumn(ProcessCategory category, IReadOnlyList<MonitoredProcess> items)
    {
        _category = category;
        BackColor = Theme.CardBg;
        Padding = new Padding(1);
        Dock = DockStyle.Fill;

        // ---- 标题行 ----
        var headerBar = new Panel
        {
            Dock = DockStyle.Top,
            Height = 64,
            BackColor = Theme.CardBg,
        };

        var accentBar = new Panel
        {
            Location = new Point(18, 18),
            Size = new Size(4, 28),
            BackColor = category == ProcessCategory.Referee
                ? Theme.Accent
                : Color.FromArgb(155, 89, 232),
        };

        _titleLabel = new Label
        {
            Text = category.DisplayName(),
            Font = new Font(Theme.FontFamily, 13F, FontStyle.Bold),
            ForeColor = Theme.TitleText,
            AutoSize = true,
            Location = new Point(32, 20),
        };

        _countBadge = new CountBadge
        {
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Size = new Size(64, 26),
        };

        _closeBtn = new RoundButton("一键关闭")
        {
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Size = new Size(96, 32),
            BaseColor = Theme.Danger,
            Ghost = true,
        };
        _closeBtn.Click += (_, _) => CloseRequested?.Invoke(_category);

        headerBar.Controls.Add(accentBar);
        headerBar.Controls.Add(_titleLabel);
        headerBar.Controls.Add(_countBadge);
        headerBar.Controls.Add(_closeBtn);
        headerBar.Resize += (_, _) => LayoutHeader(headerBar);
        LayoutHeader(headerBar);

        // 分隔线
        var divider = new Panel
        {
            Dock = DockStyle.Top,
            Height = 1,
            BackColor = Theme.CardBorder,
        };

        // ---- 卡片列表：固定高度的卡片自上而下排列，可滚动 ----
        _cardArea = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.CardBg,
            Padding = new Padding(14, 12, 14, 14),
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
        };

        var cards = new Dictionary<string, ProcessCard>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            var card = new ProcessCard(item.ExeName)
            {
                Height = 58,
                Margin = new Padding(0, 0, 0, 8),
            };
            cards[item.ExeName] = card;
            _cardArea.Controls.Add(card);
        }
        Cards = cards;

        _cardArea.Resize += (_, _) => ResizeCards();

        Controls.Add(_cardArea);
        Controls.Add(divider);
        Controls.Add(headerBar);

        ResizeCards();
    }

    private void ResizeCards()
    {
        int w = _cardArea.ClientSize.Width - _cardArea.Padding.Horizontal;
        if (w <= 0) return;
        foreach (ProcessCard card in _cardArea.Controls)
            card.Width = w;
    }

    /// <summary>右侧自右向左排列：一键关闭按钮 + 计数徽章。</summary>
    private void LayoutHeader(Panel headerBar)
    {
        int right = headerBar.Width - 18;
        _closeBtn.Location = new Point(right - _closeBtn.Width, (headerBar.Height - _closeBtn.Height) / 2);
        right -= _closeBtn.Width + 10;
        _countBadge.Location = new Point(right - _countBadge.Width, (headerBar.Height - _countBadge.Height) / 2);
    }

    public void UpdateSummary(int running, int total)
        => _countBadge.SetCount(running, total);

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        // 外边框圆角
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var pen = new Pen(Theme.CardBorder, 1f);
        using var path = Theme.RoundedRect(rect, 10);
        e.Graphics.DrawPath(pen, path);
    }
}

/// <summary>显示 "运行中/总数" 的小徽章。</summary>
public sealed class CountBadge : Control
{
    private int _running;
    private int _total;

    public CountBadge()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.UserPaint
            | ControlStyles.ResizeRedraw
            | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
    }

    public void SetCount(int running, int total)
    {
        if (_running == running && _total == total) return;
        _running = running;
        _total = total;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        bool allUp = _total > 0 && _running == _total;
        var fg = allUp ? Theme.Running : Theme.SubtleText;
        var bg = allUp ? Theme.RunningSoft : Theme.StoppedSoft;

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using (var path = Theme.RoundedRect(rect, Height / 2))
        using (var b = new SolidBrush(bg))
            g.FillPath(b, path);

        TextRenderer.DrawText(g, $"{_running}/{_total}", Theme.Badge, rect, fg,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}
