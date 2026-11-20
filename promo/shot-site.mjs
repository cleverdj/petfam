// Renders exact frames of the homepage (../site/index.html) to PNGs, to check it without opening a browser.
//   node shot-site.mjs --frames "0:1,0.2:3"         scroll progress : time, at 1440x900 -> out/site/f-0-1.png ...
//   node shot-site.mjs --frames "0:1" --q "view=face" --size 390x844 --tag phone
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import puppeteer from 'puppeteer-core';

const here = path.dirname(fileURLToPath(import.meta.url));
const out = path.join(here, 'out', 'site');
fs.mkdirSync(out, { recursive: true });
const arg = (n, d) => { const i = process.argv.indexOf(n); return i > 0 ? process.argv[i + 1] : d; };
const frames = arg('--frames', '0:1').split(',').map(s => s.split(':').map(Number));
const [w, h] = arg('--size', '1440x900').split('x').map(Number), tag = arg('--tag', 'f'), q = arg('--q', '');
const chrome = process.env.CHROME ?? ['C:/Program Files/Google/Chrome/Application/chrome.exe', '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome', '/usr/bin/google-chrome'].find(p => fs.existsSync(p));
const browser = await puppeteer.launch({ executablePath: chrome, headless: true, args: ['--ignore-gpu-blocklist', '--enable-gpu', '--use-angle=default', '--allow-file-access-from-files', '--hide-scrollbars'] });
try {
  const page = await browser.newPage();
  page.on('pageerror', e => console.log('[page error]', e.message));
  page.on('console', m => { if (m.type() === 'error' || m.type() === 'warn') console.log('[page]', m.text().slice(0, 600)); });
  await page.setViewport({ width: w, height: h, deviceScaleFactor: 1 });
  const t0 = Date.now();
  await page.goto(pathToFileURL(path.join(here, '..', 'site', 'index.html')).href + '?capture=1&' + q, { waitUntil: 'networkidle0', timeout: 120000 });
  await page.waitForFunction('window.__ready', { timeout: 120000 }); await page.evaluate('window.__ready');
  console.log(`loaded in ${((Date.now() - t0) / 1000).toFixed(1)}s`);
  for (const [p, t] of frames) {
    const ms = await page.evaluate((p, t) => {
      const gl = document.getElementById('scene').getContext('webgl2'), px = new Uint8Array(4), sync = () => gl.readPixels(0, 0, 1, 1, gl.RGBA, gl.UNSIGNED_BYTE, px);
      window.__frame(p, t); window.__frame(p, t); sync();
      const a = performance.now(); for (let i = 0; i < 4; i++) { window.__frame(p, t); sync(); } return (performance.now() - a) / 2;
    }, p, t);
    const file = path.join(out, `${tag}-${p}-${t}`.replace(/\./g, '_') + '.png');
    await page.screenshot({ path: file });
    console.log(file, `${(ms / 2).toFixed(1)}ms/frame`);
  }
} finally { await browser.close(); }
