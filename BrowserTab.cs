using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace MinimalBrowser;

/// <summary>
/// 一个标签页：内含一个 WebView2 控件，并把它的事件收敛成几个简单事件对外暴露。
/// </summary>
public sealed class BrowserTab : IDisposable
{
    private const int MaxTabTitleLength = 18;

    public TabPage Page { get; }
    public WebView2 View { get; }

    public string CurrentUrl { get; private set; } = "";
    public string CurrentTitle { get; private set; } = "新标签页";
    public bool IsInitialized { get; private set; }

    /// <summary>是否正在加载页面，用于控制「停止」按钮的可用状态。</summary>
    public bool IsLoading { get; private set; }

    /// <summary>地址、标题或前进后退状态发生变化时触发。</summary>
    public event EventHandler? Updated;

    /// <summary>页面请求打开新窗口（target=_blank / window.open）时触发，参数为目标地址。</summary>
    public event EventHandler<string>? NewWindowRequested;

    /// <summary>页面加载成功时触发，参数为最终地址。</summary>
    public event EventHandler<string>? NavigationFinished;

    public BrowserTab()
    {
        View = new WebView2 { Dock = DockStyle.Fill };
        Page = new TabPage("新标签页");
        Page.Controls.Add(View);
    }

    /// <summary>
    /// 初始化 WebView2。<paramref name="privateMode"/> 为 true 时以 InPrivate 方式创建控制器，
    /// Cookie、缓存等浏览数据只存在内存里，不写入 user data 目录。
    /// </summary>
    public async Task InitializeAsync(CoreWebView2Environment environment, string startUrl, bool privateMode = false)
    {
        if (privateMode)
        {
            // InPrivate 是 per-controller 选项，因此可以和普通窗口共用同一个 user data 目录，
            // 不需要为无痕模式单独准备一份环境。
            var options = environment.CreateCoreWebView2ControllerOptions();
            options.IsInPrivateModeEnabled = true;
            await View.EnsureCoreWebView2Async(environment, options);
        }
        else
        {
            await View.EnsureCoreWebView2Async(environment);
        }

        var core = View.CoreWebView2;
        core.Settings.IsStatusBarEnabled = true;
        core.Settings.AreDevToolsEnabled = true;
        core.Settings.AreBrowserAcceleratorKeysEnabled = true;
        core.Settings.IsZoomControlEnabled = true;
        core.Settings.AreDefaultScriptDialogsEnabled = true;

        core.SourceChanged += (_, _) =>
        {
            if (!IsInitialized) return;
            CurrentUrl = core.Source;
            Updated?.Invoke(this, EventArgs.Empty);
        };

        core.NavigationStarting += (_, e) =>
        {
            if (!IsInitialized) return;
            CurrentUrl = e.Uri;
            IsLoading = true;
            Updated?.Invoke(this, EventArgs.Empty);
        };

        core.NavigationCompleted += (_, e) =>
        {
            if (!IsInitialized) return;
            IsLoading = false;
            if (e.IsSuccess) NavigationFinished?.Invoke(this, core.Source);
            Updated?.Invoke(this, EventArgs.Empty);
        };

        core.DocumentTitleChanged += (_, _) =>
        {
            if (IsInitialized) SetTitle(core.DocumentTitle);
        };

        core.HistoryChanged += (_, _) => Updated?.Invoke(this, EventArgs.Empty);

        core.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            NewWindowRequested?.Invoke(this, e.Uri ?? "");
        };

        IsInitialized = true;

        if (!string.IsNullOrWhiteSpace(startUrl))
            core.Navigate(startUrl);
    }

    public bool CanGoBack => IsInitialized && View.CoreWebView2.CanGoBack;
    public bool CanGoForward => IsInitialized && View.CoreWebView2.CanGoForward;

    public void Navigate(string url)
    {
        if (IsInitialized) View.CoreWebView2.Navigate(url);
    }

    public void GoBack()
    {
        if (CanGoBack) View.CoreWebView2.GoBack();
    }

    public void GoForward()
    {
        if (CanGoForward) View.CoreWebView2.GoForward();
    }

    public void Reload()
    {
        if (IsInitialized) View.CoreWebView2.Reload();
    }

    public void Stop()
    {
        if (IsInitialized && IsLoading) View.CoreWebView2.Stop();
    }

    private void SetTitle(string? title)
    {
        var text = string.IsNullOrWhiteSpace(title) ? "新标签页" : title.Trim();
        CurrentTitle = text;
        Page.Text = text.Length <= MaxTabTitleLength ? text : text[..(MaxTabTitleLength - 1)] + "…";
        Page.ToolTipText = text;
        Updated?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        IsInitialized = false;
        try
        {
            View.Dispose();
        }
        catch
        {
            // 关闭过程中 WebView2 可能已经释放
        }
    }
}
