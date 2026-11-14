// "The Window Ride": a PetFam short film in pure three.js. A dog and a cat leave the desktop on an app window and
// fly through eight worlds, then come home. Everything is a function of time, so render.mjs can record it frame by
// frame; open film.html through `node render.mjs --serve` to watch it live.
import * as THREE from 'three';
import { EffectComposer } from 'three/addons/postprocessing/EffectComposer.js';
import { RenderPass } from 'three/addons/postprocessing/RenderPass.js';
import { ShaderPass } from 'three/addons/postprocessing/ShaderPass.js';
import { UnrealBloomPass } from 'three/addons/postprocessing/UnrealBloomPass.js';
import { OutputPass } from 'three/addons/postprocessing/OutputPass.js';
import { DURATION, SHOTS, FLASHES, LIGHTNING, DOLPHINS, JUMP, SHOOTING } from './film-timeline.js';

const qs = new URLSearchParams(location.search);
const LANG = qs.get('lang') === 'en' ? 'en' : 'ko';
document.documentElement.classList.add(LANG);

// ------------------------------------------------------------------------------------------------ helpers
const PI = Math.PI;
const clamp = (x, a = 0, b = 1) => Math.min(b, Math.max(a, x));
const lerp = (a, b, k) => a + (b - a) * k;
const lerp3 = (a, b, k) => [lerp(a[0], b[0], k), lerp(a[1], b[1], k), lerp(a[2], b[2], k)];
const range = (t, a, b) => clamp((t - a) / (b - a));
const smooth = k => { k = clamp(k); return k * k * (3 - 2 * k); };
const ease = k => { k = clamp(k); return k < 0.5 ? 4 * k * k * k : 1 - Math.pow(-2 * k + 2, 3) / 2; };
const backOut = k => { k = clamp(k); const c1 = 1.9, c3 = c1 + 1; return 1 + c3 * Math.pow(k - 1, 3) + c1 * Math.pow(k - 1, 2); };
function rng(seed) {
  let s = (seed * 2654435761) >>> 0 || 1;
  return () => { s ^= s << 13; s >>>= 0; s ^= s >>> 17; s ^= s << 5; s >>>= 0; return s / 4294967296; };
}
const PERM = (() => { const r = rng(7), p = new Float32Array(512); for (let i = 0; i < 256; i++) p[i] = p[i + 256] = r(); return p; })();
const cell = (x, y) => PERM[((x * 73 + y * 151) & 255) + ((y * 37) & 255) & 511];
function vnoise(x, y) {
  const xi = Math.floor(x), yi = Math.floor(y), fx = x - xi, fy = y - yi, u = fx * fx * (3 - 2 * fx), v = fy * fy * (3 - 2 * fy);
  return lerp(lerp(cell(xi, yi), cell(xi + 1, yi), u), lerp(cell(xi, yi + 1), cell(xi + 1, yi + 1), u), v);
}
function fbm(x, y, oct = 4) { let v = 0, a = 0.5; for (let i = 0; i < oct; i++) { v += a * vnoise(x, y); x *= 2.03; y *= 2.03; a *= 0.5; } return v / (1 - Math.pow(0.5, oct)); }

const NOISE = `
  float hash(vec2 p){ p = fract(p * vec2(123.34, 456.21)); p += dot(p, p + 45.32); return fract(p.x * p.y); }
  float vnoise(vec2 p){ vec2 i = floor(p), f = fract(p); f = f * f * (3.0 - 2.0 * f);
    return mix(mix(hash(i), hash(i + vec2(1, 0)), f.x), mix(hash(i + vec2(0, 1)), hash(i + vec2(1, 1)), f.x), f.y); }
  float fbm(vec2 p){ float v = 0.0, a = 0.5; for (int i = 0; i < 4; i++){ v += a * vnoise(p); p *= 2.03; a *= 0.5; } return v; }`;

// ------------------------------------------------------------------------------------------------ renderer
const W = 1920, H = 1080;
const canvas = document.getElementById('c');
const renderer = new THREE.WebGLRenderer({ canvas, antialias: false, preserveDrawingBuffer: true });
renderer.setPixelRatio(1);
renderer.setSize(W, H, false);
const scene = new THREE.Scene();
scene.fog = new THREE.FogExp2('#ffffff', 0.002);
const camera = new THREE.PerspectiveCamera(40, W / H, 0.1, 6000);
const G = { time: { value: 0 }, flash: { value: 0 }, pscale: { value: 1 } };

const hemi = new THREE.HemisphereLight('#fff', '#888', 1.5);
const sunL = new THREE.DirectionalLight('#fff', 2);
const lanternL = new THREE.PointLight('#ffc978', 0, 0, 0);
scene.add(hemi, sunL);

const composer = new EffectComposer(renderer, new THREE.WebGLRenderTarget(W, H, { type: THREE.HalfFloatType, samples: 4 }));
composer.setPixelRatio(1);
composer.setSize(W, H);
composer.addPass(new RenderPass(scene, camera));
const bloom = new UnrealBloomPass(new THREE.Vector2(W, H), 0.4, 0.6, 0.85);
composer.addPass(bloom);
composer.addPass(new OutputPass());
const grade = new ShaderPass({
  uniforms: { tDiffuse: { value: null }, time: G.time, res: { value: new THREE.Vector2(W, H) } },
  vertexShader: 'varying vec2 vUv; void main(){ vUv = uv; gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 1.0); }',
  fragmentShader: `uniform sampler2D tDiffuse; uniform float time; uniform vec2 res; varying vec2 vUv; ${NOISE}
    void main(){
      vec3 c = texture2D(tDiffuse, vUv).rgb;
      c = mix(c, smoothstep(0.0, 1.0, c), 0.22);
      float l = dot(c, vec3(0.3, 0.59, 0.11));
      c = mix(vec3(l), c, 1.08);
      vec2 q = (vUv - 0.5) * vec2(1.0, 0.82);
      c *= mix(0.72, 1.0, smoothstep(0.68, 0.2, length(q)));
      c += (hash(vUv * res + floor(time * 24.0)) - 0.5) * 0.022;
      gl_FragColor = vec4(c, 1.0);
    }`,
});
composer.addPass(grade);

function canvasTex(w, h, draw) {
  const c = document.createElement('canvas'); c.width = w; c.height = h;
  draw(c.getContext('2d'), w, h);
  const t = new THREE.CanvasTexture(c); t.colorSpace = THREE.SRGBColorSpace; t.anisotropy = 8;
  return t;
}
const blobTex = canvasTex(128, 128, (g, w, h) => {
  const gr = g.createRadialGradient(w / 2, h / 2, 0, w / 2, h / 2, w / 2);
  gr.addColorStop(0, 'rgba(255,255,255,1)'); gr.addColorStop(0.4, 'rgba(255,255,255,0.5)'); gr.addColorStop(1, 'rgba(255,255,255,0)');
  g.fillStyle = gr; g.fillRect(0, 0, w, h);
});
const rayTex = canvasTex(64, 512, (g, w, h) => {
  const gr = g.createLinearGradient(0, 0, 0, h);
  gr.addColorStop(0, 'rgba(255,255,255,0.9)'); gr.addColorStop(1, 'rgba(255,255,255,0)');
  g.fillStyle = gr; g.fillRect(0, 0, w, h);
  const side = g.createLinearGradient(0, 0, w, 0);
  side.addColorStop(0, 'rgba(0,0,0,1)'); side.addColorStop(0.5, 'rgba(0,0,0,0)'); side.addColorStop(1, 'rgba(0,0,0,1)');
  g.globalCompositeOperation = 'destination-out'; g.fillStyle = side; g.fillRect(0, 0, w, h);
});
const glow = (parent, color, size, x, y, z, opacity = 1) => {
  const m = new THREE.Mesh(new THREE.PlaneGeometry(1, 1), new THREE.MeshBasicMaterial({ map: blobTex, color, transparent: true, opacity, depthWrite: false, blending: THREE.AdditiveBlending, fog: false }));
  m.position.set(x, y, z); m.scale.setScalar(size); m.userData.billboard = true; parent.add(m);
  return m;
};

// ------------------------------------------------------------------------------------------------ materials
// One soft painted shader for all scenery: a lit and a shaded tint, a rim light, fog and lightning flashes.
const SOFT_V = `
  varying vec3 vN; varying vec3 vW; varying vec3 vC;
  void main(){
    vC = vec3(1.0);
    #ifdef USE_COLOR
      vC *= color;
    #endif
    #ifdef USE_INSTANCING_COLOR
      vC *= instanceColor;
    #endif
    mat4 m = modelMatrix;
    #ifdef USE_INSTANCING
      m = modelMatrix * instanceMatrix;
    #endif
    vec4 w = m * vec4(position, 1.0);
    vW = w.xyz; mat3 m3 = mat3(m); vN = normalize(m3 * (normal / vec3(dot(m3[0], m3[0]), dot(m3[1], m3[1]), dot(m3[2], m3[2]))));
    gl_Position = projectionMatrix * viewMatrix * w;
  }`;
const SOFT_F = `
  uniform vec3 col, sunDir, fogCol, shadeTint, litTint, rimCol; uniform float fogDen, rim, steps, emis, flash;
  varying vec3 vN; varying vec3 vW; varying vec3 vC;
  void main(){
    vec3 n = normalize(vN); if (!gl_FrontFacing) n = -n;
    vec3 base = col * vC;
    float d = dot(n, sunDir) * 0.5 + 0.5;
    float band = steps > 0.5 ? smoothstep(0.46, 0.54, d) : smoothstep(0.2, 0.85, d);
    vec3 c = mix(base * shadeTint, base * litTint, band);
    vec3 v = normalize(cameraPosition - vW);
    c += rimCol * pow(1.0 - max(dot(n, v), 0.0), 3.0) * rim;
    c += base * emis;
    c += flash * vec3(0.5, 0.55, 0.85) * (0.25 + 0.75 * max(n.y, 0.0));
    float dist = length(cameraPosition - vW);
    gl_FragColor = vec4(mix(c, fogCol + flash * vec3(0.2, 0.22, 0.35), 1.0 - exp(-dist * dist * fogDen * fogDen)), 1.0);
  }`;
