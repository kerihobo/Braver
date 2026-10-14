import * as THREE from 'three';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';
import './style.css';

const canvas = document.querySelector('#model-canvas');
const emptyState = document.querySelector('#empty-state');
const modelLabel = document.querySelector('#model-label');
const animationSelect = document.querySelector('#animation-select');
const playButton = document.querySelector('#play-button');
const resetButton = document.querySelector('#reset-button');
const timeline = document.querySelector('#timeline');
const timeLabel = document.querySelector('#time-label');
const speedSelect = document.querySelector('#speed-select');

const scene = new THREE.Scene();
scene.background = new THREE.Color('#202a35');

const camera = new THREE.PerspectiveCamera(42, 1, 0.01, 100000);
camera.up.set(0, 1, 0);

const renderer = new THREE.WebGLRenderer({ canvas, antialias: true, alpha: false });
renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 2));
renderer.outputColorSpace = THREE.SRGBColorSpace;
renderer.toneMapping = THREE.NoToneMapping;

scene.add(new THREE.HemisphereLight(0xdce9f6, 0x38424c, 2.0));
const keyLight = new THREE.DirectionalLight(0xffffff, 2.1);
keyLight.position.set(1.5, 2.4, 2.0);
scene.add(keyLight);
const fillLight = new THREE.DirectionalLight(0x9eb8d0, 0.8);
fillLight.position.set(-2.0, 0.8, -1.5);
scene.add(fillLight);

const controls = new OrbitControls(camera, renderer.domElement);
controls.enableDamping = true;
controls.dampingFactor = 0.08;
controls.screenSpacePanning = true;

const loader = new GLTFLoader();
const clock = new THREE.Clock();
let currentModel = null;
let mixer = null;
let currentAction = null;
let currentClip = null;
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

function clearModel() {
  if (mixer) mixer.stopAllAction();
  mixer = null;
  currentAction = null;
  currentClip = null;
  isPlaying = false;
  playButton.textContent = 'Play';
  animationSelect.replaceChildren(new Option('No animations', ''));
  animationSelect.disabled = true;
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
  modelLabel.textContent = '';
}

function fitCamera(root) {
  root.updateMatrixWorld(true);
  const bounds = new THREE.Box3().setFromObject(root);
  if (bounds.isEmpty()) {
    camera.position.set(0, 0, 5);
    controls.target.set(0, 0, 0);
    return;
  }

  const size = bounds.getSize(new THREE.Vector3());
  const center = bounds.getCenter(new THREE.Vector3());
  const largest = Math.max(size.x, size.y, size.z, 0.1);
  const distance = (largest * 0.5) / Math.tan(THREE.MathUtils.degToRad(camera.fov * 0.5)) * 1.55;
  camera.position.set(center.x + distance * 0.9, center.y + distance * 0.65, center.z + distance * 1.15);
  camera.near = Math.max(largest / 10000, 0.001);
  camera.far = Math.max(largest * 100, 1000);
  camera.updateProjectionMatrix();
  controls.target.copy(center);
  controls.minDistance = Math.max(largest / 1000, 0.01);
  controls.maxDistance = largest * 30;
  controls.update();
}

function setClip(index) {
  if (!mixer || !currentModel?.animations?.[index]) return;
  mixer.stopAllAction();
  currentClip = currentModel.animations[index];
  currentAction = mixer.clipAction(currentClip);
  currentAction.reset();
  currentAction.setLoop(THREE.LoopRepeat, Infinity);
  currentAction.timeScale = Number(speedSelect.value);
  currentAction.play();
  currentAction.paused = !isPlaying;
  mixer.update(0);
  timeline.value = '0';
  updateTimeLabel(0);
}

function updateTimeLabel(time) {
  const duration = currentClip?.duration || 0;
  const displayed = duration > 0 ? Math.min(time % duration, duration) : 0;
  timeLabel.textContent = `${displayed.toFixed(2)} / ${duration.toFixed(2)} s`;
}

