/* 3D Browser Pro — lógica do painel */
'use strict';

const el = (id) => document.getElementById(id);

const FILTERS = [
  { id: 'all', label: 'Todos' },
  { id: 'glb', label: 'GLB' },
  { id: 'gltf', label: 'glTF' },
  { id: 'obj', label: 'OBJ' },
  { id: 'fbx', label: 'FBX' },
  { id: 'other', label: 'Outros' }
];

let tab = null;
let assets = [];
let scene = null;
let filter = 'all';
let query = '';

function fmtMeta(id) {
  const list = {
    glb: { label: 'GLB', color: '#38bdf8' },
    gltf: { label: 'glTF', color: '#22d3ee' },
    obj: { label: 'OBJ', color: '#a78bfa' },
    fbx: { label: 'FBX', color: '#f472b6' },
    stl: { label: 'STL', color: '#facc15' },
    ply: { label: 'PLY', color: '#4ade80' },
    dae: { label: 'DAE', color: '#fb923c' },
    '3ds': { label: '3DS', color: '#94a3b8' },
    usdz: { label: 'USDZ', color: '#e879f9' }
  };
  return list[id] || { label: (id || '?').slice(0, 4), color: '#94a3b8' };
}

function prettySize(bytes) {
  const b = Number(bytes) || 0;
  if (b <= 0) return null;
  if (b < 1024) return b + ' B';
  if (b < 1048576) return (b / 1024).toFixed(1).replace('.', ',') + ' KB';
  return (b / 1048576).toFixed(1).replace('.', ',') + ' MB';
}

function hostOf(url) {
  try { return new URL(url).hostname.replace(/^www\./, ''); }
  catch (e) { return ''; }
}

function fileNameOf(asset) {
  if (asset.name) return asset.name;
  try {
    const u = new URL(asset.url);
    const parts = u.pathname.split('/').filter(Boolean);
    const last = parts.length ? decodeURIComponent(parts[parts.length - 1]) : '';
    if (last) return last;
  } catch (e) { /* usa fallback */ }
  const m = fmtMeta(asset.format);
  return (hostOf(asset.url) || 'modelo') + '_' + String(asset.key).slice(-6) + '.' + m.label.toLowerCase();
}

/* ------------------------------------------------------------------ */
/* Render                                                              */
/* ------------------------------------------------------------------ */

function renderFilters() {
  const box = el('filters');
  box.innerHTML = '';
  FILTERS.forEach((f) => {
    const b = document.createElement('button');
    b.className = 'chip-filter' + (filter === f.id ? ' on' : '');
    b.textContent = f.label;
    b.addEventListener('click', () => {
      filter = f.id;
      renderFilters();
      renderList();
    });
    box.appendChild(b);
  });
}

function visibleAssets() {
  return assets.filter((a) => {
    if (filter !== 'all') {
      if (filter === 'other') {
        if (['glb', 'gltf', 'obj', 'fbx'].includes(a.format)) return false;
      } else if (a.format !== filter) {
        return false;
      }
    }
    if (!query) return true;
    const hay = (fileNameOf(a) + ' ' + (a.url || '')).toLowerCase();
    return hay.includes(query);
  });
}

