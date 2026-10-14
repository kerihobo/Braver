import * as THREE from 'three';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';

const canvas = document.querySelector('#model-canvas');
const emptyState = document.querySelector('#empty-state');
const modelNameLabel = document.querySelector('#model-name');
const animationLabel = document.querySelector('#animation-label');
const playButton = document.querySelector('#play-button');
const resetButton = document.querySelector('#reset-button');
const timeline = document.querySelector('#timeline');
const timeLabel = document.querySelector('#time-label');
const speedSelect = document.querySelector('#speed-select');

const scene = new THREE.Scene();
scene.background = new THREE.Color('#202a35');

const camera = new THREE.PerspectiveCamera(42, 1, 0.01, 100000);
camera.up.set(0, 1, 0);

const defaultLights = [];
defaultLights.push(new THREE.HemisphereLight(0xdce9f6, 0x38424c, 2.0));
const keyLight = new THREE.DirectionalLight(0xffffff, 2.1);
keyLight.position.set(1.5, 2.4, 2.0);
defaultLights.push(keyLight);
const fillLight = new THREE.DirectionalLight(0x9eb8d0, 0.8);
fillLight.position.set(-2.0, 0.8, -1.5);
defaultLights.push(fillLight);
scene.add(...defaultLights);

const loader = new GLTFLoader();
const clock = new THREE.Clock();
let renderer = null;
let controls = null;
let currentModel = null;
let currentModelName = '';
let mixer = null;
let currentAction = null;
let currentClip = null;
let currentAnimationName = '';
let activeLighting = null;
let fieldLightObjects = [];
let isPlaying = false;
let viewerVisible = true;
let requestId = 0;
let lastTimelineUpdate = 0;
let loadSequence = 0;

function postToHost(payload) {
  window.chrome?.webview?.postMessage(payload);
}

function setEmptyState(message, visible = true) {
  emptyState.textContent = message;
  emptyState.classList.toggle('visible', visible);
}

function updateViewerLabels() {
  modelNameLabel.textContent = currentModelName;
  animationLabel.textContent = currentAnimationName ? `Animation: ${currentAnimationName}` : '';
  animationLabel.hidden = !currentAnimationName;
}

function disposeObject(root) {
  root.traverse((object) => {
    if (object.geometry) object.geometry.dispose();
    if (!object.material) return;
    const materials = Array.isArray(object.material) ? object.material : [object.material];
    for (const material of materials) {
      for (const value of Object.values(material)) {
        if (value?.isTexture) value.dispose();
      }
      material.dispose();
    }
  });
}

function resetCurrentModel() {
  if (mixer) mixer.stopAllAction();
  mixer = null;
  currentAction = null;
  currentClip = null;
  currentModelName = '';
  currentAnimationName = '';
  isPlaying = false;
  playButton.textContent = 'Play';
  playButton.disabled = true;
  resetButton.disabled = true;
  timeline.disabled = true;
  timeline.value = '0';
  timeLabel.textContent = '0.00 / 0.00 s';
  if (currentModel) {
    scene.remove(currentModel.scene);
    disposeObject(currentModel.scene);
    currentModel = null;
  }
  fieldLightObjects = [];
  defaultLights.forEach((light) => { light.visible = true; });
  updateViewerLabels();
  scheduleFrame();
}

function makeLitMaterial(source) {
  const material = new THREE.MeshLambertMaterial({
    color: source.color,
    map: source.map,
    alphaMap: source.alphaMap,
    vertexColors: source.vertexColors,
    transparent: source.transparent,
    opacity: source.opacity,
    alphaTest: source.alphaTest,
    side: source.side,
    depthTest: source.depthTest,
    depthWrite: source.depthWrite,
    blending: source.blending,
  });
  material.name = source.name;
  material.toneMapped = source.toneMapped;
  source.dispose();
  return material;
}

