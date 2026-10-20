using RMGuide.Core;

namespace RMGuide.Controls;

/// <summary>
/// 倒计时提示窗：用于「请拔掉网线」这类需要用户操作后等待的场景。
/// 显示剩余秒数，到 0 自动关闭。
/// </summary>
public sealed class CountdownDialog : Form
{
    private readonly Label _titleLabel;
    private readonly Label _hintLabel;
    private readonly Label _countLabel;
    private readonly System.Windows.Forms.Timer _timer;
    private int _left;

    public CountdownDialog(string title, string hint, int seconds)
    {
        _left = seconds;

        Text = title;
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        TopMost = true;
        ClientSize = new Size(430, 158);
        BackColor = Color.White;
        Font = new Font(Theme.FontFamily, 9.5F);
        ControlBox = false;

        _titleLabel = new Label
        {
            Text = title,
            Font = new Font(Theme.FontFamily, 11.5F, FontStyle.Bold),
            ForeColor = Theme.TitleText,
            AutoSize = true,
            Location = new Point(20, 16),
        };

        _hintLabel = new Label
        {
            Text = hint,
            Font = new Font(Theme.FontFamily, 9.5F),
            ForeColor = Theme.SubtleText,
            AutoSize = false,
            Size = new Size(392, 56),
            Location = new Point(20, 48),
        };

        _countLabel = new Label
        {
            Text = $"剩余 {_left} 秒",
            Font = new Font(Theme.FontFamily, 10.5F, FontStyle.Bold),
            ForeColor = Theme.Accent,
            AutoSize = true,
            Location = new Point(20, 118),
        };

        Controls.Add(_titleLabel);
        Controls.Add(_hintLabel);
        Controls.Add(_countLabel);

        _timer = new System.Windows.Forms.Timer { Interval = 1000 };
        _timer.Tick += (_, _) =>
        {
            _left--;
            _countLabel.Text = _left > 0 ? $"剩余 {_left} 秒" : "正在比对…";
            if (_left <= 0)
            {
                _timer.Stop();
                Close();
            }
        };
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _timer.Start();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _timer.Dispose();
        base.Dispose(disposing);
    }
}
