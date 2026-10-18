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
      scriptN: n => `Сценарий ${n}`, scriptSummary: n => `сценарий, ${n} ${ruPlural(n, 'блок', 'блока', 'блоков')}`,
      dbl: 'Двойной клик', sgl: 'Одиночный клик', every: d => `каждые ${d} мс`, times: n => `${n} раз`, unlimited: 'без ограничения',
      o_toggle: 'Переключение', o_hold: 'Удержание', o_cursor: 'Курсор', o_point: 'По точкам', o_single: 'Одиночный', o_double: 'Двойной', o_instant: 'Мгновенно', o_smooth: 'Плавно', o_inf: 'Бесконечно', o_fixed: 'N раз',
      nPoints: n => `${n} ${ruPlural(n, 'точка', 'точки', 'точек')}`, ptN: n => `Точка ${n}`, ptKeyFb: 'клавишу точки',
      ptHint: k => `Наведите курсор и нажмите ${k}, чтобы добавить точку. Курсор обходит точки по очереди и кликает в каждой.`,
      removeAll: 'Убрать все', unbind: 'Снять', doneBtn: 'Готово', cancel: 'Отмена', actionsCleared: 'Действия очищены', noBinding: x => `«${x}» без назначения`,
      popTitle: 'Как нажимать', pm_click: 'Клик', pm_hold: 'Зажать', pressDur: 'Длительность нажатия', pressDurSub: 'Сколько клавиша удерживается при каждом клике', holdFor: 'Удерживать', h_run: 'Пока работает', h_time: 'Время', releaseAfter: 'Отпускать через', holdRunSub: 'Клавиша зажата всё время работы кликера. Отпускается на паузе и стопе', holdTimeSub: 'В каждом такте клавиша зажимается на это время и отпускается', removeAct: 'Убрать из действий', g_hold: 'ДЕРЖ',
      dryRunTitle: 'Ввод не уходит в систему',
      vNoAction: 'Выберите хотя бы одно действие', vNoPoint: 'Добавьте хотя бы одну точку маршрута', vMaxBelowMin: 'Максимальный интервал меньше минимального', vCount: 'Количество повторов должно быть не меньше 1', vHoldKey: 'В режиме удержания нужна клавиша «Старт / Стоп»',
      scenario: 'Сценарий', up_title: 'Доступна новая версия', up_dlTitle: 'Загрузка обновления', up_readyTitle: 'Обновление готово', up_later: 'Позже', up_go: 'Обновить', up_skip: 'Пропустить эту версию', up_cancel: 'Отмена', up_pct: (p, a, b) => `${p}% · ${a} из ${b} МБ`, up_readyS: 'Перезапустите GhostClick, чтобы установить новую версию. Профили и сценарии сохранятся.', up_onExit: 'При выходе', up_restart: 'Перезапустить', up_skipped: v => `Версия ${v} пропущена`, up_exitNote: 'Обновление установится при выходе',
      b_press: 'Нажать', b_hold: 'Зажать', b_release: 'Отпустить', b_holdFor: 'Удержать', b_scroll: 'Прокрутить', b_drag: 'Перетащить', b_type: 'Ввести текст', b_move: 'Переместить', b_shift: 'Сдвинуть', b_wait: 'Ждать', b_waitKey: 'Ждать клавишу', b_repeat: 'Повторить', b_random: 'Случайно одно из', b_stop: 'Стоп', b_label: 'Метка', b_unknown: 'Неизвестный блок',
      bd_press: 'Клик кнопкой мыши или нажатие клавиши', bd_hold: 'Оставляет зажатым до блока «Отпустить»', bd_release: 'Отпускает зажатую кнопку или клавишу', bd_holdFor: 'Зажать, подождать и отпустить', bd_scroll: 'Колёсико вверх или вниз на N щелчков', bd_drag: 'Зажать в точке A, довести до B, отпустить', bd_type: 'Печатает строку посимвольно', bd_move: 'Переводит курсор в точку', bd_shift: 'Смещение от текущей позиции', bd_wait: 'Пауза', bd_waitKey: 'Стоит, пока вы не нажмёте клавишу', bd_repeat: 'Повторяет вложенные блоки', bd_random: 'Выполняет один случайный вложенный блок', bd_stop: 'Завершает сценарий', bd_label: 'Заголовок-разделитель',
      g_mk: 'Мышь и клавиатура', g_cur: 'Курсор', g_time: 'Время', g_ctl: 'Управление',
      f_what: 'Что нажать', f_whatHold: 'Что зажать', f_whatRel: 'Что отпустить', f_whatKeep: 'Что держать', f_click: 'Клик', f_dur: 'Длительность нажатия', f_ms: 'Своя длительность', f_where: 'Где', f_point: 'Точка', f_time: 'Сколько', f_range: 'От – до', f_dir: 'Направление', f_count: 'Количество', f_btn: 'Кнопка', f_from: 'Откуда', f_to: 'Куда', f_speed: 'Скорость', f_text: 'Текст', f_charPause: 'Пауза между символами', f_move: 'Перемещение', f_moveTime: 'Время перемещения', f_dx: 'По горизонтали', f_dy: 'По вертикали', f_wait: 'Пауза', f_unit: 'Единицы', f_key: 'Клавиша', f_rep: 'Повтор', f_times: 'Сколько раз', f_note: 'Заметка', f_heading: 'Заголовок',
      op_profile: 'Из профиля', op_own: 'Своя', op_cursor: 'Где курсор', op_point: 'Точка', op_fixed: 'Фиксированно', op_rand: 'Случайно', op_up: 'Вверх', op_down: 'Вниз', op_slow: 'Медленно', op_mid: 'Средне', op_fast: 'Быстро', op_n: 'N раз', op_once: '1 раз', u_ms: 'мс', u_s: 'с', u_notch: 'щелч.',
      notches: n => `${n} ${ruPlural(n, 'щелчок', 'щелчка', 'щелчков')}`, nBlocks: n => `${n} ${ruPlural(n, 'блок', 'блока', 'блоков')}`, nVariants: n => `из ${n}`, timesN: n => `${n} ${ruPlural(n, 'раз', 'раза', 'раз')}`, qt: x => `«${x}»`,
      sc_ptype: 'Тип профиля', sc_normal: 'Обычный', sc_scn: 'Сценарий', sc_ptypeN: 'Старт запускает обычный кликер', sc_ptypeS: 'Старт запускает сценарий сверху вниз', sc_stepPause: 'Пауза между шагами', sc_runs: 'Выполнить', sc_runsN: 'Сколько раз', sc_lap: 'Один круг', sc_lapVal: (s, wk) => `≈ ${s} с${wk ? ' + ожидание клавиши' : ''}`, sc_lapInf: 'Бесконечно',
      sc_blocks: 'Блоки', sc_collapse: 'Свернуть всё', sc_expand: 'Развернуть всё', sc_undo: 'Отменить (Ctrl+Z)', sc_redo: 'Вернуть (Ctrl+Y)', sc_test: 'Тестовый прогон', sc_testStop: 'Остановить', sc_testStep: (i, n) => (n ? `Шаг ${i} из ${n} · без реальных кликов` : `Шаг ${i} · без реальных кликов`),
      sc_add: 'Добавить блок', sc_search: 'Поиск по названию', sc_cancel: 'Отмена', sc_nothing: 'Ничего не найдено', sc_runFrom: 'Запустить отсюда', sc_dup: 'Дублировать', sc_del: 'Удалить', sc_running: 'Выполняется', sc_waitingKey: k => `Жду ${k} — нажмите, чтобы продолжить`, sc_end: x => `Конец · ${x}`, sc_dropHere: 'перетащите блоки сюда',
      sc_pickHint: k => (k ? `Кликните в нужную точку или наведите курсор в другом окне и нажмите ${k} · Esc — отмена` : 'Кликните в нужную точку · Esc — отмена'), sc_pick: 'Выбрать на экране', sc_picked: (x, y) => `Точка: ${x}, ${y}`, sc_none: 'Не выбрано', sc_keyBtn: 'Клавиша…', sc_pressKey: 'Нажмите клавишу…', sc_noneHeld: 'Выше по сценарию ничего не зажато',
      e_key: 'Не выбрана клавиша', e_relNone: 'Выше по сценарию ничего не зажато', e_rel: k => `${k} не зажата выше по сценарию`, e_text: 'Пустой текст', e_empty: 'Внутри нет блоков',
      sc_emptyT: 'Сценарий пуст', sc_emptyS: 'Добавьте блоки — они выполнятся по очереди сверху вниз.', sc_testDone: 'Тестовый прогон завершён', sc_testErr: x => `Прогон остановлен: ошибка в блоке «${x}»`, sc_off: 'Выкл', sc_notePh: 'Видна в свёрнутом виде', sc_textPh: 'Например, /trade', sc_newLabel: '— Новый раздел —',
      ofN: (a, b) => `${a} из ${b}`,
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
      scriptN: n => `Script ${n}`, scriptSummary: n => `script, ${n} ${n === 1 ? 'block' : 'blocks'}`,
      dbl: 'Double click', sgl: 'Single click', every: d => `every ${d} ms`, times: n => `${n} times`, unlimited: 'unlimited',
      o_toggle: 'Toggle', o_hold: 'Hold', o_cursor: 'Cursor', o_point: 'Points', o_single: 'Single', o_double: 'Double', o_instant: 'Instant', o_smooth: 'Smooth', o_inf: 'Infinite', o_fixed: 'N times',
      nPoints: n => `${n} ${n === 1 ? 'point' : 'points'}`, ptN: n => `Point ${n}`, ptKeyFb: 'the point key',
      ptHint: k => `Hover the cursor and press ${k} to add a point. The cursor visits the points in order and clicks each one.`,
      removeAll: 'Remove all', unbind: 'Unbind', doneBtn: 'Done', cancel: 'Cancel', actionsCleared: 'Actions cleared', noBinding: x => `“${x}” unbound`,
      popTitle: 'How to press', pm_click: 'Click', pm_hold: 'Hold', pressDur: 'Press duration', pressDurSub: 'How long the key is held on each click', holdFor: 'Hold', h_run: 'While running', h_time: 'Timed', releaseAfter: 'Release after', holdRunSub: 'The key stays down while the clicker runs. Released on pause and stop', holdTimeSub: 'Each cycle the key is held for this long, then released', removeAct: 'Remove from actions', g_hold: 'HOLD',
      dryRunTitle: 'Input doesn’t reach the system',
      vNoAction: 'Choose at least one action', vNoPoint: 'Add at least one route point', vMaxBelowMin: 'The maximum interval is less than the minimum', vCount: 'The repeat count must be at least 1', vHoldKey: 'Hold mode needs a “Start / Stop” key',
      scenario: 'Scenario', up_title: 'New version available', up_dlTitle: 'Downloading update', up_readyTitle: 'Update ready', up_later: 'Later', up_go: 'Update', up_skip: 'Skip this version', up_cancel: 'Cancel', up_pct: (p, a, b) => `${p}% · ${a} of ${b} MB`, up_readyS: 'Restart GhostClick to install the new version. Profiles and scenarios are kept.', up_onExit: 'On exit', up_restart: 'Restart', up_skipped: v => `Version ${v} skipped`, up_exitNote: 'The update will install on exit',
      b_press: 'Press', b_hold: 'Hold down', b_release: 'Release', b_holdFor: 'Hold for', b_scroll: 'Scroll', b_drag: 'Drag', b_type: 'Type text', b_move: 'Move', b_shift: 'Offset', b_wait: 'Wait', b_waitKey: 'Wait for key', b_repeat: 'Repeat', b_random: 'Random one of', b_stop: 'Stop', b_label: 'Label', b_unknown: 'Unknown block',
      bd_press: 'Mouse click or key press', bd_hold: 'Keeps it down until a Release block', bd_release: 'Releases a held button or key', bd_holdFor: 'Press, wait and release', bd_scroll: 'Wheel up or down by N notches', bd_drag: 'Press at A, move to B, release', bd_type: 'Types a string character by character', bd_move: 'Moves the cursor to a point', bd_shift: 'Offset from the current position', bd_wait: 'Pause', bd_waitKey: 'Waits until you press a key', bd_repeat: 'Repeats the nested blocks', bd_random: 'Runs one random nested block', bd_stop: 'Ends the scenario', bd_label: 'Section heading',
      g_mk: 'Mouse & keyboard', g_cur: 'Cursor', g_time: 'Time', g_ctl: 'Control',
      f_what: 'What to press', f_whatHold: 'What to hold', f_whatRel: 'What to release', f_whatKeep: 'What to hold', f_click: 'Click', f_dur: 'Press duration', f_ms: 'Custom duration', f_where: 'Where', f_point: 'Point', f_time: 'How long', f_range: 'From – to', f_dir: 'Direction', f_count: 'Amount', f_btn: 'Button', f_from: 'From', f_to: 'To', f_speed: 'Speed', f_text: 'Text', f_charPause: 'Pause between characters', f_move: 'Movement', f_moveTime: 'Move time', f_dx: 'Horizontal', f_dy: 'Vertical', f_wait: 'Pause', f_unit: 'Units', f_key: 'Key', f_rep: 'Repeat', f_times: 'How many times', f_note: 'Note', f_heading: 'Heading',
      op_profile: 'From profile', op_own: 'Custom', op_cursor: 'At cursor', op_point: 'Point', op_fixed: 'Fixed', op_rand: 'Random', op_up: 'Up', op_down: 'Down', op_slow: 'Slow', op_mid: 'Medium', op_fast: 'Fast', op_n: 'N times', op_once: 'Once', u_ms: 'ms', u_s: 's', u_notch: 'notches',
      notches: n => `${n} ${n === 1 ? 'notch' : 'notches'}`, nBlocks: n => `${n} ${n === 1 ? 'block' : 'blocks'}`, nVariants: n => `of ${n}`, timesN: n => `${n} ${n === 1 ? 'time' : 'times'}`, qt: x => `“${x}”`,
      sc_ptype: 'Profile type', sc_normal: 'Regular', sc_scn: 'Scenario', sc_ptypeN: 'Start runs the regular clicker', sc_ptypeS: 'Start runs the scenario top to bottom', sc_stepPause: 'Pause between steps', sc_runs: 'Run', sc_runsN: 'How many times', sc_lap: 'One lap', sc_lapVal: (s, wk) => `≈ ${s} s${wk ? ' + key wait' : ''}`, sc_lapInf: 'Infinite',
      sc_blocks: 'Blocks', sc_collapse: 'Collapse all', sc_expand: 'Expand all', sc_undo: 'Undo (Ctrl+Z)', sc_redo: 'Redo (Ctrl+Y)', sc_test: 'Test run', sc_testStop: 'Stop', sc_testStep: (i, n) => (n ? `Step ${i} of ${n} · no real clicks` : `Step ${i} · no real clicks`),
      sc_add: 'Add block', sc_search: 'Search by name', sc_cancel: 'Cancel', sc_nothing: 'Nothing found', sc_runFrom: 'Run from here', sc_dup: 'Duplicate', sc_del: 'Delete', sc_running: 'Running', sc_waitingKey: k => `Waiting for ${k} — press it to continue`, sc_end: x => `End · ${x}`, sc_dropHere: 'drag blocks here',
      sc_pickHint: k => (k ? `Click the point you need, or hover it in another window and press ${k} · Esc to cancel` : 'Click the point you need · Esc to cancel'), sc_pick: 'Pick on screen', sc_picked: (x, y) => `Point: ${x}, ${y}`, sc_none: 'Not set', sc_keyBtn: 'Key…', sc_pressKey: 'Press a key…', sc_noneHeld: 'Nothing is held above in the scenario',
      e_key: 'No key selected', e_relNone: 'Nothing is held above in the scenario', e_rel: k => `${k} is not held above in the scenario`, e_text: 'Empty text', e_empty: 'No blocks inside',
      sc_emptyT: 'Scenario is empty', sc_emptyS: 'Add blocks — they run one by one from top to bottom.', sc_testDone: 'Test run finished', sc_testErr: x => `Run stopped: error in “${x}”`, sc_off: 'Off', sc_notePh: 'Shown when collapsed', sc_textPh: 'e.g. /trade', sc_newLabel: '— New section —',
      ofN: (a, b) => `${a} of ${b}`,
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
