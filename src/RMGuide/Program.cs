using RMGuide.Core;

namespace RMGuide;

static class Program
{
    /// <summary>
    ///  应用程序主入口。
    /// </summary>
    [STAThread]
    static int Main(string[] args)
    {
        // 提权 helper：以管理员身份只执行 netsh 命令，不打开界面
        if (args.Length >= 2 && args[0] == "--netsh-helper")
            return NetConfigApplier.RunHelper(args[1]);

        // 启用高 DPI、默认字体等配置
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
        return 0;
    }
}
