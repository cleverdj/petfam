// Soundtrack for the promo, synthesised from scratch (no samples, nothing to license): a light ukulele-like
// strum over C-G-Am-F, a glockenspiel tune, soft drums, and the sound effects listed in timeline.js.
import fs from 'node:fs';
import { DURATION, sfx } from './timeline.js';

const RATE = 48000;
const BPM = 120, BEAT = 60 / BPM, BAR = BEAT * 4;
const hz = midi => 440 * Math.pow(2, (midi - 69) / 12);

function rng(seed) {
  let s = (seed * 2654435761) >>> 0 || 1;
  return () => { s ^= s << 13; s >>>= 0; s ^= s >>> 17; s ^= s << 5; s >>>= 0; return s / 4294967296; };
}

export function writeSoundtrack(file, lang = 'ko') {
  const n = Math.ceil(DURATION * RATE);
  const L = new Float32Array(n), R = new Float32Array(n);
  const mix = (buf, start, gain, pan = 0) => {
    const i0 = Math.round(start * RATE), gl = gain * Math.min(1, 1 - pan), gr = gain * Math.min(1, 1 + pan);
    for (let i = 0; i < buf.length; i++) { const j = i0 + i; if (j < 0 || j >= n) continue; L[j] += buf[i] * gl; R[j] += buf[i] * gr; }
  };
  const noise = rng(1234);

  // Karplus-Strong plucked string: sounds like a small nylon-string instrument.
  const pluck = (f, dur, bright = 0.5) => {
    const len = Math.round(dur * RATE), period = Math.max(2, Math.round(RATE / f)), ring = new Float32Array(period);
    for (let i = 0; i < period; i++) ring[i] = noise() * 2 - 1;
    const out = new Float32Array(len);
    let p = 0, prev = 0;
    for (let i = 0; i < len; i++) {
      const cur = ring[p], next = ring[(p + 1) % period];
      const v = (cur * (0.5 + bright * 0.25) + next * (0.5 - bright * 0.25)) * 0.996;
      ring[p] = v; p = (p + 1) % period;
      prev = prev * 0.3 + cur * 0.7;
      out[i] = prev * Math.min(1, (len - i) / (RATE * 0.05));
    }
    return out;
  };
  const tone = (f, dur, parts, decay, attack = 0.004) => {
    const len = Math.round(dur * RATE), out = new Float32Array(len);
    for (let i = 0; i < len; i++) {
      const t = i / RATE, env = Math.min(1, t / attack) * Math.exp(-t / decay) * Math.min(1, (len - i) / (RATE * 0.02));
      let v = 0; for (const [m, a] of parts) v += Math.sin(2 * Math.PI * f * m * t) * a;
      out[i] = v * env;
    }
    return out;
  };
  const glock = (f, dur = 1.2) => tone(f, dur, [[1, 1], [2.76, 0.28], [5.4, 0.1]], 0.38);
  const bass = (f, dur) => tone(f, dur, [[1, 1], [2, 0.25], [3, 0.08]], 0.35, 0.01);
  const kick = () => {
    const len = Math.round(0.3 * RATE), out = new Float32Array(len); let ph = 0;
    for (let i = 0; i < len; i++) { const t = i / RATE; ph += 2 * Math.PI * (48 + 90 * Math.exp(-t / 0.03)) / RATE; out[i] = Math.sin(ph) * Math.exp(-t / 0.09); }
    return out;
  };
  const hiss = (dur, decay, hp = 0.9, lp = 1) => {
    const len = Math.round(dur * RATE), out = new Float32Array(len); let a = 0, b = 0;
    for (let i = 0; i < len; i++) {
      const x = noise() * 2 - 1; a = a + lp * (x - a); const h = a - b; b = b + (1 - hp) * (a - b);
      out[i] = h * Math.exp(-(i / RATE) / decay);
    }
    return out;
  };
  const sweep = (f0, f1, dur, decay, wave = Math.sin) => {
    const len = Math.round(dur * RATE), out = new Float32Array(len); let ph = 0;
    for (let i = 0; i < len; i++) { const t = i / RATE, k = t / dur; ph += 2 * Math.PI * (f0 * Math.pow(f1 / f0, k)) / RATE; out[i] = wave(ph) * Math.exp(-t / decay) * Math.min(1, t / 0.004); }
    return out;
  };

  // ------------------------------------------------------------------------------------------- music
  const chords = [[60, 64, 67], [55, 59, 62, 67], [57, 60, 64], [53, 57, 60, 65]];
  const roots = [36, 43, 45, 41];
  const tune = [
    [76, null, 79, null, 81, 79, 76, null], [74, null, 79, null, 71, null, 74, null],
    [72, null, 76, null, 81, null, 79, 76], [77, null, 81, null, 79, null, null, null],
    [79, null, 76, null, 72, null, 76, 79], [86, null, 83, null, 79, null, 81, 83],
    [84, null, 81, null, 76, null, 81, null], [79, null, 77, null, 76, null, 74, null],
  ];
  const strum = [1, 0, 0.6, 0.8, 0, 0.7, 0.9, 0.6];
  const bars = Math.ceil(DURATION / BAR);
  const musicEnd = 46.0;
  for (let bar = 0; bar < bars; bar++) {
    const t0 = bar * BAR;
    if (t0 >= musicEnd) break;
    const c = chords[bar % 4];
    const intro = t0 < 6, quiet = t0 >= 34 && t0 < 38;
    strum.forEach((a, s) => {
      if (!a || (intro && s % 4)) return;
      const ts = t0 + s * BEAT / 2;
      if (ts >= musicEnd) return;
      c.forEach((m, k) => mix(pluck(hz(m + 12), 1.4, 0.4), ts + k * 0.012 * (s % 2 ? -1 : 1) + (s % 2 ? 0.03 : 0), 0.16 * a * (intro ? 0.7 : 1), -0.25));
    });
    if (!intro) {
      for (const b of [0, 2, 3]) if (t0 + b * BEAT < musicEnd) mix(bass(hz(roots[bar % 4] + (b === 3 ? 7 : 0)), BEAT * 1.6), t0 + b * BEAT, 0.34);
      for (let b = 0; b < 4; b++) {
        const tb = t0 + b * BEAT;
        if (tb >= musicEnd - 0.1) continue;
        if (b % 2 === 0) mix(kick(), tb, quiet ? 0.25 : 0.5);
        else mix(hiss(0.18, 0.06, 0.5, 0.6), tb, quiet ? 0.08 : 0.16, 0.1);
        mix(hiss(0.05, 0.015, 0.2), tb + BEAT / 2, 0.07, 0.3);
      }
      const line = tune[(bar - 3 + 8) % 8];
      line.forEach((m, s) => { if (m && t0 + s * BEAT / 2 < musicEnd) mix(glock(hz(m)), t0 + s * BEAT / 2, quiet ? 0.1 : 0.17, 0.3); });
    } else if (bar >= 1) {
      tune[(bar + 3) % 8].forEach((m, s) => { if (m && s % 2 === 0) mix(glock(hz(m)), t0 + s * BEAT / 2, 0.12, 0.3); });
    }
  }
  // Last chord rings out under the end card.
  [48, 60, 64, 67, 72].forEach((m, k) => mix(pluck(hz(m + 12), 3.8, 0.3), musicEnd + k * 0.03, 0.16));
  mix(glock(hz(84), 3), musicEnd + 0.15, 0.2, 0.3);

  // ------------------------------------------------------------------------------------------- effects
  const fx = {
    pop: () => sweep(420, 980, 0.12, 0.05),
    tick: () => hiss(0.03, 0.006, 0.05),
    click: () => sweep(1600, 900, 0.05, 0.012),
    whoosh: () => { const h = hiss(0.9, 10, 0.7, 0.25); for (let i = 0; i < h.length; i++) { const k = i / h.length; h[i] *= Math.sin(Math.PI * k) ** 2; } return h; },
    scan: () => sweep(500, 1800, 1.0, 0.8, x => Math.sin(x) * 0.6 + Math.sin(x * 2) * 0.2),
    splash: () => { const h = hiss(0.7, 0.18, 0.4, 0.5); const b = sweep(300, 900, 0.25, 0.08); for (let i = 0; i < b.length; i++) h[i] += b[i] * 0.4; return h; },
    'splash-small': () => hiss(0.25, 0.06, 0.4, 0.6),
    shake: () => { const h = hiss(0.9, 10, 0.5, 0.5); for (let i = 0; i < h.length; i++) h[i] *= 0.5 + 0.5 * Math.sin(i / RATE * 2 * Math.PI * 18); return h; },
    thud: () => sweep(140, 55, 0.35, 0.1),
    sparkle: () => { const o = new Float32Array(Math.round(1.4 * RATE)); [84, 88, 91, 96].forEach((m, k) => { const g = glock(hz(m), 1.0); for (let i = 0; i < g.length; i++) { const j = i + Math.round(k * 0.07 * RATE); if (j < o.length) o[j] += g[i] * 0.6; } }); return o; },
    tada: () => { const o = new Float32Array(Math.round(2 * RATE)); [72, 76, 79, 84, 88].forEach((m, k) => { const g = glock(hz(m), 1.5); for (let i = 0; i < g.length; i++) { const j = i + Math.round(k * 0.06 * RATE); if (j < o.length) o[j] += g[i] * 0.55; } }); return o; },
    chime: () => { const a = tone(hz(84), 2.5, [[1, 1], [2.0, 0.35], [3.01, 0.15]], 0.9); const b = tone(hz(91), 2.5, [[1, 1], [2.0, 0.3]], 0.8); for (let i = 0; i < a.length; i++) a[i] = a[i] * 0.6 + b[i] * 0.4; return a; },
  };
  const gains = { pop: 0.22, tick: 0.18, click: 0.3, whoosh: 0.35, scan: 0.1, splash: 0.5, 'splash-small': 0.3, shake: 0.22, thud: 0.6, sparkle: 0.35, tada: 0.4, chime: 0.35 };
  for (const e of sfx(lang)) mix(fx[e.k](), e.t, gains[e.k], e.k === 'pop' ? 0.15 : 0);

  // Rain on the desktop.
  {
    const t0 = 34.0, t1 = 37.4, h = hiss(t1 - t0, 1e9, 0.6, 0.35);
    for (let i = 0; i < h.length; i++) { const t = i / RATE; h[i] *= Math.min(1, t / 0.6, (t1 - t0 - t) / 0.8); }
    mix(h, t0, 0.22);
  }

  // ------------------------------------------------------------------------------------------- master
  let peak = 0;
  for (let i = 0; i < n; i++) {
    const t = i / RATE, env = Math.min(1, t / 0.4) * Math.min(1, Math.max(0, (DURATION - t) / 1.2));
    L[i] = Math.tanh(L[i] * 1.1) * env; R[i] = Math.tanh(R[i] * 1.1) * env;
    peak = Math.max(peak, Math.abs(L[i]), Math.abs(R[i]));
  }
  const g = 0.89 / (peak || 1);
  const data = Buffer.alloc(n * 4);
  for (let i = 0; i < n; i++) {
    data.writeInt16LE(Math.round(clampS(L[i] * g) * 32767), i * 4);
    data.writeInt16LE(Math.round(clampS(R[i] * g) * 32767), i * 4 + 2);
  }
  const head = Buffer.alloc(44);
  head.write('RIFF', 0); head.writeUInt32LE(36 + data.length, 4); head.write('WAVE', 8);
  head.write('fmt ', 12); head.writeUInt32LE(16, 16); head.writeUInt16LE(1, 20); head.writeUInt16LE(2, 22);
  head.writeUInt32LE(RATE, 24); head.writeUInt32LE(RATE * 4, 28); head.writeUInt16LE(4, 32); head.writeUInt16LE(16, 34);
  head.write('data', 36); head.writeUInt32LE(data.length, 40);
  fs.writeFileSync(file, Buffer.concat([head, data]));
  return file;
}
const clampS = x => Math.max(-1, Math.min(1, x));

if (process.argv[1]?.endsWith('audio.mjs')) {
  const lang = process.argv.includes('en') ? 'en' : 'ko';
  console.log(writeSoundtrack(`out/soundtrack-${lang}.wav`, lang));
}
