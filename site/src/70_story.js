// ---- the story: scrolling moves through eight beats; this file says where the camera, the day and the pets are --------
const BEATS = 8;
const CAM = [
  { p: [17, 7.5, 23], l: [0, 0.9, 0], f: 32, o: 0.19 },          // 0 the island, morning
  { p: [1.0, 2.6, 15], l: [0.2, 2.3, 0], f: 34, o: 0.16 },       // 1 through the app windows
  { p: [12.4, 2.3, 7.6], l: [4.6, 0.7, 1.4], f: 30, o: 0.14 },   // 2 the pond
  { p: [0.6, 1.9, 8.2], l: [0, 1.5, 1.8], f: 26, o: 0 },     // 3 the two of them talking
  { p: [2.6, 3.3, 9.2], l: [-4.0, 1.9, -1.6], f: 34, o: -0.12 },  // 4 the cabin goes up
  { p: [7.5, 3.6, 13], l: [-1.4, 1.3, 0.6], f: 34, o: 0.13 },       // 5 weather
  { p: [-1.2, 2.2, 12.5], l: [-1.2, 1.9, 1.6], f: 30, o: 0 },       // 6 the family, golden hour
  { p: [7.5, 4.2, 15.5], l: [-2.6, 2.6, 0], f: 32, o: 0 },     // 7 night
];
const E = (o = {}) => ({ ...ENV0, ...o });
const ENVS = [
  [0, E({ sun: [0.55, 0.5, 0.55], sunCol: [1.0, 0.9, 0.76], warm: 0.25 })],
  [1.6, E({ sun: [0.4, 0.72, 0.5] })],
  [2.4, E({ sun: [0.25, 0.85, 0.45], sunInt: 3.1 })],
  [4.5, E({ sun: [-0.35, 0.7, 0.55], cloud: 0.5 })],
  [4.85, E({ sun: [-0.3, 0.7, 0.5], sunCol: [0.75, 0.8, 0.9], sunInt: 0.7, top: [0.26, 0.31, 0.42], hor: [0.5, 0.56, 0.64], hemi: 0.75, cloud: 0.98, cloudCol: [0.55, 0.58, 0.66], fog: [0.5, 0.56, 0.64], fogDen: 0.007, exposure: 0.95, sat: 0.9, bloom: 0.2 })],
  [5.2, E({ sun: [-0.3, 0.7, 0.5], sunCol: [0.75, 0.8, 0.9], sunInt: 0.7, top: [0.26, 0.31, 0.42], hor: [0.5, 0.56, 0.64], hemi: 0.75, cloud: 0.98, cloudCol: [0.55, 0.58, 0.66], fog: [0.5, 0.56, 0.64], fogDen: 0.007, exposure: 0.95, sat: 0.9, bloom: 0.2 })],
  [5.5, E({ sun: [-0.3, 0.6, 0.5], sunCol: [0.9, 0.93, 1.0], sunInt: 1.5, top: [0.45, 0.55, 0.72], hor: [0.8, 0.86, 0.94], hemi: 1.0, cloud: 0.9, cloudCol: [0.85, 0.88, 0.95], fog: [0.82, 0.87, 0.94], fogDen: 0.012, exposure: 0.95, sat: 0.92 })],
  [5.75, E({ sun: [-0.3, 0.6, 0.5], sunCol: [0.9, 0.93, 1.0], sunInt: 1.5, top: [0.45, 0.55, 0.72], hor: [0.8, 0.86, 0.94], hemi: 1.0, cloud: 0.9, cloudCol: [0.85, 0.88, 0.95], fog: [0.82, 0.87, 0.94], fogDen: 0.012, exposure: 0.95, sat: 0.92 })],
  [5.96, E({ sun: [-0.8, 0.2, 0.45], sunCol: [1.0, 0.6, 0.3], sunInt: 3.2, top: [0.16, 0.26, 0.6], hor: [1.0, 0.64, 0.4], hemi: 0.7, ground: [0.5, 0.36, 0.3], cloud: 0.5, cloudCol: [1.0, 0.8, 0.7], fog: [0.98, 0.7, 0.5], fogDen: 0.007, warm: 0.7, bloom: 0.4, sat: 1.12 })],
  [6.5, E({ sun: [-0.8, 0.2, 0.45], sunCol: [1.0, 0.6, 0.3], sunInt: 3.2, top: [0.16, 0.26, 0.6], hor: [1.0, 0.64, 0.4], hemi: 0.7, ground: [0.5, 0.36, 0.3], cloud: 0.5, cloudCol: [1.0, 0.8, 0.7], fog: [0.98, 0.7, 0.5], fogDen: 0.007, warm: 0.7, bloom: 0.4, sat: 1.12 })],
  [7, E({ sun: [-0.5, 0.42, -0.6], sunCol: [0.5, 0.62, 1.0], sunInt: 1.9, top: [0.01, 0.02, 0.08], hor: [0.06, 0.11, 0.28], hemi: 0.75, ground: [0.08, 0.1, 0.14], cloud: 0.3, cloudCol: [0.3, 0.36, 0.55], night: 1, fog: [0.04, 0.07, 0.18], fogDen: 0.006, exposure: 1.5, bloom: 0.7, sat: 1.05 })],
];
function envAt(s) {
  let a = ENVS[0], b = ENVS[0];
  for (let i = 1; i < ENVS.length; i++) { if (s <= ENVS[i][0]) { a = ENVS[i - 1]; b = ENVS[i]; break; } a = b = ENVS[i]; }
  const k = a === b ? 0 : smooth(range(s, a[0], b[0]));
  for (const key in ENV0) { const x = a[1][key], y = b[1][key]; env[key] = Array.isArray(x) ? x.map((v, i) => lerp(v, y[i], k)) : lerp(x, y, k); }
}
const camPos = new THREE.Vector3(), camLook = new THREE.Vector3(), _a = new THREE.Vector3(), _b = new THREE.Vector3();
function cameraAt(s, t, ptr) {
  const i = Math.min(BEATS - 2, Math.floor(s)), k = ease(clamp(s - i)), A = CAM[i], B = CAM[i + 1], portrait = camera.aspect < 0.9;
  camPos.set(...A.p).lerp(_a.set(...B.p), k); camLook.set(...A.l).lerp(_b.set(...B.l), k);
  camPos.y += Math.sin(k * PI) * 1.6;                                        // rise a little between beats, like a crane
  const orbit = Math.sin(t * 0.11) * 0.07 * (1 - bell(s, 0.6, 4.4, 0.3) * 0.7);   // slow drift, mostly on the wide shots
  camPos.sub(camLook).applyAxisAngle(_a.set(0, 1, 0), orbit + (ptr.x * 0.05)).multiplyScalar(portrait ? 1.55 : 1).add(camLook);
  camPos.y += ptr.y * 0.35 + (portrait ? 1.2 : 0);
  camera.position.copy(camPos); camera.fov = lerp(A.f, B.f, k) * (portrait ? 1.25 : 1);
  { const w = renderer.domElement.width, h = renderer.domElement.height, o = portrait ? 0 : lerp(A.o, B.o, k); camera.setViewOffset(w, h, -o * w, 0, w, h); }   // slide the picture aside to leave room for the words
  camera.updateProjectionMatrix(); camera.lookAt(camLook); camera.updateMatrixWorld();
}

