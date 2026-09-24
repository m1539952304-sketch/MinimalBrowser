'use strict';

const { app, BrowserWindow, Menu, dialog, ipcMain, session } = require('electron');
const fs = require('fs');
const path = require('path');
const Store = require('./store');

// 无痕窗口共用一个「非持久化」分区：分区名不以 persist: 开头时，Cookie / 缓存只存在内存里。
const INCOGNITO_PARTITION = 'incognito';

/** 需要从网页内部（焦点在 webview 里）也生效的快捷键。 */
const SHORTCUT_KEYS = new Set([
  'ctrl+t', 'ctrl+shift+n', 'ctrl+w', 'ctrl+l', 'ctrl+d',
  'ctrl+b', 'ctrl+h', 'ctrl+j', 'f5', 'alt+arrowleft', 'alt+arrowright',
]);

const windows = new Set();
const activeDownloads = new Map();
const instrumentedSessions = new WeakSet();

let store;
let downloadSeq = 0;

// ================= 启动 =================

app.whenReady().then(() => {
  Menu.setApplicationMenu(null); // 极简：不要系统菜单栏，快捷键由程序自己处理

  store = new Store(app.getPath('userData'));

  instrumentSession(session.defaultSession);
  createWindow(false);
});

// 无痕分区是延迟创建的，等它出现时再挂上下载处理
app.on('session-created', (sess) => instrumentSession(sess));

app.on('window-all-closed', () => {
  store?.saveBookmarks();
  store?.saveHistory();
  app.quit();
});

// ================= 窗口 =================

function createWindow(privateMode) {
  const win = new BrowserWindow({
    width: 1200,
    height: 780,
    minWidth: 680,
    minHeight: 440,
    title: privateMode ? 'MinimalBrowser（无痕模式）' : 'MinimalBrowser',
    icon: path.join(__dirname, '..', 'build', 'icon.png'),
    backgroundColor: '#ffffff',
    webPreferences: {
      preload: path.join(__dirname, 'preload.js'),
      contextIsolation: true,
      nodeIntegration: false,
      webviewTag: true,
      additionalArguments: [privateMode ? '--private-mode=1' : '--private-mode=0'],
    },
  });

  win.isPrivate = privateMode;
  windows.add(win);
  win.on('closed', () => windows.delete(win));
  win.loadFile(path.join(__dirname, 'index.html'));
  return win;
}

function broadcast(channel, payload) {
  for (const win of windows) {
    if (!win.isDestroyed()) win.webContents.send(channel, payload);
  }
}

// ================= webContents 统一处理 =================

app.on('web-contents-created', (_event, contents) => {
  // target=_blank / window.open：不在新窗口里打开，而是交给所属窗口新建标签页
  contents.setWindowOpenHandler(({ url }) => {
    // webview 的宿主是渲染进程；窗口自身的 contents 没有宿主，交给它自己
    const host = contents.hostWebContents && !contents.hostWebContents.isDestroyed()
      ? contents.hostWebContents
      : contents;
    if (!host.isDestroyed()) host.send('open-new-tab', url);
    return { action: 'deny' };
  });

  // webview 内部的键盘事件不会经过宿主渲染进程，这里拦下来转交给对应窗口处理
  contents.on('before-input-event', (event, input) => {
    if (input.type !== 'keyDown' || input.isAutoRepeat) return;

    const combo = comboOf(input);
    if (!SHORTCUT_KEYS.has(combo)) return;

    const host = contents.hostWebContents;
    if (!host || host.isDestroyed()) return; // 宿主渲染进程自己的按键由它自己处理
    event.preventDefault();
    host.send('shortcut', combo);
  });
});

function comboOf(input) {
  const parts = [];
  if (input.control) parts.push('ctrl');
  if (input.shift) parts.push('shift');
  if (input.alt) parts.push('alt');
  if (input.meta) parts.push('meta');
  parts.push(String(input.key).toLowerCase());
  return parts.join('+');
}

