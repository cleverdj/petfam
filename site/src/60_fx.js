// ---- air and water in motion: rain, snow, splashes, petals, fireflies, butterflies, chimney smoke ---------------------
// One point cloud type with per-point size and alpha; every effect below fills one from the time alone.
class Dots {
  constructor(n, { additive = false, color = [1, 1, 1], soft = true } = {}) {
    this.n = n; this.pos = new Float32Array(n * 3); this.size = new Float32Array(n); this.alpha = new Float32Array(n); this.col = new Float32Array(n * 3).fill(1);
    const g = this.geo = new THREE.BufferGeometry();
    g.setAttribute('position', new THREE.BufferAttribute(this.pos, 3)); g.setAttribute('aSize', new THREE.BufferAttribute(this.size, 1)); g.setAttribute('aAlpha', new THREE.BufferAttribute(this.alpha, 1)); g.setAttribute('aCol', new THREE.BufferAttribute(this.col, 3));
    this.U = { px: { value: 1 }, tint: { value: new THREE.Color(...color) } };
    this.mesh = new THREE.Points(g, new THREE.ShaderMaterial({ transparent: true, depthWrite: false, blending: additive ? THREE.AdditiveBlending : THREE.NormalBlending, uniforms: this.U,
      vertexShader: 'attribute float aSize, aAlpha; attribute vec3 aCol; uniform float px; varying float vA; varying vec3 vC; void main(){ vec4 mv = modelViewMatrix * vec4(position, 1.0); vA = aAlpha; vC = aCol; gl_PointSize = aSize * px / max(-mv.z, 0.1); gl_Position = projectionMatrix * mv; }',
      fragmentShader: `uniform vec3 tint; varying float vA; varying vec3 vC; void main(){ float d = length(gl_PointCoord - 0.5) * 2.0; float a = ${soft ? 'pow(smoothstep(1.0, 0.0, d), 1.5)' : 'smoothstep(1.0, 0.8, d)'}; if (a * vA < 0.01) discard; gl_FragColor = vec4(tint * vC, a * vA); }` }));
    this.mesh.frustumCulled = false; scene.add(this.mesh);
  }
  set(i, x, y, z, s, a) { this.pos[i * 3] = x; this.pos[i * 3 + 1] = y; this.pos[i * 3 + 2] = z; this.size[i] = s; this.alpha[i] = a; }
  rgb(i, r, g, b) { this.col[i * 3] = r; this.col[i * 3 + 1] = g; this.col[i * 3 + 2] = b; }
  done(visible = true) { this.mesh.visible = visible; if (!visible) return; for (const k of ['position', 'aSize', 'aAlpha', 'aCol']) this.geo.attributes[k].needsUpdate = true; this.U.px.value = renderer.domElement.height / (2 * Math.tan(THREE.MathUtils.degToRad(camera.fov / 2))); }
}
const fxSnow = new Dots(MOBILE ? 500 : 1400), fxSpray = new Dots(260, { color: [0.9, 0.97, 1.0] }), fxPetals = new Dots(120, { color: [1.0, 0.72, 0.82], soft: false });
const fxFlies = new Dots(140, { additive: true, color: [2.6, 2.1, 0.7] }), fxSmoke = new Dots(40, { color: [0.92, 0.92, 0.95] }), fxHearts = new Dots(24, { additive: true, color: [2.4, 0.7, 0.9] });
const RAIN_N = MOBILE ? 900 : 2600, rainPos = new Float32Array(RAIN_N * 6), rainGeo = new THREE.BufferGeometry(); rainGeo.setAttribute('position', new THREE.BufferAttribute(rainPos, 3));
const fxRain = new THREE.LineSegments(rainGeo, new THREE.LineBasicMaterial({ color: 0xcfe0ff, transparent: true, opacity: 0, fog: false })); fxRain.frustumCulled = false; scene.add(fxRain);