const worlds = {};
function makeWorld(key, env) {
  const group = new THREE.Group(); group.visible = false; scene.add(group);
  const U = {
    sunDir: { value: new THREE.Vector3(...env.sun).normalize() }, fogCol: { value: new THREE.Color(env.fog) }, fogDen: { value: env.den },
    shadeTint: { value: new THREE.Color(env.shade) }, litTint: { value: new THREE.Color(env.lit ?? '#ffffff') },
    rimCol: { value: new THREE.Color(env.rim ?? '#ffffff') }, flash: G.flash, time: G.time,
  };
  const s = env.sky;
  const sky = new THREE.Mesh(new THREE.SphereGeometry(3000, 32, 20), new THREE.ShaderMaterial({
    side: THREE.BackSide, depthWrite: false,
    uniforms: { ...U, top: { value: new THREE.Color(s.top) }, mid: { value: new THREE.Color(s.mid) }, bot: { value: new THREE.Color(s.bot) },
      sunCol: { value: new THREE.Color(s.sunCol ?? '#000000').multiplyScalar(s.sunPow ?? 1) }, stars: { value: s.stars ?? 0 }, band: { value: s.band ?? 0 } },
    vertexShader: 'varying vec3 vD; void main(){ vD = position; gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 1.0); }',
    fragmentShader: `uniform vec3 top, mid, bot, sunDir, sunCol; uniform float stars, band, time, flash; varying vec3 vD; ${NOISE}
      void main(){
        vec3 d = normalize(vD); float y = d.y;
        vec3 c = mix(bot, mid, smoothstep(-0.12, 0.16, y)); c = mix(c, top, smoothstep(0.12, 0.8, y));
        float s = max(dot(d, sunDir), 0.0);
        c += sunCol * (smoothstep(0.99975, 0.9999, s) * 2.5 + pow(s, 900.0) * 0.35 + pow(s, 60.0) * 0.14 + pow(s, 6.0) * 0.1);
        if (stars > 0.0) {
          vec2 uv = vec2(atan(d.z, d.x), asin(clamp(y, -1.0, 1.0))) * 110.0; vec2 g = floor(uv), f = fract(uv) - 0.5;
          float h = hash(g), tw = 0.6 + 0.4 * sin(time * 2.0 + h * 40.0);
          c += vec3(0.9, 0.95, 1.0) * step(0.975, h) * smoothstep(0.22 * fract(h * 37.0) + 0.06, 0.0, length(f)) * tw * stars * 2.2 * smoothstep(-0.05, 0.25, y);
          float bd = dot(d, normalize(vec3(0.3, 0.6, -0.75))) * 4.0; float b = exp(-bd * bd);
          c += band * b * vec3(0.2, 0.22, 0.45) * (0.4 + fbm(uv * 0.06));
        }
        c += flash * vec3(0.22, 0.25, 0.42);
        gl_FragColor = vec4(c, 1.0);
      }`,
  }));
  sky.frustumCulled = false; sky.renderOrder = -10; group.add(sky);
  return (worlds[key] = { key, group, U, sky, env, update() {} });
}
const mat = (w, color, o = {}) => new THREE.ShaderMaterial({
  uniforms: { ...w.U, col: { value: new THREE.Color(color) }, rim: { value: o.rim ?? 0.12 }, steps: { value: o.steps ?? 0 }, emis: { value: o.emis ?? 0 } },
  vertexShader: SOFT_V, fragmentShader: SOFT_F, vertexColors: !!o.vc, side: o.side ?? THREE.FrontSide,
});
const M4 = new THREE.Matrix4(), Q = new THREE.Quaternion(), E = new THREE.Euler(), P3 = new THREE.Vector3(), S3 = new THREE.Vector3();
function instances(parent, geo, material, items, colors) {
  const m = new THREE.InstancedMesh(geo, material, items.length); m.frustumCulled = false;
  const c = new THREE.Color();
  items.forEach((it, i) => {
    Q.setFromEuler(E.set(it[6] ?? 0, it[7] ?? 0, it[8] ?? 0));
    m.setMatrixAt(i, M4.compose(P3.set(it[0], it[1], it[2]), Q, S3.set(it[3], it[4], it[5])));
    if (colors) m.setColorAt(i, c.set(colors[i]));
  });
  parent.add(m);
  return m;
}
const PUFF = new THREE.SphereGeometry(1, 18, 12);
const puffs = (w, items, color, o = {}) => instances(w.group, PUFF, mat(w, color, { rim: 0.25, ...o }), items);
function terrain(w, size, seg, hfn, cfn, o = {}) {
  const g = new THREE.PlaneGeometry(size, size, seg, seg); g.rotateX(-PI / 2);
  if (o.cx) g.translate(o.cx, 0, 0);
  const p = g.attributes.position, col = new Float32Array(p.count * 3), c = new THREE.Color();
  for (let i = 0; i < p.count; i++) { const x = p.getX(i), z = p.getZ(i), y = hfn(x, z); p.setY(i, y); cfn(x, z, y, c); col[i * 3] = c.r; col[i * 3 + 1] = c.g; col[i * 3 + 2] = c.b; }
  g.setAttribute('color', new THREE.BufferAttribute(col, 3)); g.computeVertexNormals();
  const m = new THREE.Mesh(g, mat(w, '#ffffff', { vc: true, rim: 0.05, ...o })); m.frustumCulled = false; w.group.add(m);
  return m;
}

// Soft round particles with per-particle size and alpha: fireflies, snow, petals, flowers, sparkles, spray.
class Particles {
  constructor(parent, n, { additive = false, tint = [1, 1, 1], soft = true, w = null, maxPx = 4096 } = {}) {
    this.n = n; this.pos = new Float32Array(n * 3); this.size = new Float32Array(n); this.alpha = new Float32Array(n).fill(1); this.col = new Float32Array(n * 3).fill(1);
    const g = this.geo = new THREE.BufferGeometry();
    g.setAttribute('position', new THREE.BufferAttribute(this.pos, 3)); g.setAttribute('aSize', new THREE.BufferAttribute(this.size, 1));
    g.setAttribute('aAlpha', new THREE.BufferAttribute(this.alpha, 1)); g.setAttribute('aCol', new THREE.BufferAttribute(this.col, 3));
    this.points = new THREE.Points(g, new THREE.ShaderMaterial({
      transparent: true, depthWrite: false, blending: additive ? THREE.AdditiveBlending : THREE.NormalBlending,
      uniforms: { scale: G.pscale, maxPx: { value: maxPx }, tint: { value: new THREE.Color(...tint) }, soft: { value: soft ? 1 : 0 }, fogCol: w ? w.U.fogCol : { value: new THREE.Color() }, fogDen: w ? w.U.fogDen : { value: 0 } },
      vertexShader: `attribute float aSize, aAlpha; attribute vec3 aCol; uniform float scale, maxPx; varying float vA, vF; varying vec3 vCol;
        void main(){ vec4 mv = modelViewMatrix * vec4(position, 1.0); vA = aAlpha; vCol = aCol; vF = -mv.z; gl_PointSize = min(aSize * scale / max(-mv.z, 0.1), maxPx); gl_Position = projectionMatrix * mv; }`,
      fragmentShader: `uniform vec3 tint, fogCol; uniform float soft, fogDen; varying float vA, vF; varying vec3 vCol;
        void main(){ float d = length(gl_PointCoord - 0.5) * 2.0; float a = soft > 0.5 ? pow(smoothstep(1.0, 0.0, d), 1.6) : smoothstep(1.0, 0.82, d);
          vec3 c = mix(tint * vCol, fogCol, 1.0 - exp(-vF * vF * fogDen * fogDen)); gl_FragColor = vec4(c, a * vA); }`,
    }));
    this.points.frustumCulled = false; parent.add(this.points);
  }
  set(i, x, y, z, s, a = 1) { this.pos[i * 3] = x; this.pos[i * 3 + 1] = y; this.pos[i * 3 + 2] = z; this.size[i] = s; this.alpha[i] = a; }
  color(i, c) { this.col[i * 3] = c.r; this.col[i * 3 + 1] = c.g; this.col[i * 3 + 2] = c.b; }
  done() { for (const k of ['position', 'aSize', 'aAlpha', 'aCol']) this.geo.attributes[k].needsUpdate = true; }
}

// ------------------------------------------------------------------------------------------------ pets
const gradMap = (() => { const t = new THREE.DataTexture(new Uint8Array([165, 255]), 2, 1, THREE.RedFormat); t.minFilter = t.magFilter = THREE.NearestFilter; t.needsUpdate = true; return t; })();
const toon = (c, extra = {}) => new THREE.MeshToonMaterial({ color: c, gradientMap: gradMap, ...extra });
const OUT = new THREE.MeshBasicMaterial({ color: '#4b3326', side: THREE.BackSide });
const SPH = new THREE.SphereGeometry(1, 32, 20), CONE = new THREE.ConeGeometry(1, 1, 18), LEG = new THREE.CapsuleGeometry(0.075, 0.24, 4, 12);
function add(parent, geo, material, pos, scl, outline = true) {
  const m = new THREE.Mesh(geo, material); m.position.set(...pos); if (scl) m.scale.set(...scl); parent.add(m);
  if (outline) { const o = new THREE.Mesh(geo, OUT); o.scale.setScalar(1.07); m.add(o); }
  return m;
}
function makePet(parent, o) {
  const p = { o, hips: [], ears: [], eyes: [], seed: o.seed };
  const cat = o.kind === 'cat';
  const root = p.root = new THREE.Group(); parent.add(root);
  const body = p.body = new THREE.Group(); root.add(body);
  const coat = toon(o.coat), belly = toon(o.belly), ink = toon('#2a201c');
  const hipY = 0.4;
  add(body, SPH, coat, [0, hipY + 0.1, 0], [0.3, 0.27, 0.47]);
  add(body, SPH, belly, [0, hipY + 0.02, 0.2], [0.22, 0.18, 0.26], false);
  for (const [x, z] of [[-0.16, 0.28], [0.16, 0.28], [-0.16, -0.28], [0.16, -0.28]]) {
    const hip = new THREE.Group(); hip.position.set(x, hipY, z); body.add(hip);
    add(hip, LEG, coat, [0, -0.18, 0]); add(hip, SPH, belly, [0, -0.37, 0.025], [0.085, 0.055, 0.105]);
    p.hips.push(hip);
  }
  const head = p.head = new THREE.Group(); head.position.set(0, hipY + 0.4, 0.4); body.add(head);
  add(head, SPH, coat, [0, 0.06, 0], cat ? [0.29, 0.25, 0.26] : [0.28, 0.27, 0.27]);
  if (!cat) { add(head, SPH, belly, [0, -0.04, 0.19], [0.15, 0.12, 0.15]); add(head, SPH, ink, [0, 0.02, 0.335], [0.055, 0.042, 0.04], false); }
  else { add(head, SPH, belly, [0, -0.03, 0.2], [0.13, 0.09, 0.09], false); add(head, SPH, toon('#e98a9a'), [0, 0.02, 0.285], [0.03, 0.022, 0.02], false); }
  const white = new THREE.MeshBasicMaterial({ color: '#ffffff' }), blushMat = new THREE.MeshBasicMaterial({ color: '#ff8fa3', transparent: true, opacity: 0.55 });
  for (const s of [-1, 1]) {
    const eye = add(head, SPH, ink, [s * 0.11, 0.11, 0.215], [0.042, 0.054, 0.035], false);
    const hl = new THREE.Mesh(SPH, white); hl.position.set(0.3, 0.38, 0.82); hl.scale.setScalar(0.32); eye.add(hl);
    p.eyes.push(eye);
    const blush = new THREE.Mesh(SPH, blushMat); blush.position.set(s * 0.175, 0, 0.19); blush.scale.set(0.05, 0.028, 0.02); head.add(blush);
    const ear = new THREE.Group(); head.add(ear); ear.userData.s = s;
    if (cat) { ear.position.set(s * 0.15, 0.27, -0.01); add(ear, CONE, coat, [0, 0.08, 0], [0.1, 0.17, 0.07]); add(ear, CONE, toon('#f4a7b4'), [0, 0.06, 0.045], [0.055, 0.11, 0.03], false); }
    else { ear.position.set(s * 0.24, 0.17, -0.02); add(ear, SPH, toon(o.ear), [s * 0.03, -0.14, 0], [0.085, 0.19, 0.13]); }
    p.ears.push(ear);
  }
  const tail = p.tail = new THREE.Group(); tail.position.set(0, hipY + 0.2, -0.42); body.add(tail);
  if (cat) add(tail, new THREE.CapsuleGeometry(0.05, 0.5, 4, 10), coat, [0, 0.28, 0]);
  else add(tail, new THREE.CapsuleGeometry(0.06, 0.2, 4, 10), coat, [0, 0.15, 0]);
  // A scarf, flapping behind in the wind.
  add(body, new THREE.TorusGeometry(0.2, 0.06, 8, 20), toon(o.scarf), [0, hipY + 0.27, 0.3], [1, 1, 1]).rotation.x = PI / 2 - 0.5;
  p.scarf = new THREE.Group(); p.scarf.position.set(0.12, hipY + 0.25, 0.14); body.add(p.scarf);
  add(p.scarf, new THREE.BoxGeometry(0.11, 0.03, 0.5), toon(o.scarf), [0, 0, -0.25]);
  return p;
}
function pose(p, st, t) {
  const cat = p.o.kind === 'cat', sit = st.sit ?? 1, lie = st.lie ?? 0, wind = st.wind ?? 0;
  p.root.position.set(st.x, st.y + (st.jump ?? 0), st.z); p.root.rotation.y = st.yaw ?? 0; p.root.scale.setScalar(st.s ?? 1);
  const a = st.amp ?? 0, wk = Math.sin(t * 9) * a;
  p.hips[0].rotation.x = wk + 0.5 * sit - 1.3 * lie; p.hips[1].rotation.x = -wk + 0.5 * sit - 1.3 * lie;
  p.hips[2].rotation.x = -wk - 1.1 * sit + 1.3 * lie; p.hips[3].rotation.x = wk - 1.1 * sit + 1.3 * lie;
  p.hips[0].scale.y = p.hips[1].scale.y = (1 + 0.3 * sit * (1 - lie)) * (1 - 0.45 * lie); p.hips[2].scale.y = p.hips[3].scale.y = 1 - 0.45 * lie;
  p.body.rotation.x = -0.5 * sit * (1 - lie); p.body.position.y = -0.2 * lie;
  p.body.rotation.z = Math.sin(t * 38) * 0.25 * (st.shake ?? 0);
  p.head.rotation.set(0.45 * sit * (1 - lie) + (st.nod ?? 0) + lie * 0.25, st.look ?? 0, st.tilt ?? 0);
  p.tail.rotation.y = Math.sin(t * (cat ? 2.2 : 13) + p.seed) * 0.55 * (st.wag ?? 0.5);
  p.tail.rotation.x = (cat ? -0.45 : -0.85) + lie * 0.9 - wind * 0.3;
  for (const [i, ear] of p.ears.entries()) {
    const s = ear.userData.s, flap = wind * (0.8 + Math.sin(t * 17 + i * 2 + p.seed) * 0.3);
    if (cat) { ear.rotation.z = -s * 0.28 - s * (st.scared ?? 0) * 0.8; ear.rotation.x = -wind * 0.35 - (st.scared ?? 0) * 0.5; }
    else { ear.rotation.z = s * (0.3 + flap * 1.0); ear.rotation.x = -flap * 0.7; }
  }
  p.scarf.rotation.x = -0.9 + wind * (0.75 + Math.sin(t * 15 + p.seed) * 0.22); p.scarf.rotation.y = Math.sin(t * 11 + p.seed) * 0.25 * wind; p.scarf.scale.z = 0.5 + wind * 0.6;
  const blink = ((t * 0.27 + p.seed * 0.37) % 1) < 0.035;
  for (const e of p.eyes) e.scale.y = st.happy || blink ? 0.009 : 0.054;
}

