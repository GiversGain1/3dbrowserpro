/* 3D Browser Pro — metadados de formatos 3D (compartilhado entre serviço e página) */
(function () {
  'use strict';

  var FORMATS = [
    { id: 'glb', label: 'GLB', exts: ['glb'], mimes: ['model/gltf-binary'], color: '#38bdf8' },
    { id: 'gltf', label: 'glTF', exts: ['gltf'], mimes: ['model/gltf+json'], color: '#22d3ee' },
    { id: 'obj', label: 'OBJ', exts: ['obj'], mimes: [], color: '#a78bfa' },
    { id: 'fbx', label: 'FBX', exts: ['fbx'], mimes: ['application/x-fbx'], color: '#f472b6' },
    { id: 'stl', label: 'STL', exts: ['stl'], mimes: ['model/stl'], color: '#facc15' },
    { id: 'ply', label: 'PLY', exts: ['ply'], mimes: ['model/ply'], color: '#4ade80' },
    { id: 'dae', label: 'DAE', exts: ['dae'], mimes: ['model/vnd.collada+xml'], color: '#fb923c' },
    { id: '3ds', label: '3DS', exts: ['3ds'], mimes: [], color: '#94a3b8' },
    { id: 'usdz', label: 'USDZ', exts: ['usdz', 'usd', 'usda', 'usdc'], mimes: ['model/vnd.usdz+zip'], color: '#e879f9' }
  ];

  var BY_EXT = {};
  var BY_ID = {};
  FORMATS.forEach(function (f) {
    BY_ID[f.id] = f;
    f.exts.forEach(function (e) { BY_EXT[e] = f; });
  });

  var EXT_LOOKUP = Object.keys(BY_EXT);

  function extOf(url) {
    try {
      var u = new URL(String(url), (typeof location !== 'undefined' ? location.href : 'https://x/'));
      var m = /\.([a-z0-9]{2,5})$/i.exec(u.pathname);
      return m ? m[1].toLowerCase() : '';
    } catch (e) {
      return '';
    }
  }

  /** Detecta o formato a partir da URL (prioritário) e, se preciso, do MIME. */
  function detectFormat(url, mime) {
    var ext = extOf(url || '');
    if (BY_EXT[ext]) return BY_EXT[ext];

    var m = String(mime || '').toLowerCase().split(';')[0].trim();
    if (!m) return null;
    for (var i = 0; i < FORMATS.length; i++) {
      if (FORMATS[i].mimes.indexOf(m) >= 0) return FORMATS[i];
    }
    if (m === 'model/gltf-binary') return BY_ID.glb;
    if (m === 'model/gltf+json') return BY_ID.gltf;
    return null;
  }

  function formatById(id) {
    return BY_ID[id] || null;
  }

  function formatOfExt(ext) {
    return BY_EXT[String(ext || '').toLowerCase()] || null;
  }

  function prettySize(bytes) {
    var b = Number(bytes) || 0;
    if (b <= 0) return '—';
    if (b < 1024) return b + ' B';
    if (b < 1024 * 1024) return (b / 1024).toFixed(1).replace('.', ',') + ' KB';
    return (b / (1024 * 1024)).toFixed(1).replace('.', ',') + ' MB';
  }

  function hostOf(url) {
    try { return new URL(String(url)).hostname.replace(/^www\./, ''); }
    catch (e) { return 'site'; }
  }

  function baseNameOf(url) {
    try {
      var u = new URL(String(url));
      var parts = u.pathname.split('/').filter(Boolean);
      var last = parts.length ? parts[parts.length - 1] : '';
      if (!last) return '';
      try { last = decodeURIComponent(last); } catch (e) {}
      return last;
    } catch (e) {
      return '';
    }
  }

  /** Nome de arquivo seguro, preservando a extensão correta do formato. */
  function fileNameFromUrl(url, formatId) {
    var name = baseNameOf(url).replace(/[\\/:*?"<>|\u0000-\u001f]+/g, '_');
    var fmt = BY_ID[formatId] || null;
    var ext = extOf(url);
    if (!/\.[a-z0-9]{2,5}$/i.test(name)) {
      name = (hostOf(url) || 'modelo') + '_' + Date.now().toString(36);
      name += '.' + (ext || (fmt ? fmt.exts[0] : 'bin'));
    }
    return name.slice(-120);
  }

  function sanitizeName(name) {
    return String(name || '')
      .replace(/[\\/:*?"<>|\u0000-\u001f]+/g, '_')
      .replace(/^\.+/, '')
      .slice(0, 80) || 'arquivo';
  }

  function normalizeUrl(url) {
    try {
      var u = new URL(String(url));
      u.hash = '';
      return u.href;
    } catch (e) {
      return String(url || '');
    }
  }

  var CTX = (typeof globalThis !== 'undefined') ? globalThis : window;
  CTX.BP3D = Object.assign(CTX.BP3D || {}, {
    formats: FORMATS,
    extLookup: EXT_LOOKUP,
    detectFormat: detectFormat,
    formatById: formatById,
    formatOfExt: formatOfExt,
    prettySize: prettySize,
    hostOf: hostOf,
    baseNameOf: baseNameOf,
    fileNameFromUrl: fileNameFromUrl,
    sanitizeName: sanitizeName,
    normalizeUrl: normalizeUrl
  });
})();