// Butterflies: two wings each, looping over the flowers.
const butterflies = [];
{
  const wing = new THREE.Shape(); wing.moveTo(0, 0); wing.bezierCurveTo(0.1, 0.22, 0.34, 0.26, 0.36, 0.08); wing.bezierCurveTo(0.38, -0.06, 0.26, -0.2, 0.1, -0.16); wing.lineTo(0, 0);
  const g = new THREE.ShapeGeometry(wing, 6).rotateX(-PI / 2);
  for (const [c, cx, cz, rad, sp, ph] of [['#ffd23f', -7.5, 5.0, 1.6, 0.5, 0], ['#8fd3ff', 3.2, -3.4, 1.9, 0.42, 2], ['#ff9ac1', 8.0, -6.0, 1.4, 0.55, 4]]) {
    const b = new THREE.Group(), m = new THREE.MeshStandardMaterial({ color: c, side: THREE.DoubleSide, roughness: 0.6, emissive: c, emissiveIntensity: 0.15 }), l = new THREE.Mesh(g, m), r = new THREE.Mesh(g, m); r.scale.x = -1; l.castShadow = r.castShadow = true;
    b.add(l, r); b.userData = { l, r, cx, cz, rad, sp, ph }; scene.add(b); butterflies.push(b);
  }
}