// ------------------------------------------------------------------------------------------------ the window
// An app window lying flat like a flying carpet: a cream pane, a coral title bar with three buttons, a lantern.
const veh = new THREE.Group(); scene.add(veh);
{
  const s = new THREE.Shape(), w = 1.45, l = 1.25, r = 0.22;
  s.moveTo(-w + r, -l); s.lineTo(w - r, -l); s.quadraticCurveTo(w, -l, w, -l + r); s.lineTo(w, l - r); s.quadraticCurveTo(w, l, w - r, l);
  s.lineTo(-w + r, l); s.quadraticCurveTo(-w, l, -w, l - r); s.lineTo(-w, -l + r); s.quadraticCurveTo(-w, -l, -w + r, -l);
  const g = new THREE.ExtrudeGeometry(s, { depth: 0.1, bevelEnabled: true, bevelThickness: 0.03, bevelSize: 0.03, bevelSegments: 2, curveSegments: 8 });
  g.rotateX(PI / 2);
  add(veh, g, toon('#f2e4cc'), [0, 0, 0]);
  add(veh, new THREE.BoxGeometry(2.72, 0.05, 0.34), toon('#ff8a5c'), [0, 0.035, -1.02], null, false);
  ['#ffe08a', '#9be7a0', '#ffffff'].forEach((c, i) => add(veh, new THREE.CylinderGeometry(0.07, 0.07, 0.03, 16), toon(c), [-1.12 + i * 0.22, 0.07, -1.02], null, false));
  for (let i = 0; i < 3; i++) add(veh, new THREE.BoxGeometry(1.5 - i * 0.35, 0.012, 0.06), toon('#eadfca'), [0.45, 0.036, -0.55 + i * 0.2], null, false);
  add(veh, new THREE.CylinderGeometry(0.03, 0.03, 1.3, 8), toon('#6b4a36'), [-1.2, 0.65, -0.72]);
  add(veh, new THREE.CylinderGeometry(0.025, 0.025, 0.42, 8), toon('#6b4a36'), [-1.02, 1.28, -0.72], null, false).rotation.z = PI / 2;
  veh.userData.lamp = add(veh, SPH, new THREE.MeshBasicMaterial({ color: new THREE.Color(1.5, 1.1, 0.5) }), [-0.84, 1.08, -0.72], [0.11, 0.14, 0.11], false);
  add(veh, new THREE.BoxGeometry(0.2, 0.04, 0.2), toon('#6b4a36'), [-0.84, 1.23, -0.72], null, false);
  veh.userData.halo = glow(veh, '#ffb957', 1.3, -0.84, 1.08, -0.72, 0.8);
  lanternL.position.set(-0.84, 1.1, -0.5); veh.add(lanternL);
  veh.userData.under = new THREE.Mesh(new THREE.PlaneGeometry(4.6, 4.2), new THREE.MeshBasicMaterial({ map: blobTex, color: '#ffe2a8', transparent: true, opacity: 0.5, depthWrite: false, blending: THREE.AdditiveBlending, fog: false }));
  veh.userData.under.rotation.x = PI / 2; veh.userData.under.position.y = -0.16; veh.add(veh.userData.under);
}
const dog = makePet(veh, { kind: 'dog', coat: '#e7b36e', belly: '#fff1dc', ear: '#c98c4b', scarf: '#e2574c', seed: 1 });
const cat = makePet(veh, { kind: 'cat', coat: '#8f9aab', belly: '#f4f1ec', scarf: '#4c8fd6', seed: 2 });
const trail = new Particles(veh, 90, { additive: true, tint: [2.2, 1.7, 0.9] });

// ------------------------------------------------------------------------------------------------ world: room
const loader = new THREE.TextureLoader();
const wallpaper = await loader.loadAsync('wallpapers/sunny-meadow.jpg'); wallpaper.colorSpace = THREE.SRGBColorSpace; wallpaper.anisotropy = 8;
{
  const w = makeWorld('room', { sun: [0.75, 0.4, 0.5], fog: '#3a2d55', den: 0.004, shade: '#5c4a90', lit: '#ffd9b0', rim: '#ffb37a',
    sky: { top: '#2d2a5c', mid: '#8a5a8c', bot: '#f2a27a' }, hemi: ['#b9a6e0', '#5a3f4a', 1.3], sunL: ['#ffc79a', 1.7], bloom: 0.55, lantern: 0.5 });
  const g = w.group, box = (sx, sy, sz, c, x, y, z, o) => { const m = new THREE.Mesh(new THREE.BoxGeometry(sx, sy, sz), mat(w, c, o)); m.position.set(x, y, z); g.add(m); return m; };
  const cyl = (rt, rb, h, c, x, y, z, o) => { const m = new THREE.Mesh(new THREE.CylinderGeometry(rt, rb, h, 24), mat(w, c, o)); m.position.set(x, y, z); g.add(m); return m; };
  box(80, 40, 0.5, '#7a689e', 0, 12, -4.5); box(80, 0.5, 40, '#4a3a5c', 0, -9, 10);
  box(34, 0.4, 10, '#9a6844', 0, -0.2, 1.2); box(34, 1.2, 0.4, '#84563a', 0, -0.9, 6.1);
  const winTex = [['#34306e', '#c66a8a', '#ffb070'], ['#7ec3f2', '#ffe9c0', '#ffd08a']].map(cols => canvasTex(64, 256, (c, cw, ch) => {
    const gr = c.createLinearGradient(0, 0, 0, ch); gr.addColorStop(0, cols[0]); gr.addColorStop(0.6, cols[1]); gr.addColorStop(1, cols[2]); c.fillStyle = gr; c.fillRect(0, 0, cw, ch);
  }));
  const pane = new THREE.Mesh(new THREE.PlaneGeometry(8, 6.4), new THREE.MeshBasicMaterial({ map: winTex[0], color: new THREE.Color(1.5, 1.5, 1.5), fog: false })); pane.position.set(9.5, 6, -4.2); g.add(pane);
  box(8.6, 0.3, 0.3, '#f1e6d6', 9.5, 9.3, -4.1); box(8.6, 0.3, 0.3, '#f1e6d6', 9.5, 2.7, -4.1); box(0.3, 6.9, 0.3, '#f1e6d6', 5.35, 6, -4.1); box(0.3, 6.9, 0.3, '#f1e6d6', 13.65, 6, -4.1);
  box(0.16, 6.4, 0.2, '#f1e6d6', 9.5, 6, -4.1); box(8, 0.16, 0.2, '#f1e6d6', 9.5, 6, -4.1);
  const paneGlow = glow(g, '#ffb070', 22, 9.5, 6, -3.9, 0.5);
  box(5.3, 3.2, 0.3, '#2a2c38', 0, 2.3, -0.2); box(0.5, 1.0, 0.25, '#2a2c38', 0, 0.5, -0.3); cyl(1.2, 1.3, 0.1, '#2a2c38', 0, 0.05, -0.3);
  const screen = new THREE.Mesh(new THREE.PlaneGeometry(4.9, 2.76), new THREE.MeshBasicMaterial({ map: wallpaper, color: new THREE.Color(0.95, 0.95, 0.95), fog: false })); screen.position.set(0, 2.3, -0.04); g.add(screen);
  glow(g, '#9fd0ff', 11, 0, 2.3, -0.6, 0.35);
  box(3.4, 0.12, 1.2, '#efe7da', 0, 0.06, 2.6); box(0.7, 0.08, 1.0, '#efe7da', 2.7, 0.04, 2.6);
  cyl(0.3, 0.26, 0.62, '#e2725b', 3.9, 0.31, 1.3); cyl(0.5, 0.38, 0.7, '#c98f6a', -4.6, 0.35, 0.4);
  instances(g, PUFF, mat(w, '#5aa864', { rim: 0.2 }), [[-4.6, 1.1, 0.4, 0.55, 0.7, 0.55], [-5.0, 1.5, 0.5, 0.4, 0.6, 0.4], [-4.2, 1.6, 0.2, 0.42, 0.6, 0.42], [-4.6, 2.0, 0.4, 0.35, 0.5, 0.35]]);
  [['#d96a5a', 0.3], ['#5a8fd9', 0.26], ['#e8c15a', 0.22]].forEach(([c, h], i) => box(1.7 - i * 0.15, h, 1.2, c, 6.3, 0.15 + i * 0.28, 0.4));
  cyl(0.05, 0.05, 3.0, '#3a3440', -7.2, 1.5, -0.6); cyl(0.5, 0.9, 0.7, '#ffd39a', -7.2, 3.1, -0.6, { emis: 0.9 });
  const lampGlow = glow(g, '#ffb866', 9, -7.2, 2.9, -0.3, 0.55);
  const dusk = { shade: new THREE.Color('#5c4a90'), lit: new THREE.Color('#ffd9b0'), fog: new THREE.Color('#3a2d55') };
  const morn = { shade: new THREE.Color('#8f9fd0'), lit: new THREE.Color('#fff6e2'), fog: new THREE.Color('#cfd9ee') };
  w.update = t => {
    const m = t > 50 ? morn : dusk;
    w.U.shadeTint.value.copy(m.shade); w.U.litTint.value.copy(m.lit); w.U.fogCol.value.copy(m.fog);
    pane.material.map = winTex[t > 50 ? 1 : 0]; pane.material.color.setScalar(t > 50 ? 1.0 : 1.5); paneGlow.material.opacity = t > 50 ? 0.2 : 0.5; paneGlow.material.color.set(t > 50 ? '#ffe2a8' : '#ffb070'); lampGlow.material.opacity = t > 50 ? 0.12 : 0.55;
    w.env.hemi = t > 50 ? ['#dfe9ff', '#b89a84', 1.9] : ['#b9a6e0', '#5a3f4a', 1.3];
  };
}

