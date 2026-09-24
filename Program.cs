namespace MinimalBrowser;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm());
    }
}

/// <summary>
/// 程序图标。exe 的资源图标由 ApplicationIcon 提供，这里再从清单资源读一份给窗口标题栏用。
/// </summary>
internal static class AppIcon
{
    public static Icon? Value { get; } = Load();

    private static Icon? Load()
    {
        try
        {
            using var stream = typeof(AppIcon).Assembly
                .GetManifestResourceStream("MinimalBrowser.app.ico");
            return stream is null ? null : new Icon(stream);
        }
        catch
        {
            return null;
        }
    }
}