// A pet's path through the story: keys [beat, x, z, pose]. Between two places it walks; at a place it holds its pose.
function actor(keys) {
  const len = [0]; for (let i = 1; i < keys.length; i++) len.push(len[i - 1] + Math.hypot(keys[i][1] - keys[i - 1][1], keys[i][2] - keys[i - 1][2]));
  return s => {
    let i = 1; while (i < keys.length - 1 && s > keys[i][0]) i++;
    const A = keys[i - 1], B = keys[i], k = range(s, A[0], B[0]), e = smooth(k), d = len[i] - len[i - 1], moving = d > 0.05 && k > 0 && k < 1;
    const pa = A[3] ?? {}, pb = B[3] ?? {}, m = moving ? Math.min(1, Math.sin(PI * k) * 3) : 0;
    const head = Math.atan2(B[1] - A[1], B[2] - A[2]), yaw = moving ? head : (k >= 1 ? pb.yaw ?? head : pa.yaw ?? 0);
    const hold = k >= 1 ? pb : pa;
    return { x: lerp(A[1], B[1], e), z: lerp(A[2], B[2], e), yaw, gait: m, phase: (len[i - 1] + d * e) * 6.2, sit: (hold.sit ?? 0) * (1 - m), lie: (hold.lie ?? 0) * (1 - m), run: pb.run ?? 0 };
  };
}
const RX = ISLE.rock[0], RZ = ISLE.rock[1];
const dogTrack = actor([
  [0.6, -1.2, 2.2, { yaw: 0.6 }], [1.0, -2.4, 1.6, { yaw: 0.4 }], [1.6, 1.4, 1.5, { yaw: 1.4 }], [1.8, 2.5, 1.45, { yaw: 1.5 }],
  [2.72, 2.5, 1.45, { yaw: -1.5 }], [3.0, -1.1, 2.0, { yaw: 1.05, sit: 1 }], [3.5, -1.1, 2.0, { yaw: 1.05, sit: 1 }], [3.9, -1.9, 1.4, { yaw: -1.0, sit: 1 }],
  [4.55, -1.9, 1.4, { yaw: -1.0, sit: 1 }], [4.8, 0.4, 3.0, { yaw: 0.3 }], [5.3, 0.4, 3.0, { yaw: 0.3, sit: 1 }], [5.72, 0.4, 3.0, { yaw: 0.3, sit: 1 }],
  [5.92, -2.1, 2.2, { yaw: 0.15, sit: 1 }], [6.55, -2.1, 2.2, { yaw: 0.15, sit: 1 }], [6.9, DOOR.x + 1.6, DOOR.z + 0.9, { yaw: 0.9, lie: 1 }], [9, DOOR.x + 1.6, DOOR.z + 0.9, { yaw: 0.9, lie: 1 }],
]);
const catTrack = actor([
  [2.6, RX, RZ, { yaw: 0.5, sit: 1 }], [3.0, 1.1, 2.1, { yaw: -1.05, sit: 1 }], [3.5, 1.1, 2.1, { yaw: -1.05, sit: 1 }], [3.9, -0.4, 0.4, { yaw: -1.2, sit: 1 }],
  [4.62, -0.4, 0.4, { yaw: -1.2, sit: 1 }], [4.9, DOOR.x, DOOR.z, { yaw: 2.4 }], [5.7, DOOR.x, DOOR.z, { yaw: 0.7 }], [5.92, -0.4, 2.2, { yaw: -0.15, sit: 1 }],
  [6.55, -0.4, 2.2, { yaw: -0.15, sit: 1 }], [6.9, DOOR.x + 0.2, DOOR.z + 0.5, { yaw: 0.2, lie: 1 }], [9, DOOR.x + 0.2, DOOR.z + 0.5, { yaw: 0.2, lie: 1 }],
]);
const EXTRA_SPOTS = [[-4.5, 1.8, 0.35], [-3.3, 2.8, 0.25], [0.9, 2.9, -0.25], [2.0, 2.1, -0.35]];

