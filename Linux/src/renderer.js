'use strict';

const api = window.api;
const privateMode = api.isPrivate;

/** 可选搜索引擎。前缀已包含参数名，直接拼接转义后的关键词即可。 */
const SEARCH_ENGINES = [
  { name: 'Bing', prefix: 'https://www.bing.com/search?q=' },
  { name: 'Google', prefix: 'https://www.google.com/search?q=' },
  { name: '百度', prefix: 'https://www.baidu.com/s?wd=' },
  { name: 'DuckDuckGo', prefix: 'https://duckduckgo.com/?q=' },
];

const HOME = 'about:blank';
const MAX_TITLE_LENGTH = 18;

const $ = (id) => document.getElementById(id);
const tabsEl = $('tabs');
const pagesEl = $('pages');
const addressEl = $('address');
const engineEl = $('engine');
const sideEl = $('side');
const sideListEl = $('sideList');
const sideTitleEl = $('sideTitle');
const sideClearEl = $('sideClear');
const downloadsEl = $('downloadsPanel');
const downloadsListEl = $('downloadsList');
const tabMenuEl = $('tabMenu');

const navButtons = {};
for (const button of document.querySelectorAll('#toolbar button[data-act]')) {
  navButtons[button.dataset.act] = button;
}

let store = { bookmarks: [], history: [], settings: { searchEngine: 'Bing' } };

/** 当前窗口的所有标签页，元素形如 { id, view, title, url, loading }。 */
const tabs = [];
let activeId = null;
let tabSeq = 0;

let sideMode = 'favorites';
let sideSelected = null;

/** 本窗口收到的下载任务，key 为主进程分配的 id。 */
const downloads = new Map();
let selectedDownloadId = null;
let renamingDownloadId = null;

const baseTitle = privateMode ? 'MinimalBrowser（无痕模式）' : 'MinimalBrowser';

// ================= 标签页 =================

function activeTab() {
  return tabs.find((t) => t.id === activeId);
}

function openTab(url, activate = true) {
  const id = ++tabSeq;
  const view = document.createElement('webview');

  view.setAttribute('allowpopups', '');
  // 非持久化分区：Cookie、缓存只存在内存里，关闭程序即消失
  if (privateMode) view.setAttribute('partition', 'incognito');
  view.className = 'page';
  view.src = url || HOME;

  pagesEl.appendChild(view);

  const tab = { id, view, title: '新标签页', url: url || HOME, loading: false };
  tabs.push(tab);
  wireTab(tab);
  renderTabs();

  if (activate) selectTab(id);
  return tab;
}

function wireTab(tab) {
  const view = tab.view;

  view.addEventListener('did-start-loading', () => {
    tab.loading = true;
    updateChrome();
  });

  view.addEventListener('did-stop-loading', () => {
    tab.loading = false;
    updateChrome();
  });

  const syncUrl = (event) => {
    tab.url = event.url || safeCall(view, 'getURL') || tab.url;
    if (tab.id === activeId) updateAddress();
    updateChrome();
  };
  view.addEventListener('did-navigate', syncUrl);
  view.addEventListener('did-navigate-in-page', syncUrl);

  view.addEventListener('page-title-updated', (event) => {
    tab.title = event.title || '新标签页';
    renderTabs();
    if (tab.id === activeId) updateTitle();
  });

  view.addEventListener('did-finish-load', () => {
    // 无痕窗口不留浏览记录
    if (privateMode) return;
    api.store.addHistory(tab.title, safeCall(view, 'getURL') || tab.url);
  });

  view.addEventListener('enter-html-full-screen', () => document.body.classList.add('fullscreen'));
  view.addEventListener('leave-html-full-screen', () => document.body.classList.remove('fullscreen'));
}

function safeCall(view, method) {
  try {
    return view[method]();
  } catch {
    // webview 还没 attach 时调用会抛异常
    return null;
  }
}

function selectTab(id) {
  activeId = id;
  for (const tab of tabs) tab.view.classList.toggle('active', tab.id === id);

  renderTabs();
  updateChrome();
  updateAddress();
  updateBookmarkButton();

  const tab = activeTab();
  if (tab) {
    try {
      tab.view.focus();
    } catch {
      // 尚未 attach，忽略
    }
  }
}

