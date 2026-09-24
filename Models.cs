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

/// <summary>
/// 上次退出时打开的标签页。写入是在浏览过程中持续进行的，因此即使进程被强杀，
/// 文件里也是最近一次的状态；<see cref="CleanExit"/> 用来区分「正常关闭」与「异常退出」。
/// </summary>
public sealed class SessionState
{
    /// <summary>标签页地址，按从左到右的顺序；空白页不记录。</summary>
    public List<string> Tabs { get; set; } = new();

    /// <summary>退出时处于选中状态的标签页下标。</summary>
    public int ActiveIndex { get; set; }

    /// <summary>上次是否正常关闭。false 表示崩溃 / 被强杀 / 随系统关机，下次启动应恢复标签页。</summary>
    public bool CleanExit { get; set; }
}
