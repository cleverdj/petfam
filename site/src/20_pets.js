// ---- the pets: sculpted from distance fields, coated in shell fur, moved by a small rig ------------------------------
// Each body part is its own sculpted mesh (so it can turn at its joint); the fur hides the seams. Every pose is a pure
// function of the state handed to update(), so a frame can be rendered on its own.
const FUR = { shells: QUALITY.shells, den: 175 };
const furMats = [];
function furMaterial(len, shells, den = FUR.den) {
  const m = new THREE.MeshStandardMaterial({ vertexColors: true, roughness: 0.95, metalness: 0, alphaToCoverage: true });
  m.userData.U = { furLen: { value: len }, shells: { value: shells }, furDen: { value: den }, furBend: { value: new THREE.Vector3() }, wet: { value: 0 } };
  m.onBeforeCompile = s => {
    Object.assign(s.uniforms, m.userData.U);
    s.vertexShader = s.vertexShader.replace('#include <common>', `#include <common>
      attribute float aFur; uniform float furLen, shells; uniform vec3 furBend; varying float vSh, vFurA; varying vec3 vBase;`)
      .replace('#include <begin_vertex>', `#include <begin_vertex>
      float sh = (float(gl_InstanceID) + 1.0) / shells; vSh = sh; vBase = position; vFurA = aFur;
      vec3 fdir = normalize(objectNormal + vec3(0.0, -0.5 * sh, 0.0) + furBend * sh);
      transformed += fdir * aFur * furLen * sh;`);
    s.fragmentShader = s.fragmentShader.replace('#include <common>', `#include <common>
      uniform float furDen, wet; varying float vSh, vFurA; varying vec3 vBase; ${GLSL_NOISE}`)
      .replace('#include <color_fragment>', `#include <color_fragment>
      vec3 fq = mat3(0.80, 0.36, -0.48, -0.60, 0.48, -0.64, 0.0, 0.80, 0.60) * vBase * furDen; vec3 fc = floor(fq); float frnd = hash31(fc);
      vec3 fj = vec3(hash31(fc + 3.1), hash31(fc + 7.7), hash31(fc + 13.3)) - 0.5;
      float flen = 0.35 + 0.65 * frnd;
      float frad = 0.60 * (1.0 - vSh / flen) * mix(1.0, 0.75, wet);
      if (vFurA > 0.02 && (frad <= 0.0 || length(fract(fq) - 0.5 - fj * 0.45) > frad)) discard;
      diffuseColor.rgb *= mix(0.5, 1.12, vSh) * (0.9 + 0.2 * frnd) * mix(1.0, 0.62, wet);`)
      .replace('#include <roughnessmap_fragment>', `#include <roughnessmap_fragment>
      roughnessFactor = mix(roughnessFactor, 0.35, wet * 0.8);`);
  };
  furMats.push(m);
  return m;
}
const skinMat = new THREE.MeshStandardMaterial({ vertexColors: true, roughness: 0.9 });
skinMat.onBeforeCompile = s => { s.fragmentShader = s.fragmentShader.replace('#include <color_fragment>', '#include <color_fragment>\n diffuseColor.rgb *= 0.5;'); };
const IDENT = new THREE.Matrix4();
// A sculpted part with its coat: the skin casts the shadow, the shells carry the fur.
function furry(geo, len, shells = FUR.shells) {
  const g = new THREE.Group();
  const skin = new THREE.Mesh(geo, skinMat); skin.castShadow = true; skin.receiveShadow = true; g.add(skin);
  const mat = furMaterial(len, shells);
  const coat = new THREE.InstancedMesh(geo, mat, shells); for (let i = 0; i < shells; i++) coat.setMatrixAt(i, IDENT);
  coat.receiveShadow = true; coat.frustumCulled = false; g.add(coat);
  g.userData.mat = mat;
  return g;
}
const C = hex => new THREE.Color(hex);
const SPECS = {
  dog: { kind: 'dog', coat: C('#d9933f'), cream: C('#fbe9cc'), dark: C('#b06c26'), ear: C('#a8621f'), furBody: 0.062, furTail: 0.085, tailN: 5, tailL: 0.075, tailR: [0.06, 0.04],
    eyeR: 0.041, eye: [0.09, 0.035, 0.176], iris: C('#2a1a10'), nose: C('#1d1614'), scale: 1.55 },
  cat: { kind: 'cat', coat: C('#8f98a8'), cream: C('#f6f3ee'), dark: C('#565d6b'), ear: C('#7b8494'), furBody: 0.042, furTail: 0.04, tailN: 8, tailL: 0.078, tailR: [0.042, 0.03],
    eyeR: 0.043, eye: [0.094, 0.025, 0.162], iris: C('#b9d36a'), nose: C('#e58f9c'), scale: 1.36, stripes: true },
};
function variant(base, o) { return { ...SPECS[base], ...o }; }

