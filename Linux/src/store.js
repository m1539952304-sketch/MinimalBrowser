'use strict';

const fs = require('fs');
const path = require('path');

/**
 * 收藏夹 / 历史 / 用户偏好的 JSON 本地存储，与 Windows 版 BrowserStore 的数据格式保持一致，
 * 因此两端的 bookmarks.json / history.json / settings.json 可以互相拷贝使用。
 */
class Store {
  constructor(dir) {
    this.dir = dir;
    this.bookmarksPath = path.join(dir, 'bookmarks.json');
    this.historyPath = path.join(dir, 'history.json');
    this.settingsPath = path.join(dir, 'settings.json');

    fs.mkdirSync(dir, { recursive: true });

    this.bookmarks = this.#read(this.bookmarksPath, []);
    this.history = this.#read(this.historyPath, []);
    this.settings = Object.assign({ searchEngine: 'Bing' }, this.#read(this.settingsPath, {}));
  }

  #read(file, fallback) {
    try {
      const data = JSON.parse(fs.readFileSync(file, 'utf8'));
      return Array.isArray(data) || (data && typeof data === 'object') ? data : fallback;
    } catch {
      return fallback;
    }
  }

  #write(file, data) {
    try {
      fs.writeFileSync(file, JSON.stringify(data, null, 2));
    } catch {
      // 磁盘不可写时静默忽略，不影响浏览
    }
  }

  saveBookmarks() {
    this.#write(this.bookmarksPath, this.bookmarks);
  }

  saveHistory() {
    this.#write(this.historyPath, this.history);
  }

  saveSettings() {
    this.#write(this.settingsPath, this.settings);
  }

  isBookmarked(url) {
    return this.bookmarks.some((b) => b.url === url);
  }

  /** 已收藏则取消，否则添加。返回操作后的收藏状态。 */
  toggleBookmark(title, url) {
    const index = this.bookmarks.findIndex((b) => b.url === url);
    if (index >= 0) {
      this.bookmarks.splice(index, 1);
      this.saveBookmarks();
      return false;
    }
    this.bookmarks.push({ title: title || url, url, addedAt: new Date().toISOString() });
    this.saveBookmarks();
    return true;
  }

  deleteBookmark(url) {
    const before = this.bookmarks.length;
    this.bookmarks = this.bookmarks.filter((b) => b.url !== url);
    if (this.bookmarks.length !== before) this.saveBookmarks();
  }

  /** 记录一次访问；同一地址 5 秒内去重，总量上限 2000 条。 */
  addHistory(title, url) {
    if (!url || url === 'about:blank') return;

    const now = Date.now();
    const last = this.history[0];
    if (last && last.url === url && now - Date.parse(last.visitedAt) < 5000) return;

    this.history.unshift({ title: title || url, url, visitedAt: new Date(now).toISOString() });
    if (this.history.length > 2000) this.history.length = 2000;
    this.saveHistory();
  }

  deleteHistory(url, visitedAt) {
    const before = this.history.length;
    this.history = this.history.filter((h) => !(h.url === url && h.visitedAt === visitedAt));
    if (this.history.length !== before) this.saveHistory();
  }

  clearHistory() {
    this.history = [];
    this.saveHistory();
  }

  setSetting(key, value) {
    this.settings[key] = value;
    this.saveSettings();
  }

  snapshot() {
    return { bookmarks: this.bookmarks, history: this.history, settings: this.settings };
  }
}

module.exports = Store;