function applyFieldPointLighting(root, lighting, convertMaterials = false) {
  for (const light of fieldLightObjects)
    light.parent?.remove(light);
  fieldLightObjects = [];

  if (!Array.isArray(lighting?.pointLights)) {
    defaultLights.forEach((light) => { light.visible = true; });
    return;
  }

  defaultLights.forEach((light) => { light.visible = false; });
  const ambientLight = new THREE.AmbientLight(lighting.ambientColor ?? 0x575455, lighting.ambientIntensity ?? 1);
  root.add(ambientLight);
  fieldLightObjects.push(ambientLight);

  const lightRoot = root.getObjectByName('root') || root;
  const positionScale = lighting.positionScale || 512;
  for (const sourceLight of lighting.pointLights) {
    const light = new THREE.PointLight(
      sourceLight.color,
      sourceLight.intensity ?? 48,
      sourceLight.distance ?? 0,
      sourceLight.decay ?? 2
    );
    // FF7's light records are in the HRC model's local XYZ basis. Parenting to
    // the animated root applies the same root-axis rotation as the skeleton.
    // Keep that basis, but convert FF7's 512-units-per-model-unit positions;
    // the coordinates are local offsets, not positions thousands of GLB units away.
    light.position.set(
      sourceLight.x / positionScale,
      sourceLight.y / positionScale,
      sourceLight.z / positionScale
    );
    lightRoot.add(light);
    fieldLightObjects.push(light);
  }

  if (convertMaterials) {
    root.traverse((object) => {
      if (!object.isMesh || !object.material) return;
      object.material = Array.isArray(object.material)
        ? object.material.map((material) => makeLitMaterial(material))
        : makeLitMaterial(object.material);
    });
  }
}

export function clearModel(message) {
  loadSequence++;
  activeLighting = null;
  resetCurrentModel();
  setEmptyState(message || 'Choose a model to preview');
}

export function setLighting(lighting) {
  activeLighting = lighting;
  if (currentModel)
    applyFieldPointLighting(currentModel.scene, activeLighting, false);
  scheduleFrame();
}

function fitCamera(root, zoomFactor = 1.2) {
  root.updateMatrixWorld(true);
  // Include all child meshes and their current animated pose when finding the orbit center.
  const bounds = new THREE.Box3().setFromObject(root, true);
  if (bounds.isEmpty()) {
    camera.position.set(0, 0, 5);
    controls.target.set(0, 0, 0);
    return;
  }

  const size = bounds.getSize(new THREE.Vector3());
  const center = bounds.getCenter(new THREE.Vector3());
  const largest = Math.max(size.x, size.y, size.z, 0.1);
  const cameraZoom = Number.isFinite(zoomFactor) && zoomFactor > 0 ? zoomFactor : 1.2;
  const distance = (largest * 0.5) / Math.tan(THREE.MathUtils.degToRad(camera.fov * 0.5)) * 1.55 / 1.4 * 1.1 / 1.2 / cameraZoom;
  camera.position.set(center.x + distance * 0.9, center.y + distance * 0.65, center.z + distance * 1.15);
  camera.near = Math.max(largest / 10000, 0.001);
  camera.far = Math.max(largest * 100, 1000);
  camera.updateProjectionMatrix();
  controls.target.copy(center);
  controls.minDistance = Math.max(largest / 1000, 0.01);
  controls.maxDistance = largest * 30;
  controls.update();
}

function ensureRenderer() {
  if (renderer) return;
  renderer = new THREE.WebGLRenderer({ canvas, antialias: false, alpha: false });
  renderer.setPixelRatio(1);
  renderer.outputColorSpace = THREE.SRGBColorSpace;
  renderer.toneMapping = THREE.NoToneMapping;

  controls = new OrbitControls(camera, renderer.domElement);
  controls.enableDamping = true;
  controls.dampingFactor = 0.08;
  controls.screenSpacePanning = true;
  controls.addEventListener('change', scheduleFrame);
  resize();
}

function setClip(index) {
  if (!mixer || !currentModel?.animations?.[index]) return;
  mixer.stopAllAction();
  currentClip = currentModel.animations[index];
  currentAnimationName = currentClip.name?.trim() || `Animation ${index + 1}`;
  updateViewerLabels();
  currentAction = mixer.clipAction(currentClip);
  currentAction.reset();
  currentAction.setLoop(THREE.LoopRepeat, Infinity);
  currentAction.timeScale = Number(speedSelect.value);
  currentAction.play();
  currentAction.paused = false;
  mixer.update(0);
  timeline.value = '0';
  updateTimeLabel(0);
  scheduleFrame();
}

function updateTimeLabel(time) {
  const duration = currentClip?.duration || 0;
  const displayed = duration > 0 ? Math.min(time % duration, duration) : 0;
  timeLabel.textContent = `${displayed.toFixed(2)} / ${duration.toFixed(2)} s`;
}

