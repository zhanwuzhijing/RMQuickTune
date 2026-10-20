using RMGuide.Core;

namespace RMGuide.Controls;

/// <summary>
/// 「识别到的网卡要设成哪个角色」选择窗。
/// 默认不预选，必须由用户明确选择后才允许确定。
/// </summary>
public sealed class RolePickDialog : Form
{
    private readonly List<(string Key, RadioButton Radio)> _options = new();
    private readonly Button _okButton;

    /// <summary>用户选择的角色 Key；未选择时为 null。</summary>
    public string? SelectedRole { get; private set; }

    public RolePickDialog(string adapterName, string mac, string description,
        IReadOnlyList<(string Key, string Text)> options)
    {
        Text = "选择用途";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(452, 108 + options.Count * 34 + 56);
        BackColor = Color.White;
        Font = new Font(Theme.FontFamily, 9.5F);

        var title = new Label
        {
            Text = $"刚断开的是：{adapterName}",
            Font = new Font(Theme.FontFamily, 11F, FontStyle.Bold),
            ForeColor = Theme.TitleText,
            AutoSize = true,
            Location = new Point(20, 16),
        };

        var detail = new Label
        {
            Text = (mac.Length > 0 ? $"MAC：{mac}\n" : "") + description,
            Font = new Font(Theme.FontFamily, 9F),
            ForeColor = Theme.SubtleText,
            AutoSize = false,
            Size = new Size(412, 44),
            Location = new Point(20, 44),
        };

        var ask = new Label
        {
            Text = "它应该作为哪一块网卡使用？",
            Font = new Font(Theme.FontFamily, 9.5F),
            ForeColor = Theme.TitleText,
            AutoSize = true,
            Location = new Point(20, 92),
        };

        Controls.Add(title);
        Controls.Add(detail);
        Controls.Add(ask);

        int y = 118;
        foreach (var (key, text) in options)
        {
            var radio = new RadioButton
            {
                Text = text,
                Tag = key,
                AutoSize = true,
                Location = new Point(30, y),
                Font = new Font(Theme.FontFamily, 9.5F),
            };
            radio.CheckedChanged += (_, _) => UpdateOk();
            _options.Add((key, radio));
            Controls.Add(radio);
            y += 34;
        }

        var ok = new Button
        {
            Text = "确定",
            Size = new Size(88, 30),
            Location = new Point(ClientSize.Width - 196, ClientSize.Height - 44),
            DialogResult = DialogResult.None,
            Enabled = false,
        };
        ok.Click += (_, _) =>
        {
            var chosen = _options.FirstOrDefault(o => o.Radio.Checked);
            if (chosen.Radio is null) return;
            SelectedRole = chosen.Key;
            DialogResult = DialogResult.OK;
            Close();
        };

        var cancel = new Button
        {
            Text = "取消",
            Size = new Size(88, 30),
            Location = new Point(ClientSize.Width - 100, ClientSize.Height - 44),
            DialogResult = DialogResult.Cancel,
        };

        _okButton = ok;
        Controls.Add(ok);
        Controls.Add(cancel);

        CancelButton = cancel;
        AcceptButton = null;
    }

    private void UpdateOk()
        => _okButton.Enabled = _options.Any(o => o.Radio.Checked);
}
