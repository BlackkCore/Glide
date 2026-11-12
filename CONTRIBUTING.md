# Contributing to Glide

Thank you for helping. Glide is a desktop interface built on [v2rayN](https://github.com/2dust/v2rayN) (GPL-3.0); contributions are accepted under the same licence.

## Before you start
- Bugs and ideas: open an issue using the templates. For problems inside the proxy engines (Xray, sing-box, mihomo) or protocol questions, the upstream projects are usually the better place.
- Security problems: do not open a public issue; see [SECURITY.md](SECURITY.md).

## Building and testing
```bash
git clone https://github.com/BlackkCore/Glide.git
cd Glide/v2rayN
dotnet build v2rayN.slnx -c Release
dotnet test --project ./ServiceLib.Tests -c Release
node --test ../scripts/source/*.test.mjs
```
You need the .NET SDK pinned in `global.json` and Node.js 20 or newer for the source tooling tests. A change is ready for review when the build has no errors and every test passes.

## What a good change looks like
- Small and focused: one problem, the smallest fix that solves it, a regression test next to it.
- Say what you ran. "The Release build has no errors and the full test suite passes" is useful; "should work" is not. If you could not run something (a real VPN or TUN test, an installer), say so.
- Do not rename internal namespaces, configuration keys or migration paths: they keep existing settings and upstream merges working.
- Translations and resource files (`*.resx`) are maintained separately; please open an issue before changing them.
- Do not add third-party code or data without its licence; list it in `THIRD_PARTY_NOTICES.md`.

## Release and signing
Releases are built by the GitHub Actions workflows and signed with project-held keys. Never commit keys, tokens or personal proxy settings.
