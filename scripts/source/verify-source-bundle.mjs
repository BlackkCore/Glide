#!/usr/bin/env node
// Checks a source bundle made by make-source-bundle.mjs against cores.lock.json and cores.source.json.
// Exits non-zero, listing every problem, so a release cannot go out with binaries and missing or mismatched sources.
//   node scripts/source/verify-source-bundle.mjs --dir <bundle> --tag <tag> --commit <glide commit> [--release] [--root .]
import { existsSync, readFileSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { CORES, SourceError, expectedSourceAssets, glideArchive, loadPins, manifestName, readJson, run, sha256, sumsName } from './lib.mjs';

const REQUIRED_IN_GLIDE_SOURCE = [
  'LICENSE', 'NOTICE.md', 'THIRD_PARTY_NOTICES.md', 'cores.lock.json', 'cores.source.json', 'global.json',
  'v2rayN/Directory.Packages.props', 'installer/glide.iss', '.github/workflows/package-zip.yml', '.github/workflows/package-source.yml',
  'scripts/source/make-source-bundle.mjs', 'licenses/cores/mihomo-GPL-3.0.txt', 'licenses/cores/sing-box-GPL-3.0-with-name-clause.txt',
];

export function verify({ dir, tag, commit, release = false, root = process.cwd() }) {
  const problems = [];
  const bad = (m) => problems.push(m);
  let loaded;
  try { loaded = loadPins(join(root, 'cores.lock.json'), join(root, 'cores.source.json')); } catch (e) { return [e.message]; }
  const { versions, cores, pins } = loaded;

  for (const name of expectedSourceAssets(tag, versions)) if (!existsSync(join(dir, name))) bad(`missing ${name}`);
  if (problems.length) return problems;

  const manifest = readJson(join(dir, manifestName(tag)));
  if (manifest.glide?.tag !== tag || manifest.glide?.commit !== commit) bad(`manifest says Glide ${manifest.glide?.tag}@${manifest.glide?.commit}, expected ${tag}@${commit}`);

  // Checksums: the sums file must describe exactly the files present.
  const sums = new Map(readFileSync(join(dir, sumsName(tag)), 'utf8').split('\n').filter(Boolean).map((l) => { const [h, n] = l.split(/\s{2}/); return [n, h]; }));
  for (const name of expectedSourceAssets(tag, versions)) {
    if (name === sumsName(tag)) continue;
    if (sums.get(name) !== sha256(join(dir, name))) bad(`${name}: checksum does not match ${sumsName(tag)}`);
  }

  // Each core: right version, pinned commit, archive present, licence and dependency source inside.
  for (const id of Object.keys(CORES)) {
    const entry = manifest.cores?.find((c) => c.id === id);
    if (!entry) { bad(`manifest has no entry for ${id}`); continue; }
    if (entry.version !== versions[id]) bad(`${id}: manifest version ${entry.version} is not the shipped ${versions[id]}`);
    if (entry.commit !== cores[id].commit || entry.tag !== cores[id].tag) bad(`${id}: manifest commit ${entry.commit} is not the pinned ${cores[id].commit}`);
    if (entry.archive !== CORES[id].archive(versions[id])) bad(`${id}: archive is named ${entry.archive}`);
    if (release && entry.vendored !== true) bad(`${id}: a release needs the vendored dependency source`);
    const files = listTar(join(dir, CORES[id].archive(versions[id])));
    if (!files) { bad(`${CORES[id].archive(versions[id])}: cannot be read as a tar.gz`); continue; }
    const top = CORES[id].archive(versions[id]).replace(/-source\.tar\.gz$/, '');
    const has = (p) => files.has(`${top}/${p}`);
    if (!has('go.mod') || !has('go.sum')) bad(`${id}: archive lacks go.mod or go.sum`);
    if (![...files].some((f) => /^[^/]+\/(LICENSE|COPYING)[^/]*$/i.test(f))) bad(`${id}: archive has no licence file`);
    if (release && !has('vendor/modules.txt')) bad(`${id}: archive has no vendor/modules.txt`);
    if ([...files].some((f) => f.split('/').includes('.git'))) bad(`${id}: archive contains .git`);
    if ([...files].some((f) => /\/vendor\/github\.com\/sagernet\/cronet-go\/lib\/[^/]+\/libcronet\.(a|so|dll|dylib|lib)$/.test(f))) bad(`${id}: archive carries prebuilt libcronet binaries (should be pruned)`);
  }
  if (manifest.components?.find((c) => c.id === 'libcronet.dll')?.builtFrom?.windowsAmd64GitBlob !== pins.libcronet.windowsAmd64GitBlob) bad('libcronet origin in the manifest differs from cores.source.json');

  // Glide's own source: the release must be rebuildable from it.
  const glide = listTar(join(dir, glideArchive(tag)));
  if (!glide) { bad(`${glideArchive(tag)}: cannot be read as a tar.gz`); return problems; }
  for (const f of REQUIRED_IN_GLIDE_SOURCE) if (!glide.has(`Glide-${tag}/${f}`)) bad(`Glide source archive lacks ${f}`);
  return problems;
}

function listTar(path) {
  try {
    return new Set(run('tar', ['--force-local', '-tzf', path]).split('\n').filter(Boolean).map((l) => l.replace(/^\.\//, '')));
  } catch { return null; }
}

if (process.argv[1]?.endsWith('verify-source-bundle.mjs')) {
  const a = {};
  for (let i = 2; i < process.argv.length; i++) {
    const k = process.argv[i];
    if (k === '--release') a.release = true; else a[k.slice(2)] = process.argv[++i];
  }
  try {
    for (const k of ['dir', 'tag', 'commit']) if (!a[k]) throw new SourceError(`--${k} is required`);
    const problems = verify({ ...a, dir: resolve(a.dir), root: resolve(a.root ?? '.') });
    if (problems.length) {
      console.error(`source bundle verification FAILED:\n - ${problems.join('\n - ')}`);
      process.exit(1);
    }
    console.log(`source bundle for ${a.tag} verified${a.release ? ' (release mode)' : ''}`);
  } catch (e) { console.error(`verification error: ${e.message}`); process.exit(1); }
}