// Everything the pets do at story position s and time t. click: { pet, t0 } from the last click.
function petsAt(s, t, ptrWorld, click) {
  const out = { splash: [], wake: null, wet: 0 };
  const lookAt = (st, hy) => { if (!ptrWorld) return [0, 0]; const a = Math.atan2(ptrWorld.x - st.x, ptrWorld.z - st.z) - st.yaw, w = Math.atan2(Math.sin(a), Math.cos(a)); return [clamp(w, -1, 1) * 0.8 * (1 - st.gait), clamp((ptrWorld.y - hy) * 0.12, -0.3, 0.3)]; };
  // ---- dog
  let d = dogTrack(s);
  const live = 1 - smooth(range(s, 0.3, 0.62));
  if (live > 0) { const a = t * 0.42, x = -0.4 + Math.cos(a) * 2.4, z = 2.6 + Math.sin(a) * 1.1; d = { ...d, x: lerp(d.x, x, live), z: lerp(d.z, z, live), yaw: live > 0.5 ? Math.atan2(-Math.sin(a) * 2.4, Math.cos(a) * 1.1) : d.yaw, gait: Math.max(d.gait, live), phase: live > 0.5 ? a * 2.0 * 6.2 : d.phase }; }
  d.y = groundY(d.x, d.z); d.wag = 0.6;
  // The dive: a run-up, an arc over the bank, a splash, a swim, and out again.
  const [px, pz] = ISLE.pond, jump = range(s, 1.8, 2.0), swim = bell(s, 2.0, 2.72, 0.04), out2 = range(s, 2.6, 2.72);
  if (s > 1.8 && s < 2.72) {
    const sw = t * 0.9, sx = px + Math.cos(sw) * 1.1, sz = pz + Math.sin(sw) * 0.8;
    if (jump < 1) { d.x = lerp(2.5, px - 0.6, jump); d.z = lerp(1.45, pz, jump); d.y = lerp(groundY(2.5, 1.45), ISLE.water - 0.5, jump) + Math.sin(jump * PI) * 1.5; d.yaw = 1.5; d.air = Math.sin(jump * PI); d.pitch = lerp(-0.5, 0.6, jump); d.gait = 0; }
    else { const k = smooth(out2); d.x = lerp(sx, 2.5, k); d.z = lerp(sz, 1.45, k); d.y = lerp(ISLE.water - 0.62, groundY(2.5, 1.45), k); d.yaw = k > 0.05 ? -1.5 : sw + PI / 2 + PI / 2; d.gait = 1; d.phase = t * 11; d.pitch = -0.12; out.wake = { x: d.x, z: d.z, k: 1 - k }; }
    out.splash.push({ age: (s - 2.0) * 3.2, x: px - 0.6, y: ISLE.water, z: pz, power: 1.15 });
  }
  out.wet = bell(s, 2.0, 3.25, 0.03); d.roll = Math.sin(t * 34) * 0.16 * bell(s, 2.78, 2.98, 0.04);
  if (s > 4.8 && s < 5.25) { const h = Math.abs(Math.sin(t * 5)); d.y += h * 0.5; d.air = h * 0.6; d.sit = 0; d.happy = true; if (h < 0.15) out.splash.push({ age: ((t * 5 / PI) % 1) * 0.6, x: d.x, y: d.y, z: d.z, power: 0.35 }); }
  if (s > 5.3 && s < 5.72) d.look = [0, 0.45];
  if (s > 6.85) d.happy = true;
  d.look ??= lookAt(d, d.y + 1.2);
  // ---- cat
  let c = catTrack(s);
  c.y = groundY(c.x, c.z); c.wag = 0.4;
  if (s < 2.6) { c.y = groundY(RX, RZ) + 0.86; c.lie = smooth(range(s, 1.5, 1.9)); c.sit = 1 - c.lie; c.happy = c.lie > 0.6; }
  else if (s < 3.0) c.y += (1 - smooth(range(s, 2.6, 2.75))) * 0.86;
  const inside = bell(s, 4.9, 5.7, 0.03); c.scale = 1 - inside; c.hide = inside > 0.98;
  if (s > 6.85) c.happy = true;
  c.look ??= lookAt(c, c.y + 1.0);
  if (s > 2.9 && s < 3.6) { d.look = [0.25, 0.05]; c.look = [-0.25, 0.0]; d.tilt = Math.sin(t * 1.3) * 0.18; }
  // ---- the rest of the family, for the golden hour
  out.extras = EXTRA_SPOTS.map(([x, z, yaw], i) => { const k = backOut(range(s, 5.74 + i * 0.04, 5.9 + i * 0.04)) * (1 - smooth(range(s, 6.6 + i * 0.03, 6.75 + i * 0.03)));
    const st = { x, z, y: groundY(x, z), yaw, sit: 1, scale: Math.max(0.001, k), hide: k < 0.01, wag: 0.7 }; st.look = lookAt(st, st.y + 1.1); return st; });
  if (click) { const a = t - click.t0, st = click.pet === 'dog' ? d : click.pet === 'cat' ? c : out.extras[click.pet]; if (st && a > 0 && a < 0.55) { st.y += Math.sin(a / 0.55 * PI) * 0.7; st.happy = true; st.air = Math.sin(a / 0.55 * PI) * 0.5; } }
  out.dog = d; out.cat = c;
  return out;
}
