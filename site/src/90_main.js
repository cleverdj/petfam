// ---- boot, input, the frame ---------------------------------------------------------------------------------------------
const dog = makePet(SPECS.dog), cat = makePet(SPECS.cat);
const extras = [
  makePet(variant('dog', { coat: C('#f4f1ea'), cream: C('#ffffff'), dark: C('#d9d2c4'), ear: C('#e6dfd2'), scale: 1.25 })),
  makePet(variant('cat', { coat: C('#e8964a'), cream: C('#fff3e2'), dark: C('#b9692a'), ear: C('#d9843c'), iris: C('#e2b23a'), scale: 1.2 })),
  makePet(variant('cat', { coat: C('#3d3d4a'), cream: C('#4a4a58'), dark: C('#2c2c36'), ear: C('#34343f'), iris: C('#ffd23f'), stripes: false, scale: 1.25 })),
  makePet(variant('dog', { coat: C('#6f7888'), cream: C('#f1f3f7'), dark: C('#4c5462'), ear: C('#59616f'), scale: 1.45 })),
];
for (const p of [dog, cat, ...extras]) { scene.add(p.root); p.fur = []; p.root.traverse(o => { if (o.material?.userData?.U) p.fur.push(o.material.userData.U); }); }
const setWet = (p, v) => { for (const u of p.fur) u.wet.value = v; };

// ---- language and the words on screen
const root = document.documentElement;
function setLang(l) {
  root.lang = l;
  document.querySelectorAll('[data-ko]').forEach(e => { e.innerHTML = e.dataset[l]; });
  document.getElementById('lang').textContent = l === 'ko' ? 'EN' : '한국어';
  document.title = l === 'ko' ? 'PetFam — 우리 펫 가족이 바탕화면에 살아요' : 'PetFam — Your pet family, living on your desktop';
  try { localStorage.setItem('petfam-lang', l); } catch { /* private mode */ }
}
document.getElementById('lang').onclick = () => setLang(root.lang === 'ko' ? 'en' : 'ko');
{ let saved = null; try { saved = localStorage.getItem('petfam-lang'); } catch { /* private mode */ } setLang(QS.get('lang') ?? saved ?? ((navigator.language || 'en').startsWith('ko') ? 'ko' : 'en')); }
const beatEls = [...document.querySelectorAll('.beat')], dotsEl = document.getElementById('dots'), bubbles = [...document.querySelectorAll('.bubble')];
dotsEl.innerHTML = beatEls.map(() => '<i></i>').join('');
// [dog says, cat answers] in Korean and English.
const TALK = [
  { ko: ['여기 풀밭 진짜 좋다멍!', '흥, 나쁘지 않다냥.'], en: ['I love this meadow!', 'Hmph. Not bad.'] },
  { ko: ['같이 연못 들어가자멍!', '젖는 건 질색이다냥.'], en: ['Come swim with me!', 'I do not do wet.'] },
  { ko: ['집사 오늘 일 많아 보인다멍.', '그럼 간식은 누가 주냥…'], en: ['Our human looks busy today.', 'Then who brings the treats…'] },
];
const _v = new THREE.Vector3();
function updateDom(s, t) {
  const portrait = innerWidth < 760;
  beatEls.forEach((el, i) => {
    const a = clamp(1 - Math.abs(s - i) * (i === 0 || i === BEATS - 1 ? 2.2 : 2.6)), e = smooth(a), center = el.classList.contains('center');
    el.style.opacity = e.toFixed(3); el.style.visibility = e > 0.01 ? 'visible' : 'hidden';
    el.style.transform = `${center ? 'translateX(-50%) ' : ''}${!center && !portrait ? 'translateY(-50%) ' : ''}translateY(${((s - i) * -70).toFixed(1)}px)`;
    dotsEl.children[i].classList.toggle('on', Math.round(s) === i);
  });
  const talk = Math.abs(s - 3) < 0.3, turn = Math.floor(t / 2.8), pair = TALK[Math.floor(turn / 2) % TALK.length][root.lang === 'ko' ? 'ko' : 'en'];
  [dog, cat].forEach((p, i) => {
    const el = bubbles[i], on = talk && turn % 2 === i;
    if (on && el.textContent !== pair[i]) el.textContent = pair[i];
    p.head.getWorldPosition(_v); _v.y += 0.62; _v.project(camera);
    el.style.left = `${(_v.x * 0.5 + 0.5) * innerWidth}px`; el.style.top = `${(-_v.y * 0.5 + 0.5) * innerHeight}px`; el.classList.toggle('on', on);
  });
}

