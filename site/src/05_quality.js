// ---- how much detail this device gets -----------------------------------------------------------------------------
const QS = new URLSearchParams(location.search);
const MOBILE = matchMedia('(max-width: 760px)').matches || (navigator.hardwareConcurrency ?? 8) <= 4;
const QUALITY = MOBILE ? { msaa: 2, shells: 12, grass: 48000, leaves: 0.5, px: Math.min(devicePixelRatio, 1.5), shadow: 2048 }
  : { msaa: 4, shells: 22, grass: 90000, leaves: 1, px: Math.min(devicePixelRatio, 2), shadow: 4096 };
