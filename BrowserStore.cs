using System.Text.Json;

namespace MinimalBrowser;

/// <summary>
/// 收藏夹与历史记录的本地存储（JSON 文件，位于 %APPDATA%\MinimalBrowser）。
/// </summary>
public sealed class BrowserStore
{
    private const int MaxHistory = 2000;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _bookmarksPath;
    private readonly string _historyPath;
    private readonly string _settingsPath;

    public List<Bookmark> Bookmarks { get; private set; }
    public List<HistoryEntry> History { get; private set; }
    public AppSettings Settings { get; private set; }

    public BrowserStore()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MinimalBrowser");
        Directory.CreateDirectory(dir);

        _bookmarksPath = Path.Combine(dir, "bookmarks.json");
        _historyPath = Path.Combine(dir, "history.json");
        _settingsPath = Path.Combine(dir, "settings.json");

        Bookmarks = Load<Bookmark>(_bookmarksPath);
        History = Load<HistoryEntry>(_historyPath);
        Settings = LoadOne(_settingsPath, new AppSettings());
    }

    // ---------- 收藏夹 ----------

    public bool IsBookmarked(string url) =>
        !string.IsNullOrWhiteSpace(url) &&
        Bookmarks.Any(b => string.Equals(b.Url, url, StringComparison.OrdinalIgnoreCase));

    /// <summary>已收藏则取消收藏，否则加入收藏。返回操作后是否处于已收藏状态。</summary>
    public bool ToggleBookmark(string title, string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;

        if (IsBookmarked(url))
        {
            Bookmarks.RemoveAll(b => string.Equals(b.Url, url, StringComparison.OrdinalIgnoreCase));
            SaveBookmarks();
            return false;
        }

        Bookmarks.Add(new Bookmark
        {
            Title = string.IsNullOrWhiteSpace(title) ? url : title.Trim(),
            Url = url,
            AddedAt = DateTime.Now,
        });
        SaveBookmarks();
        return true;
    }

    public void SaveBookmarks() => Save(_bookmarksPath, Bookmarks);

    // ---------- 历史记录 ----------

    public void AddHistory(string title, string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // 同一地址短时间内重复访问（重定向、刷新）只记一次
        if (History.Count > 0)
        {
            var last = History[0];
            if (string.Equals(last.Url, url, StringComparison.OrdinalIgnoreCase) &&
                (DateTime.Now - last.VisitedAt).TotalSeconds < 5)
            {
                return;
            }
        }

        History.Insert(0, new HistoryEntry
        {
            Title = string.IsNullOrWhiteSpace(title) ? url : title.Trim(),
            Url = url,
            VisitedAt = DateTime.Now,
        });

        if (History.Count > MaxHistory)
            History.RemoveRange(MaxHistory, History.Count - MaxHistory);

        SaveHistory();
    }

    public void ClearHistory()
    {
        History.Clear();
        SaveHistory();
    }

    public void SaveHistory() => Save(_historyPath, History);

    // ---------- 用户偏好 ----------

    public void SaveSettings() => SaveOne(_settingsPath, Settings);

    // ---------- 读写 ----------

    private static List<T> Load<T>(string path)
    {
        try
        {
            if (!File.Exists(path)) return new List<T>();
            return JsonSerializer.Deserialize<List<T>>(File.ReadAllText(path)) ?? new List<T>();
        }
        catch
        {
            return new List<T>();
        }
    }

    private static T LoadOne<T>(string path, T fallback)
    {
        try
        {
            if (!File.Exists(path)) return fallback;
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path)) ?? fallback;
        }
        catch
        {
            return fallback;
        }
    }

    private static void Save<T>(string path, List<T> items)
    {
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(items, JsonOptions));
        }
        catch
        {
            // 磁盘不可写时静默忽略，不影响浏览
        }
    }

    private static void SaveOne<T>(string path, T value)
    {
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(value, JsonOptions));
        }
        catch
        {
            // 同上
        }
    }
}
