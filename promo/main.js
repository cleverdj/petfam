// PetFam promo video, in a hand-painted animation style: painted backgrounds (the wallpapers baked through a
// brush-stroke filter), crisp cel-shaded pets, painted clouds, drifting petals. A three.js scene driven entirely by time, so every frame can be rendered on its own
// (render.mjs steps through renderAt(t) and records each frame). Open index.html in a browser to watch it live.
import * as THREE from 'three';
import { EffectComposer } from 'three/addons/postprocessing/EffectComposer.js';
import { RenderPass } from 'three/addons/postprocessing/RenderPass.js';
import { ShaderPass } from 'three/addons/postprocessing/ShaderPass.js';
import { UnrealBloomPass } from 'three/addons/postprocessing/UnrealBloomPass.js';
import { OutputPass } from 'three/addons/postprocessing/OutputPass.js';
import { DURATION, CAPTIONS, BUBBLES, CHIPS, SCAN, TYPE, PRESS, BUILD, LINEUP } from './timeline.js';

const qs = new URLSearchParams(location.search);
const LANG = qs.get('lang') === 'en' ? 'en' : 'ko';
const L = (ko, en) => (LANG === 'ko' ? ko : en);
document.documentElement.classList.add(LANG);
document.documentElement.lang = LANG;

// ---------------------------------------------------------------------------------------------------- math
const clamp = (x, a = 0, b = 1) => Math.min(b, Math.max(a, x));
const lerp = (a, b, k) => a + (b - a) * k;
const range = (t, a, b) => clamp((t - a) / (b - a));
const smooth = k => { k = clamp(k); return k * k * (3 - 2 * k); };
const ease = k => { k = clamp(k); return k < 0.5 ? 4 * k * k * k : 1 - Math.pow(-2 * k + 2, 3) / 2; };
const sine = k => { k = clamp(k); return -(Math.cos(Math.PI * k) - 1) / 2; };
const backOut = k => { k = clamp(k); const c1 = 1.9, c3 = c1 + 1; return 1 + c3 * Math.pow(k - 1, 3) + c1 * Math.pow(k - 1, 2); };
const win = (t, a, b, fi = 0.35, fo = 0.3) => smooth(range(t, a, a + fi)) * (1 - smooth(range(t, b - fo, b)));
const arc = (t, a, b, h) => { const k = range(t, a, b); return k > 0 && k < 1 ? 4 * h * k * (1 - k) : 0; };
function rng(seed) {
  let s = (seed * 2654435761) >>> 0 || 1;
  return () => { s ^= s << 13; s >>>= 0; s ^= s >>> 17; s ^= s << 5; s >>>= 0; return s / 4294967296; };
}

// ---------------------------------------------------------------------------------------------------- world
// The wallpaper is a 16 x 9 plane at z = 0; pixel (u, v) of the 2560 x 1440 picture maps onto it.
const W = 1920, H = 1080, SW = 16, SH = 9;
const wx = u => (u / 2560 - 0.5) * SW;
const wy = v => (0.5 - v / 1440) * SH;
const pu = x => (x / SW + 0.5) * 2560;
const depth = v => lerp(0.62, 1.22, clamp((v - 650) / 790));
const zOf = v => 0.75 + (v - 900) / 1440 * 1.2;

const canvas = document.getElementById('c');
const renderer = new THREE.WebGLRenderer({ canvas, antialias: true, preserveDrawingBuffer: true });
renderer.setPixelRatio(1);
renderer.setSize(W, H, false);
renderer.outputColorSpace = THREE.SRGBColorSpace;
renderer.localClippingEnabled = true;
// Sky effects stay inside the screen, also while the camera still looks at the monitor from afar.
const SCREEN_CLIP = [new THREE.Plane(new THREE.Vector3(1, 0, 0), 8), new THREE.Plane(new THREE.Vector3(-1, 0, 0), 8), new THREE.Plane(new THREE.Vector3(0, 1, 0), 4.5), new THREE.Plane(new THREE.Vector3(0, -1, 0), 4.5)];

const scene = new THREE.Scene();
const camera = new THREE.PerspectiveCamera(30, W / H, 0.1, 400);
const CAM_Z = (SH / 2) / Math.tan(THREE.MathUtils.degToRad(15));

function canvasTex(w, h, draw) {
  const c = document.createElement('canvas'); c.width = w; c.height = h;
  draw(c.getContext('2d'), w, h);
  const t = new THREE.CanvasTexture(c); t.colorSpace = THREE.SRGBColorSpace; t.anisotropy = 8;
  return t;
}
function roundRect(g, x, y, w, h, r) {
  g.beginPath(); g.moveTo(x + r, y); g.arcTo(x + w, y, x + w, y + h, r); g.arcTo(x + w, y + h, x, y + h, r);
  g.arcTo(x, y + h, x, y, r); g.arcTo(x, y, x + w, y, r); g.closePath();
}
function shapeRoundRect(s, x, y, w, h, r) {
  s.moveTo(x + r, y); s.lineTo(x + w - r, y); s.quadraticCurveTo(x + w, y, x + w, y + r);
  s.lineTo(x + w, y + h - r); s.quadraticCurveTo(x + w, y + h, x + w - r, y + h);
  s.lineTo(x + r, y + h); s.quadraticCurveTo(x, y + h, x, y + h - r);
  s.lineTo(x, y + r); s.quadraticCurveTo(x, y, x + r, y);
}

scene.background = canvasTex(16, 512, (g, w, h) => {
  const gr = g.createLinearGradient(0, 0, 0, h);
  gr.addColorStop(0, '#3a4377'); gr.addColorStop(0.5, '#6d6595'); gr.addColorStop(1, '#c98f7a');
  g.fillStyle = gr; g.fillRect(0, 0, w, h);
});

const hemi = new THREE.HemisphereLight('#ffffff', '#c9b89c', 1.7);
scene.add(hemi);
const sun = new THREE.DirectionalLight('#fff3e0', 2.4);
sun.position.set(-4, 7, 9);
scene.add(sun);

const blobTex = canvasTex(128, 128, (g, w, h) => {
  const gr = g.createRadialGradient(w / 2, h / 2, 0, w / 2, h / 2, w / 2);
  gr.addColorStop(0, 'rgba(255,255,255,1)'); gr.addColorStop(0.5, 'rgba(255,255,255,0.55)'); gr.addColorStop(1, 'rgba(255,255,255,0)');
  g.fillStyle = gr; g.fillRect(0, 0, w, h);
});
const softRectTex = canvasTex(256, 256, (g) => {
  g.filter = 'blur(14px)'; g.fillStyle = '#fff'; roundRect(g, 30, 30, 196, 196, 20); g.fill();
});

// ---------------------------------------------------------------------------------------------------- wallpaper
const loader = new THREE.TextureLoader();
async function wallpaper(name) {
  const t = await loader.loadAsync(`wallpapers/${name}.jpg`);
  t.colorSpace = THREE.SRGBColorSpace; t.anisotropy = 8;
  return t;
}
const NOISE = `
  float hash(vec2 p){ p = fract(p * vec2(123.34, 456.21)); p += dot(p, p + 45.32); return fract(p.x * p.y); }
  float vnoise(vec2 p){ vec2 i = floor(p), f = fract(p); f = f * f * (3.0 - 2.0 * f);
    return mix(mix(hash(i), hash(i + vec2(1, 0)), f.x), mix(hash(i + vec2(0, 1)), hash(i + vec2(1, 1)), f.x), f.y); }
  float fbm(vec2 p){ float v = 0.0, a = 0.5; for (int i = 0; i < 5; i++){ v += a * vnoise(p); p *= 2.03; a *= 0.5; } return v; }`;
