# Third-party notices

Glide is a derivative of [v2rayN](https://github.com/2dust/v2rayN) (GPL-3.0; project author per its build metadata: 2dust) and is itself distributed under the GNU General Public License v3.0 (see `LICENSE`, and `NOTICE.md` for Glide's own attribution). Everything below is shipped with or inside Glide releases. Where a licence file exists it is in this release's `Licenses\` folder; the source of each project is at the address given.

This list was generated from the project's resolved packages and the pinned release assets, not written from memory.

## Application and bundled files

| Component | Licence | Notes |
|---|---|---|
| v2rayN (upstream of Glide) | GPL-3.0 | Source: https://github.com/2dust/v2rayN. Upstream copyright notices must stay in the source files. |
| GlobalHotKeys (`v2rayN/GlobalHotKeys`) | WTFPL v2 | Copyright (C) 2004 Sam Hocevar (the licence text's own notice). Upstream: https://github.com/2dust/GlobalHotKeys. `Licenses\GlobalHotKeys-WTFPL.txt` |
| Manrope font | SIL OFL 1.1 | Copyright 2019 The Manrope Project Authors. `Licenses\Manrope-OFL.txt` |
| JetBrains Mono font | SIL OFL 1.1 | Copyright 2020 The JetBrains Mono Project Authors. `Licenses\JetBrainsMono-OFL.txt` |
| Vazirmatn font | SIL OFL 1.1 | Copyright 2015 The Vazirmatn Project Authors. `Licenses\Vazirmatn-OFL.txt` |
| Noto Sans SC font | SIL OFL 1.1 | Copyright (c) 2014-2021 Adobe (http://www.adobe.com/), with Reserved Font Name 'Source' (as recorded inside the font file). `Licenses\NotoSansSC-OFL.txt` |
| `EnableLoopback.exe` | **Not shipped** (since 2026-11-12) | The Progress Software "EnableLoopback" utility has no redistribution grant, so Glide no longer includes it. "Allow Store apps to use the proxy" uses Windows' own `CheckNetIsolation` through a built-in screen. |

## Proxy cores (release zips and installers)

Pinned in `cores.lock.json` (versions and SHA-256) and `cores.source.json` (the exact upstream source commits). Licence texts are in `Licenses\cores\`.

| Core | Version | Licence | Source commit (corresponding source) |
|---|---|---|---|
| Xray-core | 26.10.31 | MPL-2.0 | https://github.com/XTLS/Xray-core, tag v26.10.31, `a4a3dff5b20c2faae567b742c172c7b95e75c083` |
| sing-box | 1.14.3 | GPL-3.0-or-later, with the upstream's extra term that derivative works may not use the sing-box name | https://github.com/SagerNet/sing-box, tag v1.14.3, `7054cac5124465ffdb145870d7c01885566711b1` (the commit its signed build attestation names) |
| mihomo | 1.19.29 | GPL-3.0 | https://github.com/MetaCubeX/mihomo, tag v1.19.29, `e26714a181ac0e2fa803453c0a8e9a9ce94e31cb` |
| Wintun (`wintun.dll`) | as shipped in the Xray zip | WireGuard "Prebuilt Binaries License" (redistribution allowed alongside software that uses it only through its public API; no source obligation) | https://www.wintun.net/ , file `Wintun-prebuilt-binaries.txt` |
| `libcronet.dll` (windows-amd64, shipped with sing-box) | cronet-go `0d28acc44093df24b2526dea3d6ffefd6b0a54f0` | BSD-3-Clause (Chromium / naiveproxy); the Go bindings that load it, SagerNet/cronet-go, are GPL-3.0 and are part of the sing-box source archive | Prebuilt in https://github.com/SagerNet/cronet-go (git blob `91268f1f6fbacf1f5a684428a3ff5b77a7f69527`, identical to the file in the sing-box zip), from SagerNet/naiveproxy `72a06c9fca0e2d228588c7f3074bf7efff3ff686`. Licence text `Licenses\cores\libcronet-Chromium-BSD-3-Clause.txt`. Chromium bundles further components with their own notices (Chromium's credits list); those are not reproduced here |

### Where the source is

Each release page carries the corresponding source next to the binaries (`Licenses\SOURCE.txt` in every zip and installer says the same):

- `Glide-source-<tag>.tar.gz`: Glide itself, with its build scripts, workflows and dependency lists, at the release commit.
- `xray-core-<version>-source.tar.gz`, `sing-box-<version>-source.tar.gz`, `mihomo-<version>-source.tar.gz`: each core at the commit above, with all its Go dependencies in `vendor/`, and the upstream build files (`.github/`, `release/`, `Makefile`) that show how the release binaries are made.
- `SOURCES-<tag>.json` and `SHA256SUMS-source-<tag>.txt`: the exact origin and SHA-256 of every archive.

How the source for each release is published is described in `docs/SOURCE_AND_LICENSES.md`. The cores are separate programs started by Glide.

## Data files

Taken unmodified from `2dust/v2rayN-core-bin` (pinned in `cores.lock.json`, bundle SHA-256 checked in CI). Only `geosite.dat` is included in the release packages; Glide downloads the other files from the locations below when the user asks (the first-run message, or Update all). They are data read by the cores, not programs.

**MaxMind attribution.** This product includes GeoLite Data created by MaxMind, available from https://www.maxmind.com. The IP-to-country data in `geoip.dat`, `geoip-only-cn-private.dat`, `Country.mmdb`, and `geoip.metadb` is built from MaxMind's GeoLite2 Country data (the GeoLite End User License Agreement, https://www.maxmind.com/en/geolite2/eula, applies to it: Creative Commons Attribution-ShareAlike 4.0 for the copyrightable elements plus MaxMind's additional terms). Glide's release packages do not include these files; Glide downloads them from the upstream locations listed below when the user asks.

**v2fly/domain-list-community** (the domain lists behind `geosite.dat`), MIT licence: Copyright (c) 2018-2019 V2Ray. Permission is granted, free of charge, to use, copy, modify, merge, publish, distribute, sublicense and sell copies of the data, subject to this notice being included in all copies or substantial portions of it. The data is provided "as is", without warranty of any kind.

| Files | Upstream | Licence | Matched upstream release | Status |
|---|---|---|---|---|
| `geosite.dat` | https://github.com/Loyalsoldier/v2ray-rules-dat (built from v2fly/domain-list-community, MIT, and Loyalsoldier's own lists) | GPL-3.0 (repository LICENSE) | release 202611082203, byte-identical | included in release packages; no MaxMind data |
| `geoip.dat` | https://github.com/Loyalsoldier/v2ray-rules-dat (built from MaxMind GeoLite2 Country CSV) | GPL-3.0 (repository LICENSE) plus MaxMind GeoLite terms | releases 202611052203 to 202611102204, byte-identical | downloaded on request; MaxMind terms apply |
| `Country.mmdb`, `geoip-only-cn-private.dat` | https://github.com/Loyalsoldier/geoip (built from MaxMind GeoLite2 Country CSV) | CC-BY-SA-4.0 (repository LICENSE) plus MaxMind GeoLite terms | not matched (files sit on a branch, not in a release) | downloaded on request; MaxMind terms apply |
| `geoip.metadb` | https://github.com/MetaCubeX/meta-rules-dat (credits Loyalsoldier/geoip and v2fly) | GPL-3.0 (repository LICENSE) plus MaxMind GeoLite terms | not matched (rolling "latest" release) | downloaded on request; MaxMind terms apply |
| `srss\*.srs` | https://github.com/2dust/sing-box-rules, a fork of lyc8503/sing-box-rules whose README says it is ported from Loyalsoldier/v2ray-rules-dat | **no licence file in either repository**; derived from GPL-3.0 and GeoLite2 data | not matched | not included in release packages (the repositories that publish them have no licence file); sing-box downloads a rule set on demand |

## Certificate root data

`Sample/chrome_roots_pem` and `Sample/mozilla_roots_pem` are compiled into Glide. They are built from two public sources:

- **Common CA Database (CCADB)**, the roots Mozilla includes (https://ccadb.my.salesforce-sites.com/mozilla/IncludedRootsPEMTxt?TrustBitsInclude=Websites), licensed under the Community Data License Agreement – Permissive, Version 2.0 (https://cdla.dev/permissive-2-0/). The names and trademarks of CCADB contributors may not be used to endorse or promote products derived from this data without prior written permission.
- **Chromium Root Store** (https://chromium.googlesource.com/chromium/src/+/main/net/data/ssl/chrome_root_store/root_store.certs), Copyright (c) The Chromium Authors, BSD 3-Clause License:

Redistribution and use in source and binary forms, with or without modification, are permitted provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer.
2. Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following disclaimer in the documentation and/or other materials provided with the distribution.
3. Neither the name of the copyright holder nor the names of its contributors may be used to endorse or promote products derived from this software without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

## .NET packages

| Package | Version | Licence | Authors | Use |
|---|---|---|---|---|
| Avalonia | 12.1.6 | MIT | Avalonia Team | transitive |
| Avalonia.Angle.Windows.Natives | 2.1.27548.20260419 | BSD-3-Clause (ANGLE), `Licenses/third-party/ANGLE-BSD-3-Clause.txt` | Avalonia Team | transitive |
| Avalonia.AvaloniaEdit | 12.0.0 | MIT | Avalonia Team | direct |
| Avalonia.BuildServices | 11.3.2 | MIT | Avalonia Team | transitive |
| Avalonia.Controls.DataGrid | 12.1.0 | MIT | Avalonia Team | transitive |
| Avalonia.Desktop | 12.1.6 | MIT | Avalonia Team | direct |
| Avalonia.FreeDesktop | 12.1.6 | MIT | Avalonia Team | transitive |
| Avalonia.FreeDesktop.AtSpi | 12.1.6 | MIT | Avalonia Team | transitive |
| Avalonia.HarfBuzz | 12.1.6 | MIT | Avalonia Team | transitive |
| Avalonia.Native | 12.1.6 | MIT | Avalonia Team | transitive |
| Avalonia.Remote.Protocol | 12.1.6 | MIT | Avalonia Team | transitive |
| Avalonia.Skia | 12.1.6 | MIT | Avalonia Team | transitive |
| Avalonia.Win32 | 12.1.6 | MIT | Avalonia Team | transitive |
| Avalonia.X11 | 12.1.6 | MIT | Avalonia Team | transitive |
| AvaloniaUI.DiagnosticsSupport | 2.2.3 | none declared (not shipped) | AvaloniaUI OÜ | Debug builds only, not shipped |
| CliWrap | 3.10.5 | MIT | Tyrrrz | transitive |
| DialogHost.Avalonia | 0.12.3 | MIT | SKProCH | direct |
| Downloader | 6.0.1 | MIT | Behzad Khosravifar | transitive |
| HarfBuzzSharp | 8.3.1.3 | MIT | Microsoft | transitive |
| HarfBuzzSharp.NativeAssets.Linux | 8.3.1.5 | MIT | Microsoft | transitive |
| HarfBuzzSharp.NativeAssets.macOS | 8.3.1.3 | MIT | Microsoft | transitive |
| HarfBuzzSharp.NativeAssets.WebAssembly | 8.3.1.3 | MIT | Microsoft | transitive |
| HarfBuzzSharp.NativeAssets.Win32 | 8.3.1.3 | MIT | Microsoft | transitive |
| IPNetwork2 | 5.0.1 | BSD-2-Clause | Luc Dvchosal | transitive |
| Irihi.Avalonia.Shared | 0.5.0 | MIT | IRIHI Technology Co., Ltd. | transitive |
| MicroCom.Runtime | 0.11.6 | MIT | MicroCom.Runtime | transitive |
| Microsoft.Extensions.DependencyInjection.Abstractions | 10.0.12 | MIT | Microsoft | transitive |
| Microsoft.Extensions.Logging.Abstractions | 10.0.12 | MIT | Microsoft | transitive |
| Microsoft.IO.RecyclableMemoryStream | 3.0.1 | MIT | Microsoft | transitive |
| Microsoft.Win32.SystemEvents | 6.0.0 | MIT | Microsoft | transitive |
| NLog | 6.2.2 | BSD-3-Clause | Jarek Kowalski,Kim Christensen,Julian Verdurmen | transitive |
| ProDataGrid | 12.1.5 | MIT | Wiesław Šoltés | direct |
| ProDataGrid.FormulaEngine | 12.1.5 | MIT | Wiesław Šoltés | transitive |
| ProDataGrid.FormulaEngine.Excel | 12.1.5 | MIT | Wiesław Šoltés | transitive |
| QRCoder | 1.8.0 | MIT | Raffael Herrmann, Shane Krueger | transitive |
| ReactiveUI | 26.0.1 | MIT | .NET Foundation and Contributors | direct |
| ReactiveUI.Avalonia | 12.1.6 | MIT | ReactiveUI and Avalonia Teams, and Contributors | direct |
| ReactiveUI.Binding | 9.1.0 | MIT | Glenn Watson | transitive |
| ReactiveUI.Binding.Avalonia | 9.1.0 | MIT | Glenn Watson | transitive |
| ReactiveUI.Core | 26.0.1 | MIT | .NET Foundation and Contributors | transitive |
| ReactiveUI.Disposables | 9.0.0 | MIT | ReactiveUI Association Inc | transitive |
| ReactiveUI.Primitives | 9.0.0 | MIT | ReactiveUI Association Inc | transitive |
| ReactiveUI.Primitives.Avalonia | 9.0.0 | MIT | ReactiveUI Association Inc | transitive |
| ReactiveUI.Primitives.Core | 9.0.0 | MIT | ReactiveUI Association Inc | transitive |
| ReactiveUI.SourceGenerators | 4.2.0 | MIT | .NET Foundation and Contributors | transitive |
| SQLitePCLRaw.lib.e_sqlite3 | 3.53.3 | SQLite is Public Domain (package licence text) | Eric Sink (SourceGear) | direct; native `e_sqlite3.dll` for win-x64, win-x86, win-arm64; replaced the unofficial Repobot build (R-25) |
| Semi.Avalonia | 12.1.0.1 | MIT | IRIHI Technology Co., Ltd. | direct |
| Semi.Avalonia.DataGrid | 12.1.0.1 | MIT | IRIHI Technology Co., Ltd. | direct |
| SkiaSharp | 3.119.4 | MIT | Microsoft | transitive |
| SkiaSharp.NativeAssets.Linux | 3.119.4 | MIT | Microsoft | direct |
| SkiaSharp.NativeAssets.macOS | 3.119.4 | MIT | Microsoft | transitive |
| SkiaSharp.NativeAssets.WebAssembly | 3.119.4 | MIT | Microsoft | transitive |
| SkiaSharp.NativeAssets.Win32 | 3.119.4 | MIT | Microsoft | transitive |
| Splat | 21.0.0 | MIT | .NET Foundation and Contributors | transitive |
| Splat.Builder | 21.0.0 | MIT | .NET Foundation and Contributors | transitive |
| Splat.Core | 21.0.0 | MIT | .NET Foundation and Contributors | transitive |
| Splat.Logging | 21.0.0 | MIT | .NET Foundation and Contributors | transitive |
| sqlite-net-e | 1.11.285 | MIT, `Licenses/third-party/sqlite-net-MIT.txt` | SQLite-net | transitive |
| SQLitePCLRaw.config.e_sqlite3 | 3.0.3 | Apache-2.0 | Eric Sink | transitive |
| SQLitePCLRaw.core | 3.0.3 | Apache-2.0 | Eric Sink | transitive |
| SQLitePCLRaw.provider.e_sqlite3 | 3.0.3 | Apache-2.0 | Eric Sink | transitive |
| System.Drawing.Common | 6.0.0 | MIT | Microsoft | transitive |
| Tmds.DBus.Protocol | 0.94.1 | MIT | Tom Deseyn | transitive |
| WebDav.Client | 2.9.0 | MIT | skazantsev | transitive |
| YamlDotNet | 18.1.0 | MIT | Antoine Aubry | transitive |
| ZXing.Net | 0.16.11 | Apache-2.0 | Michael Jahn | transitive |
| ZXing.Net.Bindings.SkiaSharp | 0.16.22 | Apache-2.0 | Michael Jahn | transitive |

Packages whose licence is a file ship that file under `Licenses/third-party/` (ANGLE, sqlite-net); the others are MIT, Apache-2.0, BSD or Unlicense as listed.