// ------------------------------------------------------------------------------------------------ world: clouds
function cloudSea(w, seed, n, y0, spread, color) {
  const r = rng(seed), items = [];
  for (let i = 0; i < n; i++) {
    const x = (r() * 2 - 1) * spread, z = (r() * 2 - 1) * spread, s = 6 + r() * r() * 16;
    items.push([x, y0 + fbm(x / 90 + 5, z / 90) * 16 + s * 0.3, z, s, s * 0.62, s]);
  }
  for (let k = 0; k < 10; k++) {
    const a = r() * PI * 2, d = 170 + r() * 230; let cx = Math.cos(a) * d + 40, cz = Math.sin(a) * d;
    if (Math.abs(cz) < 70) cz += Math.sign(cz || 1) * 80;
    const h = 45 + r() * 75;
    for (let j = 0; j < 34; j++) { const f = j / 34, rad = (1 - f * 0.72) * (24 + r() * 10); items.push([cx + (r() - 0.5) * rad * 1.6, y0 + 12 + f * h, cz + (r() - 0.5) * rad * 1.6, rad * (0.5 + r() * 0.5), rad * (0.4 + r() * 0.4), rad * (0.5 + r() * 0.5)]); }
  }
  return puffs(w, items, color, { rim: w.key === 'moon' ? 0.55 : 0.3 });
}
const birds = [];
{
  const w = makeWorld('clouds', { sun: [0.9, 0.13, -0.4], fog: '#f3a9a4', den: 0.0030, shade: '#9a78cf', lit: '#ffe6cf', rim: '#fff0c4',
    sky: { top: '#4753b0', mid: '#ee93a6', bot: '#ffd7a1', sunCol: '#ffd9a0', sunPow: 1.6 }, hemi: ['#ffd9d0', '#b88ad0', 1.7], sunL: ['#ffd2a0', 2.6], bloom: 0.55, lantern: 0.2 });
  cloudSea(w, 21, 2400, -24, 470, '#ffffff');
  const wing = new THREE.BufferGeometry(); wing.setAttribute('position', new THREE.Float32BufferAttribute([0, 0, 0.25, 0, 0, -0.25, 1.2, 0, -0.1], 3)); wing.computeVertexNormals();
  const bm = new THREE.MeshBasicMaterial({ color: '#5b3f6a', side: THREE.DoubleSide });
  for (let i = 0; i < 16; i++) {
    const b = new THREE.Group(), l = new THREE.Mesh(wing, bm), rr = new THREE.Mesh(wing, bm); rr.scale.x = -1; b.add(l, rr); b.userData = { l, r: rr, i }; w.group.add(b); birds.push(b);
  }
  w.update = t => birds.forEach(b => {
    const i = b.userData.i, row = Math.ceil(i / 2), side = i % 2 ? 1 : -1;
    b.position.set(-6 + 7.5 * (t - 12) - row * 2.2, 21 + Math.sin(t * 0.8 + i) * 0.6 + row * 0.3, -14 + side * row * 2.0);
    b.rotation.y = PI / 2; const f = Math.sin(t * 7 + i * 1.3) * 0.7; b.userData.l.rotation.z = f; b.userData.r.rotation.z = -f;
  });
}

// ------------------------------------------------------------------------------------------------ world: hills
const HH = (x, z) => (fbm(x / 150 + 3, z / 150 + 7) - 0.5) * 70 + (fbm(x / 40, z / 40) - 0.5) * 8;
const hillY = x => (HH(x - 24, 0) + HH(x - 12, 0) + HH(x, 0) + HH(x + 12, 0) + HH(x + 24, 0)) / 5 + 11;
const CABIN = { x: 232, z: 12 }; CABIN.y = HH(CABIN.x, CABIN.z);
const blades = [];
{
  const w = makeWorld('hills', { sun: [0.4, 0.8, 0.35], fog: '#cfe6f2', den: 0.0021, shade: '#5f86b8', lit: '#fff6da', rim: '#ffffff',
    sky: { top: '#2f6fd0', mid: '#8cc4ee', bot: '#e6f3e0', sunCol: '#fff3d0', sunPow: 1.0 }, hemi: ['#eaf6ff', '#8fbf7a', 1.9], sunL: ['#fff1d0', 2.4], bloom: 0.3, lantern: 0 });
  const c1 = new THREE.Color('#4f9a3e'), c2 = new THREE.Color('#9bd65c'), c3 = new THREE.Color('#d6dc6c');
  terrain(w, 760, 220, HH, (x, z, y, c) => { c.lerpColors(c1, c2, clamp(fbm(x / 30 + 9, z / 30) * 1.3 - 0.1 + y / 90)); c.lerp(c3, smooth(range(fbm(x / 70 + 2, z / 70 + 5), 0.58, 0.75)) * 0.6); }, { cx: 110 });
  const r = rng(31), fl = new Particles(w.group, 26000, { soft: false, w, maxPx: 11 }), pal = ['#ffffff', '#ff7aa2', '#ffd166', '#c084fc', '#ff8a5c', '#fff3a0'].map(c => new THREE.Color(c));
  for (let i = 0; i < fl.n; i++) { const x = -90 + r() * 400, z = (r() * 2 - 1) * 110; fl.set(i, x, HH(x, z) + 0.2, z, 0.2 + r() * 0.16); fl.color(i, pal[Math.floor(r() * pal.length)].clone().multiplyScalar(0.8)); }
  fl.done();
  const trunks = [], tops = [], cols = [], greens = ['#3f8a45', '#4c9a4a', '#5aa850', '#3a7f5a'];
  for (let i = 0; i < 520; i++) {
    const x = -160 + r() * 520, z = (r() * 2 - 1) * 240; if (Math.abs(z) < 14 || Math.hypot(x - CABIN.x, z - CABIN.z) < 22 || (x > -45 && x < 75 && z > 0 && z < 48)) continue;
    const y = HH(x, z), s = 2.2 + r() * 2.6;
    trunks.push([x, y + s * 0.5, z, 0.3 * s * 0.4, s, 0.3 * s * 0.4]); tops.push([x, y + s * 1.5, z, s * 0.85, s * 1.15, s * 0.85]); cols.push(greens[Math.floor(r() * 4)]);
  }
  instances(w.group, new THREE.CylinderGeometry(1, 1.2, 1, 8), mat(w, '#7a5236'), trunks);
  instances(w.group, PUFF, mat(w, '#ffffff', { rim: 0.18 }), tops, cols);
  for (const [x, z, s] of [[-12, -34, 1.2], [22, 40, 1.4], [58, -30, 1.1], [92, 46, 1.5], [128, -40, 1.3], [-46, 38, 1.2], [20, -52, 1.0]]) {
    const m = new THREE.Group(); m.position.set(x, HH(x, z) - 0.5, z); m.scale.setScalar(s); m.rotation.y = -0.5; w.group.add(m);
    const tower = new THREE.Mesh(new THREE.CylinderGeometry(1.3, 2.3, 9, 14), mat(w, '#fff4e6')); tower.position.y = 4.5; m.add(tower);
    const cap = new THREE.Mesh(new THREE.ConeGeometry(1.8, 2.2, 14), mat(w, '#d8553f')); cap.position.y = 10.1; m.add(cap);
    const hub = new THREE.Group(); hub.position.set(0, 8.8, 1.7); m.add(hub); blades.push(hub);
    for (let k = 0; k < 4; k++) { const b = new THREE.Mesh(new THREE.BoxGeometry(1.1, 6.4, 0.12), mat(w, '#f7ead0', { side: THREE.DoubleSide })); b.position.y = 3.4; const arm = new THREE.Group(); arm.rotation.z = k * PI / 2; arm.add(b); hub.add(arm); }
  }
  // The pets' cabin, with its lantern: home.
  const cab = new THREE.Group(); cab.position.set(CABIN.x, CABIN.y - 0.3, CABIN.z); cab.rotation.y = -0.9; cab.scale.setScalar(1.6); w.group.add(cab);
  const logs = new THREE.Mesh(new THREE.BoxGeometry(4, 2.6, 3), mat(w, '#b07a45')); logs.position.y = 1.3; cab.add(logs);
  const tri = new THREE.Shape(); tri.moveTo(-2.5, 0); tri.lineTo(2.5, 0); tri.lineTo(0, 2); tri.lineTo(-2.5, 0);
  const roofG = new THREE.ExtrudeGeometry(tri, { depth: 3.6, bevelEnabled: false }); roofG.translate(0, 0, -1.8);
  const roof = new THREE.Mesh(roofG, mat(w, '#c8553e')); roof.position.y = 2.6; cab.add(roof);
  const door = new THREE.Mesh(new THREE.BoxGeometry(0.9, 1.6, 0.1), mat(w, '#6b3f22')); door.position.set(0.8, 0.8, 1.52); cab.add(door);
  const win = new THREE.Mesh(new THREE.BoxGeometry(0.9, 0.8, 0.1), new THREE.MeshBasicMaterial({ color: new THREE.Color(1.35, 1.05, 0.5) })); win.position.set(-0.9, 1.5, 1.52); cab.add(win);
  const pole = new THREE.Mesh(new THREE.CylinderGeometry(0.06, 0.08, 2.6, 8), mat(w, '#3d3a40')); pole.position.set(2.8, 1.3, 1.8); cab.add(pole);
  const bulb = new THREE.Mesh(SPH, new THREE.MeshBasicMaterial({ color: new THREE.Color(1.6, 1.2, 0.55) })); bulb.scale.setScalar(0.2); bulb.position.set(2.8, 2.7, 1.8); cab.add(bulb);
  const halo = glow(cab, '#ffb957', 2.6, 2.8, 2.7, 1.8, 0.7);
  const cr = rng(5), cl = [];
  for (let i = 0; i < 70; i++) { const x = -150 + cr() * 520, z = (cr() * 2 - 1) * 330, s = 14 + cr() * 22; cl.push([x, 95 + cr() * 50, z, s * 1.6, s * 0.6, s]); }
  puffs(w, cl, '#ffffff', { rim: 0.3 });
  const petals = new Particles(w.group, 140, { tint: [1, 0.86, 0.9] });
  const day = w.env, dawn = { sun: new THREE.Vector3(-0.75, 0.16, -0.55).normalize(), fog: new THREE.Color('#f4c7a8'), shade: new THREE.Color('#6b6fb0'), lit: new THREE.Color('#ffe2b8') };
  const dayV = { sun: w.U.sunDir.value.clone(), fog: w.U.fogCol.value.clone(), shade: w.U.shadeTint.value.clone(), lit: w.U.litTint.value.clone() };
  const su = w.sky.material.uniforms, daySky = [su.top.value.clone(), su.mid.value.clone(), su.bot.value.clone(), su.sunCol.value.clone()];
  const dawnSky = [new THREE.Color('#5a78c8'), new THREE.Color('#f6b9a0'), new THREE.Color('#ffe6b0'), new THREE.Color('#ffd9a0').multiplyScalar(0.9)];
  w.update = (t, tl, v) => {
    const d = t > 90, a = d ? dawn : dayV, sk = d ? dawnSky : daySky;
    w.U.sunDir.value.copy(a.sun); w.U.fogCol.value.copy(a.fog); w.U.shadeTint.value.copy(a.shade); w.U.litTint.value.copy(a.lit); w.U.fogDen.value = d ? 0.0034 : day.den;
    su.top.value.copy(sk[0]); su.mid.value.copy(sk[1]); su.bot.value.copy(sk[2]); su.sunCol.value.copy(sk[3]);
    w.env.hemi = d ? ['#ffe1cf', '#8f8ac0', 1.6] : ['#eaf6ff', '#8fbf7a', 1.9]; w.env.sunL = d ? ['#ffc890', 2.2] : ['#fff1d0', 2.4]; w.env.bloom = d ? 0.4 : 0.3; w.env.lantern = d ? 0.6 : 0;
    halo.material.opacity = d ? 0.5 : 0.15;
    blades.forEach((b, i) => { b.rotation.z = t * 0.9 + i; });
    const pr = rng(77);
    for (let i = 0; i < petals.n; i++) {
      const sp = 3 + pr() * 4, ox = pr() * 60, oy = pr() * 22 - 8, oz = pr() * 50 - 25, ph = pr() * 6.3;
      petals.set(i, camera.position.x + 14 - ((t * sp + ox) % 28), camera.position.y + oy * 0.4 + Math.sin(t * 1.1 + ph) * 0.8, camera.position.z + oz * 0.4 + Math.sin(t * 0.7 + ph), 0.09 + pr() * 0.08, 0.9);
    }
    petals.done();
  };
}

