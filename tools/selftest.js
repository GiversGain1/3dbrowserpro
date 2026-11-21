/* 3D Browser Pro — harness de autoteste
 * Executado dentro de tools/_selfcheck.html (gerado por script), onde os
 * fontes ficam embutidos em <script type="text/plain">. Não usa Node.
 */
'use strict';

window.__runAll = async function () {
  const R = { checks: {}, glb: {}, obj: {}, bridge: {}, icons: {}, log: [] };
  const out = document.getElementById('out');
  const src = (id) => {
    const el = document.getElementById(id);
    return el ? el.textContent : '';
  };

  function ok(name, cond, extra) {
    R.checks[name] = cond ? true : ('FALHOU: ' + (extra || ''));
    return !!cond;
  }
  function fail(name, msg) {
    R.checks[name] = 'FALHOU: ' + msg;
    return false;
  }
  function note(msg) { R.log.push(msg); }

  /* ---------------------------------------------------------------- */
  /* 1. Sintaxe                                                       */
  /* ---------------------------------------------------------------- */

  const classic = ['formats', 'settings', 'hook', 'bridge', 'popup', 'options'];
  for (const name of classic) {
    try {
      new Function(src('f-' + name));
      R.checks['sintaxe:' + name] = true;
    } catch (e) {
      fail('sintaxe:' + name, e.message);
    }
  }

  try {
    const url = URL.createObjectURL(new Blob([src('f-background')], { type: 'text/javascript' }));
    await import(url);
    R.checks['sintaxe:background'] = true;
  } catch (e) {
    // execução falha no navegador (sem chrome.webRequest); import interessa só o parse
    if (e && e.name === 'SyntaxError') fail('sintaxe:background', e.message);
    else R.checks['sintaxe:background'] = 'parse-ok (execução só no service worker)';
  }

  /* ---------------------------------------------------------------- */
  /* 2. Executa formatos, hook e bridge                               */
  /* ---------------------------------------------------------------- */

  function runScript(code, label) {
    const s = document.createElement('script');
    s.textContent = code;
    try {
      document.body.appendChild(s);
      return true;
    } catch (e) {
      fail('execução:' + label, e.message);
      return false;
    }
  }

  runScript(src('f-formats'), 'formats');
  runScript(src('f-settings'), 'settings');
  runScript(src('f-hook'), 'hook');

  // shim mínimo de chrome para a bridge
  const captured = { blobs: [] };
  window.chrome = {
    runtime: {
      onMessage: { addListener(fn) { window.__bridgeHandler = fn; } },
      sendMessage() { return Promise.resolve({ ok: true }); },
      lastError: null
    }
  };

  const origCreate = URL.createObjectURL;
  const origClick = HTMLAnchorElement.prototype.click;
  URL.createObjectURL = function (blob) { captured.blobs.push(blob); return 'blob:fake'; };
  HTMLAnchorElement.prototype.click = function () {};

  runScript(src('f-bridge'), 'bridge');

  ok('ponte:registrou-handler', typeof window.__bridgeHandler === 'function');

  /* ---------------------------------------------------------------- */
  /* 2b. Configurações                                                */
  /* ---------------------------------------------------------------- */

  const ST = globalThis.BP3D.settings;
  ok('config:exposto', !!(ST && typeof ST.normalize === 'function'));
  const padroes = ST.normalize(null);
  ok('config:padroes', padroes.folder === '3D Browser Pro' &&
    padroes.enabledFormats.indexOf('glb') >= 0 && padroes.maxFileMB === 96, JSON.stringify(padroes));
  ok('config:saneia-pasta', ST.folderName('../../etc/passwd').indexOf('..') < 0 &&
    ST.folderName('') === '3D Browser Pro', ST.folderName('../../etc/passwd'));
  ok('config:limites', ST.normalize({ maxFileMB: 99999 }).maxFileMB === 1024 &&
    ST.normalize({ maxTabMB: 1 }).maxTabMB === 16);
  ok('config:auto', ST.isAutoEnabled(ST.normalize({ autoFormats: ['all'] }), 'fbx') === true &&
    ST.isAutoEnabled(ST.normalize({ autoFormats: [] }), 'fbx') === false);
  ok('config:formato', ST.isFormatEnabled(ST.normalize({ enabledFormats: ['glb'] }), 'fbx') === false);

  /* ---------------------------------------------------------------- */
  /* 3. Cena falsa compatível com THREE                               */
  /* ---------------------------------------------------------------- */

  function attr(values, size) {
    const arr = new Float32Array(values);
    return {
      count: values.length / size,
      itemSize: size,
      array: arr,
      isInterleavedBufferAttribute: false,
      getX: (i) => arr[i * size],
      getY: (i) => arr[i * size + 1],
      getZ: (i) => arr[i * size + 2],
      getW: () => 1
    };
  }

  const identityAt = (tx, ty, tz) => ({ elements: [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, tx, ty, tz, 1] });

  const quad = {
    isObject3D: true, type: 'Mesh', name: 'QuadTeste', isMesh: true,
    matrixAutoUpdate: true,
    position: { x: 0, y: 1, z: 0 },
    quaternion: { x: 0, y: 0, z: 0, w: 1 },
    scale: { x: 1, y: 1, z: 1 },
    matrix: { elements: [] },
    matrixWorld: identityAt(0, 1, 0),
    geometry: {
      attributes: {
        position: attr([0, 0, 0, 1, 0, 0, 1, 1, 0, 0, 1, 0], 3),
        normal: attr([0, 0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 1], 3),
        uv: attr([0, 0, 1, 0, 1, 1, 0, 1], 2)
      },
      index: { count: 6, array: new Uint16Array([0, 1, 2, 0, 2, 3]) },
      groups: []
    },
    material: { name: 'MatTeste', color: { r: 1, g: 0.5, b: 0 }, opacity: 1, roughness: 0.5, metalness: 0.1, side: 0 },
    children: []
  };

  const group = {
    isObject3D: true, type: 'Group', name: 'GrupoTeste',
    matrixAutoUpdate: true,
    position: { x: 2, y: 0, z: 0 },
    quaternion: { x: 0, y: 0, z: 0, w: 1 },
    scale: { x: 1, y: 1, z: 1 },
    matrix: { elements: [] },
    matrixWorld: identityAt(2, 0, 0),
    children: [quad]
  };

  const scene = {
    isScene: true, isObject3D: true, name: 'CenaTeste',
    matrixAutoUpdate: true,
    position: { x: 0, y: 0, z: 0 },
    quaternion: { x: 0, y: 0, z: 0, w: 1 },
    scale: { x: 1, y: 1, z: 1 },
    matrix: { elements: [] },
    matrixWorld: identityAt(0, 0, 0),
    children: [group],
    updateMatrixWorld() {},
    traverse(cb) {
      const walk = (n) => { cb(n); (n.children || []).forEach(walk); };
      walk(this);
    }
  };

  window.scene = scene;
  document.title = 'Site Modelo 3D';

  /* ---------------------------------------------------------------- */
  /* 4. RPC com o hook                                                */
  /* ---------------------------------------------------------------- */

  function rpc(cmd, extra, timeout) {
    return new Promise((resolve) => {
      const id = 't' + Math.random().toString(36).slice(2);
      let done = false;
      const h = (ev) => {
        const d = ev.data;
        if (d && d.__bp3d === 1 && d.dir === 'page' && d.reply && d.id === id) {
          done = true;
          window.removeEventListener('message', h);
          resolve(d);
        }
      };
      window.addEventListener('message', h);
      window.postMessage(Object.assign({ __bp3d: 1, dir: 'ext', id, cmd }, extra || {}), '*');
      setTimeout(() => { if (!done) { window.removeEventListener('message', h); resolve({ ok: false, error: 'timeout' }); } }, timeout || 8000);
    });
  }

  const info = await rpc('scene-info');
  ok('cena:detectada', info && info.ok && info.info.meshes === 1 && info.info.triangles === 2,
    JSON.stringify(info && info.info));

  /* ---------------------------------------------------------------- */
  /* 5. Exportar GLB e validar bytes                                  */
  /* ---------------------------------------------------------------- */

  const glb = await rpc('export', { format: 'glb' }, 15000);
  if (!glb || !glb.ok) {
    fail('glb:export', (glb && glb.error) || 'sem resposta');
  } else {
    const buf = glb.buffer;
    R.glb = { bytes: buf.byteLength, meshes: glb.meshes, triangles: glb.triangles, name: glb.name };

    const dv = new DataView(buf);
    ok('glb:magic', dv.getUint32(0, true) === 0x46546c67);
    ok('glb:versao', dv.getUint32(4, true) === 2);
    ok('glb:tamanho-total', dv.getUint32(8, true) === buf.byteLength,
      dv.getUint32(8, true) + ' != ' + buf.byteLength);

    const jsonLen = dv.getUint32(12, true);
    ok('glb:chunk-json', dv.getUint32(16, true) === 0x4e4f534a);
    const jsonText = new TextDecoder().decode(new Uint8Array(buf, 20, jsonLen));
    let J = null;
    try { J = JSON.parse(jsonText); } catch (e) { fail('glb:json-parse', e.message); }

    if (J) {
      const binStart = 20 + jsonLen;
      const binLen = dv.getUint32(binStart, true);
      const binTypeOk = dv.getUint32(binStart + 4, true) === 0x004e4942;
      const binBuf = new Uint8Array(buf, binStart + 8, binLen);

      ok('glb:chunk-bin', binTypeOk);
      ok('glb:bin-alinhado', binLen % 4 === 0, 'binLen=' + binLen);
      ok('glb:buffer-byteLength', J.buffers[0].byteLength === Math.max(binBuf.length, 0) &&
        J.buffers[0].byteLength % 4 === 0, J.buffers[0].byteLength);
      ok('glb:meshes', J.meshes.length === 1 && J.meshes[0].primitives.length === 1);
      ok('glb:materiais', J.materials.length === 1);
      ok('glb:accessors', J.accessors.length === 4, 'accessors=' + J.accessors.length);

      const prim = J.meshes[0].primitives[0];
      ok('glb:atributos', prim.attributes.POSITION === 0 && prim.attributes.NORMAL === 1 &&
        prim.attributes.TEXCOORD_0 === 2 && prim.indices === 3,
        JSON.stringify(prim.attributes));

      const posAcc = J.accessors[prim.attributes.POSITION];
      ok('glb:min-max', JSON.stringify(posAcc.min) === '[0,0,0]' && JSON.stringify(posAcc.max) === '[1,1,0]',
        JSON.stringify([posAcc.min, posAcc.max]));

      // lê as posições do blob binário
      const pv = J.bufferViews[posAcc.bufferView];
      const pos = new Float32Array(buf, binStart + 8 + pv.byteOffset, pv.byteLength / 4);
      const expected = [0, 0, 0, 1, 0, 0, 1, 1, 0, 0, 1, 0];
      let posOk = pos.length === expected.length;
      if (posOk) for (let i = 0; i < expected.length; i++) if (Math.abs(pos[i] - expected[i]) > 1e-6) posOk = false;
      ok('glb:posicoes', posOk, Array.from(pos).join(','));

      const idxAcc = J.accessors[prim.indices];
      const iv = J.bufferViews[idxAcc.bufferView];
      const IndArr = idxAcc.componentType === 5125 ? Uint32Array : Uint16Array;
      const idx = new IndArr(buf, binStart + 8 + iv.byteOffset, idxAcc.count);
      ok('glb:indices', idxAcc.count === 6 && idx[0] === 0 && idx[1] === 1 && idx[2] === 2 && idx[5] === 3,
        Array.from(idx).join(','));

      const uvAcc = J.accessors[prim.attributes.TEXCOORD_0];
      ok('glb:uv', uvAcc.count === 4 && uvAcc.type === 'VEC2');

      const mat = J.materials[0].pbrMetallicRoughness;
      const lin = 0.5 <= 0.04045 ? 0.5 / 12.92 : Math.pow((0.5 + 0.055) / 1.055, 2.4);
      ok('glb:material', !!mat && Math.abs(mat.baseColorFactor[1] - lin) < 0.02,
        JSON.stringify(mat && mat.baseColorFactor));

      // hierarquia: cena(t 0,0,0) -> grupo(t 2,0,0) -> malha(t 0,1,0)
      const rootNode = J.nodes[J.scenes[0].nodes[0]];
      const groupNode = J.nodes[rootNode.children[0]];
      const meshNode = J.nodes[groupNode.children[0]];
      ok('glb:hierarquia', J.scenes[0].nodes.length === 1 &&
        JSON.stringify(rootNode.translation) === '[0,0,0]' &&
        JSON.stringify(groupNode.translation) === '[2,0,0]' &&
        JSON.stringify(meshNode.translation) === '[0,1,0]' &&
        meshNode.mesh === 0,
        JSON.stringify({
          root: rootNode.translation,
          grupo: groupNode.translation,
          malha: meshNode.translation
        }));

      ok('glb:contagem', J.meshes[0] && glb.meshes === 1 && glb.triangles === 2);
    }
  }

  /* ---------------------------------------------------------------- */
  /* 6. Exportar OBJ                                                  */
  /* ---------------------------------------------------------------- */

  const obj = await rpc('export', { format: 'obj' }, 15000);
  if (!obj || !obj.ok) {
    fail('obj:export', (obj && obj.error) || 'sem resposta');
  } else {
    const text = new TextDecoder().decode(new Uint8Array(obj.buffer));
    const lines = text.split('\n');
    R.obj = { bytes: obj.buffer.byteLength, meshes: obj.meshes, triangles: obj.triangles };
    const vs = lines.filter((l) => l.startsWith('v '));
    const vns = lines.filter((l) => l.startsWith('vn '));
    const vts = lines.filter((l) => l.startsWith('vt '));
    const fs = lines.filter((l) => l.startsWith('f '));
    ok('obj:vertices', vs.length === 4, 'v=' + vs.length);
    ok('obj:normais', vns.length === 4, 'vn=' + vns.length);
    ok('obj:uv', vts.length === 4, 'vt=' + vts.length);
    ok('obj:faces', fs.length === 2, 'f=' + fs.length);
    ok('obj:material', text.includes('usemtl MatTeste'));
    ok('obj:objeto', text.includes('o QuadTeste'));
    ok('obj:matriz-mundo', vs[0] === 'v 0 1 0', 'primeiro = ' + vs[0]);
    ok('obj:formato-face', fs[0] === 'f 1/1/1 2/2/2 3/3/3', fs[0]);
  }

  /* ---------------------------------------------------------------- */
  /* 7. Ponte: memória, download e conversão GLB -> glTF              */
  /* ---------------------------------------------------------------- */

  if (glb && glb.ok) {
    const copy = glb.buffer.slice(0);
    window.postMessage({
      __bp3d: 1, dir: 'page', kind: 'bytes',
      url: 'https://exemplo.com/modelo.glb',
      mime: 'model/gltf-binary',
      format: 'glb',
      buffer: copy
    }, '*', [copy]);

    await new Promise((r) => setTimeout(r, 250));

    const state = await new Promise((resolve) => {
      const ret = window.__bridgeHandler({ __to: 'page', cmd: 'state' }, {}, resolve);
      setTimeout(() => resolve({ ok: false, error: 'timeout-state' }), 5000);
    });

    ok('ponte:estado', state && state.ok && state.assets.length === 1 && state.assets[0].inMemory === true,
      JSON.stringify(state && state.assets));
    R.bridge.assets = state.assets ? state.assets.length : 0;

    const key = state.assets && state.assets[0] && state.assets[0].key;

    if (key) {
      const before = captured.blobs.length;
      const dl = await new Promise((resolve) => {
        window.__bridgeHandler({ __to: 'page', cmd: 'download', key }, {}, resolve);
        setTimeout(() => resolve({ ok: false, error: 'timeout-dl' }), 5000);
      });
      const dlBlob = captured.blobs[captured.blobs.length - 1];
      ok('ponte:download', dl && dl.ok && captured.blobs.length === before + 1 &&
        dlBlob && dlBlob.size === glb.buffer.byteLength, JSON.stringify(dl));

      const before2 = captured.blobs.length;
      const conv = await new Promise((resolve) => {
        window.__bridgeHandler({ __to: 'page', cmd: 'convert', key }, {}, resolve);
        setTimeout(() => resolve({ ok: false, error: 'timeout-conv' }), 5000);
      });

      const convBlob = captured.blobs[captured.blobs.length - 1];
      ok('ponte:conversao', conv && conv.ok && captured.blobs.length === before2 + 1 && !!convBlob,
        JSON.stringify(conv));

      if (convBlob) {
        try {
          const txt = await convBlob.text();
          const g = JSON.parse(txt);
          const uri = g.buffers[0].uri || '';
          ok('gltf:buffer-embutido', uri.indexOf('data:application/octet-stream;base64,') === 0);
          ok('gltf:mesmo-conteudo', g.meshes.length === 1 && g.asset.version === '2.0');
          R.bridge.gltfBytes = txt.length;
        } catch (e) {
          fail('gltf:parse', e.message);
        }
      }
    }
  } else {
    R.checks['ponte:estado'] = 'pulado (sem GLB)';
  }

  /* ---------------------------------------------------------------- */
  /* 8. Ícones                                                        */
  /* ---------------------------------------------------------------- */

  function roundRectPath(ctx, x, y, w, h, r) {
    ctx.beginPath();
    ctx.moveTo(x + r, y);
    ctx.arcTo(x + w, y, x + w, y + h, r);
    ctx.arcTo(x + w, y + h, x, y + h, r);
    ctx.arcTo(x, y + h, x, y, r);
    ctx.arcTo(x, y, x + w, y, r);
    ctx.closePath();
  }

  function drawIcon(size) {
    const SS = 4;
    const W = size * SS;
    const cv = document.createElement('canvas');
    cv.width = W;
    cv.height = W;
    const ctx = cv.getContext('2d');
    const r = W * 0.234;

    roundRectPath(ctx, 0, 0, W, W, r);
    ctx.save();
    ctx.clip();

    const g = ctx.createLinearGradient(0, 0, W, W);
    g.addColorStop(0, '#8b3ff8');
    g.addColorStop(1, '#14143f');
    ctx.fillStyle = g;
    ctx.fillRect(0, 0, W, W);

    const rg = ctx.createRadialGradient(W * 0.5, W * 0.42, 0, W * 0.5, W * 0.42, W * 0.62);
    rg.addColorStop(0, 'rgba(34,211,238,0.45)');
    rg.addColorStop(1, 'rgba(34,211,238,0)');
    ctx.fillStyle = rg;
    ctx.fillRect(0, 0, W, W);

    const cx = W * 0.5;
    const cy = W * 0.5;
    const Rr = W * 0.344;
    const A = [cx, cy - Rr];
    const B = [cx + Rr * 0.866, cy - Rr * 0.5];
    const C = [cx + Rr * 0.866, cy + Rr * 0.5];
    const D = [cx, cy + Rr];
    const E = [cx - Rr * 0.866, cy + Rr * 0.5];
    const F = [cx - Rr * 0.866, cy - Rr * 0.5];
    const O = [cx, cy];

    const poly = (pts, color) => {
      ctx.beginPath();
      ctx.moveTo(pts[0][0], pts[0][1]);
      for (let i = 1; i < pts.length; i++) ctx.lineTo(pts[i][0], pts[i][1]);
      ctx.closePath();
      ctx.fillStyle = color;
      ctx.fill();
    };

    poly([A, B, O, F], '#c9f9ff');
    poly([F, O, D, E], '#22d3ee');
    poly([B, C, D, O], '#605cf5');

    ctx.strokeStyle = '#e8fbff';
    ctx.lineWidth = Math.max(1.6, W * 0.026);
    ctx.lineJoin = 'round';
    ctx.lineCap = 'round';
    const seg = (p, q) => {
      ctx.beginPath();
      ctx.moveTo(p[0], p[1]);
      ctx.lineTo(q[0], q[1]);
      ctx.stroke();
    };
    [[A, B], [B, C], [C, D], [D, E], [E, F], [F, A], [O, B], [O, F], [O, D]].forEach(([p, q]) => seg(p, q));

    const ax = cx + Rr * 0.42;
    const ay = cy + Rr * 0.08;
    const sh = Rr * 0.42;
    ctx.strokeStyle = '#ffffff';
    ctx.lineWidth = Math.max(2, W * 0.045);
    seg([ax, ay - sh * 0.55], [ax, ay + sh * 0.35]);
    poly([[ax - Rr * 0.19, ay + sh * 0.22], [ax + Rr * 0.19, ay + sh * 0.22], [ax, ay + sh * 0.72]], '#ffffff');
    ctx.restore();

    const out = document.createElement('canvas');
    out.width = size;
    out.height = size;
    const octx = out.getContext('2d');
    octx.imageSmoothingEnabled = true;
    octx.imageSmoothingQuality = 'high';
    octx.drawImage(cv, 0, 0, size, size);
    return out.toDataURL('image/png');
  }

  window.__ICONS = {};
  [16, 32, 48, 128].forEach((s) => {
    try {
      const url = drawIcon(s);
      if (/^data:image\/png;base64,/.test(url) && url.length > 200) {
        window.__ICONS[s] = url.replace(/^data:image\/png;base64,/, '');
        R.icons[s] = window.__ICONS[s].length;
      } else {
        R.checks['icone:' + s] = 'FALHOU: dataURL inválida';
      }
    } catch (e) {
      R.checks['icone:' + s] = 'FALHOU: ' + e.message;
    }
  });

  /* ---------------------------------------------------------------- */

  const bad = Object.entries(R.checks).filter(([, v]) => v !== true && String(v).startsWith('FALHOU'));
  R.resumo = bad.length ? bad.length + ' falha(s)' : 'tudo ok';
  out.textContent = JSON.stringify({ resumo: R.resumo, checks: R.checks }, null, 2);
  return R;
};