const fsCam = new THREE.OrthographicCamera(-1, 1, 1, -1, 0, 1);
const fsScene = new THREE.Scene();
const fsQuad = new THREE.Mesh(new THREE.PlaneGeometry(2, 2));
fsScene.add(fsQuad);
const FS_VERT = 'varying vec2 vUv; void main(){ vUv = uv; gl_Position = vec4(position.xy, 0.0, 1.0); }';
const target = () => new THREE.WebGLRenderTarget(2560, 1440, { type: THREE.HalfFloatType, depthBuffer: false });
function runPass(frag, uniforms, out) {
  fsQuad.material = new THREE.ShaderMaterial({ uniforms: { res: { value: new THREE.Vector2(2560, 1440) }, ...uniforms }, vertexShader: FS_VERT, fragmentShader: frag });
  renderer.setRenderTarget(out); renderer.render(fsScene, fsCam); renderer.setRenderTarget(null);
  fsQuad.material.dispose();
  return out.texture;
}
// Turns a flat vector wallpaper into a painted background: wobbly hand-drawn edges and uneven pigment, brush
// strokes (a Kuwahara filter), pigment pooling at the edges, a little warmth, and paper grain.
function paint(tex, horizon) {
  const a = runPass(`uniform sampler2D map; uniform vec2 res; uniform float horizon; varying vec2 vUv; ${NOISE}
    void main(){
      vec2 p = vUv * res;
      vec2 warp = vec2(fbm(p / 70.0), fbm(p / 70.0 + 17.3)) - 0.5;
      vec3 c = texture2D(map, vUv + warp * 5.0 / res).rgb;
      float pig = fbm(p / 160.0) - 0.5, fine = fbm(p / 22.0) - 0.5;
      c *= 1.0 + pig * 0.14 + fine * 0.07;
      // Sky: a deep blue overhead fading to a pale haze at the horizon, in uneven washes.
      float sky = smoothstep(0.1, 0.25, c.b - c.r) * smoothstep(horizon, horizon + 0.1, vUv.y) * smoothstep(0.12, 0.35, c.b) * (1.0 - smoothstep(0.45, 0.65, dot(c, vec3(0.3, 0.59, 0.11))));
      vec3 skyCol = mix(vec3(0.62, 0.80, 0.90), vec3(0.06, 0.26, 0.70), smoothstep(horizon, 1.0, vUv.y));
      skyCol *= 0.94 + 0.12 * fbm(vec2(p.x / 260.0, p.y / 90.0));
      c = mix(c, skyCol, sky * 0.8);
      // Grass: dabs and strokes of darker blue-green and sunny yellow-green, like gouache.
      float grass = smoothstep(0.02, 0.1, c.g - max(c.r, c.b));
      float stroke = fbm(vec2(p.x / 7.0, p.y / 26.0) + fbm(p / 90.0) * 3.0);
      float dab = fbm(p / 38.0);
      vec3 deep = c * vec3(0.5, 0.7, 0.74), sunny = c * vec3(1.2, 1.08, 0.62);
      vec3 g = mix(deep, c, smoothstep(0.3, 0.55, stroke));
      g = mix(g, sunny, smoothstep(0.58, 0.8, dab) * 0.55);
      c = mix(c, g * 0.9, grass);
      c = mix(c, c * vec3(1.05, 1.0, 0.92), smoothstep(0.2, 0.8, dot(c, vec3(0.3, 0.6, 0.1))));
      float l = dot(c, vec3(0.3, 0.59, 0.11));
      c = max(mix(vec3(l), c, 1.03), 0.0);
      gl_FragColor = vec4(c, 1.0);
    }`, { map: { value: tex }, horizon: { value: horizon } }, target());
  const b = runPass(`uniform sampler2D map; uniform vec2 res; varying vec2 vUv;
    void main(){
      const int R = 7;
      vec3 m0 = vec3(0), m1 = vec3(0), m2 = vec3(0), m3 = vec3(0), s0 = vec3(0), s1 = vec3(0), s2 = vec3(0), s3 = vec3(0);
      for (int j = 0; j <= R; j++) for (int i = 0; i <= R; i++) {
        vec3 c;
        c = texture2D(map, vUv + vec2(-i, -j) / res).rgb; m0 += c; s0 += c * c;
        c = texture2D(map, vUv + vec2( i, -j) / res).rgb; m1 += c; s1 += c * c;
        c = texture2D(map, vUv + vec2(-i,  j) / res).rgb; m2 += c; s2 += c * c;
        c = texture2D(map, vUv + vec2( i,  j) / res).rgb; m3 += c; s3 += c * c;
      }
      float n = float((R + 1) * (R + 1));
      m0 /= n; m1 /= n; m2 /= n; m3 /= n;
      vec4 v = vec4(dot(abs(s0 / n - m0 * m0), vec3(1)), dot(abs(s1 / n - m1 * m1), vec3(1)), dot(abs(s2 / n - m2 * m2), vec3(1)), dot(abs(s3 / n - m3 * m3), vec3(1)));
      vec4 k = v * 200.0, w = 1.0 / (1.0 + k * k);
      gl_FragColor = vec4(max((m0 * w.x + m1 * w.y + m2 * w.z + m3 * w.w) / (w.x + w.y + w.z + w.w), 0.0), 1.0);
    }`, { map: { value: a } }, target());
  return runPass(`uniform sampler2D map; uniform vec2 res; varying vec2 vUv; ${NOISE}
    float L(vec2 o){ return dot(texture2D(map, vUv + o / res).rgb, vec3(0.3, 0.59, 0.11)); }
    void main(){
      vec3 c = texture2D(map, vUv).rgb;
      float gx = L(vec2(2, 0)) - L(vec2(-2, 0)), gy = L(vec2(0, 2)) - L(vec2(0, -2));
      float edge = clamp(length(vec2(gx, gy)) * 2.2, 0.0, 0.28);
      c *= 1.0 - edge;
      float paper = fbm(vUv * res / 3.0) * 0.6 + fbm(vUv * res / 40.0) * 0.4;
      c *= 0.955 + paper * 0.07;
      float l = dot(c, vec3(0.3, 0.59, 0.11));
      c = mix(vec3(l), c, 1.12);
      gl_FragColor = vec4(max(c, 0.0), 1.0);
    }`, { map: { value: b } }, target());
}
const [meadow, seaside, garden] = (await Promise.all(['sunny-meadow', 'seaside', 'cozy-garden'].map(wallpaper))).map((t, i) => paint(t, [0.6, 0.645, 0.73][i]));

const backdrop = new THREE.Mesh(new THREE.PlaneGeometry(SW, SH), new THREE.ShaderMaterial({
  uniforms: {
    a: { value: meadow }, b: { value: meadow }, wipe: { value: 0 }, tint: { value: new THREE.Color(1, 1, 1) },
    brand: { value: 0 }, brandA: { value: new THREE.Color('#8fc6ec') }, brandB: { value: new THREE.Color('#fdf1dc') },
  },
  vertexShader: 'varying vec2 vUv; void main(){ vUv = uv; gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 1.0); }',
  fragmentShader: `
    uniform sampler2D a; uniform sampler2D b; uniform float wipe; uniform vec3 tint;
    uniform float brand; uniform vec3 brandA; uniform vec3 brandB;
    varying vec2 vUv;
    void main(){
      vec3 ca = texture2D(a, vUv).rgb, cb = texture2D(b, vUv).rgb;
      float d = length((vUv - vec2(0.5, 0.42)) * vec2(16.0 / 9.0, 1.0));
      float r = wipe * 2.15;
      float m = 1.0 - smoothstep(r - 0.03, r, d);
      vec3 c = mix(ca, cb, m);
      float ring = smoothstep(r - 0.07, r - 0.012, d) * (1.0 - smoothstep(r - 0.012, r + 0.03, d));
      c += ring * 0.85 * step(0.001, wipe) * step(wipe, 0.999);
      c *= tint;
      vec3 bc = mix(brandB, brandA, vUv.y);
      gl_FragColor = vec4(mix(c, bc, brand), 1.0);
      #include <colorspace_fragment>
    }`,
}));
scene.add(backdrop);

// ---------------------------------------------------------------------------------------------------- toon look
const gradMap = (() => {
  const t = new THREE.DataTexture(new Uint8Array([168, 255]), 2, 1, THREE.RedFormat);
  t.minFilter = t.magFilter = THREE.NearestFilter; t.needsUpdate = true; return t;
})();
const toon = (c, extra = {}) => new THREE.MeshToonMaterial({ color: c, gradientMap: gradMap, ...extra });
const OUT = new THREE.MeshBasicMaterial({ color: '#4b3326', side: THREE.BackSide });
const SPH = new THREE.SphereGeometry(1, 32, 20);
const CONE = new THREE.ConeGeometry(1, 1, 18);
const LEG = new THREE.CapsuleGeometry(0.075, 0.24, 4, 12);
const PLANE = new THREE.PlaneGeometry(1, 1);

function add(parent, geo, mat, pos, scl, outline = true) {
  const m = new THREE.Mesh(geo, mat);
  m.position.set(...pos);
  if (scl) m.scale.set(...scl);
  parent.add(m);
  if (outline) { const o = new THREE.Mesh(geo, OUT); o.scale.setScalar(1.07); m.add(o); }
  return m;
}

// ---------------------------------------------------------------------------------------------------- monitor
const monitor = new THREE.Group();
scene.add(monitor);
{
  const dark = toon('#272b35');
  const outer = new THREE.Shape(); shapeRoundRect(outer, -8.8, -5.3, 17.6, 10.6, 0.45);
  const hole = new THREE.Path(); hole.moveTo(-8.15, -4.65); hole.lineTo(-8.15, 4.65); hole.lineTo(8.15, 4.65); hole.lineTo(8.15, -4.65); hole.lineTo(-8.15, -4.65);
  outer.holes.push(hole);
  const bezel = new THREE.Mesh(new THREE.ExtrudeGeometry(outer, { depth: 1.2, bevelEnabled: true, bevelThickness: 0.08, bevelSize: 0.06, bevelSegments: 3, curveSegments: 10 }), dark);
  bezel.position.z = -0.3; monitor.add(bezel);
  add(monitor, new THREE.BoxGeometry(17.6, 10.6, 0.5), dark, [0, 0, -0.6], null, false);
  add(monitor, new THREE.BoxGeometry(1.4, 2.6, 0.5), dark, [0, -6.3, -1.0], null, false);
  add(monitor, new THREE.CylinderGeometry(3.2, 3.4, 0.3, 48), dark, [0, -7.6, -1.0], [1, 1, 0.55], false);
  const led = new THREE.Mesh(SPH, new THREE.MeshBasicMaterial({ color: '#7dd3fc' }));
  led.position.set(0, -5.0, 0.95); led.scale.setScalar(0.06); monitor.add(led);
  add(scene, new THREE.BoxGeometry(80, 0.4, 30), toon('#7a5640'), [0, -7.95, -4], null, false);
  const glow = new THREE.Mesh(PLANE, new THREE.MeshBasicMaterial({ map: blobTex, color: '#ffc58f', transparent: true, opacity: 0.45, depthWrite: false }));
  glow.position.set(0, 0, -1.5); glow.scale.set(40, 26, 1); scene.add(glow);
}

