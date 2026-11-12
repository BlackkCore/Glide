# Source code and licences

Glide is licensed under the GNU GPL v3 ([LICENSE](../LICENSE)). It is built on [v2rayN](https://github.com/2dust/v2rayN) by 2dust and contributors ([NOTICE.md](../NOTICE.md)); changes to files that come from v2rayN are marked with `// Glide` comments in the code. Every release zip and installer carries the licence texts in `Licenses\`, and [THIRD_PARTY_NOTICES.md](../THIRD_PARTY_NOTICES.md) lists the third-party components with their versions and licences.

## What a release contains

| Component | Licence | Source |
|---|---|---|
| Glide (derived from v2rayN) | GPL-3.0 | `Glide-source-<tag>.tar.gz` on the release page |
| sing-box, mihomo (with cronet-go inside sing-box) | GPL-3.0 (sing-box adds a term about the project name) | one archive each, with `vendor/` |
| Xray-core | MPL-2.0 | one archive, with `vendor/` |
| GlobalHotKeys | WTFPL v2 | in the Glide source archive |
| `libcronet.dll`, Wintun | BSD-3-Clause; WireGuard Prebuilt Binaries License | licence text ships; origin recorded in `SOURCES-<tag>.json` |
| .NET packages, fonts | MIT, BSD, Apache-2.0, SIL OFL and others | listed in THIRD_PARTY_NOTICES.md |

The proxy engines run as separate programs that Glide starts and talks to over local ports and files. The exact engine versions and SHA-256 values are pinned in [cores.lock.json](../cores.lock.json), and their upstream source commits in [cores.source.json](../cores.source.json). The IP-to-country data files are not in the release packages; Glide downloads them when you ask (see THIRD_PARTY_NOTICES.md).

## Source for each release

Every release carries the source for what it ships, uploaded before the binaries and checked by the release workflow before the release becomes public:

| Asset | Content |
|---|---|
| `Glide-source-<tag>.tar.gz` | Glide's source at the release commit, the licences and notices, the lock files, the installer script, the workflows and the scripts in `scripts/` |
| `xray-core-<ver>-source.tar.gz`, `sing-box-<ver>-source.tar.gz`, `mihomo-<ver>-source.tar.gz` | the upstream repository at the pinned commit, its licence, and a `vendor/` folder with every Go module it builds with |
| `SOURCES-<tag>.json` | for each archive: repository, tag, commit, licence, version and the SHA-256 of the matching binary |
| `SHA256SUMS-source-<tag>.txt` | SHA-256 of every file above |

Each file has a GPG signature (`.sig`); the key's fingerprint is in the [README](../README.md#verify-your-download). To rebuild Glide, see *Build from source* in the README and [CONTRIBUTING.md](../CONTRIBUTING.md).
