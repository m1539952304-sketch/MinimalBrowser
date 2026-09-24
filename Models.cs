namespace MinimalBrowser;

/// <summary>一条收藏记录。</summary>
public sealed class Bookmark
{
    public string Title { get; set; } = "";
    public string Url { get; set; } = "";
    public DateTime AddedAt { get; set; } = DateTime.Now;
}

/// <summary>一条历史记录。</summary>
public sealed class HistoryEntry
{
    public string Title { get; set; } = "";
    public string Url { get; set; } = "";
    public DateTime VisitedAt { get; set; } = DateTime.Now;
}

/// <summary>可持久化的用户偏好。</summary>
public sealed class AppSettings
{
    /// <summary>当前搜索引擎名称，取值见 MainForm.SearchEngines。</summary>
    public string SearchEngine { get; set; } = "Bing";
}
