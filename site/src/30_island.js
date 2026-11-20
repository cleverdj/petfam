// ---- the island: a small world floating above the clouds -------------------------------------------------------------
const ISLE = { R: 13, pond: [5.2, 1.6, 3.1], water: 0.18, cabin: [-5.2, -2.6], rock: [1.8, -3.4] };
const PATH = [[-4.3, -0.7], [-2.2, 0.9], [0.6, 1.5], [2.6, 1.3]];
function pathDist(x, z) {
  let d = 1e9;
  for (let i = 1; i < PATH.length; i++) {
    const [ax, az] = PATH[i - 1], [bx, bz] = PATH[i], h = clamp(((x - ax) * (bx - ax) + (z - az) * (bz - az)) / ((bx - ax) ** 2 + (bz - az) ** 2));
    d = Math.min(d, Math.hypot(x - ax - (bx - ax) * h, z - az - (bz - az) * h));
  }
  return d + (fbm2(x * 1.3, z * 1.3) - 0.5) * 0.5;
}
const pondD = (x, z) => Math.hypot(x - ISLE.pond[0], (z - ISLE.pond[1]) * 1.25) / ISLE.pond[2] + (fbm2(x * 0.7 + 4, z * 0.7) - 0.5) * 0.18;
const edgeR = a => ISLE.R * (0.9 + 0.16 * fbm2(Math.cos(a) * 1.6 + 5, Math.sin(a) * 1.6 + 5));
// Height of the ground. Everything that stands on the island asks this.
function groundY(x, z) {
  const r = Math.hypot(x, z);
  let h = 0.75 * (1 - (r / ISLE.R) ** 2) + (fbm2(x * 0.16 + 3, z * 0.16 + 8) - 0.5) * 1.7 * smooth(range(r, 2.5, 9)) + (fbm2(x * 0.9, z * 0.9) - 0.5) * 0.12;
  const cd = Math.hypot(x - ISLE.cabin[0], z - ISLE.cabin[1]); h = lerp(0.62, h, smooth(range(cd, 2.4, 4.6)));
  const pd = pondD(x, z); h = lerp(0.36, h, smooth(range(pd, 1.05, 1.9)));
  h -= 1.25 * (1 - smooth(range(pd, 0.45, 1.02)));
  return h;
}
const isleU = { snow: U.snow, wet: U.wet };
// Snow settles on whatever faces up; rain darkens it.
// Patched materials must say what makes their shader text different, or three would share one program between them.
const keyed = (mat, k) => { mat.userData.key = (mat.userData.key ?? '') + '|' + k; mat.customProgramCacheKey = () => mat.userData.key; return mat; };
function weatherPatch(mat, snowK = 1) {
  keyed(mat, 'w' + snowK);
  mat.onBeforeCompile = s => {
    Object.assign(s.uniforms, isleU);
    s.vertexShader = s.vertexShader.replace('#include <common>', '#include <common>\n varying vec3 vWN; varying vec3 vWP;').replace('#include <fog_vertex>', '#include <fog_vertex>\n vWN = normalize(mat3(modelMatrix) * objectNormal); vWP = (modelMatrix * vec4(transformed, 1.0)).xyz;');
    s.fragmentShader = s.fragmentShader.replace('#include <common>', `#include <common>\n uniform float snow, wet; varying vec3 vWN; varying vec3 vWP; ${GLSL_NOISE}`)
      .replace('#include <color_fragment>', `#include <color_fragment>
        diffuseColor.rgb *= mix(1.0, 0.72, wet);
        float sn = smoothstep(0.35, 0.8, vWN.y + (vnoise(vWP * 3.0) - 0.5) * 0.5) * smoothstep(0.0, 1.0, snow * 1.5 - vnoise(vWP * 0.8) * 0.5) * ${snowK.toFixed(2)};
        diffuseColor.rgb = mix(diffuseColor.rgb, vec3(0.93, 0.96, 1.0), sn);`)
      .replace('#include <roughnessmap_fragment>', '#include <roughnessmap_fragment>\n roughnessFactor = mix(roughnessFactor, 0.45, wet * 0.6);');
  };
  return mat;
}