function renderList() {
  const list = el('list');
  const items = visibleAssets();
  list.innerHTML = '';

  el('empty').classList.toggle('hidden', items.length > 0 || assets.length > 0 && false);

  if (!items.length) {
    if (assets.length) {
      const p = document.createElement('p');
      p.className = 'muted';
      p.style.padding = '10px 2px';
      p.textContent = 'Nenhum arquivo com esse filtro.';
      list.appendChild(p);
    }
    el('empty').classList.toggle('hidden', assets.length > 0);
    return;
  }

  items.forEach((a) => {
    const m = fmtMeta(a.format);

    const row = document.createElement('div');
    row.className = 'row';

    const fmtEl = document.createElement('span');
    fmtEl.className = 'fmt';
    fmtEl.textContent = m.label;
    fmtEl.style.background = m.color;

    const meta = document.createElement('div');
    meta.className = 'meta';

    const name = document.createElement('div');
    name.className = 'name';
    name.textContent = fileNameOf(a);
    name.title = a.url || '';

    const sub = document.createElement('div');
    sub.className = 'sub';
    const bits = [];
    if (hostOf(a.url)) bits.push(hostOf(a.url));
    const size = prettySize(a.size);
    if (size) bits.push(size);
    sub.textContent = bits.join(' · ') || 'capturado';

    if (a.inMemory) {
      const tag = document.createElement('span');
      tag.className = 'mem';
      tag.textContent = ' · memória';
      sub.appendChild(tag);
    }

    meta.appendChild(name);
    meta.appendChild(sub);

    const actions = document.createElement('div');
    actions.className = 'actions';

    const dl = document.createElement('button');
    dl.className = 'btn primary';
    dl.textContent = 'Baixar';
    dl.addEventListener('click', () => doDownload(a, dl));
    actions.appendChild(dl);

    if (a.format === 'glb' && a.inMemory) {
      const conv = document.createElement('button');
      conv.className = 'btn';
      conv.textContent = '.gltf';
      conv.title = 'Converter para glTF autocontido';
      conv.addEventListener('click', () => doConvert(a, conv));
      actions.appendChild(conv);
    }

    if (/^https?:/i.test(a.url || '')) {
      const copy = document.createElement('button');
      copy.className = 'btn ghost';
      copy.textContent = '🔗';
      copy.title = 'Copiar o link do arquivo';
      copy.addEventListener('click', () => doCopyLink(a, copy));
      actions.appendChild(copy);
    }

    row.appendChild(fmtEl);
    row.appendChild(meta);
    row.appendChild(actions);
    list.appendChild(row);
  });
}

function renderScene() {
  const box = el('scene');
  if (!scene) { box.classList.add('hidden'); return; }

  const has = !!scene.hasScene;
  box.classList.toggle('hidden', !has && !scene.hasCanvas);

  const meta = el('scene-meta');
  const hint = el('scene-hint');
  const buttons = box.querySelectorAll('[data-export]');

  if (has) {
    const tris = Number(scene.triangles) || 0;
    meta.textContent = scene.meshes + (scene.meshes === 1 ? ' malha' : ' malhas') +
      ' · ' + tris.toLocaleString('pt-BR') + ' triângulos';
    hint.textContent = 'Geometria lida da cena em memória. Texturas não são incluídas.';
    buttons.forEach((b) => { b.disabled = false; b.classList.remove('hidden'); });
  } else {
    meta.textContent = 'canvas WebGL ativo';
    hint.textContent = 'A cena não foi exposta pelo site. Use os arquivos capturados na lista abaixo.';
    buttons.forEach((b) => b.classList.add('hidden'));
  }
}

function setStatus(text) {
  el('status').textContent = text || '—';
}

function setSite() {
  const site = el('site');
  if (!tab) { site.textContent = 'sem aba ativa'; return; }
  if (!tab.url || !/^https?:/i.test(tab.url)) {
    site.textContent = 'abra um site com 3D nesta aba';
    return;
  }
  site.textContent = hostOf(tab.url);
}

/* ------------------------------------------------------------------ */
/* Ações                                                               */
/* ------------------------------------------------------------------ */

async function load(spin) {
  if (spin) el('refresh').classList.add('spin');
  setStatus('lendo…');

  try {
    const tabs = await chrome.tabs.query({ active: true, currentWindow: true });
    tab = tabs && tabs[0] ? tabs[0] : null;
    setSite();

    if (!tab || tab.id == null) {
      assets = [];
      scene = null;
      renderList();
      renderScene();
      setStatus('sem aba ativa');
      return;
    }

    const res = await chrome.runtime.sendMessage({ type: 'state', tabId: tab.id });
    if (!res || !res.ok) throw new Error((res && res.error) || 'falha ao ler estado');

    assets = res.assets || [];
    scene = res.scene || null;

    renderList();
    renderScene();

    const totalMem = assets.filter((a) => a.inMemory).length;
    const total = assets.length;
    setStatus(total ? `${total} arquivo${total > 1 ? 's' : ''} · ${totalMem} na memória` : 'nenhum arquivo capturado');
  } catch (e) {
    setStatus('erro: ' + (e && e.message ? e.message : e));
  } finally {
    if (spin) setTimeout(() => el('refresh').classList.remove('spin'), 400);
  }
}

