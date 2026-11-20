// Screenshots of the homepage (../site) at a few scroll positions, to check it without opening a browser.
//   node shot-site.mjs            -> out/site/*.png
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import puppeteer from 'puppeteer-core';

const here = path.dirname(fileURLToPath(import.meta.url));
const out = path.join(here, 'out', 'site');
fs.mkdirSync(out, { recursive: true });
const chrome = process.env.CHROME ?? ['C:/Program Files/Google/Chrome/Application/chrome.exe', '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome', '/usr/bin/google-chrome'].find(p => fs.existsSync(p));
const browser = await puppeteer.launch({ executablePath: chrome, headless: true, args: ['--ignore-gpu-blocklist', '--allow-file-access-from-files'] });
try {
  for (const [name, w, h] of [['desktop', 1440, 900], ['phone', 390, 844]]) {
    const page = await browser.newPage();
    page.on('pageerror', e => console.log('[page error]', e.message));
    page.on('console', m => { if (m.type() === 'error') console.log('[page]', m.text()); });
    await page.setViewport({ width: w, height: h, deviceScaleFactor: 1 });
    await page.goto(pathToFileURL(path.join(here, '..', 'site', 'index.html')).href + '?', { waitUntil: 'networkidle0' });
    const n = await page.evaluate(() => document.querySelectorAll('main section').length);
    for (let i = 0; i < n; i++) {
      if (name === 'phone' && ![0, 2, 7].includes(i)) continue;
      await page.evaluate(i => { document.documentElement.style.scrollBehavior = 'auto'; const el = document.querySelectorAll('main section')[i]; scrollTo(0, el.offsetTop + el.offsetHeight / 2 - innerHeight / 2); }, i);
      await new Promise(r => setTimeout(r, 1800));
      await page.screenshot({ path: path.join(out, `${name}-${i}.png`) });
    }
    await page.close();
  }
  console.log(out);
} finally { await browser.close(); }
