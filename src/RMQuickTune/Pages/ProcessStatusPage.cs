using RMQuickTune.Controls;
using RMQuickTune.Core;

namespace RMQuickTune.Pages;

/// <summary>
/// 主界面：按「裁判端 / 选手端」分栏展示相关程序的运行状态。
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

    private readonly TableLayoutPanel _columns;
    private readonly List<CategoryColumn> _categoryColumns = new();

    // ExeName -> 卡片，刷新时直接定位
    private readonly Dictionary<string, ProcessCard> _cardByExe = new(StringComparer.OrdinalIgnoreCase);

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

        // ---- 分栏区：每个分类一列 ----
        var categories = _monitor.Targets
            .Select(t => t.Category)
            .Distinct()
            .ToArray();

        _columns = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.ContentBg,
            Padding = new Padding(22, 0, 22, 16),
            ColumnCount = 1,
            RowCount = categories.Length,
        };
        _columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        for (int i = 0; i < categories.Length; i++)
            _columns.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / categories.Length));

        for (int i = 0; i < categories.Length; i++)
        {
            var cat = categories[i];
            var items = _monitor.Targets.Where(t => t.Category == cat).ToArray();
            var col = new CategoryColumn(cat, items);
            foreach (var kv in col.Cards)
                _cardByExe[kv.Key] = kv.Value;

            col.Margin = new Padding(0, i == 0 ? 4 : 8, 0, i == categories.Length - 1 ? 0 : 8);
            _categoryColumns.Add(col);
            _columns.Controls.Add(col, 0, i);
        }

        Controls.Add(_columns);
        Controls.Add(_header);

        PositionRefreshButton();

        _timer = new System.Windows.Forms.Timer { Interval = 2000 };
        _timer.Tick += (_, _) => RefreshStatus();
    }

    private void PositionRefreshButton()
    {
        _refreshBtn.Location = new Point(
            _header.ClientSize.Width - _refreshBtn.Width - 28,
            (_header.Height - _refreshBtn.Height) / 2 + 4);
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
        var perCategoryRunning = new Dictionary<ProcessCategory, int>();
        var perCategoryTotal = new Dictionary<ProcessCategory, int>();

        foreach (var s in statuses)
        {
            if (_cardByExe.TryGetValue(s.ExeName, out var card))
                card.UpdateStatus(s.IsRunning, s.InstanceCount, s.Pid);

            if (s.IsRunning) running++;
            perCategoryTotal[s.Category] = perCategoryTotal.GetValueOrDefault(s.Category) + 1;
            if (s.IsRunning)
                perCategoryRunning[s.Category] = perCategoryRunning.GetValueOrDefault(s.Category) + 1;
        }

        foreach (var col in _categoryColumns)
        {
            int r = perCategoryRunning.GetValueOrDefault(col.Category);
            int t = perCategoryTotal.GetValueOrDefault(col.Category);
            col.UpdateSummary(r, t);
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
