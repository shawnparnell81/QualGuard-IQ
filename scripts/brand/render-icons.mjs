#!/usr/bin/env node
// Renders the QualGuard IQ logo SVGs (web/src/assets/brand) into every PNG and ICO the app ships: the mark at 16/32/192/512, the
// maskable and apple-touch icons (mark on white with a safe margin), the full logo at 600/1200 for light and dark, the
// favicon.ico (16/32/48) and the email logo copy. Uses the Chromium that comes with the web tests (Playwright).
// After changing a logo, bump the ?v= number on the icon links in web/index.html and web/public/site.webmanifest so
// browsers fetch the new icons. Run from the repository root after changing an SVG:  node scripts/brand/render-icons.mjs
import { createRequire } from 'node:module';
import { readFileSync, writeFileSync, copyFileSync } from 'node:fs';
import { resolve } from 'node:path';

const root = process.cwd();
const pub = resolve(root, 'web/public');
const svgs = resolve(root, 'web/src/assets/brand');
const { chromium } = createRequire(resolve(root, 'web/package.json'))('playwright');

const browser = await chromium.launch();
const page = await browser.newPage({ deviceScaleFactor: 1 });

/** Draws an SVG file on a w x h canvas (optionally on a solid background, scaled to a share of the canvas) and returns PNG bytes. */
async function render(svgFile, w, h, { background = null, scale = 1 } = {}) {
  const svg = readFileSync(resolve(svgs, svgFile), 'utf8');
  const src = `data:image/svg+xml;base64,${Buffer.from(svg).toString('base64')}`;
  await page.setViewportSize({ width: w, height: h });
  await page.setContent(`<html><body style="margin:0;background:${background ?? 'transparent'}">
    <div style="width:${w}px;height:${h}px;display:flex;align-items:center;justify-content:center">
    <img src="${src}" style="width:${w * scale}px;height:${h * scale}px;display:block"></div></body></html>`);
  await page.locator('img').evaluate((img) => img.decode());
  return page.screenshot({ type: 'png', omitBackground: !background, clip: { x: 0, y: 0, width: w, height: h } });
}

const out = (file, bytes) => { writeFileSync(resolve(pub, file), bytes); console.log(file, bytes.length, 'bytes'); };

for (const n of [16, 32, 192, 512]) out(`brand/mark-${n}.png`, await render('mark.svg', n, n));
for (const n of [192, 512]) out(`brand/mark-maskable-${n}.png`, await render('mark.svg', n, n, { background: '#FFFFFF', scale: 0.7 }));
out('apple-touch-icon.png', await render('mark.svg', 180, 180, { background: '#FFFFFF', scale: 0.745 }));
for (const [suffix, file] of [['', 'logo-full.svg'], ['-dark', 'logo-full-dark.svg']])
  for (const w of [600, 1200]) out(`brand/logo-full${suffix}-${w}.png`, await render(file, w, Math.round((w * 188) / 656)));

// favicon.ico: PNG images inside an ICO directory (supported by every current browser and Windows)
const icons = [];
for (const n of [16, 32, 48]) icons.push({ n, png: await render('mark.svg', n, n) });
const header = Buffer.alloc(6 + 16 * icons.length);
header.writeUInt16LE(0, 0); header.writeUInt16LE(1, 2); header.writeUInt16LE(icons.length, 4);
let offset = header.length;
icons.forEach(({ n, png }, i) => {
  const e = 6 + 16 * i;
  header.writeUInt8(n, e); header.writeUInt8(n, e + 1); header.writeUInt8(0, e + 2); header.writeUInt8(0, e + 3);
  header.writeUInt16LE(1, e + 4); header.writeUInt16LE(32, e + 6); header.writeUInt32LE(png.length, e + 8); header.writeUInt32LE(offset, e + 12);
  offset += png.length;
});
out('favicon.ico', Buffer.concat([header, ...icons.map((i) => i.png)]));

await browser.close();

// the email logo is the light 600 px logo
copyFileSync(resolve(pub, 'brand/logo-full-600.png'), resolve(root, 'src/QualGuardIQ.Infrastructure/Services/Brand/logo-full-600.png'));
console.log('src/QualGuardIQ.Infrastructure/Services/Brand/logo-full-600.png copied');