// ------------------------------------------------------------------------------------------------ world: sea
const dolphins = [];
let spray;
{
  const w = makeWorld('sea', { sun: [-0.35, 0.7, 0.55], fog: '#bfe8f7', den: 0.0012, shade: '#4f86c0', lit: '#fffbe8', rim: '#ffffff',
    sky: { top: '#1f7fe0', mid: '#7fd0f5', bot: '#e9fbff', sunCol: '#fff6d8', sunPow: 1.3 }, hemi: ['#e6f8ff', '#5fb8d8', 2.0], sunL: ['#fff6dc', 2.5], bloom: 0.35, lantern: 0 });
  const water = new THREE.Mesh(new THREE.PlaneGeometry(2400, 2400, 256, 256).rotateX(-PI / 2), new THREE.ShaderMaterial({
    uniforms: { ...w.U },
    vertexShader: `uniform float time; varying vec3 vW; varying vec3 vN; varying float vH;
      float wave(vec2 p){ return sin(p.x * 0.11 + time * 1.3) * 0.55 + sin(p.y * 0.17 - time * 1.1) * 0.4 + sin((p.x + p.y) * 0.31 + time * 1.9) * 0.22 + sin((p.x * 0.7 - p.y) * 0.53 + time * 2.4) * 0.1; }
      void main(){ vec4 w = modelMatrix * vec4(position, 1.0); float h = wave(w.xz); float e = 0.6;
        vN = normalize(vec3(wave(w.xz - vec2(e, 0.0)) - wave(w.xz + vec2(e, 0.0)), 2.0 * e, wave(w.xz - vec2(0.0, e)) - wave(w.xz + vec2(0.0, e))));
        w.y += h; vH = h; vW = w.xyz; gl_Position = projectionMatrix * viewMatrix * w; }`,
    fragmentShader: `uniform vec3 sunDir, fogCol; uniform float fogDen, time; varying vec3 vW; varying vec3 vN; varying float vH; ${NOISE}
      void main(){ vec3 n = normalize(vN + vec3(fbm(vW.xz * 0.9 + time * 0.6) - 0.5, 0.0, fbm(vW.zx * 0.9 - time * 0.5) - 0.5) * 0.35);
        vec3 v = normalize(cameraPosition - vW); float fr = pow(1.0 - max(dot(n, v), 0.0), 3.0);
        vec3 c = mix(vec3(0.01, 0.2, 0.5), vec3(0.04, 0.55, 0.66), smoothstep(-0.9, 1.0, vH));
        c = mix(c, vec3(0.55, 0.82, 1.0), fr * 0.5);
        c += vec3(1.0, 0.95, 0.8) * pow(max(dot(reflect(-v, n), sunDir), 0.0), 260.0) * 1.6;
        c = mix(c, vec3(0.95), smoothstep(1.05, 1.3, vH + fbm(vW.xz * 0.5 + time * 0.3) * 0.5) * 0.7);
        float dist = length(cameraPosition - vW); gl_FragColor = vec4(mix(c, fogCol, 1.0 - exp(-dist * dist * fogDen * fogDen)), 1.0); }`,
  }));
  water.frustumCulled = false; w.group.add(water);
  for (const [x, z, s] of [[70, -120, 1.4], [-60, -200, 1.0], [210, -150, 1.2], [150, 140, 0.8]]) {
    puffs(w, [[x, -1, z, 34 * s, 5 * s, 26 * s]], '#f3dfa6'); puffs(w, [[x, 2 * s, z, 24 * s, 9 * s, 18 * s], [x + 8 * s, 4 * s, z - 3, 12 * s, 8 * s, 10 * s]], '#58a85a');
    const r = rng(x + 9), palms = [], leaves = [];
    for (let i = 0; i < 6; i++) { const px = x + (r() - 0.5) * 28 * s, pz = z + (r() - 0.5) * 18 * s, h = 9 + r() * 6; palms.push([px, 8 * s + h / 2, pz, 0.35, h, 0.35, 0, 0, (r() - 0.5) * 0.3]); leaves.push([px, 8 * s + h, pz, 3.6, 0.9, 3.6]); }
    instances(w.group, new THREE.CylinderGeometry(0.7, 1, 1, 8), mat(w, '#8a623e'), palms); instances(w.group, PUFF, mat(w, '#3f9a55', { rim: 0.2 }), leaves);
  }
  const stripes = canvasTex(8, 64, (c, cw, ch) => { for (let i = 0; i < 4; i++) { c.fillStyle = i % 2 ? '#ffffff' : '#e2574c'; c.fillRect(0, i * 16, cw, 16); } });
  const lh = new THREE.Mesh(new THREE.CylinderGeometry(2.2, 3.2, 26, 20), new THREE.MeshToonMaterial({ map: stripes, gradientMap: gradMap })); lh.position.set(70, 26, -120); w.group.add(lh);
  const lamp = new THREE.Mesh(new THREE.CylinderGeometry(2.6, 2.6, 3, 16), new THREE.MeshBasicMaterial({ color: new THREE.Color(2.4, 2.1, 1.2) })); lamp.position.set(70, 40.5, -120); w.group.add(lamp);
  const top = new THREE.Mesh(new THREE.ConeGeometry(3.2, 3, 16), mat(w, '#e2574c')); top.position.set(70, 43.5, -120); w.group.add(top);
  const boats = [];
  for (const [x, z] of [[30, -60], [120, -75], [-30, -90], [180, -50], [90, 70]]) {
    const b = new THREE.Group(); b.position.set(x, 0.4, z);
    const hull = new THREE.Mesh(new THREE.BoxGeometry(5, 1.2, 1.8), mat(w, '#b5673f')); b.add(hull);
    const sg = new THREE.BufferGeometry(); sg.setAttribute('position', new THREE.Float32BufferAttribute([0, 0.8, 0, 2.6, 0.8, 0, 0, 7.5, 0], 3)); sg.computeVertexNormals();
    b.add(new THREE.Mesh(sg, mat(w, '#fffaf0', { side: THREE.DoubleSide }))); w.group.add(b); boats.push(b);
  }
  const far = [], fr = rng(8);
  for (let i = 0; i < 40; i++) { const a = fr() * PI * 2, d = 500 + fr() * 350, s = 30 + fr() * 40; far.push([Math.cos(a) * d, 30 + fr() * 50, Math.sin(a) * d, s * 1.7, s * 0.55, s]); }
  puffs(w, far, '#ffffff', { rim: 0.3 });
  for (let i = 0; i < 3; i++) {
    const d = new THREE.Group(), blue = toon('#5d86b8'), wh = toon('#eef4fb');
    add(d, SPH, blue, [0, 0, 0], [1.25, 0.36, 0.36]); add(d, SPH, wh, [0.15, -0.1, 0], [0.95, 0.26, 0.3], false);
    add(d, SPH, blue, [1.25, -0.03, 0], [0.36, 0.13, 0.13]);
    add(d, CONE, blue, [-0.1, 0.42, 0], [0.16, 0.4, 0.06]).rotation.z = 0.5;
    add(d, CONE, blue, [-1.3, 0, 0.22], [0.1, 0.5, 0.2]).rotation.set(PI / 2, 0, 0.9); add(d, CONE, blue, [-1.3, 0, -0.22], [0.1, 0.5, 0.2]).rotation.set(-PI / 2, 0, 0.9);
    add(d, CONE, blue, [0.35, -0.2, 0.34], [0.09, 0.4, 0.14]).rotation.set(2.2, 0, 0.4); add(d, CONE, blue, [0.35, -0.2, -0.34], [0.09, 0.4, 0.14]).rotation.set(-2.2, 0, 0.4);
    add(d, SPH, toon('#1c2430'), [0.95, 0.09, 0.27], [0.04, 0.05, 0.03], false); add(d, SPH, toon('#1c2430'), [0.95, 0.09, -0.27], [0.04, 0.05, 0.03], false);
    d.scale.setScalar(1.35); w.group.add(d); dolphins.push(d);
  }
  spray = new Particles(w.group, 420, { tint: [1, 1, 1] });
  w.update = (t, tl, v) => {
    boats.forEach((b, i) => { b.position.y = 0.5 + Math.sin(t * 1.2 + i) * 0.35; b.rotation.z = Math.sin(t * 0.9 + i * 2) * 0.08; });
    dolphins.forEach(d => { d.visible = false; });
    let n = 0;
    for (const j of DOLPHINS) {
      const k = (t - j.t) / JUMP, d = dolphins[j.d], behind = j.t >= 42;
      const x0 = v.x + (behind ? -6.5 : 2.5) + (j.d - 1) * 1.6, z0 = v.z + j.side * (4.2 + j.d * 1.3);
      if (k > 0 && k < 1) {
        d.visible = true; d.position.set(x0 + (k - 0.5) * 7, -0.9 + Math.sin(PI * k) * 3.9, z0);
        d.rotation.set(0, 0, Math.atan2(PI * 3.9 * Math.cos(PI * k), 7));
      }
      for (const [kk, big] of [[0, 0.7], [1, 1]]) {
        const a = t - (j.t + kk * JUMP), r = rng(j.t * 10 + kk * 3 + 1);
        for (let i = 0; i < 30; i++) {
          const ang = r() * PI * 2, sp = (1 + r() * 2.5) * big, up = (3 + r() * 4.5) * big;
          if (a < 0 || a > 1.1 || n >= spray.n) continue;
          spray.set(n++, x0 + (kk - 0.5) * 7 + Math.cos(ang) * sp * a, Math.max(-0.5, 0.2 + up * a - 6.5 * a * a), z0 + Math.sin(ang) * sp * a, 0.22 * (1 - a * 0.6), 0.95);
        }
      }
    }
    // Spray under the window as it skims the water.
    const r = rng(4);
    for (let i = 0; i < 60 && n < spray.n; i++) { const a = (t * 1.6 + r()) % 1; spray.set(n++, v.x - 1 - a * 6 - r() * 2, 0.3 + Math.sin(a * PI) * (0.5 + r() * 0.9), v.z + (r() - 0.5) * 3.2, 0.16 * (1 - a), 0.8); }
    for (; n < spray.n; n++) spray.set(n, 0, -50, 0, 0, 0);
    spray.done();
  };
}

