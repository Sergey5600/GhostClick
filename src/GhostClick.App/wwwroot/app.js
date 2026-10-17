// GhostClick v3 interface.
//
// The page owns the editing of the visible profile slice (`p`): assignment rules, conflicts
// and settings live here and every change is sent to the host whole. The host owns the
// engine, the hotkeys, persistence, the unsaved-changes flag and the run counters, and
// pushes those back. See host.js for the transport and GhostClick.Bridge for the other end.
(function () {
  'use strict';

  const { h, render, Component } = window.preact;
  const html = window.htm.bind(h);
  const { SLOTS, MODS, RU, KEYS, MOUSE, MOUSE_BODY, ARROWS, SUB, label } = window.GC_LAYOUT;
  const { LANGS, t: T } = window.GC_I18N;

  const STAGE_W = 1920;
  const STAGE_H = 1080;
  const TOAST_MS = 3200;
  const LOG_SIZE = 12;
  const RATE_WINDOW_MS = 2000;
  const HINT_KEY = 'gc_hintSeen';

  // The «Как нажимать» popover: it opens after hovering an action for a moment (at once when
  // moving over from another one) and closes a moment after the pointer leaves both.
  const POP_OPEN_MS = 260;
  const POP_CLOSE_MS = 220;
  const POP_HALF_W = 180;
  // The keyboard and mouse are drawn at 1.25x inside a 1770 px block centred on the stage;
  // a popover is kept within the stage, the block's left edge being 75 px in.
  const DEVICE_SCALE = 1.25;
  const DEVICES_LEFT = (STAGE_W - 1770) / 2;
  const MOUSE_LEFT = 1086 + 80;

  // A newly added action: a 30 ms click. A held one is held while running, or for 500 ms.
  const NEW_ACTION = { mode: 'click', press: 30, hold: 'run', holdMs: 500 };

  const col = (slot, alpha) => (alpha == null ? `rgb(${slot.o})` : `rgb(${slot.o} / ${alpha})`);
  const fmt = b => (b ? [...b.mods, label(b.key)].join(' + ') : '—');
  const same = (a, b) => !!a && !!b && a.key === b.key && a.mods.slice().sort().join() === b.mods.slice().sort().join();
  const now = () => new Date().toTimeString().slice(0, 8);
  const evMods = e => [
    ...(e.ctrlKey ? ['Ctrl'] : []),
    ...(e.shiftKey ? ['Shift'] : []),
    ...(e.altKey ? ['Alt'] : []),
    ...(e.metaKey ? ['Win'] : []),
  ];
  const slotById = id => SLOTS.find(s => s.id === id);
  const toggleLook = on => ({ track: on ? '#30d158' : '#39393d', knob: on ? 22 : 2 });
  const isHold = a => !!a && a.mode === 'hold';

  function readHintSeen() {
    try { return localStorage.getItem(HINT_KEY) === '1'; } catch (e) { return false; }
  }

  function writeHintSeen() {
    try { localStorage.setItem(HINT_KEY, '1'); } catch (e) { /* private mode: show it again next time */ }
  }

  // ---------------------------------------------------------------- icons (Lucide, ISC)

  const ICON = {
    chevrons: html`<svg width="14" height="14" viewBox="0 0 24 24" style="display:block; fill:none; stroke:#8e8e93; stroke-width:2.5; stroke-linecap:round; stroke-linejoin:round;"><path d="m7 15 5 5 5-5"></path><path d="m7 9 5-5 5 5"></path></svg>`,
    check: html`<svg width="14" height="14" viewBox="0 0 24 24" style="display:block; fill:none; stroke:#f5f5f7; stroke-width:3; stroke-linecap:round; stroke-linejoin:round;"><path d="M20 6 9 17l-5-5"></path></svg>`,
    close: html`<svg width="14" height="14" viewBox="0 0 24 24" style="display:block; fill:none; stroke:#aeaeb2; stroke-width:3; stroke-linecap:round;"><path d="M18 6 6 18"></path><path d="m6 6 12 12"></path></svg>`,
    remove: html`<svg width="12" height="12" viewBox="0 0 24 24" style="display:block; fill:none; stroke:#8e8e93; stroke-width:3; stroke-linecap:round;"><path d="M18 6 6 18"></path><path d="m6 6 12 12"></path></svg>`,
    minus: html`<svg width="14" height="14" viewBox="0 0 24 24"><path d="M5 12h14"></path></svg>`,
    plus: html`<svg width="14" height="14" viewBox="0 0 24 24"><path d="M5 12h14M12 5v14"></path></svg>`,
    minusSmall: html`<svg width="13" height="13" viewBox="0 0 24 24"><path d="M5 12h14"></path></svg>`,
    plusSmall: html`<svg width="13" height="13" viewBox="0 0 24 24"><path d="M5 12h14M12 5v14"></path></svg>`,
    click: html`<svg width="16" height="16" viewBox="0 0 24 24"><path d="M9.037 9.69a.498.498 0 0 1 .653-.653l11 4.5a.5.5 0 0 1-.074.949l-4.349 1.041a1 1 0 0 0-.74.739l-1.04 4.35a.5.5 0 0 1-.95.074z"></path><path d="M14 4.1 12 6"></path><path d="m5.1 8-2.9-.8"></path><path d="m6 12-1.9 2"></path><path d="M7.2 2.2 8 5.1"></path></svg>`,
    pin: html`<svg width="16" height="16" viewBox="0 0 24 24"><path d="M20 10c0 4.993-5.539 10.193-7.399 11.799a1 1 0 0 1-1.202 0C9.539 20.193 4 14.993 4 10a8 8 0 0 1 16 0"></path><circle cx="12" cy="10" r="3"></circle></svg>`,
    timer: html`<svg width="16" height="16" viewBox="0 0 24 24"><path d="M10 2h4"></path><path d="M12 14l3-3"></path><circle cx="12" cy="14" r="8"></circle></svg>`,
    shield: html`<svg width="16" height="16" viewBox="0 0 24 24"><path d="M20 13c0 5-3.5 7.5-7.66 8.95a1 1 0 0 1-.67-.01C7.5 20.5 4 18 4 13V6a1 1 0 0 1 1-1c2 0 4.5-1.2 6.24-2.72a1.17 1.17 0 0 1 1.52 0C14.51 3.81 17 5 19 5a1 1 0 0 1 1 1z"></path></svg>`,
    list: html`<svg width="16" height="16" viewBox="0 0 24 24"><path d="M8 6h13"></path><path d="M8 12h13"></path><path d="M8 18h13"></path><path d="M3 6h.01"></path><path d="M3 12h.01"></path><path d="M3 18h.01"></path></svg>`,
  };

  // ---------------------------------------------------------------- the app

  class App extends Component {
    constructor(props) {
      super(props);
      this.host = props.host;
      // The newest profile slice. Event handlers read this rather than this.state.p, which
      // only catches up at the next render.
      this.p = null;
      this.mouse = { x: 0, y: 0 };
      this.samples = [];
      this.hintSeen = readHintSeen();
      this.state = {
        connected: false,
        lang: 'ru',
        // Profile slice; mirrored to the host on every change.
        p: null,
        profile: '',
        profiles: [],
        dirty: false,
        // Interface state.
        sel: null,
        pending: [],
        pressed: {},
        toast: '',
        toastErr: false,
        settings: false,
        menu: false,
        // The action whose «Как нажимать» popover is open (its key code), or null.
        pop: null,
        // Runtime, from the host.
        run: 'stopped',
        clicks: 0,
        elapsedMs: 0,
        pointIndex: -1,
        rate: 0,
        dry: false,
        log: [],
        viewW: window.innerWidth,
        viewH: window.innerHeight,
      };
    }

    componentDidMount() {
      this.onResize = () => this.setState({ viewW: window.innerWidth, viewH: window.innerHeight });
      this.onKeyDown = e => this.keyDown(e);
      this.onKeyUp = e => this.keyUp(e);
      this.onMouseMove = e => { this.mouse = { x: e.screenX, y: e.screenY }; };
      this.onMouseDown = e => this.mouseDown(e);
      this.onMouseUp = e => { if (e.button === 3 || e.button === 4) e.preventDefault(); };
      this.onBlur = () => this.setState({ pressed: {}, sel: null, pending: [], pop: null });

      window.addEventListener('resize', this.onResize);
      window.addEventListener('keydown', this.onKeyDown);
      window.addEventListener('keyup', this.onKeyUp);
      window.addEventListener('mousemove', this.onMouseMove);
      window.addEventListener('mousedown', this.onMouseDown);
      window.addEventListener('mouseup', this.onMouseUp);
      window.addEventListener('blur', this.onBlur);

      this.host.listen(message => this.receive(message));
      this.host.send({ type: 'ready' });
    }

    componentWillUnmount() {
      window.removeEventListener('resize', this.onResize);
      window.removeEventListener('keydown', this.onKeyDown);
      window.removeEventListener('keyup', this.onKeyUp);
      window.removeEventListener('mousemove', this.onMouseMove);
      window.removeEventListener('mousedown', this.onMouseDown);
      window.removeEventListener('mouseup', this.onMouseUp);
      window.removeEventListener('blur', this.onBlur);
      clearTimeout(this.toastTimer);
      clearTimeout(this.popTimer);
    }

    componentDidUpdate(_, previous) {
      // The host silences its hotkeys while we wait for a key, so pressing F6 to assign it
      // does not also start a run.
      if (!!previous.sel !== !!this.state.sel) {
        this.host.send({ type: 'listen', value: !!this.state.sel });
      }
    }

    // -------------------------------------------------------------- host messages

    receive(m) {
      if (!m || typeof m !== 'object') return;

      switch (m.type) {
        case 'init':
          this.p = m.profile.data;
          window.GC_I18N.setLang(m.lang);
          this.setState({
            connected: true,
            lang: window.GC_I18N.lang(),
            profiles: m.profiles,
            profile: m.profile.name,
            p: m.profile.data,
            dirty: m.profile.dirty,
            dry: !!m.dryRun,
          });
          this.applyState(m.runtime);
          if (m.backend) this.log(`Backend: ${m.backend}`);
          break;
        case 'profile':
          this.p = m.profile.data;
          this.setState({ profiles: m.profiles, profile: m.profile.name, p: m.profile.data, dirty: m.profile.dirty, menu: false });
          break;
        case 'dirty':
          this.setState({ dirty: !!m.value });
          break;
        case 'state':
          this.applyState(m);
          break;
        case 'log':
          this.log(m.message, m.time);
          break;
        case 'toast':
          this.toast(m.message, m.error);
          break;
        case 'pointAdded':
          this.addPoint(m.x, m.y);
          break;
        case 'hotkey': {
          const b = this.p && this.p.bind[m.slot];
          if (b) this.setState(s => ({ pressed: { ...s.pressed, [b.key]: !!m.down } }));
          break;
        }
      }
    }

    applyState(m) {
      if (!m) return;
      if (m.state === 'running' && this.state.run === 'stopped') this.samples = [];
      this.samples.push({ t: m.elapsedMs, c: m.clicks });
      while (this.samples.length > 2 && m.elapsedMs - this.samples[0].t > RATE_WINDOW_MS) this.samples.shift();

      const first = this.samples[0];
      const span = m.elapsedMs - first.t;
      const rate = m.state === 'running' && span >= 400 ? ((m.clicks - first.c) * 1000) / span : 0;

      this.setState({ run: m.state, clicks: m.clicks, elapsedMs: m.elapsedMs, pointIndex: m.pointIndex, rate });
    }

    // -------------------------------------------------------------- helpers

    // Changes the profile slice and sends the result to the host.
    setP(patch) {
      const p = { ...this.p, ...patch };
      this.p = p;
      this.setState({ p });
      this.host.send({ type: 'setProfile', data: p });
    }

    log(message, time) {
      this.setState(s => ({ log: [...s.log, { t: time || now(), m: message }].slice(-LOG_SIZE) }));
    }

    toast(message, error) {
      clearTimeout(this.toastTimer);
      this.setState({ toast: message, toastErr: !!error });
      this.toastTimer = setTimeout(() => this.setState({ toast: '' }), TOAST_MS);
    }

    // -------------------------------------------------------------- input

    keyDown(e) {
      if (!this.p) return;
      if (e.target && /INPUT|SELECT|TEXTAREA/.test(e.target.tagName)) return;
      if (e.code === 'Escape' && (this.state.settings || this.state.menu || this.state.pop)) {
        this.setState({ settings: false, menu: false, pop: null });
        return;
      }

      const code = e.code;
      if (!window.GC_LAYOUT.LABEL[code]) return;
      this.setState(s => ({ pressed: { ...s.pressed, [code]: true } }));

      if (this.state.sel) {
        e.preventDefault();
        if (MODS[code]) {
          if (!e.repeat) this.togglePending(MODS[code]);
          return;
        }
        this.assign(this.state.sel, { mods: [...new Set([...this.state.pending, ...evMods(e)])], key: code });
        return;
      }

      if (MODS[code] || e.repeat) return;

      // Inside the app the global hook fires hotkeys, focused or not; firing them here too
      // would toggle twice. Only the browser preview has to do it itself.
      if (this.host.native) return;
      const hit = SLOTS.find(s => s.id !== 'action' && same(this.p.bind[s.id], { mods: evMods(e), key: code }));
      if (hit) {
        e.preventDefault();
        this.host.previewHotkey(hit.id, this.mouse);
      }
    }

    keyUp(e) {
      if (!this.state.pressed[e.code]) return;
      this.setState(s => {
        const pressed = { ...s.pressed };
        delete pressed[e.code];
        return { pressed };
      });
    }

    // The middle and side buttons can be pressed for real while waiting for an input; the
    // left and right ones work the page, so those are picked on the drawing.
    mouseDown(e) {
      if (e.button === 3 || e.button === 4) e.preventDefault();

      // Buttons do not take focus, so Space or Enter pressed later (to assign them, say)
      // cannot click the last button again. A number field being edited is committed first.
      if (e.target instanceof Element && e.target.closest('button')) {
        if (document.activeElement instanceof HTMLInputElement) document.activeElement.blur();
        e.preventDefault();
      }

      if (!this.state.sel) return;
      const id = { 1: 'Mouse3', 3: 'Mouse4', 4: 'Mouse5' }[e.button];
      if (!id) return;
      e.preventDefault();
      this.assign(this.state.sel, { mods: [...new Set([...this.state.pending, ...evMods(e)])], key: id });
    }

    // Enters (or, with null, leaves) assignment mode. A notice left over from the previous
    // assignment would hide the new instructions, so it goes.
    select(id) {
      if (id) clearTimeout(this.toastTimer);
      clearTimeout(this.popTimer);
      this.setState(id ? { sel: id, pending: [], toast: '', pop: null } : { sel: null, pending: [] });
    }

    togglePending(mod) {
      this.setState(s => ({ pending: s.pending.includes(mod) ? s.pending.filter(x => x !== mod) : [...s.pending, mod] }));
    }

    clickInput(id) {
      const { sel } = this.state;
      const p = this.p;
      if (sel) {
        if (MODS[id]) return this.togglePending(MODS[id]);
        return this.assign(sel, { mods: this.state.pending, key: id });
      }

      const owner = p.actions.some(a => a.key === id)
        ? slotById('action')
        : SLOTS.find(s => s.id !== 'action' && p.bind[s.id] && p.bind[s.id].key === id);
      if (owner) this.select(owner.id);
      else this.toast(T('pickFirst'));
    }

    // -------------------------------------------------------------- «Как нажимать»

    // Hovering an action opens its popover, after a moment unless another one is already
    // open. Never while assigning or while the settings are out.
    popOpen(id) {
      const st = this.state;
      if (st.sel || st.settings || !this.p || !this.p.actions.some(a => a.key === id)) return;
      clearTimeout(this.popTimer);
      if (st.pop === id) return;
      this.popTimer = setTimeout(() => this.setState({ pop: id }), st.pop ? 0 : POP_OPEN_MS);
    }

    // Leaving the action or the popover: close unless the pointer comes back in time.
    popCloseSoon() {
      clearTimeout(this.popTimer);
      this.popTimer = setTimeout(() => this.setState({ pop: null }), POP_CLOSE_MS);
    }

    updateAction(key, patch) {
      this.setP({ actions: this.p.actions.map(a => (a.key === key ? { ...a, ...patch } : a)) });
    }

    // Steps a number of an action from its newest value, so quick repeated clicks add up.
    stepAction(key, field, delta, min, fallback) {
      const action = this.p.actions.find(a => a.key === key);
      if (action) this.updateAction(key, { [field]: Math.max(min, (action[field] ?? fallback) + delta) });
    }

    removeAction(key) {
      const action = this.p.actions.find(a => a.key === key);
      if (!action) return;
      clearTimeout(this.popTimer);
      this.setP({ actions: this.p.actions.filter(a => a !== action) });
      this.setState({ pop: null });
      this.toast(T('removed', fmt(action)));
    }

    // -------------------------------------------------------------- assignment rules

    assign(slot, b) {
      if (slot === 'action') return this.toggleAction(b);
      if (b.key === 'Mouse1' && !b.mods.length) {
        return this.toast(T('lmbErr'), true);
      }

      const p = this.p;
      const bind = { ...p.bind, [slot]: b };
      let msg = `${slotById(slot).title}: ${fmt(b)}`;
      SLOTS.forEach(s => {
        if (s.id !== slot && s.id !== 'action' && same(bind[s.id], b)) {
          bind[s.id] = null;
          msg += T('unboundFrom', s.title);
        }
      });

      const actions = p.actions.filter(a => !same(a, b));
      if (actions.length !== p.actions.length) msg += T('removedFromActions');

      this.setP({ bind, actions });
      this.setState({ sel: null, pending: [] });
      this.toast(msg);
      this.log(T('assigned', msg));
    }

    toggleAction(b) {
      const p = this.p;
      const actions = p.actions.slice();
      const index = actions.findIndex(a => same(a, b));
      if (index >= 0) {
        actions.splice(index, 1);
        this.setP({ actions });
        this.setState({ pending: [] });
        this.toast(T('removed', fmt(b)));
        return;
      }

      const bind = { ...p.bind };
      let msg = T('added', fmt(b));
      SLOTS.forEach(s => {
        if (s.id !== 'action' && same(bind[s.id], b)) {
          bind[s.id] = null;
          msg += T('unboundFrom', s.title);
        }
      });

      actions.push({ ...b, ...NEW_ACTION });
      this.setP({ actions, bind });
      this.setState({ pending: [] });
      this.toast(msg);
      this.log(msg);
    }

    addPoint(x, y) {
      const points = [...this.p.points, { x, y, delayMs: 0, label: null, enabled: true }];
      this.setP({ points, target: 'points' });
      this.log(T('ptLog', points.length, x, y));
      this.toast(T('ptAdded', points.length, x, y));
    }

    // -------------------------------------------------------------- commands

    startStop() {
      this.host.send({ type: this.state.run === 'stopped' ? 'start' : 'stop' });
    }

    pause() {
      if (this.state.run === 'running') this.host.send({ type: 'pause' });
      else if (this.state.run === 'paused') this.host.send({ type: 'resume' });
    }

    toggleDry() {
      const dry = !this.state.dry;
      this.setState({ dry });
      this.host.send({ type: 'setDryRun', value: dry });
      this.log(`Dry run: ${T(dry ? 'on' : 'off')}`);
    }

    // Applies at once; the host keeps the choice for the next start and writes its own log
    // lines in it from now on. Lines already in the log stay as they are.
    setLang(lang) {
      if (lang === this.state.lang) return;
      window.GC_I18N.setLang(lang);
      this.setState({ lang: window.GC_I18N.lang() });
      this.host.send({ type: 'setLanguage', lang });
    }

    clearSlot() {
      const sel = slotById(this.state.sel);
      if (sel.id === 'action') {
        this.setP({ actions: [] });
        this.setState({ pending: [] });
        this.toast(T('actionsCleared'));
        return;
      }

      this.setP({ bind: { ...this.p.bind, [sel.id]: null } });
      this.setState({ sel: null, pending: [] });
      this.toast(T('noBinding', sel.title));
    }

    number(key, min) {
      return e => {
        const value = parseInt(e.target.value, 10);
        const clamped = Math.max(min, isNaN(value) ? min : value);
        e.target.value = String(clamped);
        this.setP({ [key]: clamped });
      };
    }

    segment(key, options) {
      return options.map(([value, text]) => html`
        <button class="segment" style=${`background:${this.state.p[key] === value ? '#636366' : 'transparent'};`}
          onClick=${() => this.setP({ [key]: value })}>${text}</button>`);
    }

    // -------------------------------------------------------------- rendering

    look(id, owner, sel, selMods) {
      const st = this.state;
      const o = owner[id];
      const pend = sel && MODS[id] && st.pending.includes(MODS[id]);
      const selMod = sel && MODS[id] && selMods.includes(MODS[id]) && !st.pending.length;
      // An action held down looks pressed for as long as the clicker runs.
      const act = o && o.id === 'action' ? st.p.actions.find(a => a.key === id) : null;
      const down = st.pressed[id] || (isHold(act) && st.run === 'running');
      let bg = '#1c1c1e', border = 'transparent', bw = 0, fg = '#f5f5f7', tag = '', tagColor = 'transparent';

      if (o) {
        bg = col(o); fg = '#000'; tag = isHold(act) ? T('g_hold') : o.tag; tagColor = 'rgba(0,0,0,0.62)';
        if (sel && sel.id === o.id) { bw = 2; border = '#fff'; }
      }

      if (pend) { bg = col(sel); fg = '#000'; border = '#fff'; bw = 2; tag = T('mod'); tagColor = 'rgba(0,0,0,0.62)'; }
      else if (selMod) { border = col(sel); bw = 2; }

      if (down) bg = o ? col(o, 0.75) : '#3a3a3c';

      return {
        bg, border, bw, fg, tag, tagColor, act,
        ruColor: fg === '#000' ? 'rgba(0,0,0,0.55)' : '#8e8e93',
        transform: down ? 'translateY(2px)' : 'none',
        // Actions explain themselves in their popover instead.
        title: act ? '' : o ? `${label(id)} — ${o.title}` : label(id),
      };
    }

    renderKeyboard(owner, sel, selMods) {
      return KEYS.map(k => {
        const lk = this.look(k.id, owner, sel, selMods);
        const sub = SUB[k.id];
        const arrow = ARROWS[k.id];
        const legend = sub ? sub[0] : k.label;
        const special = /^[^A-Za-z0-9]$/.test(k.label);
        const fs = arrow ? 18 : special ? 20 : (k.label.length <= 2 || /^F\d+$/.test(k.id)) ? 19 : (k.label.length <= 3 || k.width > 60) ? 16 : 14;
        const ff = special ? 'var(--geist-mono)' : 'var(--geist)';
        const showSub = !!sub && !lk.tag;
        // Russian letters only in Russian. A 1u key has room for a short tag (МОД) beside its
        // letter, not a long one.
        const ru = this.state.lang !== 'ru' || (lk.tag.length > 3 && k.width <= 54) ? '' : RU[k.id] || '';

        return html`
          <div key=${k.id} class="key" title=${lk.title || undefined} onClick=${() => this.clickInput(k.id)}
            onMouseEnter=${() => this.popOpen(k.id)} onMouseLeave=${() => this.popCloseSoon()}
            style=${`left:${k.left}px; top:${k.top}px; width:${k.width}px; border-width:${lk.bw}px; border-color:${lk.border}; background:${lk.bg}; transform:${lk.transform};`}>
            ${arrow
              ? html`<svg width="20" height="20" viewBox="0 0 24 24" style=${`stroke:${lk.fg};`}><path d=${arrow[0]}></path><path d=${arrow[1]}></path></svg>`
              : html`<span class="legend" style=${`font-family:${ff}; font-size:${fs}px; color:${lk.fg};`}>${legend}</span>`}
            <div class="bottom">
              ${showSub
                ? html`<span class="sub" style=${`color:${lk.fg};`}>${sub[1]}</span>`
                : html`<span class="tag" style=${`color:${lk.tagColor};`}>${lk.tag}</span>`}
              <span class="ru" style=${`color:${lk.ruColor};`}>${ru}</span>
            </div>
          </div>`;
      });
    }

    renderMouse(owner, sel, selMods) {
      const buttons = MOUSE.map(m => {
        const l = this.look(m.id, owner, sel, selMods);
        if (!l.bw) {
          l.border = '#000';
          l.bw = 2;
          if (m.id === 'Mouse3' && l.bg === '#1c1c1e') l.bg = '#3a3a3c';
        }
        const name = T('mn_' + m.id);
        return { ...m, ...l, label: m.showLabel ? label(m.id) : '', title: l.act ? '' : owner[m.id] ? `${name} — ${owner[m.id].title}` : name };
      });

      const shape = m => html`
        <g key=${m.id} class="mouse-button" onClick=${() => this.clickInput(m.id)}
          onMouseEnter=${() => this.popOpen(m.id)} onMouseLeave=${() => this.popCloseSoon()}>
          ${m.title && html`<title>${m.title}</title>`}
          <path d=${m.d} style=${`fill:${m.bg}; stroke:${m.border}; stroke-width:${m.bw};`}></path>
        </g>`;

      return html`
        <div class="mouse" onContextMenu=${e => e.preventDefault()}>
          <svg width="250" height="369" viewBox="0 0 250 369">
            <defs><clipPath id="gcMouseBody"><path d=${MOUSE_BODY}></path></clipPath></defs>
            ${buttons.filter(m => /Mouse[45]/.test(m.id)).map(shape)}
            <path d=${MOUSE_BODY} style="fill:#121214;"></path>
            <g style="clip-path:url(#gcMouseBody);">
              ${buttons.filter(m => !/Mouse[45]/.test(m.id)).map(shape)}
            </g>
          </svg>
          ${buttons.map(m => html`
            <div key=${m.id} class="mouse-label" style=${`left:${m.lx - 40}px; top:${m.ly - Math.round(m.fs * 0.8)}px;`}>
              <span class="name" style=${`font-size:${m.fs}px; color:${m.fg};`}>${m.label}</span>
              <span class="tag" style=${`color:${m.tagColor};`}>${m.showTag ? m.tag : ''}</span>
            </div>`)}
        </div>`;
    }

    // The «Как нажимать» popover over the action being hovered, in the unscaled coordinates of
    // the device block it sits in.
    renderPopover() {
      const st = this.state;
      if (!st.pop || st.sel || st.settings) return null;
      const action = st.p.actions.find(a => a.key === st.pop);
      const key = action && KEYS.find(k => k.id === action.key);
      const button = action && MOUSE.find(m => m.id === action.key);
      if (!key && !button) return null;

      const x = key ? (key.left + key.width / 2) * DEVICE_SCALE : (MOUSE_LEFT + button.ax) * DEVICE_SCALE;
      const y = key ? key.top * DEVICE_SCALE : button.ay * DEVICE_SCALE;
      const minX = POP_HALF_W + 5 - DEVICES_LEFT;
      const maxX = STAGE_W - DEVICES_LEFT - POP_HALF_W - 5;
      const left = Math.min(maxX, Math.max(minX, x));

      const hold = isHold(action);
      const timed = action.hold === 'time';
      const press = action.press ?? NEW_ACTION.press;
      const holdMs = action.holdMs ?? NEW_ACTION.holdMs;

      const segment = (current, options, field) => options.map(([value, text]) => html`
        <button key=${value} class="pop-segment" style=${`background:${current === value ? '#636366' : 'transparent'};`}
          onClick=${() => this.updateAction(action.key, { [field]: value })}>${text}</button>`);

      const stepper = (value, step, min, field, fallback) => html`
        <div class="pop-stepper">
          <button class="pop-step" onClick=${() => this.stepAction(action.key, field, -step, min, fallback)}>${ICON.minusSmall}</button>
          <input class="pop-number" type="number" min=${min} value=${value} onChange=${e => {
            const parsed = parseInt(e.target.value, 10);
            const clamped = Math.max(min, isNaN(parsed) ? min : parsed);
            e.target.value = String(clamped);
            this.updateAction(action.key, { [field]: clamped });
          }} />
          <button class="pop-step" onClick=${() => this.stepAction(action.key, field, step, min, fallback)}>${ICON.plusSmall}</button>
          <span class="pop-unit">${T('ms')}</span>
        </div>`;

      return html`
        <div class="popover" style=${`left:${left}px; top:${y}px;`}
          onMouseEnter=${() => clearTimeout(this.popTimer)} onMouseLeave=${() => this.popCloseSoon()}>
          <div class="pop-header">
            <div class="pop-key"><span>${fmt(action)}</span></div>
            <span class="pop-title">${T('popTitle')}</span>
          </div>
          <div class="pop-segments">${segment(hold ? 'hold' : 'click', [['click', T('pm_click')], ['hold', T('pm_hold')]], 'mode')}</div>
          ${!hold && html`
            <div class="pop-block">
              <div class="pop-row"><span class="pop-label">${T('pressDur')}</span>${stepper(press, 10, 1, 'press', NEW_ACTION.press)}</div>
              <span class="pop-sub">${T('pressDurSub')}</span>
            </div>`}
          ${hold && html`
            <div class="pop-block hold">
              <div class="pop-row"><span class="pop-label">${T('holdFor')}</span>
                <div class="pop-segments">${segment(timed ? 'time' : 'run', [['run', T('h_run')], ['time', T('h_time')]], 'hold')}</div></div>
              ${timed && html`
                <div class="pop-row"><span class="pop-label">${T('releaseAfter')}</span>${stepper(holdMs, 100, 50, 'holdMs', NEW_ACTION.holdMs)}</div>`}
              <span class="pop-sub">${T(timed ? 'holdTimeSub' : 'holdRunSub')}</span>
            </div>`}
          <div class="pop-separator"></div>
          <button class="pop-remove" onClick=${() => this.removeAction(action.key)}>${T('removeAct')}</button>
        </div>`;
    }

    renderSlots(sel) {
      const { p } = this.state;
      return SLOTS.map(s => {
        const isAction = s.id === 'action';
        const bound = isAction ? (p.actions.length ? p.actions : null) : p.bind[s.id];
        const listening = sel && sel.id === s.id;
        const keyText = isAction
          ? (p.actions.length > 3
            ? `${p.actions.slice(0, 2).map(fmt).join(' + ')} +${p.actions.length - 2}`
            : p.actions.map(fmt).join(' + '))
          : fmt(bound);
        const title = s.id === 'point' && p.points.length ? `${s.title} · ${p.points.length}` : s.title;
        const vars = listening
          ? `--bg:${col(s, 0.16)}; --bg-hover:${col(s, 0.24)}; --border:${col(s)};`
          : '';

        return html`
          <div key=${s.id} class="slot" title=${s.desc} style=${vars}
            onClick=${() => this.select(listening ? null : s.id)}>
            <span class="title">${title}</span>
            <div class="chip" style=${`background:${listening ? 'transparent' : bound ? col(s) : '#2c2c2e'};`}>
              ${listening && html`<span class="waiting" style=${`color:${col(s)};`}>${T('listening')}</span>`}
              ${!listening && bound && html`<span class="keys">${keyText}</span>`}
              ${!listening && !bound && html`<span class="unbound">—</span>`}
            </div>
          </div>`;
      });
    }

    renderHint(sel) {
      const st = this.state;
      if (sel && !this.hintSeen) {
        this.hintSeen = true;
        writeHintSeen();
      }

      let hint = this.hintSeen ? '' : T('hintInit');
      let hintColor = '#8e8e93';
      if (sel) {
        hint = sel.id === 'action' ? T('hintAction') : T('hintKey', sel.title);
        hintColor = '#ebebf5';
      }

      if (st.toast) {
        hint = st.toast;
        hintColor = st.toastErr ? '#ff453a' : '#ebebf5';
      }

      return html`
        <div class="hint-row">
          <span class="hint" style=${`color:${hintColor};`}>${hint}</span>
          ${sel && st.pending.length > 0 && html`
            <div class="pending">
              ${st.pending.map(m => html`<span key=${m} class="pending-chip" style=${`background:${col(sel, 0.25)};`}>${m} +</span>`)}
            </div>`}
          ${sel && html`
            <div class="hint-actions">
              <button class="text-button" onClick=${() => this.clearSlot()}>${T(sel.id === 'action' ? 'removeAll' : 'unbind')}</button>
              <button class="text-button" onClick=${() => this.setState({ sel: null, pending: [] })}>${T(sel.id === 'action' ? 'doneBtn' : 'cancel')}</button>
            </div>`}
        </div>`;
    }

    renderToolbar() {
      const st = this.state;
      return html`
        <div class="toolbar">
          <span class="brand">GhostClick</span>
          <div class="profile-anchor">
            <button class="profile-button" style=${`--bg:${st.menu ? '#2c2c2e' : '#1c1c1e'};`}
              onClick=${() => this.setState({ menu: !st.menu })}>
              <span class="caption">${T('profile')}</span>
              <span class="name">${st.profile}</span>
              ${ICON.chevrons}
            </button>
            ${st.menu && html`
              <div class="menu-scrim" onClick=${() => this.setState({ menu: false })}></div>
              <div class="menu">
                ${st.profiles.map(name => html`
                  <div key=${name} class="menu-item" onClick=${() => {
                    this.setState({ menu: false });
                    if (name !== st.profile) this.host.send({ type: 'loadProfile', name });
                  }}>
                    <span class="check">${name === st.profile && ICON.check}</span>
                    <span class="label">${name}</span>
                  </div>`)}
                <div class="menu-separator"></div>
                <div class="menu-item" onClick=${() => { this.setState({ menu: false }); this.host.send({ type: 'newProfile' }); }}>
                  <span class="check"></span><span class="label">${T('newProfile')}</span>
                </div>
                <div class="menu-item" onClick=${() => { this.setState({ menu: false }); this.host.send({ type: 'duplicateProfile' }); }}>
                  <span class="check"></span><span class="label">${T('dup', st.profile)}</span>
                </div>
              </div>`}
          </div>
          ${st.dirty && html`
            <div class="unsaved">
              <span class="note">${T('unsaved')}</span>
              <button class="save-button" onClick=${() => this.host.send({ type: 'saveProfile' })}>${T('save')}</button>
              <button class="revert-button" onClick=${() => this.host.send({ type: 'revertProfile' })}>${T('revert')}</button>
            </div>`}
          <div class="spacer"></div>
          <div class="lang-switch">
            ${LANGS.map(code => html`
              <button key=${code} class=${code === st.lang ? 'on' : ''} onClick=${() => this.setLang(code)}>${code.toUpperCase()}</button>`)}
          </div>
          <button class="pill-button" onClick=${() => this.setState({ settings: true, sel: null, pending: [], menu: false, pop: null })}>${T('settings')}</button>
        </div>`;
    }

    renderDock() {
      const st = this.state;
      const p = st.p;
      const seconds = Math.floor(st.elapsedMs / 1000);
      const RUN = { stopped: [T('runStopped'), '#8e8e93'], running: [T('runRunning'), '#30d158'], paused: [T('runPaused'), '#ff9f0a'] }[st.run] || [T('runStopped'), '#8e8e93'];
      const counters = [
        { v: `${String(Math.floor(seconds / 60)).padStart(2, '0')}:${String(seconds % 60).padStart(2, '0')}`, l: T('cTime') },
        { v: String(st.clicks), l: T('cClicks') },
        { v: st.run === 'running' ? st.rate.toFixed(1) : '0', l: T('cCps') },
        { v: p.repeat === 'fixed' ? String(Math.max(0, p.count - st.clicks)) : '∞', l: T('cLeft') },
      ];
      const stopped = st.run === 'stopped';
      const dry = toggleLook(st.dry);

      return html`
        <div class="dock">
          <button class="start-button" onClick=${() => this.startStop()}
            style=${`background:${stopped ? '#30d158' : '#ff453a'}; color:${stopped ? '#000' : '#fff'};`}>
            <span class="button-text">${T(stopped ? 'start' : 'stop')}</span>
            <span class="shortcut">${p.bind.toggle ? fmt(p.bind.toggle) : ''}</span>
          </button>
          <button class="pause-button" onClick=${() => this.pause()} style=${`color:${stopped ? '#636366' : '#f5f5f7'};`}>
            <span class="button-text">${T(st.run === 'paused' ? 'resume' : 'pause')}</span>
            <span class="shortcut">${p.bind.pause ? fmt(p.bind.pause) : ''}</span>
          </button>
          <div class="status">
            <div class="state">
              <span class="dot" style=${`background:${RUN[1]};`}></span>
              <span class="label">${st.dry && st.run === 'running' ? 'Dry run' : RUN[0]}</span>
            </div>
            ${counters.map((c, i) => html`
              <div key=${i} class="counter">
                <span class="value">${c.v}</span>
                <span class="caption">${c.l}</span>
              </div>`)}
          </div>
          <div class="dock-divider"></div>
          <div class="dry-run">
            <span class="caption">Dry run</span>
            <button class="switch" style=${`background:${dry.track};`} onClick=${() => this.toggleDry()} title=${T('dryRunTitle')}>
              <span class="knob" style=${`left:${dry.knob}px;`}></span>
            </button>
          </div>
        </div>`;
    }

    renderSettings() {
      const st = this.state;
      const p = st.p;
      const isPoint = p.target === 'points';
      const n = p.points.length;
      const pointLabel = T('nPoints', n);
      const summary = [
        T(p.click === 'double' ? 'dbl' : 'sgl'),
        T('every', p.rand ? `${p.minDelay}–${p.maxDelay}` : p.delay),
        p.repeat === 'fixed' ? T('times', p.count) : T('unlimited'),
      ].join(' · ');
      const rand = toggleLook(p.rand);
      const flagDefs = ['jitter', 'holdRandom', 'stopOnMouseMove', 'activeWindowOnly'].map((key, i) => {
        const [text, explain] = T('flags')[i];
        return [key, typeof text === 'function' ? text(p.jitterPx) : text, explain];
      });
      const separator = html`<div class="row-separator"></div>`;
      const header = (color, icon, title, extra) => html`
        <div class="section-header">
          <div class="section-icon" style=${`background:${color};`}>${icon}</div>
          <span class="title">${title}</span>
          ${extra}
        </div>`;

      return html`
        <div class="backdrop" onClick=${() => this.setState({ settings: false })}
          style=${`opacity:${st.settings ? 1 : 0}; pointer-events:${st.settings ? 'auto' : 'none'};`}></div>
        <div class=${st.settings ? 'sheet' : 'sheet closed'}>
          <div class="sheet-header">
            <div class="sheet-title">
              <span class="heading">${T('settings')}</span>
              <span class="subheading">${T('profile')} ${st.profile}</span>
            </div>
            <button class="round-button" title=${T('close')} onClick=${() => this.setState({ settings: false })}>${ICON.close}</button>
          </div>
          <div class="sheet-body">
            <div class="summary">
              <div class="tile">${p.actions.map(fmt).join(' + ') || '—'}</div>
              <div class="text">
                <span class="caption">${T('pressedTogether')}</span>
                <span class="line">${summary}</span>
              </div>
              <button class="edit-button" onClick=${() => { this.setState({ settings: false }); this.select('action'); }}>${T('edit')}</button>
            </div>

            <div class="section">
              ${header('#0a84ff', ICON.click, T('click'), html`<div class="spacer"></div>`)}
              <div class="group">
                <div class="row"><span class="label">${T('type')}</span>
                  <div class="segments">${this.segment('click', [['single', T('o_single')], ['double', T('o_double')]])}</div></div>
                ${separator}
                <div class="row"><span class="label">${T('keyMode')}</span>
                  <div class="segments">${this.segment('mode', [['toggle', T('o_toggle')], ['hold', T('o_hold')]])}</div></div>
                ${separator}
                <div class="row"><span class="label">${T('target')}</span>
                  <div class="controls">
                    ${isPoint && html`<span class="points-label">${pointLabel}</span>`}
                    <div class="segments">${this.segment('target', [['cursor', T('o_cursor')], ['points', T('o_point')]])}</div>
                  </div></div>
                ${separator}
                <div class="row"><span class="label">${T('cursorMove')}</span>
                  <div class="segments">${this.segment('move', [['instant', T('o_instant')], ['smooth', T('o_smooth')]])}</div></div>
              </div>
            </div>

            ${isPoint && html`
              <div class="section">
                ${header('#bf5af2', ICON.pin, T('route'), html`
                  <span class="count">${pointLabel}</span>
                  <div class="spacer"></div>
                  ${n > 0 && html`<button class="link-button" onClick=${() => this.setP({ points: [] })}>${T('clear')}</button>`}`)}
                <div class="group">
                  ${n === 0 && html`<div class="empty">${T('ptHint', p.bind.point ? fmt(p.bind.point) : T('ptKeyFb'))}</div>`}
                  ${p.points.map((pt, i) => html`
                    <div key=${i} class="point-row" style=${`border-top:${i ? '0.5px solid rgba(84,84,88,0.55)' : 'none'};`}>
                      <span class="dot" style=${`background:${st.run !== 'stopped' && st.pointIndex === i ? '#bf5af2' : '#3a3a3c'};`}></span>
                      <span class="name">${T('ptN', i + 1)}</span>
                      <div class="spacer"></div>
                      <span class="xy">${pt.x}, ${pt.y}</span>
                      <button class="remove-button" title=${T('del')} onClick=${() => this.setP({ points: this.p.points.filter((_, j) => j !== i) })}>${ICON.remove}</button>
                    </div>`)}
                </div>
              </div>`}

            <div class="section">
              ${header('#ff9f0a', ICON.timer, T('timing'), html`<div class="spacer"></div>`)}
              <div class="group">
                <div class="row"><span class="label">${T('interval')}</span>
                  <div class="controls tight">
                    ${!p.rand && html`
                      <button class="step-button" onClick=${() => this.setP({ delay: Math.max(1, this.p.delay - 10) })}>${ICON.minus}</button>
                      <input class="number" style="width:76px;" type="number" min="1" value=${p.delay} onChange=${this.number('delay', 1)} />
                      <button class="step-button" onClick=${() => this.setP({ delay: this.p.delay + 10 })}>${ICON.plus}</button>`}
                    ${p.rand && html`
                      <input class="number" style="width:70px;" type="number" min="0" value=${p.minDelay} onChange=${this.number('minDelay', 0)} />
                      <span class="dash">–</span>
                      <input class="number" style="width:70px;" type="number" min="0" value=${p.maxDelay} onChange=${this.number('maxDelay', 0)} />`}
                    <span class="unit">${T('ms')}</span>
                  </div></div>
                ${separator}
                <div class="row">
                  <div class="stack"><span class="label">${T('randInterval')}</span><span class="explain">${T('randSub')}</span></div>
                  <button class="switch" style=${`background:${rand.track};`} onClick=${() => this.setP({ rand: !this.p.rand })}>
                    <span class="knob" style=${`left:${rand.knob}px;`}></span>
                  </button>
                </div>
                ${separator}
                <div class="row"><span class="label">${T('repeat')}</span>
                  <div class="controls medium">
                    ${p.repeat === 'fixed' && html`<input class="number" style="width:80px;" type="number" min="1" value=${p.count} onChange=${this.number('count', 1)} />`}
                    <div class="segments">${this.segment('repeat', [['inf', T('o_inf')], ['fixed', T('o_fixed')]])}</div>
                  </div></div>
              </div>
            </div>

            <div class="section">
              ${header('#30d158', ICON.shield, T('safety'), html`<div class="spacer"></div>`)}
              <div class="group">
                ${flagDefs.map(([key, text, explain], i) => {
                  const look = toggleLook(p.flags[key]);
                  return html`
                    <div key=${key}>
                      ${i > 0 && separator}
                      <div class="row tall">
                        <div class="stack"><span class="label">${text}</span><span class="explain">${explain}</span></div>
                        <button class="switch" style=${`background:${look.track};`}
                          onClick=${() => this.setP({ flags: { ...this.p.flags, [key]: !this.p.flags[key] } })}>
                          <span class="knob" style=${`left:${look.knob}px;`}></span>
                        </button>
                      </div>
                    </div>`;
                })}
              </div>
            </div>

            <div class="section">
              ${header('#636366', ICON.list, T('log'), html`
                <div class="spacer"></div>
                <button class="link-button" onClick=${() => this.setState({ log: [] })}>${T('clear')}</button>`)}
              <div class="log">
                ${st.log.map((l, i) => html`
                  <div key=${i} class="log-line"><span class="time">${l.t}</span><span class="message">${l.m}</span></div>`)}
              </div>
            </div>
          </div>
        </div>`;
    }

    render() {
      const st = this.state;
      const scale = Math.min(st.viewW / STAGE_W, st.viewH / STAGE_H) || 1;
      const frame = `width:${Math.round(STAGE_W * scale)}px; height:${Math.round(STAGE_H * scale)}px;`;

      if (!st.p) {
        return html`<div class="viewport"><div class="frame" style=${frame}></div></div>`;
      }

      const sel = slotById(st.sel);
      const owner = {};
      SLOTS.forEach(s => {
        const b = s.id === 'action' ? null : st.p.bind[s.id];
        if (b) owner[b.key] = s;
      });
      st.p.actions.forEach(a => { owner[a.key] = slotById('action'); });
      const selMods = sel && sel.id !== 'action' && st.p.bind[sel.id] ? st.p.bind[sel.id].mods : [];

      return html`
        <div class="viewport">
          <div class="frame" style=${frame}>
            <div class="stage" style=${`transform:scale(${scale});`}>
              ${this.renderToolbar()}
              <div class="main">
                <div class="assignments">
                  <div class="slots">${this.renderSlots(sel)}</div>
                  ${this.renderHint(sel)}
                </div>
                <div class="devices">
                  <div class="devices-inner">
                    <div class="keyboard">${this.renderKeyboard(owner, sel, selMods)}</div>
                    ${this.renderMouse(owner, sel, selMods)}
                  </div>
                  ${this.renderPopover()}
                </div>
                ${this.renderDock()}
              </div>
              ${this.renderSettings()}
            </div>
          </div>
        </div>`;
    }
  }

  render(html`<${App} host=${window.GC_createHost()} />`, document.getElementById('root'));
})();
