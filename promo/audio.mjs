// Soundtrack for the promo, synthesised from scratch (no samples, nothing to license): an original waltz for
// piano and strings, and the sound effects listed in timeline.js.
import fs from 'node:fs';
import { DURATION, sfx } from './timeline.js';

const RATE = 48000;
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
  // An original waltz in 3/4 for piano and strings: bass on one, chords on two and three, a singing tune on top,
  // and a string section that swells in the second half of each phrase.
  const BEAT3 = 0.42, BAR3 = BEAT3 * 3;
  const piano = (f, dur, vel = 1) => {
    const len = Math.round(dur * RATE), out = new Float32Array(len);
    const parts = [1, 2, 3, 4, 5, 6, 7].map(n => [n * Math.sqrt(1 + 0.0004 * n * n), Math.pow(n, -1.25) * (n === 1 ? 1 : 0.7), 1.8 * Math.pow(n, -0.7) * Math.min(1, 300 / f)]);
    for (let i = 0; i < len; i++) {
      const t = i / RATE;
      let v = 0;
      for (const [m, a, d] of parts) v += Math.sin(2 * Math.PI * f * m * t) * a * Math.exp(-t / d);
      out[i] = v * vel * Math.min(1, t / 0.003) * Math.min(1, (len - i) / (RATE * 0.08));
    }
    return out;
  };
  const SAW = new Float32Array(2048);
  for (let i = 0; i < 2048; i++) { let v = 0; for (let n = 1; n <= 14; n++) v += Math.sin(2 * Math.PI * n * i / 2048) / n * (n % 2 ? 1 : 0.6); SAW[i] = v * 0.55; }
  const strings = (f, dur, vel = 1, attack = 0.45) => {
    const len = Math.round(dur * RATE), out = new Float32Array(len);
    const det = [0.996, 1, 1.004];
    const ph = [0, 0.33, 0.71];
    let lp = 0;
    for (let i = 0; i < len; i++) {
      const t = i / RATE, vib = 1 + 0.0035 * Math.sin(2 * Math.PI * 5.2 * t);
      let v = 0;
      for (let k = 0; k < 3; k++) { ph[k] = (ph[k] + f * det[k] * vib / RATE) % 1; v += SAW[(ph[k] * 2048) | 0]; }
      lp += 0.08 * (v / 3 - lp);
      out[i] = lp * vel * Math.min(1, t / attack) * Math.min(1, (len - i) / (RATE * 0.5));
    }
    return out;
  };
  // [bass, chord tones] per bar; a 16-bar phrase in C.
  const CH = [
    [41, [57, 60, 64]], [43, [59, 62, 64]], [40, [55, 59, 62]], [45, [55, 59, 60]],
    [38, [53, 57, 60, 64]], [43, [53, 55, 60]], [36, [55, 59, 62, 64]], [45, [55, 61, 64]],
    [41, [57, 60, 64]], [43, [55, 59, 62]], [40, [55, 59, 62]], [45, [57, 60, 64]],
    [38, [53, 57, 60]], [43, [53, 59, 62]], [36, [55, 60, 64]], [36, [55, 60, 64]],
  ];
  // [beat, note, beats] per bar.
  const TUNE = [
    [[0, 72, 1], [1, 76, 1], [2, 79, 1]], [[0, 81, 2], [2, 79, 1]], [[0, 79, 1.5], [1.5, 76, 0.5], [2, 74, 1]], [[0, 76, 3]],
    [[0, 77, 1], [1, 76, 1], [2, 74, 1]], [[0, 72, 1], [1, 74, 2]], [[0, 76, 1.5], [1.5, 79, 0.5], [2, 83, 1]], [[0, 81, 3]],
    [[0, 81, 1], [1, 84, 1], [2, 83, 1]], [[0, 81, 2], [2, 79, 1]], [[0, 79, 1], [1, 83, 1], [2, 79, 1]], [[0, 76, 3]],
    [[0, 77, 1.5], [1.5, 76, 0.5], [2, 74, 1]], [[0, 74, 1], [1, 77, 1], [2, 71, 1]], [[0, 72, 3]], [],
  ];
  const BARS = 36; // the waltz ends on the last chord as the end card appears
  const musicEnd = BARS * BAR3;
  for (let bar = 0; bar < BARS; bar++) {
    const t0 = bar * BAR3;
    let idx = (bar - 4 + 16) % 16;
    if (bar === BARS - 2) idx = 12;
    if (bar === BARS - 1) idx = 13;
    const [bass, chord] = CH[idx];
    const intro = bar < 4, quiet = t0 >= 33.6 && t0 < 37.2, swell = !intro && idx >= 8;
    const lh = intro ? 0.45 : quiet ? 0.5 : 0.75;
    mix(piano(hz(bass), BAR3 * 1.2, 0.9 * lh), t0, 0.32, -0.15);
    mix(piano(hz(bass + 12), BAR3 * 1.2, 0.35 * lh), t0, 0.32, -0.15);
    for (const b of [1, 2]) chord.forEach((m, k) => mix(piano(hz(m), BEAT3 * 1.6, 0.55 * lh), t0 + b * BEAT3 + k * 0.006, 0.17, -0.1));
    if (intro) {
      chord.forEach((m, k) => mix(piano(hz(m + 12), BAR3, 0.5), t0 + k * BEAT3 * 0.75 + BEAT3 * 0.1, 0.12, 0.25));
    } else {
      for (const [b, m, d] of TUNE[idx]) mix(piano(hz(m), d * BEAT3 + 0.9, quiet ? 0.6 : 1), t0 + b * BEAT3, 0.3, 0.2);
      if (swell && t0 > 10) for (const [b, m, d] of TUNE[idx]) mix(strings(hz(m - 12), d * BEAT3 + 0.25, 1, 0.12), t0 + b * BEAT3, quiet ? 0.05 : 0.11, 0.1);
    }
    const pad = intro ? 0.05 : quiet ? 0.05 : swell ? 0.11 : 0.075;
    chord.forEach(m => mix(strings(hz(m - 12), BAR3 + 0.5), t0, pad, 0));
    mix(strings(hz(bass), BAR3 + 0.5), t0, pad * 0.9, 0);
  }
  // Final chord under the end card.
  [36, 48, 55, 59, 64, 67, 71].forEach((m, k) => mix(piano(hz(m), 4.5, 0.8), musicEnd + k * 0.07, 0.2, k % 2 ? 0.2 : -0.2));
  [48, 55, 59, 64].forEach(m => mix(strings(hz(m), 4.2, 1, 0.6), musicEnd, 0.08));
  mix(piano(hz(84), 3.5, 0.7), musicEnd + 0.6, 0.18, 0.3);

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
  const gains = { pop: 0.22, tick: 0.18, click: 0.3, whoosh: 0.22, scan: 0.1, splash: 0.5, 'splash-small': 0.3, shake: 0.22, thud: 0.6, sparkle: 0.35, tada: 0.4, chime: 0.25 };
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