// w: { rain, snow, night, splash: [{t0 (story seconds), x, y, z, power}], wake: {x, z, k}, smoke, petals }
function updateFx(w, t) {
  const cx = camera.position.x, cy = camera.position.y, cz = camera.position.z;
  // Rain falls in a box around the middle of the island.
  fxRain.visible = w.rain > 0.01; fxRain.material.opacity = w.rain * 0.5;
  if (fxRain.visible) { const r = rng(41); for (let i = 0; i < RAIN_N; i++) { const x = (r() - 0.5) * 34, z = (r() - 0.5) * 34, sp = 17 + r() * 7, y = 16 - ((t * sp + r() * 22) % 22); rainPos.set([x, y, z, x - 0.14, y - 0.6, z + 0.04], i * 6); } rainGeo.attributes.position.needsUpdate = true; }
  { const r = rng(77); for (let i = 0; i < fxSnow.n; i++) { const sp = 0.9 + r() * 1.1, ph = r() * 6.3; fxSnow.set(i, (r() - 0.5) * 34 + Math.sin(t * 0.8 + ph) * 0.5, 15 - ((t * sp + r() * 19) % 19), (r() - 0.5) * 34 + Math.cos(t * 0.6 + ph) * 0.5, 0.07 + r() * 0.08, w.snow * 0.95); } fxSnow.done(w.snow > 0.01); }
  // Splashes: droplets thrown up and out, then falling back.
  { let n = 0;
    for (const s of w.splash ?? []) { const a = s.age ?? (t - s.t0), r = rng(Math.round((s.x + s.z) * 10) + 7); if (a < 0 || a > 1.5) continue;
      for (let i = 0; i < 90 && n < fxSpray.n; i++) { const an = r() * TAU, sp = (0.6 + r() * 2.6) * s.power, up = (3 + r() * 4.5) * s.power, y = s.y + up * a - 7 * a * a;
        if (y < s.y - 0.05) continue; fxSpray.set(n++, s.x + Math.cos(an) * sp * a, y, s.z + Math.sin(an) * sp * a, 0.11 * (1 - a * 0.4), 0.9); } }
    if (w.wake && w.wake.k > 0.01) { const r = rng(5); for (let i = 0; i < 40 && n < fxSpray.n; i++) { const a = (t * 1.8 + r()) % 1, an = r() * TAU; fxSpray.set(n++, w.wake.x + Math.cos(an) * (0.3 + a * 0.6), ISLE.water + 0.05 + Math.sin(a * PI) * 0.22, w.wake.z + Math.sin(an) * (0.3 + a * 0.6), 0.07 * (1 - a), 0.8 * w.wake.k); } }
    const used = n; for (; n < fxSpray.n; n++) fxSpray.set(n, 0, -99, 0, 0, 0); fxSpray.done(used > 0); }
  // Petals drift down from the blossom tree; hidden in bad weather and at night.
  { const r = rng(13), k = (w.petals ?? 1) * (1 - w.rain) * (1 - w.snow), b = BLOSSOM.position;
    for (let i = 0; i < fxPetals.n; i++) { const life = 7 + r() * 6, a = ((t + r() * life) % life) / life, an = r() * TAU, d = r() * 2.2;
      fxPetals.set(i, b.x + Math.cos(an) * d + a * (3 + r() * 5) + Math.sin(t * 1.3 + i) * 0.25, b.y + 4.4 - a * 5.2 + Math.sin(t * 2 + i) * 0.1, b.z + Math.sin(an) * d + a * (r() - 0.3) * 3, 0.085, k * Math.min(1, a * 8) * (1 - smooth(range(a, 0.8, 1)))); }
    fxPetals.done(k > 0.01); }
  { const r = rng(61); for (let i = 0; i < fxFlies.n; i++) { const ph = r() * 6.3, sp = 0.5 + r(), an = r() * TAU, d = 1.5 + r() * 10; fxFlies.set(i, Math.cos(an) * d + Math.sin(t * 0.5 * sp + ph) * 0.9, 1.0 + r() * 3.2 + Math.sin(t * 0.8 * sp + ph) * 0.5, Math.sin(an) * d + Math.cos(t * 0.45 * sp + ph) * 0.9, 0.16, w.night * (0.2 + 0.8 * Math.max(0, Math.sin(t * 1.5 * sp + ph * 3)))); } fxFlies.done(w.night > 0.01); }
  // Chimney smoke, once the cabin stands.
  { const r = rng(19), top = cabin.localToWorld(new THREE.Vector3(-0.95, 4.2, -0.45)); for (let i = 0; i < fxSmoke.n; i++) { const a = (t * 0.22 + r()) % 1; fxSmoke.set(i, top.x + a * a * 1.6 + Math.sin(t + i) * 0.1 * a, top.y + a * 3.2, top.z + (r() - 0.5) * 0.4 * a, 0.5 + a * 1.4, (w.smoke ?? 0) * 0.35 * Math.sin(a * PI)); } fxSmoke.done((w.smoke ?? 0) > 0.01); }
  // Hearts when a pet is clicked.
  { let n = 0; for (const h of w.hearts ?? []) { const a = t - h.t0; if (a < 0 || a > 1.4) continue; const r = rng(Math.round(h.t0 * 60)); for (let i = 0; i < 8 && n < fxHearts.n; i++) fxHearts.set(n++, h.x + (r() - 0.5) * 0.9, h.y + a * (1.2 + r()), h.z + (r() - 0.5) * 0.9, 0.2 * (1 - a / 1.4), 1 - a / 1.4); }
    const used = n; for (; n < fxHearts.n; n++) fxHearts.set(n, 0, -99, 0, 0, 0); fxHearts.done(used > 0); }
  for (const b of butterflies) { const d = b.userData, a = t * d.sp + d.ph, x = d.cx + Math.cos(a) * d.rad * (1 + 0.3 * Math.sin(a * 2.3)), z = d.cz + Math.sin(a * 1.3) * d.rad, k = (1 - w.rain) * (1 - w.snow) * (1 - w.night);
    b.visible = k > 0.05; b.position.set(x, groundY(x, z) + 0.9 + Math.sin(t * 2.2 + d.ph) * 0.35, z); b.rotation.y = -a * 1.2; b.scale.setScalar(0.55 * k);
    const f = Math.sin(t * 22 + d.ph) * 0.9 + 0.3; d.l.rotation.z = f; d.r.rotation.z = -f; }
  if (ISLE.floaters) for (const m of ISLE.floaters) { m.position.y = m.userData.float[0] + Math.sin(t * 0.35 + m.userData.float[1]) * 0.5; m.rotation.y = t * 0.03 + m.userData.float[1]; }
}
