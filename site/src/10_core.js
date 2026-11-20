// ---- renderer, sky, light and the lens: one "weather of the moment" that every material reads -----------------------
const canvas = document.getElementById('scene');
const renderer = new THREE.WebGLRenderer({ canvas, antialias: false, powerPreference: 'high-performance' });
renderer.shadowMap.enabled = true;
renderer.shadowMap.type = THREE.PCFShadowMap;
renderer.toneMapping = THREE.NoToneMapping;          // the lens pass below does exposure, tone curve and encoding
const scene = new THREE.Scene();
const camera = new THREE.PerspectiveCamera(32, 1, 0.1, 900);
const U = { time: { value: 0 }, wind: { value: new THREE.Vector2(1, 0.3) }, snow: { value: 0 }, wet: { value: 0 } };   // shared by all materials

// The state of the day. Story keys (70_story.js) blend these; applyEnv() pushes one to the sky, the lights and the fog.
const ENV0 = { sun: [0.45, 0.62, 0.55], sunCol: [1.0, 0.93, 0.80], sunInt: 3.3, top: [0.05, 0.22, 0.70], hor: [0.44, 0.70, 0.95], hemi: 0.62, ground: [0.40, 0.46, 0.30],
  cloud: 0.42, cloudCol: [1, 1, 1], night: 0, fog: [0.62, 0.80, 0.96], fogDen: 0.0045, exposure: 1.0, bloom: 0.25, sat: 1.08, warm: 0.0 };
const env = JSON.parse(JSON.stringify(ENV0));

const skyU = { sunDir: { value: new THREE.Vector3() }, sunCol: { value: new THREE.Color() }, top: { value: new THREE.Color() }, hor: { value: new THREE.Color() },
  cloud: { value: 0.4 }, cloudCol: { value: new THREE.Color() }, night: { value: 0 }, time: U.time, forEnv: { value: 0 } };
const skyMat = new THREE.ShaderMaterial({
  side: THREE.BackSide, depthWrite: false, uniforms: skyU,
  vertexShader: 'varying vec3 vD; void main(){ vD = position; vec4 p = projectionMatrix * modelViewMatrix * vec4(position, 1.0); gl_Position = p.xyww; }',
  fragmentShader: `uniform vec3 sunDir, sunCol, top, hor, cloudCol; uniform float cloud, night, time, forEnv; varying vec3 vD; ${GLSL_NOISE}
    void main(){
      vec3 d = normalize(vD); float y = d.y;
      vec3 c = mix(hor, top, pow(smoothstep(-0.02, 0.9, y), 0.55));
      c = mix(c, hor * 0.82, smoothstep(0.0, -0.35, y));
      float s = max(dot(d, sunDir), 0.0);
      c += sunCol * (pow(s, 8.0) * 0.12 + pow(s, 64.0) * 0.35);
      if (forEnv < 0.5) c += sunCol * smoothstep(0.9994, 0.9998, s) * 30.0 * (1.0 - night);
      // Stars and a moon for the night.
      if (night > 0.01 && forEnv < 0.5) {
        vec3 q = d * 260.0; vec3 g = floor(q); float h = hash31(g);
        c += vec3(0.85, 0.9, 1.0) * step(0.992, h) * smoothstep(0.5, 0.0, length(fract(q) - 0.5)) * (0.5 + 0.5 * sin(time * 2.0 + h * 50.0)) * night * 2.5 * smoothstep(0.0, 0.2, y);
        float m = max(dot(d, normalize(vec3(-0.5, 0.42, -0.6))), 0.0);
        c += vec3(1.0, 0.97, 0.88) * (smoothstep(0.9990, 0.9993, m) * 6.0 + pow(m, 300.0) * 0.5) * night;
      }
      // Clouds: fbm on a high plane, lit from the sun's side.
      if (y > 0.015) {
        vec2 uv = d.xz / (y + 0.12) * 0.75 + vec2(time * 0.006, time * 0.002);
        float n = fbm(vec3(uv * 1.3, time * 0.01)), n2 = fbm(vec3(uv * 4.2 + 9.0, 3.0));
        float cov = smoothstep(1.0 - cloud - 0.12, 1.0 - cloud + 0.16, n * 0.75 + n2 * 0.25);
        float lit = fbm(vec3((uv + sunDir.xz * 0.12) * 1.3, time * 0.01));
        vec3 cc = mix(cloudCol * mix(hor, vec3(1.0), 0.75), cloudCol * (0.55 * hor + 0.25), smoothstep(0.35, 0.8, lit) * 0.7) + sunCol * pow(s, 6.0) * 0.25;
        c = mix(c, cc * mix(1.0, 0.25, night), cov * smoothstep(0.015, 0.14, y) * 0.94);
      }
      gl_FragColor = vec4(c, 1.0);
    }`,
});
const skyGeo = new THREE.SphereGeometry(400, 48, 24);
const sky = new THREE.Mesh(skyGeo, skyMat); sky.frustumCulled = false; sky.renderOrder = -100; scene.add(sky);
// A copy of the sky lights the scene: reflections and soft fill come from the same colours the viewer sees.
const envScene = new THREE.Scene(); envScene.add(new THREE.Mesh(skyGeo, skyMat));
const pmrem = new THREE.PMREMGenerator(renderer);
let envRT = null, envKey = '';
function refreshEnvMap() {
  const key = [env.top, env.hor, env.sun, env.cloud, env.night, env.sunCol].flat().map(v => v.toFixed(2)).join();
  if (key === envKey) return;
  envKey = key; skyU.forEnv.value = 1;
  const rt = pmrem.fromScene(envScene, 0, 1, 500);
  skyU.forEnv.value = 0; scene.environment = rt.texture; if (envRT) envRT.dispose(); envRT = rt;
}