// ---------------------------------------------------------------------------------------------------- app windows
function roundPlane(w, h, r) {
  const s = new THREE.Shape(); shapeRoundRect(s, -w / 2, -h / 2, w, h, r);
  const g = new THREE.ShapeGeometry(s, 12);
  const p = g.attributes.position, uv = g.attributes.uv;
  for (let i = 0; i < p.count; i++) uv.setXY(i, (p.getX(i) + w / 2) / w, (p.getY(i) + h / 2) / h);
  return g;
}
function windowTexture(kind) {
  return canvasTex(1200, 720, (g, w, h) => {
    const dark = kind === 'code';
    g.fillStyle = dark ? '#1f2333' : '#ffffff'; g.fillRect(0, 0, w, h);
    g.fillStyle = dark ? '#2b3046' : '#eef1f6'; g.fillRect(0, 0, w, 64);
    g.fillStyle = dark ? '#c9d1e8' : '#3b4252'; g.font = '600 28px "Segoe UI", sans-serif';
    g.fillText(dark ? 'main.cs — Editor' : 'Weekly report — Notes', 28, 42);
    g.strokeStyle = dark ? '#c9d1e8' : '#3b4252'; g.lineWidth = 3;
    g.beginPath(); g.moveTo(1040, 34); g.lineTo(1066, 34); g.stroke();
    g.strokeRect(1096, 22, 22, 22);
    g.beginPath(); g.moveTo(1146, 22); g.lineTo(1168, 44); g.moveTo(1168, 22); g.lineTo(1146, 44); g.stroke();
    const r = rng(dark ? 7 : 3);
    if (dark) {
      g.fillStyle = '#262b3d'; g.fillRect(0, 64, 70, h - 64);
      const cols = ['#7aa2f7', '#9ece6a', '#e0af68', '#bb9af7', '#7dcfff', '#c0caf5'];
      for (let i = 0; i < 15; i++) {
        const y = 100 + i * 40, ind = [0, 1, 1, 2, 2, 2, 1, 2, 3, 3, 2, 1, 0, 1, 1][i] * 48;
        g.fillStyle = '#4b5270'; g.fillRect(24, y - 12, 26, 12);
        let x = 100 + ind;
        for (let k = 0; k < 2 + Math.floor(r() * 3); k++) { const ww = 50 + r() * 160; g.fillStyle = cols[Math.floor(r() * cols.length)]; roundRect(g, x, y - 16, ww, 18, 9); g.fill(); x += ww + 18; }
      }
    } else {
      g.fillStyle = '#1f2937'; roundRect(g, 60, 110, 520, 34, 12); g.fill();
      for (let i = 0; i < 4; i++) { g.fillStyle = '#cfd6e2'; roundRect(g, 60, 180 + i * 42, 640 + r() * 380, 18, 9); g.fill(); }
      g.fillStyle = '#dbeafe'; roundRect(g, 60, 370, 420, 260, 18); g.fill();
      g.fillStyle = '#93c5fd'; for (let i = 0; i < 5; i++) { const bh = 40 + r() * 150; g.fillRect(100 + i * 72, 600 - bh, 44, bh); }
      for (let i = 0; i < 6; i++) { g.fillStyle = '#cfd6e2'; roundRect(g, 520, 380 + i * 40, 300 + r() * 300, 18, 9); g.fill(); }
    }
  });
}
function makeWindow(kind, w, h) {
  const g = new THREE.Group();
  const shadow = new THREE.Mesh(PLANE, new THREE.MeshBasicMaterial({ map: softRectTex, color: '#000', transparent: true, opacity: 0.4, depthWrite: false }));
  shadow.scale.set(w * 1.18, h * 1.25, 1); shadow.position.set(0.12, -0.22, -0.04); g.add(shadow);
  const m = new THREE.Mesh(roundPlane(w, h, 0.16), new THREE.MeshBasicMaterial({ map: windowTexture(kind), transparent: true }));
  g.add(m);
  g.userData.mats = [[shadow.material, 0.4], [m.material, 1]];
  scene.add(g);
  return g;
}
const winA = makeWindow('doc', 6.0, 3.6);
const winB = makeWindow('code', 6.2, 3.8);

// ---------------------------------------------------------------------------------------------------- pets
function makePet(o) {
  const p = { o, hips: [], ears: [], eyes: [] };
  const cat = o.kind === 'cat';
  const k = o.legs ?? 1;
  const root = new THREE.Group(); scene.add(root); p.root = root;
  const yawG = new THREE.Group(); root.add(yawG); p.yawG = yawG;
  const body = new THREE.Group(); yawG.add(body); p.body = body;
  const coat = toon(o.coat), belly = toon(o.belly ?? o.coat), ink = toon('#2a201c');
  const hipY = 0.4 * k;
  add(body, SPH, coat, [0, hipY + 0.1, 0], [0.3, 0.27, 0.47]);
  add(body, SPH, belly, [0, hipY + 0.02, 0.2], [0.22, 0.18, 0.26], false);
  for (const [x, z] of [[-0.16, 0.28], [0.16, 0.28], [-0.16, -0.28], [0.16, -0.28]]) {
    const hip = new THREE.Group(); hip.position.set(x, hipY, z); body.add(hip);
    add(hip, LEG, coat, [0, -0.18 * k, 0], [1, k, 1]);
    add(hip, SPH, belly, [0, -0.37 * k, 0.025], [0.085, 0.055, 0.105]);
    p.hips.push(hip);
  }
  const head = new THREE.Group(); head.position.set(0, hipY + 0.4, 0.4); body.add(head); p.head = head;
  add(head, SPH, coat, [0, 0.06, 0], cat ? [0.29, 0.25, 0.26] : [0.28, 0.27, 0.27]);
  if (o.mask) add(head, SPH, belly, [0, 0.02, 0.1], [0.22, 0.17, 0.2], false);
  if (!cat) {
    add(head, SPH, belly, [0, -0.04, 0.19], [0.15, 0.12, 0.15]);
    add(head, SPH, ink, [0, 0.02, 0.335], [0.055, 0.042, 0.04], false);
  } else {
    add(head, SPH, belly, [0, -0.03, 0.2], [0.13, 0.09, 0.09], false);
    add(head, SPH, toon('#e98a9a'), [0, 0.02, 0.285], [0.03, 0.022, 0.02], false);
  }
  const white = new THREE.MeshBasicMaterial({ color: '#ffffff' });
  const blushMat = new THREE.MeshBasicMaterial({ color: '#ff8fa3', transparent: true, opacity: 0.55 });
  for (const s of [-1, 1]) {
    const eye = add(head, SPH, ink, [s * 0.11, 0.11, 0.215], [0.042, 0.054, 0.035], false);
    const hl = new THREE.Mesh(SPH, white); hl.position.set(0.3, 0.38, 0.82); hl.scale.setScalar(0.32); eye.add(hl);
    p.eyes.push(eye);
    const blush = new THREE.Mesh(SPH, blushMat); blush.position.set(s * 0.175, 0, 0.19); blush.scale.set(0.05, 0.028, 0.02); head.add(blush);
    const ear = new THREE.Group(); head.add(ear);
    const earMat = toon(o.ear ?? o.coat);
    if (cat || o.ears === 'up') {
      ear.position.set(s * 0.15, 0.27, -0.01); ear.rotation.z = -s * 0.28;
      add(ear, CONE, earMat, [0, 0.08, 0], [0.1, 0.17, 0.07]);
      add(ear, CONE, toon('#f4a7b4'), [0, 0.06, 0.045], [0.055, 0.11, 0.03], false);
    } else {
      ear.position.set(s * 0.24, 0.17, -0.02); ear.rotation.z = s * 0.3;
      add(ear, SPH, earMat, [s * 0.03, -0.14, 0], [0.085, 0.19, 0.13]);
    }
    p.ears.push(ear);
  }
  const tail = new THREE.Group(); tail.position.set(0, hipY + 0.2, -0.42); body.add(tail); p.tail = tail;
  if (cat) { add(tail, new THREE.CapsuleGeometry(0.05, 0.5, 4, 10), coat, [0, 0.28, 0]); tail.rotation.x = -0.45; }
  else { add(tail, new THREE.CapsuleGeometry(0.06, 0.2, 4, 10), o.tail ? toon(o.tail) : coat, [0, 0.15, 0]); tail.rotation.x = -0.85; }

  const hat = new THREE.Group(); hat.position.set(0.04, 0.3, 0); hat.rotation.z = -0.22; head.add(hat); p.hat = hat;
  const hatTex = canvasTex(256, 256, (g, w, h) => {
    g.fillStyle = '#ff6b8b'; g.fillRect(0, 0, w, h);
    g.fillStyle = '#ffd166'; for (let i = 0; i < 6; i++) g.fillRect(0, i * 48 + 10, w, 20);
  });
  add(hat, CONE, toon('#ffffff', { map: hatTex }), [0, 0.17, 0], [0.13, 0.34, 0.13]);
  add(hat, SPH, toon('#7cd4ff'), [0, 0.36, 0], [0.05, 0.05, 0.05]);
  hat.visible = false;

  p.shadow = new THREE.Mesh(PLANE, new THREE.MeshBasicMaterial({ map: blobTex, color: '#000', transparent: true, opacity: 0.32, depthWrite: false }));
  scene.add(p.shadow);
  p.seed = o.seed ?? 1;
  return p;
}

function pose(p, st, t) {
  p.root.visible = p.shadow.visible = !!st.show && (st.pop ?? 1) > 0.001;
  if (!p.root.visible) return;
  const s = st.s * (st.pop ?? 1);
  const k = p.o.legs ?? 1;
  const jump = st.jump ?? 0;
  p.root.position.set(st.x, st.y + jump * st.s - (st.swim ?? 0) * 0.32 * st.s, st.z);
  p.root.scale.setScalar(s);
  p.yawG.rotation.y = st.yaw;
  const a = st.amp ?? 0, w = st.walk ?? 0, sit = st.sit ?? 0, lie = st.lie ?? 0;
  const sw = Math.sin(w) * a * (1 - sit) * (1 - lie);
  const fly = clamp(jump * 3) * (1 - (st.swim ?? 0));
  p.hips[0].rotation.x = sw + 0.5 * sit - 1.3 * lie + 0.7 * fly;
  p.hips[1].rotation.x = -sw + 0.5 * sit - 1.3 * lie + 0.7 * fly;
  p.hips[2].rotation.x = -sw - 1.1 * sit + 1.3 * lie - 0.7 * fly;
  p.hips[3].rotation.x = sw - 1.1 * sit + 1.3 * lie - 0.7 * fly;
  p.hips[0].scale.y = p.hips[1].scale.y = 1 + 0.3 * sit;
  p.body.rotation.x = -0.5 * sit + (st.tilt ?? 0);
  p.body.position.y = Math.abs(Math.cos(w)) * 0.05 * a - 0.2 * lie * k;
  const shake = st.shake ?? 0;
  p.body.rotation.z = Math.sin(t * 40) * 0.32 * shake;
  p.head.rotation.set(0.45 * sit + (st.nod ?? 0), st.look ?? 0, -p.body.rotation.z * 0.7 + (st.tiltHead ?? 0));
  p.head.scale.setScalar(st.headScale ?? 1);
  const cat = p.o.kind === 'cat';
  const wag = st.wag ?? (cat ? 0.25 : 0.5);
  p.tail.rotation.y = Math.sin(t * (cat ? 2.2 : 13) + p.seed) * 0.55 * wag;
  p.tail.rotation.x = (cat ? -0.45 : -0.85) + lie * 0.9;
  for (const [i, ear] of p.ears.entries()) ear.rotation.x = Math.sin(w + i) * 0.12 * a + shake * Math.sin(t * 40 + i) * 0.4;
  const blink = ((t * 0.29 + p.seed * 0.37) % 1) < 0.035;
  const closed = st.happy || blink;
  for (const e of p.eyes) e.scale.y = closed ? 0.009 : 0.054;
  p.hat.visible = !!st.hat;
  if (st.hat) p.hat.scale.setScalar(backOut(st.hat));
  p.shadow.position.set(st.x, st.y + 0.015 * st.s, st.z - 0.32 * st.s);
  p.shadow.scale.set(1.05 * s * (1 - 0.35 * clamp(jump)), 0.24 * s, 1);
  p.shadow.material.opacity = 0.32 * (1 - (st.swim ?? 0)) * (1 - 0.5 * clamp(jump));
}

