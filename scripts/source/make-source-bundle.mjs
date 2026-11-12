#!/usr/bin/env node
// Builds the corresponding-source release assets for one Glide release:
//   Glide-source-<tag>.tar.gz            Glide's own source at the release commit (git archive)
//   <core>-<version>-source.tar.gz       each redistributed core at its pinned upstream commit, plus vendor/ (all Go dependencies)
//   SOURCES-<tag>.json, SHA256SUMS-source-<tag>.txt
// Versions come from cores.lock.json; the exact upstream commits come from cores.source.json and the build fails
// when a tag no longer points at its pinned commit. Never pushes or publishes anything.
//
//   node scripts/source/make-source-bundle.mjs --out <dir> --tag <release tag> --commit <glide commit> [--repo owner/name]
//        [--release] [--no-vendor]       (--release refuses --no-vendor: a release must carry the dependency source)
//   node scripts/source/make-source-bundle.mjs --print-pins   (resolves the lock's tags to commits for review)
import { lstatSync, mkdirSync, rmSync, writeFileSync, mkdtempSync, existsSync, readFileSync, readdirSync, renameSync, statSync, createReadStream, createWriteStream } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { pipeline } from 'node:stream/promises';
import { createGzip } from 'node:zlib';
import { CORES, SourceError, fail, glideArchive, loadPins, manifestName, readJson, run, sha256, sumsName } from './lib.mjs';

const CORE_REPOS = { xray: 'XTLS/Xray-core', singBox: 'SagerNet/sing-box', mihomo: 'MetaCubeX/mihomo' };
const CORE_LICENSES = { xray: 'MPL-2.0', singBox: 'GPL-3.0-or-later with the upstream name clause', mihomo: 'GPL-3.0' };
const base = () => process.env.GLIDE_SOURCE_BASE_URL ?? 'https://github.com'; // overridden only by the offline self-tests
const urlFor = (slug) => `${base()}/${slug}.git`;

function parseArgs(argv) {
  const a = { root: process.cwd(), lock: null, pins: null };
  for (let i = 0; i < argv.length; i++) {
    const k = argv[i];
    if (['--release', '--no-vendor', '--print-pins'].includes(k)) a[k.slice(2).replace(/-(\w)/g, (_, c) => c.toUpperCase())] = true;
    else if (k.startsWith('--')) a[k.slice(2)] = argv[++i];
    else fail(`unknown argument ${k}`);
  }
  a.root = resolve(a.root);
  a.lock ??= join(a.root, 'cores.lock.json');
  a.pins ??= join(a.root, 'cores.source.json');
  return a;
}

const git = (args, cwd) => run('git', args, { cwd });

/**
 * go mod vendor copies cronet-go's prebuilt libcronet.a/.so/.dll for ~30 platforms (1.7 GB). They are binaries, not
 * source, and a Windows purego build does not read them (lib/windows_amd64 is a Go constant; the DLL is loaded at run
 * time and is recorded by its git blob in cores.source.json), so they are left out and the manifest says so.
 */
function pruneCronetBinaries(dir) {
  const root = join(dir, 'vendor', 'github.com', 'sagernet', 'cronet-go', 'lib');
  const pruned = { files: 0, bytes: 0 };
  if (!existsSync(root)) return pruned;
  for (const platform of readdirSync(root)) {
    const p = join(root, platform);
    if (!lstatSync(p).isDirectory()) continue;
    for (const f of readdirSync(p)) {
      if (/^libcronet\.(a|so|dll|dylib|lib)$/.test(f)) {
        pruned.bytes += statSync(join(p, f)).size;
        pruned.files += 1;
        rmSync(join(p, f));
      }
    }
  }
  return pruned;
}

/** go mod vendor, once more if the toolchain itself dies (seen: an access violation on Windows), never on a plain error. */
function goVendor(dir) {
  try { return run('go', ['mod', 'vendor'], { cwd: dir }); } catch (e) {
    if (!/failed \((3221225477|-1073741819|139|134)\)/.test(e.message)) throw e;
    console.error('go crashed; retrying go mod vendor once');
    return run('go', ['mod', 'vendor'], { cwd: dir });
  }
}

/** The commit a tag points to (peeled when annotated), straight from the remote. */
function resolveTag(slug, tag) {
  const out = git(['ls-remote', urlFor(slug), `refs/tags/${tag}`, `refs/tags/${tag}^{}`]);
  const rows = out.split('\n').filter(Boolean).map((l) => l.split(/\s+/));
  const hit = rows.find((r) => r[1].endsWith('^{}')) ?? rows[0];
  return hit ? hit[0] : fail(`${slug} has no tag ${tag}`);
}

