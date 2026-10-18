// The scenario editor (Настройки → Сценарий), GhostClick v4.
//
// A scenario is the profile's script (docs/scripts-ui-contract.md): blocks run top to bottom.
// The page edits `p.script` like any other part of the profile slice and sends it whole; the
// engine runs it, validates it (the host's `issues`) and reports the running block. This file
// holds the block catalogue and the editor's rendering; app.js mixes the methods in.
(function () {
  'use strict';

  const { h } = window.preact;
  const html = window.htm.bind(h);
  const { MODS, LABEL, label } = window.GC_LAYOUT;
  const { t: T } = window.GC_I18N;

  const HISTORY = 60;
  const INDENT = 28;
  const fmt = b => (b ? [...b.mods, label(b.key)].join(' + ') : '—');
  const key = (code, ...mods) => ({ mods, key: code });
  const xy = (x, y) => ({ x, y });
  const clone = v => JSON.parse(JSON.stringify(v));
  const evMods = e => [...(e.ctrlKey ? ['Ctrl'] : []), ...(e.shiftKey ? ['Shift'] : []), ...(e.altKey ? ['Alt'] : []), ...(e.metaKey ? ['Win'] : [])];
  const sameKey = (a, b) => !!a && !!b && a.key === b.key && a.mods.slice().sort().join() === b.mods.slice().sort().join();

  // ---------------------------------------------------------------- catalogue

  // Group of each type, in the order the «Добавить блок» list shows them.
  const GROUP = {
    press: 'mk', hold: 'mk', release: 'mk', holdFor: 'mk', scroll: 'mk', drag: 'mk', type: 'mk',
    move: 'cur', shift: 'cur',
    wait: 'time', waitKey: 'time',
    repeat: 'ctl', random: 'ctl', stop: 'ctl', label: 'ctl',
  };
  const GROUP_COLOR = { mk: '#0a84ff', cur: '#bf5af2', time: '#ff9f0a', ctl: '#5e5ce6' };
  const colorOf = type => (type === 'stop' ? '#ff453a' : GROUP_COLOR[GROUP[type]] || '#636366');
  const isContainer = b => b.type === 'repeat' || b.type === 'random';
  const known = b => !!GROUP[b.type];
  const titleOf = b => (known(b) ? T('b_' + b.type) : b.type || T('b_unknown'));

  // What a new block starts with, in the host's vocabulary.
  const DEFAULTS = {
    press: () => ({ input: key('Mouse1'), click: 'single', dur: 'profile', press: 30, at: null }),
    hold: () => ({ input: key('KeyW'), at: null }),
    release: () => ({ input: null, at: null }),
    holdFor: () => ({ input: key('KeyE'), ms: 1500, rand: false, minMs: 1000, maxMs: 2000 }),
    scroll: () => ({ dir: 'down', count: 3, at: null }),
    drag: () => ({ input: key('Mouse1'), at: xy(800, 500), to: xy(1100, 500), speed: 'mid' }),
    type: () => ({ text: '', ms: 30 }),
    move: () => ({ to: xy(960, 540), how: 'instant', ms: 300 }),
    shift: () => ({ dx: 50, dy: 0 }),
    wait: () => ({ ms: 500, rand: false, minMs: 400, maxMs: 700, unit: 'ms' }),
    waitKey: () => ({ input: key('Space') }),
    repeat: () => ({ mode: 'fixed', count: 10, blocks: [] }),
    random: () => ({ blocks: [] }),
    stop: () => ({}),
    label: () => ({ text: T('sc_newLabel') }),
  };

  // Lucide icons (ISC), as in the design's SICO.
  const ICONS = {
    press: ['M9.037 9.69a.498.498 0 0 1 .653-.653l11 4.5a.5.5 0 0 1-.074.949l-4.349 1.041a1 1 0 0 0-.74.739l-1.04 4.35a.5.5 0 0 1-.95.074z', 'M14 4.1 12 6', 'm5.1 8-2.9-.8', 'm6 12-1.9 2', 'M7.2 2.2 8 5.1'],
    hold: ['M12 3v14', 'm6 11 6 6 6-6', 'M19 21H5'],
    release: ['m18 9-6-6-6 6', 'M12 3v14', 'M5 21h14'],
    holdFor: ['M5 22h14', 'M5 2h14', 'M17 22v-4.172a2 2 0 0 0-.586-1.414L12 12l-4.414 4.414A2 2 0 0 0 7 17.828V22', 'M7 2v4.172a2 2 0 0 0 .586 1.414L12 12l4.414-4.414A2 2 0 0 0 17 6.172V2'],
    scroll: [['rect', { x: 5, y: 2, width: 14, height: 20, rx: 7 }], 'M12 6v4'],
    drag: ['M12 2v20', 'm15 19-3 3-3-3', 'm19 9 3 3-3 3', 'M2 12h20', 'm5 9-3 3 3 3', 'm9 5 3-3 3 3'],
    type: ['M4 7V4h16v3', 'M9 20h6', 'M12 4v16'],
    move: ['M4.037 4.688a.495.495 0 0 1 .651-.651l16 6.5a.5.5 0 0 1-.063.947l-6.124 1.58a2 2 0 0 0-1.438 1.435l-1.579 6.126a.5.5 0 0 1-.947.063z'],
    shift: ['M18 8l4 4-4 4', 'M2 12h20'],
    wait: [['circle', { cx: 12, cy: 12, r: 10 }], 'M12 6v6l4 2'],
    waitKey: [['rect', { x: 2, y: 4, width: 20, height: 16, rx: 2 }], 'M6 8h.01', 'M10 8h.01', 'M14 8h.01', 'M18 8h.01', 'M8 12h.01', 'M12 12h.01', 'M16 12h.01', 'M7 16h10'],
    repeat: ['m17 2 4 4-4 4', 'M3 11v-1a4 4 0 0 1 4-4h14', 'm7 22-4-4 4-4', 'M21 13v1a4 4 0 0 1-4 4H3'],
    random: ['m18 14 4 4-4 4', 'm18 2 4 4-4 4', 'M2 18h1.973a4 4 0 0 0 3.3-1.7l5.454-8.6a4 4 0 0 1 3.3-1.7H22', 'M2 6h1.972a4 4 0 0 1 3.3 1.7l.69 1.088', 'M22 18h-6.041a4 4 0 0 1-3.3-1.8l-.359-.45'],
    stop: [['rect', { x: 5, y: 5, width: 14, height: 14, rx: 2 }]],
    label: ['M6 12h12', 'M6 20V4', 'M18 20V4'],
    unknown: [['circle', { cx: 12, cy: 12, r: 10 }], 'M12 8v4', 'M12 16h.01'],
    layers: ['m12.83 2.18a2 2 0 0 0-1.66 0L2.6 6.08a1 1 0 0 0 0 1.83l8.58 3.91a2 2 0 0 0 1.66 0l8.58-3.9a1 1 0 0 0 0-1.83Z', 'm22 17.65-9.17 4.16a2 2 0 0 1-1.66 0L2 17.65', 'm22 12.65-9.17 4.16a2 2 0 0 1-1.66 0L2 12.65'],
  };

  function icon(name, size, stroke) {
    const parts = ICONS[name] || ICONS.unknown;
    return html`<svg class="scn-icon" width=${size || 16} height=${size || 16} viewBox="0 0 24 24" style=${`stroke:${stroke || '#fff'};`}>
      ${parts.map((p, i) => (typeof p === 'string' ? html`<path key=${i} d=${p}></path>` : h(p[0], { key: i, ...p[1] })))}
    </svg>`;
  }

  const SMALL = {
    minus: html`<svg width="12" height="12" viewBox="0 0 24 24"><path d="M5 12h14"></path></svg>`,
    plus: html`<svg width="12" height="12" viewBox="0 0 24 24"><path d="M5 12h14M12 5v14"></path></svg>`,
    grip: html`<svg width="12" height="18" viewBox="0 0 12 18" style="display:block; fill:#636366;"><circle cx="3" cy="3" r="1.6"></circle><circle cx="9" cy="3" r="1.6"></circle><circle cx="3" cy="9" r="1.6"></circle><circle cx="9" cy="9" r="1.6"></circle><circle cx="3" cy="15" r="1.6"></circle><circle cx="9" cy="15" r="1.6"></circle></svg>`,
    copy: html`<svg width="15" height="15" viewBox="0 0 24 24" class="scn-line" style="stroke:#aeaeb2;"><rect width="14" height="14" x="8" y="8" rx="2" ry="2"></rect><path d="M4 16c-1.1 0-2-.9-2-2V4c0-1.1.9-2 2-2h10c1.1 0 2 .9 2 2"></path></svg>`,
    trash: html`<svg width="15" height="15" viewBox="0 0 24 24" class="scn-line" style="stroke:#ff453a;"><path d="M3 6h18"></path><path d="M19 6v14c0 1-1 2-2 2H7c-1 0-2-1-2-2V6"></path><path d="M8 6V4c0-1 1-2 2-2h4c1 0 2 1 2 2v2"></path></svg>`,
    chevron: html`<svg width="9" height="15" viewBox="0 0 9 15" class="scn-chevron"><path d="m1.5 1.5 6 6-6 6"></path></svg>`,
    error: html`<svg width="13" height="13" viewBox="0 0 24 24" class="scn-line" style="stroke:#ff453a; stroke-width:2.4;"><circle cx="12" cy="12" r="10"></circle><path d="M12 8v4"></path><path d="M12 16h.01"></path></svg>`,
    play: html`<svg width="12" height="12" viewBox="0 0 24 24" style="display:block; fill:#30d158;"><path d="M6 3.5v17a1 1 0 0 0 1.5.86l14-8.5a1 1 0 0 0 0-1.72l-14-8.5A1 1 0 0 0 6 3.5z"></path></svg>`,
    playSmall: html`<svg width="10" height="10" viewBox="0 0 24 24" style="display:block; fill:#30d158;"><path d="M6 3.5v17a1 1 0 0 0 1.5.86l14-8.5a1 1 0 0 0 0-1.72l-14-8.5A1 1 0 0 0 6 3.5z"></path></svg>`,
    stop: html`<svg width="11" height="11" viewBox="0 0 24 24" style="display:block; fill:#ff453a;"><rect x="3" y="3" width="18" height="18" rx="3"></rect></svg>`,
    undo: html`<svg width="17" height="17" viewBox="0 0 24 24" class="scn-line" style="stroke:#0a84ff; stroke-width:2.2;"><path d="M9 14 4 9l5-5"></path><path d="M4 9h10.5a5.5 5.5 0 0 1 5.5 5.5a5.5 5.5 0 0 1-5.5 5.5H11"></path></svg>`,
    redo: html`<svg width="17" height="17" viewBox="0 0 24 24" class="scn-line" style="stroke:#0a84ff; stroke-width:2.2;"><path d="m15 14 5-5-5-5"></path><path d="M20 9H9.5A5.5 5.5 0 0 0 4 14.5A5.5 5.5 0 0 0 9.5 20H13"></path></svg>`,
    add: html`<svg width="15" height="15" viewBox="0 0 24 24" class="scn-line" style="stroke:#0a84ff; stroke-width:2.8;"><path d="M5 12h14M12 5v14"></path></svg>`,
    search: html`<svg width="15" height="15" viewBox="0 0 24 24" class="scn-line" style="stroke:#8e8e93; stroke-width:2.4;"><circle cx="11" cy="11" r="8"></circle><path d="m21 21-4.3-4.3"></path></svg>`,
    target: html`<svg width="14" height="14" viewBox="0 0 24 24" class="scn-line" style="stroke:#0a84ff; stroke-width:2.2;"><circle cx="12" cy="12" r="9"></circle><path d="M22 12h-4"></path><path d="M6 12H2"></path><path d="M12 6V2"></path><path d="M12 22v-4"></path></svg>`,
  };

  // ---------------------------------------------------------------- tree helpers

  function find(list, id) {
    for (let i = 0; i < (list || []).length; i++) {
      const b = list[i];
      if (!b) continue;
      if (b.id === id) return { list, idx: i, node: b };
      const inner = isContainer(b) ? find(b.blocks, id) : null;
      if (inner) return inner;
    }
    return null;
  }

  const walkAll = (list, fn) => (list || []).forEach(b => { if (!b) return; fn(b); if (isContainer(b)) walkAll(b.blocks, fn); });
  const countAll = list => { let n = 0; walkAll(list, () => n++); return n; };
  const contains = (b, id) => b.id === id || (isContainer(b) && (b.blocks || []).some(c => contains(c, id)));

  // Eight hex digits no other block uses; `taken` holds ids handed out but not yet in the list.
  function newId(blocks, taken) {
    const used = new Set(taken || []);
    walkAll(blocks, b => used.add(b.id));
    for (;;) {
      const id = Math.random().toString(16).slice(2, 10).padEnd(8, '0');
      if (!used.has(id)) {
        if (taken) taken.add(id);
        return id;
      }
    }
  }

  // A copy with fresh ids all the way down, for «Дублировать».
  function renew(b, blocks) {
    const copy = clone(b);
    const taken = new Set();
    walkAll([copy], n => { n.id = newId(blocks, taken); });
    return copy;
  }

  // ---------------------------------------------------------------- timing

  // How long the profile's clicks last: what «Из профиля» means for «Нажать».
  const profilePress = p => {
    const a = (p.actions || []).find(x => x.mode !== 'hold');
    return a ? a.press ?? 30 : 30;
  };

  function duration(b, p) {
    const avg = (a, z) => (a + z) / 2;
    switch (b.type) {
      case 'press': return (b.click === 'double' ? 2 : 1) * (b.dur === 'profile' ? profilePress(p) : b.press ?? 30);
      case 'holdFor': return b.rand ? avg(b.minMs, b.maxMs) : b.ms;
      case 'drag': return { slow: 1000, mid: 500, fast: 250 }[b.speed] || 500;
      case 'type': return (b.text || '').length * (b.ms || 0);
      case 'move': return b.how === 'smooth' ? b.ms || 0 : 0;
      case 'wait': return b.rand ? avg(b.minMs, b.maxMs) : b.ms;
      default: return 0;
    }
  }

  // One lap: time, whether it waits for a key, whether it never ends. A «Ждать» replaces the
  // pause between steps, as in the engine.
  function estimate(list, pause, p) {
    let ms = 0, waitsKey = false, endless = false;
    for (const b of list || []) {
      if (!b || b.enabled === false || b.type === 'label' || !known(b)) continue;
      if (b.type === 'repeat') {
        const inner = estimate(b.blocks, pause, p);
        waitsKey = waitsKey || inner.waitsKey;
        if (inner.endless || (b.mode === 'inf' && (b.blocks || []).some(c => c && c.enabled !== false))) endless = true;
        else ms += Math.max(1, b.count || 1) * inner.ms;
      } else if (b.type === 'random') {
        const choices = (b.blocks || []).filter(c => c && c.enabled !== false).map(c => estimate([c], pause, p));
        if (choices.length) {
          ms += choices.reduce((all, c) => all + c.ms, 0) / choices.length;
          waitsKey = waitsKey || choices.some(c => c.waitsKey);
          endless = endless || choices.some(c => c.endless);
        }
      } else {
        if (b.type === 'stop') break;
        if (b.type === 'waitKey') waitsKey = true;
        ms += duration(b, p) + (b.type === 'wait' ? 0 : pause);
      }
    }
    return { ms, waitsKey, endless };
  }

  // Blocks a run goes through, for «Шаг i из N»; null when it cannot be known in advance.
  function steps(list) {
    let n = 0;
    for (const b of list || []) {
      if (!b || b.enabled === false || b.type === 'label') continue;
      if (b.type === 'stop') return { n, stops: true };
      if (b.type === 'repeat') {
        const inner = steps(b.blocks);
        if (!inner) return null;
        if (inner.stops) return { n: n + inner.n, stops: true };
        if (b.mode === 'inf') return null;
        n += Math.max(1, b.count || 1) * inner.n;
      } else if (b.type === 'random') {
        const choices = (b.blocks || []).filter(c => c && c.enabled !== false).map(c => steps([c]));
        if (!choices.length) continue;
        if (choices.some(c => !c || c.stops) || new Set(choices.map(c => c.n)).size > 1) return null;
        n += choices[0].n;
      } else n += 1;
    }
    return { n, stops: false };
  }

  function totalSteps(script) {
    const lap = steps(script.blocks);
    if (!lap) return null;
    if (lap.stops || script.repeat === 'once') return lap.n;
    return script.repeat === 'fixed' ? lap.n * Math.max(1, script.count || 1) : null;
  }

  // ---------------------------------------------------------------- summaries

  const sec = ms => String(Math.round((ms || 0) / 100) / 10);
  const xyText = pt => (pt ? `${pt.x}, ${pt.y}` : '');

  function summaryOf(b, p) {
    const k = x => (x ? fmt(x) : '—');
    switch (b.type) {
      case 'press': return [k(b.input), b.click === 'double' ? T('o_double').toLowerCase() : '', b.dur === 'own' ? `${b.press ?? 30} ${T('u_ms')}` : '', xyText(b.at)].filter(Boolean).join(' · ');
      case 'hold': case 'release': case 'waitKey': return k(b.input);
      case 'holdFor': return `${k(b.input)} · ${b.rand ? `${sec(b.minMs)}–${sec(b.maxMs)}` : sec(b.ms)} ${T('u_s')}`;
      case 'scroll': return `${T(b.dir === 'up' ? 'op_up' : 'op_down').toLowerCase()} · ${T('notches', b.count ?? 1)}${b.at ? ` · ${xyText(b.at)}` : ''}`;
      case 'drag': return `${label((b.input && b.input.key) || 'Mouse1')} · ${xyText(b.at)} → ${xyText(b.to)}`;
      case 'type': return b.text ? T('qt', b.text) : '';
      case 'move': {
        const how = b.how || p.move;
        return `${xyText(b.to)}${how === 'smooth' ? ` · ${T('o_smooth').toLowerCase()} ${b.how ? `${b.ms} ${T('u_ms')}` : ''}`.trimEnd() : ''}`;
      }
      case 'shift': return `${b.dx >= 0 ? '+' : ''}${b.dx}, ${b.dy >= 0 ? '+' : ''}${b.dy} px`;
      case 'wait': {
        const s = b.unit === 's';
        const v = x => (s ? sec(x) : String(x));
        return `${b.rand ? `${v(b.minMs)}–${v(b.maxMs)}` : v(b.ms)} ${T(s ? 'u_s' : 'u_ms')}`;
      }
      case 'repeat': return b.mode === 'inf' ? '∞' : T('timesN', b.count ?? 1);
      case 'random': return T('nVariants', (b.blocks || []).length);
      case 'label': return b.text || '';
      default: return '';
    }
  }

  // The problem the host reports for a block, in the interface's words where it has some.
  function errorOf(b, issue, held) {
    if (!issue || b.enabled === false) return '';
    switch (issue.field) {
      case 'input': return b.type === 'release' && !held.length ? T('e_relNone') : T('e_key');
      case 'held': return held.length ? T('e_rel', fmt(b.input)) : T('e_relNone');
      case 'text': return T('e_text');
      case 'blocks': return T('e_empty');
      default: return issue.message;
    }
  }

  // ---------------------------------------------------------------- the editor (mixed into App)

  const methods = {
    scnScript() {
      return this.p.script || { repeat: 'once', count: 10, stepDelay: 100, blocks: [] };
    },

    setScript(patch) {
      this.setP({ script: { ...this.scnScript(), ...patch } });
    },

    // Every change to the blocks goes through here, so it can be undone.
    setBlocks(blocks) {
      const old = this.scnScript().blocks || [];
      this.setState(s => ({ hist: [...s.hist, old].slice(-HISTORY), fut: [] }));
      this.setScript({ blocks });
    },

    scnEdit(fn) {
      const blocks = clone(this.scnScript().blocks || []);
      if (fn(blocks) !== false) this.setBlocks(blocks);
    },

    scnSet(id, patch) {
      this.scnEdit(blocks => {
        const at = find(blocks, id);
        if (!at) return false;
        Object.assign(at.node, patch);
        return undefined;
      });
    },

    scnUndo() {
      const { hist } = this.state;
      if (!hist.length) return;
      const current = this.scnScript().blocks || [];
      this.setState(s => ({ hist: s.hist.slice(0, -1), fut: [current, ...s.fut] }));
      this.setScript({ blocks: hist[hist.length - 1] });
    },

    scnRedo() {
      const { fut } = this.state;
      if (!fut.length) return;
      const current = this.scnScript().blocks || [];
      this.setState(s => ({ fut: s.fut.slice(1), hist: [...s.hist, current].slice(-HISTORY) }));
      this.setScript({ blocks: fut[0] });
    },

    scnAdd(type) {
      const blocks = clone(this.scnScript().blocks || []);
      const block = { type, id: newId(blocks), enabled: true, label: null, ...DEFAULTS[type]() };
      blocks.push(block);
      this.setBlocks(blocks);
      this.setState(s => ({ adding: false, q: '', exp: { ...s.exp, [block.id]: true } }));
    },

    scnDup(id) {
      this.scnEdit(blocks => {
        const at = find(blocks, id);
        if (!at) return false;
        at.list.splice(at.idx + 1, 0, renew(at.node, blocks));
        return undefined;
      });
    },

    scnDel(id) {
      this.scnEdit(blocks => {
        const at = find(blocks, id);
        if (!at) return false;
        at.list.splice(at.idx, 1);
        return undefined;
      });
    },

    // Drops the dragged block on a row: before or after it, first inside a container (its
    // lower half), last inside one or right after it (its «Конец» line).
    scnMove(dragId, row, pos) {
      this.scnEdit(blocks => {
        const take = find(blocks, dragId);
        if (!take || contains(take.node, row.id)) return false;
        take.list.splice(take.idx, 1);
        const target = find(blocks, row.id);
        if (!target) return false;
        if (row.kind === 'end') {
          if (pos === 'top') target.node.blocks.push(take.node);
          else target.list.splice(target.idx + 1, 0, take.node);
        } else if (pos === 'top') target.list.splice(target.idx, 0, take.node);
        else if (isContainer(target.node)) target.node.blocks.unshift(take.node);
        else target.list.splice(target.idx + 1, 0, take.node);
        return undefined;
      });
    },

    // Keys the editor handles before anything else; true when it took the key.
    scnKeyDown(e) {
      const st = this.state;
      if (st.cap) {
        e.preventDefault();
        if (MODS[e.code] || e.repeat) return true;
        if (e.code === 'Escape') { this.setState({ cap: null }); return true; }
        if (!LABEL[e.code]) return true;
        const cap = st.cap;
        this.setState({ cap: null });
        this.scnSet(cap.id, { input: { mods: evMods(e), key: e.code } });
        return true;
      }

      // The key a running «Ждать клавишу» waits for must not also scroll or click the page.
      const waiting = st.run !== 'stopped' && st.block && find(this.scnScript().blocks, st.block.id);
      if (waiting && waiting.node.type === 'waitKey' && waiting.node.input && waiting.node.input.key === e.code) e.preventDefault();

      if (st.picking) {
        if (e.code === 'Escape') this.setState({ picking: null, pickPos: null, settings: true });
        return true;
      }

      if (st.settings && st.page === 'scenario' && (e.ctrlKey || e.metaKey) && (e.code === 'KeyZ' || e.code === 'KeyY')) {
        e.preventDefault();
        if (e.code === 'KeyZ' && !e.shiftKey) this.scnUndo(); else this.scnRedo();
        return true;
      }

      if (e.code === 'Escape' && st.settings && st.adding) {
        this.setState({ adding: false, q: '' });
        return true;
      }

      return false;
    },

    // A point picked with the point hotkey (F9) while picking: from any window.
    scnPointHotkey(x, y) {
      const pick = this.state.picking;
      if (!pick) return false;
      this.scnSet(pick.id, { [pick.field]: xy(x, y) });
      this.setState({ picking: null, pickPos: null, settings: true });
      this.toast(T('sc_picked', x, y));
      return true;
    },

    testRun(from) {
      if (this.state.testing) {
        this.host.send({ type: 'testStop' });
        return;
      }
      const script = this.scnScript();
      this.setState({ testStep: 0, testTotal: from ? null : totalSteps(script) });
      this.host.send(from ? { type: 'testRun', from } : { type: 'testRun' });
    },

    testError(m) {
      const at = m.block && find(this.scnScript().blocks, m.block);
      if (at) {
        this.setState(s => ({ exp: { ...s.exp, [at.node.id]: true } }));
        this.toast(T('sc_testErr', titleOf(at.node)), true);
      } else {
        this.toast(m.message, true);
      }
    },

    // -------------------------------------------------------------- rendering

    scnField(f, i) {
      const sep = i ? ' sep' : '';
      const number = (value, onSet, min, isInt) => html`
        <input class="scn-number" type="number" value=${value} onChange=${e => {
          const v = isInt ? parseInt(e.target.value, 10) : parseFloat(e.target.value);
          if (isNaN(v)) { e.target.value = String(value); return; }
          onSet(Math.max(min, v));
        }} />`;

      let control = null;
      if (f.kind === 'seg') {
        control = html`<div class="segments">${f.opts.map(([v, text]) => html`
          <button key=${v} class="segment narrow" style=${`background:${f.value === v ? '#636366' : 'transparent'};`} onClick=${() => f.set(v)}>${text}</button>`)}</div>`;
      } else if (f.kind === 'num') {
        control = html`<div class="scn-stepper">
          <button class="scn-step" onClick=${() => f.set(Math.max(f.min, Math.round((f.value - f.step) * 10) / 10))}>${SMALL.minus}</button>
          ${number(f.value, f.set, f.min, f.int)}
          <button class="scn-step" onClick=${() => f.set(Math.round((f.value + f.step) * 10) / 10)}>${SMALL.plus}</button>
          <span class="scn-unit">${f.unit}</span>
        </div>`;
      } else if (f.kind === 'range') {
        control = html`<div class="scn-stepper">
          ${number(f.from, f.setFrom, 0, false)}<span class="dash">–</span>${number(f.to, f.setTo, 0, false)}
          <span class="scn-unit">${f.unit}</span>
        </div>`;
      } else if (f.kind === 'key') {
        const capOn = this.state.cap && this.state.cap.id === f.id;
        control = html`<div class="scn-keys">
          <span class="scn-key-value" style=${f.value ? 'background:#0a84ff; color:#000;' : 'background:rgba(118,118,128,0.22); color:#8e8e93;'}>${f.value ? fmt(f.value) : T('sc_none')}</span>
          ${f.mouse && ['Mouse1', 'Mouse2', 'Mouse3', 'Mouse4', 'Mouse5'].map(m => html`
            <button key=${m} class="scn-chip" style=${`background:${f.value && f.value.key === m && !f.value.mods.length ? '#636366' : 'rgba(118,118,128,0.22)'};`}
              onClick=${() => f.set(key(m))}>${label(m)}</button>`)}
          <button class="scn-chip wide" style=${capOn ? 'background:#0a84ff; color:#000;' : ''}
            onClick=${() => this.setState({ cap: capOn ? null : { id: f.id } })}>${T(capOn ? 'sc_pressKey' : 'sc_keyBtn')}</button>
        </div>`;
      } else if (f.kind === 'held') {
        control = html`<div class="scn-keys">
          ${f.held.map((hk, j) => {
            const on = sameKey(f.value, hk);
            return html`<button key=${j} class="scn-chip" style=${on ? 'background:#0a84ff; color:#000;' : ''} onClick=${() => f.set(hk)}>${fmt(hk)}</button>`;
          })}
          ${!f.held.length && html`<span class="scn-muted">${T('sc_noneHeld')}</span>`}
        </div>`;
      } else if (f.kind === 'point') {
        const pt = f.value || xy(0, 0);
        control = html`<div class="scn-stepper">
          <span class="scn-muted">X</span>${number(pt.x, x => f.set(xy(Math.round(x), pt.y)), -99999, true)}
          <span class="scn-muted pad">Y</span>${number(pt.y, y => f.set(xy(pt.x, Math.round(y))), -99999, true)}
          <button class="scn-pick" onClick=${() => this.setState({ picking: { id: f.id, field: f.field }, pickPos: null, settings: false })}>${SMALL.target}${T('sc_pick')}</button>
        </div>`;
      } else if (f.kind === 'text') {
        control = html`<input class="scn-text" type="text" value=${f.value || ''} placeholder=${f.ph || ''}
          onChange=${e => f.set(e.target.value)} />`;
      }

      return html`<div key=${f.key || i} class=${'scn-field' + sep}><span class="scn-field-label">${f.label}</span>${control}</div>`;
    },

    scnFields(b, held) {
      const p = this.p;
      const set = patch => this.scnSet(b.id, patch);
      const fields = [];
      const seg = (label_, value, opts, onSet) => fields.push({ kind: 'seg', label: label_, value, opts, set: onSet });
      const num = (label_, value, unit, step, min, onSet, int) => fields.push({ kind: 'num', label: label_, value, unit, step, min, set: onSet, int });
      const keyField = (label_, mouse) => fields.push({ kind: 'key', id: b.id, label: label_, value: b.input, mouse, set: v => set({ input: v }) });
      const point = (label_, field) => fields.push({ kind: 'point', id: b.id, field, label: label_, value: b[field], set: v => set({ [field]: v }) });
      const at = () => {
        seg(T('f_where'), b.at ? 'point' : 'cursor', [['cursor', T('op_cursor')], ['point', T('op_point')]],
          v => set({ at: v === 'point' ? b.at || xy(960, 540) : null }));
        if (b.at) point(T('f_point'), 'at');
      };
      const timing = (label_, inSeconds) => {
        const unit = T(inSeconds ? 'u_s' : 'u_ms');
        const toUi = ms => (inSeconds ? Math.round(ms / 100) / 10 : ms);
        const fromUi = v => Math.round(inSeconds ? v * 1000 : v);
        seg(label_, b.rand ? 'rand' : 'fixed', [['fixed', T('op_fixed')], ['rand', T('op_rand')]], v => set({ rand: v === 'rand' }));
        if (b.rand) {
          fields.push({ kind: 'range', label: T('f_range'), from: toUi(b.minMs), to: toUi(b.maxMs), unit,
            setFrom: v => set({ minMs: fromUi(v) }), setTo: v => set({ maxMs: fromUi(v) }) });
        } else {
          num(T('f_time'), toUi(b.ms), unit, inSeconds ? 0.1 : 50, 0, v => set({ ms: fromUi(v) }));
        }
      };

      switch (b.type) {
        case 'press':
          keyField(T('f_what'), true);
          seg(T('f_click'), b.click === 'double' ? 'double' : 'single', [['single', T('o_single')], ['double', T('o_double')]], v => set({ click: v }));
          seg(T('f_dur'), b.dur === 'profile' ? 'profile' : 'own', [['profile', T('op_profile')], ['own', T('op_own')]], v => set({ dur: v }));
          if (b.dur !== 'profile') num(T('f_ms'), b.press ?? 30, T('u_ms'), 10, 1, v => set({ press: Math.round(v) }), true);
          at();
          break;
        case 'hold':
          keyField(T('f_whatHold'), true);
          break;
        case 'release':
          fields.push({ kind: 'held', label: T('f_whatRel'), value: b.input, held, set: v => set({ input: v }) });
          break;
        case 'holdFor':
          keyField(T('f_whatKeep'), true);
          timing(T('f_time'), true);
          break;
        case 'scroll':
          seg(T('f_dir'), b.dir === 'up' ? 'up' : 'down', [['up', T('op_up')], ['down', T('op_down')]], v => set({ dir: v }));
          num(T('f_count'), b.count ?? 1, T('u_notch'), 1, 1, v => set({ count: Math.round(v) }), true);
          at();
          break;
        case 'drag':
          seg(T('f_btn'), (b.input && b.input.key) || 'Mouse1', ['Mouse1', 'Mouse2', 'Mouse3'].map(m => [m, label(m)]), v => set({ input: key(v) }));
          point(T('f_from'), 'at');
          point(T('f_to'), 'to');
          seg(T('f_speed'), b.speed || 'mid', [['slow', T('op_slow')], ['mid', T('op_mid')], ['fast', T('op_fast')]], v => set({ speed: v }));
          break;
        case 'type':
          fields.push({ kind: 'text', label: T('f_text'), value: b.text, ph: T('sc_textPh'), set: v => set({ text: v }) });
          num(T('f_charPause'), b.ms ?? 0, T('u_ms'), 10, 0, v => set({ ms: Math.round(v) }), true);
          break;
        case 'move': {
          const how = b.how || p.move;
          point(T('f_point'), 'to');
          seg(T('f_move'), how, [['instant', T('o_instant')], ['smooth', T('o_smooth')]], v => set({ how: v }));
          if (how === 'smooth') num(T('f_moveTime'), b.ms ?? 300, T('u_ms'), 50, 0, v => set({ how: 'smooth', ms: Math.round(v) }), true);
          break;
        }
        case 'shift':
          num(T('f_dx'), b.dx ?? 0, 'px', 10, -9999, v => set({ dx: Math.round(v) }), true);
          num(T('f_dy'), b.dy ?? 0, 'px', 10, -9999, v => set({ dy: Math.round(v) }), true);
          break;
        case 'wait':
          seg(T('f_unit'), b.unit === 's' ? 's' : 'ms', [['ms', T('u_ms')], ['s', T('u_s')]], v => set({ unit: v }));
          timing(T('f_wait'), b.unit === 's');
          break;
        case 'waitKey':
          keyField(T('f_key'), false);
          break;
        case 'repeat':
          seg(T('f_rep'), b.mode === 'inf' ? 'inf' : 'fixed', [['fixed', T('op_n')], ['inf', T('o_inf')]], v => set({ mode: v }));
          if (b.mode !== 'inf') num(T('f_times'), b.count ?? 1, '', 1, 1, v => set({ count: Math.round(v) }), true);
          break;
        case 'label':
          fields.push({ kind: 'text', label: T('f_heading'), value: b.text, set: v => set({ text: v }) });
          break;
        default:
          break;
      }

      if (b.type !== 'label') fields.push({ kind: 'text', key: 'note', label: T('f_note'), value: b.label, ph: T('sc_notePh'), set: v => set({ label: v.trim() ? v : null }) });
      return fields;
    },

    // The list, flattened into rows: block cards, label lines and the «Конец» line that closes
    // a container. Guides are the vertical lines of the containers a row sits in.
    scnRows(blocks) {
      const st = this.state;
      const issues = {};
      (st.issues || []).forEach(i => { if (i.block && !issues[i.block]) issues[i.block] = i; });
      const running = st.run !== 'stopped' && st.block ? st.block.id : null;
      const rows = [];
      const held = [];

      const walk = (list, depth, guides, live) => (list || []).forEach(b => {
        if (!b) return;
        const on = b.enabled !== false;
        const heldBefore = held.slice();
        rows.push({ kind: 'block', b, depth, guides, on: on && live, heldBefore, error: errorOf(b, issues[b.id], heldBefore), running: running === b.id });
        if (on && live) {
          if (b.type === 'hold' && b.input) held.push(b.input);
          if (b.type === 'release' && b.input) {
            const i = held.findIndex(x => sameKey(x, b.input));
            if (i >= 0) held.splice(i, 1);
          }
        }
        if (isContainer(b)) {
          walk(b.blocks, depth + 1, [...guides, depth * INDENT + 13], on && live);
          rows.push({ kind: 'end', b, depth, guides, on: on && live });
        }
      });
      walk(blocks, 0, [], true);
      return rows;
    },

    scnRow(row) {
      const st = this.state;
      const p = this.p;
      const b = row.b;
      const rowKey = (row.kind === 'end' ? 'e' : 'b') + b.id;
      const drop = st.drop && st.drop.key === rowKey ? st.drop.pos : null;
      const indent = row.depth * INDENT;
      const dragging = st.drag != null;

      const onOver = e => {
        if (st.drag == null) return;
        e.preventDefault();
        const rect = e.currentTarget.getBoundingClientRect();
        const pos = e.clientY < rect.top + rect.height / 2 ? 'top' : 'bot';
        const d = this.state.drop;
        if (!d || d.key !== rowKey || d.pos !== pos) this.setState({ drop: { key: rowKey, pos } });
      };
      const onDrop = e => {
        e.preventDefault();
        const dragId = this.state.drag;
        const d = this.state.drop;
        this.setState({ drag: null, drop: null });
        if (dragId != null && d && d.key === rowKey) this.scnMove(dragId, { kind: row.kind, id: b.id }, d.pos);
      };
      const onDragStart = e => {
        e.stopPropagation();
        e.dataTransfer.effectAllowed = 'move';
        try { e.dataTransfer.setData('text/plain', b.id); } catch (x) { /* some hosts refuse custom data */ }
        const el = e.currentTarget.closest('[data-row]');
        if (el) e.dataTransfer.setDragImage(el, 24, 24);
        setTimeout(() => this.setState({ drag: b.id, hov: null }), 0);
      };
      const grip = html`<div class="scn-grip" draggable="true" onDragStart=${onDragStart}
        onDragEnd=${() => this.setState({ drag: null, drop: null })} onClick=${e => e.stopPropagation()}>${SMALL.grip}</div>`;
      const toggle = html`<button class="scn-toggle" style=${`background:${b.enabled !== false ? '#30d158' : 'rgba(120,120,128,0.32)'};`}
        onClick=${e => { e.stopPropagation(); this.scnSet(b.id, { enabled: b.enabled === false }); }}>
        <span class="knob" style=${`left:${b.enabled !== false ? 18 : 2}px;`}></span></button>`;
      const quick = st.hov === b.id && !st.exp[b.id] && !dragging && html`
        <button class="scn-quick" title=${T('sc_dup')} onClick=${e => { e.stopPropagation(); this.scnDup(b.id); }}>${SMALL.copy}</button>
        <button class="scn-quick" title=${T('sc_del')} onClick=${e => { e.stopPropagation(); this.scnDel(b.id); }}>${SMALL.trash}</button>`;
      const hover = {
        onMouseEnter: () => { if (this.state.hov !== b.id) this.setState({ hov: b.id }); },
        onMouseLeave: () => this.setState({ hov: null }),
      };
      const expanded = !!st.exp[b.id];
      const openClose = () => this.setState(s => ({ exp: { ...s.exp, [b.id]: !s.exp[b.id] } }));
      const opacity = row.on ? 1 : 0.4;

      let body;
      if (row.kind === 'end') {
        const title = titleOf(b);
        body = html`<div class="scn-end" style=${`opacity:${opacity};`}>
          <div class="scn-end-hook"></div>
          <span>${T('sc_end', title + (b.type === 'repeat' ? ' · ' + summaryOf(b, p) : ''))}</span>
          ${!(b.blocks || []).length && html`<span class="scn-end-empty">· ${T('sc_dropHere')}</span>`}
        </div>`;
      } else if (b.type === 'label' && !expanded) {
        body = html`<div class="scn-label-row" style=${`opacity:${st.drag === b.id ? 0.35 : 1};`} onClick=${openClose} ...${hover}>
          ${grip}
          <div class="scn-label-line" style=${`opacity:${opacity};`}><div class="rule"></div><span>${b.text}</span><div class="rule"></div></div>
          ${quick}${st.hov === b.id && !dragging && toggle}
        </div>`;
      } else {
        const waiting = row.running && b.type === 'waitKey' && b.input;
        const bg = row.running ? '#173320' : '#1c1c1e';
        const ring = row.running ? '0 0 0 2px #30d158' : row.error ? '0 0 0 1.5px #ff453a' : 'none';
        body = html`<div class="scn-card" style=${`background:${bg}; box-shadow:${ring}; opacity:${st.drag === b.id ? 0.35 : 1};`} ...${hover}>
          <div class="scn-head" onClick=${openClose}>
            ${grip}
            <div class="scn-head-main" style=${`opacity:${opacity};`}>
              <div class="scn-tile" style=${`background:${colorOf(b.type)};`}>${icon(known(b) ? b.type : 'unknown')}</div>
              <div class="scn-head-text">
                <div class="scn-title"><span class="name">${titleOf(b)}</span> <span class="sum">${summaryOf(b, p)}</span></div>
                ${b.label && b.type !== 'label' && html`<span class="scn-note">${b.label}</span>`}
                ${row.error && !row.running && html`<span class="scn-error">${SMALL.error}${row.error}</span>`}
                ${row.running && html`<span class="scn-running">${waiting ? T('sc_waitingKey', fmt(b.input)) : T('sc_running')}</span>`}
              </div>
            </div>
            ${quick}
            ${toggle}
            <span style=${`transform:rotate(${expanded ? 90 : 0}deg);`} class="scn-chevron-wrap">${SMALL.chevron}</span>
          </div>
          ${expanded && html`
            <div class="scn-body">
              ${this.scnFields(b, row.heldBefore).map((f, i) => this.scnField(f, i))}
              <div class="scn-actions">
                <button class="scn-run-from" onClick=${() => this.testRun(b.id)}>${SMALL.playSmall}${T('sc_runFrom')}</button>
                <button class="scn-link" onClick=${() => this.scnDup(b.id)}>${T('sc_dup')}</button>
                <div class="spacer"></div>
                <button class="scn-link red" onClick=${() => this.scnDel(b.id)}>${T('sc_del')}</button>
              </div>
            </div>`}
        </div>`;
      }

      return html`
        <div key=${rowKey} data-row="1" class="scn-row" style=${`padding-left:${indent}px;`} onDragOver=${onOver} onDrop=${onDrop}>
          ${row.guides.map((left, i) => html`<div key=${i} class="scn-guide" style=${`left:${left}px;`}></div>`)}
          ${drop === 'top' && html`<div class="scn-drop" style=${`left:${indent}px; top:-5px;`}></div>`}
          ${body}
          ${drop === 'bot' && html`<div class="scn-drop" style=${`left:${indent + (row.kind === 'block' && isContainer(b) ? INDENT : 0)}px; bottom:2px;`}></div>`}
        </div>`;
    },

    renderScenarioPage() {
      const st = this.state;
      const p = this.p;
      const script = this.scnScript();
      const blocks = script.blocks || [];
      const isScript = p.kind === 'script';
      const has = blocks.length > 0;
      const runs = script.repeat === 'fixed' ? 'n' : script.repeat === 'inf' ? 'inf' : 'once';
      const est = estimate(blocks, script.stepDelay || 0, p);
      const anyOpen = Object.keys(st.exp).some(k => st.exp[k]);
      const seg = (value, opts, onSet) => html`<div class="segments">${opts.map(([v, text]) => html`
        <button key=${v} class="segment narrow" style=${`background:${value === v ? '#636366' : 'transparent'};`} onClick=${() => onSet(v)}>${text}</button>`)}</div>`;
      const stepper = (value, step, min, onSet) => html`
        <button class="scn-step big" onClick=${() => onSet(Math.max(min, value - step))}>${SMALL.minus}</button>
        <input class="scn-number big" type="number" value=${value} onChange=${e => {
          const v = parseInt(e.target.value, 10);
          const clamped = Math.max(min, isNaN(v) ? min : v);
          e.target.value = String(clamped);
          onSet(clamped);
        }} />
        <button class="scn-step big" onClick=${() => onSet(value + step)}>${SMALL.plus}</button>`;

      const q = (st.q || '').trim().toLowerCase();
      const groups = ['mk', 'cur', 'time', 'ctl'].map(g => ({
        g,
        items: Object.keys(GROUP).filter(t => GROUP[t] === g && (!q || T('b_' + t).toLowerCase().includes(q))),
      })).filter(g => g.items.length);

      const typeRow = html`
        <div class="group scn-type">
          <div class="stack"><span class="label">${T('sc_ptype')}</span><span class="explain">${T(isScript ? 'sc_ptypeS' : 'sc_ptypeN')}</span></div>
          ${seg(isScript ? 'script' : 'clicker', [['clicker', T('sc_normal')], ['script', T('sc_scn')]], v => this.setP({ kind: v }))}
        </div>`;

      return html`
        <div class="scn">
          ${typeRow}
          ${has && html`
            <div class="group">
              <div class="row"><span class="label">${T('sc_stepPause')}</span>
                <div class="scn-stepper">${stepper(script.stepDelay ?? 0, 10, 0, v => this.setScript({ stepDelay: v }))}<span class="scn-unit">${T('u_ms')}</span></div></div>
              <div class="row-separator"></div>
              <div class="row"><span class="label">${T('sc_runs')}</span>
                ${seg(runs, [['once', T('op_once')], ['n', T('op_n')], ['inf', T('o_inf')]], v => this.setScript({ repeat: { once: 'once', n: 'fixed', inf: 'inf' }[v] }))}</div>
              ${runs === 'n' && html`
                <div class="row-separator"></div>
                <div class="row"><span class="label">${T('sc_runsN')}</span>
                  <div class="scn-stepper">${stepper(script.count ?? 1, 1, 1, v => this.setScript({ count: v }))}<span class="scn-unit"></span></div></div>`}
              <div class="row-separator"></div>
              <div class="row"><span class="label">${T('sc_lap')}</span>
                <span class="scn-lap">${est.endless ? T('sc_lapInf') : T('sc_lapVal', (est.ms / 1000).toFixed(1), est.waitsKey)}</span></div>
            </div>

            <div class="scn-list">
              <div class="scn-list-head">
                <span class="title">${T('sc_blocks')}</span><span class="count">${T('nBlocks', countAll(blocks))}</span>
                <div class="spacer"></div>
                <button class="scn-icon-button" title=${T('sc_undo')} style=${`opacity:${st.hist.length ? 1 : 0.35};`} onClick=${() => this.scnUndo()}>${SMALL.undo}</button>
                <button class="scn-icon-button" title=${T('sc_redo')} style=${`opacity:${st.fut.length ? 1 : 0.35};`} onClick=${() => this.scnRedo()}>${SMALL.redo}</button>
                <button class="link-button pad" onClick=${() => {
                  if (anyOpen) return this.setState({ exp: {} });
                  const all = {};
                  walkAll(blocks, b => { all[b.id] = true; });
                  return this.setState({ exp: all });
                }}>${T(anyOpen ? 'sc_collapse' : 'sc_expand')}</button>
              </div>
              <div class="scn-test-row">
                <button class="scn-test" style=${`color:${st.testing ? '#ff453a' : '#30d158'};`} onClick=${() => this.testRun(null)}>
                  ${st.testing ? SMALL.stop : SMALL.play}${T(st.testing ? 'sc_testStop' : 'sc_test')}</button>
                <span class="scn-test-status">${st.testing ? T('sc_testStep', Math.max(1, st.testStep), st.testTotal) : ''}</span>
              </div>
              <div class="scn-rows">${this.scnRows(blocks).map(row => this.scnRow(row))}</div>
            </div>`}
          ${!has && html`
            <div class="scn-empty">
              <div class="scn-empty-icon">${icon('layers', 32)}</div>
              <span class="title">${T('sc_emptyT')}</span>
              <span class="text">${T('sc_emptyS')}</span>
            </div>`}
          ${!st.adding && html`<button class="scn-add" onClick=${() => this.setState({ adding: true, q: '' })}>${SMALL.add}${T('sc_add')}</button>`}
          ${st.adding && html`
            <div class="group scn-picker" ref=${el => { if (el && !this.pickerShown) { this.pickerShown = true; setTimeout(() => el.scrollIntoView({ block: 'nearest', behavior: 'smooth' }), 0); } if (!el) this.pickerShown = false; }}>
              <div class="scn-search-row">
                <div class="scn-search">${SMALL.search}
                  <input ref=${el => { if (el && !this.searchFocused) { this.searchFocused = true; setTimeout(() => el.focus(), 0); } if (!el) this.searchFocused = false; }}
                    value=${st.q} placeholder=${T('sc_search')} onInput=${e => this.setState({ q: e.target.value })}
                    onKeyDown=${e => { if (e.code === 'Escape') { e.stopPropagation(); this.setState({ adding: false, q: '' }); } }} />
                </div>
                <button class="link-button big" onClick=${() => this.setState({ adding: false, q: '' })}>${T('sc_cancel')}</button>
              </div>
              <div class="scn-catalogue">
                ${groups.map(g => html`
                  <div key=${g.g}>
                    <div class="scn-group-name">${T('g_' + g.g)}</div>
                    ${g.items.map(t => html`
                      <div key=${t} class="scn-catalogue-item" onClick=${() => this.scnAdd(t)}>
                        <div class="scn-tile" style=${`background:${colorOf(t)};`}>${icon(t)}</div>
                        <div class="stack"><span class="name">${T('b_' + t)}</span><span class="desc">${T('bd_' + t)}</span></div>
                      </div>`)}
                  </div>`)}
                ${!groups.length && html`<div class="scn-nothing">${T('sc_nothing')}</div>`}
              </div>
            </div>`}
        </div>`;
    },

    // The point picker: a crosshair over the window; a click takes the screen point under it.
    renderPicker() {
      const st = this.state;
      if (!st.picking) return null;
      const pos = st.pickPos;
      const toScreen = e => ({
        x: Math.round(e.screenX * (window.devicePixelRatio || 1)),
        y: Math.round(e.screenY * (window.devicePixelRatio || 1)),
      });
      const bind = this.p.bind.point;
      return html`
        <div class="scn-pick-overlay"
          onMouseMove=${e => {
            const rect = e.currentTarget.getBoundingClientRect();
            const scale = rect.width / 1920 || 1;
            this.setState({ pickPos: { left: (e.clientX - rect.left) / scale, top: (e.clientY - rect.top) / scale, ...toScreen(e) } });
          }}
          onClick=${e => {
            const pt = toScreen(e);
            const pick = st.picking;
            this.setState({ picking: null, pickPos: null, settings: true });
            this.scnSet(pick.id, { [pick.field]: xy(pt.x, pt.y) });
            this.toast(T('sc_picked', pt.x, pt.y));
          }}>
          ${pos && html`
            <div class="scn-cross-v" style=${`left:${pos.left}px;`}></div>
            <div class="scn-cross-h" style=${`top:${pos.top}px;`}></div>
            <div class="scn-cross-label" style=${`left:${pos.left}px; top:${pos.top}px;`}>${pos.x}, ${pos.y}</div>`}
          <div class="scn-pick-hint">${T('sc_pickHint', bind ? fmt(bind) : '')}</div>
        </div>`;
    },
  };

  window.GC_SCENARIO = { methods, countAll, icon, find, titleOf };
})();