export function loadModel(base64, name, lighting = null, cameraZoom = 1.2) {
  const sequence = ++loadSequence;
  activeLighting = lighting;
  setEmptyState('Loading preview…');
  const binary = atob(base64);
  const data = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i++) data[i] = binary.charCodeAt(i);

  loader.parse(data.buffer, '', (gltf) => {
    if (sequence !== loadSequence) {
      disposeObject(gltf.scene);
      return;
    }
    resetCurrentModel();
    ensureRenderer();
    currentModel = gltf;
    gltf.scene.traverse((object) => {
      if (object.isSkinnedMesh)
        object.frustumCulled = false;
    });
    if (activeLighting?.pointLights)
      applyFieldPointLighting(gltf.scene, activeLighting, true);
    scene.add(gltf.scene);
    currentModelName = name;
    updateViewerLabels();

    if (gltf.animations.length > 0) {
      mixer = new THREE.AnimationMixer(gltf.scene);
      playButton.disabled = false;
      playButton.textContent = 'Pause';
      resetButton.disabled = false;
      timeline.disabled = false;
      isPlaying = true;
      setClip(0);
    }
    fitCamera(gltf.scene, cameraZoom);
    setEmptyState('', false);
    scheduleFrame();
    postToHost({ type: 'preview-loaded' });
  }, (error) => {
    if (sequence !== loadSequence) return;
    resetCurrentModel();
    const detail = error?.message || 'The GLB could not be loaded.';
    setEmptyState(`Preview failed: ${detail}`);
    postToHost({ type: 'preview-error', message: detail });
  });
}

export function setVisibility(visible) {
  viewerVisible = visible === true;
  if (viewerVisible) {
    clock.start();
    scheduleFrame();
  } else if (requestId !== 0) {
    cancelAnimationFrame(requestId);
    requestId = 0;
  }
}

playButton.addEventListener('click', () => {
  if (!currentAction) return;
  isPlaying = !isPlaying;
  currentAction.paused = !isPlaying;
  playButton.textContent = isPlaying ? 'Pause' : 'Play';
  clock.getDelta();
  scheduleFrame();
});

resetButton.addEventListener('click', () => {
  if (!currentAction || !mixer) return;
  isPlaying = false;
  playButton.textContent = 'Play';
  currentAction.paused = true;
  currentAction.time = 0;
  mixer.update(0);
  timeline.value = '0';
  updateTimeLabel(0);
  scheduleFrame();
});

timeline.addEventListener('input', () => {
  if (!currentAction || !currentClip || !mixer) return;
  isPlaying = false;
  playButton.textContent = 'Play';
  currentAction.paused = true;
  currentAction.time = currentClip.duration * Number(timeline.value) / Number(timeline.max);
  mixer.update(0);
  updateTimeLabel(currentAction.time);
  scheduleFrame();
});

speedSelect.addEventListener('change', () => {
  if (currentAction) currentAction.timeScale = Number(speedSelect.value);
});

function resize() {
  if (!renderer) return;
  const width = canvas.clientWidth;
  const height = canvas.clientHeight;
  if (width <= 0 || height <= 0) return;
  renderer.setSize(width, height, false);
  camera.aspect = width / height;
  camera.updateProjectionMatrix();
  scheduleFrame();
}

const resizeObserver = new ResizeObserver(resize);
resizeObserver.observe(document.querySelector('#viewer'));

function scheduleFrame() {
  if (viewerVisible && renderer && requestId === 0)
    requestId = requestAnimationFrame(animate);
}

function animate() {
  requestId = 0;
  if (!viewerVisible || !renderer) return;
  const delta = Math.min(clock.getDelta(), 0.1);
  if (isPlaying && mixer) {
    mixer.update(delta);
    const now = performance.now();
    if (now - lastTimelineUpdate > 100 && currentAction && currentClip?.duration > 0) {
      lastTimelineUpdate = now;
      const progress = (currentAction.time % currentClip.duration) / currentClip.duration;
      timeline.value = String(Math.round(progress * Number(timeline.max)));
      updateTimeLabel(currentAction.time);
    }
  }
  const controlsMoving = controls?.update() === true;
  renderer.render(scene, camera);
  if (isPlaying || controlsMoving)
    scheduleFrame();
}

resize();

window.addEventListener('beforeunload', () => {
  cancelAnimationFrame(requestId);
  resizeObserver.disconnect();
  controls?.dispose();
  renderer?.dispose();
});