function closeTab(id) {
  if (tabs.length <= 1) return;

  const index = tabs.findIndex((t) => t.id === id);
  if (index < 0) return;

  const [tab] = tabs.splice(index, 1);
  tab.view.remove();

  if (activeId === id) {
    selectTab(tabs[Math.min(index, tabs.length - 1)].id);
  } else {
    renderTabs();
  }
}

function closeOtherTabs(keepId) {
  for (const tab of [...tabs]) {
    if (tab.id !== keepId) closeTab(tab.id);
  }
}

function renderTabs() {
  tabsEl.textContent = '';

  for (const tab of tabs) {
    const el = document.createElement('div');
    el.className = 'tab' + (tab.id === activeId ? ' active' : '');
    el.title = tab.title;

    const title = document.createElement('span');
    title.className = 'tab-title';
    title.textContent = shorten(tab.title);
    el.appendChild(title);

    const close = document.createElement('button');
    close.className = 'tab-close';
    close.textContent = '✕';
    close.title = '关闭标签页 (Ctrl+W)';
    close.addEventListener('click', (event) => {
      event.stopPropagation();
      closeTab(tab.id);
    });
    el.appendChild(close);

    el.addEventListener('mousedown', (event) => {
      if (event.button === 0) {
        selectTab(tab.id);
      } else if (event.button === 1) {
        event.preventDefault();
        closeTab(tab.id);
      }
    });

    el.addEventListener('contextmenu', (event) => {
      event.preventDefault();
      event.stopPropagation();
      selectTab(tab.id);
      showTabMenu(event.clientX, event.clientY, tab);
    });

    tabsEl.appendChild(el);
  }
}

function shorten(text) {
  const value = text || '新标签页';
  return value.length <= MAX_TITLE_LENGTH ? value : value.slice(0, MAX_TITLE_LENGTH - 1) + '…';
}

function showTabMenu(x, y, tab) {
  tabMenuEl.textContent = '';

  const addItem = (label, onClick) => {
    const el = document.createElement('div');
    el.textContent = label;
    el.addEventListener('click', () => {
      hideTabMenu();
      onClick();
    });
    tabMenuEl.appendChild(el);
  };

  addItem('关闭标签页', () => closeTab(tab.id));
  addItem('关闭其他标签页', () => closeOtherTabs(tab.id));
  tabMenuEl.appendChild(document.createElement('hr'));
  addItem('新建无痕窗口', () => api.openIncognitoWindow());

  tabMenuEl.hidden = false;
  tabMenuEl.style.left = x + 'px';
  tabMenuEl.style.top = y + 'px';
}

function hideTabMenu() {
  tabMenuEl.hidden = true;
}

// ================= 界面状态 =================

function updateChrome() {
  const tab = activeTab();
  const canBack = tab ? safeCall(tab.view, 'canGoBack') : false;
  const canForward = tab ? safeCall(tab.view, 'canGoForward') : false;

  navButtons.back.disabled = !canBack;
  navButtons.forward.disabled = !canForward;
  navButtons.reload.disabled = !tab;
  navButtons.stop.disabled = !tab || !tab.loading;

  updateTitle();
}

function updateTitle() {
  const tab = activeTab();
  document.title = tab && tab.title ? `${tab.title} - ${baseTitle}` : baseTitle;
}

function updateAddress() {
  if (document.activeElement === addressEl) return;
  const tab = activeTab();
  addressEl.value = tab ? tab.url : '';
}

function updateBookmarkButton() {
  const tab = activeTab();
  const bookmarked = !!tab && store.bookmarks.some((b) => b.url === tab.url);
  navButtons.bookmark.textContent = bookmarked ? '★' : '☆';
}

// ================= 导航 =================

function searchPrefix() {
  const index = engineEl.selectedIndex;
  return index >= 0 && index < SEARCH_ENGINES.length
    ? SEARCH_ENGINES[index].prefix
    : SEARCH_ENGINES[0].prefix;
}

function normalizeInput(raw) {
  const text = (raw || '').trim();
  if (!text) return HOME;

  if (text.includes('://') || text.toLowerCase().startsWith('about:')) return text;

  if (!text.includes(' ') && (text.includes('.') || text.toLowerCase().startsWith('localhost'))) {
    return 'https://' + text;
  }

  return searchPrefix() + encodeURIComponent(text);
}

