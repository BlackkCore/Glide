#!/usr/bin/env node
// Builds (and optionally signs / verifies) Glide's signed engine index: cores-index.json.
// Glide (installed and portable) updates Xray, sing-box and mihomo only to builds listed in this file
// (see SECURITY.md). No dependencies: Node 20+, the GitHub CLI `gh`.
//
//   node scripts/cores/make-cores-index.mjs --out cores-index.json --min-glide 1.1.0 \
//        --xray 26.11.9 --sing-box 1.14.4 --mihomo 1.19.34 [--previous old.json] [--valid-days 75] \
//        [--revoke xray:26.10.1:CVE-...] [--drop xray:26.10.31] [--sign-with key.pem]
//   node scripts/cores/make-cores-index.mjs --sign cores-index.json --sign-with key.pem   # signing only (the protected workflow)
//   node scripts/cores/make-cores-index.mjs --verify cores-index.json [--public-key pub.pem]   # default: the key in UpdateSignatureKey.cs
//
// Evidence required for every asset (the script stops on any mismatch, it never trusts a hash it computed alone):
//   - GitHub's recorded digest of the release asset (release API "digest") equals the SHA-256 of what was downloaded;
//   - Xray: the maintainers' <asset>.dgst file says the same SHA-256;
//   - sing-box: `gh release verify-asset` (GitHub release attestation) succeeds; --no-attest skips it and says so;
//   - the release is at least --cooldown-hours old (default 72) so a hijacked release is likely noticed first.
// Then each file Glide installs from the zip (the same ones the release packages install) is hashed, so the app can check
// what it extracts, not only the archive. mihomo's versioned exe is listed under its installed name, with its zip name in "sources".
// Output is ASCII-only JSON, signed byte for byte with ECDSA P-256 (same key and format as app updates: base64 DER).
import { createHash, createPublicKey, createPrivateKey, sign as cryptoSign, verify as cryptoVerify } from 'node:crypto';
import { readFileSync, writeFileSync, existsSync, mkdtempSync, rmSync } from 'node:fs';
import { spawnSync } from 'node:child_process';
import { inflateRawSync } from 'node:zlib';
import { pathToFileURL } from 'node:url';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

export const FORMAT = 1;
export const MAX_VALID_DAYS = 120; // same ceiling the app enforces
export const CORES = {
  xray: { repo: 'XTLS/Xray-core', evidence: 'dgst',
    assets: { 'win-x64': () => 'Xray-windows-64.zip', 'win-x86': () => 'Xray-windows-32.zip' } },
  singBox: { repo: 'SagerNet/sing-box', evidence: 'attest',
    assets: { 'win-x64': v => `sing-box-${v}-windows-amd64.zip`, 'win-x86': v => `sing-box-${v}-windows-386.zip` } },
  mihomo: { repo: 'MetaCubeX/mihomo', evidence: 'digest',
    assets: { 'win-x64': v => `mihomo-windows-amd64-v${v}.zip`, 'win-x86': v => `mihomo-windows-386-v${v}.zip` } },
};

export class IndexError extends Error {}
const fail = m => { throw new IndexError(m); };
const sha256 = buf => createHash('sha256').update(buf).digest('hex');

// ---- a minimal zip reader (store + deflate, no zip64): enough for the engine zips, no dependency ----
export function readZip(buf) {
  let eocd = -1;
  for (let i = buf.length - 22; i >= Math.max(0, buf.length - 65557); i--) {
    if (buf.readUInt32LE(i) === 0x06054b50) { eocd = i; break; }
  }
  if (eocd < 0) fail('not a zip file');
  const count = buf.readUInt16LE(eocd + 10);
  let p = buf.readUInt32LE(eocd + 16);
  if (count === 0xffff || p === 0xffffffff) fail('zip64 archives are not supported');
  const entries = [];
  for (let n = 0; n < count; n++) {
    if (buf.readUInt32LE(p) !== 0x02014b50) fail('damaged zip directory');
    const method = buf.readUInt16LE(p + 10), csize = buf.readUInt32LE(p + 20), usize = buf.readUInt32LE(p + 24);
    const nameLen = buf.readUInt16LE(p + 28), extraLen = buf.readUInt16LE(p + 30), commentLen = buf.readUInt16LE(p + 32);
    const local = buf.readUInt32LE(p + 42);
    const name = buf.toString('utf8', p + 46, p + 46 + nameLen);
    p += 46 + nameLen + extraLen + commentLen;
    if (name.endsWith('/')) continue;
    entries.push({ name, data: () => {
      if (buf.readUInt32LE(local) !== 0x04034b50) fail(`damaged zip entry ${name}`);
      const start = local + 30 + buf.readUInt16LE(local + 26) + buf.readUInt16LE(local + 28);
      const raw = buf.subarray(start, start + csize);
      const out = method === 0 ? raw : method === 8 ? inflateRawSync(raw) : fail(`unsupported zip method ${method} in ${name}`);
      if (out.length !== usize) fail(`size mismatch inside the zip for ${name}`);
      return out;
    } });
  }
  return entries;
}