const island = new THREE.Group(); scene.add(island);
{
  // Top surface: a polar grid, painted by what lies where.
  const RINGS = 96, SEG = 192, pos = [], col = [], idx = [], c = new THREE.Color(), t = new THREE.Color();
  const G1 = new THREE.Color('#4f9632'), G2 = new THREE.Color('#86c24a'), G3 = new THREE.Color('#a9cf55'), DIRT = new THREE.Color('#a57f4c'), SAND = new THREE.Color('#e6d3a0'), BED = new THREE.Color('#2f7f7c'), ROCK = new THREE.Color('#8b8272');
  for (let i = 0; i <= RINGS; i++) for (let j = 0; j <= SEG; j++) {
    const a = j / SEG * TAU, r = Math.pow(i / RINGS, 0.85) * edgeR(a), x = Math.cos(a) * r, z = Math.sin(a) * r, y = groundY(x, z);
    pos.push(x, y, z);
    const n = fbm2(x * 0.5 + 9, z * 0.5), pd = pondD(x, z);
    c.copy(G1).lerp(G2, smooth(range(n, 0.3, 0.7))).lerp(G3, smooth(range(fbm2(x * 0.22, z * 0.22 + 4), 0.55, 0.75)) * 0.7);
    c.lerp(DIRT, (1 - smooth(range(pathDist(x, z), 0.3, 0.62))) * 0.92);
    c.lerp(SAND, smooth(range(pd, 1.32, 1.12)));
    c.lerp(BED, smooth(range(pd, 1.0, 0.7)));
    c.lerp(t.copy(BED).multiplyScalar(0.45), smooth(range(pd, 0.7, 0.2)));
    c.lerp(ROCK, smooth(range(i / RINGS, 0.955, 1.0)) * 0.8);
    col.push(c.r, c.g, c.b);
  }
  for (let i = 0; i < RINGS; i++) for (let j = 0; j < SEG; j++) { const a = i * (SEG + 1) + j, b = a + SEG + 1; idx.push(a, b, a + 1, a + 1, b, b + 1); }
  const g = new THREE.BufferGeometry(); g.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3)); g.setAttribute('color', new THREE.Float32BufferAttribute(col, 3)); g.setIndex(idx); g.computeVertexNormals();
  const top = new THREE.Mesh(g, weatherPatch(new THREE.MeshStandardMaterial({ vertexColors: true, roughness: 1, color: 0x9c9c9c }))); top.receiveShadow = true; island.add(top);

  // The rock underneath: strata, overhangs, a tip that tapers into the clouds.
  const ROWS = 70, p2 = [], c2 = [], i2 = [];
  const R1 = new THREE.Color('#9a8d78'), R2 = new THREE.Color('#6f6557'), R3 = new THREE.Color('#b9a98c'), MOSS = new THREE.Color('#5f9a3c');
  for (let i = 0; i <= ROWS; i++) for (let j = 0; j <= SEG; j++) {
    const a = j / SEG * TAU, v = i / ROWS, ca = Math.cos(a), sa = Math.sin(a), re = edgeR(a), y0 = groundY(ca * re, sa * re), y = y0 - 0.02 - 13 * Math.pow(v, 1.25);
    const lump = (fbm3(ca * 2.2 + 3, y * 0.42, sa * 2.2 + 3) - 0.5) * 3.2 * smooth(range(v, 0.0, 0.25)) + (fbm3(ca * 7, y * 1.6, sa * 7) - 0.5) * 0.8 * smooth(range(v, 0.02, 0.15));
    const strata = (Math.sin(y * 4.2 + fbm2(ca * 3, sa * 3) * 5) * 0.5 + 0.5);
    const r = Math.max(0.05, re * Math.pow(1 - v, 0.62) * (1 - 0.1 * smooth(range(v, 0.0, 0.08))) + lump * (1 - v * 0.6) + strata * 0.18 * smooth(range(v, 0.03, 0.2)));
    p2.push(ca * r, y, sa * r);
    c.copy(R1).lerp(R2, smooth(range(strata, 0.35, 0.75))).lerp(R3, smooth(range(fbm3(ca * 5, y * 0.8, sa * 5), 0.55, 0.75)) * 0.6).multiplyScalar(lerp(1, 0.55, smooth(range(v, 0.1, 0.95))));
    c.lerp(MOSS, smooth(range(v, 0.07, 0.0)) * 0.9);
    c2.push(c.r, c.g, c.b);
  }
  for (let i = 0; i < ROWS; i++) for (let j = 0; j < SEG; j++) { const a = i * (SEG + 1) + j, b = a + SEG + 1; i2.push(a, a + 1, b, a + 1, b + 1, b); }
  const g2 = new THREE.BufferGeometry(); g2.setAttribute('position', new THREE.Float32BufferAttribute(p2, 3)); g2.setAttribute('color', new THREE.Float32BufferAttribute(c2, 3)); g2.setIndex(i2); g2.computeVertexNormals();
  const cliff = new THREE.Mesh(g2, weatherPatch(new THREE.MeshStandardMaterial({ vertexColors: true, roughness: 0.95, flatShading: false }), 0.6)); cliff.castShadow = true; cliff.receiveShadow = true; island.add(cliff);
}

