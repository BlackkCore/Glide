#!/usr/bin/env node
// Final release gate: reads a release's asset names (one per line, on stdin or from --names <file>) and fails when
// binaries are there without every source asset for the pinned cores.
//   gh release view "$TAG" --json assets --jq '.assets[].name' | node scripts/source/check-release-assets.mjs --tag "$TAG" --require-binaries
import { readFileSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { loadPins, missingAssets } from './lib.mjs';

const a = {};
for (let i = 2; i < process.argv.length; i++) {
  const k = process.argv[i];
  if (k === '--require-binaries') a.requireBinaries = true; else a[k.slice(2)] = process.argv[++i];
}
try {
  if (!a.tag) throw new Error('--tag is required');
  const root = resolve(a.root ?? '.');
  const { versions } = loadPins(join(root, 'cores.lock.json'), join(root, 'cores.source.json'));
  const names = readFileSync(a.names ?? 0, 'utf8').split('\n').map((s) => s.trim()).filter(Boolean);
  const missing = missingAssets(a.tag, versions, names, a.requireBinaries);
  if (missing.length) {
    console.error(`release ${a.tag} is missing assets:\n - ${missing.join('\n - ')}\nBinaries must not be published without their corresponding source.`);
    process.exit(1);
  }
  console.log(`release ${a.tag}: all ${names.length} assets present, sources match the pinned cores`);
} catch (e) { console.error(`release asset check error: ${e.message}`); process.exit(1); }