// ------------------------------------------------------------------------------------------------ world: forest
const fz = x => Math.sin(x / 22) * 9;
{
  const w = makeWorld('forest', { sun: [0.3, 0.9, 0.2], fog: '#0b2f33', den: 0.013, shade: '#1a3a52', lit: '#b9e6d8', rim: '#7fffe0',
    sky: { top: '#0a2a2c', mid: '#0f3836', bot: '#0b2f33' }, hemi: ['#7fe0d0', '#10302c', 1.0], sunL: ['#bfffe6', 0.8], bloom: 0.75, lantern: 1.3 });
  const g1 = new THREE.Color('#1d4a3a'), g2 = new THREE.Color('#2f6a45');
  terrain(w, 520, 90, (x, z) => (fbm(x / 30, z / 30) - 0.5) * 4, (x, z, y, c) => c.lerpColors(g1, g2, fbm(x / 12 + 4, z / 12)), { cx: 20 });
  const r = rng(51), trunks = [], tcol = [], bases = [];
  for (let i = 0; i < 300; i++) {
    const x = -90 + r() * 240, z = (r() * 2 - 1) * 85; if (Math.abs(z - fz(x)) < 10) continue;
    const rad = 1.2 + r() * 3.2; trunks.push([x, 73, z, rad, 150, rad, 0, r() * 6, 0]); tcol.push(['#8a6a54', '#7a5c4c', '#9a7a5e', '#6f5648'][Math.floor(r() * 4)]); bases.push([x, z, rad]);
  }
  instances(w.group, new THREE.CylinderGeometry(0.72, 1.25, 1, 16, 1), mat(w, '#b89a80', { rim: 0.22 }), trunks);
  const canopy = [], bush = [];
  for (let i = 0; i < 280; i++) { const x = -110 + r() * 280, z = (r() * 2 - 1) * 110, s = 16 + r() * 18; canopy.push([x, 58 + r() * 34, z, s, s * 0.6, s]); }
  for (let i = 0; i < 340; i++) { const x = -90 + r() * 240, z = (r() * 2 - 1) * 85; if (Math.abs(z - fz(x)) < 4) continue; const s = 1.5 + r() * 3; bush.push([x, 0.3, z, s, s * 0.6, s]); }
  puffs(w, canopy, '#1b5a50', { rim: 0.1 }); puffs(w, bush, '#2c7a55', { rim: 0.3 });
  const shr = [], sc = [], pal = [[0.5, 2.2, 2.0], [2.2, 0.7, 1.7], [0.9, 2.3, 1.0]];
  for (let i = 0; i < 300; i++) { const [bx, bz, rad] = bases[Math.floor(r() * bases.length)], a = r() * PI * 2, d = rad + 0.6 + r() * 3, s = 0.25 + r() * 0.55; shr.push([bx + Math.cos(a) * d, 0.5 + s * 0.4, bz + Math.sin(a) * d, s, s * 0.6, s]); sc.push(i % 3); }
  const mush = instances(w.group, PUFF, new THREE.MeshBasicMaterial({ fog: true }), shr); const cc = new THREE.Color();
  sc.forEach((k, i) => mush.setColorAt(i, cc.setRGB(...pal[k])));
  for (let i = 0; i < 22; i++) {
    const m = new THREE.Mesh(new THREE.PlaneGeometry(4 + r() * 5, 95), new THREE.MeshBasicMaterial({ map: rayTex, color: '#9fffe0', transparent: true, opacity: 0.035 + r() * 0.04, depthWrite: false, blending: THREE.AdditiveBlending, side: THREE.DoubleSide, fog: false }));
    const x = -70 + r() * 200; m.position.set(x, 44, fz(x) + (r() - 0.5) * 50); m.rotation.set(0, r() * PI, 0.22); w.group.add(m);
  }
  const flies = new Particles(w.group, 900, { additive: true, tint: [2.4, 1.9, 0.6] }), fr = rng(61), fb = [];
  for (let i = 0; i < flies.n; i++) { const x = -90 + fr() * 240; fb.push([x, 1 + fr() * 16, fz(x) + (fr() - 0.5) * 60, fr() * 6.3, 0.5 + fr()]); }
  w.update = t => { fb.forEach(([x, y, z, ph, sp], i) => flies.set(i, x + Math.sin(t * 0.5 * sp + ph) * 2.5, y + Math.sin(t * 0.8 * sp + ph * 2) * 1.5, z + Math.cos(t * 0.45 * sp + ph) * 2.5, 0.34, 0.25 + 0.75 * Math.max(0, Math.sin(t * 1.6 * sp + ph * 3)))); flies.done(); };
}

// ------------------------------------------------------------------------------------------------ world: storm
const bolts = [];
let rain;
{
  const w = makeWorld('storm', { sun: [0.2, 0.9, 0.3], fog: '#1c2036', den: 0.0085, shade: '#141830', lit: '#666c98', rim: '#9aa4ff',
    sky: { top: '#0b0d1c', mid: '#161a30', bot: '#1c2036' }, hemi: ['#8f98d0', '#1a1c30', 1.1], sunL: ['#aab4ff', 0.9], bloom: 0.7, lantern: 1.1 });
  const r = rng(71), items = [];
  for (let i = 0; i < 900; i++) {
    const x = -140 + r() * 320, z = -150 + r() * 260, k = r(), s = 14 + r() * 22;
    const y = k < 0.42 ? -34 + r() * 14 : k < 0.84 ? 44 + r() * 26 : -10 + r() * 50;
    if (k >= 0.84 && z > -46) continue;
    items.push([x, y, z, s * 1.3, s * 0.7, s]);
  }
  puffs(w, items, '#ffffff', { rim: 0.2 });
  const N = 2600; rain = { n: N, geo: new THREE.BufferGeometry(), pos: new Float32Array(N * 6) };
  rain.geo.setAttribute('position', new THREE.BufferAttribute(rain.pos, 3));
  const lines = new THREE.LineSegments(rain.geo, new THREE.LineBasicMaterial({ color: '#b9c4ff', transparent: true, opacity: 0.55, fog: false })); lines.frustumCulled = false; w.group.add(lines);
  LIGHTNING.forEach((lt, i) => {
    const A = lt < 66, vx = A ? -22 + 8 * (lt - 60) : 26 + 8 * (lt - 66), br = rng(lt * 7);
    const bx = A ? vx + (i % 2 ? 26 : -22) : vx - 70, bz = A ? -52 : 26 + br() * 10, pts = [];
    let x = bx, z = bz;
    for (let k = 0; k <= 14; k++) { pts.push(new THREE.Vector3(x, 62 - k * 6.6, z)); x += (br() - 0.5) * 11; z += (br() - 0.5) * 4; }
    const m = new THREE.Mesh(new THREE.TubeGeometry(new THREE.CatmullRomCurve3(pts, false, 'catmullrom', 0.05), 80, 0.42, 5), new THREE.MeshBasicMaterial({ color: new THREE.Color(7, 7.5, 11), fog: false }));
    const fork = pts.slice(5, 11).map((p, k) => new THREE.Vector3(p.x + k * 3.2, p.y - k * 1.5, p.z));
    m.add(new THREE.Mesh(new THREE.TubeGeometry(new THREE.CatmullRomCurve3(fork, false, 'catmullrom', 0.05), 40, 0.22, 4), m.material));
    m.visible = false; w.group.add(m); bolts.push(m);
  });
  w.update = t => {
    const rr = rng(81), c = camera.position;
    for (let i = 0; i < N; i++) {
      const x = c.x + (rr() - 0.5) * 70, z = c.z + (rr() - 0.5) * 70, sp = 46 + rr() * 22, y = c.y + 22 - ((t * sp + rr() * 44) % 44);
      rain.pos.set([x, y, z, x - 0.45, y - 2.1, z + 0.1], i * 6);
    }
    rain.geo.attributes.position.needsUpdate = true;
    let f = 0;
    LIGHTNING.forEach((lt, i) => { const a = t - lt; bolts[i].visible = a >= 0 && a < 0.2 && Math.floor(a * 30) % 3 !== 1; if (a >= 0) f = Math.max(f, Math.exp(-a * 6) * (0.75 + 0.25 * Math.sin(a * 60))); });
    G.flash.value = f;
  };
}

