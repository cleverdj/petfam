// ---- small maths, noise and sculpting helpers shared by everything -------------------------------------------------
const PI = Math.PI, TAU = PI * 2;
const clamp = (x, a = 0, b = 1) => Math.min(b, Math.max(a, x));
const lerp = (a, b, k) => a + (b - a) * k;
const range = (t, a, b) => clamp((t - a) / (b - a));
const smooth = k => { k = clamp(k); return k * k * (3 - 2 * k); };
const ease = k => { k = clamp(k); return k < 0.5 ? 4 * k * k * k : 1 - Math.pow(-2 * k + 2, 3) / 2; };
const backOut = k => { k = clamp(k); const c1 = 1.7, c3 = c1 + 1; return 1 + c3 * Math.pow(k - 1, 3) + c1 * Math.pow(k - 1, 2); };
const bell = (t, a, b, f = 0.15) => smooth(range(t, a, a + f)) * (1 - smooth(range(t, b - f, b)));
function rng(seed) { let s = (seed * 2654435761) >>> 0 || 1; return () => { s ^= s << 13; s >>>= 0; s ^= s >>> 17; s ^= s << 5; s >>>= 0; return s / 4294967296; }; }

// Value noise in 2 and 3 dimensions, and fbm.
const PERM = (() => { const r = rng(1234), p = new Float32Array(4096); for (let i = 0; i < 4096; i++) p[i] = r(); return p; })();
const h3 = (x, y, z) => PERM[(x * 73856093 ^ y * 19349663 ^ z * 83492791) & 4095];
function noise3(x, y, z) {
  const xi = Math.floor(x), yi = Math.floor(y), zi = Math.floor(z); let fx = x - xi, fy = y - yi, fz = z - zi;
  fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy); fz = fz * fz * (3 - 2 * fz);
  const a = lerp(lerp(h3(xi, yi, zi), h3(xi + 1, yi, zi), fx), lerp(h3(xi, yi + 1, zi), h3(xi + 1, yi + 1, zi), fx), fy);
  const b = lerp(lerp(h3(xi, yi, zi + 1), h3(xi + 1, yi, zi + 1), fx), lerp(h3(xi, yi + 1, zi + 1), h3(xi + 1, yi + 1, zi + 1), fx), fy);
  return lerp(a, b, fz);
}
const noise2 = (x, y) => noise3(x, y, 7.31);
function fbm2(x, y, oct = 4) { let v = 0, a = 0.5, n = 0; for (let i = 0; i < oct; i++) { v += a * noise2(x, y); n += a; x = x * 2.03 + 11.7; y = y * 2.03 + 3.1; a *= 0.5; } return v / n; }
function fbm3(x, y, z, oct = 4) { let v = 0, a = 0.5, n = 0; for (let i = 0; i < oct; i++) { v += a * noise3(x, y, z); n += a; x = x * 2.03 + 11.7; y = y * 2.03 + 3.1; z = z * 2.03 + 5.9; a *= 0.5; } return v / n; }

// The same noise for shaders.
const GLSL_NOISE = `
  float hash11(float n){ return fract(sin(n) * 43758.5453123); }
  float hash31(vec3 p){ p = fract(p * vec3(0.1031, 0.1030, 0.0973)); p += dot(p, p.yzx + 33.33); return fract((p.x + p.y) * p.z); }
  float vnoise(vec3 p){ vec3 i = floor(p), f = fract(p); f = f * f * (3.0 - 2.0 * f);
    return mix(mix(mix(hash31(i), hash31(i + vec3(1,0,0)), f.x), mix(hash31(i + vec3(0,1,0)), hash31(i + vec3(1,1,0)), f.x), f.y),
               mix(mix(hash31(i + vec3(0,0,1)), hash31(i + vec3(1,0,1)), f.x), mix(hash31(i + vec3(0,1,1)), hash31(i + vec3(1,1,1)), f.x), f.y), f.z); }
  float fbm(vec3 p){ float v = 0.0, a = 0.5; for (int i = 0; i < 4; i++){ v += a * vnoise(p); p = p * 2.03 + 7.1; a *= 0.5; } return v / 0.9375; }`;

