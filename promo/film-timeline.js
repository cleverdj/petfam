// Timeline of the short film "The Window Ride": the picture (film.js) and the soundtrack (film-audio.mjs) both
// read it. Every world lasts 12 seconds (four bars of music at 80 bpm) and has two shots of six seconds.

export const FPS = 30;
export const DURATION = 108;

export const SHOTS = [
  { t0: 0, world: 'room', id: 'roomA' },
  { t0: 6, world: 'room', id: 'roomB' },
  { t0: 12, world: 'clouds', id: 'cloudsA' },
  { t0: 18, world: 'clouds', id: 'cloudsB' },
  { t0: 24, world: 'hills', id: 'hillsA' },
  { t0: 30, world: 'hills', id: 'hillsB' },
  { t0: 36, world: 'sea', id: 'seaA' },
  { t0: 42, world: 'sea', id: 'seaB' },
  { t0: 48, world: 'forest', id: 'forestA' },
  { t0: 54, world: 'forest', id: 'forestB' },
  { t0: 60, world: 'storm', id: 'stormA' },
  { t0: 66, world: 'storm', id: 'stormB' },
  { t0: 72, world: 'snow', id: 'snowA' },
  { t0: 78, world: 'snow', id: 'snowB' },
  { t0: 84, world: 'moon', id: 'moonA' },
  { t0: 90, world: 'moon', id: 'moonB' },
  { t0: 96, world: 'hills', id: 'home' },
  { t0: 102, world: 'room', id: 'roomEnd' },
];

// Cuts between worlds are covered by a short flash of light.
export const FLASHES = [12, 24, 36, 48, 60, 72, 84, 96, 102];

export const LIGHTNING = [61.3, 63.9, 65.2, 67.4, 69.6];

// Dolphin jumps next to the window: time, which dolphin, which side.
export const JUMP = 1.5;
export const DOLPHINS = [
  { t: 36.6, d: 0, side: 1 }, { t: 37.7, d: 1, side: 1 }, { t: 39.2, d: 2, side: -1 }, { t: 40.2, d: 0, side: 1 },
  { t: 42.6, d: 1, side: -1 }, { t: 43.9, d: 2, side: -1 }, { t: 45.5, d: 0, side: -1 },
];

export const SHOOTING = [84.8, 86.9, 88.2, 91.5, 93.6];

export function sfx() {
  const list = [
    { t: 0.4, k: 'chime' },
    { t: 6.8, k: 'sparkle' },
    { t: 9.4, k: 'whoosh' },
    ...FLASHES.map(t => ({ t: t - 0.45, k: 'whoosh' })),
    ...[13.2, 15.4, 19.5, 25.1, 27.6, 31.8, 33.2].map(t => ({ t, k: 'bird' })),
    ...DOLPHINS.flatMap(d => [{ t: d.t, k: 'splash-small' }, { t: d.t + JUMP, k: 'splash' }]),
    ...[48.6, 50.2, 51.9, 54.7, 56.1, 57.8].map(t => ({ t, k: 'twinkle' })),
    ...LIGHTNING.map(t => ({ t, k: 'thunder' })),
    ...[72.8, 76.0, 79.5].map(t => ({ t, k: 'shimmer' })),
    ...SHOOTING.map(t => ({ t, k: 'star' })),
    { t: 99.6, k: 'thud' },
    { t: 100.0, k: 'sparkle' },
    { t: 103.4, k: 'sparkle' },
    { t: 104.2, k: 'chime' },
  ];
  return list.sort((a, b) => a.t - b.t);
}

// Background ambience: [from, to, kind, level].
export const AMBIENCE = [
  [12, 24, 'wind', 0.5], [24, 36, 'wind', 0.3], [36, 48, 'waves', 0.7], [48, 60, 'forest', 0.4],
  [60, 72, 'rain', 0.9], [60, 72, 'wind', 0.9], [72, 84, 'wind', 0.5], [84, 96, 'wind', 0.2], [96, 102, 'wind', 0.25],
];
