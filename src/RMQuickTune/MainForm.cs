using RMQuickTune.Core;
using RMQuickTune.Pages;

namespace RMQuickTune;

/// <summary>
/// 主窗体：左侧为可扩展的功能侧边栏，右侧为内容区。
/// 新增功能页时，在 <see cref="RegisterPages"/> 中添加一行即可。
/// </summary>
public sealed class MainForm : Form
{
    private readonly Panel _sidebar;
    private readonly Panel _content;
    private readonly Label _brand;

    private readonly List<(Button Button, PageBase Page)> _pages = new();
    private PageBase? _currentPage;

    private static readonly Color SidebarColor = Color.FromArgb(33, 37, 43);
    private static readonly Color SidebarHover = Color.FromArgb(55, 60, 68);
    private static readonly Color SidebarActive = Color.FromArgb(0, 122, 204);
    private static readonly Color SidebarText = Color.FromArgb(220, 220, 220);

    private const int SidebarWidth = 180;

    public MainForm()
    {
        Text = "RMQuickTune - RoboMaster 配置检查工具";
        MinimumSize = new Size(820, 520);
        ClientSize = new Size(960, 600);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Microsoft YaHei UI", 9F);

        // 右侧内容区（先加入，使其填充剩余空间）
        _content = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
        };

        // 左侧侧边栏
        _sidebar = new Panel
        {
            Dock = DockStyle.Left,
            Width = SidebarWidth,
            BackColor = SidebarColor,
        };

        _brand = new Label
        {
            Text = "RMQuickTune",
            ForeColor = Color.White,
            Font = new Font("Microsoft YaHei UI", 13F, FontStyle.Bold),
            Dock = DockStyle.Top,
            Height = 56,
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.FromArgb(24, 27, 32),
        };
        _sidebar.Controls.Add(_brand);

        Controls.Add(_content);
        Controls.Add(_sidebar);

        RegisterPages();

        // 默认选中第一个页面
        if (_pages.Count > 0)
        {
            SwitchTo(_pages[0].Page, _pages[0].Button);
        }
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
        var btn = new Button
        {
            Text = "   " + page.DisplayName,
            Dock = DockStyle.Top,
            Height = 44,
            FlatStyle = FlatStyle.Flat,
            ForeColor = SidebarText,
            BackColor = SidebarColor,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Microsoft YaHei UI", 10.5F),
            Cursor = Cursors.Hand,
            Tag = page,
        };
        btn.FlatAppearance.BorderSize = 0;
        btn.FlatAppearance.MouseOverBackColor = SidebarHover;

        btn.Click += (_, _) => SwitchTo(page, btn);

        // 让按钮叠放顺序与注册顺序一致（Dock=Top 是逆序堆叠）
        _sidebar.Controls.Add(btn);
        btn.BringToFront();
        _brand.BringToFront();

        _pages.Add((btn, page));
    }

    private void SwitchTo(PageBase page, Button button)
    {
        if (ReferenceEquals(_currentPage, page))
            return;

        _currentPage?.OnDeactivated();

        // 更新侧边栏按钮高亮
        foreach (var (b, _) in _pages)
        {
            b.BackColor = SidebarColor;
            b.ForeColor = SidebarText;
            b.FlatAppearance.BorderSize = 0;
        }
        button.BackColor = SidebarActive;
        button.ForeColor = Color.White;

        // 切换内容区
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
            {
                page.Dispose();
            }
        }
        base.Dispose(disposing);
    }
}