function fetchRef(slug, ref, dest, extra = []) {
  mkdirSync(dest, { recursive: true });
  git(['init', '-q'], dest);
  git(['remote', 'add', 'origin', urlFor(slug)], dest);
  git(['fetch', '-q', '--depth', '1', ...extra, 'origin', ref], dest);
}

async function gzipFile(src, dest) {
  await pipeline(createReadStream(src), createGzip({ level: 9 }), createWriteStream(dest));
}

function requireGnuTar() {
  if (!run('tar', ['--version']).includes('GNU tar')) fail('GNU tar is required (reproducible archives); on Windows run from Git Bash');
}

/** Reproducible tarball of parent/dirName: sorted, fixed owner and times. */
async function tarGz(parent, dirName, out, epoch) {
  requireGnuTar();
  const tmp = `${out}.tar`;
  run('tar', ['--force-local', '--sort=name', `--mtime=@${epoch}`, '--owner=0', '--group=0', '--numeric-owner', '--format=gnu', '-cf', tmp, '-C', parent, dirName]);
  await gzipFile(tmp, out);
  rmSync(tmp);
}

function printPins(args) {
  const { lock } = { lock: readJson(args.lock) };
  const work = mkdtempSync(join(tmpdir(), 'glide-pins-'));
  try {
    const cores = {};
    for (const id of Object.keys(CORES)) {
      const v = lock.x64?.[id]?.version ?? fail(`lock has no ${id}`);
      cores[id] = { name: CORES[id].name, repo: CORE_REPOS[id], tag: `v${v}`, commit: resolveTag(CORE_REPOS[id], `v${v}`), license: CORE_LICENSES[id] };
    }
    // sing-box names the cronet-go commit its Windows libcronet.dll comes from in .github/CRONET_GO_VERSION.
    const sb = join(work, 'sb');
    fetchRef(CORE_REPOS.singBox, `refs/tags/${cores.singBox.tag}`, sb);
    git(['checkout', '-q', '--detach', 'FETCH_HEAD'], sb);
    const cronetGoCommit = readFileSync(join(sb, '.github', 'CRONET_GO_VERSION'), 'utf8').trim();
    const cg = join(work, 'cg');
    fetchRef('SagerNet/cronet-go', cronetGoCommit, cg, ['--filter=blob:none']);
    const rows = git(['ls-tree', 'FETCH_HEAD', 'naiveproxy', 'lib/windows_amd64/libcronet.dll'], cg).split('\n').map((l) => l.split(/\s+/));
    const naive = rows.find((r) => r[3] === 'naiveproxy')?.[2] ?? fail('cronet-go has no naiveproxy submodule at that commit');
    const blob = rows.find((r) => r[3] === 'lib/windows_amd64/libcronet.dll')?.[2] ?? fail('cronet-go has no windows_amd64 libcronet.dll');
    console.log(JSON.stringify({
      _comment: 'Exact upstream source commits for the cores in cores.lock.json. Regenerate with `node scripts/source/make-source-bundle.mjs --print-pins` after a core bump and review every commit against the upstream release page.',
      cores,
      libcronet: {
        license: 'BSD-3-Clause (Chromium); Go bindings SagerNet/cronet-go are GPL-3.0',
        cronetGoRepo: 'SagerNet/cronet-go', cronetGoCommit,
        naiveproxyRepo: 'SagerNet/naiveproxy', naiveproxyCommit: naive,
        windowsAmd64GitBlob: blob,
      },
    }, null, 2));
  } finally { rmSync(work, { recursive: true, force: true }); }
}