function makePet(spec) {
  const cat = spec.kind === 'cat', P = { spec, legs: [], tail: [], ears: [] };
  const tmp = new THREE.Color();
  const stripe = (p, k = 34) => spec.stripes ? smooth(range(Math.sin(p.z * k + fbm3(p.x * 6, p.y * 6, p.z * 6) * 5), 0.25, 0.75)) * smooth(range(p.y, -0.12, 0.05)) : 0;
  const paintCoat = (c, p, creamK, stripeK) => { c.copy(spec.coat).lerp(tmp.copy(spec.dark), stripeK * 0.85).lerp(spec.cream, creamK).multiplyScalar(0.9 + 0.2 * fbm3(p.x * 9, p.y * 9, p.z * 9)); };

  const root = P.root = new THREE.Group(), body = P.body = new THREE.Group(); root.add(body);
  // Torso.
  const torso = furry(sculpt((x, y, z) => smin(smin(sdEllipsoid(x, y, z, 0, 0, 0.2, 0.235, 0.24, 0.3), sdEllipsoid(x, y, z, 0, -0.01, -0.22, 0.215, 0.215, 0.27), 0.12),
    sdCapsule(x, y, z, 0, 0.05, 0.3, 0, 0.23, 0.45, 0.15, 0.125), 0.1), { rmax: 0.9, seg: 72, rings: 48,
    paint: (p, n, c) => { const k = fbm3(p.x * 5, p.y * 5, p.z * 5) * 0.12; paintCoat(c, p, Math.max(smooth(range(-p.y + k, 0.06, 0.16)), smooth(range(p.z - p.y * 0.9 + k, 0.3, 0.42)) * smooth(range(-p.y, -0.2, 0.0))), stripe(p)); return 1; } }), spec.furBody);
  body.add(torso);

  // Head.
  const [ex, ey, ez] = spec.eye;
  const headSdf = cat
    ? (x, y, z) => smin(smin(sdEllipsoid(x, y, z, 0, 0, 0, 0.215, 0.195, 0.2), sdEllipsoid(x, y, z, 0, -0.075, 0.13, 0.1, 0.072, 0.1), 0.07),
        Math.min(sdEllipsoid(x, y, z, 0.115, -0.065, 0.07, 0.1, 0.085, 0.095), sdEllipsoid(x, y, z, -0.115, -0.065, 0.07, 0.1, 0.085, 0.095)), 0.07)
    : (x, y, z) => smin(sdEllipsoid(x, y, z, 0, 0, 0, 0.21, 0.2, 0.205), smin(sdEllipsoid(x, y, z, 0, -0.07, 0.19, 0.105, 0.088, 0.15), sdEllipsoid(x, y, z, 0, 0.03, 0.13, 0.13, 0.08, 0.1), 0.08), 0.08);
  const head = P.head = new THREE.Group();
  const headMesh = furry(sculpt(headSdf, { center: [0, 0, 0.03], rmax: 0.7, seg: 72, rings: 52,
    paint: (p, n, c) => {
      const muz = smooth(range(p.z - Math.max(0, p.y + 0.02) * 2.4, cat ? 0.13 : 0.17, cat ? 0.2 : 0.25)) * smooth(range(-p.y, -0.03, 0.04));
      const blaze = cat ? smooth(range(-p.y, 0.0, 0.1)) * smooth(range(p.z, 0.02, 0.14)) : 0;
      const m = cat ? smooth(range(Math.sin(p.x * 46), 0.2, 0.8)) * smooth(range(p.y, 0.07, 0.15)) * smooth(range(p.z, -0.05, 0.1)) : 0;
      paintCoat(c, p, Math.max(muz, blaze), m);
      const eye = Math.min(Math.hypot(p.x - ex, p.y - ey, p.z - ez), Math.hypot(p.x + ex, p.y - ey, p.z - ez));
      const tip = cat ? Math.hypot(p.x, p.y + 0.05, p.z - 0.23) : Math.hypot(p.x, p.y + 0.03, p.z - 0.34);
      return Math.min(lerp(1, 0.22, muz), smooth(range(eye, spec.eyeR * 0.9, spec.eyeR * 2.9)), smooth(range(tip, 0.03, 0.09)));
    } }), spec.furBody * 0.62);
  head.add(headMesh); head.position.set(0, 0.3, 0.5); body.add(head);
  // Eyes, nose, whiskers.
  const eyeMat = new THREE.MeshPhysicalMaterial({ color: cat ? spec.iris : 0x120b08, roughness: 0.04, clearcoat: 1, clearcoatRoughness: 0.02 });
  const pupilMat = new THREE.MeshBasicMaterial({ color: 0x050404 }), glint = new THREE.MeshBasicMaterial({ color: new THREE.Color(1.6, 1.6, 1.6) });
  const EYE = new THREE.SphereGeometry(1, 24, 16);
  P.eyes = [-1, 1].map(s => {
    const e = new THREE.Group(); e.position.set(s * ex, ey, ez);
    const ball = new THREE.Mesh(EYE, eyeMat); ball.scale.setScalar(spec.eyeR); e.add(ball);
    if (cat) { const pu = new THREE.Mesh(EYE, pupilMat); pu.scale.set(spec.eyeR * 0.36, spec.eyeR * 0.82, spec.eyeR * 0.3); pu.position.set(s * 0.004, 0, spec.eyeR * 0.78); e.add(pu); e.userData.pupil = pu; }
    const g = new THREE.Mesh(EYE, glint); g.scale.setScalar(spec.eyeR * 0.17); g.position.set(spec.eyeR * 0.3, spec.eyeR * 0.36, spec.eyeR * 0.84); e.add(g);
    head.add(e); return e;
  });
  const noseMat = new THREE.MeshPhysicalMaterial({ color: spec.nose, roughness: 0.25, clearcoat: 0.8 });
  const nose = new THREE.Mesh(EYE, noseMat); nose.castShadow = true;
  if (cat) { nose.scale.set(0.022, 0.016, 0.014); nose.position.set(0, -0.045, 0.236); } else { nose.scale.set(0.043, 0.033, 0.03); nose.position.set(0, -0.035, 0.335); }
  head.add(nose);
  if (cat) {
    const pts = []; for (const s of [-1, 1]) for (let i = 0; i < 4; i++) { pts.push(s * 0.06, -0.07 + i * 0.008, 0.2, s * (0.25 + (i % 2) * 0.04), -0.09 + i * 0.03, 0.17); }
    const wg = new THREE.BufferGeometry(); wg.setAttribute('position', new THREE.Float32BufferAttribute(pts, 3));
    head.add(new THREE.LineSegments(wg, new THREE.LineBasicMaterial({ color: 0xffffff, transparent: true, opacity: 0.75 })));
  }
  // Ears.
  for (const s of [-1, 1]) {
    const pivot = new THREE.Group(); let ear;
    if (cat) {
      ear = furry(sculpt((x, y, z) => sdEllipsoid(x, y, z, 0, 0.09, 0, 0.09, 0.115, 0.032), { center: [0, 0.09, 0], rmax: 0.3, seg: 28, rings: 20,
        paint: (p, n, c) => { const inner = smooth(range(n.z, 0.3, 0.7)) * smooth(range(Math.hypot(p.x, p.y - 0.08), 0.085, 0.04)); c.copy(spec.ear).lerp(tmp.set('#f0a9b6'), inner); return lerp(0.8, 0.1, inner); },
        deform: p => { const k = smooth(range(p.y, 0.0, 0.2)); p.x *= 1 - 0.88 * k; p.z *= 1 - 0.5 * k; p.z -= p.x * p.x * 2.0; } }), 0.02, 8);
      pivot.position.set(s * 0.118, 0.135, -0.02); pivot.rotation.set(-0.1, s * 0.25, -s * 0.3);
    } else {
      ear = furry(sculpt((x, y, z) => sdEllipsoid(x, y, z, 0, -0.105, 0, 0.082, 0.125, 0.03), { center: [0, -0.105, 0], rmax: 0.3, seg: 28, rings: 20,
        paint: (p, n, c) => { c.copy(spec.ear).multiplyScalar(0.9 + 0.2 * fbm3(p.x * 20, p.y * 20, 3)); return 0.9; },
        deform: p => { p.z += p.y * p.y * 1.5; } }), 0.04, 10);
      pivot.position.set(s * 0.165, 0.135, -0.015); pivot.rotation.set(0.15, 0, s * 0.42);
    }
    pivot.add(ear); pivot.userData = { s, rest: pivot.rotation.clone() }; head.add(pivot); P.ears.push(pivot);
  }
  // Legs: hip -> upper -> knee -> lower with the paw.
  const upper = rear => furry(sculpt((x, y, z) => sdCapsule(x, y, z, 0, 0.02, 0, 0, -0.2, 0, rear ? 0.105 : 0.088, 0.062), { center: [0, -0.09, 0], rmax: 0.4, seg: 28, rings: 22,
    paint: (p, n, c) => { paintCoat(c, p, 0, spec.stripes ? smooth(range(Math.sin(p.y * 44), 0.3, 0.8)) : 0); return 0.8; } }), spec.furBody * 0.75, 12);
  const lower = () => furry(sculpt((x, y, z) => smin(sdCapsule(x, y, z, 0, 0.01, 0, 0, -0.19, 0, 0.06, 0.05), sdEllipsoid(x, y, z, 0, -0.205, 0.035, 0.064, 0.046, 0.088), 0.04), { center: [0, -0.11, 0.01], rmax: 0.4, seg: 28, rings: 22,
    paint: (p, n, c) => { const sock = smooth(range(-p.y, 0.1, 0.16)); paintCoat(c, p, sock, spec.stripes ? smooth(range(Math.sin(p.y * 44 + 2), 0.3, 0.8)) * (1 - sock) : 0); return lerp(0.6, 0.22, smooth(range(-p.y, 0.14, 0.2))); } }), spec.furBody * 0.6, 10);
  const upF = upper(false), upR = upper(true), low = lower();
  for (const [x, y, z, rear] of [[-0.13, -0.1, 0.28, 0], [0.13, -0.1, 0.28, 0], [-0.135, -0.09, -0.3, 1], [0.135, -0.09, -0.3, 1]]) {
    const hip = new THREE.Group(); hip.position.set(x, y, z); hip.add((rear ? upR : upF).clone());
    const knee = new THREE.Group(); knee.position.set(0, -0.2, 0); knee.add(low.clone()); hip.add(knee);
    body.add(hip); P.legs.push({ hip, knee, rear });
  }
  // Tail: a chain of short tapered segments.
  let parent = new THREE.Group(); parent.position.set(0, 0.1, -0.46); body.add(parent); P.tailRoot = parent;
  for (let i = 0; i < spec.tailN; i++) {
    const r0 = lerp(spec.tailR[0], spec.tailR[1], i / spec.tailN), r1 = lerp(spec.tailR[0], spec.tailR[1], (i + 1) / spec.tailN), Lz = spec.tailL, last = i === spec.tailN - 1;
    const seg = furry(sculpt((x, y, z) => sdCapsule(x, y, z, 0, 0, 0, 0, 0, -Lz, r0, r1), { center: [0, 0, -Lz / 2], rmax: 0.3, seg: 18, rings: 12,
      paint: (p, n, c) => { paintCoat(c, p, !cat && i >= spec.tailN - 2 ? 0.8 : 0, cat && i % 2 ? 0.9 : 0); return last ? 0.8 : 1; } }), spec.furTail, 12);
    const j = new THREE.Group(); j.add(seg); parent.add(j); if (i) j.position.z = -Lz; P.tail.push(j); parent = j;
  }
  root.scale.setScalar(spec.scale);

  // st: { x, y, z, yaw, gait 0..1, phase, sit 0..1, lie 0..1, look [yaw, pitch], wag 0..1, happy, airborne 0..1, wind [x,z] }
  P.update = (st, t) => {
    const sit = st.sit ?? 0, lie = st.lie ?? 0, gait = st.gait ?? 0, ph = st.phase ?? 0, air = st.air ?? 0, sc = spec.scale * (st.scale ?? 1);
    root.position.set(st.x, st.y, st.z); root.rotation.y = st.yaw ?? 0; root.scale.setScalar(sc);
    const breath = Math.sin(t * 2.4 + (cat ? 1 : 0)) * 0.012;
    body.position.y = 0.54 - 0.2 * sit - 0.3 * lie + Math.abs(Math.sin(ph)) * 0.022 * gait;
    body.rotation.set(-0.55 * sit * (1 - lie) + (st.pitch ?? 0) + Math.sin(ph * 2) * 0.02 * gait, 0, Math.sin(ph) * 0.04 * gait + (st.roll ?? 0));
    torso.scale.set(1 + breath, 1 + breath, 1);
    P.legs.forEach((L, i) => {
      const side = i % 2, off = (L.rear ? (side ? 0 : PI) : (side ? PI : 0)), a = ph + off;
      let hip = Math.sin(a) * 0.55 * gait, knee = Math.max(0, Math.sin(a - 1.9)) * 1.0 * gait;
      if (L.rear) { hip = lerp(hip, -1.15, sit); knee = lerp(knee, 2.15, sit); } else { hip = lerp(hip, 0.62, sit); knee = lerp(knee, -0.12, sit); }
      hip = lerp(hip, L.rear ? 1.25 : -1.3, lie); knee = lerp(knee, L.rear ? 0.25 : 0.1, lie);
      hip = lerp(hip, L.rear ? 0.9 : -0.9, air); knee = lerp(knee, 0.3, air);
      L.hip.rotation.x = hip; L.knee.rotation.x = knee;
    });
    const look = st.look ?? [0, 0];
    head.rotation.set(0.5 * sit * (1 - lie) + 0.25 * lie - look[1] + Math.sin(t * 0.9 + (cat ? 2 : 0)) * 0.03 - gait * Math.sin(ph * 2) * 0.03, look[0], (st.tilt ?? 0) + Math.sin(t * 0.7) * 0.02);
    head.position.y = 0.3 - 0.05 * lie;
    const wag = st.wag ?? 0.3;
    P.tailRoot.rotation.set(cat ? 0.75 - 0.5 * lie : 0.95 + 0.25 * (st.happy ? 1 : 0), 0, 0);
    P.tail.forEach((j, i) => {
      if (cat) { j.rotation.x = 0.14 + Math.sin(t * 1.3 + i * 0.5) * 0.06; j.rotation.y = Math.sin(t * 1.7 - i * 0.6) * (0.1 + 0.16 * wag); }
      else { j.rotation.x = 0.3; j.rotation.y = Math.sin(t * (7 + 8 * wag) - i * 0.5) * (0.1 + 0.3 * wag); }
    });
    for (const e of P.ears) { const r = e.userData.rest, s = e.userData.s; e.rotation.set(r.x + (cat ? 0 : Math.sin(ph * 2 + s) * 0.12 * gait + air * 0.5), r.y, r.z + (cat ? Math.sin(t * 0.6 + s) * 0.04 : s * (air * 0.7 + Math.abs(Math.sin(ph)) * 0.08 * gait))); }
    const blink = ((t * 0.23 + (cat ? 0.4 : 0)) % 1) < 0.03 || st.happy;
    for (const e of P.eyes) e.scale.y = blink ? 0.12 : 1;
  };
  return P;
}
