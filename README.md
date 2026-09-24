# MinimalBrowser

一个 Windows 10 上的极简浏览器，基于 **C# WinForms + Microsoft Edge WebView2**。

复用系统自带的 Edge 内核，不额外打包 Chromium，因此体积和内存占用都远小于 Electron 方案。

> Linux 版见 [`Linux/`](Linux/README.md) —— 基于 Electron 的移植版，功能与操作方式一致。Linux 上没有可复用的系统浏览器内核，因此那一版自带 Chromium、体积更大。

## 功能

- **多标签页** —— 标签宽度固定，中键关闭，右键菜单可「关闭标签页 / 关闭其他标签页 / 新建无痕窗口」；末尾常驻 `＋` 页，点击即新建
- **地址栏** —— 自动识别输入内容：带 `://` 按网址处理，不含空格且含 `.` 自动补 `https://`，其余走搜索
- **多搜索引擎** —— 地址栏左侧下拉切换 Bing / Google / 百度 / DuckDuckGo，选择结果写入本地配置并长期生效
- **基础导航** —— 前进 / 后退 / 刷新 / 停止加载 / 主页
- **收藏夹** —— 一键收藏（`Ctrl+D`），侧栏查看、跳转、删除
- **历史记录** —— 自动记录，同地址 5 秒内去重，上限 2000 条，支持单条删除和一键清空
- **下载管理** —— 每次下载都弹保存对话框自选文件名与目录，面板显示进度 / 实时速度 / 状态，支持重命名（`F2`）
- **全屏适配** —— 页面进入全屏（如视频）时自动隐藏工具栏
- **新窗口接管** —— 页面中 `target=_blank` 的链接在新标签页打开
- **无痕模式** —— `Ctrl+Shift+N` 或标签右键菜单打开无痕窗口，Cookie、缓存等浏览数据只留在内存、不写入磁盘，也不记录历史；无痕窗口与普通窗口共享收藏夹
- **崩溃恢复** —— 打开中的标签页持续写入本地，只有异常退出（崩溃、被任务管理器强杀、随系统关机）才在下次启动恢复到上次的标签页与选中位置；正常关闭不留会话，下次从空白页开始（无痕窗口不参与）

数据存放位置：

