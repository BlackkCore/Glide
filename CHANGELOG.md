# Changelog

All notable changes to Glide. The format follows [Keep a Changelog](https://keepachangelog.com/); versions follow the release tags. Glide is built on [v2rayN](https://github.com/2dust/v2rayN); upstream's own changes are merged in and are not listed here.

## [1.1.0] - Unreleased

Copies of 1.0.0 that are already installed check this repository for updates and will be offered this release.

### Security
- The installer now installs for all users into `Program Files`, where ordinary programs cannot change the executables that may run with administrator rights. Settings and downloaded data live in `%LOCALAPPDATA%\Glide`. A folder outside `Program Files` is refused.
- In-app updates are accepted only when the zip carries a valid ECDSA P-256 signature (checked by Glide, and again by the updater on the locked file before extracting).
- Engine updates in the portable version install only builds listed in a signed engine list (ECDSA, expiring, replay-protected). A download is checked against its signed size and SHA-256 before anything is replaced, each installed file is checked too, and the previous engine is kept.
- The proxy engines in each release are pinned to exact versions and checked against SHA-256 values when the release is built; the sing-box build is verified against its signed provenance; configuration import no longer copies executables.
- Release packaging is a draft until every check passes; the release gate verifies GPG signatures, update signatures, contents and that the tag points at the build commit.

### Added
- A built-in "Allow Store apps to use the proxy" screen that uses Windows' own `CheckNetIsolation` (English and Persian).
- Corresponding source for Glide and the bundled engines, with a manifest and SHA-256 list, on every release.
- A first-run message that explains the routing data download (about 45 MB, once) and offers it before the first connection.
- MaxMind attribution in the About screen and the documentation.

### Changed
- Xray, sing-box and mihomo have no pre-release update option in the portable version (nothing signed can exist for it).
- The language selector offers English and فارسی. A language saved by an earlier version falls back to English for display and is not overwritten.
- The IP-to-country data files (`geoip.dat`, `Country.mmdb`, `geoip-only-cn-private.dat`, `geoip.metadb`) are no longer bundled; Glide downloads them in the app. `geosite.dat` is still included.
- A failed download that is really a name-lookup (DNS) problem now says so, naming the host.
- The copyright line credits 2dust and contributors together with Glide's changes.

### Fixed
- Installed Glide kept its data in `%LOCALAPPDATA%\v2rayN` (the original v2rayN's folder) when `Program Files` was not writable, and created a stray `guiTemps` folder in `Program Files` as administrator.
- In-app updates extracted nothing when the zip contained the updater itself.
- Update checks for Xray, mihomo and sing-box reported "StatusCode error: OK".
- Screen readers had no name for two icon-only subscription buttons.

### Removed
- `EnableLoopback.exe` (third-party, no redistribution permission) and the 15 bundled sing-box rule-set files (no licence); sing-box downloads the rule sets it needs.
- Unused Linux and macOS icon files from the Windows packages.

## [1.0.0] - 2026-11-04

First public release: a redesigned Windows interface for v2rayN with the Xray, sing-box and mihomo engines, a verified connection status, guided troubleshooting, routing in plain words, subscription status, and an English and Persian (right-to-left) interface. Installer for the current user, portable zip, 32-bit and 64-bit.