const leafOf = n => n.split(/[\/]/).pop();

/**
 * The files Glide installs from an engine zip, exactly as the release packages do (package-zip.yml): Xray: xray.exe and wintun.dll;
 * sing-box: sing-box.exe and libcronet*.dll (required on x64); mihomo: its single versioned exe, installed as mihomo.exe.
 * Returns { files: installedName -> SHA-256 of the extracted file, sources: installedName -> name inside the zip (only when renamed) }.
 */
export function selectInstallFiles(coreKey, arch, zipBuf) {
  const entries = readZip(zipBuf).map(e => ({ leaf: leafOf(e.name), e }));
  const files = {}, sources = {};
  const one = (label, re) => {
    const m = entries.filter(x => re.test(x.leaf));
    if (m.length === 0) fail(`${label} is not in the zip`);
    if (m.length > 1) fail(`${label} appears more than once in the zip`);
    return m[0];
  };
  if (coreKey === 'xray') {
    for (const n of ['xray.exe', 'wintun.dll']) files[n] = sha256(one(n, new RegExp(`^${n.replace('.', '\.')}$`, 'i')).e.data());
  } else if (coreKey === 'singBox') {
    files['sing-box.exe'] = sha256(one('sing-box.exe', /^sing-box.exe$/i).e.data());
    const cronet = entries.filter(x => /^libcronet.*.dll$/i.test(x.leaf));
    if (arch === 'win-x64' && cronet.length === 0) fail('the x64 sing-box zip has no libcronet.dll');
    for (const c of cronet) {
      if (files[c.leaf]) fail(`${c.leaf} appears more than once in the zip`);
      files[c.leaf] = sha256(c.e.data());
    }
  } else if (coreKey === 'mihomo') {
    const m = one('the mihomo executable', /^mihomo.*.exe$/i);
    files['mihomo.exe'] = sha256(m.e.data());
    sources['mihomo.exe'] = m.leaf;
  } else fail(`unknown core ${coreKey}`);
  const sorted = o => Object.fromEntries(Object.entries(o).sort(([a], [b]) => a.localeCompare(b)));
  return { files: sorted(files), sources: sorted(sources) };
}

// ---- validation: the same shape rules the app applies (CoreIndexVerifier.Validate) ----
const SHA = /^[0-9a-f]{64}$/;
const FILE = /^[A-Za-z0-9][A-Za-z0-9._-]*\.(exe|dll)$/i;
const VER = /^\d+\.\d+\.\d+$/;
export function validateIndex(idx, now = new Date()) {
  if (idx.format !== FORMAT) fail('format');
  if (!Number.isInteger(idx.sequence) || idx.sequence < 1) fail('sequence');
  const issued = new Date(idx.issued), expires = new Date(idx.expires);
  if (isNaN(issued) || isNaN(expires)) fail('issued/expires');
  if (expires <= now) fail('expired');
  if (expires - issued > MAX_VALID_DAYS * 864e5) fail(`validity longer than ${MAX_VALID_DAYS} days`);
  for (const [core, list] of Object.entries(idx.cores)) {
    if (!CORES[core]) fail(`unknown core ${core}`);
    for (const e of list) {
      if (!VER.test(e.version) || !VER.test(e.minGlide)) fail(`${core}: bad version`);
      for (const [arch, a] of Object.entries(e.assets)) {
        const u = new URL(a.url);
        if (u.protocol !== 'https:' || u.hostname !== 'github.com') fail(`${core} ${e.version} ${arch}: address must be https://github.com/`);
        if (!(a.size > 0 && a.size <= 300 * 1024 * 1024) || !SHA.test(a.sha256)) fail(`${core} ${e.version} ${arch}: size/hash`);
        const names = Object.entries(a.files);
        if (!names.length) fail(`${core} ${e.version} ${arch}: no files`);
        for (const [n, h] of names) if (!FILE.test(n) || !SHA.test(h)) fail(`${core} ${e.version} ${arch}: file ${n}`);
        for (const [n, s] of Object.entries(a.sources ?? {})) if (!(n in a.files) || !FILE.test(s)) fail(`${core} ${e.version} ${arch}: source for ${n}`);
      }
    }
  }
  return idx;
}

