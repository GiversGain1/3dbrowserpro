/* 3D Browser Pro — configurações (padrões, validação e persistência) */
(function () {
  'use strict';

  var DEFAULTS = {
    folder: '3D Browser Pro',
    enabledFormats: ['glb', 'gltf', 'obj', 'fbx', 'stl', 'ply'],
    autoFormats: [],
    maxFileMB: 96,
    maxTabMB: 160,
    badge: true,
    keepOnNavigate: false
  };

  var ALL_FORMATS = ['glb', 'gltf', 'obj', 'fbx', 'stl', 'ply', 'dae', '3ds', 'usdz'];

  function isKnown(id) {
    return ALL_FORMATS.indexOf(id) >= 0 || id === 'all';
  }

  function unique(list) {
    var out = [];
    for (var i = 0; i < list.length; i++) {
      if (out.indexOf(list[i]) < 0) out.push(list[i]);
    }
    return out;
  }

  function clamp(value, min, max, fallback) {
    var n = Number(value);
    if (!isFinite(n)) return fallback;
    n = Math.round(n);
    if (n < min) return min;
    if (n > max) return max;
    return n;
  }

  /** Nome de pasta seguro: um único nível, sem caminhos nem "..". */
  function folderName(value) {
    var name = String(value == null ? '' : value)
      .replace(/[\\/:*?"<>|\u0000-\u001f]+/g, ' ')
      .replace(/\.{2,}/g, '')
      .replace(/\s+/g, ' ')
      .trim()
      .replace(/^\.+/, '')
      .trim();
    if (!name) return DEFAULTS.folder;
    return name.slice(0, 60);
  }

  function normalize(raw) {
    var s = Object.assign({}, DEFAULTS, raw || {});

    s.folder = folderName(s.folder);
    s.enabledFormats = unique(Array.isArray(s.enabledFormats) ? s.enabledFormats.filter(isKnown) : DEFAULTS.enabledFormats)
      .filter(function (id) { return id !== 'all'; });
    if (!s.enabledFormats.length) s.enabledFormats = DEFAULTS.enabledFormats.slice();

    s.autoFormats = unique(Array.isArray(s.autoFormats) ? s.autoFormats.filter(isKnown) : []);
    s.maxFileMB = clamp(s.maxFileMB, 8, 1024, DEFAULTS.maxFileMB);
    s.maxTabMB = clamp(s.maxTabMB, 16, 2048, DEFAULTS.maxTabMB);
    s.badge = s.badge !== false;
    s.keepOnNavigate = s.keepOnNavigate === true;

    return s;
  }

  function isFormatEnabled(settings, id) {
    return (settings.enabledFormats || []).indexOf(id) >= 0;
  }

  function isAutoEnabled(settings, id) {
    var list = settings.autoFormats || [];
    return list.indexOf('all') >= 0 || list.indexOf(id) >= 0;
  }

  async function load() {
    try {
      var got = await chrome.storage.local.get('settings');
      return normalize(got && got.settings);
    } catch (e) {
      return normalize(null);
    }
  }

  async function save(patch) {
    var next = normalize(Object.assign({}, await load(), patch || {}));
    try { await chrome.storage.local.set({ settings: next }); } catch (e) { /* modo restrito */ }
    return next;
  }

  var CTX = (typeof globalThis !== 'undefined') ? globalThis : window;
  CTX.BP3D = Object.assign(CTX.BP3D || {}, {
    settings: {
      DEFAULTS: DEFAULTS,
      ALL_FORMATS: ALL_FORMATS,
      normalize: normalize,
      load: load,
      save: save,
      folderName: folderName,
      isFormatEnabled: isFormatEnabled,
      isAutoEnabled: isAutoEnabled
    }
  });
})();