function navigate(raw) {
  const url = normalizeInput(raw);
  const tab = activeTab();
  if (tab) {
    try {
      tab.view.loadURL(url);
      tab.view.focus();
    } catch {
      tab.view.src = url;
    }
  } else {
    openTab(url);
  }
}

// ================= 收藏夹 / 历史 =================

async function refreshStore() {
  store = await api.store.getAll();
}

function toggleBookmark() {
  const tab = activeTab();
  if (!tab || !tab.url || tab.url === HOME) return;

  api.store.toggleBookmark(tab.title, tab.url).then(async (bookmarked) => {
    await refreshStore();
    updateBookmarkButton();
    if (!sideEl.hidden && sideMode === 'favorites') renderSideList();
    if (bookmarked === false && sideSelected && sideSelected.url === tab.url) sideSelected = null;
  });
}

function toggleSide(mode) {
  if (mode === 'history' && privateMode) return;

  if (!sideEl.hidden && sideMode === mode) {
    sideEl.hidden = true;
    return;
  }

  sideMode = mode;
  sideSelected = null;
  sideTitleEl.textContent = mode === 'favorites' ? '收藏夹' : '历史记录';
  sideClearEl.hidden = mode !== 'history';
  sideEl.hidden = false;
  downloadsEl.hidden = true;
  renderSideList();
}

function renderSideList() {
  sideListEl.textContent = '';

  const items = sideMode === 'favorites'
    ? [...store.bookmarks].sort((a, b) => String(b.addedAt).localeCompare(String(a.addedAt)))
    : store.history;

  for (const item of items) {
    const li = document.createElement('li');
    const time = item.addedAt || item.visitedAt || '';
    li.textContent = sideMode === 'favorites'
      ? `${item.title || item.url}  ·  ${item.url}`
      : `${item.title || item.url}  ·  ${formatTime(time)}`;
    li.dataset.url = item.url;
    li.dataset.time = time;

    li.addEventListener('click', () => {
      sideSelected = { url: item.url, time };
      for (const other of sideListEl.children) other.classList.remove('selected');
      li.classList.add('selected');
    });
    li.addEventListener('dblclick', () => navigate(item.url));

    sideListEl.appendChild(li);
  }
}

function deleteSelectedSideItem() {
  if (!sideSelected) return;

  const action = sideMode === 'favorites'
    ? api.store.deleteBookmark(sideSelected.url)
    : api.store.deleteHistory(sideSelected.url, sideSelected.time);

  action.then(async () => {
    sideSelected = null;
    await refreshStore();
    renderSideList();
    updateBookmarkButton();
  });
}

function clearHistory() {
  if (!confirm('确定要清空全部历史记录吗？')) return;
  api.store.clearHistory().then(async () => {
    sideSelected = null;
    await refreshStore();
    renderSideList();
  });
}

function formatTime(iso) {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return '';
  const pad = (n) => String(n).padStart(2, '0');
  return `${pad(date.getMonth() + 1)}-${pad(date.getDate())} ${pad(date.getHours())}:${pad(date.getMinutes())}`;
}

// ================= 下载 =================

function toggleDownloads() {
  downloadsEl.hidden = !downloadsEl.hidden;
  if (!downloadsEl.hidden) {
    sideEl.hidden = true;
    renderDownloads();
  }
}

function showDownloads() {
  downloadsEl.hidden = false;
  sideEl.hidden = true;
}

function onDownloadAdded(info) {
  downloads.set(info.id, { ...info, speed: 0, done: false, cancelled: false, failed: false });
  showDownloads();
  renderDownloads();
}

function onDownloadProgress(progress) {
  const item = downloads.get(progress.id);
  if (!item) return;
  item.received = progress.received;
  item.total = progress.total;
  item.speed = progress.speed;
  item.state = progress.state;
  renderDownloads();
}

function onDownloadDone(result) {
  const item = downloads.get(result.id);
  if (!item) return;
  item.done = true;
  item.cancelled = result.state === 'cancelled';
  item.failed = result.state === 'interrupted';
  item.received = item.total || item.received;
  renderDownloads();
}

function onDownloadRenamed(info) {
  for (const item of downloads.values()) {
    if (item.path === info.path) {
      item.path = info.newPath;
      item.fileName = info.fileName;
    }
  }
  renderDownloads();
}