// ------------------------------------------------------------------------------------------------ world: snow
{
  const w = makeWorld('snow', { sun: [-0.3, 0.75, -0.5], fog: '#0b2146', den: 0.0021, shade: '#27488c', lit: '#b4cdea', rim: '#6dffc8',
    sky: { top: '#030818', mid: '#0a1f4a', bot: '#16407a', stars: 1, band: 0.5 }, hemi: ['#8fe0d0', '#2a4a8a', 1.4], sunL: ['#bfe0ff', 1.3], bloom: 0.5, lantern: 1.0 });
  const SH = (x, z) => { const m = smooth(range(Math.abs(z), 34, 160)), n = fbm(x / 170 + 11, z / 170 + 3), ridge = 1 - Math.abs(2 * n - 1); return m * (16 + ridge * ridge * 190 * (0.45 + fbm(x / 60, z / 60 + 9))) + (fbm(x / 22, z / 22) - 0.5) * 3 * m; };
  const s1 = new THREE.Color('#f4fbff'), s2 = new THREE.Color('#9fb6d8');
  terrain(w, 900, 230, SH, (x, z, y, c) => c.lerpColors(s1, s2, smooth(range(fbm(x / 18 + 3, z / 18), 0.5, 0.8)) * smooth(range(y, 20, 120))), { cx: 40, rim: 0.3 });
  const lake = new THREE.Mesh(new THREE.PlaneGeometry(1000, 76).rotateX(-PI / 2), new THREE.ShaderMaterial({
    uniforms: { ...w.U },
    vertexShader: 'varying vec3 vW; void main(){ vec4 w = modelMatrix * vec4(position, 1.0); vW = w.xyz; gl_Position = projectionMatrix * viewMatrix * w; }',
    fragmentShader: `uniform vec3 fogCol; uniform float fogDen, time; varying vec3 vW; ${NOISE}
      void main(){ float b = fbm(vec2(vW.x * 0.03 + time * 0.05, vW.z * 0.25)); vec3 c = mix(vec3(0.02, 0.07, 0.2), vec3(0.08, 0.75, 0.55), pow(max(b, 0.0), 2.2) * 0.9);
        c += vec3(0.5, 0.25, 0.8) * pow(max(fbm(vec2(vW.x * 0.02 - time * 0.04, vW.z * 0.2 + 7.0)), 0.0), 3.0) * 0.6;
        c += vec3(0.7, 0.9, 1.0) * step(0.985, hash(floor(vW.xz * 1.5))) * 0.6;
        float dist = length(cameraPosition - vW); gl_FragColor = vec4(mix(c, fogCol, 1.0 - exp(-dist * dist * fogDen * fogDen)), 1.0); }`,
  })); lake.position.set(40, 0.6, 0); w.group.add(lake);
  const r = rng(91), pines = [], caps = [];
  for (let i = 0; i < 700; i++) {
    const x = -230 + r() * 520, z = (r() < 0.5 ? -1 : 1) * (37 + r() * 95), y = SH(x, z); if (y > 62) continue;
    const s = 3 + r() * 4; pines.push([x, y + s * 1.3, z, s * 0.62, s * 2.8, s * 0.62]); caps.push([x, y + s * 2.12, z, s * 0.31, s * 1.3, s * 0.31]);
  }
  instances(w.group, new THREE.ConeGeometry(1, 1, 10), mat(w, '#1f5a52', { rim: 0.2 }), pines); instances(w.group, new THREE.ConeGeometry(1, 1, 10), mat(w, '#d6e6f6', { rim: 0.2 }), caps);
  const ribbons = [];
  for (let i = 0; i < 6; i++) {
    const g = new THREE.PlaneGeometry(1300, 120, 200, 1), p = g.attributes.position;
    for (let k = 0; k < p.count; k++) { const x = p.getX(k); p.setZ(k, Math.sin(x / 130 + i * 1.7) * 48 + Math.sin(x / 47 + i * 3.1) * 15); }
    const m = new THREE.Mesh(g, new THREE.ShaderMaterial({
      transparent: true, depthWrite: false, side: THREE.DoubleSide, blending: THREE.AdditiveBlending, uniforms: { time: G.time, seed: { value: i * 7.3 }, amp: { value: 1 } },
      vertexShader: 'varying vec2 vUv; void main(){ vUv = uv; gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 1.0); }',
      fragmentShader: `uniform float time, seed, amp; varying vec2 vUv; ${NOISE}
        void main(){ float rays = fbm(vec2(vUv.x * 70.0 + time * 0.35 + seed, time * 0.12 + seed)); rays = rays * rays * 2.6 * (0.5 + fbm(vec2(vUv.x * 6.0 - time * 0.08, seed)));
          float prof = smoothstep(0.0, 0.1, vUv.y) * pow(clamp(1.0 - vUv.y, 0.0, 1.0), 1.5) * smoothstep(0.0, 0.08, vUv.x) * smoothstep(1.0, 0.92, vUv.x);
          vec3 c = mix(vec3(0.12, 1.0, 0.58), vec3(0.75, 0.3, 1.0), smoothstep(0.15, 0.85, vUv.y)); gl_FragColor = vec4(c * rays * prof * 0.75 * amp, 1.0); }`,
    }));
    m.position.set(40, 165 + i * 12, -210 + i * 52); w.group.add(m); ribbons.push(m);
  }
  const snow = new Particles(w.group, 1500, { tint: [1, 1, 1] });
  w.update = (t, tl, v) => {
    const sr = rng(95), c = camera.position;
    for (let i = 0; i < snow.n; i++) { const sp = 1.2 + sr() * 1.6, ph = sr() * 6.3; snow.set(i, c.x + (sr() - 0.5) * 60 + Math.sin(t * 0.7 + ph) * 0.8, c.y + 15 - ((t * sp + sr() * 30) % 30), c.z + (sr() - 0.5) * 60, 0.1 + sr() * 0.12, 0.9); }
    snow.done();
  };
}

// ------------------------------------------------------------------------------------------------ world: moon
const MOON = new THREE.Vector3(-0.75, 0.22, 0.6).normalize();
const shooting = [];
{
  const w = makeWorld('moon', { sun: [-0.75, 0.3, 0.6], fog: '#0c1233', den: 0.0034, shade: '#2a3674', lit: '#aab6ea', rim: '#c9d4ff',
    sky: { top: '#02030d', mid: '#0a1030', bot: '#1c2c66', stars: 1.4, band: 1.2 }, hemi: ['#aab8ff', '#1a2050', 1.3], sunL: ['#dfe6ff', 1.5], bloom: 0.45, lantern: 1.2 });
  cloudSea(w, 33, 2000, -46, 520, '#ffffff');
  const tex = canvasTex(1024, 512, (g, cw, ch) => {
    g.fillStyle = '#f6f0dc'; g.fillRect(0, 0, cw, ch); const r = rng(13);
    for (let i = 0; i < 46; i++) { const x = r() * cw, y = r() * ch, rad = 8 + r() * 46; const gr = g.createRadialGradient(x, y, rad * 0.2, x, y, rad); gr.addColorStop(0, 'rgba(196,186,160,0.75)'); gr.addColorStop(0.8, 'rgba(206,196,172,0.5)'); gr.addColorStop(1, 'rgba(246,240,220,0)'); g.fillStyle = gr; g.beginPath(); g.arc(x, y, rad, 0, PI * 2); g.fill(); }
  });
  const moon = new THREE.Mesh(new THREE.SphereGeometry(120, 48, 32), new THREE.MeshBasicMaterial({ map: tex, color: new THREE.Color(0.8, 0.78, 0.72), fog: false }));
  moon.position.copy(MOON).multiplyScalar(640).add(new THREE.Vector3(1, 30, 0)); moon.rotation.y = 2.2; w.group.add(moon);
  const halo = glow(w.group, '#9fb4ff', 700, moon.position.x * 1.25, moon.position.y * 1.25, moon.position.z * 1.25, 0.3);
  const st = canvasTex(256, 16, (g, cw, ch) => { const gr = g.createLinearGradient(0, 0, cw, 0); gr.addColorStop(0, 'rgba(255,255,255,0)'); gr.addColorStop(0.85, 'rgba(255,255,255,0.9)'); gr.addColorStop(1, 'rgba(255,255,255,1)'); g.fillStyle = gr; g.fillRect(0, 5, cw, 6); });
  SHOOTING.forEach((ts, i) => {
    const m = new THREE.Mesh(new THREE.PlaneGeometry(70, 3), new THREE.MeshBasicMaterial({ map: st, color: new THREE.Color(2, 2.2, 3), transparent: true, depthWrite: false, blending: THREE.AdditiveBlending, fog: false }));
    m.userData = { ts, i }; m.visible = false; w.group.add(m); shooting.push(m);
  });
  const right = new THREE.Vector3(), up = new THREE.Vector3(), fwd = new THREE.Vector3();
  w.update = t => {
    halo.material.opacity = 0.3 + Math.sin(t * 0.8) * 0.03;
    camera.matrixWorld.extractBasis(right, up, fwd);
    shooting.forEach(m => {
      const k = (t - m.userData.ts) / 0.9, i = m.userData.i; m.visible = k > 0 && k < 1; if (!m.visible) return;
      const a = -0.45 - (i % 3) * 0.12, sx = (i % 2 ? -1 : 1) * (120 + i * 30), d = (k - 0.5) * 300;
      m.position.copy(camera.position).addScaledVector(fwd, -700).addScaledVector(right, sx + Math.cos(a) * d).addScaledVector(up, 190 - i * 14 + Math.sin(a) * d);
      m.quaternion.copy(camera.quaternion); m.rotateZ(a); m.material.opacity = Math.sin(PI * k);
    });
  };
}

