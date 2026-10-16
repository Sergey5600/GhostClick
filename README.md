<div align="center">

<img src="assets/banner.png" alt="GhostClick" width="100%">

<br><br>

<img src="assets/icon.png" alt="GhostClick icon" width="96">

# GhostClick

**Автокликер для Windows, который двигает мышь как человек.**
Клики, нажатия клавиш, маршруты по точкам и плавное движение курсора — всё на глобальных хоткеях.

<br>

![Windows](https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4?style=for-the-badge&logo=windows&logoColor=white)
![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)
![C#](https://img.shields.io/badge/C%23-12-239120?style=for-the-badge&logo=csharp&logoColor=white)
![Tests](https://img.shields.io/badge/тесты-120%2B-success?style=for-the-badge)

[Возможности](#-возможности) •
[Быстрый старт](#-быстрый-старт) •
[Использование](#-использование-движка) •
[Архитектура](#-архитектура) •
[Игры и анти-чит](#-совместимость-с-играми) •
[Статус](#-статус)

</div>

---

## ✨ Возможности

<table>
<tr>
<td width="50%" valign="top">

### 🖱️ Клики и клавиши
- **Toggle** — старт/стоп по хоткею
- **Hold** — кликает, пока зажата клавиша
- Фиксированное количество или бесконечно
- Интервал с точностью **1 мс**, можно случайный `min–max`
- ЛКМ / ПКМ / СКМ / X1 / X2, одиночный и двойной клик
- Автонажатие клавиш с модификаторами

</td>
<td width="50%" valign="top">

### 🎯 Куда кликать
- **На месте** — курсор вообще не двигается
- **Фиксированная точка**
- **Макро-маршрут** — список точек, у каждой своя задержка
- **Направленный дрейф** — движение по углу с кликами по пути
- Запись точки: навёл курсор → нажал хоткей

</td>
</tr>
<tr>
<td width="50%" valign="top">

### 🌊 Движение мыши
- **Instant** — телепорт
- **Smooth** — `Linear` / `EaseIn` / `EaseOut` / `EaseInOut`
- **Directional** — угол 0–360°, скорость в px/s, поведение у края экрана: `Stop` / `Bounce` / `Wrap` / `Restart`
- Субпиксельная точность — даже медленный дрейф реально едет

</td>
<td width="50%" valign="top">

### 👻 Рандомизация
- Случайное смещение точки клика по X/Y
- Дрожание траектории
- Разброс всех пауз на ±N %
- Ввод отправляется скан-кодами — игры с raw input его видят

</td>
</tr>
<tr>
<td width="50%" valign="top">

### ⌨️ Глобальные хоткеи
- Работают вне фокуса окна
- Любая клавиша или `Ctrl` / `Alt` / `Shift` / `Win` + клавиша
- Поддержка боковых кнопок мыши X1/X2
- Кликер не может затриггерить сам себя

</td>
<td width="50%" valign="top">

### 🛡️ Безопасность и профили
- Экстренный стоп (`Esc` по умолчанию)
- Лимиты по времени и количеству действий
- Стоп, если пользователь сам увёл курсор
- Зажатые кнопки гарантированно отпускаются
- JSON-профили, последний грузится сам

</td>
</tr>
</table>

---

## 🚀 Быстрый старт

Нужен [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```bash
git clone https://github.com/Sergey5600/GhostClick.git
cd GhostClick

# собрать всё решение (Windows)
dotnet build GhostClick.sln

# запустить dev-окно
dotnet run --project src/ClickerEngine.DevUI
```

По умолчанию: **F6** — старт/стоп, **Esc** — экстренный стоп.

> [!TIP]
> В dev-окне есть галочка **Dry run**: ввод никуда не уходит, а в журнале видно точный поток событий. Удобно проверять маршруты и сглаживание, не гоняя мышь по экрану.

---

## 💻 Использование движка

Вся логика живёт в библиотеке `ClickerEngine.Core` без зависимостей от UI — её можно подключить к любому интерфейсу.

```csharp
using var host = ClickerHost.CreateDefault();

var profile = host.Start();          // грузит последний профиль и включает хоткеи
profile.Interval.DelayMs = 50;
profile.Action.Button = MouseButton.Left;
host.ApplyProfile(profile);

host.Engine.Started += (_, e) => Console.WriteLine($"Старт: {e.Profile.Name}");
host.Engine.Stopped += (_, e) => Console.WriteLine($"Стоп: {e.Reason}, {e.ActionsPerformed} действий");

host.Engine.Start();                 // или просто жмёшь хоткей
```

> [!IMPORTANT]
> События движка поднимаются **на его собственном потоке**. В WPF маршаль их только через `Dispatcher.BeginInvoke`, не `Invoke`: `Stop()` ждёт поток движка, и синхронный вызов даст взаимную блокировку.

<details>
<summary><b>Собрать компоненты вручную</b></summary>

```csharp
var screen   = new Win32ScreenGeometry();
var injector = new SendInputInjector(screen);
var engine   = new AutoClickerEngine(injector, new SystemClock(), screen, new SystemRandomSource());
```

</details>

<details>
<summary><b>Где лежат профили</b></summary>

```
%APPDATA%\GhostClick\
├── settings.json          последний использованный профиль
└── profiles\
    ├── Default.json
    └── Fishing.json
```

Запись атомарная (через временный файл), enum'ы пишутся именами — файлы читаемые и правятся руками.

</details>

---

## 🏗️ Архитектура

| Проект | TFM | Назначение |
| --- | --- | --- |
| `src/ClickerEngine.Core` | `net8.0` | Весь движок: конфиг, планировщик, ввод, хоткеи, профили |
| `src/ClickerEngine.DevUI` | `net8.0-windows` (WPF) | Временное dev-окно для ручных проверок |
| `tests/ClickerEngine.Core.Tests` | `net8.0` | Юнит-тесты движка |

```
ClickerEngine.Core
├── Configuration/   ClickerProfile и все настройки — POCO, сериализуются в JSON
├── Input/           IInputInjector (шов к ОС), SendInputInjector, RecordingInputInjector
├── Hotkeys/         GlobalHotkeyService (low-level hooks), HotkeyController
├── Timing/          IClock: SystemClock (~1 мс) и VirtualClock для тестов
├── Motion/          интерполяция, easing, дрожание, направленный дрейф
├── Planning/        ActionPlanner: профиль → ленивый поток EngineStep
├── Engine/          AutoClickerEngine: поток выполнения, состояния, лимиты, счётчики
├── Profiles/        JSON-хранилище профилей + автозагрузка последнего
├── Points/          PointRecorder: запись координат по хоткею
└── ClickerHost      фасад — точка входа для любого UI
```

<details>
<summary><b>Ключевые решения подробнее</b></summary>

#### `IInputInjector` — единственный шов к ОС

Всё, что движок делает с системой, — пять примитивов:

```csharp
public interface IInputInjector : ICursorProvider
{
    void MoveTo(ScreenPoint position);
    void MouseButtonDown(MouseButton button);
    void MouseButtonUp(MouseButton button);
    void KeyDown(VirtualKey key);
    void KeyUp(VirtualKey key);
}
```

Штатная реализация — `SendInputInjector` (user-mode `SendInput`). Другой бэкенд (драйвер, железный девайс, удалённый агент) = новая реализация интерфейса, остальной код не меняется. `RecordingInputInjector` вместо реального ввода пишет журнал — на нём работают тесты и режим Dry run.

#### План отделён от исполнения

`ActionPlanner` превращает профиль в **ленивый поток** шагов `EngineStep` (`Wait` / `MoveTo` / `MouseDown` / `MouseUp` / `KeyDown` / `KeyUp`). Бесконечный режим — буквально бесконечная последовательность, останавливает её движок. Поэтому движок тестируется без часов, потоков и Windows API.

#### Часы и случайность инжектируются

`IClock` и `IRandomSource` — параметры конструктора. `VirtualClock` прогоняет пятиминутный сценарий мгновенно, `ScriptedRandomSource` делает проверки дрожания точными, а не статистическими.

#### Хоткеи на low-level хуках

`WH_KEYBOARD_LL` + `WH_MOUSE_LL` вместо `RegisterHotKey` — ради кнопок X1/X2, **отпускания** клавиши (на нём держится режим Hold) и «голых» клавиш. Хуки живут на своём потоке, а события поднимаются на втором — обработчик может блокироваться, не рискуя, что Windows снимет хук по `LowLevelHooksTimeout`. Свой ввод отфильтровывается по `LLKHF_INJECTED` + метке в `dwExtraInfo`.

#### Почему Core — чистый `net8.0`

Так вся логика (планировщик, интерполяция, профили) собирается и тестируется на любой платформе, а Win32-части помечены `[SupportedOSPlatform("windows")]`. Dev-окно на WPF собирается только на Windows.

</details>

---

## 🎮 Совместимость с играми

Ввод идёт через `SendInput` и работает в большинстве игр **без ядерного анти-чита**. Ради этого:

- клавиатура отправляется **скан-кодами** (`KEYEVENTF_SCANCODE`) — иначе игры на raw input / DirectInput игнорируют событие;
- движение абсолютное по всему виртуальному рабочему столу, с `MOUSEEVENTF_MOVE_NOCOALESCE`, чтобы Windows не склеивала шаги;
- per-monitor DPI awareness — записанные координаты совпадают с реальными.

> [!WARNING]
> Игры с ядерным анти-читом (BattlEye, EasyAntiCheat, Vanguard и т. п.) скорее всего всё равно увидят программный ввод: он помечен системой как инжектированный (`LLMHF_INJECTED`). Рандомизация меняет статистику, но не природу событий. Используй на свой риск и не нарушай правила игр.

> [!NOTE]
> Если целевое окно запущено от администратора, а кликер — нет, Windows отбросит ввод (UIPI). GhostClick в этом случае останавливается с понятной ошибкой, а не кликает в пустоту. Лечится запуском кликера от администратора.

---

## 🧪 Тесты

```bash
dotnet test tests/ClickerEngine.Core.Tests/ClickerEngine.Core.Tests.csproj
```

120+ тестов: планировщик, интерполяция и дрейф, жизненный цикл движка (старт/стоп/пауза, лимиты, отпускание кнопок, ошибки), профили, хоткеи (Toggle против Hold), запись точек. Движок и тесты собираются и на Linux.

---

## 📍 Статус

| Этап | |
| --- | :---: |
| MVP: хоткей старт/стоп, интервал, клик на месте, ЛКМ, JSON-профиль | ✅ |
| Фиксированная точка, макро-маршрут, dev-UI | ✅ |
| Плавное движение, дрожание, направленное движение | ✅ |
| Автонажатие клавиш, много профилей, failsafe, счётчики | ✅ |
| Настоящий UI вместо dev-окна | 🛠️ |

---

<div align="center">

Сделано в **SD Studio** 👻

</div>
