// node --test scripts/cores/*.test.mjs   (a glob: a bare directory fails on some runners' Node)
// The engine-index generator, tested offline: synthetic release folders, a stub `gh`, throwaway keys.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, writeFileSync, readFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { createHash, generateKeyPairSync } from 'node:crypto';
import { deflateRawSync, crc32 } from 'node:zlib';
import { readZip, selectInstallFiles, buildEntry, assembleIndex, toJson, signBytes, verifyBytes, validateIndex, main, CORES, IndexError } from './make-cores-index.mjs';

const sha = b => createHash('sha256').update(b).digest('hex');
const NOW = new Date('2026-11-20T12:00:00Z');
const OLD = '2026-11-10T00:00:00Z'; // published long enough ago for the 72 h cool-down

/** A real zip writer (stored or deflated) so the reader is tested against genuine archives. */
function makeZip(files, { deflate = true } = {}) {
  const locals = [], central = []; let offset = 0;
  for (const [name, data] of Object.entries(files)) {
    const n = Buffer.from(name), raw = Buffer.from(data), comp = deflate ? deflateRawSync(raw) : raw, crc = crc32(raw);
    const lh = Buffer.alloc(30); lh.writeUInt32LE(0x04034b50, 0); lh.writeUInt16LE(20, 4); lh.writeUInt16LE(deflate ? 8 : 0, 8);
    lh.writeUInt32LE(crc, 14); lh.writeUInt32LE(comp.length, 18); lh.writeUInt32LE(raw.length, 22); lh.writeUInt16LE(n.length, 26);
    locals.push(lh, n, comp);
    const ch = Buffer.alloc(46); ch.writeUInt32LE(0x02014b50, 0); ch.writeUInt16LE(20, 4); ch.writeUInt16LE(20, 6); ch.writeUInt16LE(deflate ? 8 : 0, 10);
    ch.writeUInt32LE(crc, 16); ch.writeUInt32LE(comp.length, 20); ch.writeUInt32LE(raw.length, 24); ch.writeUInt16LE(n.length, 28); ch.writeUInt32LE(offset, 42);
    central.push(ch, n); offset += 30 + n.length + comp.length;
  }
  const cd = Buffer.concat(central), end = Buffer.alloc(22);
  end.writeUInt32LE(0x06054b50, 0); end.writeUInt16LE(Object.keys(files).length, 8); end.writeUInt16LE(Object.keys(files).length, 10);
  end.writeUInt32LE(cd.length, 12); end.writeUInt32LE(offset, 16);
  return Buffer.concat([...locals, cd, end]);
}

/** What each engine's real zip holds (names as upstream ships them), plus files Glide must ignore. */
function zipFiles(coreKey, version, arch, extra = {}) {
  const tag = `${coreKey}-${arch}`;
  if (coreKey === 'xray') return { 'xray.exe': `exe-${tag}`, 'wintun.dll': `wintun-${arch}`, 'geoip.dat': 'geo', 'geosite.dat': 'site', 'README.md': 'x', ...extra };
  if (coreKey === 'singBox') return { [`sing-box-${version}-windows/sing-box.exe`]: `exe-${tag}`, ...(arch === 'win-x64' ? { [`sing-box-${version}-windows/libcronet.dll`]: 'cronet' } : {}), LICENSE: 'l', ...extra };
  return { [`mihomo-windows-${arch === 'win-x64' ? 'amd64' : '386'}-v${version}.exe`]: `exe-${tag}`, ...extra };
}

