<p align="center">
  <img src="assets/readme/banner.png" alt="GhostClick — an autoclicker for Windows" width="900">
</p>

<p align="center">
  <sub><b>English</b> · <a href="README.ru.md">Русский</a></sub>
</p>

<p align="center">
  <b>GhostClick presses keys and mouse buttons for you.</b><br>
  Pick what to press, hit <b>F6</b>, and it clicks on its own: on a timer wherever your cursor is,
  or along a route of points on the screen. A little randomness in where it clicks and how long
  each press lasts makes the clicks look human.
</p>

<p align="center">
  <a href="https://github.com/Sergey5600/GhostClick/releases/latest"><img src="assets/readme/download-en.png" alt="Download for Windows" width="324"></a>
  <br>
  <sub>Windows 10 and 11, 64-bit · the interface is in Russian for&nbsp;now</sub>
</p>

## How to install

1. Click **Download for Windows**. On the page that opens, under **Assets**, click the file whose
   name starts with **GhostClick-Setup** and ends with **.exe** to download it. You don't need
   **Source code (zip)** or **Source code (tar.gz)**. If your browser warns that the file
   "isn't commonly downloaded", choose **Keep** (and confirm once more if it asks again).
2. Open the downloaded file. If a "Windows protected your PC" window appears, click
   **More info** → **Run anyway**: that's how Windows warns about apps without a digital signature.
3. Keep clicking **Next**, then click **Install**. That's it: GhostClick is now in the Start menu
   and on your desktop. (If your Windows isn't in English, the installer is in Russian: **Далее**
   means Next and **Установить** means Install.)

You don't need administrator rights. If your PC is missing the Microsoft Edge WebView2 component,
the installer adds it for you.

## How to use

The GhostClick interface is in Russian for now, so the steps below give each label as you'll see
it, followed by what it means.

1. Click **Действия** (Actions) at the top of the window, pick what to press on the on-screen
   keyboard and mouse, and click **Готово** (Done). You can pick several, and they're pressed
   together. Right after installing, the left mouse button (**ЛКМ**) is already chosen.
2. Point the cursor where you need it and press **F6**: GhostClick starts clicking. Press **F6**
   again to stop, **F7** to pause, or **Ctrl + F12** for an emergency stop.
3. To click in different places, point the cursor and press **F9** for each one: every point is
   added to a route, and GhostClick visits them in turn.
4. Once everything is set up, click **Сохранить** (Save) at the top of the window, next to the
   profile name. Otherwise your settings are lost when GhostClick restarts.

The hotkeys work even when the GhostClick window isn't active, for example while you're in a game.
You can change any of them: click it at the top of the window, then press the key you want.
Everything else is in **Настройки** (Settings) in the top-right corner.

<p align="center">
  <img src="assets/readme/screenshot.png" alt="The GhostClick window: assigned keys are highlighted on the keyboard and mouse, with the control buttons and counters at the bottom" width="900">
</p>

## What it can do

- **Mouse and keyboard:** left, right, middle and side mouse buttons, keys and combinations
  with Ctrl, Shift, Alt and Win, one at a time or several at once.
- **Single or double click**, an interval in milliseconds, forever or a set number of times.
- **Two modes:** the hotkey turns the clicker on and off, or it clicks only while you hold it.
- **A route of points:** the cursor moves from point to point instantly or smoothly.
- **Human-like:** a random interval, a slight random shift of the click point, varying press length.
- **Safety:** an emergency stop, a stop as soon as you grab the mouse, clicks only in the window
  where you started.
- **Profiles** for different games and tasks; the last one opens by itself at launch.

## Good to know

- **Updating:** download the new version and install it over the old one; your profiles are kept.
- **Uninstalling:** Settings → Apps → GhostClick → Uninstall. Your profiles stay unless you
  choose to remove them too.
- If a game runs as administrator, GhostClick has to run as administrator as well, or Windows
  won't let its presses through.
- Games with serious anti-cheat (BattlEye, Easy Anti-Cheat, Vanguard and the like) will most
  likely notice an autoclicker.

---

<sub>Developer docs (in Russian): [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md)</sub>
