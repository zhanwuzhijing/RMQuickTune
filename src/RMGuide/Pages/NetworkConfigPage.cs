using System.Drawing.Drawing2D;
using RMGuide.Controls;
using RMGuide.Core;

namespace RMGuide.Pages;

/// <summary>
/// 网络配置页：对应两份官方手册的两个大模块。
/// 左侧显示各网卡的真实状态，右侧给出「哪块网卡该配成什么」，可一键应用、存档、重置。
/// </summary>
public sealed class NetworkConfigPage : PageBase
{
    // ---- 状态 ----
    private NetModule _module = NetModule.Referee;
    private IReadOnlyList<AdapterInfo> _adapters = Array.Empty<AdapterInfo>();
    private IReadOnlyList<NetworkCheck> _probeChecks = Array.Empty<NetworkCheck>();
    private NetPlan? _plan;

    /// <summary>
    /// 「接赛事引擎局域网的那张网卡」。裁判端叫局域网网卡、选手端叫赛事引擎网卡，
    /// 物理上是同一张，因此共用这一个状态：改任何一边，另一边立刻跟着变。
    /// null = 使用自动检测结果。
    /// </summary>
    private string? _lanNic;

    /// <summary>第二角色（裁判端=外网网卡 / 选手端=自定义客户端网卡）。</summary>
    private string? _pickSecond;

    private bool _busy;
    private bool _suppressSelect;

    /// <summary>下拉显示文本 → 真实网卡名（虚拟网卡显示时带「（虚拟）」后缀）。</summary>
    private readonly Dictionary<string, string> _displayToName = new(StringComparer.Ordinal);

    /// <summary>检测轮次号：用于丢弃已过期的异步结果。</summary>
    private int _epoch;
    private CancellationTokenSource? _cts;
    private DateTime? _checkedAt;

    // ---- 控件 ----
    private readonly Panel _header;
    private readonly Label _title;
    private readonly Label _summary;
    private readonly Label _hint;
    private readonly RoundButton _tabReferee;
    private readonly RoundButton _tabPlayer;
    private readonly RoundButton _applyBtn;
    private readonly RoundButton _snapBtn;
    private readonly RoundButton _identifyBtn;

    private readonly ComboBox _firstPick;
    private readonly ComboBox _secondPick;
    private readonly Label _firstPickLabel;
    private readonly Label _secondPickLabel;

    private readonly ListColumn _adapterColumn;
    private readonly ListColumn _planColumn;

    private static readonly Color RoleSoft = Color.FromArgb(226, 242, 255);

    /// <summary>下拉里的「不使用第二块网卡」哨兵值。</summary>
    private const string NoNic = "\u0000none";

    private static string NoNicText(NetModule module) => module == NetModule.Referee
        ? "不使用（方案 1：单网卡直连路由器）"
        : "不使用（同局域网直连）";

    public override string DisplayName => "网络配置";