function renderDownloads() {
  downloadsListEl.textContent = '';
  const items = [...downloads.values()].sort((a, b) => b.id - a.id);

  for (const item of items) {
    const li = document.createElement('li');
    li.className = 'dl-item';
    if (item.id === selectedDownloadId) li.classList.add('selected');
    if (item.done && !item.cancelled && !item.failed) li.classList.add('done');
    if (item.failed || item.cancelled) li.classList.add('failed');
    li.addEventListener('click', () => {
      selectedDownloadId = item.id;
      renderDownloads();
    });
    li.addEventListener('dblclick', () => startRename(item.id));

    const row = document.createElement('div');
    row.className = 'dl-row';

    const name = document.createElement('span');
    name.className = 'dl-name';
    if (item.id === renamingDownloadId) {
      name.dataset.rename = String(item.id);
      const input = document.createElement('input');
      input.type = 'text';
      input.value = item.fileName;
      input.addEventListener('keydown', (event) => {
        if (event.key === 'Enter') {
          event.preventDefault();
          commitRename(item.id, input.value);
        } else if (event.key === 'Escape') {
          renamingDownloadId = null;
          renderDownloads();
        }
      });
      input.addEventListener('blur', () => {
        if (renamingDownloadId === item.id) commitRename(item.id, input.value);
      });
      name.appendChild(input);
    } else {
      name.textContent = item.fileName;
      name.title = item.path;
    }

    const status = document.createElement('span');
    status.className = 'dl-status';
    status.textContent = downloadStatusText(item);

    row.appendChild(name);
    row.appendChild(status);
    li.appendChild(row);

    const bar = document.createElement('div');
    bar.className = 'dl-bar';
    const fill = document.createElement('div');
    fill.style.width = downloadPercent(item) + '%';
    bar.appendChild(fill);
    li.appendChild(bar);

    downloadsListEl.appendChild(li);
  }

  // 重命名输入框需要重新聚焦（列表是整体重建的）
  if (renamingDownloadId !== null) {
    const input = downloadsListEl.querySelector(`[data-rename="${renamingDownloadId}"] input`);
    if (input) {
      input.focus();
      input.select();
    }
  }
}

function downloadPercent(item) {
  if (!item.total) return item.done ? 100 : 0;
  return Math.min(100, Math.round((item.received / item.total) * 100));
}

function downloadStatusText(item) {
  if (item.cancelled) return '已取消';
  if (item.failed) return '失败';
  if (item.state === 'interrupted') return '已中断';
  if (item.done) return '已完成';

  const percent = item.total ? `${downloadPercent(item)}%` : formatBytes(item.received);
  return item.speed > 0 ? `${percent} · ${formatBytes(item.speed)}/s` : percent;
}

function formatBytes(bytes) {
  if (!bytes || bytes <= 0) return '0 B';
  const units = ['B', 'KB', 'MB', 'GB'];
  const index = Math.min(units.length - 1, Math.floor(Math.log(bytes) / Math.log(1024)));
  return (bytes / 1024 ** index).toFixed(index === 0 ? 0 : 1) + ' ' + units[index];
}

function selectedDownload() {
  return selectedDownloadId === null ? null : downloads.get(selectedDownloadId);
}

function startRename(id) {
  const item = downloads.get(id);
  if (!item || !item.done || item.cancelled || item.failed) return;
  renamingDownloadId = id;
  selectedDownloadId = id;
  renderDownloads();
}

async function commitRename(id, value) {
  const item = downloads.get(id);
  renamingDownloadId = null;
  if (!item) return;

  const name = (value || '').trim();
  if (!name || name === item.fileName) {
    renderDownloads();
    return;
  }

  const result = await api.downloads.rename(item.path, name);
  if (!result.ok) {
    alert('重命名失败：' + result.error);
    renderDownloads();
    return;
  }

  item.path = result.path;
  item.fileName = name;
  renderDownloads();
}

// ================= 快捷键 =================

function handleShortcut(combo) {
  switch (combo) {
    case 'ctrl+t': openTab(HOME); return true;
    case 'ctrl+shift+n': api.openIncognitoWindow(); return true;
    case 'ctrl+w': { const tab = activeTab(); if (tab) closeTab(tab.id); return true; }
    case 'ctrl+l': addressEl.focus(); addressEl.select(); return true;
    case 'ctrl+d': toggleBookmark(); return true;
    case 'ctrl+b': toggleSide('favorites'); return true;
    case 'ctrl+h': if (!privateMode) toggleSide('history'); return true;
    case 'ctrl+j': toggleDownloads(); return true;
    case 'f5': { const tab = activeTab(); if (tab) tab.view.reload(); return true; }
    case 'alt+arrowleft': { const tab = activeTab(); if (tab) tab.view.goBack(); return true; }
    case 'alt+arrowright': { const tab = activeTab(); if (tab) tab.view.goForward(); return true; }
    default: return false;
  }
}

