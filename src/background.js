/* 3D Browser Pro — service worker (MV3) */
import './lib/formats.js';
import './lib/settings.js';

const F = globalThis.BP3D;
const S = F.settings;

const MAX_PER_TAB = 250;
const tabAssets = new Map();     // tabId -> Map<key, asset>
const requestInfo = new Map();   // requestId -> { tabId, url }
const autoDone = new Set();      // URLs já baixadas automaticamente

let cfg = S.normalize(null);

S.load().then((value) => { cfg = value; refreshBadges(); }).catch(() => {});

chrome.storage.onChanged.addListener((changes, area) => {
  if (area !== 'local' || !changes.settings) return;
  cfg = S.normalize(changes.settings.newValue);
  refreshBadges();
});

/* ------------------------------------------------------------------ */
/* Registro por aba                                                    */
/* ------------------------------------------------------------------ */

function tabMap(tabId) {
  let m = tabAssets.get(tabId);
  if (!m) { m = new Map(); tabAssets.set(tabId, m); }
  return m;
}

let broadcastTimer = null;
function scheduleBroadcast() {
  if (broadcastTimer) return;
  broadcastTimer = setTimeout(() => {
    broadcastTimer = null;
    chrome.runtime.sendMessage({ type: 'assets-updated' }).catch(() => {});
  }, 600);
}

function updateBadge(tabId) {
  const m = tabAssets.get(tabId);
  const n = m ? m.size : 0;
  const text = (cfg.badge && n) ? (n > 99 ? '99+' : String(n)) : '';
  try {
    chrome.action.setBadgeBackgroundColor({ color: '#7c3aed', tabId });
    chrome.action.setBadgeText({ tabId, text });
  } catch (e) { /* aba já fechada */ }
}

function refreshBadges() {
  for (const tabId of tabAssets.keys()) updateBadge(tabId);
}

function upsert(tabId, patch) {
  if (tabId == null || tabId < 0 || !patch || !patch.url) return;

  const fmt = patch.format ? F.formatById(patch.format) : F.detectFormat(patch.url, patch.mime);
  if (!fmt && !patch.inMemory) return;
  if (fmt && !S.isFormatEnabled(cfg, fmt.id)) return;

  const m = tabMap(tabId);
  const key = patch.key || F.normalizeUrl(patch.url);
  const prev = m.get(key) || {};
  const isNew = !prev.url;
  const next = Object.assign({}, prev, patch, {
    key,
    url: F.normalizeUrl(patch.url),
    format: patch.format || (fmt ? fmt.id : 'bin'),
    ts: patch.ts || Date.now(),
    source: patch.source || prev.source || 'net'
  });
  m.set(key, next);

  if (m.size > MAX_PER_TAB) m.delete(m.keys().next().value);

  updateBadge(tabId);
  scheduleBroadcast();

  if (isNew) maybeAutoDownload(tabId, next);
}

function maybeAutoDownload(tabId, asset) {
  if (!cfg.autoFormats.length) return;
  if (asset.inMemory) return;
  if (!S.isAutoEnabled(cfg, asset.format)) return;

  const key = asset.url;
  if (autoDone.has(key)) return;
  autoDone.add(key);
  if (autoDone.size > 800) autoDone.clear();

  downloadAsset(tabId, asset).catch(() => {});
}

/* ------------------------------------------------------------------ */
/* Captura de rede                                                     */
/* ------------------------------------------------------------------ */

chrome.webRequest.onBeforeRequest.addListener((d) => {
  try {
    if (!d.url || d.tabId == null || d.tabId < 0) return;
    if (!/^https?:/i.test(d.url)) return;
    const fmt = F.detectFormat(d.url, '');
    if (!fmt || !S.isFormatEnabled(cfg, fmt.id)) return;
    requestInfo.set(d.requestId, { tabId: d.tabId, url: d.url });
    upsert(d.tabId, { url: d.url, format: fmt.id, source: 'net' });
  } catch (e) { /* ignora */ }
}, { urls: ['<all_urls>'] });

