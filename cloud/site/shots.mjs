// Encodes the PNGs that SiteScreenshotTests rendered into the .webp files index.html serves.
// Run: node cloud/site/shots.mjs   (after running the Daynote.Desktop.Tests SiteScreenshotTests)
//
// The screenshots are rendered by a test rather than captured by hand, for the reason written up
// in SiteScreenshotTests: the hand-captured set rotted for two months and still advertised the
// clipboard drawer, deleted in August. The test writes PNG because that is what Avalonia writes;
// this turns it into what the page asks for. Keeping the two apart means a redesign only needs the
// test rerun, and nothing here knows what the app looks like.
import { readdirSync, statSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { createRequire } from 'node:module';

const here = dirname(fileURLToPath(import.meta.url));

// sharp is a dependency of the worker next door, not of this folder, so plain `import 'sharp'`
// would not resolve: node walks up from cloud/site and never looks inside cloud/worker.
const require = createRequire(join(here, '..', 'worker', 'package.json'));
const sharp = require('sharp');

const shots = join(here, 'shots');
const out = join(here, 'public', 'img');

// The sizes index.html declares on each <img>. A mismatch is a layout shift, so they are asserted
// rather than resized into: a wrong size here means the test rendered the wrong thing.
const EXPECTED = {
  'hero-ko': [1046, 714],
  'hero-en': [1046, 714],
  'shot-ko-01-overview': [1200, 675],
  'shot-ko-03-files': [1200, 675],
  'shot-ko-04-shortcuts': [1200, 675],
  'shot-en-01-overview': [1200, 675],
  'shot-en-03-files': [1200, 675],
  'shot-en-04-shortcuts': [1200, 675],
};

let names;
try {
  names = readdirSync(shots).filter((f) => f.endsWith('.png'));
} catch {
  console.error(`No ${shots}. Run the SiteScreenshotTests in Daynote.Desktop.Tests first.`);
  process.exit(1);
}

const missing = Object.keys(EXPECTED).filter((n) => !names.includes(`${n}.png`));
if (missing.length) {
  console.error(`Missing renders: ${missing.join(', ')}. Run both SiteScreenshotTests methods.`);
  process.exit(1);
}

for (const name of names) {
  const base = name.replace(/\.png$/, '');
  const expected = EXPECTED[base];
  if (!expected) {
    console.warn(`skip ${name} — not an image the page serves`);
    continue;
  }

  const source = join(shots, name);
  const image = sharp(source);
  const { width, height } = await image.metadata();
  if (width !== expected[0] || height !== expected[1]) {
    console.error(`${name} is ${width}x${height}; index.html declares ${expected.join('x')}.`);
    process.exit(1);
  }

  const target = join(out, `${base}.webp`);
  // Lossy at 82 holds the UI's text edges and lands near the sizes the hand-made set shipped at.
  await image.webp({ quality: 82, effort: 6 }).toFile(target);
  const kb = Math.round(statSync(target).size / 1024);
  console.log(`${base}.webp  ${width}x${height}  ${kb} KB`);
}
