// Offline self-tests for the source tooling, run with `node --test scripts/source/*.test.mjs` (needs git and GNU tar).
// Builds fake upstream repositories in a temp folder; nothing touches the network or any real repository.
import test from 'node:test';
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { mkdtempSync, mkdirSync, writeFileSync, readFileSync, rmSync, appendFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, dirname } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { missingAssets, expectedSourceAssets, CORES, glideArchive, sha256 } from './lib.mjs';
import { verify } from './verify-source-bundle.mjs';
import { noticeText } from './write-source-notice.mjs';

const HERE = dirname(fileURLToPath(import.meta.url));
const MAKE = join(HERE, 'make-source-bundle.mjs');
const TAG = '9.9.9';

const git = (cwd, ...args) => {
  const r = spawnSync('git', ['-c', 'user.name=t', '-c', 'user.email=t@example.com', '-c', 'commit.gpgsign=false', ...args], { cwd, encoding: 'utf8' });
  assert.equal(r.status, 0, `git ${args.join(' ')}: ${r.stderr}`);
  return r.stdout.trim();
};
const put = (dir, rel, text) => { mkdirSync(dirname(join(dir, rel)), { recursive: true }); writeFileSync(join(dir, rel), text); };

/** A fake world: three upstream core repos, a Glide repo whose lock and pins point at them, and the env that redirects GitHub. */
function world({ version = '1.0.0' } = {}) {
  const root = mkdtempSync(join(tmpdir(), 'glide-srctest-'));
  const up = join(root, 'up');
  const cores = {};
  const slugs = { xray: 'fake/xray', singBox: 'fake/sing-box', mihomo: 'fake/mihomo' };
  for (const id of Object.keys(CORES)) {
    const dir = join(up, `${slugs[id]}.git`);
    mkdirSync(dir, { recursive: true });
    git(dir, 'init', '-q');
    put(dir, 'go.mod', `module example.com/${id}\n\ngo 1.20\n`);
    put(dir, 'go.sum', '');
    put(dir, 'LICENSE', 'licence text\n');
    put(dir, 'main.go', 'package main\n');
    if (id === 'singBox') put(dir, '.github/CRONET_GO_VERSION', `${'c'.repeat(40)}\n`);
    git(dir, 'add', '-A');
    git(dir, 'commit', '-q', '-m', 'release');
    git(dir, 'tag', '-a', '-m', 'release', `v${version}`);
    cores[id] = { dir, commit: git(dir, 'rev-parse', `v${version}^{commit}`) };
  }
  const glide = join(root, 'glide');
  mkdirSync(glide);
  git(glide, 'init', '-q');
  const lockEntry = (id) => ({ version, sha256: 'a'.repeat(64), id });
  const lock = { coreBin: { commit: 'b'.repeat(40), sha256: 'b'.repeat(64) }, x64: {}, x86: {} };
  for (const id of Object.keys(CORES)) { lock.x64[id] = lockEntry(id); lock.x86[id] = lockEntry(id); }
  const pins = {
    cores: Object.fromEntries(Object.keys(CORES).map((id) => [id, { name: CORES[id].name, repo: slugs[id], tag: `v${version}`, commit: cores[id].commit, license: 'GPL-3.0' }])),
    libcronet: { license: 'BSD-3-Clause', cronetGoCommit: 'c'.repeat(40), naiveproxyCommit: 'd'.repeat(40), windowsAmd64GitBlob: 'e'.repeat(40) },
  };
  put(glide, 'cores.lock.json', JSON.stringify(lock, null, 2));
  put(glide, 'cores.source.json', JSON.stringify(pins, null, 2));
  for (const f of ['LICENSE', 'NOTICE.md', 'THIRD_PARTY_NOTICES.md', 'global.json', 'v2rayN/Directory.Packages.props', 'installer/glide.iss', '.github/workflows/package-zip.yml', '.github/workflows/package-source.yml', 'scripts/source/make-source-bundle.mjs', 'licenses/cores/mihomo-GPL-3.0.txt', 'licenses/cores/sing-box-GPL-3.0-with-name-clause.txt']) put(glide, f, `${f}\n`);
  git(glide, 'add', '-A');
  git(glide, 'commit', '-q', '-m', 'glide');
  const commit = git(glide, 'rev-parse', 'HEAD');
  const env = { ...process.env, GLIDE_SOURCE_BASE_URL: pathToFileURL(up).href };
  return { root, glide, commit, cores, env, out: join(root, 'out'), cleanup: () => rmSync(root, { recursive: true, force: true }) };
}