// ---- sources of evidence (network, or --assets-dir for tests) ----
function gh(args, opts = {}) {
  const bin = process.env.GH_BIN || 'gh';
  const lead = JSON.parse(process.env.GH_BIN_ARGS || '[]'); // tests: GH_BIN=node GH_BIN_ARGS='["stub.mjs"]'
  const r = spawnSync(bin, [...lead, ...args], { encoding: 'utf8', maxBuffer: 64 * 1024 * 1024, ...opts });
  return r;
}
function ghJson(path) {
  const r = gh(['api', path]);
  if (r.status !== 0) fail(`gh api ${path} failed: ${(r.stderr || '').trim()}`);
  return JSON.parse(r.stdout);
}
async function download(url) {
  const res = await fetch(url, { redirect: 'follow' });
  if (!res.ok) fail(`download failed (${res.status}): ${url}`);
  return Buffer.from(await res.arrayBuffer());
}

/** Everything known about one release: GitHub's release JSON (with asset digests and the publish time). */
async function releaseOf(repo, tag, ctx) {
  if (ctx.assetsDir) return JSON.parse(readFileSync(join(ctx.assetsDir, `release-${repo.replace('/', '_')}-${tag}.json`), 'utf8'));
  return ghJson(`repos/${repo}/releases/tags/${tag}`);
}
async function fetchAsset(rel, name, ctx) {
  const a = rel.assets.find(x => x.name === name);
  if (!a) fail(`${rel.tag_name}: asset ${name} not found`);
  const buf = ctx.assetsDir ? readFileSync(join(ctx.assetsDir, name)) : await download(a.browser_download_url);
  return { buf, meta: a };
}

export async function buildEntry(coreKey, version, opts, ctx) {
  const core = CORES[coreKey], tag = `v${version}`;
  const rel = await releaseOf(core.repo, tag, ctx);
  const ageHours = (ctx.now - new Date(rel.published_at)) / 36e5;
  if (!(ageHours >= opts.cooldownHours)) fail(`${coreKey} ${version} was published ${ageHours.toFixed(1)} h ago; the cool-down is ${opts.cooldownHours} h (--cooldown-hours)`);
  if (rel.draft || rel.prerelease) fail(`${coreKey} ${version} is a draft or pre-release`);
  const entry = { version, minGlide: opts.minGlide, vettedOn: ctx.now.toISOString().slice(0, 10), assets: {} };
  for (const arch of opts.archs) {
    const name = core.assets[arch](version);
    const { buf, meta } = await fetchAsset(rel, name, ctx);
    const digest = sha256(buf);
    if (meta.digest !== `sha256:${digest}`) fail(`${name}: GitHub's recorded digest (${meta.digest}) differs from the download (sha256:${digest})`);
    if (core.evidence === 'dgst') {
      const d = await fetchAsset(rel, `${name}.dgst`, ctx);
      const m = /SHA2-256=\s*([0-9a-f]{64})/i.exec(d.buf.toString('utf8'));
      if (!m || m[1].toLowerCase() !== digest) fail(`${name}: the maintainers' .dgst does not match the download`);
    }
    if (core.evidence === 'attest') {
      if (opts.attest) {
        const dir = mkdtempSync(join(tmpdir(), 'glide-attest-'));
        try {
          const file = join(dir, name);
          writeFileSync(file, buf);
          const r = gh(['release', 'verify-asset', tag, file, '-R', core.repo]);
          if (r.status !== 0) fail(`${name}: release attestation check failed: ${(r.stderr || r.stdout || '').trim()}`);
        } finally { rmSync(dir, { recursive: true, force: true }); }
      } else console.error(`WARNING: ${name}: attestation check skipped (--no-attest)`);
    }
    const { files, sources } = selectInstallFiles(coreKey, arch, buf);
    entry.assets[arch] = {
      name, url: `https://github.com/${core.repo}/releases/download/${tag}/${name}`,
      size: buf.length, sha256: digest, files,
      ...(Object.keys(sources).length ? { sources } : {}),
    };
  }
  return entry;
}

