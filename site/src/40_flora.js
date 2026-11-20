// ---- everything that grows: grass, flowers, trees, bushes. All of it leans with one wind. -----------------------------
const WIND_GLSL = `
  uniform float time; uniform vec2 wind;
  vec3 sway(vec3 wp, float h, float stiff){
    float g = sin(dot(wp.xz, wind) * 0.55 + time * 1.7) * 0.5 + sin(dot(wp.xz, wind.yx * vec2(1.0, -1.0)) * 1.3 + time * 2.9) * 0.25 + sin(wp.x * 3.1 + wp.z * 2.7 + time * 4.3) * 0.08;
    return vec3(wind.x, 0.0, wind.y) * (g + 0.25) * h * h * stiff;
  }`;
// Bends a material's vertices with the wind. hExpr is GLSL for "how far up the plant this vertex is" (0 at the root).
function windPatch(mat, hExpr, stiff, extra = null, tag = '') {
  keyed(mat, 'wind' + hExpr + stiff + tag);
  const prev = mat.onBeforeCompile;
  mat.onBeforeCompile = (s, r) => {
    if (prev) prev(s, r);
    s.uniforms.time = U.time; s.uniforms.wind = U.wind;
    s.vertexShader = s.vertexShader.replace('#include <common>', `#include <common>\n ${WIND_GLSL}`)
      .replace('#include <project_vertex>', `
        vec4 wpos = vec4(transformed, 1.0);
        #ifdef USE_INSTANCING
          wpos = instanceMatrix * wpos;
        #endif
        wpos = modelMatrix * wpos;
        wpos.xyz += sway(wpos.xyz, ${hExpr}, ${stiff.toFixed(3)});
        vec4 mvPosition = viewMatrix * wpos;
        gl_Position = projectionMatrix * mvPosition;`);
    if (extra) extra(s);
  };
  return mat;
}
const M4 = new THREE.Matrix4(), Q4 = new THREE.Quaternion(), E4 = new THREE.Euler(), V4 = new THREE.Vector3(), S4 = new THREE.Vector3();
const place = (mesh, i, x, y, z, sx, sy, sz, rx = 0, ry = 0, rz = 0) => { Q4.setFromEuler(E4.set(rx, ry, rz)); mesh.setMatrixAt(i, M4.compose(V4.set(x, y, z), Q4, S4.set(sx, sy, sz))); };
const onLawn = (x, z) => Math.hypot(x, z) < edgeR(Math.atan2(z, x)) * 0.97 && pondD(x, z) > 1.12 && pathDist(x, z) > 0.42 && Math.hypot(x - ISLE.cabin[0], z - ISLE.cabin[1]) > 2.0;