// Walks along keys [time, u, v]: eased between keys, standing still where two keys share a place.
function track(keys, dir0 = 1) {
  const cum = [0];
  for (let i = 1; i < keys.length; i++) cum.push(cum[i - 1] + Math.hypot(keys[i][1] - keys[i - 1][1], keys[i][2] - keys[i - 1][2]));
  const dirOf = i => { for (let j = i; j >= 1; j--) { const d = keys[j][1] - keys[j - 1][1]; if (Math.abs(d) > 1) return Math.sign(d); } return dir0; };
  return t => {
    let i = 1;
    while (i < keys.length && t > keys[i][0]) i++;
    if (i >= keys.length) { const e = keys.at(-1); return { u: e[1], v: e[2], dist: cum.at(-1), amp: 0, dir: dirOf(keys.length - 1) }; }
    const [t0, u0, v0] = keys[i - 1], [t1, u1, v1] = keys[i];
    if (t <= t0) return { u: u0, v: v0, dist: cum[i - 1], amp: 0, dir: dirOf(i - 1) };
    const k = (t - t0) / (t1 - t0), e = sine(k), len = cum[i] - cum[i - 1];
    const moving = len > 1;
    const dir = moving ? lerp(dirOf(i - 1), dirOf(i), smooth(k * (t1 - t0) / 0.35)) : dirOf(i);
    return { u: lerp(u0, u1, e), v: lerp(v0, v1, e), dist: cum[i - 1] + len * e, amp: moving ? Math.min(1, Math.sin(Math.PI * k) * 2.5) * 0.75 : 0, dir };
  };
}
const SIDE = Math.PI / 2 - 0.55;
function base(tk) {
  const s = depth(tk.v);
  return { show: true, s, x: wx(tk.u), y: wy(tk.v), z: zOf(tk.v), yaw: tk.dir * SIDE, walk: tk.dist * 0.045 / s, amp: tk.amp, dir: tk.dir };
}
const faceCam = (st, k, look = 0.42) => { st.yaw = lerp(st.yaw, st.dir * 0.5, k); st.look = -st.dir * look * k; };

const dog = makePet({ kind: 'dog', coat: '#e7b36e', belly: '#fff1dc', ear: '#c98c4b', seed: 1 });
const cat = makePet({ kind: 'cat', coat: '#8f9aab', belly: '#f4f1ec', seed: 2 });
const kitten = makePet({ kind: 'cat', coat: '#f2a65a', belly: '#fff1df', seed: 3 });
const corgi = makePet({ kind: 'dog', coat: '#e0913f', belly: '#fff6ea', ears: 'up', legs: 0.68, seed: 4 });
const husky = makePet({ kind: 'dog', coat: '#7d8696', belly: '#f4f6fa', ears: 'up', mask: true, seed: 5 });
const blackCat = makePet({ kind: 'cat', coat: '#3c3c48', belly: '#55556a', seed: 6 });
const shiba = makePet({ kind: 'dog', coat: '#d98545', belly: '#fff3e2', ears: 'up', tail: '#f6e6d2', seed: 7 });
const LINE = [[corgi, 420], [cat, 700], [husky, 980], [kitten, 1280], [dog, 1580], [blackCat, 1860], [shiba, 2140]];

// ---------------------------------------------------------------------------------------------------- house
const HOUSE = { u: 560, v: 905, s: 0.85, yaw: 0.38, z: 0.6 };
const house = new THREE.Group();
house.position.set(wx(HOUSE.u), wy(HOUSE.v), HOUSE.z);
house.rotation.y = HOUSE.yaw;
scene.add(house);
const hp = {};
{
  const logs = canvasTex(256, 256, (g, w, h) => {
    for (let i = 0; i < 8; i++) {
      g.fillStyle = i % 2 ? '#b07a45' : '#a06b3a'; g.fillRect(0, i * 32, w, 32);
      g.fillStyle = 'rgba(70,40,20,0.55)'; g.fillRect(0, i * 32 + 29, w, 3);
    }
  });
  hp.walls = new THREE.Group(); house.add(hp.walls);
  add(hp.walls, new THREE.BoxGeometry(2.0, 1.3, 1.4), toon('#ffffff', { map: logs }), [0, 0.65, 0]);
  const tri = new THREE.Shape(); tri.moveTo(-1.28, 0); tri.lineTo(1.28, 0); tri.lineTo(0, 1.0); tri.lineTo(-1.28, 0);
  const roofGeo = new THREE.ExtrudeGeometry(tri, { depth: 1.7, bevelEnabled: false }); roofGeo.translate(0, 0, -0.85);
  hp.roofMat = toon('#c0573e');
  hp.roof = new THREE.Group(); house.add(hp.roof);
  add(hp.roof, roofGeo, hp.roofMat, [0, 1.3, 0]);
  add(hp.roof, new THREE.BoxGeometry(0.28, 0.5, 0.28), toon('#8a6a58'), [0.6, 1.85, -0.2]);
  hp.door = new THREE.Group(); hp.door.position.set(0.38, 0, 0.71); house.add(hp.door);
  add(hp.door, new THREE.BoxGeometry(0.5, 0.82, 0.05), toon('#6b3f22'), [0, 0.41, 0]);
  add(hp.door, SPH, toon('#ffcf5a'), [0.15, 0.42, 0.04], [0.04, 0.04, 0.04], false);
  hp.window = new THREE.Group(); hp.window.position.set(-0.45, 0.75, 0.71); house.add(hp.window);
  hp.windowGlow = new THREE.MeshBasicMaterial({ color: '#ffe39a' });
  add(hp.window, new THREE.BoxGeometry(0.48, 0.42, 0.04), hp.windowGlow, [0, 0, 0]);
  add(hp.window, new THREE.BoxGeometry(0.05, 0.42, 0.06), toon('#6b3f22'), [0, 0, 0.01], null, false);
  add(hp.window, new THREE.BoxGeometry(0.48, 0.05, 0.06), toon('#6b3f22'), [0, 0, 0.01], null, false);
  hp.lamp = new THREE.Group(); hp.lamp.position.set(1.35, 0, 0.75); house.add(hp.lamp);
  add(hp.lamp, new THREE.CylinderGeometry(0.035, 0.05, 1.25, 10), toon('#3d3a40'), [0, 0.62, 0]);
  add(hp.lamp, new THREE.BoxGeometry(0.2, 0.05, 0.2), toon('#3d3a40'), [0, 1.27, 0]);
  hp.bulb = new THREE.MeshBasicMaterial({ color: '#fff0b8' });
  add(hp.lamp, SPH, hp.bulb, [0, 1.17, 0], [0.09, 0.11, 0.09]);
  hp.halo = new THREE.Mesh(PLANE, new THREE.MeshBasicMaterial({ map: blobTex, color: '#ffd46b', transparent: true, opacity: 0, depthWrite: false, blending: THREE.AdditiveBlending }));
  hp.halo.position.set(0, 1.17, 0.1); hp.halo.scale.setScalar(1.1); hp.lamp.add(hp.halo);
  hp.flowers = [];
  const fc = ['#ff7aa2', '#ffd166', '#c084fc', '#ff8a5c', '#ffffff', '#ff7aa2'];
  for (let i = 0; i < 6; i++) {
    const f = new THREE.Group(); f.position.set(-0.95 + i * 0.27 + (i > 2 ? 0.55 : 0), 0, 0.92); house.add(f);
    add(f, new THREE.CylinderGeometry(0.015, 0.015, 0.2, 6), toon('#4f9d4a'), [0, 0.1, 0], null, false);
    add(f, SPH, toon(fc[i]), [0, 0.22, 0], [0.07, 0.07, 0.07]);
    add(f, SPH, toon('#ffe08a'), [0, 0.22, 0.05], [0.03, 0.03, 0.03], false);
    hp.flowers.push(f);
  }
}
const doorWorld = () => { const v = new THREE.Vector3(); house.updateMatrixWorld(true); hp.door.getWorldPosition(v); return v; };
house.scale.setScalar(HOUSE.s);
const DOOR = doorWorld();
const DOOR_U = pu(DOOR.x), DOOR_V = HOUSE.v + 6;

// ---------------------------------------------------------------------------------------------------- particles
function instanced(geo, mat, n) {
  const m = new THREE.InstancedMesh(geo, mat, n); m.frustumCulled = false; scene.add(m);
  const z = new THREE.Matrix4().makeScale(0, 0, 0);
  for (let i = 0; i < n; i++) m.setMatrixAt(i, z);
  return m;
}
const M = new THREE.Matrix4(), Q = new THREE.Quaternion(), E = new THREE.Euler(), P = new THREE.Vector3(), S = new THREE.Vector3();
const put = (mesh, i, x, y, z, s, rx = 0, ry = 0, rz = 0, sy = s) => {
  Q.setFromEuler(E.set(rx, ry, rz)); M.compose(P.set(x, y, z), Q, S.set(s, sy, s)); mesh.setMatrixAt(i, M);
};

const drops = instanced(new THREE.SphereGeometry(1, 10, 8), new THREE.MeshBasicMaterial({ color: '#cbe8ff' }), 220);
const dropList = [];
function burst(t0, x, y, z, n, spd, up, seed, size = 0.05, span = 0) {
  const r = rng(seed);
  for (let i = 0; i < n; i++) {
    const a = r() * Math.PI * 2, sp = spd * (0.35 + r() * 0.65);
    dropList.push({ t0: t0 + r() * span, x, y, z, vx: Math.cos(a) * sp, vy: up * (0.45 + r() * 0.8), vz: Math.abs(Math.sin(a)) * sp * 0.4, life: 0.6 + r() * 0.45, size: size * (0.6 + r() * 0.8) });
  }
}

