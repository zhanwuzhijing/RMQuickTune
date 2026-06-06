using RMQuickTune.Core;

namespace RMQuickTune.Pages;

/// <summary>
/// 主界面：显示 RoboMaster 赛事引擎相关程序的运行状态。
/// 定时自动刷新，绿色=运行中，灰色=未运行。
/// </summary>
public sealed class ProcessStatusPage : PageBase
{
    private readonly ProcessMonitor _monitor = new();
    private readonly System.Windows.Forms.Timer _timer;
    private readonly ListView _list;
    private readonly Label _summary;
    private readonly Button _refreshBtn;

    private static readonly Color RunningColor = Color.FromArgb(46, 160, 67);
    private static readonly Color StoppedColor = Color.FromArgb(160, 160, 160);

    public override string DisplayName => "运行状态";

    public ProcessStatusPage()
    {
        Padding = new Padding(16);

        // 顶部标题
        var title = new Label
        {
            Text = "程序运行状态",
            Font = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(16, 12),
        };

        _summary = new Label
        {
            Text = "正在检测…",
            Font = new Font("Microsoft YaHei UI", 9F),
            ForeColor = Color.Gray,
            AutoSize = true,
            Location = new Point(18, 44),
        };

        _refreshBtn = new Button
        {
            Text = "立即刷新",
            Size = new Size(90, 30),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            FlatStyle = FlatStyle.System,
        };
        _refreshBtn.Click += (_, _) => Refresh(force: true);

        // 进程列表
        _list = new ListView
        {
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            HeaderStyle = ColumnHeaderStyle.Nonclickable,
            Location = new Point(16, 72),
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Microsoft YaHei UI", 9.5F),
            OwnerDraw = false,
            MultiSelect = false,
        };
        _list.Columns.Add("状态", 70, HorizontalAlignment.Center);
        _list.Columns.Add("程序", 320, HorizontalAlignment.Left);
        _list.Columns.Add("PID", 90, HorizontalAlignment.Center);
        _list.Columns.Add("实例数", 80, HorizontalAlignment.Center);

        // 预先为每个目标程序建立一行
        foreach (var target in _monitor.Targets)
        {
            var item = new ListViewItem(new[] { "●", target.ExeName, "-", "-" })
            {
                UseItemStyleForSubItems = false,
            };
            _list.Items.Add(item);
        }

        Controls.Add(_list);
        Controls.Add(title);
        Controls.Add(_summary);
        Controls.Add(_refreshBtn);

        Resize += (_, _) => LayoutControls();
        LayoutControls();

        _timer = new System.Windows.Forms.Timer { Interval = 2000 };
        _timer.Tick += (_, _) => Refresh(force: false);
    }

    private void LayoutControls()
    {
        _refreshBtn.Location = new Point(ClientSize.Width - _refreshBtn.Width - 16, 16);
        _list.Size = new Size(
            ClientSize.Width - 32,
            ClientSize.Height - _list.Top - 16);
    }

    public override void OnActivated()
    {
        Refresh(force: true);
        _timer.Start();
    }

    public override void OnDeactivated()
    {
        _timer.Stop();
    }

    private void Refresh(bool force)
    {
        var statuses = _monitor.CheckAll();
        int runningCount = 0;

        _list.BeginUpdate();
        try
        {
            for (int i = 0; i < statuses.Count && i < _list.Items.Count; i++)
            {
                var s = statuses[i];
                var item = _list.Items[i];

                if (s.IsRunning) runningCount++;

                // 状态圆点
                item.SubItems[0].Text = "●";
                item.SubItems[0].ForeColor = s.IsRunning ? RunningColor : StoppedColor;

                // 程序名
                item.SubItems[1].Text = s.ExeName;
                item.SubItems[1].ForeColor = s.IsRunning ? Color.Black : Color.Gray;

                // PID
                item.SubItems[2].Text = s.Pid?.ToString() ?? "-";
                item.SubItems[2].ForeColor = Color.DimGray;

                // 实例数
                item.SubItems[3].Text = s.InstanceCount > 0 ? s.InstanceCount.ToString() : "-";
                item.SubItems[3].ForeColor = Color.DimGray;
            }
        }
        finally
        {
            _list.EndUpdate();
        }

        _summary.Text = $"运行中 {runningCount} / {statuses.Count}    最后刷新 {DateTime.Now:HH:mm:ss}";
        _summary.ForeColor = runningCount == statuses.Count
            ? RunningColor
            : Color.Gray;
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
