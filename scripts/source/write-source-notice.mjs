#!/usr/bin/env node
// Writes Licenses/SOURCE.txt for the release zip (and so the installer): where the corresponding source is.
//   node scripts/source/write-source-notice.mjs --tag <tag> --commit <sha> --repo owner/name --out <file>
import { writeFileSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { CORES, glideArchive, loadPins, manifestName, sumsName } from './lib.mjs';

export function noticeText({ tag, commit, repo, versions, cores }) {
  const page = `https://github.com/${repo}/releases/tag/${tag}`;
  const lines = [
    `Glide ${tag} - where the source code is`,
    '',
    `This package was built from commit ${commit} of https://github.com/${repo}.`,
    `The corresponding source for the GPL-licensed programs in it (and for Xray-core, MPL-2.0) is on the release page of the version you downloaded:`,
    `  ${page}`,
    '',
    `  ${glideArchive(tag)}`,
    '      Glide itself (GPL-3.0), with the build scripts, workflows and dependency lists.',
    ...Object.keys(CORES).flatMap((id) => [`  ${CORES[id].archive(versions[id])}`, `      ${CORES[id].name} ${versions[id]} (${cores[id].license}), upstream tag ${cores[id].tag}, commit ${cores[id].commit}, with all Go dependencies in vendor/.`]),
    `  ${manifestName(tag)}, ${sumsName(tag)}`,
    '      The exact origin of every archive and its SHA-256 checksum.',
    '',
    'The source files stay on that page as long as these binaries are offered there.',
    'Other bundled components (fonts, libraries, Wintun, libcronet, data files) and their licences are listed in THIRD_PARTY_NOTICES.md, in this folder.',
    '',
  ];
  return lines.join('\r\n');
}

if (process.argv[1]?.endsWith('write-source-notice.mjs')) {
  const a = {};
  for (let i = 2; i < process.argv.length; i += 2) a[process.argv[i].slice(2)] = process.argv[i + 1];
  try {
    for (const k of ['tag', 'commit', 'repo', 'out']) if (!a[k]) throw new Error(`--${k} is required`);
    if (!/^[0-9a-f]{40}$/.test(a.commit) || !/^[\w.-]+\/[\w.-]+$/.test(a.repo)) throw new Error('--commit or --repo is malformed');
    const root = resolve(a.root ?? '.');
    const { versions, cores } = loadPins(join(root, 'cores.lock.json'), join(root, 'cores.source.json'));
    writeFileSync(a.out, noticeText({ ...a, versions, cores }));
    console.log(`wrote ${a.out}`);
  } catch (e) { console.error(`source notice error: ${e.message}`); process.exit(1); }
}