const make = (w, ...extra) => spawnSync(process.execPath, [MAKE, '--root', w.glide, '--out', w.out, '--tag', TAG, '--commit', w.commit, '--repo', 'owner/glide', ...extra], { env: w.env, encoding: 'utf8' });
const check = (w, release = false) => verify({ dir: w.out, tag: TAG, commit: w.commit, release, root: w.glide });

test('builds, names and verifies a bundle (no vendoring)', () => {
  const w = world();
  try {
    const r = make(w, '--no-vendor');
    assert.equal(r.status, 0, r.stderr);
    assert.deepEqual(check(w), []);
    const sums = readFileSync(join(w.out, `SHA256SUMS-source-${TAG}.txt`), 'utf8');
    for (const name of expectedSourceAssets(TAG, { xray: '1.0.0', singBox: '1.0.0', mihomo: '1.0.0' }).filter((n) => !n.startsWith('SHA256SUMS'))) assert.match(sums, new RegExp(`^[0-9a-f]{64}  ${name.replace(/\./g, '\\.')}$`, 'm'));
    const manifest = JSON.parse(readFileSync(join(w.out, `SOURCES-${TAG}.json`), 'utf8'));
    assert.equal(manifest.cores.find((c) => c.id === 'singBox').commit, w.cores.singBox.commit);
    assert.equal(manifest.components.find((c) => c.id === 'EnableLoopback.exe'), undefined, 'EnableLoopback.exe is no longer shipped');
  } finally { w.cleanup(); }
});

test('archives are reproducible', () => {
  const a = world();
  try {
    make(a, '--no-vendor');
    const first = Object.fromEntries([glideArchive(TAG), ...Object.values(CORES).map((c) => c.archive('1.0.0'))].map((n) => [n, sha256(join(a.out, n))]));
    rmSync(a.out, { recursive: true });
    make(a, '--no-vendor');
    for (const [n, h] of Object.entries(first)) assert.equal(sha256(join(a.out, n)), h, n);
  } finally { a.cleanup(); }
});

test('a release refuses to skip the dependency source', () => {
  const w = world();
  try {
    const r = make(w, '--release', '--no-vendor');
    assert.notEqual(r.status, 0);
    assert.match(r.stderr, /vendored dependency source/);
    make(w, '--no-vendor');
    assert.ok(check(w, true).some((p) => /vendored dependency source/.test(p)));
  } finally { w.cleanup(); }
});

test('a tag that moved since it was pinned is rejected', () => {
  const w = world();
  try {
    const dir = w.cores.singBox.dir;
    put(dir, 'extra.go', 'package main\n');
    git(dir, 'add', '-A');
    git(dir, 'commit', '-q', '-m', 'late change');
    git(dir, 'tag', '-f', '-a', '-m', 'moved', 'v1.0.0');
    const r = make(w, '--no-vendor');
    assert.notEqual(r.status, 0);
    assert.match(r.stderr, /now points to .* not the pinned/);
  } finally { w.cleanup(); }
});

