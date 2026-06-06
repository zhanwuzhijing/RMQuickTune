using RMQuickTune.Controls;
using RMQuickTune.Core;
using RMQuickTune.Pages;

namespace RMQuickTune;

/// <summary>
/// 主窗体：左侧可扩展功能侧边栏，右侧内容区。
/// 新增功能页时，在 <see cref="RegisterPages"/> 中添加一行即可。
/// </summary>
public sealed class MainForm : Form
{
    private readonly Panel _sidebar;
    private readonly Panel _content;

    private readonly List<(NavButton Button, PageBase Page)> _pages = new();
    private PageBase? _currentPage;

    private const int SidebarWidth = 196;

    public MainForm()
    {
        Text = "RMQuickTune - RoboMaster 配置检查工具";
        MinimumSize = new Size(840, 560);
        ClientSize = new Size(1000, 640);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font(Theme.FontFamily, 9F);
        BackColor = Theme.ContentBg;
        DoubleBuffered = true;

        // 右侧内容区（先加入，填充剩余空间）
        _content = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.ContentBg,
        };

        // 左侧侧边栏
        _sidebar = new Panel
        {
            Dock = DockStyle.Left,
            Width = SidebarWidth,
            BackColor = Theme.SidebarBg,
        };

        var brand = BuildBrandHeader();
        _sidebar.Controls.Add(brand);

        Controls.Add(_content);
        Controls.Add(_sidebar);

        RegisterPages();

        if (_pages.Count > 0)
            SwitchTo(_pages[0].Page, _pages[0].Button);
    }

    private Panel BuildBrandHeader()
    {
        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 72,
            BackColor = Theme.SidebarBrandBg,
        };

        var logo = new Label
        {
            Text = "RM",
            Font = new Font(Theme.FontFamily, 12F, FontStyle.Bold),
            ForeColor = Color.White,
            BackColor = Theme.Accent,
            Size = new Size(38, 38),
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(18, 17),
        };

        var title = new Label
        {
            Text = "QuickTune",
            Font = Theme.Brand,
            ForeColor = Color.White,
            AutoSize = true,
            Location = new Point(64, 18),
        };

        var subtitle = new Label
        {
            Text = "配置检查工具",
            Font = new Font(Theme.FontFamily, 8F),
            ForeColor = Theme.SidebarItemText,
            AutoSize = true,
            Location = new Point(66, 42),
        };

        header.Controls.Add(title);
        header.Controls.Add(subtitle);
        header.Controls.Add(logo);
        return header;
    }

    /// <summary>
    /// 注册功能页面。新增功能时在此处追加 <see cref="AddPage"/> 调用即可。
    /// </summary>
    private void RegisterPages()
    {
        AddPage(new ProcessStatusPage());
        // 后续功能页示例：
        // AddPage(new NetworkConfigPage());
        // AddPage(new DependencyCheckPage());
    }

    private void AddPage(PageBase page)
    {
        var btn = new NavButton(page.DisplayName) { Tag = page };
        btn.Click += (_, _) => SwitchTo(page, btn);

        // Dock=Top 逆序堆叠，加完置顶并把品牌头压回最上
        _sidebar.Controls.Add(btn);
        btn.BringToFront();
        if (_sidebar.Controls.Count > 0)
            _sidebar.Controls[0].BringToFront(); // brand header

        _pages.Add((btn, page));
    }

    private void SwitchTo(PageBase page, NavButton button)
    {
        if (ReferenceEquals(_currentPage, page))
            return;

        _currentPage?.OnDeactivated();

        foreach (var (b, _) in _pages)
            b.Active = false;
        button.Active = true;

        _content.SuspendLayout();
        _content.Controls.Clear();
        _content.Controls.Add(page);
        _content.ResumeLayout();

        _currentPage = page;
        page.OnActivated();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var (_, page) in _pages)
                page.Dispose();
        }
        base.Dispose(disposing);
    }
}