function eventCombo(event) {
  const parts = [];
  if (event.ctrlKey) parts.push('ctrl');
  if (event.shiftKey) parts.push('shift');
  if (event.altKey) parts.push('alt');
  if (event.metaKey) parts.push('meta');
  parts.push(event.key.toLowerCase());
  return parts.join('+');
}

// ================= 事件绑定 =================

function bindToolbar() {
  navButtons.back.addEventListener('click', () => { const t = activeTab(); if (t) t.view.goBack(); });
  navButtons.forward.addEventListener('click', () => { const t = activeTab(); if (t) t.view.goForward(); });
  navButtons.reload.addEventListener('click', () => { const t = activeTab(); if (t) t.view.reload(); });
  navButtons.stop.addEventListener('click', () => { const t = activeTab(); if (t && t.loading) t.view.stop(); });
  navButtons.home.addEventListener('click', () => navigate(HOME));
  navButtons.bookmark.addEventListener('click', () => toggleBookmark());
  navButtons.favorites.addEventListener('click', () => toggleSide('favorites'));
  navButtons.history.addEventListener('click', () => toggleSide('history'));
  navButtons.downloads.addEventListener('click', () => toggleDownloads());
  $('newTab').addEventListener('click', () => openTab(HOME));

  addressEl.addEventListener('keydown', (event) => {
    if (event.key === 'Enter') {
      event.preventDefault();
      navigate(addressEl.value);
    } else if (event.key === 'Escape') {
      updateAddress();
      const tab = activeTab();
      if (tab) tab.view.focus();
    }
  });
  addressEl.addEventListener('focus', () => addressEl.select());
}

function bindSide() {
  $('sideClose').addEventListener('click', () => { sideEl.hidden = true; });
  $('sideDelete').addEventListener('click', () => deleteSelectedSideItem());
  sideClearEl.addEventListener('click', () => clearHistory());

  sideListEl.addEventListener('keydown', (event) => {
    if (event.key === 'Enter' && sideSelected) navigate(sideSelected.url);
  });
}

function bindDownloads() {
  $('downloadsClose').addEventListener('click', () => { downloadsEl.hidden = true; });
  $('downloadRename').addEventListener('click', () => {
    const item = selectedDownload();
    if (item) startRename(item.id);
  });
}

function bindGlobalKeys() {
  window.addEventListener('keydown', (event) => {
    const combo = eventCombo(event);
    if (handleShortcut(combo)) {
      event.preventDefault();
      event.stopPropagation();
    }
    if (event.key === 'Escape') hideTabMenu();
  });
  document.addEventListener('click', () => hideTabMenu());
}

// ================= 启动 =================

async function init() {
  store = await api.store.getAll();

  for (const engine of SEARCH_ENGINES) {
    const option = document.createElement('option');
    option.value = engine.name;
    option.textContent = engine.name;
    engineEl.appendChild(option);
  }

  const saved = SEARCH_ENGINES.findIndex((e) => e.name === store.settings.searchEngine);
  engineEl.selectedIndex = saved < 0 ? 0 : saved;
  engineEl.addEventListener('change', () => {
    store.settings.searchEngine = SEARCH_ENGINES[engineEl.selectedIndex].name;
    api.store.setSetting('searchEngine', store.settings.searchEngine);
  });

  if (privateMode) {
    $('privateBadge').hidden = false;
    navButtons.history.disabled = true;
    navButtons.history.title = '无痕模式下不记录历史';
  }

  bindToolbar();
  bindSide();
  bindDownloads();
  bindGlobalKeys();

  api.onOpenNewTab((url) => openTab(url || HOME));
  api.onShortcut((combo) => handleShortcut(combo));
  api.downloads.onAdded(onDownloadAdded);
  api.downloads.onProgress(onDownloadProgress);
  api.downloads.onDone(onDownloadDone);
  api.downloads.onRenamed(onDownloadRenamed);

  openTab(HOME);
}

init();