    public NetworkConfigPage()
    {
        BackColor = Theme.ContentBg;

        // ---------- 顶部 ----------
        _header = new Panel { Dock = DockStyle.Top, Height = 168, BackColor = Theme.ContentBg };

        _title = new Label
        {
            Text = "网络配置",
            Font = Theme.PageTitle,
            ForeColor = Theme.TitleText,
            AutoSize = true,
            Location = new Point(28, 14),
        };

        _tabReferee = new RoundButton("裁判端 · 赛事引擎联网")
        {
            Size = new Size(196, 34),
            Location = new Point(30, 56),
            Font = new Font(Theme.FontFamily, 9F, FontStyle.Bold),
        };
        _tabReferee.Click += (_, _) => SwitchModule(NetModule.Referee);

        _tabPlayer = new RoundButton("选手端 · 自定义客户端")
        {
            Size = new Size(196, 34),
            Location = new Point(236, 56),
            Font = new Font(Theme.FontFamily, 9F, FontStyle.Bold),
        };
        _tabPlayer.Click += (_, _) => SwitchModule(NetModule.Player);

        _summary = new Label
        {
            Text = "正在检测网络…",
            Font = Theme.PageSubtitle,
            ForeColor = Theme.SubtleText,
            AutoSize = true,
            Location = new Point(30, 104),
        };

        _hint = new Label
        {
            Text = "应用前会自动存档，可一键恢复默认或回到存档点。",
            Font = Theme.PageSubtitle,
            ForeColor = Theme.SubtleText,
            AutoSize = true,
            Location = new Point(30, 130),
        };

        _applyBtn = new RoundButton("应用配置")
        {
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Size = new Size(112, 36),
            BaseColor = Theme.Accent,
            Enabled = false,
        };
        _applyBtn.Click += (_, _) => _ = ApplyAsync();

        _snapBtn = new RoundButton("存档 / 恢复")
        {
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Size = new Size(112, 36),
            BaseColor = Theme.Accent,
            Ghost = true,
        };
        _snapBtn.Click += (_, _) => ShowSnapshotMenu();

        _identifyBtn = new RoundButton("识别网卡")
        {
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Size = new Size(96, 36),
            BaseColor = Theme.Accent,
            Ghost = true,
        };
        _identifyBtn.Click += (_, _) => _ = IdentifyAdapterAsync();

        _header.Controls.Add(_title);
        _header.Controls.Add(_tabReferee);
        _header.Controls.Add(_tabPlayer);
        _header.Controls.Add(_summary);
        _header.Controls.Add(_hint);
        _header.Controls.Add(_applyBtn);
        _header.Controls.Add(_snapBtn);
        _header.Controls.Add(_identifyBtn);
        _header.Resize += (_, _) => LayoutHeaderButtons();

        // ---------- 右栏的网卡选择器 ----------
        var picker = new Panel { Dock = DockStyle.Fill, BackColor = Theme.CardBg, Height = 86 };

        _firstPickLabel = new Label
        {
            Text = "局域网网卡",
            Font = Theme.CardMeta,
            ForeColor = Theme.SubtleText,
            AutoSize = false,
            Size = new Size(206, 22),
            TextAlign = ContentAlignment.MiddleLeft,
            Location = new Point(20, 18),
        };
        _firstPick = CreateCombo(new Point(232, 14));
        _firstPick.SelectedIndexChanged += (_, _) => OnPickChanged(isFirst: true);

        _secondPickLabel = new Label
        {
            Text = "外网网卡",
            Font = Theme.CardMeta,
            ForeColor = Theme.SubtleText,
            AutoSize = false,
            Size = new Size(206, 22),
            TextAlign = ContentAlignment.MiddleLeft,
            Location = new Point(20, 54),
        };
        _secondPick = CreateCombo(new Point(232, 50));
        _secondPick.SelectedIndexChanged += (_, _) => OnPickChanged(isFirst: false);

        picker.Controls.Add(_firstPickLabel);
        picker.Controls.Add(_firstPick);
        picker.Controls.Add(_secondPickLabel);
        picker.Controls.Add(_secondPick);

        // ---------- 左右两栏 ----------
        var columns = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.ContentBg,
            Padding = new Padding(22, 0, 22, 16),
            ColumnCount = 2,
            RowCount = 1,
        };
        columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42f));
        columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58f));
        columns.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        _adapterColumn = new ListColumn("网卡状态", null, 0) { Margin = new Padding(0, 4, 8, 0) };
        _planColumn = new ListColumn("配置方案", picker, 86) { Margin = new Padding(8, 4, 0, 0) };

        columns.Controls.Add(_adapterColumn, 0, 0);
        columns.Controls.Add(_planColumn, 1, 0);

        Controls.Add(columns);
        Controls.Add(_header);

        LayoutHeaderButtons();
        UpdateTabs();
    }

    private ComboBox CreateCombo(Point location) => new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        FlatStyle = FlatStyle.Flat,
        Font = new Font(Theme.FontFamily, 9.5F),
        BackColor = Color.White,
        Size = new Size(260, 26),
        Location = location,
    };

    private void LayoutHeaderButtons()
    {
        int right = _header.ClientSize.Width - 28;
        _applyBtn.Location = new Point(right - _applyBtn.Width, 52);
        right -= _applyBtn.Width + 8;
        _snapBtn.Location = new Point(right - _snapBtn.Width, 52);
        right -= _snapBtn.Width + 8;
        _identifyBtn.Location = new Point(right - _identifyBtn.Width, 52);
    }

    // ================= 生命周期 =================

    public override void OnActivated()
    {
        if (_adapters.Count == 0 || _plan is null)
            _ = RefreshAsync();
    }

    public override void OnDeactivated()
    {
        try { _cts?.Cancel(); } catch { /* ignore */ }
    }

    private void SwitchModule(NetModule module)
    {
        if (_module == module) return;

        _module = module;
        // _lanNic 不重置：它就是两个模块共用的那张网卡
        _pickSecond = null;
        _probeChecks = Array.Empty<NetworkCheck>();

        // 注意：这里不清空 _adapters/_plan。
        // 清空后若上一轮异步探测刚好返回，会用空列表重渲染成「未检测到网卡」。
        UpdateTabs();
        _summary.Text = "正在切换模块…";
        _summary.ForeColor = Theme.SubtleText;
        _ = RefreshAsync();
    }

    private void UpdateTabs()
    {
        bool referee = _module == NetModule.Referee;
        _tabReferee.Ghost = !referee;
        _tabPlayer.Ghost = referee;
        _tabReferee.Invalidate();
        _tabPlayer.Invalidate();

        _firstPickLabel.Text = referee ? "局域网网卡" : "赛事引擎网卡（与裁判端联动）";
        _secondPickLabel.Text = referee ? "外网网卡" : "自定义客户端网卡";

        _firstPickLabel.Visible = true;
        _firstPick.Visible = true;
        _firstPickLabel.Location = new Point(20, 18);
        _firstPick.Location = new Point(232, 14);
        _secondPickLabel.Location = new Point(20, 54);
        _secondPick.Location = new Point(232, 50);
        _planColumn.SetExtraHeight(86);
    }

    private void OnPickChanged(bool isFirst)
    {
        if (_suppressSelect) return;

        var combo = isFirst ? _firstPick : _secondPick;
        string? display = combo.SelectedItem as string;

        string? value = display is not null && _displayToName.TryGetValue(display, out var nicName)
            ? nicName
            : null;

        if (!isFirst && display == NoNicText(_module)) value = NoNic;

        if (isFirst)
        {
            _lanNic = value;
        }
        else
        {
            _pickSecond = value;
        }

        RebuildPlan();
    }

    // ================= 检测 =================

    private async Task RefreshAsync()
    {
        // 用递增的轮次号判定「我是不是最新的一次检测」。
        // 快速切模块时会有多次 Refresh 并发，旧的那次必须丢弃，否则会用旧数据覆盖界面。
        int epoch = ++_epoch;
        _busy = true;
        _applyBtn.Enabled = false;
        _summary.Text = "正在检测网络…";
        _summary.ForeColor = Theme.SubtleText;

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        try
        {
            var adapters = NetworkInspector.GetAdapters();
            if (IsDisposed || epoch != _epoch) return;

            _adapters = adapters;
            _probeChecks = Array.Empty<NetworkCheck>();
            RebuildPlan();              // 先出结论，不等连通性探测
            _checkedAt = DateTime.Now;
            Render();

            var probes = await NetAdvisor.ProbeAsync(_module, ct);
            if (IsDisposed || ct.IsCancellationRequested || epoch != _epoch) return;

            _probeChecks = probes;
            Render();
        }
        catch (OperationCanceledException)
        {
            // 切页/切模块，忽略
        }
        catch (Exception ex)
        {
            if (epoch != _epoch) return;
            _summary.Text = "网络检测失败：" + ex.Message;
            _summary.ForeColor = Theme.Danger;
        }
        finally
        {
            if (epoch == _epoch)
            {
                _busy = false;
                if (!IsDisposed) _applyBtn.Enabled = _plan?.Targets.Count > 0;
            }
        }
    }

    private void RebuildPlan()
    {
        var (recFirst, recSecond) = NetAdvisor.Recommend(_module, _adapters);

        // 共用同一张「赛事引擎局域网网卡」：两个模块读写同一个 _lanNic
        string? first = _lanNic ?? recFirst;
        string? second = _pickSecond == NoNic
            ? null
            : _pickSecond ?? recSecond;

        // 同一张网卡不能既当赛事引擎网卡又当自定义客户端网卡
        if (second is not null && second == first) second = null;

        _plan = NetAdvisor.Build(_module, _adapters, first, second);

        _applyBtn.Enabled = _plan.Targets.Count > 0 && !_busy;
    }

    // ================= 渲染 =================

    private void Render()
    {
        if (IsDisposed) return;

        SyncPickers();
        RenderAdapters();
        RenderPlan();
        UpdateSummary();
    }

    private void SyncPickers()
    {
        _suppressSelect = true;
        try
        {
            var choices = BuildChoices();
            _displayToName.Clear();
            foreach (var c in choices) _displayToName[c.Display] = c.Name;

            var displays = choices.Select(c => c.Display).ToList();

            // 第一角色：裁判端局域网网卡 / 选手端赛事引擎网卡（同一个状态）
            FillCombo(_firstPick, displays, DisplayOf(_plan?.FirstName, displays), allowNone: false);

            // 第二角色不能选同一张网卡
            var secondDisplays = choices
                .Where(c => c.Name != _plan?.FirstName)
                .Select(c => c.Display)
                .ToList();
            FillCombo(_secondPick, secondDisplays, DisplayOf(_plan?.SecondName, secondDisplays), allowNone: true);
        }
        finally
        {
            _suppressSelect = false;
        }
    }

    /// <summary>下拉候选：物理网卡在前，虚拟网卡在后并标注，避免学生看不出为什么少了几块。</summary>
    private List<(string Display, string Name)> BuildChoices()
        => _adapters
            .OrderBy(a => a.IsVirtual ? 1 : 0)
            .ThenBy(a => a.IsWireless ? 1 : 0)
            .ThenBy(a => a.Name)
            .Select(a => (a.IsVirtual ? a.Name + "（虚拟）" : a.Name, a.Name))
            .ToList();

    private static string? DisplayOf(string? name, List<string> displays)
    {
        if (name is null) return null;
        return displays.FirstOrDefault(d => d == name || d == name + "（虚拟）");
    }

    private void FillCombo(ComboBox combo, List<string> names, string? current, bool allowNone)
    {
        combo.Items.Clear();
        if (allowNone) combo.Items.Add(NoNicText(_module));
        foreach (var n in names) combo.Items.Add(n);

        if (combo.Items.Count == 0) return;

        int index = 0;
        if (current is not null)
        {
            int i = combo.Items.IndexOf(current);
            if (i >= 0) index = i;
        }

        combo.SelectedIndex = Math.Min(index, combo.Items.Count - 1);
    }

    private void RenderAdapters()
    {
        _adapterColumn.List.SuspendLayout();
        ClearCards(_adapterColumn.List);

        // 全部列出（含虚拟网卡），虚拟的标注出来，避免学生以为网卡"消失了"
        var visible = _adapters.ToList();

        if (visible.Count == 0)
        {
            _adapterColumn.List.Controls.Add(BuildEmptyCard("未检测到任何网卡"));
            _adapterColumn.List.ResumeLayout();
            _adapterColumn.SetCount("0 块");
            _adapterColumn.ResizeCards();
            return;
        }

        foreach (var a in visible)
        {
            var card = new InfoCard { Margin = new Padding(0, 0, 0, 8) };

            var lines = new List<string>();
            string kind = a.IsWireless ? "无线" : "有线";
            string speed = a.SpeedMbps >= 1000 ? $"{a.SpeedMbps / 1000.0:0.#} Gbps"
                          : a.SpeedMbps > 0 ? $"{a.SpeedMbps} Mbps" : "";
            string mode = a.DhcpEnabled is bool d ? (d ? "自动获取" : "静态") : "";
            lines.Add(string.Join("  ·  ", new[] { kind, speed, mode }.Where(s => s.Length > 0)));

            if (a.MacAddress.Length > 0)
                lines.Add("MAC：" + a.MacAddress);
            if (a.Description.Length > 0)
                lines.Add("型号：" + a.Description);

            if (a.Addresses.Count == 0)
                lines.Add("IP：未获取到 IPv4 地址");
            else
                foreach (var addr in a.Addresses)
                    lines.Add($"IP：{addr.Ip}/{addr.PrefixLength}    掩码：{addr.Mask}");

            lines.Add(a.Gateways.Count > 0 ? "网关：" + string.Join("、", a.Gateways) : "网关：未配置");
            lines.Add(a.DnsServers.Count > 0 ? "DNS：" + string.Join("、", a.DnsServers) : "DNS：未配置");

            // 角色标签优先，其次状态
            string? role = a.Name == _plan?.FirstName ? _plan?.FirstLabel
                         : a.Name == _plan?.SecondName ? _plan?.SecondLabel
                         : null;

            Color badgeColor, badgeSoft, stripe;
            string badge;
            if (role is not null)
            {
                badge = role;
                badgeColor = Theme.Accent;
                badgeSoft = RoleSoft;
                stripe = Theme.Accent;
            }
            else if (a.IsVirtual)
            {
                badge = "虚拟";
                badgeColor = Theme.Stopped;
                badgeSoft = Theme.StoppedSoft;
                stripe = Theme.Stopped;
            }
            else
            {
                badge = a.StatusText;
                (badgeColor, badgeSoft, stripe) = a.StatusText switch
                {
                    "已连接" => (Theme.Running, Theme.RunningSoft, Theme.Running),
                    "未连接" => (Theme.Stopped, Theme.StoppedSoft, Theme.Stopped),
                    _ => (Theme.Warning, Theme.WarningSoft, Theme.Warning),
                };
            }

            card.SetContent(a.Name, badge, badgeColor, badgeSoft, stripe, lines);

            _adapterColumn.List.Controls.Add(card);
            card.AutoFit();
        }

        _adapterColumn.List.ResumeLayout();
        int virtualCount = visible.Count(a => a.IsVirtual);
        _adapterColumn.SetCount(virtualCount > 0
            ? $"{visible.Count - virtualCount} 块 + {virtualCount} 虚拟"
            : $"{visible.Count} 块");
        _adapterColumn.ResizeCards();
    }

    private void RenderPlan()
    {
        _planColumn.List.SuspendLayout();
        ClearCards(_planColumn.List);

        var plan = _plan;
        if (plan is null)
        {
            _planColumn.List.Controls.Add(BuildEmptyCard("正在生成配置方案…"));
            _planColumn.List.ResumeLayout();
            _planColumn.SetCount("");
            _planColumn.ResizeCards();
            return;
        }

        // 1) 目标配置卡
        foreach (var t in plan.Targets)
        {
            var adapter = _adapters.FirstOrDefault(a => a.Name == t.AdapterName);
            bool satisfied = IsSatisfied(t, adapter);

            var lines = new List<string> { "目标：" + t.TargetText };
            lines.Add("当前：" + (adapter is null ? "网卡不存在" : Describe(adapter)));

            var card = new InfoCard { Margin = new Padding(0, 0, 0, 8) };
            card.SetContent($"{t.RoleLabel} · {t.AdapterName}",
                satisfied ? "已符合" : "待修改",
                satisfied ? Theme.Running : Theme.Warning,
                satisfied ? Theme.RunningSoft : Theme.WarningSoft,
                satisfied ? Theme.Running : Theme.Warning,
                lines);
            _planColumn.List.Controls.Add(card);
            card.AutoFit();
        }

        // 2) 检查项卡
        foreach (var c in plan.Checks.Concat(_probeChecks))
        {
            var card = new InfoCard { Margin = new Padding(0, 0, 0, 8) };
            var (badgeColor, badgeSoft, stripe) = c.Result switch
            {
                NetResult.Ok => (Theme.Running, Theme.RunningSoft, Theme.Running),
                NetResult.Warn => (Theme.Warning, Theme.WarningSoft, Theme.Warning),
                NetResult.Error => (Theme.Danger, Theme.DangerSoft, Theme.Danger),
                _ => (Theme.Stopped, Theme.StoppedSoft, Theme.Stopped),
            };

            card.SetContent(c.Label, NetworkInspector.ResultText(c.Result),
                badgeColor, badgeSoft, stripe, new[] { c.Detail });
            _planColumn.List.Controls.Add(card);
            card.AutoFit();
        }

        _planColumn.List.ResumeLayout();
        _planColumn.SetCount($"{plan.Targets.Count} 项目标");
        _planColumn.ResizeCards();
    }

    private void UpdateSummary()
    {
        var plan = _plan;
        if (plan is null) return;

        var all = plan.Checks.Concat(_probeChecks).ToList();
        int ok = all.Count(c => c.Result == NetResult.Ok);
        int warn = all.Count(c => c.Result == NetResult.Warn);
        int err = all.Count(c => c.Result == NetResult.Error);

        string moduleName = _module == NetModule.Referee ? "裁判端 · 赛事引擎联网" : "选手端 · 自定义客户端";
        string time = _checkedAt is null ? "" : $"    ·    {_checkedAt:HH:mm:ss}";

        _summary.Text = $"{moduleName}    ·    检查 {all.Count} 项：{ok} 正常 / {warn} 注意 / {err} 异常{time}";
        _summary.ForeColor = err > 0 ? Theme.Danger : warn > 0 ? Theme.Warning : Theme.Running;

        _applyBtn.Enabled = plan.Targets.Count > 0 && !_busy;
    }

    private static bool IsSatisfied(NetTarget t, AdapterInfo? a)
    {
        if (a is null) return false;

        if (t.Dhcp)
            return a.DhcpEnabled != false && a.Addresses.Any(x => !x.IsApipa);

        bool ipOk = a.Addresses.Any(x => x.Ip == t.Ip && x.Mask == t.Mask);
        bool gwOk = t.Gateway is not null
            ? a.Gateways.Contains(t.Gateway)
            : a.Gateways.Count == 0;
        return ipOk && gwOk;
    }

    private static string Describe(AdapterInfo a)
    {
        string mode = a.DhcpEnabled is bool d ? (d ? "自动获取 " : "静态 ") : "";
        string ip = a.Addresses.Count == 0
            ? "无 IP"
            : string.Join("、", a.Addresses.Select(x => $"{x.Ip}/{x.PrefixLength}"));
        string gw = a.Gateways.Count > 0 ? $"，网关 {string.Join("、", a.Gateways)}" : "，无网关";
        return mode + ip + gw;
    }

    // ================= 应用 / 存档 / 恢复 =================

    private async Task ApplyAsync()
    {
        var plan = _plan;
        if (plan is null || plan.Targets.Count == 0) return;

        var cmds = NetConfigApplier.BuildApply(plan);
        if (cmds.Count == 0) return;

        string detail = string.Join("\n", plan.Targets.Select(t => $"  · {t.RoleLabel}（{t.AdapterName}）\n      → {t.TargetText}"));

        var confirm = MessageBox.Show(this,
            "即将修改以下网卡配置：\n\n" + detail +
            "\n\n修改前会自动保存一个存档点，随时可以恢复。\n" +
            "修改网络配置需要管理员权限，可能会弹出授权窗口。",
            "应用网络配置",
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);

        if (confirm != DialogResult.Yes) return;

        var snapshot = SnapshotStore.Save(_adapters, "应用前自动存档", true);
        string snapInfo = string.IsNullOrEmpty(snapshot.Path)
            ? "\n（警告：存档点保存失败）"
            : "";

        Cursor = Cursors.WaitCursor;
        _applyBtn.Enabled = false;
        NetApplyResult result;
        try
        {
            result = await Task.Run(() => NetConfigApplier.Run(cmds));
        }
        finally
        {
            Cursor = Cursors.Default;
        }

        await RefreshAsync();

        if (result.Canceled)
        {
            MessageBox.Show(this, "已取消管理员授权，配置未修改。" + snapInfo,
                "应用网络配置", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (result.AllOk)
        {
            MessageBox.Show(this, $"配置已应用（{result.Results.Count} 条命令）。{snapInfo}",
                "应用网络配置", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        else
        {
            var failed = result.Failed.Select(f => $"· {f.Args}\n  {f.Output}").ToList();
            MessageBox.Show(this,
                "部分配置未能应用：\n\n" + string.Join("\n", failed) +
                (result.Error is null ? "" : $"\n\n错误：{result.Error}") + snapInfo,
                "应用网络配置", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async Task ResetToDefaultAsync()
    {
        var names = _adapters.Where(a => !a.IsVirtual).Select(a => a.Name).ToList();
        if (names.Count == 0) return;

        var confirm = MessageBox.Show(this,
            "恢复默认设置：以下网卡全部改为「自动获取 IP + 自动 DNS + 自动跃点」。\n\n  · " +
            string.Join("\n  · ", names) +
            "\n\n如果原本是静态 IP，之后需要靠存档点恢复。继续吗？",
            "恢复默认设置",
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);

        if (confirm != DialogResult.Yes) return;

        SnapshotStore.Save(_adapters, "恢复默认前自动存档", true);
        var cmds = NetConfigApplier.BuildResetToDefault(names);

        Cursor = Cursors.WaitCursor;
        NetApplyResult result;
        try
        {
            result = await Task.Run(() => NetConfigApplier.Run(cmds));
        }
        finally
        {
            Cursor = Cursors.Default;
        }

        await RefreshAsync();

        if (result.Canceled)
        {
            MessageBox.Show(this, "已取消管理员授权，配置未修改。", "恢复默认设置",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        MessageBox.Show(this,
            result.AllOk ? "已恢复为自动获取。" : "部分网卡恢复失败：" + string.Join("、", result.Failed.Select(f => f.Args)),
            "恢复默认设置", MessageBoxButtons.OK,
            result.AllOk ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
    }

    private async Task RestoreSnapshotAsync(SnapshotFile snapshot)
    {
        var cmds = NetConfigApplier.BuildRestore(snapshot);
        if (cmds.Count == 0)
        {
            MessageBox.Show(this, "该存档点没有可恢复的网卡配置。", "恢复存档点",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        string list = string.Join("\n", snapshot.Adapters.Select(a =>
            $"  · {a.Name} → {(a.DhcpEnabled ? "自动获取" : $"{a.Ip} / {a.Mask}")}"));

        var confirm = MessageBox.Show(this,
            $"将恢复到存档点「{snapshot.SavedAt:yyyy-MM-dd HH:mm}」：\n\n{list}\n\n继续吗？",
            "恢复存档点",
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);

        if (confirm != DialogResult.Yes) return;

        SnapshotStore.Save(_adapters, "恢复前自动存档", true);

        Cursor = Cursors.WaitCursor;
        NetApplyResult result;
        try
        {
            result = await Task.Run(() => NetConfigApplier.Run(cmds));
        }
        finally
        {
            Cursor = Cursors.Default;
        }

        await RefreshAsync();

        if (result.Canceled)
        {
            MessageBox.Show(this, "已取消管理员授权，配置未修改。", "恢复存档点",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        MessageBox.Show(this,
            result.AllOk ? "已恢复到存档点。" : "部分网卡恢复失败：" + string.Join("、", result.Failed.Select(f => f.Args)),
            "恢复存档点", MessageBoxButtons.OK,
            result.AllOk ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
    }

    private async Task DisableFirewallAsync()
    {
        var confirm = MessageBox.Show(this,
            "将关闭 Windows Defender 防火墙（域/专用/公用三个配置文件）。\n\n" +
            "赛事环境需要关闭防火墙，但会降低本机安全性。之后可在系统设置里重新开启。\n\n确定关闭吗？",
            "关闭防火墙",
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);

        if (confirm != DialogResult.Yes) return;

        Cursor = Cursors.WaitCursor;
        NetApplyResult result;
        try
        {
            result = await Task.Run(() => NetConfigApplier.Run(new[] { NetConfigApplier.BuildFirewallOff() }));
        }
        finally
        {
            Cursor = Cursors.Default;
        }

        await RefreshAsync();

        MessageBox.Show(this,
            result.AllOk ? "防火墙已关闭。" : "关闭失败，可能需要管理员权限。",
            "关闭防火墙", MessageBoxButtons.OK,
            result.AllOk ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
    }

    private void ShowSnapshotMenu()
    {
        var menu = new ContextMenuStrip { Font = new Font(Theme.FontFamily, 9.5F) };

        var save = new ToolStripMenuItem("保存当前配置为存档点");
        save.Click += (_, _) => SaveSnapshotManually();
        menu.Items.Add(save);

        menu.Items.Add(new ToolStripSeparator());

        var reset = new ToolStripMenuItem("恢复默认设置（全部自动获取）");
        reset.Click += (_, _) => _ = ResetToDefaultAsync();
        menu.Items.Add(reset);

        var restore = new ToolStripMenuItem("恢复存档点");
        var snapshots = SnapshotStore.List();
        if (snapshots.Count == 0)
        {
            restore.Enabled = false;
            restore.ToolTipText = "还没有存档点";
        }
        else
        {
            foreach (var s in snapshots.Take(12))
            {
                var captured = s;
                var item = new ToolStripMenuItem(s.DisplayName) { Tag = s };
                item.Click += (_, _) => _ = RestoreSnapshotAsync(captured);
                restore.DropDownItems.Add(item);
            }
        }
        menu.Items.Add(restore);

        menu.Items.Add(new ToolStripSeparator());

        var firewall = new ToolStripMenuItem("关闭 Windows 防火墙");
        firewall.Click += (_, _) => _ = DisableFirewallAsync();
        menu.Items.Add(firewall);

        var copy = new ToolStripMenuItem("复制当前诊断信息");
        copy.Click += (_, _) => CopyDiagnostics();
        menu.Items.Add(copy);

        menu.Show(_snapBtn, new Point(0, _snapBtn.Height + 4));
    }

    /// <summary>
    /// 帮学生把「系统里的网卡名」和「眼前这根网线」对上号：
    /// 倒计时 10 秒内断开目标网卡，程序比对状态变化指出是哪一块，
    /// 再让用户自己选择它要作为哪一种网卡。
    /// </summary>
    private async Task IdentifyAdapterAsync()
    {
        var go = MessageBox.Show(this,
            "接下来用 10 秒判断哪块网卡是哪块：\n\n" +
            "1. 点「确定」后开始倒计时\n" +
            "2. 倒计时期间拔掉要识别的那块网卡的网线（无线网卡请关闭该 WiFi）\n" +
            "3. 结束后会告诉你哪块网卡断了，并让你选择它的用途\n\n" +
            "准备好了就点「确定」。",
            "识别网卡", MessageBoxButtons.OKCancel, MessageBoxIcon.Information);

        if (go != DialogResult.OK) return;

        _identifyBtn.Enabled = false;

        var before = NetworkInspector.GetAdapters();

        // 倒计时窗自己跑 10 秒，结束后自动关闭
        using (var countdown = new CountdownDialog("识别网卡",
            "请现在拔掉要识别的那块网卡的网线；无线网卡请在系统里关闭该 WiFi。", 10))
        {
            countdown.ShowDialog(this);
        }

        var after = NetworkInspector.GetAdapters();
        _identifyBtn.Enabled = true;

        var changed = new List<AdapterInfo>();
        foreach (var b in before.Where(x => !x.IsVirtual))
        {
            var a = after.FirstOrDefault(x => x.Name == b.Name);
            if (IsChanged(b, a)) changed.Add(b);
        }

        if (changed.Count == 0)
        {
            MessageBox.Show(this,
                "没有检测到变化。可能拔错了网卡，或这块网卡的连接并没有断开。\n\n可以再试一次。",
                "识别网卡", MessageBoxButtons.OK, MessageBoxIcon.Information);
            await RefreshAsync();
            return;
        }

        if (changed.Count > 1)
        {
            MessageBox.Show(this,
                "检测到多块网卡发生变化：\n\n" + string.Join("\n", changed.Select(c => "  · " + c.Name))
                + "\n\n可能断开的不止一根，或无线网卡正在自动重连。",
                "识别网卡", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            await RefreshAsync();
            return;
        }

        // 不默认设置，交给用户选
        var target = changed[0];
        var options = _module == NetModule.Referee
            ? new List<(string, string)>
            {
                ("first", "设为「局域网网卡」"),
                ("second", "设为「外网网卡」"),
            }
            : new List<(string, string)>
            {
                ("first", "设为「赛事引擎网卡」"),
                ("second", "设为「自定义客户端网卡」"),
            };

        using (var pick = new RolePickDialog(target.Name, target.MacAddress,
            $"{(target.IsWireless ? "无线" : "有线")}网卡　{(target.IsUp ? "当前已连接" : "当前未连接")}", options))
        {
            if (pick.ShowDialog(this) == DialogResult.OK && pick.SelectedRole is { } role)
            {
                if (role == "first")
                {
                    _lanNic = target.Name;
                }
                else
                {
                    // 同一张网卡不能同时当第一角色
                    if (_lanNic == target.Name) _lanNic = null;
                    _pickSecond = target.Name;
                }
            }
        }

        await RefreshAsync();
    }

    /// <summary>断开 / 地址变化都算「变了」。</summary>
    private static bool IsChanged(AdapterInfo before, AdapterInfo? after)
    {
        if (after is null) return true;
        if (before.IsUp != after.IsUp) return true;

        var a = before.Addresses.Select(x => x.Ip).OrderBy(x => x, StringComparer.Ordinal).ToList();
        var b = after.Addresses.Select(x => x.Ip).OrderBy(x => x, StringComparer.Ordinal).ToList();
        return !a.SequenceEqual(b);
    }

    private void SaveSnapshotManually()
    {
        var snapshot = SnapshotStore.Save(_adapters, "手动存档", false);
        if (string.IsNullOrEmpty(snapshot.Path))
        {
            MessageBox.Show(this, "存档失败：没有写入权限或磁盘异常。", "存档点",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        MessageBox.Show(this,
            $"已保存 {snapshot.Adapters.Count} 块网卡的配置。\n\n位置：{snapshot.Path}",
            "存档点", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void CopyDiagnostics()
    {
        try
        {
            var lines = new List<string>
            {
                $"RMGuide 网络诊断  {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
                $"模块：{(_module == NetModule.Referee ? "裁判端 · 赛事引擎联网" : "选手端 · 自定义客户端")}",
                $"管理员权限：{(NetConfigApplier.IsAdmin ? "是" : "否")}",
                "",
                "== 网卡 ==",
            };

            foreach (var a in _adapters.Where(x => !x.IsVirtual))
            {
                lines.Add($"[{(a.IsWireless ? "无线" : "有线")}] {a.Name} — {a.StatusText}");
                if (a.Description.Length > 0) lines.Add($"  型号：{a.Description}");
                foreach (var addr in a.Addresses)
                    lines.Add($"  地址：{addr.Ip}/{addr.PrefixLength}  掩码 {addr.Mask}");
                if (a.Gateways.Count > 0) lines.Add($"  网关：{string.Join("、", a.Gateways)}");
                if (a.DnsServers.Count > 0) lines.Add($"  DNS ：{string.Join("、", a.DnsServers)}");
            }

            if (_plan is not null)
            {
                lines.Add("");
                lines.Add("== 配置方案 ==");
                lines.Add($"  {_plan.FirstLabel}：{_plan.FirstName ?? "未指定"}");
                lines.Add($"  {_plan.SecondLabel}：{_plan.SecondName ?? "未指定"}");
                foreach (var t in _plan.Targets) lines.Add($"  {t.RoleLabel} → {t.TargetText}");

                lines.Add("");
                lines.Add("== 检查项 ==");
                foreach (var c in _plan.Checks.Concat(_probeChecks))
                    lines.Add($"[{NetworkInspector.ResultText(c.Result)}] {c.Label}：{c.Detail}");
            }

            Clipboard.SetText(string.Join(Environment.NewLine, lines));
            _hint.Text = "诊断信息已复制到剪贴板。";
            _hint.ForeColor = Theme.Running;
        }
        catch (Exception ex)
        {
            _hint.Text = "复制失败：" + ex.Message;
            _hint.ForeColor = Theme.Danger;
        }
    }

    // ================= 小工具 =================

    private static void ClearCards(Control list)
    {
        var old = list.Controls.Cast<Control>().ToList();
        list.Controls.Clear();
        foreach (var c in old) c.Dispose();
    }

    private static InfoCard BuildEmptyCard(string text)
    {
        var card = new InfoCard { Height = 84, Margin = new Padding(0, 0, 0, 8) };
        card.SetContent("无数据", "", Theme.SubtleText, Theme.StoppedSoft, Theme.Stopped, new[] { text });
        return card;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            try { _cts?.Cancel(); } catch { /* ignore */ }
            _cts?.Dispose();
        }
        base.Dispose(disposing);
    }

    /// <summary>带标题、可选顶部扩展区与圆角边框的可滚动卡片列。</summary>
    private sealed class ListColumn : Panel
    {
        private readonly Label _titleLabel;
        private readonly Label _countLabel;
        private readonly Panel? _extraHost;

        public FlowLayoutPanel List { get; }

        public ListColumn(string title, Control? extra, int extraHeight)
        {
            BackColor = Theme.CardBg;
            Padding = new Padding(1);
            Dock = DockStyle.Fill;

            var headerBar = new Panel { Dock = DockStyle.Top, Height = 52, BackColor = Theme.CardBg };

            _titleLabel = new Label
            {
                Text = title,
                Font = new Font(Theme.FontFamily, 11F, FontStyle.Bold),
                ForeColor = Theme.TitleText,
                AutoSize = true,
                Location = new Point(20, 16),
            };

            _countLabel = new Label
            {
                Text = "",
                Font = Theme.CardMeta,
                ForeColor = Theme.SubtleText,
                AutoSize = false,
                Size = new Size(110, 20),
                TextAlign = ContentAlignment.MiddleRight,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
            };

            headerBar.Controls.Add(_titleLabel);
            headerBar.Controls.Add(_countLabel);
            headerBar.Resize += (_, _) =>
                _countLabel.Location = new Point(headerBar.Width - _countLabel.Width - 20, 18);

            var divider = new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Theme.CardBorder };

            List = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Theme.CardBg,
                Padding = new Padding(14, 12, 14, 14),
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
            };
            List.Resize += (_, _) => ResizeCards();

            // Dock 顺序：Fill 先加，Top 后加的在上方
            Controls.Add(List);

            if (extra is not null)
            {
                _extraHost = new Panel { Dock = DockStyle.Top, Height = extraHeight, BackColor = Theme.CardBg };
                _extraHost.Controls.Add(extra);
                Controls.Add(_extraHost);
            }

            Controls.Add(divider);
            Controls.Add(headerBar);
        }

        /// <summary>调整顶部扩展区高度（选手端隐藏一行后需要收缩）。</summary>
        public void SetExtraHeight(int height)
        {
            if (_extraHost is null || _extraHost.Height == height) return;
            _extraHost.Height = height;
        }

        public void SetCount(string text) => _countLabel.Text = text;

        public void ResizeCards()
        {
            int w = List.ClientSize.Width - List.Padding.Horizontal;
            if (w <= 0) return;
            int scrollbar = List.VerticalScroll.Visible ? SystemInformation.VerticalScrollBarWidth : 0;
            int target = Math.Max(60, w - scrollbar);

            foreach (Control c in List.Controls)
            {
                if (c.Width != target) c.Width = target;
                if (c is InfoCard card) card.AutoFit();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using var pen = new Pen(Theme.CardBorder, 1f);
            using var path = Theme.RoundedRect(rect, 10);
            e.Graphics.DrawPath(pen, path);
        }
    }
}
