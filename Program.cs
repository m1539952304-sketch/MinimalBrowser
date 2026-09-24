namespace MinimalBrowser;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        // 所有窗口共用一个 store，这样无痕窗口和普通窗口看到同一份收藏夹，
        // 也不会出现两个窗口各自持有一份内存副本、互相覆盖对方改动的问题。
        var store = new BrowserStore();
        Application.Run(new MainForm(store));
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