async function doDownload(asset, button) {
  const label = button.textContent;
  button.disabled = true;
  button.textContent = '…';
  try {
    const res = await chrome.runtime.sendMessage({ type: 'download', tabId: tab.id, asset });
    if (!res || !res.ok) {
      setStatus('não foi possível baixar: ' + ((res && res.error) || 'erro'));
      button.textContent = label;
      button.disabled = false;
      return;
    }
    setStatus('baixando ' + (res.name || res.filename || 'arquivo'));
    setTimeout(() => load(false), 600);
  } catch (e) {
    setStatus('erro: ' + e.message);
    button.textContent = label;
    button.disabled = false;
  }
}

async function doConvert(asset, button) {
  button.disabled = true;
  button.textContent = '…';
  try {
    const res = await chrome.runtime.sendMessage({ type: 'convert', tabId: tab.id, key: asset.key });
    setStatus(res && res.ok ? 'gerado ' + res.name : 'conversão falhou: ' + ((res && res.error) || ''));
  } catch (e) {
    setStatus('erro: ' + e.message);
  } finally {
    button.disabled = false;
    button.textContent = '.gltf';
  }
}

async function doCopyLink(asset, button) {
  try {
    await navigator.clipboard.writeText(asset.url);
    const original = button.textContent;
    button.textContent = '✓';
    setTimeout(() => { button.textContent = original; }, 1200);
    setStatus('link copiado');
  } catch (e) {
    setStatus('não foi possível copiar o link');
  }
}

async function doDownloadAll(button) {
  const items = visibleAssets();
  if (!items.length) { setStatus('nada para baixar'); return; }

  const label = button.textContent;
  button.disabled = true;
  button.textContent = 'enviando…';
  setStatus('iniciando ' + items.length + ' download(s)…');
  try {
    const res = await chrome.runtime.sendMessage({ type: 'download-all', tabId: tab.id, assets: items });
    if (res && res.ok) setStatus(res.count + ' de ' + res.total + ' download(s) iniciado(s)');
    else setStatus('falha ao baixar tudo: ' + ((res && res.error) || 'erro'));
  } catch (e) {
    setStatus('erro: ' + e.message);
  } finally {
    button.disabled = false;
    button.textContent = label;
  }
}

async function doExport(format, button) {
  const original = button.textContent;
  button.disabled = true;
  button.textContent = 'exportando…';
  setStatus('lendo geometria da GPU…');
  try {
    const res = await chrome.runtime.sendMessage({ type: 'export', tabId: tab.id, format });
    if (!res || !res.ok) {
      setStatus('exportação falhou: ' + ((res && res.error) || 'erro'));
    } else {
      setStatus(`salvo ${res.name} · ${(res.triangles || 0).toLocaleString('pt-BR')} triângulos`);
    }
  } catch (e) {
    setStatus('erro: ' + e.message);
  } finally {
    button.disabled = false;
    button.textContent = original;
  }
}

async function clearAll() {
  if (!tab) return;
  await chrome.runtime.sendMessage({ type: 'clear', tabId: tab.id });
  assets = [];
  renderList();
  setStatus('lista limpa');
}

/* ------------------------------------------------------------------ */
/* Boot                                                                */
/* ------------------------------------------------------------------ */

renderFilters();

el('search').addEventListener('input', (e) => {
  query = e.target.value.trim().toLowerCase();
  renderList();
});

el('refresh').addEventListener('click', () => load(true));
el('clear').addEventListener('click', clearAll);

el('settings').addEventListener('click', () => {
  try { chrome.runtime.openOptionsPage(); } catch (e) { /* ignora */ }
});

el('folder').addEventListener('click', () => {
  try { chrome.downloads.showDefaultFolder(); } catch (e) { setStatus('pasta indisponível'); }
});

el('download-all').addEventListener('click', (e) => doDownloadAll(e.currentTarget));

document.querySelectorAll('[data-export]').forEach((b) => {
  b.addEventListener('click', () => doExport(b.dataset.export, b));
});

chrome.runtime.onMessage.addListener((msg) => {
  if (msg && msg.type === 'assets-updated') load(false);
});

load(false);
