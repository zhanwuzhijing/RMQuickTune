using RMQuickTune.Controls;
using RMQuickTune.Core;

namespace RMQuickTune.Pages;

/// <summary>
/// 主界面：以卡片网格展示 RoboMaster 赛事引擎相关程序的运行状态。
/// 定时自动刷新，绿色=运行中，灰色=已停止。
/// </summary>
public sealed class ProcessStatusPage : PageBase
{
    private readonly ProcessMonitor _monitor = new();
    private readonly System.Windows.Forms.Timer _timer;

    private readonly Panel _header;
    private readonly Label _titleLabel;
    private readonly Label _summaryLabel;
    private readonly RoundButton _refreshBtn;
    private readonly FlowLayoutPanel _cardArea;
    private readonly List<ProcessCard> _cards = new();

    public override string DisplayName => "运行状态";

    public ProcessStatusPage()
    {
        BackColor = Theme.ContentBg;

        // ---- 顶部标题栏 ----
        _header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 92,
            BackColor = Theme.ContentBg,
            Padding = new Padding(28, 20, 28, 0),
        };

        _titleLabel = new Label
        {
            Text = "程序运行状态",
            Font = Theme.PageTitle,
            ForeColor = Theme.TitleText,
            AutoSize = true,
            Location = new Point(28, 18),
        };

        _summaryLabel = new Label
        {
            Text = "正在检测…",
            Font = Theme.PageSubtitle,
            ForeColor = Theme.SubtleText,
            AutoSize = true,
            Location = new Point(30, 54),
        };

        _refreshBtn = new RoundButton("立即刷新")
        {
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
        };
        _refreshBtn.Click += (_, _) => RefreshStatus();

        _header.Controls.Add(_titleLabel);
        _header.Controls.Add(_summaryLabel);
        _header.Controls.Add(_refreshBtn);
        _header.Resize += (_, _) => PositionRefreshButton();

        // ---- 卡片区（可滚动、自动换行）----
        _cardArea = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = Theme.ContentBg,
            Padding = new Padding(22, 4, 22, 22),
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
        };
        _cardArea.Resize += (_, _) => ResizeCards();

        foreach (var target in _monitor.Targets)
        {
            var card = new ProcessCard(target.ExeName)
            {
                Margin = new Padding(6),
            };
            _cards.Add(card);
            _cardArea.Controls.Add(card);
        }

        Controls.Add(_cardArea);
        Controls.Add(_header);

        PositionRefreshButton();
        ResizeCards();

        _timer = new System.Windows.Forms.Timer { Interval = 2000 };
        _timer.Tick += (_, _) => RefreshStatus();
    }

    private void PositionRefreshButton()
    {
        _refreshBtn.Location = new Point(
            _header.ClientSize.Width - _refreshBtn.Width - 28,
            (_header.Height - _refreshBtn.Height) / 2 + 4);
    }

    /// <summary>根据可用宽度让卡片自适应：宽屏两列，窄屏单列。</summary>
    private void ResizeCards()
    {
        int avail = _cardArea.ClientSize.Width - _cardArea.Padding.Horizontal;
        if (avail <= 0) return;

        // 每列最小约 360px，决定列数（1 或 2）
        int columns = avail >= 760 ? 2 : 1;
        int margin = 12; // 每张卡左右 margin 合计
        int cardWidth = (avail / columns) - margin;
        if (cardWidth < 280) cardWidth = avail - margin;

        _cardArea.SuspendLayout();
        foreach (var card in _cards)
            card.Width = cardWidth;
        _cardArea.ResumeLayout();
    }

    public override void OnActivated()
    {
        RefreshStatus();
        _timer.Start();
    }

    public override void OnDeactivated() => _timer.Stop();

    private void RefreshStatus()
    {
        var statuses = _monitor.CheckAll();
        int running = 0;

        for (int i = 0; i < statuses.Count && i < _cards.Count; i++)
        {
            var s = statuses[i];
            if (s.IsRunning) running++;
            _cards[i].UpdateStatus(s.IsRunning, s.InstanceCount, s.Pid);
        }

        bool allUp = running == statuses.Count;
        _summaryLabel.Text =
            $"运行中 {running} / {statuses.Count}      最后刷新 {DateTime.Now:HH:mm:ss}";
        _summaryLabel.ForeColor = allUp ? Theme.Running : Theme.SubtleText;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer?.Stop();
            _timer?.Dispose();
        }
        base.Dispose(disposing);
    }
}
