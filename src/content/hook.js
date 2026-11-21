/* 3D Browser Pro — hook no mundo da página
 * Intercepta fetch/XHR de arquivos 3D, observa a cena THREE.js em memória
 * e exporta a geometria viva como GLB ou OBJ (sem bibliotecas externas).
 */
(function () {
  'use strict';
  if (window.__BP3D_HOOK__) return;
  window.__BP3D_HOOK__ = true;

  var MAX_CAPTURE = 96 * 1024 * 1024;
  var EXT_3D = {
    glb: 1, gltf: 1, obj: 1, fbx: 1, stl: 1, ply: 1, dae: 1, '3ds': 1,
    usdz: 1, usd: 1, usda: 1, usdc: 1
  };
  var SKIP_PROBE = {
    mp4: 1, webm: 1, mov: 1, m4a: 1, mp3: 1, wav: 1, ogg: 1, zip: 1, rar: 1,
    '7z': 1, pdf: 1, gz: 1, wasm: 1, png: 1, jpg: 1, jpeg: 1, webp: 1, gif: 1,
    avif: 1, ico: 1, css: 1, js: 1, mjs: 1, html: 1, txt: 1, xml: 1, svg: 1
  };
  var GENERIC_MIME = /^(application\/octet-stream|binary\/octet-stream|text\/plain|application\/binary|application\/x-unknown|application\/json|text\/json|application\/x-json|)$/;

  /* ---------------------------------------------------------------- */
  /* Utilidades                                                        */
  /* ---------------------------------------------------------------- */

  function post(payload, transfer) {
    try {
      payload.__bp3d = 1;
      payload.dir = 'page';
      window.postMessage(payload, '*', transfer || []);
    } catch (e) { /* ignora */ }
  }

  function absUrl(url) {
    try { return new URL(String(url), location.href).href; }
    catch (e) { return String(url || ''); }
  }

  function extOf(url) {
    try {
      var p = new URL(String(url), location.href).pathname;
      var m = /\.([a-z0-9]{2,5})$/i.exec(p);
      return m ? m[1].toLowerCase() : '';
    } catch (e) { return ''; }
  }

  function detectId(url, mime) {
    var e = extOf(url);
    if (EXT_3D[e]) return e;
    var m = String(mime || '').toLowerCase().split(';')[0].trim();
    if (m === 'model/gltf-binary') return 'glb';
    if (m === 'model/gltf+json' || m === 'model/vnd.three+json') return 'gltf';
    if (m === 'model/obj') return 'obj';
    if (m === 'application/x-fbx' || m === 'model/x-fbx') return 'fbx';
    if (m === 'model/stl') return 'stl';
    if (m === 'model/ply') return 'ply';
    if (m === 'model/vnd.collada+xml') return 'dae';
    if (m === 'model/vnd.usdz+zip') return 'usdz';
    return null;
  }

  function looks3D(url, mime) {
    if (detectId(url, mime)) return true;
    return /^(model\/|application\/x-fbx)/.test(String(mime || '').toLowerCase());
  }

  function snapshotProbe(url) {
    var e = extOf(url);
    return !e || !SKIP_PROBE[e];
  }

  function headText(buf, len) {
    var n = Math.min(buf.byteLength, len || 64);
    var u8 = new Uint8Array(buf, 0, n);
    var s = '';
    for (var i = 0; i < u8.length; i++) s += String.fromCharCode(u8[i]);
    return s;
  }

  /** Identifica o formato pelos primeiros bytes (arquivos sem extensão). */
  function sniffFormat(buf) {
    if (!buf || buf.byteLength < 16) return null;
    var u8 = new Uint8Array(buf, 0, 4);
    if (u8[0] === 0x67 && u8[1] === 0x6c && u8[2] === 0x54 && u8[3] === 0x46) return 'glb'; // 'glTF'
    var head = headText(buf, 96);
    if (head.indexOf('Kaydara FBX Binary') === 0) return 'fbx';
    if (/^ply[\r\n]/.test(head)) return 'ply';
    if (/^\s*\{/.test(head) && /"asset"\s*:/.test(headText(buf, 1024))) return 'gltf';
    if (u8[0] === 0x4D && u8[1] === 0x4D) return '3ds';
    if (u8[0] === 0x50 && u8[1] === 0x4B) return /usd[acz]/i.test(headText(buf, 4096)) ? 'usdz' : null;
    var xml = headText(buf, 2048);
    if (/^\s*</.test(xml) && /COLLADA/i.test(xml)) return 'dae';
    if (/^solid[\r\n ]/.test(headText(buf, 8)) && /facet|vertex/i.test(headText(buf, 512))) return 'stl';
    if (/^\s*(#.*\r?\n)?\s*(v|o|g|mtllib|usemtl|s)\s/m.test(headText(buf, 400))) return 'obj';
    if (buf.byteLength >= 84) {
      try {
        var tris = new DataView(buf).getUint32(80, true);
        if (tris > 0 && 84 + tris * 50 === buf.byteLength) return 'stl';
      } catch (e) { /* ignora */ }
    }
    return null;
  }

  /* ---------------------------------------------------------------- */
  /* Captura: fetch                                                    */
  /* ---------------------------------------------------------------- */

  function inspectResponse(url, res) {
    var ct = String(res.headers.get('content-type') || '').toLowerCase().split(';')[0].trim();
    var direct = looks3D(url, ct);
    var probe = !direct && GENERIC_MIME.test(ct) && snapshotProbe(url);
    if (!direct && !probe) return;

    var len = Number(res.headers.get('content-length') || 0);
    if (len && len > MAX_CAPTURE) {
      post({ kind: 'notice', message: 'Arquivo 3D muito grande para a memória (' + Math.round(len / 1048576) + ' MB).' });
      return;
    }

    res.clone().arrayBuffer().then(function (buf) {
      if (!buf || !buf.byteLength || buf.byteLength > MAX_CAPTURE) return;
      var id = direct ? detectId(url, ct) : null;
      if (!id) id = sniffFormat(buf);
      if (!id) return;
      post({
        kind: 'bytes',
        url: absUrl(url),
        mime: ct,
        format: id,
        buffer: buf
      }, [buf]);
    }).catch(function () { /* stream já consumido */ });
  }

  try {
    if (typeof window.fetch === 'function') {
      var origFetch = window.fetch;
      window.fetch = function (input, init) {
        var p = origFetch.apply(this, arguments);
        try {
          var url = typeof input === 'string' ? input : (input && input.url) || '';
          p.then(function (res) {
            try {
              if (res && res.type !== 'opaque') inspectResponse(url, res);
            } catch (e) { /* ignora */ }
          }, function () { /* erro de rede */ });
        } catch (e) { /* ignora */ }
        return p;
      };
    }
  } catch (e) { /* ignora */ }

  /* ---------------------------------------------------------------- */
  /* Captura: XMLHttpRequest                                           */
  /* ---------------------------------------------------------------- */

  try {
    var XHR = window.XMLHttpRequest;
    if (XHR && XHR.prototype) {
      var origOpen = XHR.prototype.open;
      var origSend = XHR.prototype.send;

      XHR.prototype.open = function (method, url) {
        try { this.__bp3dUrl = url; } catch (e) { /* ignora */ }
        return origOpen.apply(this, arguments);
      };

      XHR.prototype.send = function () {
        var self = this;
        try {
          self.addEventListener('load', function () {
            try {
              var url = self.__bp3dUrl || self.responseURL || '';
              var ct = String(self.getResponseHeader('content-type') || '').toLowerCase().split(';')[0].trim();
              if (!looks3D(url, ct) && !(GENERIC_MIME.test(ct) && snapshotProbe(url))) return;

              var rt = self.responseType;
              if (rt === 'arraybuffer' && self.response && self.response.byteLength) {
                if (self.response.byteLength > MAX_CAPTURE) return;
                var copy = self.response.slice(0);
                var id = detectId(url, ct) || sniffFormat(copy);
                if (!id) return;
                post({ kind: 'bytes', url: absUrl(url), mime: ct, format: id, buffer: copy }, [copy]);
              } else if (rt === 'blob' && self.response && self.response.size) {
                if (self.response.size > MAX_CAPTURE) return;
                self.response.arrayBuffer().then(function (buf) {
                  var fid = detectId(url, ct) || sniffFormat(buf);
                  if (!fid) return;
                  post({ kind: 'bytes', url: absUrl(url), mime: ct, format: fid, buffer: buf }, [buf]);
                }).catch(function () {});
              } else if ((rt === '' || rt === 'text') && /gltf\+json|json/.test(ct) && self.responseText) {
                var enc = new TextEncoder().encode(self.responseText);
                if (enc.byteLength > MAX_CAPTURE) return;
                var f2 = detectId(url, ct) || 'gltf';
                post({ kind: 'bytes', url: absUrl(url), mime: ct, format: f2, buffer: enc.buffer }, [enc.buffer]);
              }
            } catch (e) { /* ignora */ }
          }, true);
        } catch (e) { /* ignora */ }
        return origSend.apply(this, arguments);
      };
    }
  } catch (e) { /* ignora */ }

  /* ---------------------------------------------------------------- */
  /* Captura: Web Worker e createObjectURL                             */
  /* A Meshy decifra o GLB num worker e entrega o ArrayBuffer por       */
  /* postMessage (e às vezes por Blob URL): nada disso passa por        */
  /* fetch/XHR, então o arquivo nunca aparecia na lista.                */
  /* ---------------------------------------------------------------- */

  var recent = [];

  /* o mesmo modelo chega por mais de um caminho em poucos segundos */
  function seenRecently(size) {
    var now = Date.now();
    var fresh = [];
    var dup = false;
    for (var i = 0; i < recent.length; i++) {
      if (now - recent[i].t > 4000) continue;
      fresh.push(recent[i]);
      if (Math.abs(recent[i].size - size) < 16) dup = true;
    }
    recent = fresh;
    if (!dup) recent.push({ size: size, t: now });
    return dup;
  }

  function captureBuffer(buf, url, mime) {
    if (!buf || !buf.byteLength || buf.byteLength > MAX_CAPTURE) return false;
    var id = sniffFormat(buf);
    if (!id || seenRecently(buf.byteLength)) return false;
    var copy = buf.slice ? buf.slice(0) : buf;
    post({ kind: 'bytes', url: url, mime: mime || '', format: id, buffer: copy }, [copy]);
    return true;
  }

  /* workers mandam { data: ArrayBuffer } — ou uma view que cobre o buffer inteiro */
  function captureWorkerMessage(d) {
    if (!d || typeof d !== 'object') return;
    var v = d.data;
    var buf = null;
    try {
      if (v instanceof ArrayBuffer) buf = v;
      else if (v && v.buffer instanceof ArrayBuffer && v.byteLength === v.buffer.byteLength && v.byteLength > 16) buf = v.buffer;
    } catch (e) { return; }
    /* URL falsa mas única e hierárquica: vira chave própria na lista e um bom nome de arquivo */
    if (buf) captureBuffer(buf, 'worker://' + (location.hostname || 'pagina') + '/modelo-' + Date.now().toString(36), '');
  }

  function captureBlob(blob, url) {
    if (!blob || typeof blob.arrayBuffer !== 'function' || blob.size < 12 || blob.size > MAX_CAPTURE) return;
    var type = String(blob.type || '').toLowerCase();

    if (type === 'model/gltf-binary') {
      blob.arrayBuffer().then(function (buf) {
        captureBuffer(buf, url, type);
      }).catch(function () { /* ignora */ });
      return;
    }

    /* tipo genérico: só baixa o resto se a assinatura for de GLB */
    blob.slice(0, 16).arrayBuffer().then(function (head) {
      var u8 = new Uint8Array(head);
      if (u8.length < 4 || u8[0] !== 0x67 || u8[1] !== 0x6c || u8[2] !== 0x54 || u8[3] !== 0x46) return;
      blob.arrayBuffer().then(function (buf) {
        captureBuffer(buf, url, type);
      }).catch(function () { /* ignora */ });
    }).catch(function () { /* ignora */ });
  }

  try {
    var OrigWorker = window.Worker;
    if (typeof OrigWorker === 'function') {
      window.Worker = function BP3DWorker(url, opts) {
        var w = new OrigWorker(url, opts);
        try {
          var add = w.addEventListener.bind(w);
          w.addEventListener = function (type, fn, o) {
            if (type !== 'message' || typeof fn !== 'function') return add(type, fn, o);
            return add(type, function (e) {
              try { captureWorkerMessage(e && e.data); } catch (err) { /* ignora */ }
              return fn.call(this, e);
            }, o);
          };

          var proto = Object.getPrototypeOf(w);
          var desc = proto && Object.getOwnPropertyDescriptor(proto, 'onmessage');
          if (desc && desc.set) {
            var handlers = [];
            Object.defineProperty(w, 'onmessage', {
              configurable: true,
              get: function () { return handlers[0] || null; },
              set: function (fn) {
                handlers[0] = fn;
                desc.set.call(w, function (e) {
                  try { captureWorkerMessage(e && e.data); } catch (err) { /* ignora */ }
                  return typeof fn === 'function' ? fn.call(this, e) : undefined;
                });
              }
            });
          }
        } catch (e) { /* worker opaco: segue sem capturar */ }
        return w;
      };
      window.Worker.prototype = OrigWorker.prototype;
    }
  } catch (e) { /* ignora */ }

  try {
    if (window.URL && typeof URL.createObjectURL === 'function') {
      var origCreateObjectURL = URL.createObjectURL.bind(URL);
      URL.createObjectURL = function (blob) {
        var url = origCreateObjectURL.apply(URL, arguments);
        try { captureBlob(blob, url); } catch (e) { /* ignora */ }
        return url;
      };
    }
  } catch (e) { /* ignora */ }

  /* ---------------------------------------------------------------- */
  /* Cena THREE.js ao vivo                                             */
  /* ---------------------------------------------------------------- */

  var lastScene = null;
  var lastCamera = null;
  var threeHooked = false;

  function hookThree(THREE) {
    if (!THREE || threeHooked) return;
    var proto = THREE.WebGLRenderer && THREE.WebGLRenderer.prototype;
    if (!proto || typeof proto.render !== 'function') return;
    var origRender = proto.render;
    proto.render = function (scene, camera) {
      try {
        if (scene && (scene.isScene || scene.isObject3D)) {
          lastScene = scene;
          lastCamera = camera;
        }
      } catch (e) { /* ignora */ }
      return origRender.apply(this, arguments);
    };
    threeHooked = true;
  }

  /* three.js avisa quem estiver em __THREE_DEVTOOLS__ (funciona mesmo com bundle/módulo,
     sem window.THREE). Registramos antes de a página criar renderer/cena. */
  function hookDevtools() {
    try {
      var target = window.__THREE_DEVTOOLS__;
      if (!target || typeof target.addEventListener !== 'function') {
        target = new EventTarget();
        window.__THREE_DEVTOOLS__ = target;
      }
      if (target.__bp3d) return;
      target.__bp3d = true;
      target.addEventListener('observe', function (ev) {
        var o = ev && ev.detail;
        if (!o || typeof o !== 'object') return;
        try {
          if (o.isScene) lastScene = o;
          else if (o.isObject3D) lastScene = o;
          else if (o.scene && o.scene.isObject3D) { lastScene = o.scene; lastCamera = o.camera || lastCamera; }
        } catch (e2) { /* ignora */ }
      });
    } catch (e) { /* ambientes sem EventTarget */ }
  }

  hookDevtools();

  var tries = 0;
  var poll = setInterval(function () {
    tries++;
    try { if (window.THREE) hookThree(window.THREE); } catch (e) { /* ignora */ }
    if (threeHooked || tries > 240) clearInterval(poll);
  }, 250);

  function gatherRoots() {
    var roots = [];
    var push = function (o) {
      if (!o || typeof o !== 'object') return;
      if (roots.indexOf(o) >= 0) return;
      if (o.isScene || o.isObject3D || o.isMesh || Array.isArray(o.children)) roots.push(o);
    };

    push(lastScene);
    try { push(window.scene); } catch (e) { /* ignora */ }
    try { if (window.viewer) push(window.viewer.scene || window.viewer.model); } catch (e) { /* ignora */ }

    try {
      var mvs = document.querySelectorAll('model-viewer');
      for (var i = 0; i < mvs.length; i++) {
        try { push(mvs[i].model); } catch (e) { /* ignora */ }
      }
    } catch (e) { /* ignora */ }

    return roots;
  }

  function countScene(roots) {
    var meshes = 0;
    var triangles = 0;
    var objects = 0;
    var seen = [];

    for (var i = 0; i < roots.length; i++) {
      (function (root) {
        if (!root || typeof root.traverse !== 'function') return;
        root.traverse(function (o) {
          if (!o || seen.indexOf(o) >= 0) return;
          seen.push(o);
          objects++;
          if (o.isMesh && o.geometry && o.geometry.attributes && o.geometry.attributes.position) {
            meshes++;
            var g = o.geometry;
            var count = g.index ? g.index.count : g.attributes.position.count;
            triangles += Math.floor(count / 3);
          }
        });
      })(roots[i]);
    }
    return { meshes: meshes, triangles: triangles, objects: objects };
  }

  function sceneInfo() {
    var roots = gatherRoots();
    var counts = countScene(roots);
    var hasCanvas = false;
    try { hasCanvas = !!document.querySelector('canvas'); } catch (e) { /* ignora */ }

    var names = [];
    for (var i = 0; i < roots.length && names.length < 10; i++) {
      var r = roots[i];
      names.push((r && (r.name || r.type)) || 'cena');
    }

    return {
      hasScene: counts.meshes > 0,
      meshes: counts.meshes,
      triangles: counts.triangles,
      objects: counts.objects,
      roots: roots.length,
      hasCanvas: hasCanvas,
      hasThreeGlobal: !!window.THREE,
      modelViewers: (function () {
        try { return document.querySelectorAll('model-viewer').length; } catch (e) { return 0; }
      })(),
      sources: names
    };
  }

  /* ---------------------------------------------------------------- */
  /* Leitura de atributos                                              */
  /* ---------------------------------------------------------------- */

  function flatten(attr, size) {
    var n = attr.count;
    var out = new Float32Array(n * size);
    for (var i = 0; i < n; i++) {
      out[i * size] = attr.getX(i);
      if (size > 1) out[i * size + 1] = attr.getY(i);
      if (size > 2) out[i * size + 2] = attr.getZ(i);
      if (size > 3) out[i * size + 3] = attr.getW(i);
    }
    return out;
  }

  function bounds(positions) {
    var min = [Infinity, Infinity, Infinity];
    var max = [-Infinity, -Infinity, -Infinity];
    for (var i = 0; i < positions.length; i += 3) {
      for (var k = 0; k < 3; k++) {
        var v = positions[i + k];
        if (v < min[k]) min[k] = v;
        if (v > max[k]) max[k] = v;
      }
    }
    if (!isFinite(min[0])) { min = [0, 0, 0]; max = [0, 0, 0]; }
    return { min: min, max: max };
  }

  function indicesOf(geom) {
    var n = geom.attributes.position.count;
    if (geom.index && geom.index.array) {
      var src = geom.index.array;
      if (src instanceof Uint32Array) return src;
      return n > 65535 ? Uint32Array.from(src) : Uint16Array.from(src);
    }
    var out = n > 65535 ? new Uint32Array(n) : new Uint16Array(n);
    for (var i = 0; i < n; i++) out[i] = i;
    return out;
  }

  function srgbToLinear(c) {
    return c <= 0.04045 ? c / 12.92 : Math.pow((c + 0.055) / 1.055, 2.4);
  }

  function r6(n) {
    var v = Number(n);
    if (!isFinite(v)) return 0;
    return Math.round(v * 1e6) / 1e6;
  }

  /* ---------------------------------------------------------------- */
  /* Escritor GLB                                                      */
  /* ---------------------------------------------------------------- */

  function buildGLB(roots, baseName) {
    var gltf = {
      asset: { version: '2.0', generator: '3D Browser Pro' },
      scene: 0,
      scenes: [{ nodes: [] }],
      nodes: [],
      meshes: [],
      materials: [],
      accessors: [],
      bufferViews: [],
      buffers: [{ byteLength: 0 }]
    };

    var parts = [];
    var offset = 0;
    var triangles = 0;

    function align4() {
      var pad = (4 - (offset % 4)) % 4;
      if (pad) { parts.push(new Uint8Array(pad)); offset += pad; }
    }

    function addView(arr) {
      align4();
      var u8 = new Uint8Array(arr.buffer, arr.byteOffset, arr.byteLength);
      parts.push(u8);
      gltf.bufferViews.push({ buffer: 0, byteOffset: offset, byteLength: u8.byteLength });
      offset += u8.byteLength;
      return gltf.bufferViews.length - 1;
    }

    function addAccessor(arr, componentType, type, minmax) {
      var count;
      if (type === 'SCALAR') count = arr.length;
      else if (type === 'VEC2') count = arr.length / 2;
      else if (type === 'VEC4') count = arr.length / 4;
      else count = arr.length / 3;

      var accessor = { bufferView: addView(arr), componentType: componentType, count: count, type: type };
      if (minmax) { accessor.min = minmax.min; accessor.max = minmax.max; }
      gltf.accessors.push(accessor);
      return gltf.accessors.length - 1;
    }

    var matCache = null;
    function materialIndex(mat) {
      if (!mat) return -1;
      if (!matCache) matCache = new Map();
      if (matCache.has(mat)) return matCache.get(mat);

      var m = { name: mat.name || 'Material' };
      var pbr = {};
      if (mat.color && typeof mat.color.r === 'number') {
        pbr.baseColorFactor = [
          r6(srgbToLinear(mat.color.r)),
          r6(srgbToLinear(mat.color.g)),
          r6(srgbToLinear(mat.color.b)),
          r6(typeof mat.opacity === 'number' ? mat.opacity : 1)
        ];
      }
      if (typeof mat.roughness === 'number') pbr.roughnessFactor = r6(mat.roughness);
      if (typeof mat.metalness === 'number') pbr.metallicFactor = r6(mat.metalness);
      if (Object.keys(pbr).length) m.pbrMetallicRoughness = pbr;

      if (mat.transparent || (typeof mat.opacity === 'number' && mat.opacity < 1)) m.alphaMode = 'BLEND';
      if (mat.side === 2 || mat.side === 'DoubleSide') m.doubleSided = true;
      if (mat.emissive && (mat.emissive.r || mat.emissive.g || mat.emissive.b)) {
        m.emissiveFactor = [r6(mat.emissive.r), r6(mat.emissive.g), r6(mat.emissive.b)];
      }
      if (mat.map) m.extras = { observacao: 'textura não exportada' };

      gltf.materials.push(m);
      var idx = gltf.materials.length - 1;
      matCache.set(mat, idx);
      return idx;
    }

    var meshCache = new Map();

    function addMesh(obj) {
      if (meshCache.has(obj)) return meshCache.get(obj);
      var geom = obj.geometry;
      if (!geom || !geom.attributes || !geom.attributes.position) return null;

      var positions = flatten(geom.attributes.position, 3);
      var boundsPos = bounds(positions);
      var posAccessor = addAccessor(positions, 5126, 'VEC3', boundsPos);

      var normAccessor = -1;
      if (geom.attributes.normal) {
        normAccessor = addAccessor(flatten(geom.attributes.normal, 3), 5126, 'VEC3', null);
      }
      var uvAccessor = -1;
      if (geom.attributes.uv) {
        uvAccessor = addAccessor(flatten(geom.attributes.uv, 2), 5126, 'VEC2', null);
      }

      var indices = indicesOf(geom);
      var mats = Array.isArray(obj.material) ? obj.material : [obj.material];
      var specs = [];

      if (geom.groups && geom.groups.length) {
        for (var gi = 0; gi < geom.groups.length; gi++) {
          var g = geom.groups[gi];
          var start = g.start || 0;
          var count = (g.count != null) ? g.count : (indices.length - start);
          if (count <= 0) continue;
          specs.push({ start: start, count: count, material: mats[g.materialIndex] || mats[0] });
        }
      }
      if (!specs.length) specs.push({ start: 0, count: indices.length, material: mats[0] });

      var mesh = { name: obj.name || ('malha_' + (gltf.meshes.length + 1)), primitives: [] };
      gltf.meshes.push(mesh);
      var meshIdx = gltf.meshes.length - 1;
      meshCache.set(obj, meshIdx);

      for (var si = 0; si < specs.length; si++) {
        var spec = specs[si];
        var slice = indices.subarray(spec.start, spec.start + spec.count);
        if (!slice.length) continue;

        var idxArr = (slice instanceof Uint32Array) ? slice.slice(0) : Uint16Array.from(slice);
        var compType = (idxArr instanceof Uint32Array) ? 5125 : 5123;
        var idxAccessor = addAccessor(idxArr, compType, 'SCALAR', null);

        var prim = {
          attributes: { POSITION: posAccessor },
          indices: idxAccessor,
          mode: 4
        };
        if (normAccessor >= 0) prim.attributes.NORMAL = normAccessor;
        if (uvAccessor >= 0) prim.attributes.TEXCOORD_0 = uvAccessor;

        var mi = materialIndex(spec.material);
        if (mi >= 0) prim.material = mi;

        mesh.primitives.push(prim);
        triangles += Math.floor(slice.length / 3);
      }

      if (!mesh.primitives.length) {
        gltf.meshes.pop();
        meshCache.delete(obj);
        return null;
      }
      return meshIdx;
    }

    function addNode(obj) {
      var node = {};
      if (obj.name) node.name = String(obj.name);

      if (obj.matrixAutoUpdate === false && obj.matrix && obj.matrix.elements) {
        node.matrix = Array.prototype.map.call(obj.matrix.elements, r6);
      } else {
        if (obj.position) node.translation = [r6(obj.position.x), r6(obj.position.y), r6(obj.position.z)];
        if (obj.quaternion) node.rotation = [r6(obj.quaternion.x), r6(obj.quaternion.y), r6(obj.quaternion.z), r6(obj.quaternion.w)];
        if (obj.scale) node.scale = [r6(obj.scale.x), r6(obj.scale.y), r6(obj.scale.z)];
      }

      if (obj.isMesh && obj.geometry) {
        var mi = addMesh(obj);
        if (mi != null) node.mesh = mi;
      }

      var kids = [];
      var children = obj.children || [];
      for (var i = 0; i < children.length; i++) {
        var ci = addNode(children[i]);
        if (ci != null) kids.push(ci);
      }
      if (kids.length) node.children = kids;

      gltf.nodes.push(node);
      return gltf.nodes.length - 1;
    }

    for (var r = 0; r < roots.length; r++) {
      var root = roots[r];
      if (!root) continue;
      try { if (root.updateMatrixWorld) root.updateMatrixWorld(true); } catch (e) { /* ignora */ }
      var idx = addNode(root);
      if (idx != null) gltf.scenes[0].nodes.push(idx);
    }

    if (!gltf.meshes.length) return null;

    gltf.buffers[0].byteLength = offset;

    var bin = new Uint8Array(offset);
    var cursor = 0;
    for (var pi = 0; pi < parts.length; pi++) {
      bin.set(parts[pi], cursor);
      cursor += parts[pi].byteLength;
    }

    var jsonBytes = new TextEncoder().encode(JSON.stringify(gltf));
    var jsonPad = (4 - (jsonBytes.length % 4)) % 4;
    var binPad = (4 - (bin.length % 4)) % 4;
    var jsonLen = jsonBytes.length + jsonPad;
    var binLen = bin.length + binPad;
    var total = 12 + 8 + jsonLen + 8 + binLen;

    var out = new ArrayBuffer(total);
    var dv = new DataView(out);
    var u8 = new Uint8Array(out);

    dv.setUint32(0, 0x46546c67, true);   // 'glTF'
    dv.setUint32(4, 2, true);
    dv.setUint32(8, total, true);

    dv.setUint32(12, jsonLen, true);
    dv.setUint32(16, 0x4e4f534a, true);  // 'JSON'
    u8.set(jsonBytes, 20);
    for (var jp = 0; jp < jsonPad; jp++) u8[20 + jsonBytes.length + jp] = 0x20;

    var binStart = 20 + jsonLen;
    dv.setUint32(binStart, binLen, true);
    dv.setUint32(binStart + 4, 0x004e4942, true); // 'BIN'
    u8.set(bin, binStart + 8);
    for (var bp = 0; bp < binPad; bp++) u8[binStart + 8 + bin.length + bp] = 0;

    return { buffer: out, meshes: gltf.meshes.length, triangles: triangles, name: baseName };
  }

  /* ---------------------------------------------------------------- */
  /* Escritor OBJ                                                      */
  /* ---------------------------------------------------------------- */

  function invNormalMatrix(e) {
    var det =
      e[0] * (e[5] * e[10] - e[9] * e[6]) -
      e[4] * (e[1] * e[10] - e[9] * e[2]) +
      e[8] * (e[1] * e[6] - e[5] * e[2]);

    if (!det || !isFinite(det)) return [1, 0, 0, 0, 1, 0, 0, 0, 1];
    var id = 1 / det;

    return [
      (e[5] * e[10] - e[9] * e[6]) * id,
      -(e[4] * e[10] - e[8] * e[6]) * id,
      (e[4] * e[9] - e[8] * e[5]) * id,
      -(e[1] * e[10] - e[9] * e[2]) * id,
      (e[0] * e[10] - e[8] * e[2]) * id,
      -(e[0] * e[9] - e[8] * e[1]) * id,
      (e[1] * e[6] - e[5] * e[2]) * id,
      -(e[0] * e[6] - e[4] * e[2]) * id,
      (e[0] * e[5] - e[4] * e[1]) * id
    ];
  }

  var num = function (n) {
    var s = (Math.abs(n) < 1e-9 ? 0 : n).toFixed(6);
    if (s.indexOf('.') >= 0) s = s.replace(/0+$/, '').replace(/\.$/, '');
    return s === '-0' ? '0' : s;
  };

  function buildOBJ(roots, baseName) {
    var lines = ['# 3D Browser Pro — export OBJ', '# geometria tesselada; texturas não incluídas', ''];
    var vBase = 1, vtBase = 1, vnBase = 1;
    var meshes = 0, triangles = 0;
    var seen = [];

    for (var r = 0; r < roots.length; r++) {
      var root = roots[r];
      if (!root) continue;
      try { if (root.updateMatrixWorld) root.updateMatrixWorld(true); } catch (e) { /* ignora */ }

      var visit = function (o) {
        if (!o || !o.isMesh || !o.geometry) return;
        if (!o.geometry.attributes || !o.geometry.attributes.position) return;
        if (seen.indexOf(o) >= 0) return;
        seen.push(o);

        var geom = o.geometry;
        var pos = geom.attributes.position;
        var nor = geom.attributes.normal;
        var uv = geom.attributes.uv;
        var e = (o.matrixWorld && o.matrixWorld.elements) || [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];
        var nm = invNormalMatrix(e);

        var n = pos.count;
        var name = (o.name || ('malha_' + (meshes + 1))).replace(/\s+/g, '_');
        lines.push('o ' + name);

        var matName = '';
        if (o.material && !Array.isArray(o.material)) matName = o.material.name || '';
        if (matName) lines.push('usemtl ' + matName.replace(/\s+/g, '_'));

        for (var i = 0; i < n; i++) {
          var x = pos.getX(i), y = pos.getY(i), z = pos.getZ(i);
          lines.push(
            'v ' + num(e[0] * x + e[4] * y + e[8] * z + e[12]) + ' ' +
            num(e[1] * x + e[5] * y + e[9] * z + e[13]) + ' ' +
            num(e[2] * x + e[6] * y + e[10] * z + e[14])
          );
        }

        if (uv) {
          for (var iu = 0; iu < n; iu++) {
            lines.push('vt ' + num(uv.getX(iu)) + ' ' + num(uv.getY(iu)));
          }
        }

        if (nor) {
          for (var inn = 0; inn < n; inn++) {
            var nx = nor.getX(inn), ny = nor.getY(inn), nz = nor.getZ(inn);
            var tx = nm[0] * nx + nm[3] * ny + nm[6] * nz;
            var ty = nm[1] * nx + nm[4] * ny + nm[7] * nz;
            var tz = nm[2] * nx + nm[5] * ny + nm[8] * nz;
            var l = Math.sqrt(tx * tx + ty * ty + tz * tz) || 1;
            lines.push('vn ' + num(tx / l) + ' ' + num(ty / l) + ' ' + num(tz / l));
          }
        }

        var idx = indicesOf(geom);
        var hasVt = !!uv;
        var hasVn = !!nor;

        for (var f = 0; f + 2 < idx.length; f += 3) {
          var a = idx[f], b = idx[f + 1], c = idx[f + 2];
          if (hasVt && hasVn) {
            lines.push(
              'f ' + (vBase + a) + '/' + (vtBase + a) + '/' + (vnBase + a) +
              ' ' + (vBase + b) + '/' + (vtBase + b) + '/' + (vnBase + b) +
              ' ' + (vBase + c) + '/' + (vtBase + c) + '/' + (vnBase + c)
            );
          } else if (hasVn) {
            lines.push(
              'f ' + (vBase + a) + '//' + (vnBase + a) +
              ' ' + (vBase + b) + '//' + (vnBase + b) +
              ' ' + (vBase + c) + '//' + (vnBase + c)
            );
          } else if (hasVt) {
            lines.push(
              'f ' + (vBase + a) + '/' + (vtBase + a) +
              ' ' + (vBase + b) + '/' + (vtBase + b) +
              ' ' + (vBase + c) + '/' + (vtBase + c)
            );
          } else {
            lines.push('f ' + (vBase + a) + ' ' + (vBase + b) + ' ' + (vBase + c));
          }
          triangles++;
        }

        vBase += n;
        if (hasVt) vtBase += n;
        if (hasVn) vnBase += n;
        meshes++;
        lines.push('');
      };

      if (typeof root.traverse === 'function') root.traverse(visit);
    }

    if (!meshes) return null;
    var text = lines.join('\n');
    return {
      buffer: new TextEncoder().encode(text).buffer,
      meshes: meshes,
      triangles: triangles,
      name: baseName
    };
  }

  /* ---------------------------------------------------------------- */
  /* RPC vindo da ponte                                                */
  /* ---------------------------------------------------------------- */

  function reply(id, payload, transfer) {
    payload.__bp3d = 1;
    payload.dir = 'page';
    payload.reply = true;
    payload.id = id;
    try { window.postMessage(payload, '*', transfer || []); } catch (e) { /* ignora */ }
  }

  function cleanName() {
    var t = '';
    try { t = (document.title || '').trim(); } catch (e) { /* ignora */ }
    t = t.replace(/[\\/:*?"<>|]+/g, '_').replace(/\s+/g, '_').slice(0, 60);
    var host = '';
    try { host = location.hostname.replace(/^www\./, '').replace(/[^a-z0-9.-]/gi, '_'); } catch (e) { /* ignora */ }
    return (host || 'cena') + (t ? '_' + t : '') + '_' + Date.now().toString(36);
  }

  window.addEventListener('message', function (ev) {
    if (ev.source !== window) return;
    var d = ev.data;
    if (!d || typeof d !== 'object' || d.__bp3d !== 1 || d.dir !== 'ext') return;
    if (d.reply) return;

    if (d.cmd === 'config') {
      if (d.config && typeof d.config.maxCapture === 'number' && d.config.maxCapture > 0) {
        MAX_CAPTURE = d.config.maxCapture;
      }
      return;
    }

    if (d.cmd === 'ping') {
      reply(d.id, { ok: true, pong: true });
      return;
    }

    if (d.cmd === 'scene-info') {
      try { reply(d.id, { ok: true, info: sceneInfo() }); }
      catch (e) { reply(d.id, { ok: false, error: String(e && e.message ? e.message : e) }); }
      return;
    }

    if (d.cmd === 'export') {
      try {
        var roots = gatherRoots();
        if (!roots.length) {
          reply(d.id, { ok: false, error: 'Nenhuma cena 3D encontrada nesta página. Ative o play do modelo e tente novamente.' });
          return;
        }
        var base = cleanName();
        var res = d.format === 'obj' ? buildOBJ(roots, base) : buildGLB(roots, base);
        if (!res) {
          reply(d.id, { ok: false, error: 'A cena não tem geometria exportável (só câmeras ou luzes).' });
          return;
        }
        reply(d.id, {
          ok: true,
          buffer: res.buffer,
          name: res.name,
          meshes: res.meshes,
          triangles: res.triangles
        }, [res.buffer]);
      } catch (e) {
        reply(d.id, { ok: false, error: String(e && e.message ? e.message : e) });
      }
      return;
    }
  });
})();