const hemi = new THREE.HemisphereLight(0xffffff, 0x556644, 1);
const sun = new THREE.DirectionalLight(0xffffff, 3);
sun.castShadow = true; sun.shadow.mapSize.set(4096, 4096); sun.shadow.bias = -0.0004; sun.shadow.normalBias = 0.035; sun.shadow.radius = 3;
Object.assign(sun.shadow.camera, { left: -17, right: 17, top: 17, bottom: -17, near: 1, far: 90 });
scene.add(hemi, sun, sun.target);
scene.fog = new THREE.FogExp2(0xffffff, 0.01);

// ---- lens: HDR scene -> bloom -> tilt-shift, exposure, filmic curve, grade, vignette, grain -------------------------
let composer, bloomPass, lensPass;
const lensU = { tDiffuse: { value: null }, time: U.time, res: { value: new THREE.Vector2(1, 1) }, exposure: { value: 1 }, sat: { value: 1.08 }, warm: { value: 0 },
  tilt: { value: 0.0 }, focus: { value: 0.52 }, vig: { value: 0.32 }, fade: { value: 0 } };
function buildLens(w, h) {
  const rt = new THREE.WebGLRenderTarget(w, h, { type: THREE.HalfFloatType, samples: QUALITY.msaa });
  composer = new EffectComposer(renderer, rt);
  composer.addPass(new RenderPass(scene, camera));
  bloomPass = new UnrealBloomPass(new THREE.Vector2(w / 2, h / 2), 0.25, 0.7, 1.35);
  composer.addPass(bloomPass);
  lensPass = new ShaderPass({
    uniforms: lensU,
    vertexShader: 'varying vec2 vUv; void main(){ vUv = uv; gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 1.0); }',
    fragmentShader: `uniform sampler2D tDiffuse; uniform vec2 res; uniform float time, exposure, sat, warm, tilt, focus, vig, fade; varying vec2 vUv; ${GLSL_NOISE}
      vec3 aces(vec3 x){ return clamp((x * (2.51 * x + 0.03)) / (x * (2.43 * x + 0.59) + 0.14), 0.0, 1.0); }
      void main(){
        // Tilt-shift: sharp in a band, soft above and below, which is what makes a scene read as a miniature.
        float r = tilt * smoothstep(0.10, 0.5, abs(vUv.y - focus)) * 7.0;
        vec3 c = texture2D(tDiffuse, vUv).rgb;
        if (r > 0.3) { vec3 a = c; for (int i = 0; i < 12; i++) { float an = float(i) * 2.39996, rr = sqrt((float(i) + 0.5) / 12.0) * r; a += texture2D(tDiffuse, vUv + vec2(cos(an), sin(an)) * rr / res).rgb; } c = a / 13.0; }
        c *= exposure;
        c = mix(c, c * vec3(1.06, 1.0, 0.90), warm);
        c = aces(c);
        float l = dot(c, vec3(0.2126, 0.7152, 0.0722));
        c = mix(vec3(l), c, sat);
        c = mix(c, smoothstep(0.0, 1.0, c), 0.25);
        vec2 q = vUv - 0.5; c *= 1.0 - vig * smoothstep(0.35, 0.95, length(q * vec2(1.0, 0.85)) * 1.25);
        c = pow(c, vec3(1.0 / 2.2));
        c += (fract(sin(dot(gl_FragCoord.xy + mod(floor(time * 24.0), 64.0) * 17.3, vec2(12.9898, 78.233))) * 43758.5453) - 0.5) * 0.016;
        gl_FragColor = vec4(mix(c, vec3(0.045, 0.04, 0.05), fade), 1.0);
      }`,
  });
  composer.addPass(lensPass);
}

const _c = new THREE.Color();
function applyEnv() {
  const e = env;
  skyU.sunDir.value.set(...e.sun).normalize(); skyU.sunCol.value.setRGB(...e.sunCol); skyU.top.value.setRGB(...e.top); skyU.hor.value.setRGB(...e.hor);
  skyU.cloud.value = e.cloud; skyU.cloudCol.value.setRGB(...e.cloudCol); skyU.night.value = e.night;
  sun.color.setRGB(...e.sunCol); sun.intensity = e.sunInt;
  hemi.color.setRGB(...e.hor).lerp(_c.setRGB(...e.top), 0.35); hemi.groundColor.setRGB(...e.ground); hemi.intensity = e.hemi;
  scene.fog.color.setRGB(...e.fog); scene.fog.density = e.fogDen;
  scene.environmentIntensity = 0.55 * lerp(1, 0.6, e.night);
  lensU.exposure.value = e.exposure; lensU.sat.value = e.sat; lensU.warm.value = e.warm;
  if (bloomPass) bloomPass.strength = e.bloom;
  refreshEnvMap();
}
// The sun's shadow box follows the point of interest.
function aimSun(x, y, z) {
  sun.target.position.set(x, y, z); sun.position.copy(skyU.sunDir.value).multiplyScalar(45).add(sun.target.position); sun.target.updateMatrixWorld();
  sky.position.copy(camera.position);
}
