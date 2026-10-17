// Every string of the interface, in Russian and English. Carried over from the design's
// I18N dictionary; strings with parameters are functions. The host writes its own log lines
// and notices (GhostClick.Bridge/UiText.cs) and says the same things the same way.
(function () {
  'use strict';

  const ruPlural = (n, one, few, many) =>
    n % 10 === 1 && n % 100 !== 11 ? one : [2, 3, 4].includes(n % 10) && ![12, 13, 14].includes(n % 100) ? few : many;

  const I18N = {
    ru: {
      profile: 'Профиль', newProfile: 'Новый профиль', unsaved: 'Есть изменения', save: 'Сохранить', revert: 'Отменить', settings: 'Настройки', listening: 'Ожидание…', close: 'Закрыть', pressedTogether: 'Кликер нажимает одновременно', edit: 'Изменить', click: 'Клик', type: 'Тип', keyMode: 'Режим клавиши', target: 'Точка', cursorMove: 'Движение курсора', route: 'Маршрут', clear: 'Очистить', del: 'Удалить', timing: 'Темп', interval: 'Интервал', ms: 'мс', randInterval: 'Случайный интервал', randSub: 'Каждая пауза между кликами разная', repeat: 'Повтор', safety: 'Безопасность', log: 'Журнал',
      s_toggle: 'Старт / Стоп', d_toggle: 'Запускает и останавливает кликер', g_toggle: 'СТАРТ',
      s_pause: 'Пауза', d_pause: 'Приостанавливает без сброса счётчиков', g_pause: 'ПАУЗА',
      s_panic: 'Аварийная остановка', d_panic: 'Немедленно прекращает все действия', g_panic: 'СТОП',
      s_action: 'Действия', d_action: 'Клавиши и кнопки, которые кликер нажимает одновременно', g_action: 'КЛИК',
      s_point: 'Добавить точку', d_point: 'Добавляет позицию курсора в маршрут', g_point: 'ТОЧКА',
      lbl_Mouse1: 'ЛКМ', lbl_Mouse2: 'ПКМ', lbl_Mouse3: 'СКМ', lbl_Mouse4: 'X1', lbl_Mouse5: 'X2',
      mn_Mouse1: 'Левая кнопка', mn_Mouse2: 'Правая кнопка', mn_Mouse3: 'Колесо (СКМ)', mn_Mouse4: 'Боковая X1', mn_Mouse5: 'Боковая X2',
      profLoaded: n => `Профиль «${n}» загружен`, removed: x => `Убрано: ${x}`, added: x => `Добавлено: ${x}`, unboundFrom: x => `, снято с «${x}»`,
      lmbErr: 'ЛКМ без модификатора нельзя назначить на горячую клавишу', removedFromActions: ', убрано из действий', assigned: m => `Назначено — ${m}`, pickFirst: 'Сначала выберите действие сверху',
      ptLog: (n, x, y) => `Точка ${n}: ${x}, ${y}`, ptAdded: (n, x, y) => `Точка ${n} добавлена: ${x}, ${y}`, done: 'Выполнено', stopped: 'Остановлено',
      start: 'Старт', stop: 'Стоп', noActions: 'нет действий', nClicks: n => `${n} ${ruPlural(n, 'клик', 'клика', 'кликов')}`, pause: 'Пауза', resumed: 'Продолжено', resume: 'Продолжить', mod: 'МОД',
      flags: [[px => `Джиттер координат ±${px} px`, 'Точка клика немного смещается каждый раз'], ['Случайная длительность нажатия', 'Кнопка удерживается разное время'], ['Стоп при движении мыши', 'Кликер остановится, если вы возьмёте мышь'], ['Только активное окно', 'Клики не уходят в другие программы']],
      cTime: 'Время', cClicks: 'Кликов', cCps: 'В секунду', cLeft: 'Осталось', runStopped: 'Остановлен', runRunning: 'Работает', runPaused: 'На паузе',
      hintInit: 'Выберите действие, затем клавишу на клавиатуре или кнопку мыши', hintAction: 'Отмечайте клавиши и кнопки мыши: все отмеченные нажимаются одновременно. Повторное нажатие убирает', hintKey: x => `Нажмите клавишу для «${x}». Ctrl, Shift, Alt и Win добавляются к сочетанию`,
      profN: n => `Профиль ${n}`, copyOf: (n, i) => (i > 1 ? `${n} (копия ${i})` : `${n} (копия)`), dup: n => `Дублировать «${n}»`, profSaved: n => `Профиль «${n}» сохранён`, on: 'вкл', off: 'выкл',
      profCreated: n => `Профиль «${n}» создан`, profCopied: (n, source) => `Профиль «${n}» создан из «${source}»`,
      dbl: 'Двойной клик', sgl: 'Одиночный клик', every: d => `каждые ${d} мс`, times: n => `${n} раз`, unlimited: 'без ограничения',
      o_toggle: 'Переключение', o_hold: 'Удержание', o_cursor: 'Курсор', o_point: 'По точкам', o_single: 'Одиночный', o_double: 'Двойной', o_instant: 'Мгновенно', o_smooth: 'Плавно', o_inf: 'Бесконечно', o_fixed: 'N раз',
      nPoints: n => `${n} ${ruPlural(n, 'точка', 'точки', 'точек')}`, ptN: n => `Точка ${n}`, ptKeyFb: 'клавишу точки',
      ptHint: k => `Наведите курсор и нажмите ${k}, чтобы добавить точку. Курсор обходит точки по очереди и кликает в каждой.`,
      removeAll: 'Убрать все', unbind: 'Снять', doneBtn: 'Готово', cancel: 'Отмена', actionsCleared: 'Действия очищены', noBinding: x => `«${x}» без назначения`,
      popTitle: 'Как нажимать', pm_click: 'Клик', pm_hold: 'Зажать', pressDur: 'Длительность нажатия', pressDurSub: 'Сколько клавиша удерживается при каждом клике', holdFor: 'Удерживать', h_run: 'Пока работает', h_time: 'Время', releaseAfter: 'Отпускать через', holdRunSub: 'Клавиша зажата всё время работы кликера. Отпускается на паузе и стопе', holdTimeSub: 'В каждом такте клавиша зажимается на это время и отпускается', removeAct: 'Убрать из действий', g_hold: 'ДЕРЖ',
      dryRunTitle: 'Ввод не уходит в систему',
      vNoAction: 'Выберите хотя бы одно действие', vNoPoint: 'Добавьте хотя бы одну точку маршрута', vMaxBelowMin: 'Максимальный интервал меньше минимального', vCount: 'Количество повторов должно быть не меньше 1', vHoldKey: 'В режиме удержания нужна клавиша «Старт / Стоп»',
    },
    en: {
      profile: 'Profile', newProfile: 'New profile', unsaved: 'Unsaved changes', save: 'Save', revert: 'Revert', settings: 'Settings', listening: 'Listening…', close: 'Close', pressedTogether: 'Clicker presses together', edit: 'Edit', click: 'Click', type: 'Type', keyMode: 'Key mode', target: 'Target', cursorMove: 'Cursor movement', route: 'Route', clear: 'Clear', del: 'Delete', timing: 'Timing', interval: 'Interval', ms: 'ms', randInterval: 'Random interval', randSub: 'Each pause between clicks is different', repeat: 'Repeat', safety: 'Safety', log: 'Log',
      s_toggle: 'Start / Stop', d_toggle: 'Starts and stops the clicker', g_toggle: 'START',
      s_pause: 'Pause', d_pause: 'Pauses without resetting counters', g_pause: 'PAUSE',
      s_panic: 'Emergency stop', d_panic: 'Stops all actions immediately', g_panic: 'STOP',
      s_action: 'Actions', d_action: 'Keys and buttons the clicker presses together', g_action: 'CLICK',
      s_point: 'Add point', d_point: 'Adds the cursor position to the route', g_point: 'POINT',
      lbl_Mouse1: 'LMB', lbl_Mouse2: 'RMB', lbl_Mouse3: 'MMB', lbl_Mouse4: 'X1', lbl_Mouse5: 'X2',
      mn_Mouse1: 'Left button', mn_Mouse2: 'Right button', mn_Mouse3: 'Wheel (MMB)', mn_Mouse4: 'Side X1', mn_Mouse5: 'Side X2',
      profLoaded: n => `Profile “${n}” loaded`, removed: x => `Removed: ${x}`, added: x => `Added: ${x}`, unboundFrom: x => `, unbound from “${x}”`,
      lmbErr: 'LMB without a modifier can’t be used as a hotkey', removedFromActions: ', removed from actions', assigned: m => `Assigned — ${m}`, pickFirst: 'Pick an action above first',
      ptLog: (n, x, y) => `Point ${n}: ${x}, ${y}`, ptAdded: (n, x, y) => `Point ${n} added: ${x}, ${y}`, done: 'Completed', stopped: 'Stopped',
      start: 'Start', stop: 'Stop', noActions: 'no actions', nClicks: n => `${n} ${n === 1 ? 'click' : 'clicks'}`, pause: 'Pause', resumed: 'Resumed', resume: 'Resume', mod: 'MOD',
      flags: [[px => `Coordinate jitter ±${px} px`, 'The click point shifts slightly every time'], ['Random press duration', 'The button is held for a different time'], ['Stop on mouse movement', 'The clicker stops if you grab the mouse'], ['Active window only', 'Clicks don’t go to other programs']],
      cTime: 'Time', cClicks: 'Clicks', cCps: 'Per second', cLeft: 'Left', runStopped: 'Stopped', runRunning: 'Running', runPaused: 'Paused',
      hintInit: 'Pick an action, then a key on the keyboard or a mouse button', hintAction: 'Mark keys and mouse buttons: all marked ones are pressed together. Press again to remove', hintKey: x => `Press a key for “${x}”. Ctrl, Shift, Alt and Win are added to the combo`,
      profN: n => `Profile ${n}`, copyOf: (n, i) => (i > 1 ? `${n} (copy ${i})` : `${n} (copy)`), dup: n => `Duplicate “${n}”`, profSaved: n => `Profile “${n}” saved`, on: 'on', off: 'off',
      profCreated: n => `Profile “${n}” created`, profCopied: (n, source) => `Profile “${n}” created from “${source}”`,
      dbl: 'Double click', sgl: 'Single click', every: d => `every ${d} ms`, times: n => `${n} times`, unlimited: 'unlimited',
      o_toggle: 'Toggle', o_hold: 'Hold', o_cursor: 'Cursor', o_point: 'Points', o_single: 'Single', o_double: 'Double', o_instant: 'Instant', o_smooth: 'Smooth', o_inf: 'Infinite', o_fixed: 'N times',
      nPoints: n => `${n} ${n === 1 ? 'point' : 'points'}`, ptN: n => `Point ${n}`, ptKeyFb: 'the point key',
      ptHint: k => `Hover the cursor and press ${k} to add a point. The cursor visits the points in order and clicks each one.`,
      removeAll: 'Remove all', unbind: 'Unbind', doneBtn: 'Done', cancel: 'Cancel', actionsCleared: 'Actions cleared', noBinding: x => `“${x}” unbound`,
      popTitle: 'How to press', pm_click: 'Click', pm_hold: 'Hold', pressDur: 'Press duration', pressDurSub: 'How long the key is held on each click', holdFor: 'Hold', h_run: 'While running', h_time: 'Timed', releaseAfter: 'Release after', holdRunSub: 'The key stays down while the clicker runs. Released on pause and stop', holdTimeSub: 'Each cycle the key is held for this long, then released', removeAct: 'Remove from actions', g_hold: 'HOLD',
      dryRunTitle: 'Input doesn’t reach the system',
      vNoAction: 'Choose at least one action', vNoPoint: 'Add at least one route point', vMaxBelowMin: 'The maximum interval is less than the minimum', vCount: 'The repeat count must be at least 1', vHoldKey: 'Hold mode needs a “Start / Stop” key',
    },
  };

  const LANGS = ['ru', 'en'];
  let current = 'ru';

  // The page speaks one language at a time; switching applies to everything drawn after it.
  function setLang(lang) {
    current = LANGS.includes(lang) ? lang : 'ru';
    document.documentElement.lang = current;
  }

  function t(key, ...args) {
    const value = (I18N[current] || I18N.ru)[key];
    return typeof value === 'function' ? value(...args) : value;
  }

  window.GC_I18N = { I18N, LANGS, setLang, lang: () => current, t };
})();
