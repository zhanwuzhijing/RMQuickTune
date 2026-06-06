namespace RMQuickTune.Core;

/// <summary>
/// 全局视觉主题：统一颜色、字体、圆角等，便于整体调整风格。
/// </summary>
public static class Theme
{
    // ---- 侧边栏 ----
    public static readonly Color SidebarBg = Color.FromArgb(24, 28, 35);
    public static readonly Color SidebarBrandBg = Color.FromArgb(18, 21, 27);
    public static readonly Color SidebarItemText = Color.FromArgb(170, 178, 189);
    public static readonly Color SidebarItemHover = Color.FromArgb(38, 44, 54);
    public static readonly Color SidebarItemActiveBg = Color.FromArgb(33, 39, 48);
    public static readonly Color SidebarItemActiveText = Color.White;
    public static readonly Color Accent = Color.FromArgb(0, 153, 255);

    // ---- 内容区 ----
    public static readonly Color ContentBg = Color.FromArgb(243, 245, 248);
    public static readonly Color CardBg = Color.White;
    public static readonly Color CardBorder = Color.FromArgb(228, 231, 236);
    public static readonly Color TitleText = Color.FromArgb(28, 33, 40);
    public static readonly Color SubtleText = Color.FromArgb(130, 138, 148);

    // ---- 状态色 ----
    public static readonly Color Running = Color.FromArgb(34, 197, 94);
    public static readonly Color RunningSoft = Color.FromArgb(223, 246, 231);
    public static readonly Color Stopped = Color.FromArgb(160, 168, 178);
    public static readonly Color StoppedSoft = Color.FromArgb(238, 240, 243);

    // ---- 字体 ----
    public const string FontFamily = "Microsoft YaHei UI";

    public static Font Brand { get; } = new(FontFamily, 14.5F, FontStyle.Bold);
    public static Font NavItem { get; } = new(FontFamily, 10.5F);
    public static Font PageTitle { get; } = new(FontFamily, 17F, FontStyle.Bold);
    public static Font PageSubtitle { get; } = new(FontFamily, 9.5F);
    public static Font CardTitle { get; } = new(FontFamily, 10.5F, FontStyle.Bold);
    public static Font CardMeta { get; } = new(FontFamily, 8.5F);
    public static Font Badge { get; } = new(FontFamily, 8.5F, FontStyle.Bold);

    /// <summary>创建一条圆角矩形路径。</summary>
    public static System.Drawing.Drawing2D.GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        var path = new System.Drawing.Drawing2D.GraphicsPath();
        int d = radius * 2;
        if (d <= 0)
        {
            path.AddRectangle(r);
            path.CloseFigure();
            return path;
        }
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