async function main() {
  const args = parseArgs(process.argv.slice(2));
  if (args.printPins) return printPins(args);
  for (const k of ['out', 'tag', 'commit']) if (!args[k]) fail(`--${k} is required`);
  if (!/^[0-9a-f]{40}$/.test(args.commit)) fail('--commit must be a full 40-character commit');
  if (!/^[0-9A-Za-z][0-9A-Za-z._-]*$/.test(args.tag)) fail('--tag has unexpected characters');
  if (args.release && args.noVendor) fail('--release needs the vendored dependency source; drop --no-vendor');
  const { versions, cores, pins } = loadPins(args.lock, args.pins);
  const out = resolve(args.out);
  mkdirSync(out, { recursive: true });
  const work = mkdtempSync(join(tmpdir(), 'glide-src-'));
  const manifest = { schema: 1, generatedBy: 'scripts/source/make-source-bundle.mjs', glide: {}, cores: [], components: [] };
  try {
    // Glide's own source: exactly the tracked files of the release commit.
    git(['cat-file', '-e', `${args.commit}^{commit}`], args.root);
    const epoch = git(['show', '-s', '--format=%ct', args.commit], args.root);
    const gName = glideArchive(args.tag);
    const gTar = join(work, 'glide.tar');
    git(['archive', '--format=tar', `--prefix=Glide-${args.tag}/`, '-o', gTar, args.commit], args.root);
    await gzipFile(gTar, join(out, gName));
    manifest.glide = { repo: args.repo ?? null, tag: args.tag, commit: args.commit, license: 'GPL-3.0', archive: gName };

    for (const id of Object.keys(CORES)) {
      const pin = cores[id];
      const version = versions[id];
      const dir = join(work, id);
      fetchRef(pin.repo, `refs/tags/${pin.tag}`, dir);
      const got = git(['rev-parse', 'FETCH_HEAD^{commit}'], dir);
      if (got !== pin.commit) fail(`${pin.repo} tag ${pin.tag} now points to ${got}, not the pinned ${pin.commit}; the tag moved or the pin is wrong. Nothing was built from it`);
      git(['checkout', '-q', '--detach', pin.commit], dir);
      if (id === 'singBox') {
        const cg = readFileSync(join(dir, '.github', 'CRONET_GO_VERSION'), 'utf8').trim();
        if (cg !== pins.libcronet.cronetGoCommit) fail(`sing-box ${version} builds libcronet from cronet-go ${cg}, but cores.source.json pins ${pins.libcronet.cronetGoCommit}; re-pin libcronet`);
      }
      for (const need of ['go.mod', 'go.sum']) if (!existsSync(join(dir, need))) fail(`${pin.repo}@${pin.tag} has no ${need}`);
      const license = readdirSync(dir).find((n) => /^(LICENSE|COPYING)/i.test(n)) ?? fail(`${pin.repo}@${pin.tag} has no licence file`);
      rmSync(join(dir, '.git'), { recursive: true, force: true });
      let goVersion = null;
      let prunedBinaries = null;
      if (!args.noVendor) {
        goVersion = run('go', ['version']);
        goVendor(dir);
        prunedBinaries = pruneCronetBinaries(dir);
        if (!existsSync(join(dir, 'vendor', 'modules.txt'))) fail(`go mod vendor made no vendor/modules.txt for ${pin.repo}`);
      }
      const top = `${CORES[id].archive(version).replace(/-source\.tar\.gz$/, '')}`;
      const stage = join(work, `stage-${id}`);
      mkdirSync(stage);
      renameSync(dir, join(stage, top));
      const aName = CORES[id].archive(version);
      await tarGz(stage, top, join(out, aName), epoch);
      manifest.cores.push({
        id, name: CORES[id].name, version, license: pin.license ?? CORE_LICENSES[id], repo: pin.repo, tag: pin.tag, commit: pin.commit,
        binaries: ['x64', 'x86'].map((arch) => ({ arch, releaseAssetSha256: lockSha(args.lock, arch, id) })),
        archive: aName, licenseFile: license, vendored: !args.noVendor, goVersion, prunedPrebuiltBinaries: prunedBinaries,
        buildInfo: id === 'singBox' ? 'release/DEFAULT_BUILD_TAGS, release/LDFLAGS and .github/workflows/build.yml in the archive' : 'the release workflow under .github/ in the archive',
      });
    }

    manifest.components.push(
      { id: 'libcronet.dll', license: pins.libcronet.license, shippedWith: 'sing-box (windows-amd64 only)', builtFrom: pins.libcronet, note: 'Prebuilt in SagerNet/cronet-go; no GPL source obligation applies to the Chromium-licensed library, the pinned commits record its origin. The GPL Go bindings are in the sing-box archive (vendor/).' },
      { id: 'wintun.dll', license: 'WireGuard Prebuilt Binaries License', note: 'Redistributed unmodified under its own licence; no source obligation. See Licenses/cores.' },
    );
    writeFileSync(join(out, manifestName(args.tag)), `${JSON.stringify(manifest, null, 2)}\n`);
    const names = readdirSync(out).filter((n) => n !== sumsName(args.tag) && statSync(join(out, n)).isFile()).sort();
    writeFileSync(join(out, sumsName(args.tag)), names.map((n) => `${sha256(join(out, n))}  ${n}`).join('\n') + '\n');
    console.log(`source bundle for ${args.tag} written to ${out}:\n  ${names.join('\n  ')}`);
  } finally { rmSync(work, { recursive: true, force: true }); }
}

function lockSha(lockPath, arch, id) { return readJson(lockPath)[arch][id].sha256; }

main().catch((e) => {
  console.error(`${e instanceof SourceError ? 'source bundle FAILED' : 'unexpected error'}: ${e.message}`);
  process.exit(1);
});