chrome.webRequest.onHeadersReceived.addListener((d) => {
  try {
    if (!d.url || !/^https?:/i.test(d.url)) return;
    const info = requestInfo.get(d.requestId);
    const tabId = (d.tabId != null && d.tabId >= 0) ? d.tabId : (info ? info.tabId : -1);
    if (tabId < 0) return;

    const headers = d.responseHeaders || [];
    let mime = '';
    let size = 0;
    for (const h of headers) {
      const n = String(h.name || '').toLowerCase();
      if (n === 'content-type') mime = String(h.value || '').split(';')[0].trim().toLowerCase();
      else if (n === 'content-length') size = Number(h.value) || 0;
    }

    const fmt = (info && F.detectFormat(info.url, '')) || F.detectFormat(d.url, mime);
    if (!fmt || !S.isFormatEnabled(cfg, fmt.id)) return;

    upsert(tabId, { url: info ? info.url : d.url, mime, size, format: fmt.id, source: 'net' });
  } catch (e) { /* ignora */ }
}, { urls: ['<all_urls>'] }, ['responseHeaders']);

chrome.webRequest.onCompleted.addListener((d) => {
  requestInfo.delete(d.requestId);
  if (requestInfo.size > 800) requestInfo.clear();
}, { urls: ['<all_urls>'] });

chrome.webRequest.onErrorOccurred.addListener((d) => { requestInfo.delete(d.requestId); }, { urls: ['<all_urls>'] });

/* ------------------------------------------------------------------ */
/* Ciclo de vida das abas                                              */
/* ------------------------------------------------------------------ */

chrome.tabs.onRemoved.addListener((tabId) => { tabAssets.delete(tabId); });

chrome.tabs.onUpdated.addListener((tabId, info) => {
  if (info.status !== 'loading') return;
  if (!cfg.keepOnNavigate) {
    tabAssets.delete(tabId);
    autoDone.clear();
  }
  updateBadge(tabId);
  scheduleBroadcast();
});

/* ------------------------------------------------------------------ */
/* Ponte com o content script                                          */
/* ------------------------------------------------------------------ */

function sendToPage(tabId, msg, timeoutMs = 30000) {
  return new Promise((resolve) => {
    let done = false;
    const timer = setTimeout(() => {
      if (done) return;
      done = true;
      resolve({ ok: false, error: 'timeout' });
    }, timeoutMs);

    try {
      chrome.tabs.sendMessage(tabId, Object.assign({ __to: 'page' }, msg), (res) => {
        if (done) return;
        done = true;
        clearTimeout(timer);
        if (chrome.runtime.lastError) resolve({ ok: false, error: chrome.runtime.lastError.message });
        else resolve(res || { ok: false, error: 'sem-resposta' });
      });
    } catch (e) {
      if (done) return;
      done = true;
      clearTimeout(timer);
      resolve({ ok: false, error: String(e) });
    }
  });
}

async function getPageState(tabId) {
  if (tabId == null || tabId < 0) return { assets: [], scene: null };
  const res = await sendToPage(tabId, { cmd: 'state' }, 3000);
  if (!res || !res.ok) return { assets: [], scene: null, error: res && res.error };
  return { assets: res.assets || [], scene: res.scene || null };
}

async function getState(tabId) {
  const net = Array.from((tabAssets.get(tabId) || new Map()).values());
  const page = await getPageState(tabId);

  const byKey = new Map();
  for (const a of net) {
    const k = a.key || F.normalizeUrl(a.url);
    byKey.set(k, Object.assign({}, a, { key: k, inMemory: !!a.inMemory }));
  }
  for (const a of page.assets) {
    const k = a.key || F.normalizeUrl(a.url || '');
    const prev = byKey.get(k) || {};
    byKey.set(k, Object.assign({}, prev, a, {
      key: k,
      inMemory: true,
      source: 'mem',
      mime: a.mime || prev.mime || '',
      size: a.size || prev.size || 0
    }));
  }

  const assets = Array.from(byKey.values()).sort((a, b) => (b.ts || 0) - (a.ts || 0));
  return { ok: true, tabId, assets, scene: page.scene, pageError: page.error || null };
}

