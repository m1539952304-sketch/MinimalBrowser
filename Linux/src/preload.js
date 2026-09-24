'use strict';

const { contextBridge, ipcRenderer } = require('electron');

/**
 * 渲染进程与主进程之间唯一的通道。渲染进程关掉了 nodeIntegration，
 * 所有文件读写、对话框、下载控制都通过这里转发。
 */
contextBridge.exposeInMainWorld('api', {
  /** 当前窗口是否为无痕窗口，由主进程通过启动参数告知。 */
  isPrivate: process.argv.includes('--private-mode=1'),

  store: {
    getAll: () => ipcRenderer.invoke('store:get-all'),
    toggleBookmark: (title, url) => ipcRenderer.invoke('store:toggle-bookmark', title, url),
    addHistory: (title, url) => ipcRenderer.invoke('store:add-history', title, url),
    deleteBookmark: (url) => ipcRenderer.invoke('store:delete-bookmark', url),
    deleteHistory: (url, visitedAt) => ipcRenderer.invoke('store:delete-history', url, visitedAt),
    clearHistory: () => ipcRenderer.invoke('store:clear-history'),
    setSetting: (key, value) => ipcRenderer.invoke('store:set-setting', key, value),
  },

  downloads: {
    rename: (filePath, newName) => ipcRenderer.invoke('download:rename', filePath, newName),
    onAdded: (cb) => ipcRenderer.on('download:added', (_e, info) => cb(info)),
    onProgress: (cb) => ipcRenderer.on('download:progress', (_e, info) => cb(info)),
    onDone: (cb) => ipcRenderer.on('download:done', (_e, info) => cb(info)),
    onRenamed: (cb) => ipcRenderer.on('download:renamed', (_e, info) => cb(info)),
  },

  openIncognitoWindow: () => ipcRenderer.invoke('window:incognito'),
  onOpenNewTab: (cb) => ipcRenderer.on('open-new-tab', (_e, url) => cb(url)),
  onShortcut: (cb) => ipcRenderer.on('shortcut', (_e, combo) => cb(combo)),
});