// ---- signed distance sculpting -------------------------------------------------------------------------------------
// Shapes are written as distance functions and blended smoothly; sculpt() turns one into a mesh by walking outward from
// a centre along every direction of a sphere (so the shape must be visible from that centre, which limbs and heads are).
const smin = (a, b, k) => { const h = clamp(0.5 + 0.5 * (b - a) / k); return lerp(b, a, h) - k * h * (1 - h); };
const sdSphere = (x, y, z, cx, cy, cz, r) => Math.hypot(x - cx, y - cy, z - cz) - r;
function sdEllipsoid(x, y, z, cx, cy, cz, rx, ry, rz) {
  const px = (x - cx) / rx, py = (y - cy) / ry, pz = (z - cz) / rz, k0 = Math.hypot(px, py, pz);
  if (k0 < 1e-6) return -Math.min(rx, ry, rz);
  const k1 = Math.hypot(px / rx, py / ry, pz / rz);
  return k0 * (k0 - 1) / k1;
}
function sdCapsule(x, y, z, ax, ay, az, bx, by, bz, ra, rb = ra) {
  const pax = x - ax, pay = y - ay, paz = z - az, bax = bx - ax, bay = by - ay, baz = bz - az;
  const h = clamp((pax * bax + pay * bay + paz * baz) / (bax * bax + bay * bay + baz * baz));
  return Math.hypot(pax - bax * h, pay - bay * h, paz - baz * h) - lerp(ra, rb, h);
}
// sdf(x, y, z) -> distance. paint(p, n, out) fills out.color (THREE.Color) and returns the fur length factor 0..1.
function sculpt(sdf, { center = [0, 0, 0], rmax = 1, seg = 56, rings = 40, paint = null, deform = null } = {}) {
  const geo = new THREE.SphereGeometry(1, seg, rings), pos = geo.attributes.position, nor = geo.attributes.normal;
  const col = new Float32Array(pos.count * 3), fur = new Float32Array(pos.count), c = new THREE.Color(), p = new THREE.Vector3(), n = new THREE.Vector3();
  const [cx, cy, cz] = center, e = 0.004;
  for (let i = 0; i < pos.count; i++) {
    const dx = pos.getX(i), dy = pos.getY(i), dz = pos.getZ(i);
    let lo = 0, hi = rmax;
    for (let k = 0; k < 22; k++) { const m = (lo + hi) / 2; if (sdf(cx + dx * m, cy + dy * m, cz + dz * m) < 0) lo = m; else hi = m; }
    const x = cx + dx * lo, y = cy + dy * lo, z = cz + dz * lo;
    n.set(sdf(x + e, y, z) - sdf(x - e, y, z), sdf(x, y + e, z) - sdf(x, y - e, z), sdf(x, y, z + e) - sdf(x, y, z - e)).normalize();
    p.set(x, y, z);
    c.setRGB(1, 1, 1); fur[i] = paint ? paint(p, n, c) : 1;
    col[i * 3] = c.r; col[i * 3 + 1] = c.g; col[i * 3 + 2] = c.b;
    if (deform) deform(p, n);
    pos.setXYZ(i, p.x, p.y, p.z); nor.setXYZ(i, n.x, n.y, n.z);
  }
  geo.setAttribute('color', new THREE.BufferAttribute(col, 3)); geo.setAttribute('aFur', new THREE.BufferAttribute(fur, 1));
  if (deform) geo.computeVertexNormals();
  geo.computeBoundingSphere();
  return geo;
}
function canvasTex(w, h, draw, srgb = true) {
  const c = document.createElement('canvas'); c.width = w; c.height = h; draw(c.getContext('2d'), w, h);
  const t = new THREE.CanvasTexture(c); if (srgb) t.colorSpace = THREE.SRGBColorSpace; t.anisotropy = 8; return t;
}
