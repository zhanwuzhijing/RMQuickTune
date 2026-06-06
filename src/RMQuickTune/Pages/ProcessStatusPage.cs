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

    private readonly CloudVersionChecker _cloudChecker = new();

    private readonly Panel _header;
    private readonly Label _titleLabel;
    private readonly Label _summaryLabel;
    private readonly Label _engineLabel;
    private readonly Label _cloudLabel;
    private readonly ToolTip _engineTip = new();
    private readonly ToolTip _cloudTip = new();
    private readonly RoundButton _refreshBtn;

    // 最近一次本地检测结果，供云端对比复用，避免重复调用 Detect()
    private EngineInfo? _lastEngineInfo;
    private bool _cloudFetching;

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
            Height = 136,
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

        _cloudLabel = new Label
        {
            Text = "云端版本校验：等待检测…",
            Font = Theme.PageSubtitle,
            ForeColor = Theme.SubtleText,
            AutoSize = true,
            Location = new Point(30, 102),
        };

        _refreshBtn = new RoundButton("立即刷新")
        {
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
        };
        _refreshBtn.Click += (_, _) =>
        {
            RefreshStatus();
            _ = RefreshCloudAsync(); // 手动刷新时重新请求云端
        };

        _header.Controls.Add(_titleLabel);
        _header.Controls.Add(_summaryLabel);
        _header.Controls.Add(_engineLabel);
        _header.Controls.Add(_cloudLabel);
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
        // 先把磁盘缓存加载进内存，这样首次刷新就能直接用缓存做比较
        _cloudChecker.LoadCacheToMemory();

        RefreshStatus();   // 内部会用 _cloudChecker.Current 渲染云端校验
        _timer.Start();

        // 打开软件自动从云端拉一次最新；拉到后，定时刷新会自动用上（无需手动点刷新）
        _ = RefreshCloudAsync();
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

        // 用内存中已有的云端数据（缓存或上次拉取）持续渲染比较结果。
        // 始终在 UI 线程执行（定时器/手动刷新均如此），不依赖异步拉取的线程时序。
        UpdateCloudInfo(_cloudChecker.Current);
    }

    /// <summary>检测并展示 Engine 版本与 Server 归属校验结果。</summary>
    private void UpdateEngineInfo()
    {
        EngineInfo info;
        try { info = EngineLocator.Detect(); }
        catch
        {
            _lastEngineInfo = null;
            _engineLabel.Text = "Engine 检测失败";
            _engineLabel.ForeColor = Theme.SubtleText;
            _engineTip.SetToolTip(_engineLabel, string.Empty);
            return;
        }

        _lastEngineInfo = info;

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

    /// <summary>异步从云端拉取最新版本数据。拉取成功后写入内存/缓存；
    /// 界面渲染由定时刷新（RefreshStatus -> UpdateCloudInfo）统一负责，
    /// 因此这里只需触发一次即时刷新，不直接更新控件，避免线程时序问题。</summary>
    private async Task RefreshCloudAsync()
    {
        if (_cloudFetching) return;
        _cloudFetching = true;
        try
        {
            // 无任何数据时提示“获取中”（有缓存则继续显示缓存比较结果）
            if (_cloudChecker.Current is null)
            {
                _cloudLabel.Text = "云端版本校验：正在获取云端数据…";
                _cloudLabel.ForeColor = Theme.SubtleText;
            }

            await _cloudChecker.RefreshAsync().ConfigureAwait(true);
        }
        catch
        {
            // 忽略：失败时 _cloudChecker.Current 仍是上次/缓存数据
        }
        finally
        {
            _cloudFetching = false;
        }

        // 拉取结束后即时刷新一次界面（marshal 回 UI 线程）
        try
        {
            if (IsHandleCreated && !IsDisposed)
                BeginInvoke(new Action(() => UpdateCloudInfo(_cloudChecker.Current)));
        }
        catch { /* 句柄未就绪/已销毁，定时刷新会兜底 */ }
    }

    /// <summary>根据云端数据与本地 engine 信息，渲染云端校验结果与最后更新时间。</summary>
    private void UpdateCloudInfo(CloudVersionData? cloud)
    {
        if (IsDisposed || Disposing) return;

        // 异步 await 之后的续体可能不在 UI 线程上（尤其首次在窗体构造期触发时），
        // 这里统一 marshal 回 UI 线程，避免更新被静默丢弃导致“要点一次刷新才显示”。
        if (InvokeRequired)
        {
            try { BeginInvoke(new Action(() => UpdateCloudInfo(cloud))); }
            catch { /* 句柄未就绪/已销毁，忽略 */ }
            return;
        }

        var engine = _lastEngineInfo;

        if (engine is null || !engine.EngineRunning)
        {
            _cloudLabel.Text = "云端版本校验：Engine 未运行，无法对比";
            _cloudLabel.ForeColor = Theme.SubtleText;
            _cloudTip.SetToolTip(_cloudLabel, string.Empty);
            return;
        }

        if (cloud is null)
        {
            _cloudLabel.Text = "云端版本校验：无法获取云端数据（请检查网络）";
            _cloudLabel.ForeColor = Theme.Danger;
            _cloudTip.SetToolTip(_cloudLabel, string.Empty);
            return;
        }

        var audit = CloudVersionAudit.Build(engine, cloud);

        // 时间标记
        string timeTag = $"更新于 {cloud.FetchedAt:MM-dd HH:mm}" + (cloud.FromCache ? "（缓存）" : "");

        if (audit.Items.Count == 0)
        {
            string ev = audit.EventType is null ? "未能识别赛事类型" : audit.EventType;
            _cloudLabel.Text = $"云端版本校验：{ev}，无可对比项    ·    {timeTag}";
            _cloudLabel.ForeColor = Theme.SubtleText;
            _cloudTip.SetToolTip(_cloudLabel, BuildCloudTooltip(audit));
            return;
        }

        int newer = audit.CloudNewerCount;   // 云端高于本地 → 报错
        int older = audit.CloudOlderCount;   // 云端低于本地 → 警告
        int match = audit.MatchCount;
        int comparable = audit.ComparableCount;

        string evName = audit.EventType ?? "未知赛事";
        string summary;
        Color color;
        switch (audit.Severity)
        {
            case AuditSeverity.Error:
                summary = $"云端版本校验[{evName}]：✗ {newer} 项需更新（云端更高）"
                    + (older > 0 ? $"，{older} 项云端偏旧" : "");
                color = Theme.Danger;
                break;
            case AuditSeverity.Warning:
                summary = $"云端版本校验[{evName}]：⚠ {older} 项云端版本偏低（可能云端未更新）";
                color = Theme.Warning;
                break;
            default: // Ok
                if (comparable == 0)
                {
                    summary = $"云端版本校验[{evName}]：暂无可对比项";
                    color = Theme.SubtleText;
                }
                else
                {
                    summary = $"云端版本校验[{evName}]：✓ 全部一致（{match}/{comparable} 项）";
                    color = Theme.Running;
                }
                break;
        }

        _cloudLabel.Text = $"{summary}    ·    {timeTag}";
        _cloudLabel.ForeColor = color;
        _cloudTip.SetToolTip(_cloudLabel, BuildCloudTooltip(audit));
    }

    private static string BuildCloudTooltip(CloudAuditResult audit)
    {
        var lines = new List<string>();
        if (audit.EventType is not null)
            lines.Add($"赛事类型：{audit.EventType}");
        foreach (var i in audit.Items)
        {
            string mark = i.Result switch
            {
                CompareResult.Match => "✓ 一致",
                CompareResult.CloudNewer => "✗ 需更新（云端更高）",
                CompareResult.CloudOlder => "⚠ 云端偏旧",
                _ => "— 未对比",
            };
            string local = string.IsNullOrEmpty(i.LocalVersion) ? "（未运行/无）" : i.LocalVersion;
            string cloud = string.IsNullOrEmpty(i.CloudVersion) ? "（无）" : i.CloudVersion;
            lines.Add($"{i.Label}：本地 {local} | 云端 {cloud}  {mark}");
        }
        if (audit.CloudFetchedAt is not null)
            lines.Add($"云端数据时间：{audit.CloudFetchedAt:yyyy-MM-dd HH:mm:ss}" + (audit.CloudFromCache ? "（缓存）" : ""));
        return string.Join("\n", lines);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer?.Stop();
            _timer?.Dispose();
            _engineTip?.Dispose();
            _cloudTip?.Dispose();
        }
        base.Dispose(disposing);
    }
}
