// Score and sound for the short film, synthesised from scratch: one theme at 80 bpm (a bar is three seconds, a
// world is four bars) arranged differently for every world, plus ambience and the effects in film-timeline.js.
import fs from 'node:fs';
import { DURATION, sfx, AMBIENCE } from './film-timeline.js';

const RATE = 48000, BEAT = 0.75, BAR = 3, E8 = BEAT / 2;
const hz = m => 440 * Math.pow(2, (m - 69) / 12);
function rng(seed) {
  let s = (seed * 2654435761) >>> 0 || 1;
  return () => { s ^= s << 13; s >>>= 0; s ^= s >>> 17; s ^= s << 5; s >>>= 0; return s / 4294967296; };
}

export function writeSoundtrack(file) {
  const n = Math.ceil(DURATION * RATE), L = new Float32Array(n), R = new Float32Array(n);
  const mix = (buf, start, gain, pan = 0) => {
    const i0 = Math.round(start * RATE), gl = gain * Math.min(1, 1 - pan), gr = gain * Math.min(1, 1 + pan);
    for (let i = 0; i < buf.length; i++) { const j = i0 + i; if (j < 0 || j >= n) continue; L[j] += buf[i] * gl; R[j] += buf[i] * gr; }
  };
  const noise = rng(4321);

  // ---------------------------------------------------------------------------------------- instruments
  const piano = (f, dur, vel = 1, soft = 1) => {
    const len = Math.round(dur * RATE), out = new Float32Array(len);
    const parts = [1, 2, 3, 4, 5, 6].map(k => [k * Math.sqrt(1 + 0.0004 * k * k), Math.pow(k, -1.25 * soft) * (k === 1 ? 1 : 0.7), 1.8 * Math.pow(k, -0.7) * Math.min(1, 300 / f)]);
    for (let i = 0; i < len; i++) { const t = i / RATE; let v = 0; for (const [m, a, d] of parts) v += Math.sin(2 * Math.PI * f * m * t) * a * Math.exp(-t / d); out[i] = v * vel * Math.min(1, t / 0.003) * Math.min(1, (len - i) / (RATE * 0.08)); }
    return out;
  };
  const harp = (f, dur, vel = 1) => {
    const len = Math.round(dur * RATE), out = new Float32Array(len);
    for (let i = 0; i < len; i++) { const t = i / RATE; out[i] = (Math.sin(2 * Math.PI * f * t) + 0.4 * Math.sin(4 * Math.PI * f * t) * Math.exp(-t / 0.25) + 0.15 * Math.sin(6 * Math.PI * f * t) * Math.exp(-t / 0.12)) * Math.exp(-t / 0.7) * vel * Math.min(1, t / 0.002) * Math.min(1, (len - i) / (RATE * 0.05)); }
    return out;
  };
  const glock = (f, dur = 1.6, vel = 1) => {
    const len = Math.round(dur * RATE), out = new Float32Array(len);
    for (let i = 0; i < len; i++) { const t = i / RATE; out[i] = (Math.sin(2 * Math.PI * f * t) + 0.28 * Math.sin(2 * Math.PI * f * 2.76 * t) * Math.exp(-t / 0.2) + 0.1 * Math.sin(2 * Math.PI * f * 5.4 * t) * Math.exp(-t / 0.08)) * Math.exp(-t / 0.5) * vel * Math.min(1, t / 0.002) * Math.min(1, (len - i) / (RATE * 0.05)); }
    return out;
  };
  const SAW = new Float32Array(2048);
  for (let i = 0; i < 2048; i++) { let v = 0; for (let k = 1; k <= 14; k++) v += Math.sin(2 * Math.PI * k * i / 2048) / k * (k % 2 ? 1 : 0.6); SAW[i] = v * 0.55; }
  const strings = (f, dur, vel = 1, attack = 0.45, trem = 0, bright = 0.08) => {
    const len = Math.round(dur * RATE), out = new Float32Array(len), det = [0.996, 1, 1.004], ph = [0, 0.33, 0.71]; let lp = 0;
    for (let i = 0; i < len; i++) {
      const t = i / RATE, vib = 1 + 0.0035 * Math.sin(2 * Math.PI * 5.2 * t); let v = 0;
      for (let k = 0; k < 3; k++) { ph[k] = (ph[k] + f * det[k] * vib / RATE) % 1; v += SAW[(ph[k] * 2048) | 0]; }
      lp += bright * (v / 3 - lp);
      out[i] = lp * vel * Math.min(1, t / attack) * Math.min(1, (len - i) / (RATE * 0.5)) * (1 - trem * (0.5 + 0.5 * Math.sin(2 * Math.PI * 13 * t)));
    }
    return out;
  };
  const hiss = (dur, decay, hp = 0.9, lp = 1) => {
    const len = Math.round(dur * RATE), out = new Float32Array(len); let a = 0, b = 0;
    for (let i = 0; i < len; i++) { const x = noise() * 2 - 1; a += lp * (x - a); const h = a - b; b += (1 - hp) * (a - b); out[i] = h * Math.exp(-(i / RATE) / decay); }
    return out;
  };
  const sweep = (f0, f1, dur, decay) => {
    const len = Math.round(dur * RATE), out = new Float32Array(len); let ph = 0;
    for (let i = 0; i < len; i++) { const t = i / RATE; ph += 2 * Math.PI * (f0 * Math.pow(f1 / f0, t / dur)) / RATE; out[i] = Math.sin(ph) * Math.exp(-t / decay) * Math.min(1, t / 0.004); }
    return out;
  };
  const timp = (f, vel = 1) => {
    const len = Math.round(1.6 * RATE), out = new Float32Array(len); let ph = 0;
    for (let i = 0; i < len; i++) { const t = i / RATE; ph += 2 * Math.PI * f * (1 + 0.5 * Math.exp(-t / 0.02)) / RATE; out[i] = (Math.sin(ph) + 0.4 * Math.sin(ph * 1.5)) * Math.exp(-t / 0.45) * vel + (noise() - 0.5) * 0.5 * Math.exp(-t / 0.02) * vel; }
    return out;
  };

  // ---------------------------------------------------------------------------------------- score
  const CH = { C: [36, [60, 64, 67]], Am: [45, [57, 60, 64]], F: [41, [57, 60, 65]], G: [43, [55, 59, 62]], Em: [40, [55, 59, 64]], Dm: [38, [57, 62, 65]], E: [40, [56, 59, 64]], Fm: [41, [56, 60, 65]] };
  // Melodies: per bar, [eighth, note, eighths].
  const M1 = [[[0, 69, 2], [2, 72, 2], [4, 77, 2], [6, 76, 1], [7, 72, 1]], [[0, 74, 3], [3, 71, 1], [4, 74, 2], [6, 79, 2]], [[0, 79, 2], [2, 76, 2], [4, 71, 2], [6, 74, 1], [7, 76, 1]], [[0, 76, 4], [4, 72, 2], [6, 69, 2]]];
  const M2 = [[[0, 69, 1], [1, 72, 1], [2, 77, 2], [4, 81, 2], [6, 79, 1], [7, 77, 1]], [[0, 79, 2], [2, 74, 2], [4, 71, 2], [6, 74, 2]], [[0, 76, 2], [2, 79, 2], [4, 84, 3], [7, 79, 1]], [[0, 76, 8]]];
  const M2end = [M2[0], M2[1], [[0, 76, 2], [2, 79, 2], [4, 84, 2], [6, 86, 2]], [[0, 84, 8]]];
  const M3 = [[[0, 69, 2], [2, 76, 2], [4, 74, 1], [5, 72, 1], [6, 71, 2]], [[0, 67, 2], [2, 71, 2], [4, 76, 4]], [[0, 77, 2], [2, 76, 2], [4, 72, 2], [6, 69, 2]], [[0, 68, 2], [2, 71, 2], [4, 76, 4]]];
  const M3s = [M3[0], [[0, 69, 2], [2, 72, 2], [4, 77, 4]], [[0, 77, 2], [2, 76, 2], [4, 74, 2], [6, 69, 2]], M3[3]];
  const MSEA = [[[0, 76, 3], [3, 72, 1], [4, 69, 4]], [[0, 77, 3], [3, 72, 1], [4, 69, 4]], [[0, 79, 3], [3, 76, 1], [4, 72, 4]], [[0, 74, 4], [4, 71, 2], [6, 74, 2]]];
  const MSNOW = [[[0, 79, 4], [4, 76, 4]], [[0, 83, 4], [4, 79, 4]], [[0, 81, 4], [4, 84, 4]], [[0, 80, 6], [6, 77, 2]]];
  const MROOM = [[], [], [[4, 72, 2], [6, 76, 2]], [[0, 74, 4], [4, 79, 4]]];
  const SECTIONS = [
    { ch: ['C', 'Am', 'F', 'G'], mel: MROOM, ins: 'piano', melG: 0.24, pad: 0.03, arp: 4, arpG: 0.12, bass: 0.18 },
    { ch: ['F', 'G', 'Em', 'Am'], mel: M1, ins: 'piano', melG: 0.3, dbl: 0.11, pad: 0.09, arp: 8, arpG: 0.1, bass: 0.3 },
    { ch: ['F', 'G', 'C', 'C'], mel: M2, ins: 'glock', melG: 0.2, dbl: 0.07, pad: 0.06, arp: 8, arpG: 0.13, bass: 0.26, bounce: 1 },
    { ch: ['Am', 'F', 'C', 'G'], mel: MSEA, ins: 'strings', melG: 0.17, pad: 0.07, arp: 16, arpG: 0.085, bass: 0.26 },
    { ch: ['Am', 'Em', 'F', 'E'], mel: M3, ins: 'glock', melG: 0.16, pad: 0.07, arp: 4, arpG: 0.07, bass: 0.2, dark: 1 },
    { ch: ['Am', 'F', 'Dm', 'E'], mel: M3s, ins: 'brass', melG: 0.2, pad: 0.13, arp: 16, arpG: 0.05, bass: 0.4, trem: 0.7, timp: 1 },
    { ch: ['C', 'Em', 'F', 'Fm'], mel: MSNOW, ins: 'glock', melG: 0.2, pad: 0.08, arp: 8, arpG: 0.05, bass: 0.16, high: 1 },
    { ch: ['F', 'G', 'Em', 'Am'], mel: M1, ins: 'piano', melG: 0.26, pad: 0.06, arp: 0, arpG: 0, bass: 0.22, slow: 1 },
    { ch: ['F', 'G', 'C', 'C'], mel: M2end, ins: 'piano', melG: 0.32, dbl: 0.13, pad: 0.1, arp: 8, arpG: 0.11, bass: 0.32, last: 1 },
  ];
  SECTIONS.forEach((sec, si) => {
    for (let b = 0; b < 4; b++) {
      const t0 = (si * 4 + b) * BAR, [bass, tones] = CH[sec.ch[b]], final = sec.last && b === 3;
      // Bass and pad.
      mix(piano(hz(bass), BAR * 1.1, 0.9), t0, sec.bass, -0.1); if (!sec.slow) mix(piano(hz(bass + 12), BAR * 0.6, 0.5), t0 + 2 * BEAT, sec.bass * 0.7, -0.1);
      for (const m of tones) mix(strings(hz(m - (sec.high ? -12 : 12)), BAR + (final ? 3.5 : 0.6), 1, sec.trem ? 0.08 : 0.5, sec.trem ?? 0, sec.high ? 0.05 : 0.08), t0, sec.pad * (sec.high ? 0.6 : 1), 0);
      mix(strings(hz(bass + (sec.dark || sec.trem ? 0 : 12)), BAR + 0.6, 1, 0.4, sec.trem ?? 0), t0, sec.pad * 0.9, 0);
      // Arpeggio.
      if (sec.arp && !final) {
        const seq = [bass + 12, tones[0], tones[1], tones[2], tones[0] + 12, tones[2], tones[1], tones[0]];
        for (let k = 0; k < sec.arp; k++) {
          const ts = t0 + k * BAR / sec.arp, m = seq[k % seq.length] + (sec.arp === 16 && k >= 8 ? 12 : 0) + (sec.high ? 12 : 0);
          if (sec.bounce && k % 2 === 0) continue;
          mix(harp(hz(m), 1.4, 1), ts, sec.arpG * (k % (sec.arp / 4) === 0 ? 1.15 : 0.85), k % 2 ? 0.3 : -0.3);
        }
      }
      if (sec.bounce) for (let k = 0; k < 4; k++) mix(harp(hz(bass + 24), 0.35, 1), t0 + k * BEAT, 0.12, -0.2);
      if (final) [bass, bass + 12, ...tones, tones[0] + 12, tones[1] + 12].forEach((m, k) => mix(piano(hz(m), 5, 0.8), t0 + k * 0.07, 0.2, k % 2 ? 0.2 : -0.2));
      // Melody.
      for (const [e, m, d] of sec.mel[b]) {
        const ts = t0 + e * E8, dur = d * E8;
        if (sec.ins === 'piano') mix(piano(hz(m), dur + 1.0, 1, sec.slow ? 1.25 : 1), ts, sec.melG, 0.15);
        else if (sec.ins === 'glock') { mix(glock(hz(m + 12), 1.8), ts, sec.melG, 0.25); if (sec.dark) mix(strings(hz(m - 12), dur + 0.4, 1, 0.2), ts, 0.05, -0.2); }
        else if (sec.ins === 'strings') { mix(strings(hz(m), dur + 0.35, 1, 0.14, 0, 0.12), ts, sec.melG, 0.1); mix(strings(hz(m - 12), dur + 0.35, 1, 0.14), ts, sec.melG * 0.6, -0.1); }
        else { mix(strings(hz(m - 12), dur + 0.2, 1, 0.05, 0, 0.3), ts, sec.melG, 0); mix(strings(hz(m - 24), dur + 0.2, 1, 0.05, 0, 0.3), ts, sec.melG * 0.8, 0); }
        if (sec.dbl) mix(strings(hz(m), dur + 0.3, 1, 0.12), ts, sec.dbl, 0.1);
      }
      if (sec.timp) { mix(timp(hz(bass), 1), t0, 0.5); mix(timp(hz(bass), 0.6), t0 + 2 * BEAT, 0.4); if (b === 3) for (let k = 0; k < 8; k++) mix(timp(hz(bass), 0.3 + k * 0.08), t0 + 2 * BEAT + k * E8 / 2, 0.3); }
    }
  });

  // ---------------------------------------------------------------------------------------- ambience
  for (const [a, b, kind, level] of AMBIENCE) {
    const dur = b - a, len = Math.round(dur * RATE), out = new Float32Array(len); let l1 = 0, l2 = 0;
    for (let i = 0; i < len; i++) {
      const t = i / RATE, x = noise() * 2 - 1, env = Math.min(1, t / 0.8, (dur - t) / 0.8);
      if (kind === 'wind') { const k = 0.012 + 0.01 * Math.sin(t * 0.9) + 0.006 * Math.sin(t * 2.3); l1 += k * (x - l1); l2 += k * (l1 - l2); out[i] = l2 * 5 * env * (0.7 + 0.3 * Math.sin(t * 0.6)); }
      else if (kind === 'waves') { l1 += 0.06 * (x - l1); out[i] = l1 * 1.2 * env * Math.pow(0.5 + 0.5 * Math.sin(t * 1.25), 2); }
      else if (kind === 'rain') { l1 += 0.35 * (x - l1); l2 += 0.02 * (l1 - l2); out[i] = (l1 - l2) * 0.55 * env; }
      else { l1 += 0.004 * (x - l1); out[i] = l1 * 6 * env; }
    }
    mix(out, a, 0.16 * level);
  }

  // ---------------------------------------------------------------------------------------- effects
  const run = (notes, gap, dur, g) => { const o = new Float32Array(Math.round((notes.length * gap + dur) * RATE)); notes.forEach((m, k) => { const gl = glock(hz(m), dur); for (let i = 0; i < gl.length; i++) { const j = i + Math.round(k * gap * RATE); if (j < o.length) o[j] += gl[i] * g; } }); return o; };
  const fx = {
    whoosh: () => { const h = hiss(1.1, 10, 0.7, 0.2); for (let i = 0; i < h.length; i++) h[i] *= Math.sin(Math.PI * i / h.length) ** 2; return h; },
    chime: () => run([84, 91], 0.12, 2.6, 0.6),
    sparkle: () => run([84, 88, 91, 96, 100], 0.07, 1.2, 0.5),
    twinkle: () => run([96, 103], 0.09, 0.9, 0.4),
    shimmer: () => { const o = new Float32Array(Math.round(3 * RATE)); for (const m of [84, 91, 96]) { const s = strings(hz(m), 3, 1, 1.2, 0, 0.04); for (let i = 0; i < s.length; i++) o[i] += s[i] * 0.4; } return o; },
    star: () => sweep(2400, 900, 0.7, 0.3),
    bird: () => { const o = new Float32Array(Math.round(0.5 * RATE)); for (let k = 0; k < 3; k++) { const s = sweep(2600 + k * 300, 3600 + k * 200, 0.09, 0.05); for (let i = 0; i < s.length; i++) o[i + Math.round(k * 0.13 * RATE)] += s[i]; } return o; },
    splash: () => { const h = hiss(0.8, 0.2, 0.4, 0.5), b = sweep(300, 900, 0.25, 0.08); for (let i = 0; i < b.length; i++) h[i] += b[i] * 0.4; return h; },
    'splash-small': () => hiss(0.3, 0.07, 0.4, 0.6),
    thud: () => sweep(140, 55, 0.35, 0.1),
    thunder: () => { const len = Math.round(3.5 * RATE), o = new Float32Array(len); let l1 = 0, l2 = 0; for (let i = 0; i < len; i++) { const t = i / RATE, x = noise() * 2 - 1; l1 += 0.03 * (x - l1); l2 += 0.02 * (l1 - l2); o[i] = (l2 * 14 * (Math.exp(-t / 0.9) + 0.5 * Math.exp(-Math.pow(t - 0.7, 2) * 9)) + x * 0.5 * Math.exp(-t / 0.03)); } return o; },
  };
  const gains = { whoosh: 0.3, chime: 0.25, sparkle: 0.3, twinkle: 0.16, shimmer: 0.12, star: 0.07, bird: 0.07, splash: 0.4, 'splash-small': 0.25, thud: 0.5, thunder: 0.75 };
  const pr = rng(9);
  for (const e of sfx()) mix(fx[e.k](), e.t, gains[e.k], (pr() - 0.5) * 0.6);

  // ---------------------------------------------------------------------------------------- master
  let peak = 0;
  for (let i = 0; i < n; i++) {
    const t = i / RATE, env = Math.min(1, t / 0.5) * Math.min(1, Math.max(0, (DURATION - t) / 1.5));
    L[i] = Math.tanh(L[i] * 1.2) * env; R[i] = Math.tanh(R[i] * 1.2) * env; peak = Math.max(peak, Math.abs(L[i]), Math.abs(R[i]));
  }
  const g = 0.89 / (peak || 1), data = Buffer.alloc(n * 4);
  for (let i = 0; i < n; i++) { data.writeInt16LE(Math.round(L[i] * g * 32767), i * 4); data.writeInt16LE(Math.round(R[i] * g * 32767), i * 4 + 2); }
  const head = Buffer.alloc(44);
  head.write('RIFF', 0); head.writeUInt32LE(36 + data.length, 4); head.write('WAVE', 8); head.write('fmt ', 12); head.writeUInt32LE(16, 16);
  head.writeUInt16LE(1, 20); head.writeUInt16LE(2, 22); head.writeUInt32LE(RATE, 24); head.writeUInt32LE(RATE * 4, 28); head.writeUInt16LE(4, 32); head.writeUInt16LE(16, 34);
  head.write('data', 36); head.writeUInt32LE(data.length, 40);
  fs.writeFileSync(file, Buffer.concat([head, data]));
  return file;
}

if (process.argv[1]?.endsWith('film-audio.mjs')) console.log(writeSoundtrack('out/petfam-film-soundtrack-ko.wav'));
