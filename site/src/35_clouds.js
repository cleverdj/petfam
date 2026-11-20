// ---- cumulus around the island: painted puffs on cards that always face the camera -----------------------------------
const cumulus = [];
{
  const tex = seed => canvasTex(512, 320, (g, w, h) => {
    const r = rng(seed), blobs = [];
    for (let i = 0; i < 26; i++) { const x = 90 + r() * 330, hump = 1 - Math.abs(x - 256) / 210; blobs.push([x, 230 - r() * 150 * hump, 30 + r() * 46 * (0.5 + hump)]); }
    const layer = (dx, dy, k, c0, c1) => { for (const [x, y, rad] of blobs) { const gr = g.createRadialGradient(x + dx - rad * 0.3, y + dy - rad * 0.35, rad * 0.1, x + dx, y + dy, rad * k); gr.addColorStop(0, c0); gr.addColorStop(0.8, c1); gr.addColorStop(1, c1.replace(/[\d.]+\)$/, '0)')); g.fillStyle = gr; g.beginPath(); g.arc(x + dx, y + dy, rad * k, 0, TAU); g.fill(); } };
    layer(6, 16, 1.0, 'rgba(150,170,205,1)', 'rgba(135,158,198,0.95)');
    layer(0, 0, 0.92, 'rgba(255,255,255,1)', 'rgba(232,238,248,0.97)');
    layer(-8, -12, 0.6, 'rgba(255,255,255,1)', 'rgba(255,253,246,0.9)');
    g.globalCompositeOperation = 'destination-out'; const cut = g.createLinearGradient(0, 235, 0, 290); cut.addColorStop(0, 'rgba(0,0,0,0)'); cut.addColorStop(1, 'rgba(0,0,0,1)'); g.fillStyle = cut; g.fillRect(0, 235, w, h);
  });
  const texs = [3, 11, 23].map(tex), r = rng(404), geo = new THREE.PlaneGeometry(1, 0.625);
  for (let i = 0; i < 18; i++) {
    const a = i / 18 * TAU + r() * 0.3, d = 34 + r() * 70, s = 16 + r() * 26, m = new THREE.Mesh(geo, new THREE.MeshBasicMaterial({ map: texs[i % 3], transparent: true, depthWrite: false, fog: false }));
    m.position.set(Math.cos(a) * d, -12 + r() * 30, Math.sin(a) * d); m.scale.setScalar(s); m.userData = { a, d, y: m.position.y, sp: 0.004 + r() * 0.004 }; m.renderOrder = -40; scene.add(m); cumulus.push(m);
  }
}
function updateClouds(t) {
  const c = _c.setRGB(...env.cloudCol).multiplyScalar(lerp(1.0, 0.22, env.night)).lerp(scene.fog.color, 0.18);
  for (const m of cumulus) { const u = m.userData, a = u.a + t * u.sp; m.position.set(Math.cos(a) * u.d, u.y + Math.sin(t * 0.1 + u.a) * 0.6, Math.sin(a) * u.d); m.quaternion.copy(camera.quaternion); m.material.color.copy(c); }
}
