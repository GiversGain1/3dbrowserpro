/* 3D Browser Pro (app próprio) — painel de capturas */
(function () {
  'use strict';

  const host = window.chrome && window.chrome.webview;
  const el = (id) => document.getElementById(id);

  let items = [];
  let totalBytes = 0;
  let filterFormat = 'all';
  let query = '';
  let scene = null;
  let showRig = true;   // opção marcável "ver esqueleto" na prévia

  try { showRig = localStorage.getItem('bp3d.verEsqueleto') !== '0'; } catch (e) { }

  const FORMAT_LABEL = {
    glb: 'GLB', gltf: 'glTF', obj: 'OBJ', fbx: 'FBX', stl: 'STL',
    ply: 'PLY', dae: 'DAE', '3ds': '3DS', usdz: 'USDZ', usd: 'USD', unknown: '?'
  };

  // formatos que o visualizador interno sabe abrir
  const VIEWABLE = { glb: 1, gltf: 1, obj: 1, fbx: 1, stl: 1, ply: 1, dae: 1, '3ds': 1 };

  function send(msg) {
    if (host) host.postMessage(JSON.stringify(msg));
  }

  function open(a) {
    send(a.inMemory ? { cmd: 'preview', key: a.key } : { cmd: 'preview-url', url: a.url });
  }

  function fmtSize(bytes) {
    const n = Number(bytes) || 0;
    if (n < 1024) return n + ' B';
    if (n < 1048576) return (n / 1024).toFixed(1) + ' KB';
    return (n / 1048576).toFixed(1) + ' MB';
  }

  function hostOf(url) {
    try { return new URL(url).hostname.replace(/^www\./, ''); } catch (e) { return ''; }
  }

  function visible() {
    return items.filter((a) => {
      if (filterFormat !== 'all' && a.format !== filterFormat) return false;
      if (!query) return true;
      const hay = (a.name + ' ' + a.url + ' ' + a.format).toLowerCase();
      return hay.indexOf(query) >= 0;
    });
  }

  /* download novo: pisca o painel e destaca o item, limpando filtro/busca se for preciso */
  function flash(key) {
    document.body.classList.remove('flash');
    void document.body.offsetWidth;
    document.body.classList.add('flash');
    setTimeout(() => document.body.classList.remove('flash'), 2600);

    if (!key) return;

    if (!visible().some((a) => a.key === key)) {
      filterFormat = 'all';
      query = '';
      el('search').value = '';
      render();
    }

    const row = Array.prototype.find.call(document.querySelectorAll('.item'), (li) => li.dataset.key === key);
    if (!row) return;
    row.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
    row.classList.add('hit');
    setTimeout(() => row.classList.remove('hit'), 2600);
  }

  function renderChips() {
    const counts = {};
    items.forEach((a) => { counts[a.format] = (counts[a.format] || 0) + 1; });
    const chips = [['all', 'todos', items.length]];
    Object.keys(counts).sort().forEach((f) => chips.push([f, FORMAT_LABEL[f] || f, counts[f]]));

    el('chips').innerHTML = '';
    chips.forEach(([id, label, count]) => {
      const b = document.createElement('button');
      b.className = 'chip' + (filterFormat === id ? ' on' : '');
      b.textContent = label + ' ' + count;
      b.onclick = () => { filterFormat = id; render(); };
      el('chips').appendChild(b);
    });
  }

  function renderList() {
    const list = el('list');
    list.innerHTML = '';
    const rows = visible();

    rows.forEach((a) => {
      const li = document.createElement('li');
      li.className = 'item';
      li.dataset.key = a.key || '';

      const badge = document.createElement('div');
      badge.className = 'badge';
      badge.textContent = FORMAT_LABEL[a.format] || a.format || '?';

      /* glb/gltf ganham o holograma do esqueleto no lugar do selo de formato */
      let first = badge;
      if (showRig && (a.format === 'glb' || a.format === 'gltf')) {
        const holo = document.createElement('div');
        holo.className = 'holo';
        holo.dataset.key = a.key || '';
        holo.title = 'holograma do esqueleto — clique para abrir no visualizador';
        holo.innerHTML = '<span class="fmt">' + (FORMAT_LABEL[a.format] || a.format) + '</span>';
        holo.onclick = (e) => { e.stopPropagation(); open(a); };
        li.appendChild(holo);
        send({ cmd: 'rig-thumb', key: a.key });
        first = holo;
      }

      const meta = document.createElement('div');
      meta.className = 'meta';
      const name = document.createElement('span');
      name.className = 'name';
      name.textContent = a.name || a.format;
      name.title = a.url || a.name || '';
      const sub = document.createElement('span');
      sub.className = 'sub';
      sub.textContent = fmtSize(a.size) + (hostOf(a.url) ? ' · ' + hostOf(a.url) : '') + (a.inMemory ? ' · memória' : '');
      meta.appendChild(name);
      meta.appendChild(sub);

      const acts = document.createElement('div');
      acts.className = 'acts';

      const pv = document.createElement('button');
      pv.className = 'mini';
      pv.textContent = '👁';
      if (VIEWABLE[a.format]) {
        pv.title = 'Ver em 3D';
        pv.onclick = () => open(a);
      } else {
        pv.disabled = true;
        pv.title = 'O visualizador abre glb, gltf, obj, fbx, stl, ply, dae e 3ds';
      }
      acts.appendChild(pv);

      const dl = document.createElement('button');
      dl.className = 'mini';
      dl.textContent = '⭳';
      dl.title = 'Baixar este arquivo';
      dl.onclick = () => send({ cmd: a.inMemory ? 'download' : 'open-url', key: a.key, url: a.url });
      acts.appendChild(dl);

      if (a.format === 'glb') {
        const cv = document.createElement('button');
        cv.className = 'mini';
        cv.textContent = '.gltf';
        cv.title = 'Converter para glTF autocontido';
        cv.onclick = () => send({ cmd: 'convert', key: a.key });
        acts.appendChild(cv);
      }

      if (a.format === 'glb' || a.format === 'gltf') {
        const fb = document.createElement('button');
        fb.className = 'mini';
        fb.textContent = 'fbx';
        fb.title = 'Converter para FBX com esqueleto humanoide (T-pose, Mixamo) — a qualidade vem da aba do visualizador / ⚙';
        fb.onclick = () => send({ cmd: 'fbx', key: a.key });
        acts.appendChild(fb);
      }

      if (a.url && /^https?:/i.test(a.url)) {
        const cp = document.createElement('button');
        cp.className = 'mini';
        cp.textContent = '🔗';
        cp.title = 'Copiar link';
        cp.onclick = () => {
          send({ cmd: 'copy', text: a.url });
          cp.textContent = '✓';
          setTimeout(() => { cp.textContent = '🔗'; }, 1200);
        };
        acts.appendChild(cp);
      }

      if (first === badge) li.appendChild(badge);
      li.appendChild(meta);
      li.appendChild(acts);
      li.ondblclick = () => { if (VIEWABLE[a.format]) open(a); };
      list.appendChild(li);
    });

    el('empty').style.display = rows.length ? 'none' : 'block';
    if (!rows.length && items.length) el('empty').firstChild.textContent = 'Nada com esse filtro.';
    el('btn-all').disabled = rows.length === 0;
  }

  function renderScene() {
    const box = el('scene');
    if (scene && scene.hasScene) {
      box.className = 'scene on';
      box.textContent = scene.meshes + ' malha(s) · ' + (scene.triangles || 0).toLocaleString('pt-BR') +
        ' triângulos · ' + (scene.roots || 0) + ' raiz(es)';
    } else {
      box.className = 'scene';
      box.textContent = scene && scene.hasCanvas
        ? 'Canvas 3D na página, mas nenhuma malha legível. Dê play no modelo.'
        : 'Sem cena 3D detectada nesta página.';
    }
    const canExport = !!(scene && scene.hasScene);
    el('btn-glb').disabled = !canExport;
    el('btn-obj').disabled = !canExport;
  }

  function render() {
    renderChips();
    renderList();
    renderScene();
    const n = items.length;
    el('sub').textContent = n
      ? n + (n === 1 ? ' arquivo' : ' arquivos') + ' · ' + fmtSize(totalBytes)
      : 'nenhum arquivo';
  }

  el('search').addEventListener('input', (e) => {
    query = e.target.value.trim().toLowerCase();
    renderList();
  });

  el('btn-glb').onclick = () => send({ cmd: 'export', format: 'glb' });
  el('btn-obj').onclick = () => send({ cmd: 'export', format: 'obj' });
  el('btn-scan').onclick = () => send({ cmd: 'scan' });
  el('btn-open').onclick = () => send({ cmd: 'open-file' });
  el('btn-all').onclick = () => send({ cmd: 'download-all', keys: visible().map((a) => a.key) });
  el('btn-clear').onclick = () => send({ cmd: 'clear' });
  el('btn-folder').onclick = () => send({ cmd: 'open-folder' });
  el('btn-settings').onclick = () => send({ cmd: 'settings' });
  el('btn-close').onclick = () => send({ cmd: 'hide-panel' });

  const optRig = el('opt-rig');
  const syncRig = () => {
    optRig.checked = showRig;
    optRig.parentElement.classList.toggle('on', showRig);
    optRig.parentElement.title = showRig
      ? 'Holograma do esqueleto ligado — desmarque para ver só o selo do formato'
      : 'Holograma do esqueleto desligado — marque para ver o esqueleto na prévia';
  };

  optRig.onchange = () => {
    showRig = optRig.checked;
    syncRig();
    try { localStorage.setItem('bp3d.verEsqueleto', showRig ? '1' : '0'); } catch (e) { }
    send({ cmd: 'rig-visible', on: showRig });
    render();
  };

  syncRig();
  send({ cmd: 'rig-visible', on: showRig });   // o app aplica o mesmo estado no preview 3D e no download

  if (host) {
    host.addEventListener('message', (e) => {
      let d = e.data;
      if (typeof d === 'string') { try { d = JSON.parse(d); } catch (err) { return; } }
      if (!d || !d.type) return;

      if (d.type === 'assets') {
        items = d.items || [];
        totalBytes = d.totalBytes || 0;
        render();
      } else if (d.type === 'flash') {
        flash(d.key);
      } else if (d.type === 'scene') {
        scene = d.info || null;
        renderScene();
      } else if (d.type === 'rig-thumb') {
        const key = String(d.key || '').replace(/["\\]/g, '');
        const box = document.querySelector('.holo[data-key=' + CSS.escape(String(key)) + ']');
        if (box && d.svg) {
          box.innerHTML = d.svg;
          box.classList.add('on');
        }
      } else if (d.type === 'toast') {
        const box = el('scene');
        box.textContent = d.text || '';
        box.className = 'scene on';
      }
    });
    send({ cmd: 'hello' });
  }

  render();
})();
