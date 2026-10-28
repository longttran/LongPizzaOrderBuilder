#!/usr/bin/env node
// Checks that docs/INVARIANTS.md and the automated tests agree.
//   1. every `enforced` invariant is referenced by at least one test
//   2. every invariant ID used in a test exists in the registry (catches typos)
//   3. a `planned` invariant that a test already references must be promoted to `enforced`
//   4. IDs are unique and statuses are valid
// No dependencies. Run: node scripts/check-invariants.mjs
import { readFileSync, readdirSync, statSync } from 'node:fs';
import { join, relative, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const PREFIXES = 'PRC|ORD|API|UI|LIFE|EVT';
const idPattern = new RegExp(`\\b(?:${PREFIXES})-\\d{3}\\b`, 'g');

function walk(dir, accept, found = []) {
  for (const name of readdirSync(dir)) {
    if (['node_modules', 'bin', 'obj', 'dist', '.git'].includes(name)) continue;
    const path = join(dir, name);
    if (statSync(path).isDirectory()) walk(path, accept, found);
    else if (accept(path)) found.push(path);
  }
  return found;
}

const errors = [];
const registry = new Map();
const lines = readFileSync(join(root, 'docs/INVARIANTS.md'), 'utf8').split(/\r?\n/);
for (let i = 0; i < lines.length; i++) {
  const heading = lines[i].match(new RegExp(`^### ((?:${PREFIXES})-\\d{3}) · (.+)$`));
  if (!heading) continue;
  const [, id, title] = heading;
  const status = lines.slice(i + 1, i + 4).map(l => l.match(/^\*\*Status:\*\* (\w+)\s*$/)).find(Boolean)?.[1];
  if (registry.has(id)) errors.push(`${id} is defined more than once.`);
  if (status !== 'enforced' && status !== 'planned') errors.push(`${id} needs a "**Status:** enforced|planned" line (found: ${status ?? 'none'}).`);
  registry.set(id, { title, status, tests: new Set() });
}

const testFiles = [
  ...walk(join(root, 'tests'), p => p.endsWith('.cs')),
  ...walk(join(root, 'src/frontend/src'), p => /\.test\.tsx?$/.test(p)),
];
for (const file of testFiles) {
  for (const id of new Set(readFileSync(file, 'utf8').match(idPattern) ?? [])) {
    const entry = registry.get(id);
    if (!entry) errors.push(`${relative(root, file)} references ${id}, which is not in docs/INVARIANTS.md.`);
    else entry.tests.add(relative(root, file));
  }
}

for (const [id, { title, status, tests }] of registry) {
  if (status === 'enforced' && tests.size === 0) errors.push(`${id} (${title}) is "enforced" but no test references it.`);
  if (status === 'planned' && tests.size > 0) errors.push(`${id} (${title}) is "planned" but is referenced by ${[...tests].join(', ')}. Mark it enforced.`);
}

const count = s => [...registry.values()].filter(e => e.status === s).length;
console.log(`Invariants: ${registry.size} total, ${count('enforced')} enforced, ${count('planned')} planned. Test files scanned: ${testFiles.length}.`);
if (errors.length) {
  console.error(`\n${errors.length} problem(s):\n` + errors.map(e => `  - ${e}`).join('\n'));
  process.exit(1);
}
console.log('OK: registry and tests agree.');
