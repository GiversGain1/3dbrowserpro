/* 3D Browser Pro — ponte (mundo isolado da extensão) */
(function () {
  'use strict';
  if (window.__BP3D_BRIDGE__) return;
  window.__BP3D_BRIDGE__ = true;

  const F = globalThis.BP3D;
  const S = (globalThis.BP3D && globalThis.BP3D.settings) || null;

  let settings = S ? S.normalize(null) : { enabledFormats: [], maxFileMB: 96, maxTabMB: 160 };
  let maxTotal = 160 * 1024 * 1024;      // teto de bytes guardados por aba (configurável)

  const assets = new Map();              // key -> asset com bytes
  let totalBytes = 0;
  let reportTimer = null;
  const pending = new Map();
  let rpcSeq = 0;

  function applySettings(raw) {
    if (S) settings = S.normalize(raw);
    const mb = Number(settings.maxTabMB) || 160;
    maxTotal = Math.max(16, mb) * 1024 * 1024;
    try {
      window.postMessage({
        __bp3d: 1,
        dir: 'ext',
        cmd: 'config',
        config: { maxCapture: Math.max(8, Number(settings.maxFileMB) || 96) * 1024 * 1024 }
      }, '*');
    } catch (e) { /* ignora */ }
  }

  try {
    chrome.runtime.sendMessage({ type: 'get-config' }).then((res) => {
      if (res && res.ok && res.config) applySettings(res.config);
    }).catch(() => {});
  } catch (e) { /* contexto invalidado */ }

  /* --------------------------------------------------------------- */
  /* Comunicação com o hook (mundo da página)                        */
  /* --------------------------------------------------------------- */

  function rpc(cmd, extra, timeoutMs = 45000) {
    return new Promise((resolve) => {
      const id = 'rpc' + (++rpcSeq);
      const timer = setTimeout(() => {
        pending.delete(id);
        resolve({ ok: false, error: 'timeout' });
      }, timeoutMs);
      pending.set(id, (data) => { clearTimeout(timer); resolve(data); });
      window.postMessage(Object.assign({ __bp3d: 1, dir: 'ext', id, cmd }, extra || {}), '*');
    });
  }

  window.addEventListener('message', (ev) => {
    if (ev.source !== window) return;
    const d = ev.data;
    if (!d || typeof d !== 'object' || d.__bp3d !== 1 || d.dir !== 'page') return;

    if (d.reply && d.id && pending.has(d.id)) {
      const fn = pending.get(d.id);
      pending.delete(d.id);
      fn(d);
      return;
    }
    if (d.kind === 'bytes') {
      storeBytes(d);
      return;
    }
    if (d.kind === 'notice') {
      notify(d.message || 'Aviso da captura');
    }
  });

  function notify(text) {
    try {
      if (chrome.runtime && chrome.runtime.sendMessage) {
        chrome.runtime.sendMessage({ type: 'toast', text }).catch(() => {});
      }
    } catch (e) { /* ignora */ }
  }

  function storeBytes(d) {
    const buf = d.buffer;
    if (!buf || !buf.byteLength) return;
    const fmt = (d.format && F.formatById(d.format)) || F.detectFormat(d.url || '', d.mime || '');
    if (!fmt) return;
    if (settings.enabledFormats && settings.enabledFormats.indexOf(fmt.id) < 0) return;

    const key = 'mem:' + F.normalizeUrl(d.url || ('blob-' + Date.now()));
    const prev = assets.get(key);
    if (prev) totalBytes -= prev.bytes ? prev.bytes.byteLength : 0;

    assets.set(key, {
      key,
      url: d.url || '',
      mime: d.mime || '',
      format: fmt.id,
      size: buf.byteLength,
      bytes: buf,
      ts: Date.now(),
      name: F.fileNameFromUrl(d.url || '', fmt.id)
    });
    totalBytes += buf.byteLength;

    // descarta os mais antigos se passar do teto
    while (totalBytes > maxTotal && assets.size > 1) {
      const oldest = assets.keys().next().value;
      if (oldest === key) break;
      const gone = assets.get(oldest);
      totalBytes -= gone.bytes ? gone.bytes.byteLength : 0;
      assets.delete(oldest);
    }
    scheduleReport();
  }

  function scheduleReport() {
    if (reportTimer) return;
    reportTimer = setTimeout(() => {
      reportTimer = null;
      try {
        const list = Array.from(assets.values()).map((a) => ({
          key: a.key,
          url: a.url,
          name: a.name,
          format: a.format,
          mime: a.mime,
          size: a.size,
          ts: a.ts,
          inMemory: true
        }));
        chrome.runtime.sendMessage({ type: 'page-assets', assets: list }).catch(() => {});
      } catch (e) { /* contexto invalidado */ }
    }, 500);
  }

  /* --------------------------------------------------------------- */
  /* Download no contexto da página                                  */
  /* --------------------------------------------------------------- */

  function save(bytes, mime, name) {
    const blob = bytes instanceof Blob ? bytes : new Blob([bytes], { type: mime || 'application/octet-stream' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = name;
    a.rel = 'noopener';
    a.style.display = 'none';
    (document.body || document.documentElement).appendChild(a);
    a.click();
    setTimeout(() => { a.remove(); URL.revokeObjectURL(url); }, 90000);
    return name;
  }

  /* --------------------------------------------------------------- */
  /* GLB -> glTF autocontido (JSON + buffer em base64)               */
  /* --------------------------------------------------------------- */

  function bytesToBase64(buffer) {
    const bytes = new Uint8Array(buffer);
    let binary = '';
    const step = 0x8000;
    for (let i = 0; i < bytes.length; i += step) {
      binary += String.fromCharCode.apply(null, bytes.subarray(i, i + step));
    }
    return btoa(binary);
  }

  function glbToGltfJSON(buffer) {
    const dv = new DataView(buffer);
    if (dv.getUint32(0, true) !== 0x46546c67) throw new Error('Arquivo GLB inválido');
    const version = dv.getUint32(4, true);
    if (version !== 2) throw new Error('Só GLB versão 2 é suportado');

    let offset = 12;
    let json = null;
    let bin = null;

    while (offset + 8 <= buffer.byteLength) {
      const len = dv.getUint32(offset, true);
      const type = dv.getUint32(offset + 4, true);
      const start = offset + 8;
      if (start + len > buffer.byteLength) break;
      if (type === 0x4e4f534a) {
        json = JSON.parse(new TextDecoder().decode(new Uint8Array(buffer, start, len)));
      } else if (type === 0x004e4942) {
        bin = buffer.slice(start, start + len);
      }
      offset = start + len + ((4 - (len % 4)) % 4);
    }

    if (!json) throw new Error('Chunk JSON do GLB não encontrado');

    if (bin && json.buffers && json.buffers.length) {
      json.buffers = json.buffers.map((b, i) => {
        if (i !== 0 || b.uri) return b;
        return Object.assign({}, b, {
          uri: 'data:application/octet-stream;base64,' + bytesToBase64(bin)
        });
      });
    } else if (bin && (!json.buffers || !json.buffers.length)) {
      json.buffers = [{ byteLength: bin.byteLength, uri: 'data:application/octet-stream;base64,' + bytesToBase64(bin) }];
    }

    return JSON.stringify(json, null, 1);
  }

  /* --------------------------------------------------------------- */
  /* Comandos vindos do service worker                               */
  /* --------------------------------------------------------------- */

  function statePayload() {
    const list = Array.from(assets.values()).map((a) => ({
      key: a.key,
      url: a.url,
      name: a.name,
      format: a.format,
      mime: a.mime,
      size: a.size,
      ts: a.ts,
      inMemory: true
    }));
    return { ok: true, assets: list, totalBytes };
  }

  chrome.runtime.onMessage.addListener((msg, sender, sendResponse) => {
    if (!msg || msg.__to !== 'page') return;

    if (msg.cmd === 'state') {
      // pede também o estado da cena (assíncrono, mas o popup não pode esperar muito)
      rpc('scene-info', {}, 1500).then((info) => {
        const payload = statePayload();
        payload.scene = info && info.ok ? info.info : null;
        sendResponse(payload);
      });
      return true;
    }

    if (msg.cmd === 'download') {
      const a = assets.get(msg.key);
      if (!a) { sendResponse({ ok: false, error: 'não está na memória' }); return false; }
      const fmt = F.formatById(a.format);
      const name = a.name || F.fileNameFromUrl(a.url, fmt ? fmt.id : '');
      save(a.bytes, a.mime || (fmt ? 'model/' + fmt.exts[0] : ''), name);
      sendResponse({ ok: true, name });
      return false;
    }

    if (msg.cmd === 'convert') {
      const a = assets.get(msg.key);
      if (!a) { sendResponse({ ok: false, error: 'não está na memória' }); return false; }
      if (a.format !== 'glb' && a.format !== 'gltf') {
        sendResponse({ ok: false, error: 'conversão disponível apenas para GLB/glTF' });
        return false;
      }
      try {
        const json = glbToGltfJSON(a.bytes);
        const base = (a.name || 'modelo').replace(/\.(glb|gltf)$/i, '');
        save(new Blob([json], { type: 'model/gltf+json' }), 'model/gltf+json', base + '.gltf');
        sendResponse({ ok: true, name: base + '.gltf' });
      } catch (e) {
        sendResponse({ ok: false, error: String(e && e.message ? e.message : e) });
      }
      return false;
    }

    if (msg.cmd === 'export') {
      const format = msg.format === 'obj' ? 'obj' : 'glb';
      rpc('export', { format }, 55000).then((res) => {
        if (!res || !res.ok) {
          sendResponse({ ok: false, error: (res && res.error) || 'nada para exportar' });
          return;
        }
        const mime = format === 'obj' ? 'text/plain' : 'model/gltf-binary';
        const name = (res.name || 'cena') + '.' + format;
        save(res.buffer, mime, name);
        sendResponse({ ok: true, name, meshes: res.meshes, triangles: res.triangles });
      });
      return true;
    }

    if (msg.cmd === 'scan') {
      rpc('scene-info', {}, 3000).then((info) => {
        sendResponse({ ok: true, scene: info && info.ok ? info.info : null, assets: statePayload().assets });
      });
      return true;
    }

    if (msg.cmd === 'clear') {
      assets.clear();
      totalBytes = 0;
      sendResponse({ ok: true });
      return false;
    }

    return false;
  });

  /* --------------------------------------------------------------- */
  /* Anúncio inicial                                                 */
  /* --------------------------------------------------------------- */

  window.addEventListener('DOMContentLoaded', () => {
    rpc('scene-info', {}, 2000).then((info) => {
      if (info && info.ok && info.info.hasCanvas) {
        notify('Página com 3D detectada. Dê play no modelo e abra o 3D Browser Pro.');
      }
    });
  }, { once: true });
})();
