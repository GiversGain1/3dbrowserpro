/* 3D Browser Pro — lógica da página de configurações */
'use strict';

const S = BP3D.settings;
const el = (id) => document.getElementById(id);

let cfg = S.normalize(null);
let saveTimer = null;
let flashTimer = null;

function flash(text, isError) {
  const box = el('saved');
  box.textContent = text;
  box.classList.toggle('on', !isError);
  box.style.color = isError ? '#f87171' : '';
  clearTimeout(flashTimer);
  flashTimer = setTimeout(() => {
    box.classList.remove('on');
    box.style.color = '';
    box.textContent = '—';
  }, 1600);
}

/** Aplica localmente na hora e persiste com debounce. */
function apply(patch) {
  cfg = S.normalize(Object.assign({}, cfg, patch));
  clearTimeout(saveTimer);
  saveTimer = setTimeout(async () => {
    try {
      cfg = await S.save(cfg);
      flash('salvo ✓');
    } catch (e) {
      flash('erro ao salvar', true);
    }
  }, 300);
}

/* ------------------------------------------------------------------ */
/* Chips                                                               */
/* ------------------------------------------------------------------ */

function chip(label, active, onClick) {
  const b = document.createElement('button');
  b.type = 'button';
  b.className = 'chip' + (active ? ' on' : '');
  b.textContent = label;
  b.addEventListener('click', onClick);
  return b;
}

function renderFormats() {
  const box = el('formats');
  box.innerHTML = '';

  BP3D.formats.forEach((f) => {
    const active = cfg.enabledFormats.indexOf(f.id) >= 0;
    box.appendChild(chip(f.label, active, () => {
      const set = new Set(cfg.enabledFormats);
      if (set.has(f.id)) {
        if (set.size === 1) { flash('mantenha ao menos um formato', true); return; }
        set.delete(f.id);
      } else {
        set.add(f.id);
      }
      apply({ enabledFormats: Array.from(set) });
      renderFormats();
    }));
  });
}

function renderAuto() {
  const box = el('auto-formats');
  box.innerHTML = '';

  box.appendChild(chip('Todos', cfg.autoFormats.indexOf('all') >= 0, () => {
    const all = cfg.autoFormats.indexOf('all') >= 0;
    apply({ autoFormats: all ? [] : ['all'] });
    renderAuto();
  }));

  BP3D.formats.forEach((f) => {
    const active = cfg.autoFormats.indexOf(f.id) >= 0 || cfg.autoFormats.indexOf('all') >= 0;
    box.appendChild(chip(f.label, active, () => {
      if (cfg.autoFormats.indexOf('all') >= 0) {
        // saindo do "Todos": mantém só os outros formatos escolhidos
        apply({ autoFormats: [f.id] });
      } else {
        const set = new Set(cfg.autoFormats);
        if (set.has(f.id)) set.delete(f.id); else set.add(f.id);
        apply({ autoFormats: Array.from(set) });
      }
      renderAuto();
    }));
  });
}

/* ------------------------------------------------------------------ */
/* Preencher / ler                                                     */
/* ------------------------------------------------------------------ */

function fill() {
  el('folder').value = cfg.folder;
  el('max-file').value = cfg.maxFileMB;
  el('max-tab').value = cfg.maxTabMB;
  el('badge').checked = !!cfg.badge;
  el('keep').checked = !!cfg.keepOnNavigate;
  renderFormats();
  renderAuto();
}

/* ------------------------------------------------------------------ */
/* Eventos                                                             */
/* ------------------------------------------------------------------ */

el('folder').addEventListener('input', (e) => {
  apply({ folder: e.target.value });
});

el('folder').addEventListener('blur', (e) => {
  const clean = S.folderName(e.target.value);
  e.target.value = clean;
  apply({ folder: clean });
});

el('max-file').addEventListener('change', (e) => {
  const value = Number(e.target.value) || S.DEFAULTS.maxFileMB;
  apply({ maxFileMB: value });
  e.target.value = cfg.maxFileMB;
});

el('max-tab').addEventListener('change', (e) => {
  const value = Number(e.target.value) || S.DEFAULTS.maxTabMB;
  apply({ maxTabMB: value });
  e.target.value = cfg.maxTabMB;
});

el('badge').addEventListener('change', (e) => apply({ badge: e.target.checked }));
el('keep').addEventListener('change', (e) => apply({ keepOnNavigate: e.target.checked }));

el('open-folder').addEventListener('click', () => {
  try {
    chrome.downloads.showDefaultFolder();
    flash('pasta aberta');
  } catch (e) {
    flash('não disponível', true);
  }
});

el('reset').addEventListener('click', async () => {
  cfg = await S.save(S.normalize(null));
  fill();
  flash('padrões restaurados');
});

el('clear-all').addEventListener('click', async () => {
  const res = await chrome.runtime.sendMessage({ type: 'clear-all' });
  flash(res && res.ok ? 'capturas limpas' : 'falha ao limpar', !(res && res.ok));
});

/* ------------------------------------------------------------------ */
/* Boot                                                                */
/* ------------------------------------------------------------------ */

(async function boot() {
  try {
    cfg = await S.load();
  } catch (e) {
    cfg = S.normalize(null);
  }
  fill();
})();