/** A folder that looks like one engine release: zips, the release JSON GitHub would return, and Xray's .dgst. */
function fixture(coreKey, version, { published = OLD, tamperDigest = false, badDgst = false, prerelease = false, extra = {} } = {}) {
  const core = CORES[coreKey], dir = mkdtempSync(join(tmpdir(), 'glide-idx-')), assets = [];
  for (const arch of ['win-x64', 'win-x86']) {
    const name = core.assets[arch](version);
    const zip = makeZip(zipFiles(coreKey, version, arch, extra));
    writeFileSync(join(dir, name), zip);
    assets.push({ name, digest: 'sha256:' + (tamperDigest ? 'f'.repeat(64) : sha(zip)), browser_download_url: `https://github.com/${core.repo}/releases/download/v${version}/${name}` });
    if (core.evidence === 'dgst') {
      writeFileSync(join(dir, name + '.dgst'), `MD5= 00\nSHA2-256= ${badDgst ? '0'.repeat(64) : sha(zip)}\n`);
      assets.push({ name: name + '.dgst', digest: 'sha256:00', browser_download_url: `https://github.com/${core.repo}/releases/download/v${version}/${name}.dgst` });
    }
  }
  writeFileSync(join(dir, `release-${core.repo.replace('/', '_')}-v${version}.json`),
    JSON.stringify({ tag_name: `v${version}`, published_at: published, draft: false, prerelease, assets }));
  return dir;
}

const stubGh = (code) => {
  const dir = mkdtempSync(join(tmpdir(), 'glide-gh-')), p = join(dir, 'stub.mjs');
  writeFileSync(p, `process.exit(${code});`);
  process.env.GH_BIN = process.execPath; process.env.GH_BIN_ARGS = JSON.stringify([p]);
};
const opts = (o = {}) => ({ minGlide: '1.1.0', archs: ['win-x64', 'win-x86'], cooldownHours: 72, attest: true, drop: [], revoke: [], validDays: 75, ...o });
const ctx = dir => ({ now: NOW, assetsDir: dir });

test('zip reader: stored and deflated entries read back exactly', () => {
  for (const deflate of [true, false]) {
    const z = makeZip({ 'a/b.exe': 'BBB', 'x.txt': 'hello world' }, { deflate });
    const r = Object.fromEntries(readZip(z).map(en => [en.name, en.data().toString()]));
    assert.deepEqual(r, { 'a/b.exe': 'BBB', 'x.txt': 'hello world' });
  }
});

test('zip reader refuses a file that is not a zip', () => {
  assert.throws(() => readZip(Buffer.from('not a zip at all')), IndexError);
});

test('selection: Xray needs xray.exe and wintun.dll; everything else in the zip is ignored', () => {
  const { files, sources } = selectInstallFiles('xray', 'win-x64', makeZip(zipFiles('xray', '1', 'win-x64')));
  assert.deepEqual(Object.keys(files), ['wintun.dll', 'xray.exe']);
  assert.equal(files['xray.exe'], sha('exe-xray-win-x64'));
  assert.deepEqual(sources, {});
  assert.throws(() => selectInstallFiles('xray', 'win-x64', makeZip({ 'xray.exe': '1' })), /wintun.dll is not in the zip/);
  assert.throws(() => selectInstallFiles('xray', 'win-x64', makeZip({ 'a/xray.exe': '1', 'b/xray.exe': '2', 'wintun.dll': 'w' })), /more than once/);
});

test('selection: sing-box needs sing-box.exe; libcronet*.dll is required on x64 and optional on x86', () => {
  const x64 = selectInstallFiles('singBox', 'win-x64', makeZip(zipFiles('singBox', '1.14.4', 'win-x64')));
  assert.deepEqual(Object.keys(x64.files), ['libcronet.dll', 'sing-box.exe']);
  const x86 = selectInstallFiles('singBox', 'win-x86', makeZip(zipFiles('singBox', '1.14.4', 'win-x86')));
  assert.deepEqual(Object.keys(x86.files), ['sing-box.exe']);
  assert.throws(() => selectInstallFiles('singBox', 'win-x64', makeZip({ 'sing-box.exe': '1' })), /no libcronet.dll/);
  assert.throws(() => selectInstallFiles('singBox', 'win-x64', makeZip({ 'libcronet.dll': '1' })), /sing-box.exe is not in the zip/);
});