// Rocks: sculpted lumps, a few on the island and a few adrift beside it.
const rockMat = weatherPatch(new THREE.MeshStandardMaterial({ vertexColors: true, roughness: 0.92 }));
function makeRock(seed, sx = 1, sy = 0.7, sz = 1) {
  const r = rng(seed), o = [r() * 9, r() * 9, r() * 9], c1 = new THREE.Color('#9b9486'), c2 = new THREE.Color('#6d675d'), moss = new THREE.Color('#6a9a42');
  const g = sculpt((x, y, z) => sdEllipsoid(x, y, z, 0, 0, 0, sx, sy, sz) + (fbm3(x * 1.6 + o[0], y * 1.6 + o[1], z * 1.6 + o[2]) - 0.5) * 0.55 * Math.min(sx, sy, sz) * 2, { rmax: Math.max(sx, sy, sz) * 1.8, seg: 36, rings: 24,
    paint: (p, n, c) => { c.copy(c1).lerp(c2, fbm3(p.x * 3, p.y * 3, p.z * 3)).lerp(moss, smooth(range(n.y + fbm3(p.x * 4, 2, p.z * 4) * 0.6, 0.9, 1.2)) * 0.8); return 0; } });
  const m = new THREE.Mesh(g, rockMat); m.castShadow = true; m.receiveShadow = true; return m;
}
{
  const r = rng(77);
  const big = makeRock(5, 1.25, 0.62, 1.0); big.position.set(ISLE.rock[0], groundY(...ISLE.rock) + 0.25, ISLE.rock[1]); island.add(big);   // the cat's sunny rock
  for (let i = 0; i < 14; i++) {
    const a = r() * TAU, d = 3 + r() * 8.5, x = Math.cos(a) * d, z = Math.sin(a) * d; if (pondD(x, z) < 1.15 || pathDist(x, z) < 0.6) continue;
    const s = 0.18 + r() * 0.42, m = makeRock(20 + i, s * (0.8 + r() * 0.6), s * 0.6, s * (0.8 + r() * 0.6)); m.position.set(x, groundY(x, z) + s * 0.15, z); m.rotation.y = r() * TAU; island.add(m);
  }
  for (const [x, y, z, s] of [[-19, -3, -6, 2.2], [21, 2, -12, 1.6], [16, -6, 9, 1.1], [-15, 4, -17, 1.3]]) {
    const m = makeRock(90 + x, s, s * 0.8, s * 0.9); m.position.set(x, y, z); m.userData.float = [y, r() * 6]; island.add(m); ISLE.floaters = (ISLE.floaters ?? []).concat(m);
  }
}

