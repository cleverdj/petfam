// ---- things people made: the log cabin (built piece by piece), the app windows, a ball and a bowl ---------------------
const cabin = new THREE.Group(); cabin.position.set(ISLE.cabin[0], groundY(...ISLE.cabin), ISLE.cabin[1]); cabin.rotation.y = 0.72; island.add(cabin);
const cabinParts = [], cabinLit = { glass: null, bulb: null, light: null };
{
  const wood = c => weatherPatch(new THREE.MeshStandardMaterial({ color: c, roughness: 0.85 }), 0.9);
  const logSide = wood('#a8743f'), logEnd = new THREE.MeshStandardMaterial({ color: '#e0b77c', roughness: 0.9 }), dark = wood('#6b4426'), roofA = wood('#c2543c'), roofB = wood('#a94530'), stone = weatherPatch(new THREE.MeshStandardMaterial({ color: '#8f8a80', roughness: 1 }), 0.9);
  // Every piece remembers when it joins the build (0 = first, 1 = last) and where it belongs.
  const part = (mesh, order, x, y, z, rx = 0, ry = 0, rz = 0) => { mesh.position.set(x, y, z); mesh.rotation.set(rx, ry, rz); mesh.castShadow = true; mesh.receiveShadow = true; mesh.userData = { order, y, s: mesh.scale.clone() }; cabin.add(mesh); cabinParts.push(mesh); return mesh; };
  const W = 3.4, D = 2.6, LOGR = 0.125, ROWS = 8, Hwall = ROWS * LOGR * 1.82;
  const logGeo = len => new THREE.CylinderGeometry(LOGR, LOGR, len, 10).rotateZ(PI / 2);
  part(new THREE.Mesh(new THREE.BoxGeometry(W + 0.5, 0.22, D + 0.5), stone), 0, 0, 0.06, 0);
  const gx = logGeo(W + 0.45), gz = logGeo(D + 0.45);
  for (let i = 0; i < ROWS; i++) {
    const y = 0.28 + i * LOGR * 1.82, o = 0.06 + i / ROWS * 0.42;
    for (const s of [-1, 1]) {
      part(new THREE.Mesh(gx, [logSide, logEnd, logEnd]), o, 0, y, s * D / 2);
      part(new THREE.Mesh(gz, [logSide, logEnd, logEnd]), o + 0.02, s * W / 2, y + LOGR * 0.9, 0, 0, PI / 2, 0);
    }
  }
  for (let i = 0; i < 5; i++) for (const s of [-1, 1]) part(new THREE.Mesh(logGeo((W + 0.3) * (1 - (i + 0.6) / 5.6)), [logSide, logEnd, logEnd]), 0.52 + i * 0.02, 0, 0.28 + Hwall + i * LOGR * 1.82, s * D / 2);
  // Roof: two slopes of shingle rows and a ridge.
  const slope = 0.74, half = (W / 2 + 0.45) / Math.cos(slope), top = 0.28 + Hwall + 5 * LOGR * 1.82 + 0.1;
  for (const s of [-1, 1]) for (let i = 0; i < 9; i++) {
    const u = (i + 0.5) / 9 * half, m = new THREE.Mesh(new THREE.BoxGeometry(half / 9 * 1.25, 0.06, D + 0.9), i % 2 ? roofA : roofB);
    part(m, 0.66 + i * 0.016, s * u * Math.cos(slope), top - u * Math.sin(slope) + 0.02, 0, 0, 0, -s * slope - s * 0.09);
  }
  part(new THREE.Mesh(new THREE.BoxGeometry(0.22, 0.14, D + 1.0), dark), 0.82, 0, top + 0.06, 0);
  part(new THREE.Mesh(new THREE.BoxGeometry(0.5, 1.5, 0.5), stone), 0.84, -0.95, top - 0.35, -0.45);
  part(new THREE.Mesh(new THREE.BoxGeometry(0.62, 0.12, 0.62), stone), 0.86, -0.95, top + 0.42, -0.45);
  // Door, window and the things around them, on the front (+z) wall.
  const front = D / 2 + LOGR + 0.01;
  part(new THREE.Mesh(new THREE.BoxGeometry(0.9, 1.5, 0.08), dark), 0.88, 0.75, 1.02, front);
  part(new THREE.Mesh(new THREE.BoxGeometry(0.74, 1.36, 0.1), wood('#8a5a34')), 0.89, 0.75, 0.98, front + 0.02);
  part(new THREE.Mesh(new THREE.SphereGeometry(0.05, 12, 8), new THREE.MeshStandardMaterial({ color: '#f2c14e', roughness: 0.3, metalness: 0.8 })), 0.9, 1.0, 0.98, front + 0.09);
  part(new THREE.Mesh(new THREE.BoxGeometry(1.05, 0.95, 0.08), dark), 0.88, -0.78, 1.22, front);
  cabinLit.glass = new THREE.MeshStandardMaterial({ color: '#33363d', emissive: '#ffcc66', emissiveIntensity: 0, roughness: 0.2 });
  part(new THREE.Mesh(new THREE.BoxGeometry(0.88, 0.78, 0.09), cabinLit.glass), 0.9, -0.78, 1.22, front + 0.01);
  part(new THREE.Mesh(new THREE.BoxGeometry(0.06, 0.8, 0.12), dark), 0.91, -0.78, 1.22, front + 0.01);
  part(new THREE.Mesh(new THREE.BoxGeometry(0.9, 0.06, 0.12), dark), 0.91, -0.78, 1.22, front + 0.01);
  part(new THREE.Mesh(new THREE.BoxGeometry(1.1, 0.16, 0.24), wood('#7a4e2c')), 0.92, -0.78, 0.7, front + 0.12);
  ['#ff7aa8', '#ffd23f', '#ffffff', '#ff7aa8', '#c58bff'].forEach((c, i) => part(new THREE.Mesh(new THREE.SphereGeometry(0.09, 10, 8), new THREE.MeshStandardMaterial({ color: c, roughness: 0.7 })), 0.93 + i * 0.006, -1.18 + i * 0.2, 0.84 + (i % 2) * 0.04, front + 0.13));
  part(new THREE.Mesh(new THREE.BoxGeometry(1.3, 0.12, 0.6), stone), 0.9, 0.75, 0.2, front + 0.3);
  // Lantern on a post.
  part(new THREE.Mesh(new THREE.CylinderGeometry(0.045, 0.06, 1.9, 8), dark), 0.94, 2.25, 1.0, front + 0.5);
  part(new THREE.Mesh(new THREE.BoxGeometry(0.5, 0.05, 0.05), dark), 0.95, 2.02, 1.9, front + 0.5);
  cabinLit.bulb = new THREE.MeshStandardMaterial({ color: '#fff3d0', emissive: '#ffb347', emissiveIntensity: 0.2, roughness: 0.4 });
  part(new THREE.Mesh(new THREE.SphereGeometry(0.11, 14, 10), cabinLit.bulb), 0.96, 1.82, 1.72, front + 0.5);
  part(new THREE.Mesh(new THREE.ConeGeometry(0.17, 0.12, 6), dark), 0.96, 1.82, 1.86, front + 0.5);
  cabinLit.light = new THREE.PointLight('#ffb65c', 0, 9, 1.6); cabinLit.light.position.set(1.82, 1.7, front + 0.7); cabin.add(cabinLit.light);
}
// b = 0 nothing yet ... 1 finished. Each piece drops in and settles with a small overshoot.
function buildCabin(b) {
  for (const m of cabinParts) {
    const o = m.userData.order * 0.82, k = range(b, o, o + 0.16), e = backOut(k);
    m.visible = k > 0; m.scale.copy(m.userData.s).multiplyScalar(Math.max(0.001, e)); m.position.y = m.userData.y + (1 - ease(k)) * 2.2;
  }
}
cabin.updateMatrixWorld(true);
const DOOR = cabin.localToWorld(new THREE.Vector3(0.75, 0, 2.4));

