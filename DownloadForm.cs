using System.Diagnostics;
using Microsoft.Web.WebView2.Core;

namespace MinimalBrowser;

/// <summary>下载列表面板。关闭时只隐藏，不销毁。</summary>
public sealed class DownloadForm : Form
{
    private readonly ListView _list = new();
    private readonly Button _btnOpenFolder = new();
    private readonly Button _btnClear = new();
    private readonly Dictionary<CoreWebView2DownloadOperation, Row> _rows = new();

    private sealed class Row
    {
        public ListViewItem Item { get; }
        public string TargetPath { get; }
        public bool Completed { get; set; }

        public Row(ListViewItem item, string targetPath)
        {
            Item = item;
            TargetPath = targetPath;
        }
    }

    public DownloadForm()
    {
        Text = "下载内容";
        ClientSize = new Size(780, 400);
        MinimumSize = new Size(560, 260);
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
        _list.Columns.Add("文件名", 230);
        _list.Columns.Add("进度", 190);
        _list.Columns.Add("状态", 80);
        _list.Columns.Add("保存位置", 260);
        _list.DoubleClick += (_, _) => OpenSelectedFile();

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

        _btnClear.Text = "清除已完成";
        _btnClear.AutoSize = true;
        _btnClear.Click += (_, _) => ClearCompleted();

        bottom.Controls.Add(_btnOpenFolder);
        bottom.Controls.Add(_btnClear);

        Controls.Add(_list);
        Controls.Add(bottom);
    }

    /// <summary>把一个新下载任务加入列表，并持续跟踪它的进度。</summary>
    public void Track(CoreWebView2DownloadOperation operation, string fileName, string targetPath)
    {
        var item = new ListViewItem(new[] { fileName, "0%", "下载中", targetPath });
        var row = new Row(item, targetPath);

        _rows[operation] = row;
        _list.Items.Insert(0, item);

        operation.BytesReceivedChanged += (_, _) => Refresh(operation);
        operation.StateChanged += (_, _) => Refresh(operation);

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
            row.Item.SubItems[3].Text = operation.ResultFilePath;
        }
        catch
        {
            return;
        }

        ulong total = totalBytes ?? 0;

        row.Item.SubItems[1].Text = total > 0
            ? $"{received * 100.0 / total:0}%  ({FormatSize(received)} / {FormatSize(total)})"
            : FormatSize(received);

        switch (state)
        {
            case CoreWebView2DownloadState.InProgress:
                row.Item.SubItems[2].Text = "下载中";
                break;
            case CoreWebView2DownloadState.Completed:
                row.Item.SubItems[2].Text = "已完成";
                row.Completed = true;
                break;
            case CoreWebView2DownloadState.Interrupted:
                row.Item.SubItems[2].Text = "已中断";
                break;
            default:
                row.Item.SubItems[2].Text = "未知";
                break;
        }
    }

    private void ClearCompleted()
    {
        foreach (var pair in _rows.Where(p => p.Value.Completed).ToList())
        {
            _rows.Remove(pair.Key);
            _list.Items.Remove(pair.Value.Item);
        }
    }

    private void OpenSelectedFile()
    {
        if (_list.SelectedItems.Count == 0) return;
        var path = _rows.Values.FirstOrDefault(r => r.Item == _list.SelectedItems[0])?.TargetPath;
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
        StartProcess(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    private void OpenSelectedFolder()
    {
        if (_list.SelectedItems.Count == 0) return;
        var path = _rows.Values.FirstOrDefault(r => r.Item == _list.SelectedItems[0])?.TargetPath;
        if (string.IsNullOrEmpty(path)) return;

        if (File.Exists(path))
            StartProcess(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        else
            StartProcess(new ProcessStartInfo("explorer.exe", $"/select,\"{Path.GetDirectoryName(path)}\"") { UseShellExecute = true });
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
