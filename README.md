# MinimalBrowser

一个 Windows 10 上的极简浏览器，基于 **C# WinForms + Microsoft Edge WebView2**。

复用系统自带的 Edge 内核，不额外打包 Chromium，因此体积和内存占用都远小于 Electron 方案。

## 功能

- **多标签页** —— 标签宽度固定，中键关闭，右键菜单可「关闭标签页 / 关闭其他标签页」；末尾常驻 `＋` 页，点击即新建
- **地址栏** —— 自动识别输入内容：带 `://` 按网址处理，不含空格且含 `.` 自动补 `https://`，其余走 Bing 搜索
- **基础导航** —— 前进 / 后退 / 刷新 / 主页
- **收藏夹** —— 一键收藏（`Ctrl+D`），侧栏查看、跳转、删除
- **历史记录** —— 自动记录，同地址 5 秒内去重，上限 2000 条，支持单条删除和一键清空
- **下载管理** —— 接管下载并显示进度 / 已下载大小 / 状态，双击打开文件，可定位到文件夹
- **全屏适配** —— 页面进入全屏（如视频）时自动隐藏工具栏
- **新窗口接管** —— 页面中 `target=_blank` 的链接在新标签页打开

数据存放位置：

| 内容 | 路径 |
| --- | --- |
| 收藏夹 / 历史 | `%APPDATA%\MinimalBrowser\bookmarks.json`、`history.json` |
| 浏览器缓存 | `%LOCALAPPDATA%\MinimalBrowser\WebView2\` |
| 默认下载目录 | `%USERPROFILE%\Downloads` |

## 环境要求

- Windows 10 x64（Windows 11 同样可用）
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) —— 仅「精简版」需要
- **Microsoft Edge WebView2 Runtime** —— Windows 10 安装了 Edge 即自带；缺失时程序会弹出提示而非闪退

## 下载使用

从 [Releases](../../releases) 页面下载，二选一：

| 版本 | 大小 | 说明 |
| --- | --- | --- |
| `MinimalBrowser-v1.0.0-win-x64.exe` | 约 63 MB | 免安装单文件，双击即用，无需安装 .NET 运行时 |
| `MinimalBrowser-v1.0.0-win-x64-framework-dependent.zip` | 约 420 KB | 需预装 .NET 8 Desktop Runtime；解压后整个文件夹一起使用，不能只拷贝 exe |

## 从源码构建

```bash
git clone https://github.com/m1539952304-sketch/MinimalBrowser.git
cd MinimalBrowser
dotnet run
```

打包：

```bash
# 免安装单文件
dotnet publish -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true

# 精简版（依赖 .NET 运行时）
dotnet publish -c Release -r win-x64 --self-contained false
```

> `IncludeNativeLibrariesForSelfExtract` 不能省略 —— WebView2 的 `WebView2Loader.dll` 是原生库，缺少该参数时单文件版本无法启动。

## 快捷键

| 快捷键 | 功能 |
| --- | --- |
| `Ctrl + T` | 新建标签页 |
| `Ctrl + W` | 关闭当前标签页 |
| `Ctrl + L` | 聚焦地址栏 |
| `Ctrl + D` | 收藏 / 取消收藏当前页 |
| `Ctrl + B` | 显示收藏夹 |
| `Ctrl + H` | 显示历史记录 |
| `Ctrl + J` | 显示下载内容 |
| `F5` | 刷新 |
| `Alt + ←` / `Alt + →` | 后退 / 前进 |
| `F12` | 开发者工具（交给 WebView2 处理） |

## 项目结构

```
MinimalBrowser/
├── Program.cs              # 入口，PerMonitorV2 DPI
├── MainForm.cs             # 主窗口：工具栏、标签页、侧栏、下载、快捷键
├── BrowserTab.cs           # 单个标签页，封装 WebView2 并收敛事件
├── BrowserStore.cs         # 收藏夹 / 历史的 JSON 本地存储
├── DownloadForm.cs         # 下载列表面板
├── Models.cs               # Bookmark / HistoryEntry 数据模型
└── MinimalBrowser.csproj   # net8.0-windows + WinForms + WebView2
```

## 实现说明

- **共享 WebView2 环境**：所有标签页共用一个 `CoreWebView2Environment`（同一 user data 目录），共享浏览器进程，内存开销低于每标签一个进程。
- **快捷键拦截**：网页获得焦点时键盘消息直接进 WebView2，不经过 WinForms，因此用 `IMessageFilter` 在消息分发前拦截；`Ctrl+F`、`Ctrl+P`、缩放等仍交给 WebView2 原生处理。
- **自定义下载界面**：接管 `DownloadStarting` 并设置 `Handled = true` 屏蔽 WebView2 默认下载 UI，自动重名避让。

## 已知限制

- 仅支持 Windows x64
- 未实现无痕模式、扩展、账号同步
- 未做崩溃恢复，关闭即丢失标签页
- 图标、下载重命名、多引擎切换等尚未提供