// App windows: what you are working in, floating in front of the pets' world.
const appWindows = [];
{
  const ui = kind => canvasTex(900, 600, (g, w, h) => {
    const darkUi = kind === 1; g.fillStyle = darkUi ? '#20243a' : '#fbfaf7'; g.fillRect(0, 0, w, h);
    g.fillStyle = darkUi ? '#2c3150' : '#ece8e1'; g.fillRect(0, 0, w, 64);
    ['#ff6159', '#ffbd2e', '#28c941'].forEach((c, i) => { g.fillStyle = c; g.beginPath(); g.arc(36 + i * 34, 32, 11, 0, TAU); g.fill(); });
    const r = rng(kind + 3), cols = darkUi ? ['#7aa2f7', '#9ece6a', '#e0af68', '#bb9af7', '#c0caf5'] : ['#d9d4cb'];
    if (kind === 2) { g.fillStyle = '#e8f1ff'; g.fillRect(40, 110, w - 80, 300); g.fillStyle = '#8fb8ff'; for (let i = 0; i < 9; i++) { const bh = 60 + r() * 200; g.fillRect(80 + i * 86, 390 - bh, 52, bh); } }
    for (let i = 0; i < (kind === 2 ? 3 : 10); i++) { let x = 50 + (darkUi ? [0, 1, 2, 2, 1, 2, 3, 2, 1, 0][i] * 40 : 0); const y = (kind === 2 ? 450 : 110) + i * 46;
      for (let k = 0; k < (darkUi ? 2 + Math.floor(r() * 3) : 1); k++) { const ww = darkUi ? 60 + r() * 150 : 420 + r() * 380; g.fillStyle = i === 0 && !darkUi ? '#2a2623' : cols[Math.floor(r() * cols.length)]; g.beginPath(); g.roundRect(x, y, ww, 20, 10); g.fill(); x += ww + 16; } }
  });
  const shape = (w, h, r) => { const s = new THREE.Shape(); s.moveTo(-w / 2 + r, -h / 2); s.lineTo(w / 2 - r, -h / 2); s.quadraticCurveTo(w / 2, -h / 2, w / 2, -h / 2 + r); s.lineTo(w / 2, h / 2 - r); s.quadraticCurveTo(w / 2, h / 2, w / 2 - r, h / 2); s.lineTo(-w / 2 + r, h / 2); s.quadraticCurveTo(-w / 2, h / 2, -w / 2, h / 2 - r); s.lineTo(-w / 2, -h / 2 + r); s.quadraticCurveTo(-w / 2, -h / 2, -w / 2 + r, -h / 2); return s; };
  for (const [kind, w, h, x, y, z, ry] of [[0, 3.3, 2.2, -2.5, 2.3, 5.2, 0.16], [1, 3.1, 2.1, 3.1, 3.0, 4.4, -0.16], [2, 2.1, 1.5, 0.5, 4.5, 3.6, 0.04]]) {
    const geo = new THREE.ExtrudeGeometry(shape(w, h, 0.16), { depth: 0.05, bevelEnabled: false }), uv = geo.attributes.uv, p = geo.attributes.position;
    for (let i = 0; i < p.count; i++) uv.setXY(i, p.getX(i) / w + 0.5, p.getY(i) / h + 0.5);
    const tex = ui(kind), m = new THREE.Mesh(geo, new THREE.MeshStandardMaterial({ map: tex, emissiveMap: tex, emissive: 0xffffff, emissiveIntensity: 0.45, roughness: 0.35, transparent: true, opacity: 0.97 }));
    m.castShadow = true; m.userData = { home: [x, y, z, ry], i: appWindows.length }; scene.add(m); appWindows.push(m);
  }
}
function showWindows(k, t) {
  for (const m of appWindows) {
    const [x, y, z, ry] = m.userData.home, i = m.userData.i, e = backOut(range(k, i * 0.12, 0.5 + i * 0.12));
    m.visible = k > 0.001; m.scale.setScalar(Math.max(0.001, e)); m.position.set(x, y + Math.sin(t * 0.7 + i * 2) * 0.08 + (1 - e) * 2, z); m.rotation.set(Math.sin(t * 0.5 + i) * 0.02, ry + Math.sin(t * 0.4 + i * 3) * 0.03, 0);
    m.material.opacity = 0.97 * clamp(k * 3);
  }
}

// A ball and a food bowl, because somebody lives here.
const ball = new THREE.Mesh(new THREE.SphereGeometry(0.2, 24, 16), new THREE.MeshStandardMaterial({ map: canvasTex(64, 32, (g, w, h) => { g.fillStyle = '#e8483f'; g.fillRect(0, 0, w, h); g.fillStyle = '#fff6e6'; g.fillRect(0, 11, w, 10); }), roughness: 0.45 }));
ball.castShadow = true; scene.add(ball);
{
  const bowl = new THREE.Mesh(new THREE.LatheGeometry([[0.0, 0.02], [0.3, 0.02], [0.34, 0.2], [0.3, 0.2], [0.26, 0.07], [0, 0.07]].map(p => new THREE.Vector2(...p)), 24), new THREE.MeshStandardMaterial({ color: '#4c8fd6', roughness: 0.3, side: THREE.DoubleSide }));
  bowl.position.set(DOOR.x + 1.3, groundY(DOOR.x + 1.3, DOOR.z + 0.3), DOOR.z + 0.3); bowl.castShadow = true; island.add(bowl);
}