// The pond: clear, moving water that mirrors the sky, with lily pads.
const waterMat = new THREE.MeshPhysicalMaterial({ color: 0x1f7f96, roughness: 0.05, metalness: 0, transparent: true, opacity: 0.82, envMapIntensity: 1.6 });
waterMat.onBeforeCompile = s => {
  s.uniforms.time = U.time; s.uniforms.rip = waterMat.userData.rip = { value: new THREE.Vector4(0, 0, -9, 0) };
  s.vertexShader = s.vertexShader.replace('#include <common>', '#include <common>\n varying vec3 vWP;').replace('#include <fog_vertex>', '#include <fog_vertex>\n vWP = (modelMatrix * vec4(transformed, 1.0)).xyz;');
  s.fragmentShader = s.fragmentShader.replace('#include <common>', `#include <common>\n uniform float time; uniform vec4 rip; varying vec3 vWP; ${GLSL_NOISE}`)
    .replace('#include <normal_fragment_maps>', `#include <normal_fragment_maps>
      { vec2 q = vWP.xz; float e = 0.06;
        float h0 = fbm(vec3(q * 1.6 + time * 0.25, time * 0.3)), hx = fbm(vec3((q + vec2(e, 0.0)) * 1.6 + time * 0.25, time * 0.3)), hz = fbm(vec3((q + vec2(0.0, e)) * 1.6 + time * 0.25, time * 0.3));
        vec2 gr = vec2(hx - h0, hz - h0) / e * 0.10;
        float d = length(q - rip.xy), age = time - rip.z;      // rings from the last splash
        if (age > 0.0 && age < 4.0) gr += normalize(q - rip.xy + 1e-4) * sin(d * 9.0 - age * 9.0) * exp(-age * 1.1) * smoothstep(age * 2.4 + 0.3, age * 2.4 - 0.6, d) * 0.5;
        normal = normalize(normal + (viewMatrix * vec4(gr.x, 0.0, gr.y, 0.0)).xyz); }`);
};
{
  const [px, pz, pr] = ISLE.pond, g = new THREE.CircleGeometry(pr * 1.18, 72).rotateX(-PI / 2);
  const w = new THREE.Mesh(g, waterMat); w.position.set(px, ISLE.water, pz); w.receiveShadow = true; island.add(w);
  const padGeo = new THREE.CircleGeometry(1, 20, 0.35, TAU - 0.7).rotateX(-PI / 2), r = rng(8);
  const pads = new THREE.InstancedMesh(padGeo, new THREE.MeshStandardMaterial({ color: 0x4f9c45, roughness: 0.5, side: THREE.DoubleSide }), 16), M = new THREE.Matrix4(), q = new THREE.Quaternion(), e = new THREE.Euler();
  for (let i = 0; i < 16; i++) { const a = r() * TAU, d = (0.25 + r() * 0.5) * pr, s = 0.16 + r() * 0.16; q.setFromEuler(e.set(0, r() * TAU, 0)); M.compose(new THREE.Vector3(px + Math.cos(a) * d, ISLE.water + 0.012, pz + Math.sin(a) * d * 0.8), q, new THREE.Vector3(s, 1, s)); pads.setMatrixAt(i, M); }
  pads.receiveShadow = true; island.add(pads);
}

// A sea of cloud far below, so the island has somewhere to float.
const cloudSea = new THREE.Mesh(new THREE.PlaneGeometry(900, 900).rotateX(-PI / 2), new THREE.ShaderMaterial({
  transparent: true, depthWrite: false, uniforms: { time: U.time, col: { value: new THREE.Color(1, 1, 1) }, shade: { value: new THREE.Color(0.45, 0.6, 0.85) }, fogCol: { value: new THREE.Color() } },
  vertexShader: 'varying vec3 vW; void main(){ vec4 w = modelMatrix * vec4(position, 1.0); vW = w.xyz; gl_Position = projectionMatrix * viewMatrix * w; }',
  fragmentShader: `uniform float time; uniform vec3 col, shade, fogCol; varying vec3 vW; ${GLSL_NOISE}
    void main(){ vec2 q = vW.xz * 0.022 + vec2(time * 0.004, 0.0); float n = fbm(vec3(q, time * 0.01)) * 0.7 + fbm(vec3(q * 3.1, 4.0)) * 0.3;
      float a = smoothstep(0.36, 0.56, n); vec3 c = mix(shade, col, smoothstep(0.42, 0.7, n));
      float d = length(vW.xz - cameraPosition.xz); c = mix(c, fogCol, smoothstep(60.0, 380.0, d));
      gl_FragColor = vec4(c, a * 0.96 * smoothstep(440.0, 250.0, d)); }`,
}));
cloudSea.position.y = -16; cloudSea.renderOrder = -50; scene.add(cloudSea);