// ---- input: scroll position (eased), pointer, clicks on pets
const reduced = matchMedia('(prefers-reduced-motion: reduce)').matches;
const ptr = { x: 0, y: 0, seen: false }, ptrWorld = new THREE.Vector3(), ray = new THREE.Raycaster(), plane = new THREE.Plane(new THREE.Vector3(0, 1, 0), -0.9), noPtr = { x: 0, y: 0 };
addEventListener('pointermove', e => { ptr.x = e.clientX / innerWidth * 2 - 1; ptr.y = -(e.clientY / innerHeight * 2 - 1); ptr.seen = e.pointerType === 'mouse'; });
let click = null, lastStates = null, clockNow = 0; const hearts = [];
addEventListener('pointerdown', e => {
  if (e.target.closest('a, button') || !lastStates) return;
  let best = null, bd = 130;
  for (const [id, st] of [['dog', lastStates.dog], ['cat', lastStates.cat], ...lastStates.extras.map((x, i) => [i, x])]) {
    if (st.hide) continue; _v.set(st.x, st.y + 0.8, st.z).project(camera);
    const d = Math.hypot((_v.x * 0.5 + 0.5) * innerWidth - e.clientX, (-_v.y * 0.5 + 0.5) * innerHeight - e.clientY); if (d < bd) { bd = d; best = [id, st]; }
  }
  if (best) { click = { pet: best[0], t0: clockNow }; hearts.push({ t0: clockNow, x: best[1].x, y: best[1].y + 1.6, z: best[1].z }); if (hearts.length > 3) hearts.shift(); }
});
const scrollP = () => { const max = document.documentElement.scrollHeight - innerHeight; return max > 0 ? clamp(scrollY / max) : 0; };
let sSmooth = scrollP() * (BEATS - 1);

function resize() {
  const w = innerWidth, h = innerHeight, px = QUALITY.px;
  renderer.setPixelRatio(px); renderer.setSize(w, h, false); camera.aspect = w / h; camera.updateProjectionMatrix();
  buildLens(Math.round(w * px), Math.round(h * px)); lensU.res.value.set(w * px, h * px);
}
addEventListener('resize', resize); resize();

// One frame: story position s (0..7) and time t in seconds decide everything.
function frame(s, t) {
  clockNow = t; U.time.value = t;
  envAt(s); applyEnv();
  cameraAt(s, t, ptr.seen && !reduced ? ptr : noPtr);
  if (ptr.seen) { ray.setFromCamera(new THREE.Vector2(ptr.x, ptr.y), camera); if (!ray.ray.intersectPlane(plane, ptrWorld)) ptrWorld.set(0, 1, 6); }
  const rain = bell(s, 4.7, 5.3, 0.12), snow = bell(s, 5.25, 5.82, 0.12), night = env.night;
  const st = lastStates = petsAt(s, t, ptr.seen ? ptrWorld : null, click);
  dog.update(st.dog, t); setWet(dog, Math.max(st.wet, rain * 0.6));
  cat.root.visible = !st.cat.hide; cat.update(st.cat, t);
  extras.forEach((p, i) => { p.root.visible = !st.extras[i].hide; if (p.root.visible) p.update(st.extras[i], t); });
  U.wet.value = rain * 0.9; U.snow.value = smooth(range(s, 5.3, 5.6)) * (1 - smooth(range(s, 5.68, 5.9)));
  buildCabin(range(s, 3.3, 3.98));
  showWindows(bell(s, 0.55, 1.75, 0.3), t);
  const lit = s > 4.25 ? Math.max(night, rain * 0.8, snow * 0.5, smooth(range(s, 6.0, 6.4)) * 0.6) : 0;
  cabinLit.glass.emissiveIntensity = lit * 2.2; cabinLit.bulb.emissiveIntensity = 0.2 + lit * 5; cabinLit.light.intensity = lit * 26;
  ball.position.set(-3.1, groundY(-3.1, 3.2) + 0.2, 3.2);
  updateFx({ rain, snow, night, splash: st.splash, wake: st.wake, smoke: s > 4.0 ? 1 - 0.75 * night : 0, hearts }, t);
  if (waterMat.userData.rip) waterMat.userData.rip.value.set(ISLE.pond[0] - 0.6, ISLE.pond[1], s > 2.0 && s < 3.4 ? t - (s - 2.0) * 3.2 : -99, 0);
  lensU.tilt.value = MOBILE ? 0 : 0.5 * (1 - bell(s, 2.6, 3.4, 0.3) * 0.6);
  const cu = cloudSea.material.uniforms; cu.fogCol.value.copy(scene.fog.color); cu.col.value.setRGB(...env.cloudCol).multiplyScalar(lerp(1, 0.25, night)); cu.shade.value.setRGB(...env.hor).multiplyScalar(lerp(0.85, 0.3, night));
  aimSun(0, 0, 0); updateClouds(t);
  updateDom(s, t);
  composer.render();
}
window.__frame = (p, t) => frame(p * (BEATS - 1), t);

// Warm up every shader on a hidden frame, then lift the curtain.
window.__ready = new Promise(res => requestAnimationFrame(() => { frame(sSmooth, 0); frame(5, 0); frame(sSmooth, 0); document.getElementById('loader').classList.add('done'); res(true); }));
if (!QS.has('capture')) {
  let last = performance.now();
  const loop = ms => {
    const dt = Math.min(0.05, (ms - last) / 1000); last = ms;
    sSmooth = lerp(sSmooth, scrollP() * (BEATS - 1), 1 - Math.exp(-dt * (reduced ? 30 : 4.5)));
    if (!document.hidden) frame(sSmooth, ms / 1000);
    requestAnimationFrame(loop);
  };
  window.__ready.then(() => requestAnimationFrame(loop));
}
