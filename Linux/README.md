# MinimalBrowser（Linux 版）

Windows 版 [MinimalBrowser](../README.md) 的 Linux 移植，基于 **Electron**，功能与操作方式保持一致。

Windows 版的做法是复用系统自带的 Edge 内核（WebView2）；Linux 上没有对应的「系统浏览器内核」可复用，因此这一版**自带 Chromium**，打包成 AppImage 后双击即用、零系统依赖——代价是体积比 Windows 版大得多。

## 功能

- **多标签页** —— 中键关闭，右键菜单可「关闭标签页 / 关闭其他标签页 / 新建无痕窗口」
- **地址栏** —— 带 `://` 按网址处理，不含空格且含 `.` 自动补 `https://`，其余走搜索
- **多搜索引擎** —— 地址栏左侧下拉切换 Bing / Google / 百度 / DuckDuckGo，选择结果写入本地配置
- **基础导航** —— 前进 / 后退 / 刷新 / 停止加载 / 主页
- **收藏夹** —— 一键收藏（`Ctrl+D`），侧栏查看、跳转、删除
- **历史记录** —— 自动记录，同地址 5 秒内去重，上限 2000 条，支持单条删除和一键清空
- **下载管理** —— 弹保存对话框自选文件名与目录，面板显示进度 / 实时速度 / 状态，支持重命名（`F2`）
- **无痕模式** —— `Ctrl+Shift+N` 打开无痕窗口，Cookie、缓存只留在内存、不写入磁盘，也不记录历史
- **全屏适配** —— 页面进入全屏（如视频）时自动隐藏标签栏与工具栏
- **新窗口接管** —— 页面中 `target=_blank` 的链接在当前窗口新建标签页

数据存放位置（`userData` 目录，即 `~/.config/MinimalBrowser/`）：

| 内容 | 文件 |
| --- | --- |
| 收藏夹 | `bookmarks.json` |
| 历史记录 | `history.json` |
| 用户偏好（搜索引擎等） | `settings.json` |

JSON 结构与 Windows 版一致，两个平台的数据文件可以直接互相拷贝。

## 运行要求

- Linux x64（AppImage 需要 FUSE；没有 FUSE 时可用 `./MinimalBrowser-*.AppImage --appimage-extract` 解压后运行 `squashfs-root/AppRun`）
- 无其他系统依赖，Chromium 已打包在内

## 下载使用

从 [Releases](../../releases) 下载 `MinimalBrowser-Linux-*.AppImage`：

```bash
chmod +x MinimalBrowser-Linux-*.AppImage
./MinimalBrowser-Linux-*.AppImage
```

## 从源码运行 / 构建

```bash
cd Linux
npm install
npm start          # 开发模式直接运行

npm run dist       # 打包 AppImage + tar.gz，产物在 dist/
```

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
| `F2` | 重命名下载列表中选中的文件 |
| `F5` | 刷新 |
| `Alt + ←` / `Alt + →` | 后退 / 前进 |

## 项目结构

```
Linux/
├── src/
│   ├── main.js       # 主进程：窗口、会话、下载、IPC、快捷键拦截
│   ├── preload.js    # contextBridge 桥接（渲染进程关掉 nodeIntegration）
│   ├── renderer.js   # 界面逻辑：标签页、地址栏、侧栏、下载面板、无痕
│   ├── index.html    # 界面结构
│   ├── styles.css    # 样式
│   └── store.js      # 收藏夹 / 历史 / 用户偏好的 JSON 存储
├── build/icon.png    # 程序图标（由 Windows 版 app.ico 转出）
└── package.json      # electron + electron-builder 配置
```

## 实现说明

- **无痕模式**：无痕标签使用不以 `persist:` 开头的 Electron 分区（`partition="incognito"`），该分区只存在内存里，Cookie / 缓存不落盘；所有无痕窗口共用同一分区，彼此共享登录态但与普通窗口隔离。
- **快捷键拦截**：焦点落在页面里时按键直接进 Chromium，不经过宿主渲染进程，因此在主进程用 `before-input-event` 拦截并转发回对应窗口，与 Windows 版的 `IMessageFilter` 思路一致。
- **下载**：主进程接管 `session` 的 `will-download`，先弹保存对话框（取消即 `item.cancel()`），再监听 `updated` / `done` 广播给界面。速度由 `getReceivedBytes()` 相邻两次采样差值估算，采样间隔不足 400ms 时沿用上次读数。
- **新窗口接管**：`setWindowOpenHandler` 一律返回 `deny`，改为通知所属窗口新建标签页。
- **安全**：窗口渲染进程关闭 `nodeIntegration`、开启 `contextIsolation`，所有能力经 `preload.js` 白名单暴露。

## 已知限制

- 仅支持 Linux x64
- 未实现扩展、账号同步
- 未做崩溃恢复，关闭即丢失标签页
- 下载重命名仅支持已完成的任务

## License

[MIT](../LICENSE)