const confetti = instanced(new THREE.PlaneGeometry(0.13, 0.08), new THREE.MeshBasicMaterial({ side: THREE.DoubleSide }), 170);
{
  const cols = ['#ff6b8b', '#ffd166', '#06d6a0', '#4cc9f0', '#c084fc', '#ff8a5c'].map(c => new THREE.Color(c));
  for (let i = 0; i < 170; i++) confetti.setColorAt(i, cols[i % cols.length]);
}

const star = new THREE.Shape();
for (let i = 0; i < 8; i++) { const r = i % 2 ? 0.28 : 1, a = i * Math.PI / 4 + Math.PI / 2; const x = Math.cos(a) * r, y = Math.sin(a) * r; i ? star.lineTo(x, y) : star.moveTo(x, y); }
const stars = instanced(new THREE.ShapeGeometry(star), new THREE.MeshBasicMaterial({ color: '#fff4a8', transparent: true, opacity: 0.95, depthWrite: false }), 40);

const rain = instanced(new THREE.PlaneGeometry(0.022, 0.42), new THREE.MeshBasicMaterial({ color: '#dceaff', transparent: true, opacity: 0.7, depthWrite: false }), 700);
const snow = instanced(new THREE.CircleGeometry(1, 12), new THREE.MeshBasicMaterial({ color: '#ffffff', transparent: true, opacity: 0.95, depthWrite: false }), 420);

const water = new THREE.Mesh(new THREE.CircleGeometry(1, 48), new THREE.MeshBasicMaterial({ color: '#4499d9' }));
scene.add(water);
const rings = [0, 1].map(() => { const r = new THREE.Mesh(new THREE.RingGeometry(0.9, 1, 48), new THREE.MeshBasicMaterial({ color: '#ffffff', transparent: true, depthWrite: false })); scene.add(r); return r; });

const beam = new THREE.Mesh(PLANE, new THREE.MeshBasicMaterial({
  map: canvasTex(256, 4, (g, w, h) => {
    const gr = g.createLinearGradient(0, 0, w, 0);
    gr.addColorStop(0, 'rgba(120,220,255,0)'); gr.addColorStop(0.8, 'rgba(120,220,255,0.55)'); gr.addColorStop(0.97, 'rgba(255,255,255,1)'); gr.addColorStop(1, 'rgba(255,255,255,0)');
    g.fillStyle = gr; g.fillRect(0, 0, w, h);
  }), transparent: true, depthWrite: false, blending: THREE.AdditiveBlending,
}));
beam.scale.set(2.4, SH, 1);
scene.add(beam);

// ---------------------------------------------------------------------------------------------------- painted sky
// Tall cumulus clouds painted on a canvas in three layers (blue-grey shade, soft body, sunlit tops), drifting slowly.
function cloudTexture(seed) {
  return canvasTex(1024, 560, (g, w, h) => {
    const r = rng(seed), blobs = [];
    for (let i = 0; i < 30; i++) {
      const x = 170 + r() * 690, hump = 1 - Math.abs(x - 515) / 430;
      blobs.push([x, 420 - r() * 260 * hump, 55 + r() * 85 * (0.5 + hump)]);
    }
    // Hard-edged layers, as painted: lavender shade, cool body, warm sunlit tops, a bright rim.
    const layer = (dx, dy, k, color, blur) => {
      g.filter = `blur(${blur}px)`; g.fillStyle = color;
      for (const [x, y, rad] of blobs) { g.beginPath(); g.arc(x + dx, y + dy, rad * k, 0, Math.PI * 2); g.fill(); }
    };
    layer(0, 0, 1.0, '#9aa6cc', 1.5);
    layer(-6, -10, 0.9, '#c9d2ea', 1.2);
    layer(-14, -24, 0.72, '#f3f5fb', 1.0);
    layer(-22, -36, 0.5, '#fffaf0', 0.8);
    g.filter = 'none';
    g.globalCompositeOperation = 'destination-out';
    const cut = g.createLinearGradient(0, 430, 0, 500);
    cut.addColorStop(0, 'rgba(0,0,0,0)'); cut.addColorStop(1, 'rgba(0,0,0,1)');
    g.fillStyle = cut; g.fillRect(0, 430, w, h - 430);
  });
}
const cloudTex = [11, 23, 37].map(cloudTexture);
const clouds = [
  [0, -5.4, 3.55, 5.4], [1, 1.0, 3.95, 4.0], [2, 6.4, 3.45, 5.0], [0, 11.5, 3.8, 4.2],
].map(([k, x, y, w]) => {
  const m = new THREE.Mesh(PLANE, new THREE.MeshBasicMaterial({ map: cloudTex[k], transparent: true, depthWrite: false, clippingPlanes: SCREEN_CLIP }));
  m.userData = { x, y, w }; m.scale.set(w, w * 0.547, 1); scene.add(m);
  return m;
});

// Sun rays from the upper right, and petals and dandelion seeds on the wind.
const rayTex = canvasTex(64, 512, (g, w, h) => {
  const gr = g.createLinearGradient(0, 0, 0, h);
  gr.addColorStop(0, 'rgba(255,246,214,0.9)'); gr.addColorStop(1, 'rgba(255,246,214,0)');
  g.fillStyle = gr; g.fillRect(0, 0, w, h);
  const side = g.createLinearGradient(0, 0, w, 0);
  side.addColorStop(0, 'rgba(0,0,0,1)'); side.addColorStop(0.5, 'rgba(0,0,0,0)'); side.addColorStop(1, 'rgba(0,0,0,1)');
  g.globalCompositeOperation = 'destination-out'; g.fillStyle = side; g.fillRect(0, 0, w, h);
});
const rays = [[5.6, 1.6, 0.9], [4.2, 1.1, 0.6], [6.9, 2.2, 0.7]].map(([x, wdt, o]) => {
  const m = new THREE.Mesh(PLANE, new THREE.MeshBasicMaterial({ map: rayTex, transparent: true, depthWrite: false, blending: THREE.AdditiveBlending, opacity: 0, clippingPlanes: SCREEN_CLIP }));
  m.userData = { x, o }; m.scale.set(wdt, 13, 1); m.rotation.z = -0.42; scene.add(m);
  return m;
});
const PETALS = 70;
const petals = instanced(new THREE.CircleGeometry(1, 10), new THREE.MeshBasicMaterial({ side: THREE.DoubleSide, transparent: true, depthWrite: false, clippingPlanes: SCREEN_CLIP }), PETALS);
{
  const cols = ['#ffffff', '#ffd6e2', '#fff4d6', '#ffffff', '#ffc2d4'].map(c => new THREE.Color(c));
  for (let i = 0; i < PETALS; i++) petals.setColorAt(i, cols[i % cols.length]);
}

// Grass in the foreground, close to the camera, swaying in the wind (on the meadow and the garden).
const BLADES = 320;
const bladeGeo = new THREE.BufferGeometry();
bladeGeo.setAttribute('position', new THREE.Float32BufferAttribute([-0.05, 0, 0, 0.05, 0, 0, 0.012, 1, 0], 3));
bladeGeo.setAttribute('color', new THREE.Float32BufferAttribute([0.35, 0.5, 0.45, 0.35, 0.5, 0.45, 1.25, 1.2, 0.85], 3));
const grassMat = new THREE.MeshBasicMaterial({ vertexColors: true, side: THREE.DoubleSide, transparent: true });
const blades = instanced(bladeGeo, grassMat, BLADES);
{
  const r = rng(808), cols = ['#3f8a3a', '#2f7434', '#5a9e3c', '#4c8f45', '#6aa548'].map(c => new THREE.Color(c));
  for (let i = 0; i < BLADES; i++) blades.setColorAt(i, cols[Math.floor(r() * cols.length)]);
}

// ---------------------------------------------------------------------------------------------------- post
// Painted look over the whole frame: a soft bloom on the light, then a warm grade, gentle vignette and paper grain.
const composer = new EffectComposer(renderer);
composer.setPixelRatio(1);
composer.setSize(W, H);
composer.addPass(new RenderPass(scene, camera));
composer.addPass(new UnrealBloomPass(new THREE.Vector2(W, H), 0.14, 0.5, 0.97));
composer.addPass(new OutputPass());
const grade = new ShaderPass({
  uniforms: { tDiffuse: { value: null }, time: { value: 0 }, res: { value: new THREE.Vector2(W, H) }, amount: { value: 1 } },
  vertexShader: 'varying vec2 vUv; void main(){ vUv = uv; gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 1.0); }',
  fragmentShader: `uniform sampler2D tDiffuse; uniform float time; uniform vec2 res; uniform float amount; varying vec2 vUv; ${NOISE}
    void main(){
      vec3 c = texture2D(tDiffuse, vUv).rgb, o = c;
      float l = dot(c, vec3(0.3, 0.59, 0.11));
      c = mix(c, c * vec3(1.04, 1.0, 0.93) + vec3(0.012, 0.008, 0.0), 0.9);
      c = mix(vec3(l), c, 1.12);
      c += vec3(-0.012, 0.006, 0.018) * (1.0 - smoothstep(0.0, 0.6, l));
      c = mix(c, smoothstep(0.0, 1.0, c), 0.18);
      vec2 q = (vUv - 0.5) * vec2(1.0, 0.8);
      c *= mix(0.84, 1.0, smoothstep(0.62, 0.18, length(q)));
      float paper = fbm(vUv * res / 2.5) * 0.6 + fbm(vUv * res / 30.0) * 0.4;
      c *= 0.97 + paper * 0.045;
      c += (hash(vUv * res + floor(time * 12.0)) - 0.5) * 0.018;
      gl_FragColor = vec4(mix(o, c, amount), 1.0);
    }`,
});
composer.addPass(grade);

// ---------------------------------------------------------------------------------------------------- choreography
const dogT = track([
  [0, 560, 1100], [6.6, 560, 1100], [8.2, 1180, 1170],
  [13.0, 1180, 1170], [14.0, 1330, 1085], [14.2, 1330, 1085], [14.8, 1680, 1010], [16.6, 1900, 1020], [17.1, 2150, 1120],
  [20.0, 2150, 1120], [21.2, 1600, 1100],
  [31.6, 1600, 1100], [32.8, DOOR_U, DOOR_V],
  [35.05, DOOR_U, DOOR_V], [35.65, 1250, 1150],
]);
const catT = track([
  [0, 1500, 1290], [12.9, 1500, 1290], [14.7, 1240, 815],
  [18.5, 1240, 815], [20.2, 1150, 1110],
  [34.6, 1150, 1110], [35.8, DOOR_U, DOOR_V],
], -1);

