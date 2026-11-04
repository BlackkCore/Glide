<div align="center">

**English** · [فارسی](README.fa.md)

<br>

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/brand/glide-logo-dark.png">
  <img alt="Glide" src="docs/brand/glide-logo-light.png" width="300">
</picture>

### A calm, modern Windows client for v2rayN

The same engine you already trust (Xray, sing-box and mihomo), with an interface that is easy to read and pleasant to use.

[![Release](https://img.shields.io/github/v/release/BlackkCore/Glide?logo=github&label=Release&color=3157E6)](https://github.com/BlackkCore/Glide/releases/latest)
[![Downloads](https://img.shields.io/github/downloads/BlackkCore/Glide/total?logo=github&label=Downloads&color=3157E6)](https://github.com/BlackkCore/Glide/releases)
[![Windows 10/11](https://img.shields.io/badge/Windows-10%20%7C%2011-0078D6?logo=windows)](https://github.com/BlackkCore/Glide/releases/latest)
[![License](https://img.shields.io/badge/License-GPL--3.0-blue)](LICENSE)

[**⬇ Download for Windows**](https://github.com/BlackkCore/Glide/releases/latest)

<br>

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/screenshots/main-dark.png">
  <img alt="Glide main window" src="docs/screenshots/main-light.png" width="900">
</picture>

</div>

## Why Glide

v2rayN is powerful, but its window shows you everything at once. Glide keeps every feature and reorganizes the screen around what you actually do: pick a server, connect, and see that it's working.

### Connecting

- **One button to connect.** The power button turns the system proxy on and off. Below it are the current server, its protocol, a session timer and a live upload/download chart.
- **A status that tells the truth.** *Connected* only appears after Glide has checked that traffic really gets through, with the delay shown. Otherwise you see *Checking connection…*, *No internet*, *Port in use* (with the app that holds it) or *Engine stopped*, each with one button that fixes it.
- **Notices when the connection drops,** and again when it's back.
- **"Why can't I connect?"** One click checks, step by step: your own internet, DNS, Glide's engine, the server, real traffic through the server, and the exit IP with its country. Then it gives one plain answer, such as *"The server answers but carries no traffic: update the subscription or try another server."*
- **DNS leak check** in the same window, run through the tunnel, with a plain answer.
- **Exit IP and country** on the status card, after a one-time question (it asks api.ip.sb through the proxy).
- **Auto: fastest server.** One click per subscription. Glide keeps testing every server in the background, uses the fastest one that works, switches when one fails, and starts on the best-known server so the first seconds don't fail.
- **A tray icon that shows trouble:** a red dot while the connection has a problem.
- **Never touches what isn't its own.** Glide won't change or clear a Windows proxy that another program set, and won't start on a port another app is using. If another app holds the port, it tells you which one and offers to close it.
- **Routing in plain words:** *Bypass China sites*, *Only listed blocked sites* and *Everything through proxy*, each with a one-line explanation. On first start in Persian, Glide offers a ready-made Iran preset that sends Iranian sites direct.
- **"Capture all apps (TUN)"**, with an explanation and an *Administrator* label instead of a bare "Enable Tun" switch.

### Servers and subscriptions

- **A list, not a spreadsheet.** One clear row per server: flag, name, protocol and transport, a colored delay pill, an ⋯ menu, and an accent bar on the active server. The full table is still one click away as the advanced view.
- **Favourites.** Star a server to keep it at the top of the list and in the tray menu.
- **Add server, the easy way.** One sheet for everything: paste links or a subscription address and see what was found before importing (*"2 servers: VLESS, Trojan"*). You can also scan a QR code on screen or in an image, or fill in a form for 12 protocols.
- **Subscription status.** Data used and left, days until expiry, and the time of the last update, shown under the selected subscription. It turns red when the plan is nearly used up or has expired.
- **Warnings before a plan runs out:** a few days before expiry and when less than 10% of the data is left, plus when the next automatic update runs.
- **Quick switcher (Ctrl+K):** type a few letters to jump to a server or run an action.
- **Recent servers** and favourites in the tray, and **Send to phone**: a QR code for one server or a whole subscription.
- **Live search** as you type, plus a labelled **Test all** button.
- **A tray menu for everyday use:** Connect / Disconnect first, a Servers submenu with favourites and recent servers, then the proxy modes.

### Settings and updates

- **Basic and advanced.** Settings open in Basic mode with just the everyday options. *Show advanced settings* reveals everything else.
- **Search all settings.** Type a word and only the matching rows remain. Ctrl+F jumps to the search box.
- **Update all.** One button updates the app, the proxy engines and the rule files, each with a plain name.
- **Choose apps for routing:** tick running apps (Telegram through the proxy, banking direct) instead of typing rules. Applies with "Capture all apps (TUN)".
- **Data usage:** today, in total and per server, with an optional daily limit warning.
- **Simpler server form:** rarely needed fields wait behind *Show advanced fields*.

### Look and feel

- **Light, dark or follow Windows.** Switching themes spreads the new colors out in a circle from the button you clicked.
- **Persian, done properly.** A complete Persian translation, checked automatically on every build, with the whole window mirrored right-to-left.
- **Fits any window size.** On narrow windows the sidebar collapses to icons and the layout rearranges itself instead of cutting text off. The log pane can be hidden.
- **Accessible.** Icon buttons have names that screen readers announce.
- **Feels native on Windows 11.** Rounded windows, a theme-aware title bar, and animations that follow Windows' *Animation effects* setting.
- **Move over in one click.** On first start Glide finds your existing v2rayN and offers to import your servers, subscriptions and settings. It backs up your current settings first.

<table>
  <tr>
    <td width="50%"><img src="docs/screenshots/menu-dark.png" alt="Sidebar menu"></td>
    <td width="50%"><img src="docs/screenshots/compact-light.png" alt="Compact layout on a narrow window"></td>
  </tr>
  <tr>
    <td align="center"><sub>Sidebar menus</sub></td>
    <td align="center"><sub>Compact layout for narrow windows</sub></td>
  </tr>
  <tr>
    <td width="50%"><img src="docs/screenshots/settings-light.png" alt="Settings"></td>
    <td width="50%"><img src="docs/screenshots/import-prompt.png" alt="Import from the original v2rayN"></td>
  </tr>
  <tr>
    <td align="center"><sub>Settings in sections</sub></td>
    <td align="center"><sub>One-click import from v2rayN</sub></td>
  </tr>
</table>

## Get started

1. Download **`Glide-setup-x64.exe`** from the [latest release](https://github.com/BlackkCore/Glide/releases/latest) and run it: no admin rights needed, with a Start menu entry and an uninstaller. Prefer a portable copy? Take **`Glide-windows-x64.zip`** instead (or the `x86` files for 32-bit Windows). The Xray, sing-box and mihomo cores are included.
2. Zip only: extract it to a folder you can write to, for example `C:\Glide`. Avoid `Program Files`.
3. If the original v2rayN is running, exit it first (right-click its tray icon → *Exit*).
4. Run **`Glide.exe`**. Accept the import offer, or click **Add server** above the server list: paste a share link or subscription address, or scan a QR code.
5. Press the power button. When the status says *Connected*, you're done. If it doesn't, click *Why can't I connect?*.

**Requirements:** Windows 10 or 11, 64-bit or 32-bit.

> [!TIP]
> Glide and the original v2rayN can stay installed side by side, but don't run both at once. They use the same local port and would fight over the Windows proxy setting.

## Updating

- Open *Check update* in the sidebar and press **Update all**. It updates Glide, the proxy engines and the rule files together.
- Or download the new zip and extract it over the old folder. Your servers live in `guiConfigs` and are kept.

Glide follows [2dust/v2rayN](https://github.com/2dust/v2rayN): new upstream versions are merged in and released here with the redesign on top.

## Verify your download

Every release file has a `.sig` signature made with this key:

```text
D62D C3BC D066 5F52 5BB6  7206 E862 9FD9 AF46 1E53
```

```bash
gpg --import Glide-public-key.asc
gpg --verify Glide-windows-x64.zip.sig Glide-windows-x64.zip
```

`Glide-public-key.asc` is attached to every release. The output should say *Good signature from "BlackkCore Releases"*.

## Build from source

```bash
git clone --recursive https://github.com/BlackkCore/Glide.git
cd Glide/v2rayN/v2rayN.Desktop
dotnet publish -c Release -r win-x64 --self-contained true -o ../../out
```

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download). Put a `bin` folder with the cores next to `Glide.exe`, or let *Check update* download them.

## Credits and license

- Built on [v2rayN](https://github.com/2dust/v2rayN) by [2dust](https://github.com/2dust) and contributors. The networking is all theirs; Glide changes the desktop interface.
- Cores: [Xray](https://github.com/XTLS/Xray-core), [sing-box](https://github.com/SagerNet/sing-box), [mihomo](https://github.com/MetaCubeX/mihomo).
- Interface: [Avalonia](https://avaloniaui.net/) with [Semi.Avalonia](https://github.com/irihitech/Semi.Avalonia). Fonts: [Manrope](https://github.com/sharanda/manrope), [Vazirmatn](https://github.com/rastikerdar/vazirmatn) and [JetBrains Mono](https://github.com/JetBrains/JetBrainsMono), under the [SIL Open Font License](v2rayN/v2rayN.Desktop/Assets/Fonts/OFL.txt).
- Licensed under [GPL-3.0](LICENSE), like v2rayN.

Found a bug or have an idea? [Open an issue](https://github.com/BlackkCore/Glide/issues/new/choose).
