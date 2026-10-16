// Geometry and names of the drawn keyboard and mouse. Coordinates are in the unscaled
// 1416x369 device block (the page scales it by 1.25). Keys are named by KeyboardEvent.code,
// mouse buttons Mouse1..Mouse5 (4 = X1, 5 = X2) - the same names the host understands.
(function () {
  'use strict';

  const S = 60;

  const SLOTS = [
    { id: 'toggle', title: 'Старт / Стоп', desc: 'Запускает и останавливает кликер', tag: 'СТАРТ', o: '48 209 88' },
    { id: 'pause', title: 'Пауза', desc: 'Приостанавливает без сброса счётчиков', tag: 'ПАУЗА', o: '255 159 10' },
    { id: 'panic', title: 'Аварийная остановка', desc: 'Немедленно прекращает все действия', tag: 'СТОП', o: '255 69 58' },
    { id: 'action', title: 'Действия', desc: 'Клавиши и кнопки, которые кликер нажимает одновременно', tag: 'КЛИК', o: '10 132 255' },
    { id: 'point', title: 'Добавить точку', desc: 'Добавляет позицию курсора в маршрут', tag: 'ТОЧКА', o: '191 90 242' },
  ];

  const MODS = {
    ControlLeft: 'Ctrl', ControlRight: 'Ctrl',
    ShiftLeft: 'Shift', ShiftRight: 'Shift',
    AltLeft: 'Alt', AltRight: 'Alt',
    MetaLeft: 'Win', MetaRight: 'Win',
  };

  const RU = {
    KeyQ: 'Й', KeyW: 'Ц', KeyE: 'У', KeyR: 'К', KeyT: 'Е', KeyY: 'Н', KeyU: 'Г', KeyI: 'Ш', KeyO: 'Щ', KeyP: 'З',
    BracketLeft: 'Х', BracketRight: 'Ъ',
    KeyA: 'Ф', KeyS: 'Ы', KeyD: 'В', KeyF: 'А', KeyG: 'П', KeyH: 'Р', KeyJ: 'О', KeyK: 'Л', KeyL: 'Д',
    Semicolon: 'Ж', Quote: 'Э',
    KeyZ: 'Я', KeyX: 'Ч', KeyC: 'С', KeyV: 'М', KeyB: 'И', KeyN: 'Т', KeyM: 'Ь', Comma: 'Б', Period: 'Ю',
    Backquote: 'Ё',
  };

  const KEYS = [];
  const rowTop = r => (r === 0 ? 0 : 15 + r * S);
  const add = (id, label, x, r, w = 1) =>
    KEYS.push({ id, label, left: Math.round(x * S), top: rowTop(r), width: Math.round(w * S - 6) });
  const seq = (r, x, list) => list.forEach(([id, label, w = 1]) => { add(id, label, x, r, w); x += w; });
  const letters = s => s.split('').map(c => ['Key' + c, c]);

  add('Escape', 'Esc', 0, 0);
  ['F1', 'F2', 'F3', 'F4'].forEach((f, i) => add(f, f, 2 + i, 0));
  ['F5', 'F6', 'F7', 'F8'].forEach((f, i) => add(f, f, 6.5 + i, 0));
  ['F9', 'F10', 'F11', 'F12'].forEach((f, i) => add(f, f, 11 + i, 0));
  seq(1, 0, [['Backquote', '`'], ...'1234567890'.split('').map(d => ['Digit' + d, d]), ['Minus', '-'], ['Equal', '='], ['Backspace', 'Backspace', 2]]);
  seq(2, 0, [['Tab', 'Tab', 1.5], ...letters('QWERTYUIOP'), ['BracketLeft', '['], ['BracketRight', ']'], ['Backslash', '\\', 1.5]]);
  seq(3, 0, [['CapsLock', 'Caps', 1.75], ...letters('ASDFGHJKL'), ['Semicolon', ';'], ['Quote', "'"], ['Enter', 'Enter', 2.25]]);
  seq(4, 0, [['ShiftLeft', 'Shift', 2.25], ...letters('ZXCVBNM'), ['Comma', ','], ['Period', '.'], ['Slash', '/'], ['ShiftRight', 'Shift', 2.75]]);
  seq(5, 0, [['ControlLeft', 'Ctrl', 1.25], ['MetaLeft', 'Win', 1.25], ['AltLeft', 'Alt', 1.25], ['Space', 'Space', 6.25], ['AltRight', 'Alt', 1.25], ['MetaRight', 'Win', 1.25], ['ContextMenu', 'Menu', 1.25], ['ControlRight', 'Ctrl', 1.25]]);

  const NX = 15.2;
  seq(0, NX, [['PrintScreen', 'PrtSc'], ['ScrollLock', 'ScrLk'], ['Pause', 'Pause']]);
  seq(1, NX, [['Insert', 'Ins'], ['Home', 'Home'], ['PageUp', 'PgUp']]);
  seq(2, NX, [['Delete', 'Del'], ['End', 'End'], ['PageDown', 'PgDn']]);
  add('ArrowUp', '↑', NX + 1, 4);
  seq(5, NX, [['ArrowLeft', '←'], ['ArrowDown', '↓'], ['ArrowRight', '→']]);

  const MOUSE = [
    { id: 'Mouse1', label: 'ЛКМ', name: 'Левая кнопка', d: 'M132 4 A110 110 0 0 0 22 114 L22 143 L132 143 Z', lx: 72, ly: 100, showTag: true, fs: 18 },
    { id: 'Mouse2', label: 'ПКМ', name: 'Правая кнопка', d: 'M132 4 A110 110 0 0 1 242 114 L242 143 L132 143 Z', lx: 192, ly: 100, showTag: true, fs: 18 },
    { id: 'Mouse3', label: '', name: 'Колесо (СКМ)', d: 'M115 53 a17 17 0 0 1 34 0 v46 a17 17 0 0 1 -34 0 Z', lx: 132, ly: 0, showTag: false, fs: 9 },
    { id: 'Mouse5', label: 'X2', name: 'Боковая X2', d: 'M23 162 L12 162 A9 9 0 0 0 3 171 L3 195 A9 9 0 0 0 12 204 L23 204 Z', lx: 13, ly: 187, showTag: false, fs: 11 },
    { id: 'Mouse4', label: 'X1', name: 'Боковая X1', d: 'M23 210 L12 210 A9 9 0 0 0 3 219 L3 243 A9 9 0 0 0 12 252 L23 252 Z', lx: 13, ly: 235, showTag: false, fs: 11 },
  ];

  const MOUSE_BODY = 'M22 114 A110 110 0 0 1 242 114 L242 254 A110 110 0 0 1 22 254 Z';

  // Lucide arrow-up/down/left/right.
  const ARROWS = {
    ArrowUp: ['m5 12 7-7 7 7', 'M12 19V5'],
    ArrowDown: ['M12 5v14', 'm19 12-7 7-7-7'],
    ArrowLeft: ['m12 19-7-7 7-7', 'M19 12H5'],
    ArrowRight: ['M5 12h14', 'm12 5 7 7-7 7'],
  };

  // Keys whose shifted symbol goes on top and plain symbol in the corner.
  const SUB = { Comma: ['<', ','], Period: ['>', '.'] };

  const LABEL = {};
  KEYS.forEach(k => { LABEL[k.id] = k.label; });
  MOUSE.forEach(m => { LABEL[m.id] = m.label || 'СКМ'; });

  // Keys the host may report that are not drawn (numpad, F13+, raw VK_xx codes).
  function label(code) {
    if (LABEL[code]) return LABEL[code];
    const numpad = /^Numpad(\d)$/.exec(code);
    if (numpad) return 'Num ' + numpad[1];
    return code;
  }

  window.GC_LAYOUT = { SLOTS, MODS, RU, KEYS, MOUSE, MOUSE_BODY, ARROWS, SUB, LABEL, label };
})();