function lineup(p, t) {
  const [, u] = LINE.find(e => e[0] === p);
  const i = LINE.findIndex(e => e[0] === p);
  const v = 1150, dir = u < 1280 ? 1 : u > 1280 ? -1 : 1;
  const st = { show: true, s: depth(v), x: wx(u), y: wy(v), z: zOf(v), dir, yaw: u === 1280 ? 0.35 : dir * 0.55, look: -dir * 0.2, sit: 1, amp: 0 };
  const order = [0, 1, 2, -1, 3, 4, 5][i];
  if (order >= 0) { const t0 = LINEUP + order * 0.14; st.pop = backOut(range(t, t0, t0 + 0.35)); st.jump = arc(t, t0, t0 + 0.45, 0.35); }
  st.jump = (st.jump ?? 0) + arc(t, 45.3 + i * 0.07, 45.75 + i * 0.07, 0.3);
  st.happy = t > 45.3 && t < 46.4;
  st.wag = 0.9;
  return st;
}

function dogState(t) {
  if (t >= LINEUP) return lineup(dog, t);
  const st = base(dogT(t));
  st.jump = arc(t, 14.2, 14.8, 0.9) + arc(t, 16.6, 17.1, 0.8) + [0, 1, 2].reduce((s, i) => s + arc(t, 35.7 + i * 0.43, 36.08 + i * 0.43, 0.32), 0);
  if (t > 14.8 && t < 16.6) { st.swim = smooth(range(t, 14.8, 14.95)) * (1 - smooth(range(t, 16.45, 16.6))); st.walk = t * 13; st.amp = 0.6; }
  st.shake = win(t, 17.2, 18.1, 0.1, 0.15);
  if (t > 21.2 && t < 31.6) { faceCam(st, smooth(range(t, 21.2, 21.6)) * (1 - smooth(range(t, 31.2, 31.6)))); st.sit = smooth(range(t, 21.4, 21.8)) * (1 - smooth(range(t, 31.2, 31.6))); }
  if (t > 36.9 && t < 39.6) { faceCam(st, smooth(range(t, 36.9, 37.3))); st.sit = smooth(range(t, 37.0, 37.4)); }
  if (t > 37.6) st.nod = -0.35 * smooth(range(t, 37.6, 38.0));
  for (const b of BUBBLES) if (b.pet === 'dog' && t > b.t0 && t < b.t1) st.nod = (st.nod ?? 0) + Math.sin((t - b.t0) * 14) * 0.06;
  if (t > 31.6 && t < 35.65) st.z = 1.6;
  st.pop = (1 - range(t, 32.75, 32.95)) + (t > 35.0 ? backOut(range(t, 35.0, 35.3)) : 0);
  if (t > 39.55) st.pop = 0;
  st.happy = (t > 36.0 && t < 37.0) || (t > 38.0 && t < 39.3);
  if (t > 8.6 && t < 11.9) st.z += 0.5 * layers(t);
  return st;
}

function catState(t) {
  if (t >= LINEUP) return lineup(cat, t);
  const st = base(catT(t));
  st.sit = (1 - smooth(range(t, 12.7, 13.0))) + smooth(range(t, 20.2, 20.6)) * (1 - smooth(range(t, 34.4, 34.7)));
  if (t < 12.9) { faceCam(st, 1, 0.35 + Math.sin(t * 0.9) * 0.2); st.nod = Math.sin(t * 1.3) * 0.08; st.wag = 0.6; }
  st.lie = smooth(range(t, 14.8, 15.2)) * (1 - smooth(range(t, 18.2, 18.5)));
  st.happy = st.lie > 0.5;
  if (t > 20.2 && t < 34.4) faceCam(st, smooth(range(t, 20.2, 20.6)));
  for (const b of BUBBLES) if (b.pet === 'cat' && t > b.t0 && t < b.t1) { st.nod = Math.sin((t - b.t0) * 12) * 0.05; st.tiltHead = b.t0 < 30 ? -0.18 : 0; }
  if (t > 34.6 && t < 36) st.z = 1.6;
  st.pop = 1 - range(t, 35.75, 35.95);
  if (t > 8.6 && t < 11.9) st.z += 0.5 * layers(t);
  return st;
}

function kittenState(t) {
  if (t >= LINEUP) {
    const st = lineup(kitten, t);
    st.hat = 1; st.s *= 1; return st;
  }
  const v = 1150, u = 1280;
  const grow = ease(range(t, 40.95, 41.8));
  return {
    show: t > 40.3, s: depth(v) * lerp(0.6, 1, grow), x: wx(u), y: wy(v), z: zOf(v), yaw: 0.35, dir: 1, look: -0.2, sit: 1,
    pop: backOut(range(t, 40.35, 40.7)), headScale: lerp(1.32, 1, grow), jump: arc(t, 41.7, 42.15, 0.45),
    hat: t > 41.7 ? range(t, 41.7, 42.0) : 0, happy: t > 41.7 && t < 42.4, wag: 0.8,
  };
}
const layers = t => ease(range(t, 8.6, 9.6)) * (1 - ease(range(t, 10.9, 11.9)));

// Splashes from the dog: into the pond, out of it, the shake, and the rain puddle hops.
{
  const at = t => { const st = dogState(t); return [st.x, st.y, st.z]; };
  const [x1, y1, z1] = at(14.8); burst(14.8, x1, y1 + 0.1, z1 + 0.3, 40, 2.2, 3.4, 11, 0.06);
  const [x2, y2, z2] = at(16.6); burst(16.6, x2, y2 + 0.1, z2 + 0.3, 30, 1.8, 3.0, 12, 0.055);
  const [x3, y3, z3] = at(17.6); burst(17.2, x3, y3 + 0.6, z3 + 0.2, 60, 2.6, 1.6, 13, 0.04, 0.85);
  for (let i = 0; i < 3; i++) { const [x, y, z] = at(36.08 + i * 0.43); burst(36.08 + i * 0.43, x, y + 0.05, z + 0.25, 16, 1.4, 1.8, 20 + i, 0.04); }
}

// ---------------------------------------------------------------------------------------------------- DOM layer
const ui = document.getElementById('ui');
const el = (cls, html = '', parent = ui) => { const d = document.createElement('div'); d.className = cls; d.innerHTML = html; parent.appendChild(d); return d; };
const title = el('title', `<img src="icon.png" alt=""><div class="name">PetFam</div>`);
const tagline = el('tagline', L('우리 펫 가족이 바탕화면에 살아요', 'Your pet family, living on your desktop'));
const shade = el('shade');
const cap = el('cap', '<div class="kicker"></div><div class="line"></div>');
const capK = cap.querySelector('.kicker'), capL = cap.querySelector('.line');
const tags = [
  el('tag', L('바탕화면', 'Wallpaper')), el('tag', L('펫', 'Pets')), el('tag', L('내 창', 'Your windows')),
];
const chips = CHIPS.map(c => el('chip', `<div class="ring"></div><div class="label">${c.icon} ${c[LANG]}</div>`));
const sceneNote = el('note', L('👀 AI가 본 풍경 · 햇살 가득한 모래사장과 푸른 바다', '👀 What the AI sees · a sunny sandy beach and a blue sea'));
const bubbles = BUBBLES.map(b => el('bubble', b[LANG]));
const card = el('card', `
  <div class="ctitle">🏠 ${L('집 꾸미기', 'Decorate the house')}</div>
  <div class="crow"><div class="cinput"><span class="ctext"></span><span class="caret"></span></div><div class="cbtn">✨ ${L('AI로 꾸미기', 'Decorate with AI')}</div></div>
  <div class="cstatus"></div>`);
const ctext = card.querySelector('.ctext'), caret = card.querySelector('.caret'), cbtn = card.querySelector('.cbtn'), cstatus = card.querySelector('.cstatus');
const counter = el('counter');
const end = el('end', `<img src="icon.png" alt=""><div class="name">PetFam</div>
  <div class="sub">${L('우리 펫 가족이 바탕화면에 살아요', 'Your pet family, living on your desktop')}</div>
  <div class="pills"><span>Windows</span><span>macOS</span></div>`);
const fade = el('fade');

const _v = new THREE.Vector3();
function screenOf(x, y, z) { _v.set(x, y, z).project(camera); return [(_v.x * 0.5 + 0.5) * W, (-_v.y * 0.5 + 0.5) * H]; }
function headTop(p, extra = 0.5) {
  p.head.getWorldPosition(_v);
  const s = p.root.scale.x;
  return screenOf(_v.x, _v.y + (0.28 + extra) * s, _v.z);
}
const show = (e, a, transform = '') => { e.style.opacity = a.toFixed(3); e.style.visibility = a > 0.001 ? 'visible' : 'hidden'; e.style.transform = transform; };

