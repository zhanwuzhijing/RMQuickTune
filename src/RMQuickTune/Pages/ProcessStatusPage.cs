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
    private readonly Label _engineLabel;
    private readonly ToolTip _engineTip = new();
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
            Height = 112,
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

        _engineLabel = new Label
        {
            Text = "Engine 检测中…",
            Font = Theme.PageSubtitle,
            ForeColor = Theme.SubtleText,
            AutoSize = true,
            Location = new Point(30, 78),
        };

        _refreshBtn = new RoundButton("立即刷新")
        {
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
        };
        _refreshBtn.Click += (_, _) => RefreshStatus();

        _header.Controls.Add(_titleLabel);
        _header.Controls.Add(_summaryLabel);
        _header.Controls.Add(_engineLabel);
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
            ColumnCount = categories.Length,
            RowCount = 1,
        };
        for (int i = 0; i < categories.Length; i++)
            _columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / categories.Length));
        _columns.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        for (int i = 0; i < categories.Length; i++)
        {
            var cat = categories[i];
            var items = _monitor.Targets.Where(t => t.Category == cat).ToArray();
            var col = new CategoryColumn(cat, items);
            foreach (var kv in col.Cards)
                _cardByExe[kv.Key] = kv.Value;
            col.CloseRequested += OnCloseCategoryRequested;

            col.Margin = new Padding(i == 0 ? 0 : 8, 4, i == categories.Length - 1 ? 0 : 8, 0);
            _categoryColumns.Add(col);
            _columns.Controls.Add(col, i, 0);
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

    /// <summary>处理「一键关闭」请求：确认后结束该分类下所有运行中的程序。</summary>
    private void OnCloseCategoryRequested(ProcessCategory category)
    {
        // 先统计当前运行中的数量
        var statuses = _monitor.CheckAll();
        int runningInCat = statuses.Count(s => s.Category == category && s.IsRunning);

        if (runningInCat == 0)
        {
            MessageBox.Show(this,
                $"{category.DisplayName()}当前没有正在运行的程序。",
                "一键关闭",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var confirm = MessageBox.Show(this,
            $"确定要关闭「{category.DisplayName()}」下正在运行的 {runningInCat} 个程序吗？\n\n" +
            "程序将被强制结束，未保存的数据可能丢失。",
            "确认关闭",
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);

        if (confirm != DialogResult.Yes)
            return;

        Cursor = Cursors.WaitCursor;
        KillResult result;
        try
        {
            result = _monitor.KillCategory(category);
        }
        finally
        {
            Cursor = Cursors.Default;
        }

        RefreshStatus();

        if (result.HasFailures)
        {
            MessageBox.Show(this,
                $"已关闭 {result.Killed} 个程序。\n以下程序关闭失败（可能需要管理员权限）：\n\n" +
                string.Join("\n", result.Failed),
                "一键关闭",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

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

        UpdateEngineInfo();
    }

    /// <summary>检测并展示 Engine 版本与 Server 归属校验结果。</summary>
    private void UpdateEngineInfo()
    {
        EngineInfo info;
        try { info = EngineLocator.Detect(); }
        catch
        {
            _engineLabel.Text = "Engine 检测失败";
            _engineLabel.ForeColor = Theme.SubtleText;
            _engineTip.SetToolTip(_engineLabel, string.Empty);
            return;
        }

        if (!info.EngineRunning)
        {
            _engineLabel.Text = "Engine 未运行，无法检测版本";
            _engineLabel.ForeColor = Theme.SubtleText;
            _engineTip.SetToolTip(_engineLabel, string.Empty);
            return;
        }

        // Engine 运行中：版本部分
        string versionPart = info.VersionReadFailed || string.IsNullOrEmpty(info.Version)
            ? "Engine 版本读取失败"
            : $"Engine 版本 {info.Version}";

        // Server 版本一致性检查部分
        string serverPart = info.ServerOwnership switch
        {
            ServerOwnership.Valid => "Server 版本一致性检查 ✓",
            ServerOwnership.Mismatch => "Server 版本一致性检查 ✗ 异常（来自其他目录）",
            ServerOwnership.NotRunning => "Server 未运行",
            _ => "Server 版本一致性检查未知",
        };

        _engineLabel.Text = $"{versionPart}    ·    {serverPart}";

        // 颜色：归属异常或版本失败时偏红/灰，正常时绿
        _engineLabel.ForeColor = info.ServerOwnership switch
        {
            ServerOwnership.Valid when !info.VersionReadFailed => Theme.Running,
            ServerOwnership.Mismatch => Theme.Danger,
            _ => Theme.SubtleText,
        };

        // 异常时用 tooltip 展示实际路径
        if (info.ServerOwnership == ServerOwnership.Mismatch && !string.IsNullOrEmpty(info.ServerActualPath))
            _engineTip.SetToolTip(_engineLabel, $"运行中的 RMServer 实际路径：\n{info.ServerActualPath}");
        else if (!string.IsNullOrEmpty(info.EngineDir))
            _engineTip.SetToolTip(_engineLabel, $"Engine 目录：\n{info.EngineDir}");
        else
            _engineTip.SetToolTip(_engineLabel, string.Empty);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer?.Stop();
            _timer?.Dispose();
            _engineTip?.Dispose();
        }
        base.Dispose(disposing);
    }
}