function loadModel(base64, name, sequence) {
  setEmptyState('Loading preview…');
  const binary = atob(base64);
  const data = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i++) data[i] = binary.charCodeAt(i);

  loader.parse(data.buffer, '', (gltf) => {
    if (sequence !== loadSequence) {
      disposeObject(gltf.scene);
      return;
    }
    clearModel();
    currentModel = gltf;
    scene.add(gltf.scene);
    modelLabel.textContent = name;
    fitCamera(gltf.scene);

    if (gltf.animations.length > 0) {
      animationSelect.replaceChildren(...gltf.animations.map((clip, index) => {
        const label = clip.name?.trim() || `Animation ${index + 1}`;
        return new Option(label, String(index));
      }));
      mixer = new THREE.AnimationMixer(gltf.scene);
      animationSelect.disabled = false;
      playButton.disabled = false;
      resetButton.disabled = false;
      timeline.disabled = false;
      setClip(0);
    }
    setEmptyState('', false);
    postToHost({ type: 'preview-loaded' });
  }, (error) => {
    if (sequence !== loadSequence) return;
    clearModel();
    const detail = error?.message || 'The GLB could not be loaded.';
    setEmptyState(`Preview failed: ${detail}`);
    postToHost({ type: 'preview-error', message: detail });
  });
}

window.chrome?.webview?.addEventListener('message', (event) => {
  const message = event.data;
  if (!message || typeof message !== 'object') return;
  if (message.type === 'load-model') {
    const sequence = ++loadSequence;
    clearModel();
    loadModel(message.glbBase64, message.name || 'Model preview', sequence);
  } else if (message.type === 'clear') {
    loadSequence++;
    clearModel();
    setEmptyState(message.message || 'Choose a model to preview');
  } else if (message.type === 'visibility') {
    viewerVisible = message.visible === true;
    if (viewerVisible && requestId === 0) {
      clock.start();
      requestId = requestAnimationFrame(animate);
    } else if (!viewerVisible && requestId !== 0) {
      cancelAnimationFrame(requestId);
      requestId = 0;
    }
  }
});

animationSelect.addEventListener('change', () => {
  isPlaying = false;
  playButton.textContent = 'Play';
  setClip(Number(animationSelect.value));
});

playButton.addEventListener('click', () => {
  if (!currentAction) return;
  isPlaying = !isPlaying;
  currentAction.paused = !isPlaying;
  playButton.textContent = isPlaying ? 'Pause' : 'Play';
  clock.getDelta();
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
});

timeline.addEventListener('input', () => {
  if (!currentAction || !currentClip || !mixer) return;
  isPlaying = false;
  playButton.textContent = 'Play';
  currentAction.paused = true;
  currentAction.time = currentClip.duration * Number(timeline.value) / Number(timeline.max);
  mixer.update(0);
  updateTimeLabel(currentAction.time);
});

speedSelect.addEventListener('change', () => {
  if (currentAction) currentAction.timeScale = Number(speedSelect.value);
});

function resize() {
  const width = canvas.clientWidth;
  const height = canvas.clientHeight;
  if (width <= 0 || height <= 0) return;
  renderer.setSize(width, height, false);
  camera.aspect = width / height;
  camera.updateProjectionMatrix();
}

const resizeObserver = new ResizeObserver(resize);
resizeObserver.observe(document.querySelector('#viewer'));

function animate() {
  if (!viewerVisible) {
    requestId = 0;
    return;
  }
  requestId = requestAnimationFrame(animate);
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
  controls.update();
  renderer.render(scene, camera);
}

resize();
animate();
postToHost({ type: 'viewer-ready' });

window.addEventListener('beforeunload', () => {
  cancelAnimationFrame(requestId);
  resizeObserver.disconnect();
  controls.dispose();
  renderer.dispose();
});
