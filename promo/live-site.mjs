// Opens the homepage the way a visitor does (no capture mode), scrolls through it and saves what is on screen.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import puppeteer from 'puppeteer-core';
const here = path.dirname(fileURLToPath(import.meta.url)), out = path.join(here, 'out', 'site');
fs.mkdirSync(out, { recursive: true });
const chrome = process.env.CHROME ?? ['C:/Program Files/Google/Chrome/Application/chrome.exe', '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome', '/usr/bin/google-chrome'].find(p => fs.existsSync(p));
const browser = await puppeteer.launch({ executablePath: chrome, headless: true, args: ['--ignore-gpu-blocklist', '--enable-gpu', '--use-angle=default', '--allow-file-access-from-files', '--hide-scrollbars'] });
try {
  const page = await browser.newPage();
  page.on('pageerror', e => console.log('[page error]', e.message));
  await page.setViewport({ width: 1440, height: 900, deviceScaleFactor: 1 });
  await page.goto(pathToFileURL(path.join(here, '..', 'site', 'index.html')).href, { waitUntil: 'networkidle0', timeout: 120000 });
  await page.evaluate('window.__ready');
  await new Promise(r => setTimeout(r, 1500));
  console.log('loader hidden:', await page.evaluate(() => getComputedStyle(document.getElementById('loader')).visibility));
  await page.mouse.move(700, 500);
  for (const [i, f] of [[0, 0], [1, 0.43], [2, 1]]) {
    await page.evaluate(f => scrollTo(0, (document.documentElement.scrollHeight - innerHeight) * f), f);
    await new Promise(r => setTimeout(r, 2600));
    if (i === 1) { await page.mouse.click(560, 520); await new Promise(r => setTimeout(r, 250)); }
    await page.screenshot({ path: path.join(out, `live-${i}.png`) });
  }
  const fps = await page.evaluate(() => new Promise(res => { let n = 0; const t0 = performance.now(); const f = () => { n++; performance.now() - t0 < 2000 ? requestAnimationFrame(f) : res(n / 2); }; requestAnimationFrame(f); }));
  console.log('frames per second (headless):', fps);
} finally { await browser.close(); }