async function downloadAsset(tabId, asset) {
  if (!asset) return { ok: false, error: 'asset-ausente' };

  if (asset.inMemory || /^(blob|data):/i.test(asset.url || '')) {
    return sendToPage(tabId, { cmd: 'download', key: asset.key || asset.url });
  }

  const fmt = F.formatById(asset.format);
  const name = F.fileNameFromUrl(asset.url, fmt ? fmt.id : '');
  const parts = [];
  if (cfg.folder) parts.push(S.folderName(cfg.folder));
  const host = F.hostOf(asset.url);
  if (host) parts.push(F.sanitizeName(host));
  parts.push(F.sanitizeName(name));

  try {
    const id = await chrome.downloads.download({
      url: asset.url,
      filename: parts.join('/'),
      saveAs: false,
      conflictAction: 'uniquify'
    });
    return { ok: true, downloadId: id, filename: parts.join('/') };
  } catch (e) {
    return { ok: false, error: String(e && e.message ? e.message : e) };
  }
}

/* ------------------------------------------------------------------ */
/* Mensagens (popup, página e configurações)                           */
/* ------------------------------------------------------------------ */

chrome.runtime.onMessage.addListener((msg, sender, sendResponse) => {
  if (!msg || typeof msg !== 'object') return;

  if (msg.type === 'page-assets') {
    const tabId = sender.tab ? sender.tab.id : -1;
    if (tabId >= 0 && Array.isArray(msg.assets)) {
      for (const a of msg.assets) upsert(tabId, Object.assign({}, a, { inMemory: true, source: 'mem' }));
    }
    sendResponse({ ok: true });
    return false;
  }

  if (msg.type === 'get-config') {
    sendResponse({ ok: true, config: cfg });
    return false;
  }

  if (msg.type === 'save-config') {
    S.save(msg.patch || {}).then((next) => {
      cfg = next;
      refreshBadges();
      sendResponse({ ok: true, config: cfg });
    }, (e) => sendResponse({ ok: false, error: String(e) }));
    return true;
  }

  const tabId = msg.tabId != null ? msg.tabId : (sender.tab ? sender.tab.id : -1);

  if (msg.type === 'state') {
    getState(tabId).then(sendResponse, (e) => sendResponse({ ok: false, error: String(e) }));
    return true;
  }

  if (msg.type === 'download') {
    downloadAsset(tabId, msg.asset).then(sendResponse, (e) => sendResponse({ ok: false, error: String(e) }));
    return true;
  }

  if (msg.type === 'download-all') {
    const list = Array.isArray(msg.assets) ? msg.assets : [];
    (async () => {
      let count = 0;
      for (const item of list) {
        const res = await downloadAsset(tabId, item);
        if (res && res.ok) count++;
        await new Promise((r) => setTimeout(r, 400));
      }
      sendResponse({ ok: true, count, total: list.length });
    })().catch((e) => sendResponse({ ok: false, error: String(e) }));
    return true;
  }

  if (msg.type === 'export') {
    sendToPage(tabId, { cmd: 'export', format: msg.format || 'glb' }, 60000)
      .then(sendResponse, (e) => sendResponse({ ok: false, error: String(e) }));
    return true;
  }

  if (msg.type === 'convert') {
    sendToPage(tabId, { cmd: 'convert', key: msg.key }, 60000)
      .then(sendResponse, (e) => sendResponse({ ok: false, error: String(e) }));
    return true;
  }

  if (msg.type === 'scan') {
    sendToPage(tabId, { cmd: 'scan' }, 4000)
      .then(sendResponse, (e) => sendResponse({ ok: false, error: String(e) }));
    return true;
  }

  if (msg.type === 'clear') {
    tabAssets.delete(tabId);
    updateBadge(tabId);
    sendToPage(tabId, { cmd: 'clear' }, 2000).catch(() => {});
    scheduleBroadcast();
    sendResponse({ ok: true });
    return false;
  }

  if (msg.type === 'clear-all') {
    tabAssets.clear();
    autoDone.clear();
    chrome.tabs.query({}).then((tabs) => {
      for (const t of tabs) {
        updateBadge(t.id);
        sendToPage(t.id, { cmd: 'clear' }, 1500).catch(() => {});
      }
      scheduleBroadcast();
    }).catch(() => {});
    sendResponse({ ok: true });
    return false;
  }

  return false;
});
