// The connection to the engine.
//
// Inside the app the page talks to GhostClick.Bridge over WebView2 messages. Opened in a
// plain browser it gets a preview host instead: the same protocol, answered in memory, with
// simulated clicks - enough to review and test the interface without Windows.
(function () {
  'use strict';

  function createHost() {
    const webview = window.chrome && window.chrome.webview;
    if (webview) {
      return {
        native: true,
        send: message => webview.postMessage(message),
        listen: callback => webview.addEventListener('message', event => callback(event.data)),
      };
    }

    return createPreviewHost();
  }

  const clone = value => JSON.parse(JSON.stringify(value));

  function defaultProfile() {
    return {
      bind: {
        toggle: { mods: [], key: 'F6' },
        pause: { mods: [], key: 'F7' },
        panic: { mods: ['Ctrl'], key: 'F12' },
        point: { mods: [], key: 'F9' },
      },
      actions: [{ mods: [], key: 'Mouse1' }],
      points: [],
      mode: 'toggle', target: 'cursor', click: 'single', move: 'instant',
      repeat: 'inf', count: 100, delay: 100, rand: false, minDelay: 80, maxDelay: 140,
      flags: { jitter: true, holdRandom: true, stopOnMouseMove: false, activeWindowOnly: true },
      jitterPx: 3,
    };
  }

  function createPreviewHost() {
    const listeners = [];
    const saved = { Default: defaultProfile(), Fishing: defaultProfile(), Farm: defaultProfile() };
    saved.Fishing.actions = [{ mods: [], key: 'Mouse2' }];
    saved.Fishing.delay = 1200;
    saved.Farm.actions = [{ mods: [], key: 'Mouse1' }, { mods: [], key: 'KeyE' }];
    saved.Farm.click = 'double';

    let name = 'Default';
    let current = clone(saved.Default);
    let run = 'stopped';
    let dryRun = false;
    let clicks = 0;
    let elapsed = 0;
    let resumedAt = 0;
    let timer = 0;
    let ticker = 0;

    const now = () => new Date().toTimeString().slice(0, 8);
    const emit = message => setTimeout(() => listeners.forEach(l => l(clone(message))), 0);
    const log = message => emit({ type: 'log', time: now(), message });
    const toast = (message, error) => emit({ type: 'toast', message, error: !!error });
    const profiles = () => Object.keys(saved);
    const dirty = () => JSON.stringify(current) !== JSON.stringify(saved[name]);
    const enabledPoints = () => current.points.map((p, i) => (p.enabled === false ? -1 : i)).filter(i => i >= 0);
    const elapsedMs = () => elapsed + (run === 'running' ? Date.now() - resumedAt : 0);

    function state() {
      const enabled = enabledPoints();
      return {
        type: 'state', state: run, clicks, elapsedMs: elapsedMs(),
        pointIndex: enabled.length ? enabled[clicks % enabled.length] : -1,
      };
    }

    function profileMessage() {
      return { type: 'profile', profiles: profiles(), profile: { name, data: current, dirty: dirty() } };
    }

    function validate() {
      if (!current.actions.length) return 'Выберите хотя бы одно действие';
      if (current.target === 'points' && !enabledPoints().length) return 'Добавьте хотя бы одну точку маршрута';
      if (current.rand && current.maxDelay < current.minDelay) return 'Максимальный интервал меньше минимального';
      if (current.repeat === 'fixed' && current.count < 1) return 'Количество повторов должно быть не меньше 1';
      if (current.mode === 'hold' && !current.bind.toggle) return 'В режиме удержания нужна клавиша «Старт / Стоп»';
      return '';
    }

    function nextDelay() {
      const interval = current.rand
        ? current.minDelay + Math.random() * Math.max(0, current.maxDelay - current.minDelay)
        : current.delay;
      const hold = current.flags.holdRandom ? 35 : 0;
      return Math.max(10, interval + hold);
    }

    function schedule() {
      clearTimeout(timer);
      timer = setTimeout(() => {
        if (run !== 'running') return;
        clicks += 1;
        if (current.repeat === 'fixed' && clicks >= current.count) return stop('Выполнено');
        schedule();
      }, nextDelay());
    }

    function start() {
      if (run !== 'stopped') return;
      const problem = validate();
      if (problem) return toast(problem, true);
      run = 'running'; clicks = 0; elapsed = 0; resumedAt = Date.now();
      const fmt = c => [...c.mods, window.GC_LAYOUT.label(c.key)].join(' + ');
      log(`Старт · ${current.actions.map(fmt).join(' + ')}${dryRun ? ' · dry run' : ''}`);
      emit(state());
      schedule();
      ticker = setInterval(() => run !== 'stopped' && emit(state()), 250);
    }

    function stop(reason) {
      if (run === 'stopped') return;
      clearTimeout(timer); clearInterval(ticker);
      elapsed = elapsedMs(); run = 'stopped';
      const n = clicks;
      const word = n % 10 === 1 && n % 100 !== 11 ? 'клик' : [2, 3, 4].includes(n % 10) && ![12, 13, 14].includes(n % 100) ? 'клика' : 'кликов';
      log(`${reason} · ${n} ${word}`);
      emit(state());
    }

    function pause() {
      if (run !== 'running') return;
      clearTimeout(timer); elapsed = elapsedMs(); run = 'paused';
      log('Пауза'); emit(state());
    }

    function resume() {
      if (run !== 'paused') return;
      run = 'running'; resumedAt = Date.now();
      log('Продолжено'); emit(state()); schedule();
    }

    function load(next) {
      stop('Остановлено');
      name = next; current = clone(saved[next]);
      emit(profileMessage());
      log(`Профиль «${next}» загружен`);
    }

    function unique(make, first) {
      for (let i = first; ; i++) if (!saved[make(i)]) return make(i);
    }

    const handlers = {
      ready() {
        emit({ type: 'init', profiles: profiles(), profile: { name, data: current, dirty: dirty() }, runtime: state(), dryRun, backend: 'Preview', hotkeys: false });
        log(`Профиль «${name}» загружен`);
      },
      start, pause, resume,
      stop: () => stop('Остановлено'),
      panic: () => stop('Аварийная остановка'),
      setProfile(m) { current = clone(m.data); emit({ type: 'dirty', value: dirty() }); },
      saveProfile() {
        const problem = validate();
        if (problem) return toast(problem, true);
        saved[name] = clone(current);
        emit(profileMessage());
        toast(`Профиль «${name}» сохранён`); log(`Профиль «${name}» сохранён`);
      },
      revertProfile() { current = clone(saved[name]); emit(profileMessage()); log(`Профиль «${name}» загружен`); },
      loadProfile(m) { if (m.name !== name && saved[m.name]) load(m.name); },
      newProfile() {
        const next = unique(i => `Профиль ${i}`, profiles().length + 1);
        saved[next] = defaultProfile();
        load(next);
      },
      duplicateProfile() {
        const problem = validate();
        if (problem) return toast(problem, true);
        const source = name;
        const next = unique(i => (i === 1 ? `${source} (копия)` : `${source} (копия ${i})`), 1);
        saved[next] = clone(current);
        name = next;
        emit(profileMessage());
        log(`Профиль «${next}» создан из «${source}»`);
      },
      setDryRun(m) { if (run !== 'stopped') stop('Остановлено'); dryRun = !!m.value; },
      listen() {},
    };

    return {
      native: false,
      send(message) { const handler = handlers[message && message.type]; if (handler) handler(message); },
      listen(callback) { listeners.push(callback); },
      // There are no global hooks in a browser; the page calls these for the hotkeys it sees.
      previewHotkey(slot, point) {
        if (slot === 'toggle') return run === 'stopped' ? start() : stop('Остановлено');
        if (slot === 'pause') return run === 'running' ? pause() : resume();
        if (slot === 'panic') return stop('Аварийная остановка');
        if (slot === 'point') emit({ type: 'pointAdded', x: point.x, y: point.y });
      },
    };
  }

  window.GC_createHost = createHost;
})();