test("selection: mihomo's versioned exe is installed as mihomo.exe, and its zip name is recorded", () => {
  const { files, sources } = selectInstallFiles('mihomo', 'win-x64', makeZip(zipFiles('mihomo', '1.19.34', 'win-x64')));
  assert.deepEqual(Object.keys(files), ['mihomo.exe']);
  assert.deepEqual(sources, { 'mihomo.exe': 'mihomo-windows-amd64-v1.19.34.exe' });
  assert.throws(() => selectInstallFiles('mihomo', 'win-x64', makeZip({ 'readme.md': 'x' })), /mihomo executable is not in the zip/);
  assert.throws(() => selectInstallFiles('mihomo', 'win-x64', makeZip({ 'mihomo-a.exe': '1', 'mihomo-b.exe': '2' })), /more than once/);
  assert.throws(() => selectInstallFiles('nope', 'win-x64', makeZip({})), /unknown core/);
});

test('entry: hashes the zip and each installed file, builds the official URL', async () => {
  const dir = fixture('xray', '26.11.9');
  const e = await buildEntry('xray', '26.11.9', opts(), ctx(dir));
  const a = e.assets['win-x64'];
  assert.equal(a.name, 'Xray-windows-64.zip');
  assert.equal(a.url, 'https://github.com/XTLS/Xray-core/releases/download/v26.11.9/Xray-windows-64.zip');
  assert.equal(a.sha256, sha(readFileSync(join(dir, 'Xray-windows-64.zip'))));
  assert.deepEqual(Object.keys(a.files), ['wintun.dll', 'xray.exe']);
  assert.equal(a.files['xray.exe'], sha('exe-xray-win-x64'));
  assert.equal(a.sources, undefined);
  assert.equal(e.minGlide, '1.1.0');
});

test('entry: GitHub digest that differs from the download is refused', async () => {
  await assert.rejects(buildEntry('xray', '26.11.9', opts(), ctx(fixture('xray', '26.11.9', { tamperDigest: true }))), /recorded digest/);
});

test("entry: Xray's .dgst that disagrees is refused", async () => {
  await assert.rejects(buildEntry('xray', '26.11.9', opts(), ctx(fixture('xray', '26.11.9', { badDgst: true }))), /\.dgst does not match/);
});

test('entry: a release younger than the cool-down is refused unless the cool-down is lowered', async () => {
  const fresh = fixture('mihomo', '1.19.34', { published: '2026-11-19T12:00:00Z' }); // 24 h before NOW
  await assert.rejects(buildEntry('mihomo', '1.19.34', opts(), ctx(fresh)), /cool-down/);
  const e = await buildEntry('mihomo', '1.19.34', opts({ cooldownHours: 0 }), ctx(fresh));
  assert.equal(e.version, '1.19.34');
});

test('entry: pre-releases are refused', async () => {
  await assert.rejects(buildEntry('mihomo', '1.19.34', opts(), ctx(fixture('mihomo', '1.19.34', { prerelease: true }))), /pre-release/);
});

test('sing-box: the attestation check must succeed; --no-attest skips it', async () => {
  const dir = fixture('singBox', '1.14.4');
  stubGh(1);
  await assert.rejects(buildEntry('singBox', '1.14.4', opts(), ctx(dir)), /attestation check failed/);
  stubGh(0);
  assert.equal((await buildEntry('singBox', '1.14.4', opts(), ctx(dir))).version, '1.14.4');
  stubGh(1);
  assert.equal((await buildEntry('singBox', '1.14.4', opts({ attest: false }), ctx(dir))).version, '1.14.4');
});

