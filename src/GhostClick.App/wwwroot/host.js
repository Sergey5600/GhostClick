// The connection to the engine.
//
// Inside the app the page talks to GhostClick.Bridge over WebView2 messages. Opened in a
// plain browser it gets a preview host instead: the same protocol, answered in memory, with
// simulated clicks - enough to review and test the interface without Windows. Scripts run
// here too, block by block, with the same "block" and "issues" messages as the bridge (see
// docs/scripts-ui-contract.md).
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

  // JSON with every object's keys sorted: compares profiles whatever order their fields came in.
  const canonical = value => JSON.stringify(value, (_, v) => (v && typeof v === 'object' && !Array.isArray(v)
    ? Object.fromEntries(Object.entries(v).sort(([a], [b]) => (a < b ? -1 : a > b ? 1 : 0)))
    : v));

  function emptyScript() {
    return { repeat: 'once', count: 10, stepDelay: 100, blocks: [] };
  }

  function defaultProfile() {
    return {
      kind: 'clicker',
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
      script: emptyScript(),
    };
  }

  // ------------------------------------------------------------------ scripts

  const newId = () => Math.random().toString(16).slice(2, 10);
  const key = (code, ...mods) => ({ mods, key: code });

  function block(type, label, fields) {
    return { type, id: newId(), enabled: true, label: label || null, ...fields };
  }

  // What the bridge gives a new script: one left click to build on.
  function defaultScript() {
    const profile = defaultProfile();
    profile.kind = 'script';
    profile.script.blocks = [block('press', null, { input: key('Mouse1') })];
    return profile;
  }

  // Demo scripts: every block type, a switched-off block, a disabled «Стоп», nested repeats.
  function fishingScript() {
    const profile = defaultScript();
    profile.script = {
      repeat: 'inf', count: 10, stepDelay: 150,
      blocks: [
        block('press', 'Закинуть удочку', { input: key('Mouse2') }),
        block('wait', 'Ждать поклёвку', { ms: 3000, rand: true, minMs: 2500, maxMs: 4500 }),
        block('press', 'Подсечь', { input: key('Mouse2') }),
        block('wait', 'Вытащить', { ms: 1200, rand: false, minMs: 500, maxMs: 1500 }),
        block('press', 'Убрать улов в сумку', { input: key('KeyE') }),
      ],
    };
    profile.script.blocks[4].enabled = false;
    return profile;
  }

  function farmScript() {
    const profile = defaultScript();
    profile.script = {
      repeat: 'fixed', count: 3, stepDelay: 100,
      blocks: [
        block('move', 'К грядке', { to: { x: 640, y: 360 } }),
        block('repeat', 'Собрать урожай', {
          mode: 'fixed', count: 5,
          blocks: [
            block('press', 'Сорвать', { input: key('Mouse1'), at: { x: 640, y: 360 } }),
            block('wait', null, { ms: 300, rand: true, minMs: 250, maxMs: 400 }),
          ],
        }),
        block('hold', 'Идти вперёд', { input: key('KeyW') }),
        block('wait', null, { ms: 800, rand: false, minMs: 500, maxMs: 1500 }),
        block('release', 'Остановиться', { input: key('KeyW') }),
        block('press', 'Открыть сундук', { input: key('KeyE', 'Shift') }),
        block('stop', 'Только один круг', {}),
      ],
    };
    profile.script.blocks[6].enabled = false;
    return profile;
  }

  // Shows the error state: a press without a key and a repeat without a count.
  function draftScript() {
    const profile = defaultScript();
    profile.script.blocks = [
      block('press', 'Кнопка не выбрана', {}),
      block('repeat', null, { mode: 'fixed', count: 0, blocks: [block('wait', null, { ms: 500, rand: false, minMs: 500, maxMs: 1500 })] }),
    ];
    return profile;
  }

  const TITLES = {
    press: 'Нажать', hold: 'Зажать', release: 'Отпустить', move: 'Переместить курсор',
    wait: 'Ждать', repeat: 'Повторить', stop: 'Стоп',
  };

  const isInput = type => type === 'press' || type === 'hold' || type === 'release';
  const enabled = list => (list || []).filter(b => b && b.enabled !== false);

  // The same checks, in the same order and words, as the engine's validator and the bridge.
  function scriptIssues(script) {
    const issues = [];
    const add = (id, path, field, message) => issues.push({ block: id, path, field, message });

    if (!script.blocks || !script.blocks.some(b => b)) add(null, null, 'blocks', 'Добавьте в сценарий хотя бы один блок');
    else if (!enabled(script.blocks).length) add(null, null, 'blocks', 'Все блоки сценария выключены');
    if (script.repeat === 'fixed' && !(script.count >= 1)) add(null, null, 'count', 'Количество прогонов должно быть не меньше 1');
    if (script.stepDelay < 0) add(null, null, 'stepDelay', 'Пауза между шагами не может быть отрицательной');

    const ids = new Set();
    const check = (list, path, live) => (list || []).forEach((b, i) => {
      const at = [...path, i];
      if (!b) return add(null, at, null, 'Пустой блок');

      if (!b.id) add(null, at, 'id', 'У блока нет id');
      else if (ids.has(b.id)) add(b.id, at, 'id', 'Такой id уже есть у другого блока');
      ids.add(b.id);

      const on = live && b.enabled !== false;
      const fail = (field, message) => add(b.id || null, at, field, message);
      if (on && isInput(b.type)) {
        if (!b.input || !b.input.key) fail('input', 'Выберите клавишу или кнопку мыши');
      } else if (on && b.type === 'wait') {
        if (!b.rand && b.ms < 0) fail('ms', 'Время ожидания не может быть отрицательным');
        if (b.rand && b.minMs < 0) fail('minMs', 'Минимальное время ожидания не может быть отрицательным');
        if (b.rand && b.maxMs < b.minMs) fail('maxMs', 'Максимальное время ожидания меньше минимального');
      } else if (on && b.type === 'repeat') {
        if (b.mode !== 'inf' && !(b.count >= 1)) fail('count', 'Количество повторов должно быть не меньше 1');
        if (!enabled(b.blocks).length) fail('blocks', 'Внутри повтора нет включённых блоков');
      } else if (on && b.type !== 'move' && b.type !== 'stop') {
        fail('type', b.type ? 'Этот блок не поддерживается этой версией GhostClick' : 'У блока не указан тип');
      }

      if (b.type === 'repeat') check(b.blocks, at, on);
    });
    check(script.blocks, [], true);
    return issues;
  }

  function findBlock(list, id) {
    for (const b of list || []) {
      if (!b) continue;
      if (b.id === id) return b;
      const inner = findBlock(b.blocks, id);
      if (inner) return inner;
    }
    return null;
  }

  // The plan of a run, one step at a time, like GhostClick.Core's ScriptPlanner: blocks top to
  // bottom, the automatic pause between steps unless a «Ждать» stands in for it, repeats,
  // passes, «Стоп». A step is { block: where it is | null for the pause, ms, action }.
  function* planScript(script, holdMs) {
    const state = { pauseDue: false, stopped: false, steps: 0 };
    const passes = script.repeat === 'once' ? 1 : script.repeat === 'fixed' ? Math.max(1, script.count) : null;

    for (let pass = 1; passes === null || pass <= passes; pass++) {
      const before = state.steps;
      yield* planBlocks(script.blocks, { pass, passes, loops: [] });
      // A pass of nothing would loop forever without a single step to stop at.
      if (state.stopped || state.steps === before) return;
    }

    function* planBlocks(list, frame) {
      for (const b of enabled(list)) {
        if (state.stopped) return;
        if (b.type === 'stop') {
          state.stopped = true;
          return;
        }
        if (b.type === 'repeat') yield* planRepeat(b, frame);
        else yield* planStep(b, frame);
      }
    }

    function* planRepeat(b, frame) {
      const count = b.mode === 'inf' ? null : Math.max(1, b.count);
      for (let i = 1; count === null || i <= count; i++) {
        const before = state.steps;
        yield* planBlocks(b.blocks, { ...frame, loops: [...frame.loops, { id: b.id, iteration: i, count }] });
        if (state.stopped || state.steps === before) return;
      }
    }

    function* planStep(b, frame) {
      const at = { id: b.id, pass: frame.pass, passes: frame.passes, loops: frame.loops };
      if (b.type === 'wait') {
        state.pauseDue = false;
        state.steps++;
        const ms = b.rand ? b.minMs + Math.random() * Math.max(0, b.maxMs - b.minMs) : b.ms;
        yield { block: at, ms: Math.max(0, ms), action: false };
        return;
      }
      if (!isInput(b.type) && b.type !== 'move') return;

      if (state.pauseDue && script.stepDelay > 0) {
        state.steps++;
        yield { block: null, ms: script.stepDelay, action: false };
      }
      state.steps++;
      yield { block: at, ms: b.type === 'press' ? holdMs : 0, action: b.type === 'press' || b.type === 'release' };
      state.pauseDue = true;
    }
  }

  // ------------------------------------------------------------------ preview host

  function createPreviewHost() {
    const listeners = [];
    const saved = { Default: defaultProfile(), Fishing: defaultProfile(), Farm: defaultProfile() };
    saved.Fishing.actions = [{ mods: [], key: 'Mouse2' }];
    saved.Fishing.delay = 1200;
    saved.Farm.actions = [{ mods: [], key: 'Mouse1' }, { mods: [], key: 'KeyE' }];
    saved.Farm.click = 'double';
    saved['Рыбалка — сценарий'] = fishingScript();
    saved['Ферма — сценарий'] = farmScript();
    saved['Черновик сценария'] = draftScript();

    let name = 'Default';
    let current = clone(saved.Default);
    let run = 'stopped';
    let dryRun = false;
    let clicks = 0;
    let elapsed = 0;
    let resumedAt = 0;
    let timer = 0;
    let ticker = 0;
    let sentIssues = '';
    // A script run: its plan, the step under way and how much of it is left, the block it is on.
    let plan = null;
    let pending = null;
    let pendingLeft = 0;
    let pendingAt = 0;
    let runningBlock = null;

    const now = () => new Date().toTimeString().slice(0, 8);
    const emit = message => setTimeout(() => listeners.forEach(l => l(clone(message))), 0);
    const log = message => emit({ type: 'log', time: now(), message });
    const toast = (message, error) => emit({ type: 'toast', message, error: !!error });
    const profiles = () => Object.keys(saved);
    const dirty = () => canonical(current) !== canonical(saved[name]);
    const isScript = () => current.kind === 'script';
    const enabledPoints = () => current.points.map((p, i) => (p.enabled === false ? -1 : i)).filter(i => i >= 0);
    const elapsedMs = () => elapsed + (run === 'running' ? Date.now() - resumedAt : 0);

    function state() {
      const enabled = enabledPoints();
      return {
        type: 'state', state: run, clicks, elapsedMs: elapsedMs(),
        pointIndex: enabled.length && !isScript() ? enabled[clicks % enabled.length] : -1,
        block: runningBlock,
      };
    }

    // Problems with the current profile, as the bridge reports them: a script's by block.
    function issues() {
      const list = isScript() ? scriptIssues(current.script) : [];
      const problem = validateClicker();
      if (problem) list.push({ block: null, path: null, field: null, message: problem });
      sentIssues = JSON.stringify(list);
      return list;
    }

    function profileMessage() {
      return { type: 'profile', profiles: profiles(), profile: { name, data: current, dirty: dirty(), issues: issues() } };
    }

    // The checks of the rest of the profile; a script has no action list, interval or route.
    function validateClicker() {
      if (!isScript()) {
        if (!current.actions.length) return 'Выберите хотя бы одно действие';
        if (current.target === 'points' && !enabledPoints().length) return 'Добавьте хотя бы одну точку маршрута';
        if (current.rand && current.maxDelay < current.minDelay) return 'Максимальный интервал меньше минимального';
        if (current.repeat === 'fixed' && current.count < 1) return 'Количество повторов должно быть не меньше 1';
      }
      if (current.mode === 'hold' && !current.bind.toggle) return 'В режиме удержания нужна клавиша «Старт / Стоп»';
      return '';
    }

    // The first problem as a notice; a block's names the block, by its label or its type.
    function validate() {
      const first = isScript() && scriptIssues(current.script)[0];
      if (!first) return validateClicker();
      const b = first.block && findBlock(current.script.blocks, first.block);
      return b ? `«${(b.label || '').trim() || TITLES[b.type] || 'Неизвестный блок'}»: ${first.message}` : first.message;
    }

    function nextDelay() {
      const interval = current.rand
        ? current.minDelay + Math.random() * Math.max(0, current.maxDelay - current.minDelay)
        : current.delay;
      const hold = current.flags.holdRandom ? 35 : 0;
      return Math.max(10, interval + hold);
    }

    function schedule() {
      if (plan) return scriptStep();
      clearTimeout(timer);
      timer = setTimeout(() => {
        if (run !== 'running') return;
        clicks += 1;
        if (current.repeat === 'fixed' && clicks >= current.count) return stop('Выполнено');
        schedule();
      }, nextDelay());
    }

    function showBlock(next) {
      if (next === runningBlock) return;
      runningBlock = next;
      emit({ type: 'block', block: next });
    }

    // Runs the step under way (what is left of it after a pause), or takes the next one.
    function scriptStep() {
      clearTimeout(timer);
      if (run !== 'running') return;
      if (!pending) {
        const next = plan.next();
        if (next.done) return stop('Выполнено');
        pending = next.value;
        pendingLeft = pending.ms;
        if (pending.block) showBlock(pending.block);
      }
      pendingAt = Date.now();
      timer = setTimeout(() => {
        if (pending.action) clicks += 1;
        pending = null;
        scriptStep();
      }, pendingLeft);
    }

    function start() {
      if (run !== 'stopped') return;
      const problem = validate();
      if (problem) return toast(problem, true);
      run = 'running'; clicks = 0; elapsed = 0; resumedAt = Date.now();
      const fmt = c => [...c.mods, window.GC_LAYOUT.label(c.key)].join(' + ');
      if (isScript()) {
        const n = (function count(list) { return enabled(list).reduce((all, b) => all + 1 + count(b.blocks), 0); })(current.script.blocks);
        plan = planScript(clone(current.script), current.flags.holdRandom ? 35 : 0);
        log(`Старт · сценарий, ${n} ${plural(n, 'блок', 'блока', 'блоков')}${dryRun ? ' · dry run' : ''}`);
      } else {
        log(`Старт · ${current.actions.map(fmt).join(' + ')}${dryRun ? ' · dry run' : ''}`);
      }
      emit(state());
      schedule();
      ticker = setInterval(() => run !== 'stopped' && emit(state()), 250);
    }

    function stop(reason) {
      if (run === 'stopped') return;
      clearTimeout(timer); clearInterval(ticker);
      elapsed = elapsedMs(); run = 'stopped';
      plan = null; pending = null;
      showBlock(null);
      const n = clicks;
      log(`${reason} · ${n} ${plural(n, 'клик', 'клика', 'кликов')}`);
      emit(state());
    }

    function plural(n, one, few, many) {
      if (n % 10 === 1 && n % 100 !== 11) return one;
      return [2, 3, 4].includes(n % 10) && ![12, 13, 14].includes(n % 100) ? few : many;
    }

    function pause() {
      if (run !== 'running') return;
      clearTimeout(timer); elapsed = elapsedMs(); run = 'paused';
      if (pending) pendingLeft = Math.max(0, pendingLeft - (Date.now() - pendingAt));
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
        emit({
          type: 'init', profiles: profiles(), profile: { name, data: current, dirty: dirty(), issues: issues() },
          runtime: state(), dryRun, backend: 'Preview', hotkeys: false,
        });
        log(`Профиль «${name}» загружен`);
      },
      start, pause, resume,
      stop: () => stop('Остановлено'),
      panic: () => stop('Аварийная остановка'),
      setProfile(m) {
        // A page that predates scripts sends neither; they stay as they were.
        const data = clone(m.data);
        if (data.kind == null) data.kind = current.kind;
        if (data.script == null) data.script = current.script;
        current = data;
        emit({ type: 'dirty', value: dirty() });
        const before = sentIssues;
        const list = issues();
        if (sentIssues !== before) emit({ type: 'issues', issues: list });
      },
      saveProfile() {
        const problem = validate();
        if (problem) return toast(problem, true);
        saved[name] = clone(current);
        emit(profileMessage());
        toast(`Профиль «${name}» сохранён`); log(`Профиль «${name}» сохранён`);
      },
      revertProfile() { current = clone(saved[name]); emit(profileMessage()); log(`Профиль «${name}» загружен`); },
      loadProfile(m) { if (m.name !== name && saved[m.name]) load(m.name); },
      newProfile(m) {
        const script = m.kind === 'script';
        const next = unique(i => (script ? `Сценарий ${i}` : `Профиль ${i}`), profiles().length + 1);
        saved[next] = script ? defaultScript() : defaultProfile();
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