// Grass: tens of thousands of tapered blades.
{
  const g = new THREE.BufferGeometry(), w = MOBILE ? 0.03 : 0.02;
  g.setAttribute('position', new THREE.Float32BufferAttribute([-w, 0, 0, w, 0, 0, -w * 0.75, 0.38, 0, w * 0.75, 0.38, 0, -w * 0.4, 0.72, 0, w * 0.4, 0.72, 0, 0, 1, 0], 3));
  g.setAttribute('normal', new THREE.Float32BufferAttribute(new Array(7).fill([0, 1, 0.35]).flat(), 3));
  g.setIndex([0, 1, 2, 1, 3, 2, 2, 3, 4, 3, 5, 4, 4, 5, 6]);
  const mat = windPatch(weatherPatch(new THREE.MeshStandardMaterial({ roughness: 0.85, side: THREE.DoubleSide }), 0.9), 'position.y', 0.34, s => {
    s.fragmentShader = s.fragmentShader.replace('#include <color_fragment>', '#include <color_fragment>\n diffuseColor.rgb *= mix(0.4, 1.3, vGh);').replace('#include <common>', '#include <common>\n varying float vGh;');
    s.vertexShader = s.vertexShader.replace('#include <common>', '#include <common>\n varying float vGh;').replace('#include <begin_vertex>', '#include <begin_vertex>\n vGh = position.y; transformed.z += position.y * position.y * 0.28;');
  }, 'grass');
  const N = QUALITY.grass, grass = new THREE.InstancedMesh(g, mat, N), r = rng(3), c = new THREE.Color(), a = new THREE.Color('#2f7d2a'), b = new THREE.Color('#6fb23c'), dry = new THREE.Color('#b9c452');
  let n = 0;
  for (let tries = 0; n < N && tries < N * 4; tries++) {
    const x = (r() * 2 - 1) * ISLE.R, z = (r() * 2 - 1) * ISLE.R; if (!onLawn(x, z)) continue;
    const patch = fbm2(x * 0.45 + 7, z * 0.45), tall = fbm2(x * 0.25, z * 0.25 + 9);
    if (r() > 0.35 + patch * 0.9) continue;
    const h = (0.1 + r() * 0.13) * (0.7 + tall * 1.1) * smooth(range(pathDist(x, z), 0.42, 1.0) * 0.6 + 0.4);
    place(grass, n, x, groundY(x, z) - 0.02, z, 1 + r() * 0.8, h, 1, (r() - 0.5) * 0.3, r() * TAU, (r() - 0.5) * 0.3);
    grass.setColorAt(n, c.copy(a).lerp(b, r() * 0.6 + patch * 0.5).lerp(dry, smooth(range(tall, 0.6, 0.8)) * r() * 0.5)); n++;
  }
  grass.count = n; grass.receiveShadow = true; grass.frustumCulled = false; island.add(grass);
}

// Flowers: a stem and a five-petal head, in drifts.
{
  const petal = new THREE.Shape(); for (let i = 0; i <= 40; i++) { const an = i / 40 * TAU, rad = 0.55 + 0.45 * Math.abs(Math.cos(an * 2.5)); i ? petal.lineTo(Math.cos(an) * rad, Math.sin(an) * rad) : petal.moveTo(rad, 0); }
  const head = new THREE.ShapeGeometry(petal, 3).rotateX(-PI / 2 + 0.5);
  const stem = new THREE.CylinderGeometry(0.06, 0.06, 1, 4).translate(0, 0.5, 0), core = new THREE.SphereGeometry(0.3, 8, 6).translate(0, 0.08, 0.05);
  const N = MOBILE ? 500 : 1500, r = rng(12), pal = ['#ffffff', '#ffd23f', '#ff7aa8', '#c58bff', '#ff8a4c', '#fff3a8', '#ff5d6c'].map(h => new THREE.Color(h));
  const mk = (geo, color, rough) => { const m = new THREE.InstancedMesh(geo, windPatch(weatherPatch(new THREE.MeshStandardMaterial({ color, roughness: rough, side: THREE.DoubleSide }), 0.7), 'position.y', 0.3), N); m.receiveShadow = true; m.frustumCulled = false; island.add(m); return m; };
  const heads = mk(head, 0xffffff, 0.7), stems = mk(stem, 0x4f8f3a, 0.9), cores = mk(core, 0xffc21a, 0.6);
  let n = 0;
  for (let tries = 0; n < N && tries < N * 30; tries++) {
    const x = (r() * 2 - 1) * ISLE.R, z = (r() * 2 - 1) * ISLE.R; if (!onLawn(x, z)) continue;
    const drift = fbm2(x * 0.6 + 20, z * 0.6 + 4); if (drift < 0.52 || r() > (drift - 0.5) * 6) continue;
    const h = 0.26 + r() * 0.2, s = 0.07 + r() * 0.05, y = groundY(x, z), ry = r() * TAU, tx = (r() - 0.5) * 0.5, tz = (r() - 0.5) * 0.5;
    place(stems, n, x, y, z, s * 2.2, h, s * 2.2, tx, ry, tz); { const hx = x + Math.sin(tz) * h, hz = z - Math.sin(tx) * h, hy = y + h * Math.cos(tx) * Math.cos(tz); place(heads, n, hx, hy, hz, s, s, s, 0, ry, 0); place(cores, n, hx, hy, hz, s, s, s, 0, ry, 0); }
    heads.setColorAt(n, pal[Math.floor(fbm2(x * 0.3 + 40, z * 0.3) * 13 + r() * 2) % pal.length]); n++;
  }
  heads.count = stems.count = cores.count = n;
}

