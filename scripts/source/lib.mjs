// Shared helpers for Glide's corresponding-source tooling (GPL-3.0 section 6, MPL-2.0 section 3.2).
// No dependencies: Node 20+, git, GNU tar (and `go` for vendoring). See docs/SOURCE_AND_LICENSES.md.
import { createHash } from 'node:crypto';
import { spawnSync } from 'node:child_process';
import { readFileSync } from 'node:fs';

export class SourceError extends Error {}
export const fail = (message) => { throw new SourceError(message); };

/** The cores Glide redistributes, as keyed in cores.lock.json. */
export const CORES = {
  xray: { name: 'Xray-core', archive: (v) => `xray-core-${v}-source.tar.gz` },
  singBox: { name: 'sing-box', archive: (v) => `sing-box-${v}-source.tar.gz` },
  mihomo: { name: 'mihomo', archive: (v) => `mihomo-${v}-source.tar.gz` },
};
export const glideArchive = (tag) => `Glide-source-${tag}.tar.gz`;
export const manifestName = (tag) => `SOURCES-${tag}.json`;
export const sumsName = (tag) => `SHA256SUMS-source-${tag}.txt`;
export const BINARY_ASSETS = ['Glide-windows-x64.zip', 'Glide-windows-x86.zip', 'Glide-setup-x64.exe', 'Glide-setup-x86.exe'];

export const readJson = (path) => {
  try { return JSON.parse(readFileSync(path, 'utf8')); } catch (e) { return fail(`cannot read ${path}: ${e.message}`); }
};
export const sha256 = (path) => createHash('sha256').update(readFileSync(path)).digest('hex');

export function run(cmd, args, opts = {}) {
  const r = spawnSync(cmd, args, { encoding: 'utf8', maxBuffer: 1 << 30, ...opts });
  if (r.error) fail(`${cmd} could not run: ${r.error.message}`);
  if (r.status !== 0) fail(`${cmd} ${args.join(' ')} failed (${r.status}): ${(r.stderr || '').trim().slice(0, 600)}`);
  return (r.stdout || '').trim();
}

/** Versions from the lock (x64 and x86 must agree: one source archive serves both) checked against the commit pins. */
export function loadPins(lockPath, pinsPath) {
  const lock = readJson(lockPath);
  const pins = readJson(pinsPath);
  const versions = {};
  const cores = {};
  for (const id of Object.keys(CORES)) {
    const v64 = lock.x64?.[id]?.version;
    const v86 = lock.x86?.[id]?.version;
    if (!v64 || v64 !== v86) fail(`${lockPath}: ${id} version missing or different for x64 (${v64}) and x86 (${v86})`);
    const pin = pins.cores?.[id];
    if (!pin) fail(`${pinsPath}: no pin for ${id}`);
    if (pin.tag !== `v${v64}`) fail(`${pinsPath}: ${id} is pinned at tag ${pin.tag} but ${lockPath} ships ${v64}; re-run --print-pins and review (a core was bumped without its source pin)`);
    if (!/^[0-9a-f]{40}$/.test(pin.commit ?? '')) fail(`${pinsPath}: ${id} has no 40-character commit`);
    if (!/^[A-Za-z0-9_.-]+\/[A-Za-z0-9_.-]+$/.test(pin.repo ?? '')) fail(`${pinsPath}: ${id} has no owner/repo`);
    versions[id] = v64;
    cores[id] = pin;
  }
  const cron = pins.libcronet;
  for (const k of ['cronetGoCommit', 'naiveproxyCommit', 'windowsAmd64GitBlob']) {
    if (!/^[0-9a-f]{40}$/.test(cron?.[k] ?? '')) fail(`${pinsPath}: libcronet.${k} must be a 40-character id`);
  }
  return { lock, pins, versions, cores };
}

/** Every file a release must carry for the pinned cores. */
export function expectedSourceAssets(tag, versions) {
  return [glideArchive(tag), ...Object.keys(CORES).map((id) => CORES[id].archive(versions[id])), manifestName(tag), sumsName(tag)];
}

/** Names missing from a release's asset list; binaries without all their sources are the failure this guards. */
export function missingAssets(tag, versions, names, requireBinaries = false) {
  const have = new Set(names);
  const need = expectedSourceAssets(tag, versions);
  const hasBinary = BINARY_ASSETS.some((b) => have.has(b));
  const missing = need.filter((n) => !have.has(n));
  if (requireBinaries) missing.push(...BINARY_ASSETS.filter((n) => !have.has(n)));
  // Binaries present without a complete set of sources is always an error, even when binaries aren't required.
  return hasBinary || requireBinaries ? missing : [];
}
