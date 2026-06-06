namespace RMQuickTune;

static class Program
{
    /// <summary>
    ///  应用程序主入口。
    /// </summary>
    [STAThread]
    static void Main()
    {
        // 启用高 DPI、默认字体等配置
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