const verCmp = (a, b) => { const x = a.split('.').map(Number), y = b.split('.').map(Number); for (let i = 0; i < 3; i++) if (x[i] !== y[i]) return x[i] - y[i]; return 0; };

/** Merges new entries into the previous index, applies --drop/--revoke, advances the sequence, and validates. */
export function assembleIndex(prev, fresh, opts, ctx) {
  const idx = { format: FORMAT, sequence: 0, issued: '', expires: '', cores: {}, revoked: [] };
  for (const k of Object.keys(CORES)) idx.cores[k] = (prev?.cores?.[k] ?? []).map(e => ({ ...e }));
  for (const [k, entries] of Object.entries(fresh)) {
    for (const e of entries) idx.cores[k] = idx.cores[k].filter(x => x.version !== e.version).concat(e);
  }
  for (const d of opts.drop) {
    const [k, v] = d.split(':'); if (!CORES[k]) fail(`--drop: unknown core ${k}`);
    idx.cores[k] = idx.cores[k].filter(x => x.version !== v);
  }
  for (const k of Object.keys(idx.cores)) idx.cores[k].sort((a, b) => verCmp(a.version, b.version));
  idx.revoked = (prev?.revoked ?? []).slice();
  for (const r of opts.revoke) {
    const [core, version, ...why] = r.split(':'); if (!CORES[core] || !VER.test(version)) fail(`--revoke: bad value ${r}`);
    idx.revoked = idx.revoked.filter(x => !(x.core === core && x.version === version)).concat({ core, version, reason: why.join(':') });
  }
  idx.sequence = opts.sequence ?? (prev?.sequence ?? 0) + 1;
  if (prev && idx.sequence <= prev.sequence) fail(`sequence ${idx.sequence} must be higher than the previous ${prev.sequence}`);
  idx.issued = ctx.now.toISOString().replace(/\.\d+Z$/, 'Z');
  idx.expires = new Date(ctx.now.getTime() + opts.validDays * 864e5).toISOString().replace(/\.\d+Z$/, 'Z');
  return validateIndex(idx, ctx.now);
}

/** Deterministic, ASCII-only JSON: no charset can change the bytes that are signed. */
export function toJson(idx) {
  return JSON.stringify(idx, null, 2).replace(/[\u007f-￿]/g, c => '\\u' + c.charCodeAt(0).toString(16).padStart(4, '0')) + '\n';
}

export const signBytes = (bytes, pemPrivate) => cryptoSign('sha256', bytes, { key: createPrivateKey(pemPrivate), dsaEncoding: 'der' }).toString('base64');
export const verifyBytes = (bytes, sigB64, pemPublic) => {
  try { return cryptoVerify('sha256', bytes, { key: createPublicKey(pemPublic), dsaEncoding: 'der' }, Buffer.from(sigB64.trim(), 'base64')); } catch { return false; }
};

export function embeddedPublicKey(root = '.') {
  const src = readFileSync(join(root, 'v2rayN/ServiceLib/Common/UpdateSignatureKey.cs'), 'utf8').replace(/\r/g, '');
  const m = /-----BEGIN PUBLIC KEY-----[\s\S]*?-----END PUBLIC KEY-----/.exec(src.split('PublicKeyPem')[1] ?? '');
  if (!m) fail('no embedded public key found');
  return m[0].split('\n').map(l => l.trim()).join('\n');
}

