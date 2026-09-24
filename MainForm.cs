using Microsoft.Web.WebView2.Core;

namespace MinimalBrowser;

public sealed class MainForm : Form
{
    private const string HomePage = "about:blank";
    private const string SearchPrefix = "https://www.bing.com/search?q=";

    private static readonly string DownloadsFolder = ResolveDownloadsFolder();

    private enum SideMode { Favorites, History }

    private sealed record SideItem(string Display, string Url, DateTime Time);

    private readonly BrowserStore _store = new();
    private readonly DownloadForm _downloadForm = new();
    private readonly ContextMenuStrip _tabMenu = new();
    private readonly ShortcutMessageFilter _shortcutFilter;

    private CoreWebView2Environment? _environment;
    private bool _suppressTabEvents;

    // ---------- 工具栏 ----------
    private readonly ToolStrip _toolbar = new()
    {
        Dock = DockStyle.Top,
        GripStyle = ToolStripGripStyle.Hidden,
        RenderMode = ToolStripRenderMode.System,
        Padding = new Padding(4, 2, 4, 2),
    };

    private readonly ToolStripButton _btnBack = TextButton("←", "后退 (Alt+←)");
    private readonly ToolStripButton _btnForward = TextButton("→", "前进 (Alt+→)");
    private readonly ToolStripButton _btnReload = TextButton("⟳", "刷新 (F5)");
    private readonly ToolStripButton _btnHome = TextButton("⌂", "主页");

    private readonly ToolStripTextBox _address = new()
    {
        AutoSize = false,
        Width = 400,
        BorderStyle = BorderStyle.FixedSingle,
        Margin = new Padding(6, 0, 6, 0),
    };

    private readonly ToolStripButton _btnBookmark = TextButton("☆", "收藏 / 取消收藏 (Ctrl+D)");
    private readonly ToolStripButton _btnFavorites = TextButton("收藏夹", "显示收藏夹 (Ctrl+B)");
    private readonly ToolStripButton _btnHistory = TextButton("历史", "显示历史记录 (Ctrl+H)");
    private readonly ToolStripButton _btnDownloads = TextButton("下载", "显示下载内容 (Ctrl+J)");
    private readonly ToolStripButton _btnNewTab = TextButton("＋", "新建标签页 (Ctrl+T)");

    // ---------- 标签页 ----------
    private readonly TabControl _tabs = new()
    {
        Dock = DockStyle.Fill,
        SizeMode = TabSizeMode.Fixed,
        ItemSize = new Size(168, 26),
        Multiline = false,
    };

    private readonly TabPage _newTabPage = new("＋") { ToolTipText = "新建标签页" };

    // ---------- 收藏夹 / 历史侧栏 ----------
    private readonly Panel _sidePanel = new()
    {
        Dock = DockStyle.Right,
        Width = 300,
        Visible = false,
        Padding = new Padding(8),
    };