// ================= 下载 =================

function instrumentSession(sess) {
  if (instrumentedSessions.has(sess)) return;
  instrumentedSessions.add(sess);

  sess.on('will-download', (_event, item) => {
    const win = BrowserWindow.getFocusedWindow() || [...windows][0];

    const suggested = item.getFilename() || 'download';
    const target = dialog.showSaveDialogSync(win, {
      title: '保存文件',
      defaultPath: path.join(downloadsFolder(), suggested),
      buttonLabel: '保存',
      filters: [{ name: '所有文件', extensions: ['*'] }],
      properties: ['showOverwriteConfirmation', 'createDirectory'],
    });

    if (!target) {
      item.cancel();
      return;
    }

    item.setSavePath(target);

    const id = ++downloadSeq;
    const info = {
      id,
      fileName: path.basename(target),
      path: target,
      total: item.getTotalBytes(),
      received: 0,
      state: 'progressing',
    };

    const sample = { lastBytes: 0, lastTime: Date.now(), speed: 0 };
    activeDownloads.set(id, info);
    broadcast('download:added', info);

    item.on('updated', (_e, state) => {
      const received = item.getReceivedBytes();
      const now = Date.now();

      // 采样间隔不足 400ms 时沿用上次速度，避免读数抖动
      if (now - sample.lastTime >= 400) {
        sample.speed = (received - sample.lastBytes) / ((now - sample.lastTime) / 1000);
        sample.lastBytes = received;
        sample.lastTime = now;
      }

      info.received = received;
      info.total = item.getTotalBytes();
      info.state = state === 'interrupted' ? 'interrupted' : 'progressing';

      broadcast('download:progress', {
        id,
        received: info.received,
        total: info.total,
        speed: sample.speed,
        state: info.state,
      });
    });

    item.once('done', (_e, state) => {
      activeDownloads.delete(id);
      broadcast('download:done', { id, state, path: target, fileName: path.basename(target) });
    });
  });
}

function downloadsFolder() {
  const dir = path.join(app.getPath('home'), 'Downloads');
  try {
    fs.mkdirSync(dir, { recursive: true });
  } catch {
    return app.getPath('temp');
  }
  return dir;
}

// ================= IPC =================

ipcMain.handle('store:get-all', () => store.snapshot());
ipcMain.handle('store:toggle-bookmark', (_e, title, url) => store.toggleBookmark(title, url));
ipcMain.handle('store:add-history', (_e, title, url) => { store.addHistory(title, url); });
ipcMain.handle('store:delete-bookmark', (_e, url) => { store.deleteBookmark(url); });
ipcMain.handle('store:delete-history', (_e, url, visitedAt) => { store.deleteHistory(url, visitedAt); });
ipcMain.handle('store:clear-history', () => store.clearHistory());
ipcMain.handle('store:set-setting', (_e, key, value) => store.setSetting(key, value));

ipcMain.handle('window:incognito', () => {
  const win = createWindow(true);
  win.show();
});

ipcMain.handle('download:rename', (_e, filePath, newName) => {
  const inProgress = [...activeDownloads.values()].some((info) => info.path === filePath);
  if (inProgress) {
    return { ok: false, error: '下载中的文件不能重命名' };
  }
  if (/[\\/:*?"<>|]/.test(newName) || !newName.trim()) {
    return { ok: false, error: '文件名包含非法字符' };
  }

  const dir = path.dirname(filePath);
  const newPath = path.join(dir, newName.trim());
  if (newPath === filePath) return { ok: true, path: filePath };

  try {
    if (fs.existsSync(newPath)) return { ok: false, error: '同名文件已存在' };
    fs.renameSync(filePath, newPath);
  } catch (err) {
    return { ok: false, error: err.message };
  }

  broadcast('download:renamed', { path: filePath, newPath, fileName: path.basename(newPath) });
  return { ok: true, path: newPath };
});