| 内容 | 路径 |
| --- | --- |
| 收藏夹 / 历史 | `%APPDATA%\MinimalBrowser\bookmarks.json`、`history.json` |
| 用户偏好（搜索引擎等） | `%APPDATA%\MinimalBrowser\settings.json` |
| 上次打开的标签页（恢复用） | `%APPDATA%\MinimalBrowser\session.json` |
| 浏览器缓存 | `%LOCALAPPDATA%\MinimalBrowser\WebView2\` |
| 默认下载目录 | `%USERPROFILE%\Downloads` |

## 环境要求

- Windows 10 x64（Windows 11 同样可用）
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) —— 仅「精简版」需要
- **Microsoft Edge WebView2 Runtime** —— Windows 10 安装了 Edge 即自带；缺失时程序会弹出提示而非闪退

## 下载使用

从 [Releases](../../releases) 页面下载。Windows 版二选一，另有 Linux 版 AppImage：

| 版本 | 大小 | 说明 |
| --- | --- | --- |
| `MinimalBrowser-v1.3.0-win-x64.exe` | 约 63 MB | 免安装单文件，双击即用，无需安装 .NET 运行时 |
| `MinimalBrowser-v1.3.0-win-x64-framework-dependent.zip` | 约 550 KB | 需预装 .NET 8 Desktop Runtime；解压后整个文件夹一起使用，不能只拷贝 exe |
| `MinimalBrowser-Linux-1.0.0-x86_64.AppImage` | 约 111 MB | Linux x64 免安装单文件，`chmod +x` 后直接运行 |

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
| `Ctrl + Shift + N` | 新建无痕窗口 |
| `Ctrl + W` | 关闭当前标签页 |
| `Ctrl + L` | 聚焦地址栏 |
| `Ctrl + D` | 收藏 / 取消收藏当前页 |
| `Ctrl + B` | 显示收藏夹 |
| `Ctrl + H` | 显示历史记录（无痕窗口下禁用） |
| `Ctrl + J` | 显示下载内容 |
| `F2` | 重命名下载列表中选中的文件（焦点在下载面板时） |
| `F5` | 刷新 |
| `Alt + ←` / `Alt + →` | 后退 / 前进 |
| `F12` | 开发者工具（交给 WebView2 处理） |

## 项目结构

```
MinimalBrowser/
├── Program.cs              # 入口（PerMonitorV2 DPI）与程序图标加载
├── MainForm.cs             # 主窗口：工具栏、标签页、侧栏、下载、快捷键
├── BrowserTab.cs           # 单个标签页，封装 WebView2 并收敛事件
├── BrowserStore.cs         # 收藏夹 / 历史 / 用户偏好 / 会话 的 JSON 本地存储
├── DownloadForm.cs         # 下载列表面板（进度、速度、重命名）
├── Models.cs               # Bookmark / HistoryEntry / AppSettings / SessionState 数据模型
├── app.ico                 # 程序图标（16~256 共 7 个尺寸）
├── MinimalBrowser.csproj   # net8.0-windows + WinForms + WebView2
└── Linux/                  # Linux 版（Electron），见 Linux/README.md
```

## 实现说明

- **共享 WebView2 环境**：所有标签页共用一个 `CoreWebView2Environment`（同一 user data 目录），共享浏览器进程，内存开销低于每标签一个进程。
- **快捷键拦截**：网页获得焦点时键盘消息直接进 WebView2，不经过 WinForms，因此用 `IMessageFilter` 在消息分发前拦截；`Ctrl+F`、`Ctrl+P`、缩放等仍交给 WebView2 原生处理。
- **下载流程**：接管 `DownloadStarting`，弹 `SaveFileDialog` 让用户决定文件名与目录（取消则设置 `e.Cancel` 放弃下载），再设 `Handled = true` 屏蔽 WebView2 默认下载 UI。速度由 `BytesReceived` 的相邻两次采样差值估算，采样间隔不足 400ms 时沿用上次读数以避免抖动。
- **下载重命名**：复用 `ListView.LabelEdit` 就地编辑文件名列，`AfterLabelEdit` 里校验非法字符与同名冲突后执行 `File.Move`，下载中的文件拒绝改名。
- **加载状态**：`NavigationStarting` / `NavigationCompleted` 维护 `BrowserTab.IsLoading`，用于控制「停止」按钮的启用状态。
- **图标**：`app.ico` 同时通过 `ApplicationIcon` 嵌入 exe 资源（资源管理器 / 任务栏），并以 `LogicalName=MinimalBrowser.app.ico` 作为清单资源嵌入，供 `AppIcon` 在运行时读取后赋给窗口标题栏。
- **无痕模式**：`CoreWebView2ControllerOptions.IsInPrivateModeEnabled` 是 per-controller 选项，因此无痕窗口与普通窗口共用同一个 user data 目录和环境，无需准备第二份配置；无痕标签在创建控制器时开启该选项，浏览数据只留在内存。无痕窗口通过 `NavigationFinished` 事件判断跳过历史写入，并在 UI 上禁用历史入口。窗口间共享同一个 `BrowserStore` 实例，收藏夹改动对普通窗口立即可见。
- **崩溃恢复**：标签页的增删、切换与每次加载完成都会把当前地址列表与选中下标写进 `session.json`，属于增量写入而非退出时统一写，所以进程被强杀也留得住最近状态。文件里的 `CleanExit` 标记区分退出方式——窗口跑起来之后就先把会话标成「异常」，只有用户主动关闭窗口时才改写成「正常」，因此崩溃、`taskkill /F`、断电这些来不及走关闭流程的情况都会留下「异常」标记，下次启动据此恢复；正常关闭则不恢复，从空白页开始。恢复过程中用 `_restoring` 标志挂起来自标签页事件的会话写入，避免把恢复了一半的列表当成新会话存回去。空白页不写入会话，因此只开着一个空白页的窗口不会留下无意义的记录。WebView2 初始化失败时不动会话文件，免得把上次崩溃留下的标签页覆盖掉。

## 已知限制

- 仅支持 Windows x64
- 未实现扩展、账号同步
- 下载重命名仅支持已完成的任务，不能重命名下载中的文件

## License

[MIT](LICENSE)