// ------------------------------------------------------------------------------------------------ direction
const fly = (t, x, y, z = 0) => ({ x, y: y + Math.sin(t * 1.3) * 0.3, z, yaw: PI / 2, pitch: Math.sin(t * 1.1) * 0.03, roll: Math.sin(t * 0.9) * 0.07, s: 1 });
const chase = (v, ox, oy, oz, ly = 0.75) => { const c = Math.cos(v.yaw), s = Math.sin(v.yaw); return { p: [v.x + ox * c + oz * s, v.y + oy, v.z - ox * s + oz * c], l: [v.x, v.y + ly, v.z] }; };
const vp = v => [v.x, v.y, v.z];
const INSCREEN = { x: 0.95, y: 1.42, z: 0.12, yaw: 0.55, pitch: 0, roll: 0, s: 0.13 };
const SUN_C = new THREE.Vector3(0.9, 0.13, -0.4).normalize();
const DIRECT = {
  roomA: (tl, t) => ({ v: { ...INSCREEN, y: INSCREEN.y + Math.sin(t * 1.5) * 0.02 }, c: { p: lerp3([-6.5, 3.6, 14.5], [-2.2, 2.9, 9.8], ease(tl / 6)), l: [0.2, 2.2, 0], fov: 36 }, wind: 0 }),
  roomB: (tl, t) => {
    const k1 = ease(range(tl, 0.5, 2.9)), k2 = Math.pow(range(tl, 2.9, 6), 2.2);
    const p = lerp3([INSCREEN.x, INSCREEN.y, INSCREEN.z], [0.4, 2.7, 4.4], k1);
    const v = { x: p[0] + k2 * 9, y: p[1] + Math.sin(t * 2) * 0.05 * k1 + k2 * 3.2, z: p[2] + k2 * 9, yaw: lerp(0.55, 0.95, k1) + k2 * 0.4, pitch: -k2 * 0.25, roll: -k2 * 0.3, s: lerp(0.13, 0.62, k1) + k2 * 0.5 };
    return { v, c: { p: lerp3([-2.2, 2.9, 9.8], [-3.0, 3.1, 10.8], ease(tl / 6)), l: lerp3([0.2, 2.2, 0], vp(v), 0.55 * k1 * (1 - k2 * 0.5)), fov: 36 }, wind: k1 * 0.3 + k2 * 0.7, happy: tl > 3.2 };
  },
  cloudsA: (tl, t) => { const v = fly(t, -25 + 9 * tl, 16), mid = [2, 16, 0]; return { v, c: { p: [mid[0] - SUN_C.x * 62 - 8, 13.5 + tl * 0.25, mid[2] - SUN_C.z * 62 + 16], l: lerp3(mid, vp(v), 0.6), fov: 31 } }; },
  cloudsB: (tl, t) => { const v = fly(t, 29 + 9 * tl, 16); return { v, c: { ...chase(v, 2.4 + Math.sin(tl * 0.5) * 0.8, 1.15, 5.4 - tl * 0.22), fov: 36 }, dogLook: 0.35 }; },
  hillsA: (tl, t) => { const x = -30 + 10 * tl, v = fly(t, x, hillY(x)); return { v, c: { p: [42, HH(42, 24) + 7 - tl * 0.3, 24], l: vp(v), fov: 37 } }; },
  hillsB: (tl, t) => { const x = 30 + 10 * tl, v = fly(t, x, hillY(x)); return { v, c: { ...chase(v, -4.4, 0.9 + tl * 0.06, 2.2 + tl * 0.3), fov: 35 }, dogLook: -0.5, catLook: -0.4 }; },
  seaA: (tl, t) => { const v = fly(t, -40 + 14 * tl, 2.9); return { v, c: { p: [8 + tl * 0.8, 1.5, 30], l: lerp3([2, 2.6, 0], vp(v), 0.7), fov: 36 }, dogLook: -0.7 }; },
  seaB: (tl, t) => { const v = fly(t, 44 + 14 * tl, 2.9); return { v, c: { ...chase(v, 3.2 - tl * 0.12, 1.0, 4.8), fov: 37 }, dogLook: 0.5 * Math.sin(tl), catScared: 0.3 }; },
  forestA: (tl, t) => { const x = -28 + 8 * tl, v = fly(t, x, 6, fz(x)); v.yaw = Math.atan2(1, Math.cos(x / 22) * 9 / 22); return { v, c: { p: [34 - tl * 0.4, 4.6 + tl * 0.15, fz(34) + 4.5], l: vp(v), fov: 40 } }; },
  forestB: (tl, t) => { const x = 20 + 8 * tl, v = fly(t, x, 6, fz(x)); v.yaw = Math.atan2(1, Math.cos(x / 22) * 9 / 22); return { v, c: { ...chase(v, 2.2 + Math.sin(tl * 0.6) * 0.5, 0.85, 4.9), fov: 37 }, catLook: Math.sin(t * 1.7) * 0.6, catNod: -0.2 + Math.sin(t * 2.3) * 0.12, dogLook: Math.sin(t * 0.9) * 0.4 }; },
  stormA: (tl, t) => { const v = fly(t, -22 + 8 * tl, 10); v.y += Math.sin(t * 5.2) * 0.5; v.roll = Math.sin(t * 2.6) * 0.28; v.pitch = Math.sin(t * 3.4) * 0.12; return { v, c: { p: [4 + Math.sin(t * 9) * 0.15, 4 + Math.sin(t * 11) * 0.12, 62], l: lerp3([2, 10, 0], vp(v), 0.5), fov: 33 }, wind: 1, catScared: 1 }; },
  stormB: (tl, t) => { const v = fly(t, 26 + 8 * tl, 10); v.y += Math.sin(t * 5.2) * 0.5; v.roll = Math.sin(t * 2.6) * 0.28; v.pitch = Math.sin(t * 3.4) * 0.12; const c = chase(v, 2.6 + Math.sin(tl) * 0.5, 1.0, 4.7); c.p[0] += Math.sin(t * 13) * 0.06; c.p[1] += Math.sin(t * 17) * 0.06; return { v, c: { ...c, fov: 38 }, wind: 1, catScared: 1, shake: 0.25, dogLook: 0.5 }; },
  snowA: (tl, t) => { const v = fly(t, -35 + 10 * tl, 12); return { v, c: { p: [20 + tl * 0.6, 5.5, 33], l: lerp3([0, 24, -10], vp(v), 0.6), fov: 46 }, nod: -0.3 }; },
  snowB: (tl, t) => { const v = fly(t, 25 + 10 * tl, 12); return { v, c: { ...chase(v, 2.3 + tl * 0.1, -0.55, 5.3, 1.05), fov: 38 }, nod: -0.38, dogLook: -0.3, catLook: 0.25, wind: 0.45 }; },
  moonA: (tl, t) => { const v = fly(t, -20 + 7 * tl, 30), mid = [1, 30, 0]; return { v, c: { p: [mid[0] - MOON.x * 50, mid[1] - MOON.y * 50 + 1, mid[2] - MOON.z * 50], l: lerp3(mid, vp(v), 0.6), fov: 29 }, wind: 0.35, catSleep: 1 }; },
  moonB: (tl, t) => { const v = fly(t, 22 + 7 * tl, 30); return { v, c: { ...chase(v, 2.5 - tl * 0.12, 0.75, 5.1), fov: 36 }, wind: 0.3, catSleep: 1, nod: -0.22, dogLook: 0.55 }; },
  home: (tl, t) => {
    const k = ease(range(tl, 0, 5.0)), C = [CABIN.x, CABIN.y, CABIN.z];
    const p = lerp3([C[0] - 36, C[1] + 10, C[2] + 20], [C[0] - 5.5, C[1] + 0.9, C[2] + 6.5], k);
    const v = { x: p[0], y: p[1] + Math.sin(t * 1.3) * 0.25 * (1 - k), z: p[2], yaw: lerp(PI / 2 - 0.2, 0.25, k), pitch: (1 - k) * 0.12 * Math.sin(k * PI), roll: Math.sin(k * PI) * -0.2, s: 1 };
    return { v, c: { p: [C[0] - 13 + tl * 0.3, C[1] + 2.0, C[2] + 21], l: lerp3(vp(v), [C[0], C[1] + 2.6, C[2]], 0.25 + 0.2 * k), fov: 44 }, wind: 0.6 * (1 - k), happy: tl > 5, catSleep: 0 };
  },
  roomEnd: (tl, t) => {
    const k = ease(range(tl, 0.1, 2.5)), p = lerp3([8.5, 5.2, 10], [INSCREEN.x, INSCREEN.y, INSCREEN.z], k);
    const v = { x: p[0], y: p[1], z: p[2], yaw: lerp(-1.1, INSCREEN.yaw, k), pitch: 0, roll: (1 - k) * 0.3, s: lerp(1.0, 0.13, k) };
    return { v, c: { p: lerp3([-1.4, 2.7, 8.6], [-4.8, 3.4, 13.4], ease(tl / 6)), l: [0.2, 2.2, 0], fov: 36 }, wind: (1 - k) * 0.6, happy: tl > 2.6 };
  },
};

// ------------------------------------------------------------------------------------------------ DOM layer
const ui = document.getElementById('ui');
const el = (cls, html = '') => { const d = document.createElement('div'); d.className = cls; d.innerHTML = html; ui.appendChild(d); return d; };
const TAG = LANG === 'ko' ? '우리 펫 가족이 바탕화면에 살아요' : 'Your pet family, living on your desktop';
const title = el('title', `<div class="name">PetFam</div><div class="sub">${LANG === 'ko' ? '창문 여행' : 'The Window Ride'}</div>`);
const scrim = el('scrim');
const end = el('end', `<img src="icon.png" alt=""><div class="name">PetFam</div><div class="sub">${TAG}</div>`);
const flashEl = el('flash'), fadeEl = el('fade');
const show = (e, a, tr = '') => { e.style.opacity = a.toFixed(3); e.style.visibility = a > 0.001 ? 'visible' : 'hidden'; e.style.transform = tr; };
function updateDom(t) {
  show(title, smooth(range(t, 1.0, 2.0)) * (1 - smooth(range(t, 4.6, 5.5))), `translate(-50%, ${(1 - ease(range(t, 1.0, 2.2))) * 26}px)`);
  const e = smooth(range(t, 104.0, 105.0));
  show(scrim, e); show(end, e, `translate(-50%, ${(1 - ease(range(t, 104.0, 105.2))) * 28}px)`);
  let f = 0; for (const ft of FLASHES) f = Math.max(f, 1 - Math.abs(t - ft) / (t < ft ? 0.35 : 0.5));
  flashEl.style.opacity = clamp(f).toFixed(3);
  flashEl.style.background = t > 59 && t < 61 ? '#10121f' : '#fff8ea';
  fadeEl.style.opacity = Math.max(1 - range(t, 0, 0.8), range(t, 107.0, 108)).toFixed(3);
}

// ------------------------------------------------------------------------------------------------ frame
const look = new THREE.Vector3(), billboards = [];
scene.traverse(o => { if (o.userData.billboard) billboards.push(o); });
function renderAt(t) {
  t = clamp(t, 0, DURATION - 1e-4);
  G.time.value = t; G.flash.value = 0;
  let shot = SHOTS[0]; for (const s of SHOTS) if (t >= s.t0) shot = s;
  const tl = t - shot.t0, w = worlds[shot.world], d = DIRECT[shot.id](tl, t), v = d.v;
  for (const k in worlds) worlds[k].group.visible = worlds[k] === w;

  camera.fov = d.c.fov; camera.updateProjectionMatrix();
  camera.position.set(...d.c.p); camera.lookAt(look.set(...d.c.l)); camera.updateMatrixWorld();
  G.pscale.value = H / (2 * Math.tan(THREE.MathUtils.degToRad(camera.fov / 2)));

  veh.position.set(v.x, v.y, v.z); veh.rotation.set(v.pitch, v.yaw, v.roll, 'YXZ'); veh.scale.setScalar(v.s);
  const wind = d.wind ?? 0.7, sleep = d.catSleep ?? 0, scared = d.catScared ?? 0;
  pose(dog, { x: -0.55, y: 0.06, z: 0.42, yaw: 0.12, sit: 1, wind, look: (d.dogLook ?? 0) + Math.sin(t * 0.7) * 0.15, nod: d.nod ?? 0, happy: d.happy, wag: 0.8, shake: d.shake }, t);
  pose(cat, { x: 0.62, y: 0.06, z: -0.2, yaw: sleep ? -0.9 : -0.15, sit: 1 - sleep, lie: Math.max(sleep, scared * 0.75), wind: wind * 0.5, scared, look: (d.catLook ?? 0) + Math.sin(t * 0.5 + 2) * 0.12,
    nod: (d.catNod ?? d.nod ?? 0) * (1 - sleep), happy: sleep > 0.5 || (d.happy && !scared), wag: 0.3, shake: (d.shake ?? 0) * 1.6 }, t);
  const tr = rng(3);
  for (let i = 0; i < trail.n; i++) { const a = (t * 0.7 + tr()) % 1, x = (tr() - 0.5) * 2.6, y = (tr() - 0.5) * 0.3; trail.set(i, x * (1 - a * 0.4), -0.1 + y - a * 0.5 + Math.sin(a * 9 + i) * 0.08, -1.3 - a * (3 + wind * 5), 0.11 * (1 - a), (1 - a) * (0.35 + 0.65 * wind)); }
  trail.done();

  w.update(t, tl, v);
  const env = w.env;
  scene.fog.color.copy(w.U.fogCol.value); scene.fog.density = w.U.fogDen.value;
  hemi.color.set(env.hemi[0]); hemi.groundColor.set(env.hemi[1]); hemi.intensity = env.hemi[2] * 0.55 + G.flash.value * 2.5;
  sunL.color.set(env.sunL[0]); sunL.intensity = env.sunL[1] * 0.5; sunL.position.copy(w.U.sunDir.value).multiplyScalar(50).add(veh.position); sunL.target = veh;
  lanternL.intensity = env.lantern; veh.userData.halo.material.opacity = 0.1 + env.lantern * 0.22; veh.userData.under.material.opacity = 0.2 + env.lantern * 0.3;
  bloom.strength = env.bloom;
  w.sky.position.copy(camera.position);
  scene.updateMatrixWorld();
  for (const b of billboards) { b.parent.getWorldQuaternion(Q).invert(); b.quaternion.copy(camera.quaternion).premultiply(Q); }

  updateDom(t);
  composer.render();
}

// ------------------------------------------------------------------------------------------------ start
async function ready() {
  const text = ui.textContent;
  try { await Promise.race([Promise.all(['700 40px "Gowun Batang"', '700 40px Fredoka', '600 40px Fraunces', 'italic 500 40px Fraunces'].map(f => document.fonts.load(f, text))), new Promise(r => setTimeout(r, 15000))]); } catch { /* fall back */ }
  await Promise.all([...document.images].map(i => i.decode().catch(() => {})));
}
window.renderAt = renderAt;
window.DURATION = DURATION;
window.__ready = ready().then(() => { renderAt(Number(qs.get('t') ?? 0)); return true; });
function fit() {
  const s = Math.min(innerWidth / W, innerHeight / H);
  document.getElementById('stage').style.transform = `translate(${(innerWidth - W * s) / 2}px, ${(innerHeight - H * s) / 2}px) scale(${s})`;
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
