namespace RMQuickTune.Core;

/// <summary>
/// 所有功能页面的抽象基类。
/// 新增功能时：继承此类，实现 <see cref="DisplayName"/>，
/// 然后在 <c>MainForm</c> 中注册即可出现在侧边栏。
/// </summary>
public abstract class PageBase : UserControl
{
    protected PageBase()
    {
        Dock = DockStyle.Fill;
        BackColor = Color.White;
    }

    /// <summary>侧边栏显示的页面名称。</summary>
    public abstract string DisplayName { get; }

    /// <summary>
    /// 页面被切换到（显示）时调用。
    /// 适合在这里启动定时刷新、加载数据等。
    /// </summary>
    public virtual void OnActivated() { }

    /// <summary>
    /// 页面被切走（隐藏）时调用。
    /// 适合在这里停止定时器、释放占用等。
    /// </summary>
    public virtual void OnDeactivated() { }
}
