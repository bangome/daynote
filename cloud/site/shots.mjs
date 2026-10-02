// Encodes the PNGs that SiteScreenshotTests rendered into the .webp files index.html serves,
// and the phone screens of the mobile section from the App Store set (see below).
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

// ── Phone screens for the mobile section ──
// These come from the App Store set in docs/brand/app-store rather than from a test of their own:
// that set is already rendered from the shipping mobile shell, and the page should show the same
// screens the store listing does. They are 1320x2868 (6.9" iPhone); the page shows them at most
// ~300 CSS px wide, so 640 covers a 2x screen.
const PHONE_SOURCE = join(here, '..', '..', 'docs', 'brand', 'app-store');
const PHONE_SOURCE_SIZE = [1320, 2868];
const PHONE_SIZE = [640, 1391]; // what index.html declares; 2868 * 640 / 1320 rounds to 1391
const PHONES = ['01-day', '02-note', '05-dark'];

for (const lang of ['ko', 'en']) {
  for (const shot of PHONES) {
    const source = join(PHONE_SOURCE, lang, `daynote-app-store-${lang}-${shot}.png`);
    const image = sharp(source);
    const { width, height } = await image.metadata();
    if (width !== PHONE_SOURCE_SIZE[0] || height !== PHONE_SOURCE_SIZE[1]) {
      console.error(`${source} is ${width}x${height}; expected ${PHONE_SOURCE_SIZE.join('x')}.`);
      process.exit(1);
    }
    const target = join(out, `phone-${lang}-${shot}.webp`);
    const info = await image.resize({ width: PHONE_SIZE[0] }).webp({ quality: 82, effort: 6 }).toFile(target);
    if (info.width !== PHONE_SIZE[0] || info.height !== PHONE_SIZE[1]) {
      console.error(`${target} came out ${info.width}x${info.height}; index.html declares ${PHONE_SIZE.join('x')}.`);
      process.exit(1);
    }
    console.log(`phone-${lang}-${shot}.webp  ${info.width}x${info.height}  ${Math.round(info.size / 1024)} KB`);
  }
}

// ── Desktop screens (hero and the screens strip) ──

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
