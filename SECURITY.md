# Security policy

## Reporting a vulnerability
Please do not report security problems in a public issue. Use GitHub's private reporting: open the repository's **Security** tab and choose **Report a vulnerability**. Include the Glide version (Settings, About), your Windows version, what you did, and what you expected.

If private reporting is not available on the repository, open an issue that says only that you have a security report and ask for a private way to send it; do not put details in it.

## What is covered
Glide's own code and packaging: the installer, the in-app updater, configuration import, and how Glide starts and manages the proxy engines. Problems in Xray, sing-box or mihomo themselves belong to those projects.

## How releases protect you
- Every release file has a GPG signature (`.sig`) made with the key "BlackCore Glide Release" (Ed25519, fingerprint `218B 01F2 5642 A7A3 91BF  CC86 7D63 C285 D104 63CB`, expires 11 November 2028); the public key is on each release and the fingerprint is in the README. The release workflow signs only with that exact key and its final check rejects a release signed by any other key. The key is protected by a passphrase that exists only as a secret of the protected `release` environment.
- The signing key of the withdrawn 1.0.0 release (fingerprint `D62D C3BC D066 5F52 5BB6  7206 E862 9FD9 AF46 1E53`) is retired. Do not trust a Glide file signed with it.
- The in-app updater installs only zip files signed with the project's update key (ECDSA P-256) and checks the signature again in the updater before extracting.
- The installer puts Glide in `Program Files` and keeps settings in `%LOCALAPPDATA%\Glide`, so ordinary programs cannot replace the executables that may run with administrator rights.
- The proxy engines bundled in each release are pinned to exact versions and checked against SHA-256 values when the release is built; each release carries the matching source code.
- Engine updates (Xray, sing-box, mihomo), installed or portable, install only builds listed in a **signed engine list**. The list is signed with the same release key as app updates, expires, and cannot be replaced by an older one. A download must match its signed size and SHA-256, each installed file must match its own signed SHA-256, installation goes through a staging folder, and the previous engine is kept. If the list is missing, expired or invalid, the engines simply stay as they are. Installed, the engines live in `Program Files`: a short-lived administrator process (one Windows permission prompt; Glide itself does not run as administrator) opens the download and the list once with writes denied, verifies the list against the embedded release key again, picks the build itself, and installs only a zip that matches it; the replay floor is kept in the protected folder. A Glide update never replaces an engine that is already installed.

## Known limitations
- Glide's executables and installers are not yet signed with a Windows code-signing certificate, so Windows SmartScreen may warn.
- Portable copies keep their settings beside the program and are as protected as the folder you put them in.