test('assemble: merges previous entries, advances the sequence, sets a bounded validity window', async () => {
  const mk = async v => ({ xray: [await buildEntry('xray', v, opts(), ctx(fixture('xray', v)))] });
  const first = assembleIndex(null, await mk('26.10.31'), opts(), ctx());
  assert.equal(first.sequence, 1);
  const second = assembleIndex(first, await mk('26.11.9'), opts(), ctx());
  assert.equal(second.sequence, 2);
  assert.deepEqual(second.cores.xray.map(e => e.version), ['26.10.31', '26.11.9']);
  assert.equal(new Date(second.expires) - new Date(second.issued), 75 * 864e5);
});

test('assemble: --drop removes a version, --revoke records it, a lower sequence is refused', async () => {
  const e = await buildEntry('xray', '26.11.9', opts(), ctx(fixture('xray', '26.11.9')));
  const prev = assembleIndex(null, { xray: [e] }, opts(), ctx());
  const next = assembleIndex(prev, {}, opts({ drop: ['xray:26.11.9'], revoke: ['xray:26.10.1:CVE-2026-0001: remote code'] }), ctx());
  assert.equal(next.cores.xray.length, 0);
  assert.deepEqual(next.revoked, [{ core: 'xray', version: '26.10.1', reason: 'CVE-2026-0001: remote code' }]);
  assert.throws(() => assembleIndex(prev, {}, opts({ sequence: 1 }), ctx()), /must be higher/);
});

test('output is ASCII-only deterministic JSON that round-trips', async () => {
  const e = await buildEntry('xray', '26.11.9', opts(), ctx(fixture('xray', '26.11.9')));
  const idx = assembleIndex(null, { xray: [e] }, opts({ revoke: ['xray:26.1.1:café فارسی'] }), ctx());
  const text = toJson(idx);
  assert.ok(/^[\x00-\x7f]*$/.test(text));
  assert.deepEqual(JSON.parse(text), idx);
  assert.equal(toJson(idx), text);
});

test('sign and verify (ECDSA P-256, DER, base64); any change breaks it; a wrong key is refused', () => {
  const k = generateKeyPairSync('ec', { namedCurve: 'prime256v1' });
  const pub = k.publicKey.export({ type: 'spki', format: 'pem' }), priv = k.privateKey.export({ type: 'pkcs8', format: 'pem' });
  const bytes = Buffer.from('{"format":1}\n');
  const sig = signBytes(bytes, priv);
  assert.ok(verifyBytes(bytes, sig, pub));
  assert.ok(!verifyBytes(Buffer.from('{"format":2}\n'), sig, pub));
  const other = generateKeyPairSync('ec', { namedCurve: 'prime256v1' }).publicKey.export({ type: 'spki', format: 'pem' });
  assert.ok(!verifyBytes(bytes, sig, other));
  assert.ok(!verifyBytes(bytes, 'not base64 !!', pub));
});

test('validation: the app’s rules (https github.com only, hashes, file names, validity)', async () => {
  const e = await buildEntry('xray', '26.11.9', opts(), ctx(fixture('xray', '26.11.9')));
  const ok = () => assembleIndex(null, { xray: [structuredClone(e)] }, opts(), ctx());
  const bad = (mut, re) => { const idx = ok(); mut(idx.cores.xray[0].assets['win-x64']); assert.throws(() => validateIndex(idx, NOW), re); };
  bad(a => { a.url = 'http://github.com/x.zip'; }, /https:\/\/github\.com/);
  bad(a => { a.url = 'https://evil.example/x.zip'; }, /https:\/\/github\.com/);
  bad(a => { a.sha256 = 'abc'; }, /size\/hash/);
  bad(a => { a.files = { '..\\evil.exe': 'a'.repeat(64) }; }, /file/);
  bad(a => { a.files = {}; }, /no files/);
  bad(a => { a.sources = { 'xray.exe': '..\evil.exe' }; }, /source for/);
  bad(a => { a.sources = { 'nothere.exe': 'x.exe' }; }, /source for/);
  const long = ok(); long.expires = new Date(NOW.getTime() + 200 * 864e5).toISOString();
  assert.throws(() => validateIndex(long, NOW), /validity longer/);
  const expired = ok(); expired.expires = '2026-01-01T00:00:00Z';
  assert.throws(() => validateIndex(expired, NOW), /expired/);
});

