// Shared timeline for the promo video: the picture (main.js) and the soundtrack (audio.mjs) both read it,
// so the sound effects land on the same frames as what they belong to.

export const FPS = 30;
export const DURATION = 50;

export const CAPTIONS = [
  { t0: 6.4, t1: 12.4, k: { ko: '바탕화면 위, 창 뒤에서', en: 'On your wallpaper, behind your windows' },
    l: { ko: '일하는 창 뒤에서 펫 가족이 살아요', en: 'Your pet family lives right behind your work' } },
  { t0: 12.9, t1: 18.6, k: { ko: 'AI가 바탕화면을 읽어요', en: 'It reads your wallpaper' },
    l: { ko: '물에선 첨벙, 햇살 아래선 꾸벅꾸벅', en: 'Dogs dive into the pond, cats find the sunny spot' } },
  { t0: 20.0, t1: 26.0, k: { ko: 'AI 대화 · 풍경 인식', en: 'They talk, with AI' },
    l: { ko: '바탕화면이 바뀌면 펫들이 먼저 알아채요', en: 'Change the wallpaper and they talk about their new home' } },
  { t0: 27.2, t1: 33.4, k: { ko: '말로 꾸미는 집', en: 'Build with words' },
    l: { ko: '한 마디면 AI가 집을 지어 줘요', en: 'Describe a house and the AI builds it' } },
  { t0: 33.8, t1: 39.5, k: { ko: '진짜 날씨', en: 'Real weather' },
    l: { ko: '우리 동네에 비가 오면 바탕화면에도 비가 와요', en: 'When it rains where you live, it rains on your desktop' } },
  { t0: 40.5, t1: 42.9, k: { ko: '함께 자라요', en: 'They grow up with you' },
    l: { ko: '아기부터 어르신까지, 하루하루 함께', en: 'From baby to senior, one day at a time' } },
  { t0: 43.0, t1: 45.0, k: { ko: '33가지 품종', en: '33 breeds' },
    l: { ko: '강아지 17종 · 고양이 16종, 저마다의 성격', en: '17 dogs and 16 cats, each with its own personality' } },
];

export const BUBBLES = [
  { pet: 'dog', t0: 21.3, t1: 23.0, ko: '우와, 바다다! 같이 수영하자멍!', en: "Whoa, the sea! Let's go swimming!" },
  { pet: 'cat', t0: 22.9, t1: 24.9, ko: '흥… 예전 풀밭이 더 좋았다냥.', en: 'Hmph. I liked the old meadow better.' },
  { pet: 'dog', t0: 24.7, t1: 26.1, ko: '모래가 폭신폭신하다멍!', en: 'But the sand is so soft!' },
  { pet: 'cat', t0: 34.3, t1: 35.9, ko: '비다! 집으로 피신이다냥!', en: 'Rain! Everybody inside!' },
  { pet: 'dog', t0: 36.0, t1: 37.6, ko: '첨벙첨벙! 신난다멍!', en: 'Splish splash! Puddles!' },
  { pet: 'dog', t0: 38.0, t1: 39.4, ko: '눈이다아! 멍멍!', en: 'Snow! Woof woof!' },
];

// Places the AI finds in the meadow wallpaper (pixel coordinates of the 2560x1440 picture). Each pops up as the
// scan line passes over it.
export const SCAN = { t0: 12.6, t1: 13.7, end: 16.4 };
export const CHIPS = [
  { u: 1840, v: 1000, icon: '💧', ko: '물', en: 'Water' },
  { u: 1240, v: 790, icon: '☀️', ko: '햇살', en: 'Sunny spot' },
  { u: 600, v: 930, icon: '🌳', ko: '그늘', en: 'Shade' },
  { u: 1235, v: 1010, icon: '👣', ko: '길', en: 'Path' },
].map(c => ({ ...c, t0: SCAN.t0 + (c.u / 2560) * (SCAN.t1 - SCAN.t0) }));

export const TYPE = { t0: 27.4, t1: 28.5, ko: '등불 달린 아늑한 통나무집', en: 'A cozy log cabin with a lantern' };
export const PRESS = 28.8;
export const BUILD = 29.5;
export const LINEUP = 42.75;

export function sfx(lang) {
  const list = [
    { t: 0.3, k: 'chime' },
    { t: 3.9, k: 'whoosh' },
    { t: 8.6, k: 'whoosh' },
    { t: 11.8, k: 'whoosh' },
    { t: SCAN.t0, k: 'scan' },
    ...CHIPS.map(c => ({ t: c.t0, k: 'pop' })),
    { t: 14.8, k: 'splash' },
    { t: 16.6, k: 'splash' },
    { t: 17.2, k: 'shake' },
    { t: 18.8, k: 'whoosh' },
    ...BUBBLES.map(b => ({ t: b.t0, k: 'pop' })),
    { t: 26.0, k: 'whoosh' },
    { t: PRESS, k: 'click' },
    { t: BUILD, k: 'pop' },
    { t: BUILD + 0.95, k: 'thud' },
    { t: BUILD + 1.2, k: 'pop' },
    { t: BUILD + 1.5, k: 'sparkle' },
    { t: 32.8, k: 'pop' },
    { t: 35.0, k: 'pop' },
    { t: 35.85, k: 'pop' },
    ...[0, 1, 2].map(i => ({ t: 36.08 + i * 0.43, k: 'splash-small' })),
    { t: 39.5, k: 'whoosh' },
    { t: 40.4, k: 'pop' },
    { t: 41.7, k: 'tada' },
    ...[0, 1, 2, 3, 4, 5].map(i => ({ t: LINEUP + i * 0.14, k: 'pop' })),
    { t: 45.3, k: 'chime' },
  ];
  const text = TYPE[lang];
  const step = (TYPE.t1 - TYPE.t0) / text.length;
  for (let i = 0; i < text.length; i++) if (text[i] !== ' ') list.push({ t: TYPE.t0 + i * step, k: 'tick' });
  return list.sort((a, b) => a.t - b.t);
}