// Trees: a tapering trunk with boughs, and a crown of thousands of leaf cards shaded as one soft volume.
const leafTex = canvasTex(64, 64, (g, w, h) => {
  g.clearRect(0, 0, w, h); g.fillStyle = '#fff'; g.beginPath(); g.moveTo(32, 2); g.bezierCurveTo(60, 14, 58, 46, 32, 62); g.bezierCurveTo(6, 46, 4, 14, 32, 2); g.fill();
  g.strokeStyle = 'rgba(0,0,0,0.18)'; g.lineWidth = 2; g.beginPath(); g.moveTo(32, 6); g.lineTo(32, 58); g.stroke();
});
const TREES = [];
function makeTree(x, z, { h = 4.6, crown = 2.1, leaf = ['#3f8f3a', '#7cc24a'], seed = 1, leaves = 2600, bark = '#7a5a3c' } = {}) {
  const r = rng(seed), g = new THREE.Group(), y0 = groundY(x, z) - 0.15; g.position.set(x, y0, z);
  const barkMat = weatherPatch(new THREE.MeshStandardMaterial({ color: bark, roughness: 1 }), 0.5);
  const limb = (pts, r0, r1) => { const m = new THREE.Mesh(new THREE.TubeGeometry(new THREE.CatmullRomCurve3(pts), 14, 1, 9), barkMat); const p = m.geometry.attributes.position, nrm = m.geometry.attributes.normal;
    for (let i = 0; i < p.count; i++) { const k = Math.floor(i / 10) / 14, rad = lerp(r0, r1, Math.pow(k, 0.8)) * (1 + (fbm3(p.getX(i) * 3, p.getY(i) * 2, p.getZ(i) * 3) - 0.5) * 0.3); p.setXYZ(i, p.getX(i) - nrm.getX(i) * (1 - rad), p.getY(i) - nrm.getY(i) * (1 - rad), p.getZ(i) - nrm.getZ(i) * (1 - rad)); }
    m.geometry.computeVertexNormals(); m.castShadow = true; m.receiveShadow = true; g.add(m); };
  const lean = [(r() - 0.5) * 0.9, (r() - 0.5) * 0.9], top = new THREE.Vector3(lean[0], h * 0.62, lean[1]);
  limb([new THREE.Vector3(0, 0, 0), new THREE.Vector3(lean[0] * 0.2, h * 0.25, lean[1] * 0.2), new THREE.Vector3(lean[0] * 0.7, h * 0.48, lean[1] * 0.7), top], h * 0.075, h * 0.035);
  const blobs = [[top.x, h * 0.86, top.z, crown]];
  for (let i = 0; i < 4; i++) { const a = i / 4 * TAU + r() * 0.8, d = crown * (0.75 + r() * 0.35), by = h * (0.62 + r() * 0.2), end = new THREE.Vector3(top.x + Math.cos(a) * d, by, top.z + Math.sin(a) * d);
    limb([top.clone().multiplyScalar(0.85), top.clone().lerp(end, 0.5).add(new THREE.Vector3(0, 0.25, 0)), end], h * 0.03, h * 0.012); blobs.push([end.x, end.y + crown * 0.15, end.z, crown * (0.55 + r() * 0.2)]); }
  const N = Math.round(leaves * QUALITY.leaves), card = new THREE.PlaneGeometry(1, 1), aN = new Float32Array(N * 3);
  const mat = windPatch(weatherPatch(new THREE.MeshStandardMaterial({ map: leafTex, alphaTest: 0.5, side: THREE.DoubleSide, roughness: 0.75 }), 1.0), '(wpos.y - modelMatrix[3].y) * 0.16', 0.5, s => {
    s.vertexShader = s.vertexShader.replace('#include <common>', '#include <common>\n attribute vec3 aN;').replace('#include <defaultnormal_vertex>', '#include <defaultnormal_vertex>\n transformedNormal = normalize(normalMatrix * aN);')
      .replace('wpos.xyz += sway(', 'wpos.xyz += vec3(sin(time * 5.0 + wpos.x * 9.0), cos(time * 4.3 + wpos.z * 9.0), 0.0) * 0.012 * (uv.y); wpos.xyz += sway(');
  }, 'leaf');
  const crownMesh = new THREE.InstancedMesh(card, mat, N), ca = new THREE.Color(leaf[0]), cb = new THREE.Color(leaf[1]), c = new THREE.Color();
  for (let i = 0; i < N; i++) {
    const b = blobs[Math.floor(r() * r() * blobs.length) % blobs.length], u = r() * 2 - 1, an = r() * TAU, sq = Math.sqrt(1 - u * u), rad = b[3] * Math.pow(0.55 + r() * 0.45, 0.6);
    const nx = sq * Math.cos(an), ny = u * 0.85 + 0.12, nz = sq * Math.sin(an), s = 0.24 + r() * 0.2;
    place(crownMesh, i, b[0] + nx * rad, b[1] + ny * rad * 0.8, b[2] + nz * rad, s, s * 1.3, s, r() * TAU, r() * TAU, r() * TAU);
    aN[i * 3] = nx; aN[i * 3 + 1] = ny + 0.35; aN[i * 3 + 2] = nz;
    crownMesh.setColorAt(i, c.copy(ca).lerp(cb, smooth(range(ny + r() * 0.6, -0.4, 1.0))).multiplyScalar(0.7 + 0.3 * rad / b[3]));
  }
  card.setAttribute('aN', new THREE.InstancedBufferAttribute(aN, 3));
  crownMesh.castShadow = true; crownMesh.receiveShadow = true; crownMesh.frustumCulled = false; g.add(crownMesh);
  island.add(g); TREES.push({ g, blobs, x, z, y0 }); return g;
}
makeTree(-7.6, 3.4, { h: 5.6, crown: 2.5, seed: 4 });
makeTree(9.6, -3.2, { h: 4.8, crown: 2.1, seed: 9, leaf: ['#3a8a44', '#86c552'] });
makeTree(-1.5, -8.2, { h: 5.2, crown: 2.3, seed: 15 });
const BLOSSOM = makeTree(2.6, -6.8, { h: 3.9, crown: 1.9, seed: 21, leaf: ['#e06f9a', '#f9b6cc'], bark: '#6b4a3a', leaves: 2400 });
makeTree(-9.6, -5.6, { h: 3.6, crown: 1.6, seed: 28, leaf: ['#2f7a3c', '#6fb648'], leaves: 1600 });
makeTree(-10.6, 3.6, { h: 3.2, crown: 1.5, seed: 33, leaves: 1500 });
makeTree(6.2, -8.6, { h: 4.2, crown: 1.9, seed: 37, leaves: 2000, leaf: ['#3a8a44', '#86c552'] });
// Bushes are crowns without a trunk.
for (const [x, z, s, seed] of [[-9.2, 6.2, 0.8, 41], [-0.4, -6.2, 0.9, 42], [-10.4, 0.6, 0.7, 43], [10.6, 3.4, 0.7, 44], [-6.4, -7.2, 0.9, 45], [11.2, -0.8, 0.7, 46]]) {
  const t = makeTree(x, z, { h: 1.25 * s, crown: 0.9 * s, seed, leaves: 700, leaf: ['#357f3a', '#74bb4c'] }); t.children[0].visible = false;
}