function updateDom(t) {
  // Title over the monitor.
  const ta = win(t, 0.5, 4.9, 0.8, 0.6);
  show(title, ta, `translate(-50%, ${(1 - backOut(range(t, 0.5, 1.3))) * 40}px) scale(${0.9 + 0.1 * backOut(range(t, 0.5, 1.3))})`);
  show(tagline, win(t, 1.2, 4.9, 0.6, 0.6), `translate(-50%, ${(1 - ease(range(t, 1.2, 1.9))) * 24}px)`);

  // Captions.
  let cur = null;
  for (const c of CAPTIONS) if (t >= c.t0 && t < c.t1) cur = c;
  if (cur) {
    if (capK.textContent !== cur.k[LANG]) { capK.textContent = cur.k[LANG]; capL.textContent = cur.l[LANG]; }
    const a = win(t, cur.t0, cur.t1, 0.45, 0.35);
    show(cap, a, `translateY(${(1 - ease(range(t, cur.t0, cur.t0 + 0.55))) * 34}px)`);
  } else show(cap, 0);
  const shadeA = Math.max(...CAPTIONS.map(c => win(t, c.t0 - 0.2, c.t1 + 0.2, 0.6, 0.6)));
  show(shade, shadeA * (1 - range(t, 44.6, 45.2)));

  // Layer tags while the camera looks from the side.
  const ka = win(t, 9.6, 11.0, 0.3, 0.3);
  const anchors = [[-2.6, 2.6, 0], null, [winA.position.x - 2.6, winA.position.y + 1.9, winA.position.z]];
  tags.forEach((e, i) => {
    const [x, y] = i === 1 ? headTop(dog, 0.35) : screenOf(...anchors[i]);
    e.style.left = `${x}px`; e.style.top = `${y}px`;
    show(e, ka * smooth(range(t, 9.6 + i * 0.15, 9.9 + i * 0.15)), 'translate(-50%, -100%)');
  });

  // What the AI found in the wallpaper.
  CHIPS.forEach((c, i) => {
    const [x, y] = screenOf(wx(c.u), wy(c.v), 0.05);
    chips[i].style.left = `${x}px`; chips[i].style.top = `${y}px`;
    show(chips[i], win(t, c.t0, SCAN.end, 0.25, 0.4), `translate(-50%, -50%) scale(${0.6 + 0.4 * backOut(range(t, c.t0, c.t0 + 0.35))})`);
  });
  show(sceneNote, win(t, 20.0, 23.0, 0.4, 0.4), `translate(-50%, ${(1 - ease(range(t, 20, 20.5))) * -20}px)`);

  // Speech bubbles above the pets.
  BUBBLES.forEach((b, i) => {
    const p = b.pet === 'dog' ? dog : cat;
    const a = win(t, b.t0, b.t1, 0.12, 0.25);
    if (a <= 0 || !p.root.visible) { show(bubbles[i], 0); return; }
    let [x, y] = headTop(p);
    x = clamp(x, 230, W - 230);
    bubbles[i].style.left = `${x}px`; bubbles[i].style.top = `${y}px`;
    show(bubbles[i], a, `translate(-50%, -100%) scale(${backOut(range(t, b.t0, b.t0 + 0.3))})`);
  });

  // "Decorate with AI" card.
  const typed = TYPE[LANG];
  const n = Math.round(typed.length * range(t, TYPE.t0, TYPE.t1));
  if (ctext.textContent.length !== n) ctext.textContent = typed.slice(0, n);
  caret.style.opacity = t < PRESS && Math.floor(t * 2.4) % 2 === 0 ? 1 : 0;
  cbtn.classList.toggle('down', t > PRESS && t < PRESS + 0.15);
  const status = t < PRESS ? '' : t < BUILD + 0.1 ? L('AI에게 물어보는 중…', 'Asking your AI…') : L('통나무 벽, 빨간 지붕, 등불과 꽃을 달았어요 ✓', 'Log walls, a red roof, a lantern and flowers ✓');
  if (cstatus.textContent !== status) cstatus.textContent = status;
  show(card, win(t, 27.0, 31.0, 0.4, 0.4), `translateY(${(1 - ease(range(t, 27.0, 27.45))) * -30}px)`);

  // Growing up.
  const day = Math.round(lerp(1, 46, ease(range(t, 40.95, 41.8))));
  const stage = day < 15 ? L('아기', 'Baby') : day < 46 ? L('청소년', 'Junior') : L('어른', 'Adult');
  const label = `D+${day} · ${stage}`;
  if (counter.textContent !== label) counter.textContent = label;
  if (kitten.root.visible && t < LINEUP) {
    const [x, y] = headTop(kitten, 0.75);
    counter.style.left = `${x}px`; counter.style.top = `${y}px`;
  }
  show(counter, win(t, 40.5, 42.7, 0.3, 0.3), `translate(-50%, -100%) scale(${1 + 0.15 * arc(t, 41.7, 42.0, 1)})`);

  // End card and fades.
  show(end, smooth(range(t, 45.4, 46.1)), `translate(-50%, ${(1 - ease(range(t, 45.4, 46.2))) * 30}px)`);
  fade.style.opacity = Math.max(1 - range(t, 0, 0.6), range(t, 49.2, 50)).toFixed(3);
}

// ---------------------------------------------------------------------------------------------------- frame
const camKeys = [
  // time, pan x, pan y, push in
  [12.5, 0, 0, 0], [14.0, 0.45, -0.25, 1.1], [18.4, 0.45, -0.25, 1.1], [19.6, 0, -0.3, 1.2], [26.0, 0, -0.3, 1.2],
  [27.0, -0.35, -0.2, 1.0], [33.4, -0.35, -0.2, 1.0], [34.4, 0, -0.2, 0.8], [39.5, 0, -0.2, 0.8], [40.5, 0, -0.3, 1.15],
  [44.8, 0, -0.3, 1.15], [46.0, 0, 0, 0],
];
function drift(t) {
  let a = camKeys[0], b = camKeys[0];
  for (let i = 1; i < camKeys.length; i++) { if (t <= camKeys[i][0]) { a = camKeys[i - 1]; b = camKeys[i]; break; } a = b = camKeys[i]; }
  const k = a === b ? 0 : ease(range(t, a[0], b[0]));
  const dz = lerp(a[3], b[3], k), lim = Math.tan(THREE.MathUtils.degToRad(15)) * dz;
  return [clamp(lerp(a[1], b[1], k), -lim * 16 / 9, lim * 16 / 9), clamp(lerp(a[2], b[2], k), -lim, lim), dz];
}

function placeCamera(t) {
  const near = new THREE.Vector3(0, 0, CAM_Z), look = new THREE.Vector3();
  if (t < 6) {
    const k = ease(range(t, 1.6, 6));
    camera.position.lerpVectors(new THREE.Vector3(-9, 4.5, 62), near, k);
    look.set(0, lerp(-1.2, 0, k), 0);
  } else if (t < 12.5) {
    const k = layers(t);
    camera.position.lerpVectors(near, new THREE.Vector3(10.5, 2.4, 11.0), k);
    look.set(0, lerp(0, -0.6, k), lerp(0, 1.2, k));
  } else {
    const [x, y, dz] = drift(t);
    camera.position.set(x, y, CAM_Z - dz);
    look.set(x, y, 0);
  }
  camera.lookAt(look);
  camera.updateMatrixWorld();
}