    private readonly Label _sideTitle = new()
    {
        Dock = DockStyle.Top,
        Height = 28,
        TextAlign = ContentAlignment.MiddleLeft,
        Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold),
    };

    private readonly ListBox _sideList = new()
    {
        Dock = DockStyle.Fill,
        IntegralHeight = false,
        BorderStyle = BorderStyle.FixedSingle,
    };

    private readonly FlowLayoutPanel _sideButtons = new()
    {
        Dock = DockStyle.Bottom,
        Height = 38,
        FlowDirection = FlowDirection.LeftToRight,
        WrapContents = false,
    };

    private readonly Button _sideDelete = new() { Text = "删除选中", AutoSize = true };
    private readonly Button _sideClear = new() { Text = "清空历史", AutoSize = true };
    private readonly Button _sideClose = new() { Text = "关闭", AutoSize = true };

    private SideMode _sideMode = SideMode.Favorites;

    public MainForm()
    {
        Text = "MinimalBrowser";
        ClientSize = new Size(1200, 780);
        MinimumSize = new Size(680, 440);
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;
        Font = new Font("Microsoft YaHei UI", 9F);

        BuildToolbar();
        BuildSidePanel();
        _tabs.TabPages.Add(_newTabPage);

        // 停靠顺序：Fill 的控件先加，最后加的要停靠的控件在最外层
        Controls.Add(_tabs);
        Controls.Add(_sidePanel);
        Controls.Add(_toolbar);

        _tabs.SelectedIndexChanged += Tabs_SelectedIndexChanged;
        _tabs.MouseUp += Tabs_MouseUp;

        _shortcutFilter = new ShortcutMessageFilter(this);
        Application.AddMessageFilter(_shortcutFilter);
    }

    // ================= 初始化 =================

    private static ToolStripButton TextButton(string text, string tooltip) => new(text)
    {
        DisplayStyle = ToolStripItemDisplayStyle.Text,
        ToolTipText = tooltip,
    };

    private void BuildToolbar()
    {
        _toolbar.Items.AddRange(new ToolStripItem[]
        {
            _btnBack, _btnForward, _btnReload, _btnHome,
            _address,
            _btnBookmark, _btnFavorites, _btnHistory, _btnDownloads, _btnNewTab,
        });

        _toolbar.Resize += (_, _) => LayoutAddressBar();

        _btnBack.Click += (_, _) => ActiveTab?.GoBack();
        _btnForward.Click += (_, _) => ActiveTab?.GoForward();
        _btnReload.Click += (_, _) => ActiveTab?.Reload();
        _btnHome.Click += (_, _) => Navigate(HomePage);
        _btnBookmark.Click += (_, _) => ToggleBookmark();
        _btnFavorites.Click += (_, _) => ShowSide(SideMode.Favorites);
        _btnHistory.Click += (_, _) => ShowSide(SideMode.History);
        _btnDownloads.Click += (_, _) => ShowDownloads();
        _btnNewTab.Click += (_, _) => NewTab();

        _address.KeyDown += Address_KeyDown;
        _address.Enter += (_, _) => _address.SelectAll();
    }

    private void BuildSidePanel()
    {
        _sideList.DisplayMember = nameof(SideItem.Display);
        _sideList.DoubleClick += (_, _) => OpenSelectedSideItem();
        _sideList.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            OpenSelectedSideItem();
            e.Handled = true;
        };

        _sideDelete.Click += (_, _) => DeleteSelectedSideItem();
        _sideClear.Click += (_, _) => ClearHistory();
        _sideClose.Click += (_, _) => _sidePanel.Visible = false;

        _sideButtons.Controls.Add(_sideDelete);
        _sideButtons.Controls.Add(_sideClear);
        _sideButtons.Controls.Add(_sideClose);

        _sidePanel.Controls.Add(_sideList);
        _sidePanel.Controls.Add(_sideTitle);
        _sidePanel.Controls.Add(_sideButtons);
    }

    private void LayoutAddressBar()
    {
        int used = 0;
        foreach (ToolStripItem item in _toolbar.Items)
        {
            if (ReferenceEquals(item, _address)) continue;
            used += item.Width + item.Margin.Horizontal;
        }

        int width = _toolbar.ClientSize.Width - used - _address.Margin.Horizontal - 10;
        if (width < 120) width = 120;
        if (Math.Abs(_address.Width - width) > 1) _address.Width = width;
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);

        var userDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MinimalBrowser", "WebView2");

        try
        {
            _environment = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                "无法初始化 WebView2 运行时。\n\n" +
                "请先安装 Microsoft Edge WebView2 Runtime（Windows 10 通常已随 Edge 一起安装）。\n\n" +
                ex.Message,
                "MinimalBrowser", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Close();
            return;
        }

        LayoutAddressBar();
        await AddNewTabAsync(HomePage);
    }

    // ================= 标签页 =================

    private IEnumerable<BrowserTab> Tabs =>
        _tabs.TabPages.Cast<TabPage>().Select(p => p.Tag).OfType<BrowserTab>();

    private int RealTabCount => Tabs.Count();

    private BrowserTab? ActiveTab =>
        _tabs.SelectedTab?.Tag as BrowserTab;

    private async Task AddNewTabAsync(string url)
    {
        if (_environment is null) return;

        var tab = new BrowserTab();
        tab.Page.Tag = tab;
        tab.Updated += (sender, _) =>
        {
            if (ReferenceEquals(sender, ActiveTab)) UpdateChrome();
        };
        tab.NewWindowRequested += (_, target) =>
        {
            _ = AddNewTabAsync(string.IsNullOrWhiteSpace(target) ? HomePage : target);
        };
        tab.NavigationFinished += (sender, target) =>
        {
            if (sender is BrowserTab finished) _store.AddHistory(finished.CurrentTitle, target);
        };

        _tabs.TabPages.Insert(Math.Max(0, _tabs.TabPages.Count - 1), tab.Page);
        _tabs.SelectedTab = tab.Page;

        try
        {
            await tab.InitializeAsync(_environment, url);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "创建标签页失败：" + ex.Message,
                "MinimalBrowser", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _tabs.TabPages.Remove(tab.Page);
            tab.Dispose();
            return;
        }

        var core = tab.View.CoreWebView2;
        core.DownloadStarting += (_, args) => OnDownloadStarting(args);
        core.ContainsFullScreenElementChanged += (_, _) =>
        {
            if (!tab.IsInitialized) return;
            bool fullScreen = core.ContainsFullScreenElement;
            _toolbar.Visible = !fullScreen;
            if (fullScreen) _sidePanel.Visible = false;
        };

        UpdateChrome();
        tab.View.Focus();
    }

    private void NewTab() => _ = AddNewTabAsync(HomePage);

    private void CloseTab(BrowserTab tab)
    {
        if (RealTabCount <= 1) return;

        _suppressTabEvents = true;
        _tabs.TabPages.Remove(tab.Page);
        _suppressTabEvents = false;

        tab.Page.Controls.Clear();
        tab.Dispose();

        // 关掉最后一个真实标签时，别把 "+" 页留在选中状态
        if (ReferenceEquals(_tabs.SelectedTab, _newTabPage) && Tabs.LastOrDefault() is { } fallback)
            _tabs.SelectedTab = fallback.Page;

        UpdateChrome();
    }

    private void CloseOtherTabs(BrowserTab keep)
    {
        foreach (var tab in Tabs.Where(t => !ReferenceEquals(t, keep)).ToList())
            CloseTab(tab);
    }

    private void Tabs_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_suppressTabEvents) return;

        if (ReferenceEquals(_tabs.SelectedTab, _newTabPage))
        {
            _ = AddNewTabAsync(HomePage);
            return;
        }

        UpdateChrome();
    }

    private void Tabs_MouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Middle && e.Button != MouseButtons.Right) return;

        for (int i = 0; i < _tabs.TabPages.Count; i++)
        {
            if (!_tabs.GetTabRect(i).Contains(e.Location)) continue;
            if (_tabs.TabPages[i].Tag is not BrowserTab tab) return;

            if (e.Button == MouseButtons.Middle)
            {
                CloseTab(tab);
            }
            else
            {
                _tabs.SelectedTab = tab.Page;
                ShowTabMenu(tab);
            }
            return;
        }
    }

    private void ShowTabMenu(BrowserTab tab)
    {
        _tabMenu.Items.Clear();
        _tabMenu.Items.Add("关闭标签页", null, (_, _) => CloseTab(tab));
        _tabMenu.Items.Add("关闭其他标签页", null, (_, _) => CloseOtherTabs(tab));
        _tabMenu.Show(_tabs, _tabs.PointToClient(Cursor.Position));
    }

    // ================= 导航 =================

    private static string NormalizeInput(string raw)
    {
        var text = (raw ?? "").Trim();
        if (text.Length == 0) return HomePage;

        if (text.Contains("://", StringComparison.Ordinal) ||
            text.StartsWith("about:", StringComparison.OrdinalIgnoreCase))
        {
            return text;
        }

        if (!text.Contains(' ') &&
            (text.Contains('.') || text.StartsWith("localhost", StringComparison.OrdinalIgnoreCase)))
        {
            return "https://" + text;
        }

        return SearchPrefix + Uri.EscapeDataString(text);
    }

    private void Navigate(string raw)
    {
        var url = NormalizeInput(raw);
        if (ActiveTab is { } tab)
        {
            tab.Navigate(url);
            tab.View.Focus();
        }
        else
        {
            _ = AddNewTabAsync(url);
        }
    }

    private void Address_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            Navigate(_address.Text);
        }
        else if (e.KeyCode == Keys.Escape)
        {
            _address.Text = ActiveTab?.CurrentUrl ?? "";
            ActiveTab?.View.Focus();
        }
    }

    private void FocusAddressBar()
    {
        _address.Focus();
        _address.SelectAll();
    }

    // ================= 快捷键 =================

    /// <summary>
    /// 网页内容获得焦点时，键盘消息直接进 WebView2，不经过 WinForms 控件，
    /// 因此用消息过滤器在消息分发前把快捷键拦下来。
    /// </summary>
    private sealed class ShortcutMessageFilter : IMessageFilter
    {
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;

        private readonly MainForm _form;

        public ShortcutMessageFilter(MainForm form) => _form = form;

        public bool PreFilterMessage(ref Message m)
        {
            if (m.Msg != WM_KEYDOWN && m.Msg != WM_SYSKEYDOWN) return false;

            // 长按产生的自动重复不再重复触发
            if (((long)m.LParam & (1L << 30)) != 0) return false;

            var modifiers = Control.ModifierKeys;
            return _form.TryHandleShortcut(
                (Keys)(int)m.WParam,
                modifiers.HasFlag(Keys.Control),
                modifiers.HasFlag(Keys.Alt));
        }
    }

    private bool TryHandleShortcut(Keys keyCode, bool ctrl, bool alt)
    {
        switch (keyCode)
        {
            case Keys.T when ctrl: NewTab(); return true;
            case Keys.W when ctrl: CloseActiveTab(); return true;
            case Keys.L when ctrl: FocusAddressBar(); return true;
            case Keys.D when ctrl: ToggleBookmark(); return true;
            case Keys.B when ctrl: ShowSide(SideMode.Favorites); return true;
            case Keys.H when ctrl: ShowSide(SideMode.History); return true;
            case Keys.J when ctrl: ShowDownloads(); return true;
            case Keys.F5 when !ctrl && !alt: ActiveTab?.Reload(); return true;
            case Keys.Left when alt: ActiveTab?.GoBack(); return true;
            case Keys.Right when alt: ActiveTab?.GoForward(); return true;
            default: return false;
        }
    }

    private void CloseActiveTab()
    {
        if (ActiveTab is { } tab) CloseTab(tab);
    }

    // ================= 收藏夹 / 历史 =================

    private void ToggleBookmark()
    {
        if (ActiveTab is not { } tab) return;
        if (string.IsNullOrWhiteSpace(tab.CurrentUrl) || tab.CurrentUrl == "about:blank") return;

        _store.ToggleBookmark(tab.CurrentTitle, tab.CurrentUrl);
        UpdateChrome();

        if (_sidePanel.Visible && _sideMode == SideMode.Favorites) RefreshSideList();
    }

    private void ShowSide(SideMode mode)
    {
        if (_sidePanel.Visible && _sideMode == mode)
        {
            _sidePanel.Visible = false;
            return;
        }

        _sideMode = mode;
        _sideTitle.Text = mode == SideMode.Favorites ? "收藏夹" : "历史记录";
        _sideClear.Visible = mode == SideMode.History;
        _sidePanel.Visible = true;
        RefreshSideList();
    }

    private void RefreshSideList()
    {
        _sideList.BeginUpdate();
        _sideList.Items.Clear();

        if (_sideMode == SideMode.Favorites)
        {
            foreach (var bookmark in _store.Bookmarks.OrderByDescending(b => b.AddedAt))
            {
                var title = string.IsNullOrWhiteSpace(bookmark.Title) ? bookmark.Url : bookmark.Title;
                _sideList.Items.Add(new SideItem($"{title}  ·  {bookmark.Url}", bookmark.Url, bookmark.AddedAt));
            }
        }
        else
        {
            foreach (var entry in _store.History)
                _sideList.Items.Add(new SideItem($"{entry.Title}  ·  {entry.VisitedAt:MM-dd HH:mm}", entry.Url, entry.VisitedAt));
        }

        _sideList.EndUpdate();
    }

    private void OpenSelectedSideItem()
    {
        if (_sideList.SelectedItem is not SideItem item) return;
        Navigate(item.Url);
    }

    private void DeleteSelectedSideItem()
    {
        if (_sideList.SelectedItem is not SideItem item) return;

        if (_sideMode == SideMode.Favorites)
        {
            _store.Bookmarks.RemoveAll(b => string.Equals(b.Url, item.Url, StringComparison.OrdinalIgnoreCase));
            _store.SaveBookmarks();
        }
        else
        {
            _store.History.RemoveAll(h => h.Url == item.Url && h.VisitedAt == item.Time);
            _store.SaveHistory();
        }

        RefreshSideList();
        UpdateChrome();
    }

    private void ClearHistory()
    {
        var answer = MessageBox.Show(this, "确定要清空全部历史记录吗？", "MinimalBrowser",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (answer != DialogResult.Yes) return;

        _store.ClearHistory();
        RefreshSideList();
    }

    // ================= 下载 =================

    private void ShowDownloads()
    {
        if (!_downloadForm.Visible) _downloadForm.Show(this);
        _downloadForm.Activate();
    }

    private void OnDownloadStarting(CoreWebView2DownloadStartingEventArgs e)
    {
        var operation = e.DownloadOperation;

        var fileName = Path.GetFileName(e.ResultFilePath);
        if (string.IsNullOrWhiteSpace(fileName)) fileName = "download";

        var targetPath = MakeUniquePath(Path.Combine(DownloadsFolder, fileName));

        e.ResultFilePath = targetPath;
        e.Handled = true; // 用自己的下载面板，屏蔽 WebView2 默认下载界面

        _downloadForm.Track(operation, fileName, targetPath);
        if (!_downloadForm.Visible) _downloadForm.Show(this);
    }

    private static string MakeUniquePath(string path)
    {
        if (!File.Exists(path)) return path;

        var directory = Path.GetDirectoryName(path) ?? DownloadsFolder;
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);

        for (int i = 1; i < 1000; i++)
        {
            var candidate = Path.Combine(directory, $"{name} ({i}){extension}");
            if (!File.Exists(candidate)) return candidate;
        }

        return Path.Combine(directory, $"{name} ({Guid.NewGuid():N}){extension}");
    }

    private static string ResolveDownloadsFolder()
    {
        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            Directory.CreateDirectory(path);
            return path;
        }
        catch
        {
            return Path.GetTempPath();
        }
    }

    // ================= 界面状态 =================

    private void UpdateChrome()
    {
        var tab = ActiveTab;

        _btnBack.Enabled = tab?.CanGoBack == true;
        _btnForward.Enabled = tab?.CanGoForward == true;
        _btnReload.Enabled = tab is not null;

        if (!_address.Control.Focused)
            _address.Text = tab?.CurrentUrl ?? "";

        var bookmarked = tab is not null && _store.IsBookmarked(tab.CurrentUrl);
        _btnBookmark.Text = bookmarked ? "★" : "☆";

        Text = tab is not null && !string.IsNullOrWhiteSpace(tab.CurrentTitle)
            ? $"{tab.CurrentTitle} - MinimalBrowser"
            : "MinimalBrowser";
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        Application.RemoveMessageFilter(_shortcutFilter);

        foreach (var tab in Tabs.ToList())
        {
            tab.Page.Controls.Clear();
            tab.Dispose();
        }

        _store.SaveBookmarks();
        _store.SaveHistory();

        base.OnFormClosing(e);
    }
}