test('end to end: build three engines, sign, verify with the CLI; tampering is caught', async () => {
  const a = fixture('xray', '26.11.9'), b = fixture('singBox', '1.14.4'), c = fixture('mihomo', '1.19.34');
  // one folder holding all three releases, as --assets-dir expects
  const dir = mkdtempSync(join(tmpdir(), 'glide-e2e-'));
  for (const src of [a, b, c]) for (const f of (await import('node:fs')).readdirSync(src)) writeFileSync(join(dir, f), readFileSync(join(src, f)));
  stubGh(0);
  const k = generateKeyPairSync('ec', { namedCurve: 'prime256v1' });
  const keyFile = join(dir, 'key.pem'), pubFile = join(dir, 'pub.pem'), out = join(dir, 'cores-index.json');
  writeFileSync(keyFile, k.privateKey.export({ type: 'pkcs8', format: 'pem' }));
  writeFileSync(pubFile, k.publicKey.export({ type: 'spki', format: 'pem' }));
  await main(['--out', out, '--min-glide', '1.1.0', '--xray', '26.11.9', '--sing-box', '1.14.4', '--mihomo', '1.19.34',
    '--assets-dir', dir, '--now', '2026-11-20T12:00:00Z', '--sign-with', keyFile]);
  await main(['--verify', out, '--public-key', pubFile, '--now', '2026-11-21T00:00:00Z']);
  const idx = JSON.parse(readFileSync(out, 'utf8'));
  assert.deepEqual(Object.keys(idx.cores), ['xray', 'singBox', 'mihomo']);
  assert.equal(idx.cores.mihomo[0].assets['win-x86'].name, 'mihomo-windows-386-v1.19.34.zip');
  assert.deepEqual(idx.cores.mihomo[0].assets['win-x86'].sources, { 'mihomo.exe': 'mihomo-windows-386-v1.19.34.exe' });
  assert.equal(idx.cores.singBox[0].assets['win-x64'].name, 'sing-box-1.14.4-windows-amd64.zip');
  // tampering with one byte of the index must fail verification
  writeFileSync(out, readFileSync(out, 'utf8').replace('"sequence": 1', '"sequence": 7'));
  await assert.rejects(main(['--verify', out, '--public-key', pubFile, '--now', '2026-11-21T00:00:00Z']), /signature does not match/);
});

test('--sign signs an existing, valid index and refuses an invalid one', async () => {
  const e = await buildEntry('mihomo', '1.19.34', opts(), ctx(fixture('mihomo', '1.19.34')));
  const idx = assembleIndex(null, { mihomo: [e] }, opts(), ctx());
  const dir = mkdtempSync(join(tmpdir(), 'glide-sign-')), file = join(dir, 'cores-index.json'), keyFile = join(dir, 'k.pem');
  const k = generateKeyPairSync('ec', { namedCurve: 'prime256v1' });
  writeFileSync(keyFile, k.privateKey.export({ type: 'pkcs8', format: 'pem' }));
  writeFileSync(file, toJson(idx));
  await main(['--sign', file, '--sign-with', keyFile, '--now', '2026-11-20T12:00:00Z']);
  assert.ok(verifyBytes(readFileSync(file), readFileSync(file + '.sig256', 'utf8'), k.publicKey.export({ type: 'spki', format: 'pem' })));
  writeFileSync(file, toJson({ ...idx, format: 9 }));
  await assert.rejects(main(['--sign', file, '--sign-with', keyFile, '--now', '2026-11-20T12:00:00Z']), /format/);
  await assert.rejects(main(['--sign', file]), /--sign-with/);
});