function renderAt(t) {
  t = clamp(t, 0, DURATION);
  placeCamera(t);

  // Wallpaper changes, weather and the brand colour at the end.
  const u = backdrop.material.uniforms;
  const swaps = [[18.8, 19.9, meadow, seaside], [26.0, 27.1, seaside, garden], [39.5, 40.4, garden, meadow]];
  u.a.value = meadow; u.b.value = meadow; u.wipe.value = 0;
  for (const [a, b, from, to] of swaps) {
    if (t >= b) { u.a.value = to; u.b.value = to; }
    else if (t >= a) { u.a.value = from; u.b.value = to; u.wipe.value = ease(range(t, a, b)); break; }
    else break;
  }
  const dark = win(t, 33.5, 39.6, 0.8, 0.8);
  const snowy = smooth(range(t, 36.8, 37.6));
  u.tint.value.setRGB(lerp(1, lerp(0.48, 0.8, snowy), dark), lerp(1, lerp(0.54, 0.84, snowy), dark), lerp(1, lerp(0.7, 0.95, snowy), dark));
  u.brand.value = ease(range(t, 45.0, 46.0));
  hemi.intensity = 1.7 * lerp(1, 0.75, dark);
  sun.intensity = 2.4 * lerp(1, 0.55, dark);

  // App windows: on the screen, pulled forward while the camera looks from the side, then swept away.
  const lay = layers(t), gone = ease(range(t, 11.8, 12.5));
  winA.position.set(-4.5 - gone * 10, -1.2, lerp(1.0, 2.7, lay));
  winB.position.set(4.3 + gone * 10, 1.9, lerp(1.0, 3.3, lay));
  for (const wnd of [winA, winB]) { wnd.visible = gone < 1; for (const [m, o] of wnd.userData.mats) m.opacity = o * (1 - gone); }

  // Pets.
  pose(dog, dogState(t), t);
  pose(cat, catState(t), t);
  pose(kitten, kittenState(t), t);
  for (const [p] of LINE) if (p !== dog && p !== cat && p !== kitten) pose(p, t >= LINEUP ? lineup(p, t) : { show: false }, t);

  // The dog swimming: pond water in front of its body, with ripples.
  const ds = dogState(t);
  const swim = ds.swim ?? 0;
  water.visible = swim > 0.01;
  if (water.visible) {
    water.position.set(ds.x, ds.y - 0.02 * ds.s, ds.z + 0.55 * ds.s);
    water.scale.set(0.95 * ds.s, 0.42 * ds.s, 1);
  }
  rings.forEach((r, i) => {
    const k = ((t - 14.8) * 0.9 + i * 0.5) % 1;
    r.visible = swim > 0.01;
    r.position.set(ds.x, ds.y + 0.12 * ds.s, ds.z + 0.56 * ds.s);
    r.scale.set((0.5 + k * 0.9) * ds.s, (0.5 + k * 0.9) * 0.22 * ds.s, 1);
    r.material.opacity = 0.7 * (1 - k) * swim;
  });

  // House: built from words, snowed on, gone before the growing-up part.
  const b = t - BUILD;
  const out = 1 - ease(range(t, 39.5, 39.85));
  house.visible = b > 0 && out > 0;
  if (house.visible) {
    house.scale.setScalar(HOUSE.s * out);
    hp.walls.scale.set(1, Math.max(0.001, backOut(range(b, 0, 0.55))), 1);
    hp.roof.position.y = (1 - ease(range(b, 0.45, 0.95))) * 3.5;
    hp.roof.visible = b > 0.45;
    hp.door.scale.setScalar(Math.max(0.001, backOut(range(b, 1.0, 1.3))));
    hp.window.scale.setScalar(Math.max(0.001, backOut(range(b, 1.1, 1.4))));
    hp.lamp.scale.set(1, Math.max(0.001, backOut(range(b, 1.25, 1.6))), 1);
    hp.flowers.forEach((f, i) => f.scale.setScalar(Math.max(0.001, backOut(range(b, 1.4 + i * 0.08, 1.7 + i * 0.08)))));
    const lit = smooth(range(b, 1.6, 1.9));
    hp.halo.material.opacity = lit * lerp(0.55, 1, dark);
    hp.bulb.color.set(lit > 0.5 ? '#fff0b8' : '#9a958a');
    hp.windowGlow.color.set('#ffe39a').lerp(new THREE.Color('#8a7a5a'), 1 - lit);
    hp.roofMat.color.set('#c0573e').lerp(new THREE.Color('#f3f7ff'), 0.8 * smooth(range(t, 37.2, 39.0)));
    const door = arc(t, 32.75, 33.1, 1) + arc(t, 34.95, 35.25, 1) + arc(t, 35.7, 36.0, 1);
    hp.door.rotation.y = -door * 1.1;
  }

  // Droplets.
  dropList.forEach((d, i) => {
    const a = t - d.t0;
    if (a < 0 || a > d.life) { put(drops, i, 0, 0, 0, 0); return; }
    put(drops, i, d.x + d.vx * a, d.y + d.vy * a - 4.9 * a * a, d.z + d.vz * a, d.size * (1 - 0.5 * a / d.life));
  });
  drops.instanceMatrix.needsUpdate = true;

  // Confetti when the kitten grows up.
  {
    const r = rng(99), k = kittenState(41.75), on = t > 41.7 && t < 44.5;
    for (let i = 0; i < 170; i++) {
      const ang = r() * Math.PI * 2, sp = 2 + r() * 3.5, up = 4 + r() * 4, spin = r() * 10, a = t - 41.7 - r() * 0.1;
      if (!on || a < 0) { put(confetti, i, 0, 0, 0, 0); continue; }
      const drag = 1 - Math.exp(-a * 1.6);
      const x = k.x + Math.cos(ang) * sp * drag / 1.6, y = k.y + 0.8 + up * drag / 1.6 - 1.2 * a * a, z = k.z + 0.5 + Math.sin(ang) * 0.8 * drag;
      put(confetti, i, x, y, z, 1, spin * a, spin * a * 0.7, spin * a * 0.4);
    }
    confetti.instanceMatrix.needsUpdate = true;
  }

  // Sparkles: around the sunbathing cat, and when the house is done.
  {
    const r = rng(5);
    const cs = catState(t), sunA = cs.lie ?? 0;
    for (let i = 0; i < 40; i++) {
      if (i < 10) {
        const ph = r() * Math.PI * 2, rad = 0.6 + r() * 0.5, h = 0.2 + r() * 0.8, tw = Math.max(0, Math.sin(t * 3 + ph * 3));
        if (sunA < 0.05 || !cs.show) { put(stars, i, 0, 0, 0, 0); continue; }
        put(stars, i, cs.x + Math.cos(ph + t * 0.4) * rad * cs.s, cs.y + h * cs.s, cs.z + 0.4, 0.07 * tw * sunA, 0, 0, t + ph);
      } else {
        const ang = r() * Math.PI * 2, sp = 1.2 + r() * 1.8, a = t - (BUILD + 1.45) - r() * 0.15;
        if (a < 0 || a > 0.9) { put(stars, i, 0, 0, 0, 0); continue; }
        const hx = house.position.x + 0.2, hy = house.position.y + 1.0;
        put(stars, i, hx + Math.cos(ang) * sp * a, hy + Math.sin(ang) * sp * a, 1.9, 0.09 * (1 - a / 0.9), 0, 0, a * 6);
      }
    }
    stars.instanceMatrix.needsUpdate = true;
  }

  // Rain, then snow.
  {
    const ra = win(t, 34.0, 37.2, 0.5, 0.6), sa = win(t, 36.8, 39.6, 0.6, 0.6);
    const cx = camera.position.x, cy = camera.position.y;
    let r = rng(41);
    for (let i = 0; i < 700; i++) {
      const x0 = r() * 19 - 9.5, z = 0.4 + r() * 3.2, sp = 13 + r() * 6, off = r() * 12;
      if (i >= ra * 700) { put(rain, i, 0, 0, 0, 0); continue; }
      const y = 5.5 - ((t * sp + off) % 11);
      put(rain, i, cx + x0 - (5.5 - y) * 0.12, cy + y, z, 1, 0, 0, 0.11);
    }
    rain.instanceMatrix.needsUpdate = true;
    r = rng(77);
    for (let i = 0; i < 420; i++) {
      const x0 = r() * 19 - 9.5, z = 0.4 + r() * 3.2, sp = 0.7 + r() * 0.8, off = r() * 11, sz = 0.025 + r() * 0.035, ph = r() * 6;
      if (i >= sa * 420) { put(snow, i, 0, 0, 0, 0); continue; }
      const y = 5.5 - ((t * sp + off) % 11);
      put(snow, i, cx + x0 + Math.sin(t * 1.3 + ph) * 0.25, cy + y, z, sz);
    }
    snow.instanceMatrix.needsUpdate = true;
  }

  // Clouds drift; rain greys them. Sun rays and petals while the weather is fine.
  const greyK = dark * (1 - snowy * 0.5);
  clouds.forEach(c => {
    const { x, y, w } = c.userData;
    let cx = x + t * 0.11;
    if (cx > 10.5) cx -= 22;
    c.position.set(cx, y + Math.sin(t * 0.2 + x) * 0.05, 0.03);
    c.material.color.setRGB(lerp(1, 0.62, greyK), lerp(1, 0.66, greyK), lerp(1, 0.76, greyK));
  });
  const fine = smooth(range(t, 5, 7)) * (1 - win(t, 33.3, 39.8, 0.6, 0.8));
  rays.forEach((m, i) => {
    m.position.set(m.userData.x + camera.position.x * 0.2, 3.2, 0.08);
    m.material.opacity = m.userData.o * 0.22 * fine * (1 - u.brand.value * 0.6) * (0.75 + 0.25 * Math.sin(t * 0.7 + i * 2));
  });
  {
    const r = rng(303), cx = camera.position.x, cy = camera.position.y;
    for (let i = 0; i < PETALS; i++) {
      const sp = 0.9 + r() * 0.9, off = r() * 21, y0 = -3.5 + r() * 7.5, z = 0.6 + r() * 3, ph = r() * 6.3, size = 0.035 + r() * 0.035, seed = r() < 0.4;
      if (i >= fine * PETALS) { put(petals, i, 0, 0, 0, 0); continue; }
      const x = -10.5 + ((t * sp + off) % 21);
      const y = y0 + Math.sin(t * 0.9 + ph) * 0.45 + Math.sin(t * 2.3 + ph * 2) * 0.12;
      put(petals, i, cx + x, cy + y, z, size, Math.sin(t * 2 + ph) * 1.2, t * 1.5 + ph, t * 0.8 + ph, seed ? size : size * 0.55);
    }
    petals.instanceMatrix.needsUpdate = true;
  }

  {
    const onGreen = tx => (tx === meadow || tx === garden ? 1 : 0);
    const w = u.wipe.value, green = lerp(onGreen(u.a.value), onGreen(u.b.value), w);
    const amt = green * smooth(range(t, 12.3, 12.9)) * (1 - u.brand.value);
    grassMat.opacity = amt;
    grassMat.color.copy(u.tint.value);
    blades.visible = amt > 0.01;
    const r = rng(909), cx = camera.position.x, cy = camera.position.y, gust = Math.sin(t * 0.7) * 0.5 + 0.5;
    for (let i = 0; i < BLADES; i++) {
      const z = 2.0 + r() * 1.6, half = (camera.position.z - z) * Math.tan(THREE.MathUtils.degToRad(15));
      const x = cx + (r() * 2 - 1) * half * 16 / 9 * 1.05, hgt = 0.25 + r() * 0.6, lean = (r() - 0.5) * 0.5, ph = r() * 6.3;
      const sway = Math.sin(t * 1.7 + x * 0.9 + ph * 0.3) * (0.12 + 0.18 * gust) + Math.sin(t * 4.1 + ph) * 0.03;
      put(blades, i, x, cy - half - 0.05, z, 1, 0, 0, lean - sway, hgt * (0.9 + 0.1 * Math.sin(t * 2 + ph)));
    }
    blades.instanceMatrix.needsUpdate = true;
  }

  // The AI scan line over the meadow.
  const sk = range(t, SCAN.t0, SCAN.t1);
  beam.visible = sk > 0 && sk < 1;
  beam.position.set(lerp(-9.5, 9.5, sk), 0, 0.04);

  scene.updateMatrixWorld();
  updateDom(t);
  grade.uniforms.time.value = t;
  composer.render();
}

// ---------------------------------------------------------------------------------------------------- start
async function ready() {
  try { await Promise.race([Promise.all([document.fonts.load('700 40px "Gowun Batang"'), document.fonts.load('40px "Gowun Dodum"'), document.fonts.load('700 40px Fredoka'), document.fonts.load('600 40px Fraunces')]), new Promise(r => setTimeout(r, 4000))]); } catch { /* fall back */ }
  await Promise.all([...document.images].map(i => i.decode().catch(() => {})));
  // Korean web fonts come in many small pieces that load only when a glyph is first drawn, which would leave
  // text missing from recorded frames: run through the whole film once and load every piece it will need.
  let text = '';
  for (let t = 0; t <= DURATION; t += 0.25) { updateDom(t); text += ui.textContent; }
  const chars = [...new Set(text)].join('');
  try { await Promise.race([Promise.all(['700 40px "Gowun Batang"', '40px "Gowun Dodum"', '700 40px Fredoka', '500 40px Fredoka', '600 40px Fraunces', 'italic 500 40px Fraunces'].map(f => document.fonts.load(f, chars))), new Promise(r => setTimeout(r, 15000))]); } catch { /* fall back */ }
  await document.fonts.ready;
}
window.renderAt = renderAt;
window.DURATION = DURATION;
window.__ready = ready().then(() => { renderAt(Number(qs.get('t') ?? 0)); return true; });

function fit() {
  const s = Math.min(innerWidth / W, innerHeight / H);
  const stage = document.getElementById('stage');
  stage.style.transform = `translate(${(innerWidth - W * s) / 2}px, ${(innerHeight - H * s) / 2}px) scale(${s})`;
}
if (!qs.has('capture')) {
  addEventListener('resize', fit); fit();
  if (!qs.has('t')) {
    await window.__ready;
    const start = performance.now();
    const loop = () => { renderAt(((performance.now() - start) / 1000) % DURATION); requestAnimationFrame(loop); };
    loop();
  }
}
