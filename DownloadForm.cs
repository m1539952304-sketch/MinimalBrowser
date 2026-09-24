using System.Diagnostics;
using Microsoft.Web.WebView2.Core;

namespace MinimalBrowser;

/// <summary>下载列表面板。关闭时只隐藏，不销毁。</summary>
public sealed class DownloadForm : Form
{
    /// <summary>速度采样的最小间隔，间隔太短读数会剧烈抖动。</summary>
    private const int SpeedSampleMs = 400;

    private readonly ListView _list = new();
    private readonly Button _btnOpenFolder = new();
    private readonly Button _btnRename = new();
    private readonly Button _btnClear = new();
    private readonly Dictionary<CoreWebView2DownloadOperation, Row> _rows = new();
    private readonly System.Windows.Forms.Timer _ticker = new() { Interval = 500 };

    private sealed class Row
    {
        public ListViewItem Item { get; }
        public string TargetPath { get; set; }
        public bool Completed { get; set; }
        public bool Active { get; set; } = true;
        public long LastBytes { get; set; }
        public DateTime LastSampleUtc { get; set; } = DateTime.UtcNow;

        public Row(ListViewItem item, string targetPath)
        {
            Item = item;
            TargetPath = targetPath;
        }
    }

    public DownloadForm()
    {
        Text = "下载内容";
        ClientSize = new Size(900, 400);
        MinimumSize = new Size(600, 260);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;
        Font = new Font("Microsoft YaHei UI", 9F);

        _list.Dock = DockStyle.Fill;
        _list.View = View.Details;
        _list.FullRowSelect = true;
        _list.MultiSelect = false;
        _list.HideSelection = false;
        _list.HeaderStyle = ColumnHeaderStyle.Nonclickable;
        _list.LabelEdit = true; // 就地编辑文件名列
        _list.Columns.Add("文件名", 220);
        _list.Columns.Add("进度", 190);
        _list.Columns.Add("速度", 100);
        _list.Columns.Add("状态", 80);
        _list.Columns.Add("保存位置", 260);
        _list.DoubleClick += (_, _) => OpenSelectedFile();
        _list.AfterLabelEdit += AfterLabelEdit;
        _list.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.F2) return;
            BeginRename();
            e.Handled = true;
        };

        _ticker.Tick += (_, _) => RefreshActive();

        var bottom = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 42,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8, 6, 8, 6),
        };

        _btnOpenFolder.Text = "打开所在文件夹";
        _btnOpenFolder.AutoSize = true;
        _btnOpenFolder.Click += (_, _) => OpenSelectedFolder();

        _btnRename.Text = "重命名";
        _btnRename.AutoSize = true;
        _btnRename.Click += (_, _) => BeginRename();

        _btnClear.Text = "清除已完成";
        _btnClear.AutoSize = true;
        _btnClear.Click += (_, _) => ClearCompleted();

        bottom.Controls.Add(_btnOpenFolder);
        bottom.Controls.Add(_btnRename);
        bottom.Controls.Add(_btnClear);

        Controls.Add(_list);
        Controls.Add(bottom);

        if (AppIcon.Value is { } icon) Icon = icon;
    }

    /// <summary>把一个新下载任务加入列表，并持续跟踪它的进度。</summary>
    public void Track(CoreWebView2DownloadOperation operation, string fileName, string targetPath)
    {
        var item = new ListViewItem(new[] { fileName, "0%", "-", "下载中", targetPath });
        var row = new Row(item, targetPath);

        _rows[operation] = row;
        _list.Items.Insert(0, item);

        operation.BytesReceivedChanged += (_, _) => Refresh(operation);
        operation.StateChanged += (_, _) => Refresh(operation);

        if (!_ticker.Enabled) _ticker.Start();

        Refresh(operation);
    }

    private void Refresh(CoreWebView2DownloadOperation operation)
    {
        if (!_rows.TryGetValue(operation, out var row)) return;

        long received;
        ulong? totalBytes;
        CoreWebView2DownloadState state;

        try
        {
            received = operation.BytesReceived;
            totalBytes = operation.TotalBytesToReceive;
            state = operation.State;
            row.Item.SubItems[4].Text = operation.ResultFilePath;
        }
        catch
        {
            return; // 下载对象可能已被释放
        }

        ulong total = totalBytes ?? 0;

        row.Item.SubItems[1].Text = total > 0
            ? $"{received * 100.0 / total:0}%  ({FormatSize(received)} / {FormatSize(total)})"
            : FormatSize(received);

        row.Item.SubItems[2].Text = state == CoreWebView2DownloadState.InProgress
            ? SampleSpeed(row, received)
            : "-";

        switch (state)
        {
            case CoreWebView2DownloadState.InProgress:
                row.Item.SubItems[3].Text = "下载中";
                break;
            case CoreWebView2DownloadState.Completed:
                row.Item.SubItems[3].Text = "已完成";
                row.Completed = true;
                row.Active = false;
                break;
            case CoreWebView2DownloadState.Interrupted:
                row.Item.SubItems[3].Text = "已中断";
                row.Active = false;
                break;
            default:
                row.Item.SubItems[3].Text = "未知";
                row.Active = false;
                break;
        }
    }

    /// <summary>用两次采样之间的字节差估算瞬时速度；间隔不足时沿用上次读数。</summary>
    private static string SampleSpeed(Row row, long received)
    {
        var now = DateTime.UtcNow;
        var elapsedMs = (now - row.LastSampleUtc).TotalMilliseconds;
        if (elapsedMs < SpeedSampleMs) return row.Item.SubItems[2].Text;

        long delta = received - row.LastBytes; // 断点续传会让计数回退，此时不显示速度
        row.LastBytes = received;
        row.LastSampleUtc = now;

        if (delta <= 0) return "-";
        return FormatSize(delta / (elapsedMs / 1000.0)) + "/s";
    }

    /// <summary>定时刷新未结束的任务；全部结束后停表，避免空转。</summary>
    private void RefreshActive()
    {
        var active = _rows.Where(p => p.Value.Active).ToList();
        if (active.Count == 0)
        {
            _ticker.Stop();
            return;
        }

        foreach (var pair in active) Refresh(pair.Key);
    }

    private void ClearCompleted()
    {
        foreach (var pair in _rows.Where(p => p.Value.Completed).ToList())
        {
            _rows.Remove(pair.Key);
            _list.Items.Remove(pair.Value.Item);
        }
    }

    private Row? RowOf(ListViewItem item) =>
        _rows.Values.FirstOrDefault(r => r.Item == item);

    private void BeginRename()
    {
        if (_list.SelectedItems.Count == 0) return;

        var row = RowOf(_list.SelectedItems[0]);
        if (row is null) return;

        if (!row.Completed)
        {
            MessageBox.Show(this, "下载中的文件不能重命名，请等待下载完成。", "下载内容",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _list.SelectedItems[0].BeginEdit();
    }

    /// <summary>文件名列就地编辑结束后，把磁盘上的文件一起改名。</summary>
    private void AfterLabelEdit(object? sender, LabelEditEventArgs e)
    {
        if (e.Label is null) return; // 用户按 Esc 放弃

        var row = RowOf(_list.Items[e.Item]);
        if (row is null)
        {
            e.CancelEdit = true;
            return;
        }

        var newName = e.Label.Trim();
        if (newName.Length == 0 || newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            e.CancelEdit = true;
            MessageBox.Show(this, "文件名不能为空，也不能包含 \\ / : * ? \" < > | 等字符。", "下载内容",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var directory = Path.GetDirectoryName(row.TargetPath);
        if (string.IsNullOrEmpty(directory))
        {
            e.CancelEdit = true;
            return;
        }

        var newPath = Path.Combine(directory, newName);
        if (string.Equals(newPath, row.TargetPath, StringComparison.OrdinalIgnoreCase)) return;

        if (File.Exists(newPath))
        {
            e.CancelEdit = true;
            MessageBox.Show(this, "该目录下已存在同名文件，请换一个名字。", "下载内容",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            File.Move(row.TargetPath, newPath);
        }
        catch (Exception ex)
        {
            e.CancelEdit = true;
            MessageBox.Show(this, "重命名失败：" + ex.Message, "下载内容",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        row.TargetPath = newPath;
        row.Item.SubItems[4].Text = newPath;
    }

    private void OpenSelectedFile()
    {
        if (_list.SelectedItems.Count == 0) return;
        var path = RowOf(_list.SelectedItems[0])?.TargetPath;
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
        StartProcess(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    private void OpenSelectedFolder()
    {
        if (_list.SelectedItems.Count == 0) return;
        var path = RowOf(_list.SelectedItems[0])?.TargetPath;
        if (string.IsNullOrEmpty(path)) return;

        var target = File.Exists(path) ? path : Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(target)) return;

        StartProcess(new ProcessStartInfo("explorer.exe", $"/select,\"{target}\"") { UseShellExecute = true });
    }

    private static void StartProcess(ProcessStartInfo info)
    {
        try
        {
            Process.Start(info);
        }
        catch
        {
            // 资源管理器不可用时忽略
        }
    }

    private static string FormatSize(double bytes)
    {
        if (bytes < 0) return "-";
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return unit == 0 ? $"{bytes:0} B" : $"{value:0.##} {units[unit]}";
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _ticker.Dispose();
        base.Dispose(disposing);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // 用户点关闭时只隐藏，保留下载记录
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnFormClosing(e);
    }
}
