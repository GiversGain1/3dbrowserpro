/* 3D Browser Pro (app próprio) — ponte página <-> host
 * Roda no mundo da página, ANTES do hook de captura.
 * Reaproveita o hook (que fala window.postMessage) e leva tudo para o host via
 * chrome.webview.postMessage, em blocos base64 (o hook pode mandar dezenas de MB).
 */
(function () {
  'use strict';
  var host = window.chrome && window.chrome.webview;
  if (!host || window.__BP3D_SHIM__) return;
  window.__BP3D_SHIM__ = true;

  var CHUNK = 384 * 1024; // bytes por bloco (~512 KB em base64)

  function toBase64(u8) {
    var s = '';
    var step = 0x8000;
    for (var i = 0; i < u8.length; i += step) {
      s += String.fromCharCode.apply(null, u8.subarray(i, Math.min(i + step, u8.length)));
    }
    return btoa(s);
  }

  function send(obj) {
    try { host.postMessage(JSON.stringify(obj)); } catch (e) { /* host indisponível */ }
  }

  /* meta é um objeto plano; as chaves vão junto com o cabeçalho do bloco */
  function sendBuffer(prefix, id, meta, u8) {
    var total = u8.length;
    var head = { t: prefix + '.begin', id: id, size: total, chunks: Math.max(1, Math.ceil(total / CHUNK)) };
    for (var k in meta) if (Object.prototype.hasOwnProperty.call(meta, k)) head[k] = meta[k];
    send(head);
    for (var off = 0, i = 0; off < total; off += CHUNK, i++) {
      send({ t: prefix + '.chunk', id: id, i: i, data: toBase64(u8.subarray(off, Math.min(off + CHUNK, total))) });
    }
    send({ t: prefix + '.end', id: id });
  }

  /* página -> host */
  window.addEventListener('message', function (ev) {
    if (ev.source !== window) return;
    var d = ev.data;
    if (!d || typeof d !== 'object' || d.__bp3d !== 1 || d.dir !== 'page') return;

    if (d.kind === 'bytes' && d.buffer) {
      sendBuffer('bytes', 'b' + Date.now().toString(36) + Math.random().toString(36).slice(2, 7), {
        url: d.url || '',
        mime: d.mime || '',
        format: d.format || ''
      }, new Uint8Array(d.buffer));
      return;
    }

    if (d.kind === 'notice') {
      send({ t: 'notice', message: d.message || '' });
      return;
    }

    if (d.reply && d.id) {
      var meta = {
        rid: d.id,
        ok: d.ok !== false,
        error: d.error || '',
        name: d.name || '',
        meshes: d.meshes || 0,
        triangles: d.triangles || 0,
        info: d.info || null
      };
      if (d.buffer) sendBuffer('reply', 'r' + d.id, meta, new Uint8Array(d.buffer));
      else {
        meta.t = 'reply.meta';
        meta.size = 0;
        meta.chunks = 0;
        send(meta);
      }
      return;
    }
  }, true);

  /* host -> página: entrega como se fosse window.postMessage (o hook escuta assim) */
  host.addEventListener('message', function (e) {
    var d = e.data;
    if (typeof d === 'string') {
      try { d = JSON.parse(d); } catch (err) { return; }
    }
    if (!d || typeof d !== 'object' || d.dir !== 'ext') return;
    /* o hook só aceita mensagens marcadas; sem isso ele ignora todo comando do host */
    if (d.__bp3d !== 1) d.__bp3d = 1;
    try {
      window.dispatchEvent(new MessageEvent('message', { source: window, data: d }));
    } catch (err) {
      var evt = document.createEvent('MessageEvent');
      evt.initMessageEvent('message', false, false, d, location.origin, '', window, null);
      window.dispatchEvent(evt);
    }
  });

  send({ t: 'ready', href: location.href });
})();