test('a lock bump without its source pin is rejected', () => {
  const w = world();
  try {
    const lockPath = join(w.glide, 'cores.lock.json');
    const lock = JSON.parse(readFileSync(lockPath, 'utf8'));
    lock.x64.mihomo.version = '1.0.1';
    lock.x86.mihomo.version = '1.0.1';
    writeFileSync(lockPath, JSON.stringify(lock));
    const r = make(w, '--no-vendor');
    assert.notEqual(r.status, 0);
    assert.match(r.stderr, /mihomo is pinned at tag v1\.0\.0 but .* ships 1\.0\.1/);
  } finally { w.cleanup(); }
});

test('x64 and x86 must ship the same core version', () => {
  const w = world();
  try {
    const lockPath = join(w.glide, 'cores.lock.json');
    const lock = JSON.parse(readFileSync(lockPath, 'utf8'));
    lock.x86.xray.version = '0.9.0';
    writeFileSync(lockPath, JSON.stringify(lock));
    assert.match(make(w, '--no-vendor').stderr, /xray version missing or different/);
  } finally { w.cleanup(); }
});

test('sing-box built against another cronet-go than the pinned one is rejected', () => {
  const w = world();
  try {
    const pinsPath = join(w.glide, 'cores.source.json');
    const pins = JSON.parse(readFileSync(pinsPath, 'utf8'));
    pins.libcronet.cronetGoCommit = '1'.repeat(40);
    writeFileSync(pinsPath, JSON.stringify(pins));
    assert.match(make(w, '--no-vendor').stderr, /builds libcronet from cronet-go/);
  } finally { w.cleanup(); }
});

test('a missing archive or a tampered one fails verification', () => {
  const w = world();
  try {
    make(w, '--no-vendor');
    appendFileSync(join(w.out, CORES.mihomo.archive('1.0.0')), 'x');
    assert.ok(check(w).some((p) => /mihomo-1\.0\.0-source\.tar\.gz: checksum does not match/.test(p)));
    rmSync(join(w.out, CORES.xray.archive('1.0.0')));
    assert.ok(check(w).some((p) => p === `missing ${CORES.xray.archive('1.0.0')}`));
  } finally { w.cleanup(); }
});

test('verification names the wrong Glide commit', () => {
  const w = world();
  try {
    make(w, '--no-vendor');
    assert.ok(verify({ dir: w.out, tag: TAG, commit: 'f'.repeat(40), root: w.glide }).some((p) => /expected/.test(p)));
  } finally { w.cleanup(); }
});

test('release asset gate: binaries need every source asset', () => {
  const v = { xray: '1', singBox: '2', mihomo: '3' };
  const sources = expectedSourceAssets('1.2.3', v);
  assert.deepEqual(missingAssets('1.2.3', v, ['notes.txt']), []);
  assert.deepEqual(missingAssets('1.2.3', v, [...sources, 'Glide-windows-x64.zip']), []);
  assert.deepEqual(missingAssets('1.2.3', v, ['Glide-windows-x64.zip']), sources);
  assert.deepEqual(missingAssets('1.2.3', v, sources.filter((n) => !n.startsWith('sing-box')).concat('Glide-windows-x64.zip')), ['sing-box-2-source.tar.gz']);
  assert.ok(missingAssets('1.2.3', v, sources, true).includes('Glide-setup-x86.exe'));
});

test('the shipped notice names the release, the commit and every archive', () => {
  const v = { xray: '1', singBox: '2', mihomo: '3' };
  const cores = Object.fromEntries(Object.keys(CORES).map((id) => [id, { tag: `v${v[id]}`, commit: id[0].repeat(40), license: 'GPL-3.0' }]));
  const text = noticeText({ tag: '1.2.3', commit: 'a'.repeat(40), repo: 'owner/glide', versions: v, cores });
  for (const s of ['releases/tag/1.2.3', 'a'.repeat(40), 'Glide-source-1.2.3.tar.gz', 'sing-box-2-source.tar.gz', 'xray-core-1-source.tar.gz', 'mihomo-3-source.tar.gz', 'SOURCES-1.2.3.json']) assert.ok(text.includes(s), s);
});
