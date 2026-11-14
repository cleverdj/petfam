// Records the promo: serves this folder, opens it in Chrome, steps renderAt(t) frame by frame, and pipes the
// screenshots into ffmpeg together with the soundtrack from audio.mjs.
//
//   node render.mjs                 Korean video  -> out/petfam-promo-ko.mp4
//   node render.mjs --lang en       English video -> out/petfam-promo-en.mp4
//   node render.mjs --stills 3,14   just those moments as PNGs in out/stills (for a quick look)
//   node render.mjs --serve         serve it at http://127.0.0.1:5173/ to watch it live in a browser
import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import { spawn } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import puppeteer from 'puppeteer-core';
import { FPS, DURATION } from './timeline.js';
import { writeSoundtrack } from './audio.mjs';

const here = path.dirname(fileURLToPath(import.meta.url));
const assets = path.join(here, '..', 'src', 'PetFam.App', 'Assets');
const arg = name => { const i = process.argv.indexOf(name); return i > 0 ? process.argv[i + 1] : undefined; };
const lang = arg('--lang') === 'en' ? 'en' : 'ko';
const stills = arg('--stills')?.split(',').map(Number);
const probe = arg('--eval');
const out = path.join(here, 'out');
fs.mkdirSync(out, { recursive: true });

const types = { '.html': 'text/html; charset=utf-8', '.js': 'text/javascript', '.mjs': 'text/javascript', '.jpg': 'image/jpeg', '.png': 'image/png', '.json': 'application/json' };
const server = http.createServer((req, res) => {
  const url = decodeURIComponent(new URL(req.url, 'http://x').pathname);
  let file;
  if (url.startsWith('/wallpapers/')) file = path.join(assets, 'Wallpapers', path.basename(url));
  else if (url === '/icon.png') file = path.join(assets, 'petfam-icon.png');
  else file = path.join(here, url === '/' ? 'index.html' : url);
  if (!file.startsWith(here) && !file.startsWith(assets)) { res.writeHead(403).end(); return; }
  fs.readFile(file, (err, data) => {
    if (err) { res.writeHead(404).end(); return; }
    res.writeHead(200, { 'content-type': types[path.extname(file)] ?? 'application/octet-stream' }).end(data);
  });
});
const serveOnly = process.argv.includes('--serve');
await new Promise(r => server.listen(serveOnly ? 5173 : 0, '127.0.0.1', r));
const port = server.address().port;
if (serveOnly) {
  console.log(`http://127.0.0.1:${port}/   (add ?lang=en for English, ?t=15 to hold one moment; Ctrl+C to stop)`);
  await new Promise(() => {});
}

const chrome = process.env.CHROME ?? [
  'C:/Program Files/Google/Chrome/Application/chrome.exe',
  'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
  '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome',
  '/usr/bin/google-chrome',
].find(p => fs.existsSync(p));
const browser = await puppeteer.launch({
  executablePath: chrome, headless: true,
  args: ['--ignore-gpu-blocklist', '--enable-gpu-rasterization', '--use-angle=default', '--hide-scrollbars', '--force-color-profile=srgb'],
});
const page = await browser.newPage();
page.on('console', m => { if (m.type() === 'error' || m.type() === 'warn') console.log('[page]', m.text()); });
page.on('pageerror', e => console.log('[page error]', e.message));
await page.setViewport({ width: 1920, height: 1080, deviceScaleFactor: 1 });
await page.goto(`http://127.0.0.1:${port}/index.html?capture=1&lang=${lang}`, { waitUntil: 'networkidle0' });
await page.waitForFunction('window.__ready', { timeout: 60000 });
await page.evaluate('window.__ready');

try {
  if (stills) {
    fs.mkdirSync(path.join(out, 'stills'), { recursive: true });
    for (const t of stills) {
      await page.evaluate(t => window.renderAt(t), t);
      const file = path.join(out, 'stills', `${lang}-${String(t).replace('.', '_')}.png`);
      await page.screenshot({ path: file });
      console.log(file);
      if (probe) console.log(await page.evaluate(probe));
    }
  } else {
    const audio = path.join(out, `soundtrack-${lang}.wav`);
    writeSoundtrack(audio, lang);
    const video = path.join(out, `petfam-promo-${lang}.mp4`);
    const ff = spawn('ffmpeg', ['-y', '-loglevel', 'error', '-f', 'image2pipe', '-framerate', String(FPS), '-c:v', 'mjpeg', '-i', '-',
      '-i', audio, '-map', '0:v', '-map', '1:a', '-c:v', 'libx264', '-preset', 'slow', '-crf', '17', '-pix_fmt', 'yuv420p',
      '-c:a', 'aac', '-b:a', '192k', '-movflags', '+faststart', '-shortest', video], { stdio: ['pipe', 'inherit', 'inherit'] });
    const frames = Math.round(DURATION * FPS);
    const started = Date.now();
    for (let i = 0; i < frames; i++) {
      await page.evaluate(t => window.renderAt(t), i / FPS);
      const jpg = await page.screenshot({ type: 'jpeg', quality: 95 });
      if (!ff.stdin.write(jpg)) await new Promise(r => ff.stdin.once('drain', r));
      if (i % 60 === 0) console.log(`frame ${i}/${frames}  ${((Date.now() - started) / 1000).toFixed(0)}s`);
    }
    ff.stdin.end();
    await new Promise((r, j) => ff.on('close', c => (c === 0 ? r() : j(new Error(`ffmpeg exited with ${c}`)))));
    console.log(video);
  }
} finally {
  await browser.close();
  server.close();
}