function parseArgs(argv) {
  const o = { out: '', minGlide: '', versions: { xray: [], singBox: [], mihomo: [] }, previous: '', validDays: 75, cooldownHours: 72,
    archs: ['win-x64', 'win-x86'], attest: true, drop: [], revoke: [], signWith: '', sign: '', verify: '', publicKey: '', assetsDir: '', sequence: undefined, now: undefined };
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i], next = () => argv[++i] ?? fail(`${a} needs a value`);
    switch (a) {
      case '--out': o.out = next(); break;
      case '--min-glide': o.minGlide = next(); break;
      case '--xray': o.versions.xray.push(...next().split(',')); break;
      case '--sing-box': o.versions.singBox.push(...next().split(',')); break;
      case '--mihomo': o.versions.mihomo.push(...next().split(',')); break;
      case '--previous': o.previous = next(); break;
      case '--valid-days': o.validDays = Number(next()); break;
      case '--cooldown-hours': o.cooldownHours = Number(next()); break;
      case '--arch': o.archs = next().split(','); break;
      case '--no-attest': o.attest = false; break;
      case '--drop': o.drop.push(next()); break;
      case '--revoke': o.revoke.push(next()); break;
      case '--sign-with': o.signWith = next(); break;
      case '--sign': o.sign = next(); break;
      case '--verify': o.verify = next(); break;
      case '--public-key': o.publicKey = next(); break;
      case '--assets-dir': o.assetsDir = next(); break; // tests: read release JSON and assets from a folder, no network
      case '--sequence': o.sequence = Number(next()); break;
      case '--now': o.now = new Date(next()); break; // tests
      default: fail(`unknown option ${a}`);
    }
  }
  return o;
}

export async function main(argv) {
  const o = parseArgs(argv);
  const now = o.now ?? new Date();
  if (o.sign) { // sign an existing index; the only step that needs the private key
    if (!o.signWith) fail('--sign needs --sign-with <key.pem>');
    const bytes = readFileSync(o.sign);
    validateIndex(JSON.parse(bytes.toString('utf8')), now);
    writeFileSync(o.sign + '.sig256', signBytes(bytes, readFileSync(o.signWith)));
    console.log(`signed: ${o.sign}.sig256`);
    return;
  }
  if (o.verify) {
    const bytes = readFileSync(o.verify);
    const pub = o.publicKey ? readFileSync(o.publicKey, 'utf8') : embeddedPublicKey();
    const sigPath = o.verify + '.sig256';
    if (!existsSync(sigPath)) fail(`no signature file ${sigPath}`);
    if (!verifyBytes(bytes, readFileSync(sigPath, 'utf8'), pub)) fail('signature does not match the public key');
    const idx = validateIndex(JSON.parse(bytes.toString('utf8')), now);
    console.log(`OK: sequence ${idx.sequence}, expires ${idx.expires}, ${Object.values(idx.cores).flat().length} entries`);
    return;
  }
  if (!o.out || !VER.test(o.minGlide)) fail('--out and --min-glide X.Y.Z are required');
  if (!(o.validDays > 0 && o.validDays <= MAX_VALID_DAYS)) fail(`--valid-days must be 1..${MAX_VALID_DAYS}`);
  const ctx = { now, assetsDir: o.assetsDir };
  const fresh = {};
  for (const [k, versions] of Object.entries(o.versions)) {
    for (const v of versions) {
      if (!VER.test(v)) fail(`bad version ${v}`);
      (fresh[k] ??= []).push(await buildEntry(k, v, o, ctx));
    }
  }
  const prev = o.previous ? JSON.parse(readFileSync(o.previous, 'utf8')) : null;
  const idx = assembleIndex(prev, fresh, o, ctx);
  const text = toJson(idx);
  writeFileSync(o.out, text);
  console.log(`wrote ${o.out}: sequence ${idx.sequence}, expires ${idx.expires}`);
  if (o.signWith) {
    const sig = signBytes(Buffer.from(text, 'utf8'), readFileSync(o.signWith));
    writeFileSync(o.out + '.sig256', sig);
    console.log(`signed: ${o.out}.sig256`);
  } else console.log('UNSIGNED: sign it with the release key in the protected workflow (publish-cores-index.yml)');
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  main(process.argv.slice(2)).catch(e => { console.error(e instanceof IndexError ? `ERROR: ${e.message}` : e); process.exit(1); });
}
