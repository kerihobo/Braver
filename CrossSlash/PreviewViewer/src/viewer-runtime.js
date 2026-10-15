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
const FF7_FIELD_VIEWPORT_HEIGHT = 240;

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
let controlsMode = 'orbit';
const flyKeys = new Set();
let flyDragging = false;
let flyPointerId = null;
let flyLastPointer = { x: 0, y: 0 };
let flyYaw = 0;
let flyPitch = 0;
let flyMovementSpeed = 1;
let currentModel = null;
let currentModelName = '';
let fieldCameraFit = null;
let fieldViewState = null;
let fieldPan = null;
let fieldLocationWireframe = true;
let mixer = null;
let currentAction = null;
let currentClip = null;
let currentAnimationName = '';
let activeLighting = null;
let isPlaying = false;
let viewerVisible = true;
let requestId = 0;
let lastTimelineUpdate = 0;
let loadSequence = 0;
let animationSequence = 0;
let isLoadingModel = false;
let pendingAnimation = null;

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
  fieldCameraFit = null;
  fieldViewState = null;
  fieldPan = null;
  camera.clearViewOffset();
  if (canvas.clientWidth > 0 && canvas.clientHeight > 0) {
    camera.aspect = canvas.clientWidth / canvas.clientHeight;
    camera.updateProjectionMatrix();
  }
  canvas.classList.remove('field-preview', 'field-panning');
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
  defaultLights.forEach((light) => { light.visible = true; });
  updateViewerLabels();
  scheduleFrame();
}

function createFieldLightingState(lighting) {
  const state = {
    uniforms: null,
    dynamic: false,
    objectMatrix: new THREE.Matrix4(),
    objectNormalMatrix: new THREE.Matrix3(),
    ambientColor: new THREE.Color(),
    ambientIntensity: 0,
    lightColors: Array.from({ length: 3 }, () => new THREE.Color()),
    lightPositions: Array.from({ length: 3 }, () => new THREE.Vector3()),
    lightIntensities: [0, 0, 0],
    lightDecays: [1, 1, 1],
  };
  updateFieldLightingState(state, lighting);
  return state;
}

function updateFieldLightingState(state, lighting) {
  const pointLights = Array.isArray(lighting?.pointLights) ? lighting.pointLights : [];
  const positionScale = lighting?.positionScale || 512;
  state.ambientColor.setHex(lighting?.ambientColor ?? 0x575455);
  state.ambientIntensity = lighting ? (lighting.ambientIntensity ?? 1) : 0;
  state.dynamic = lighting?.dynamic === true;

  for (let index = 0; index < 3; index++) {
    const source = pointLights[index];
    state.lightColors[index].setHex(source?.color ?? 0xffffff);
    state.lightPositions[index].set(
      (source?.x ?? 0) / positionScale,
      (source?.y ?? 0) / positionScale,
      (source?.z ?? 0) / positionScale
    );
    state.lightIntensities[index] = source?.intensity ?? 0;
    state.lightDecays[index] = source?.decay ?? 1;
  }

  if (!state.uniforms)
    return;
  state.uniforms.fieldAmbientColor.value.copy(state.ambientColor);
  state.uniforms.fieldAmbientIntensity.value = state.ambientIntensity;
  for (let index = 0; index < 3; index++) {
    state.uniforms[`fieldLightColor${index}`].value.copy(state.lightColors[index]);
    state.uniforms[`fieldLightPosition${index}`].value.copy(state.lightPositions[index]);
    state.uniforms[`fieldLightIntensity${index}`].value = state.lightIntensities[index];
    state.uniforms[`fieldLightDecay${index}`].value = state.lightDecays[index];
  }
}

function makeLitMaterial(source, state) {
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
  material.onBeforeCompile = (shader) => {
    const uniforms = {
      fieldObjectMatrix: { value: state.objectMatrix },
      fieldObjectNormalMatrix: { value: state.objectNormalMatrix },
      fieldAmbientColor: { value: state.ambientColor },
      fieldAmbientIntensity: { value: state.ambientIntensity },
    };
    for (let index = 0; index < 3; index++) {
      uniforms[`fieldLightColor${index}`] = { value: state.lightColors[index] };
      uniforms[`fieldLightPosition${index}`] = { value: state.lightPositions[index] };
      uniforms[`fieldLightIntensity${index}`] = { value: state.lightIntensities[index] };
      uniforms[`fieldLightDecay${index}`] = { value: state.lightDecays[index] };
    }
    state.uniforms = uniforms;
    Object.assign(shader.uniforms, uniforms);

    const lightDeclarations = [0, 1, 2].map((index) => [
      `uniform vec3 fieldLightColor${index};`,
      `uniform vec3 fieldLightPosition${index};`,
      `uniform float fieldLightIntensity${index};`,
      `uniform float fieldLightDecay${index};`,
    ].join('\n')).join('\n');
    const vertexDeclarations = [
      'uniform mat4 fieldObjectMatrix;',
      'uniform mat3 fieldObjectNormalMatrix;',
      'uniform vec3 fieldAmbientColor;',
      'uniform float fieldAmbientIntensity;',
      'varying vec3 vFieldAmbientIrradiance;',
      'varying vec3 vFieldDirectIrradiance;',
      'varying vec3 vFieldDirectIrradianceBack;',
      lightDeclarations,
    ].join('\n');
    const vertexLighting = [
      'vec3 fieldVertexPosition = (fieldObjectMatrix * vec4(transformed, 1.0)).xyz;',
      'vec3 fieldVertexNormal = normalize(fieldObjectNormalMatrix * objectNormal);',
      '#ifdef FLIP_SIDED',
      '  fieldVertexNormal = -fieldVertexNormal;',
      '#endif',
      'vFieldAmbientIrradiance = fieldAmbientColor * fieldAmbientIntensity;',
      'vec3 fieldVertexDirectIrradiance = vec3(0.0);',
      'vec3 fieldVertexDirectIrradianceBack = vec3(0.0);',
      ...[0, 1, 2].flatMap((index) => [
        `vec3 fieldToLight${index} = fieldLightPosition${index} - fieldVertexPosition;`,
        `float fieldDistance${index} = length(fieldToLight${index});`,
        `vec3 fieldDirection${index} = fieldToLight${index} / max(fieldDistance${index}, 0.0001);`,
        `float fieldAttenuation${index} = fieldLightIntensity${index} / max(pow(max(fieldDistance${index}, 0.0001), fieldLightDecay${index}), 0.01);`,
        `float fieldNdotL${index} = max(dot(fieldVertexNormal, fieldDirection${index}), 0.0);`,
        `float fieldNdotLBack${index} = max(dot(-fieldVertexNormal, fieldDirection${index}), 0.0);`,
        `fieldVertexDirectIrradiance += fieldLightColor${index} * fieldAttenuation${index} * fieldNdotL${index};`,
        `fieldVertexDirectIrradianceBack += fieldLightColor${index} * fieldAttenuation${index} * fieldNdotLBack${index};`,
      ]),
      'vFieldDirectIrradiance = fieldVertexDirectIrradiance;',
      'vFieldDirectIrradianceBack = fieldVertexDirectIrradianceBack;',
    ].join('\n');
    shader.fragmentShader = shader.fragmentShader
      .replace(
        '#include <common>',
        '#include <common>\nvarying vec3 vFieldAmbientIrradiance;\nvarying vec3 vFieldDirectIrradiance;\nvarying vec3 vFieldDirectIrradianceBack;'
      )
      .replace(
        '#include <lights_fragment_end>',
        [
          '#include <lights_fragment_end>',
          'vec3 fieldDirectIrradiance = vFieldDirectIrradiance;',
          '#ifdef DOUBLE_SIDED',
          '  if (!gl_FrontFacing) fieldDirectIrradiance = vFieldDirectIrradianceBack;',
          '#endif',
          'reflectedLight.indirectDiffuse += vFieldAmbientIrradiance * BRDF_Lambert(diffuseColor.rgb);',
          'reflectedLight.directDiffuse += fieldDirectIrradiance * BRDF_Lambert(diffuseColor.rgb);',
        ].join('\n')
      );
    shader.vertexShader = shader.vertexShader
      .replace('#include <common>', `#include <common>\n${vertexDeclarations}`)
      .replace('#include <skinning_vertex>', `#include <skinning_vertex>\n${vertexLighting}`);
  };
  material.customProgramCacheKey = () => 'crossslash-field-object-lighting-v1';
  source.dispose();
  return material;
}

function applyFieldPointLighting(root, lighting, convertMaterials = false) {
  const lightRoot = root.getObjectByName('root') || root;
  const hasFieldLighting = Array.isArray(lighting?.pointLights);
  const dynamic = lighting?.dynamic === true;
  root.updateMatrixWorld(true);
  defaultLights.forEach((light) => { light.visible = !hasFieldLighting; });

  root.traverse((object) => {
    const states = object.userData.fieldLightingStates;
    if (Array.isArray(states)) {
      states.forEach((state) => updateFieldLightingState(state, lighting));
      if (!dynamic)
        object.userData.refreshFieldLightingTransforms?.();
    }
  });

  if (!convertMaterials) return;
  root.traverse((object) => {
    if (!object.isMesh || !object.material) return;
    const states = [];
    const convert = (source) => {
      const state = createFieldLightingState(lighting);
      states.push(state);
      return makeLitMaterial(source, state);
    };
    object.material = Array.isArray(object.material)
      ? object.material.map(convert)
      : convert(object.material);
    object.userData.fieldLightingStates = states;

    const refreshTransforms = () => {
      lightRoot.updateWorldMatrix(true, false);
      object.updateWorldMatrix(true, false);
      inverseRoot.copy(lightRoot.matrixWorld).invert();
      for (const state of states) {
        state.objectMatrix.multiplyMatrices(inverseRoot, object.matrixWorld);
        state.objectNormalMatrix.getNormalMatrix(state.objectMatrix);
      }
    };
    object.userData.refreshFieldLightingTransforms = refreshTransforms;

    const previousOnBeforeRender = object.onBeforeRender;
    const inverseRoot = new THREE.Matrix4();
    object.onBeforeRender = function (renderer, scene, camera, geometry, material, group) {
      previousOnBeforeRender.call(this, renderer, scene, camera, geometry, material, group);
      if (states.some((state) => state.dynamic))
        refreshTransforms();
    };
    if (dynamic)
      refreshTransforms();
  });
}

export function clearModel(message) {
  loadSequence++;
  animationSequence++;
  isLoadingModel = false;
  pendingAnimation = null;
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

function applyFieldLocationWireframe() {
  if (!currentModel || controlsMode !== 'field') return;
  let edgeNode = null;
  currentModel.scene.traverse((object) => {
    if (!edgeNode && /walkmesh edges/i.test(object.name || ''))
      edgeNode = object;
  });
  if (edgeNode)
    edgeNode.visible = fieldLocationWireframe;
}

export function setFieldLocationWireframe(visible) {
  fieldLocationWireframe = visible === true;
  applyFieldLocationWireframe();
  scheduleFrame();
}

function fitCamera(root, zoomFactor = 1.2, cameraMode = 'orbit') {
  if (cameraMode === 'field') {
    root.updateMatrixWorld(true);
    const fieldCamera = root.getObjectByProperty('isCamera', true);
    if (fieldCamera?.isPerspectiveCamera) {
      const position = fieldCamera.getWorldPosition(new THREE.Vector3());
      const orientation = fieldCamera.getWorldQuaternion(new THREE.Quaternion());
      camera.position.copy(position);
      camera.quaternion.copy(orientation);
      camera.up.set(0, 1, 0).applyQuaternion(orientation).normalize();
      camera.fov = fieldCamera.fov;
      camera.near = fieldCamera.near;
      camera.far = fieldCamera.far;
      const backgroundSize = getFieldBackgroundSize(root, fieldCamera.aspect || 4 / 3);
      fieldCameraFit = {
        fov: fieldCamera.fov,
        aspect: fieldCamera.aspect || 4 / 3,
        fullWidth: backgroundSize.width,
        fullHeight: backgroundSize.height,
      };
      fieldViewState = createFieldViewState(backgroundSize.width, backgroundSize.height, getCanvasAspect());
      canvas.classList.add('field-preview');
      updateFieldCameraProjection();
      const forward = new THREE.Vector3(0, 0, -1).applyQuaternion(orientation).normalize();
      controls?.target.copy(position).addScaledVector(forward, 1000);
      controls?.update();
      return;
    }
  }

  camera.fov = controlsMode === 'fly' ? 50 : 42;
  camera.updateProjectionMatrix();
  root.updateMatrixWorld(true);
  // Include all child meshes and their current animated pose when finding the orbit center.
  const bounds = new THREE.Box3().setFromObject(root, true);
  if (bounds.isEmpty()) {
    camera.position.set(0, 0, 5);
    camera.lookAt(0, 0, 0);
    if (controlsMode === 'orbit') {
      controls?.target.set(0, 0, 0);
    } else {
      const euler = new THREE.Euler().setFromQuaternion(camera.quaternion, 'YXZ');
      flyYaw = euler.y;
      flyPitch = euler.x;
      camera.rotation.order = 'YXZ';
      camera.rotation.set(flyPitch, flyYaw, 0, 'YXZ');
    }
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
  camera.lookAt(center);
  if (controlsMode === 'orbit') {
    controls.target.copy(center);
    controls.minDistance = Math.max(largest / 1000, 0.01);
    controls.maxDistance = largest * 30;
    controls.update();
  } else {
    flyMovementSpeed = Math.max(largest * 0.25, 0.5);
    const euler = new THREE.Euler().setFromQuaternion(camera.quaternion, 'YXZ');
    flyYaw = euler.y;
    flyPitch = euler.x;
    camera.rotation.order = 'YXZ';
    camera.rotation.set(flyPitch, flyYaw, 0, 'YXZ');
  }
}

function getFieldBackgroundSize(root, aspect) {
  let namedBackground = null;
  let largestTexture = null;
  root.traverse((object) => {
    if (!object.isMesh) return;
    const materials = Array.isArray(object.material) ? object.material : [object.material];
    for (const material of materials) {
      const image = material?.map?.image;
      const width = image?.width || image?.naturalWidth || 0;
      const height = image?.height || image?.naturalHeight || 0;
      if (!(width > 0 && height > 0)) continue;
      const size = { width, height };
      if (/background/i.test(`${object.name} ${material?.name || ''}`))
        namedBackground = size;
      if (!largestTexture || width * height > largestTexture.width * largestTexture.height)
        largestTexture = size;
    }
  });
  const size = namedBackground || largestTexture;
  return size || { width: Math.max(aspect * FF7_FIELD_VIEWPORT_HEIGHT, 1), height: FF7_FIELD_VIEWPORT_HEIGHT };
}

function createFieldViewState(fullWidth, fullHeight, viewportAspect) {
  const zoomToFitHeight = FF7_FIELD_VIEWPORT_HEIGHT / fullHeight;
  const zoomToFitWidth = (FF7_FIELD_VIEWPORT_HEIGHT * viewportAspect) / fullWidth;
  const fitZoom = Math.min(zoomToFitHeight, zoomToFitWidth);
  return {
    fullWidth,
    fullHeight,
    centerX: fullWidth / 2,
    centerY: fullHeight / 2,
    zoom: fitZoom,
    targetZoom: fitZoom,
    minZoom: Math.min(0.1, fitZoom * 0.5),
    maxZoom: 4.0,
  };
}

function applyFieldViewOffset(viewState, viewportAspect) {
  if (!viewState || !(viewportAspect > 0)) return;
  const currentViewHeight = FF7_FIELD_VIEWPORT_HEIGHT / viewState.zoom;
  const currentViewWidth = currentViewHeight * viewportAspect;
  let viewX = viewState.centerX - currentViewWidth / 2;
  let viewY = viewState.centerY - currentViewHeight / 2;

  if (currentViewWidth >= viewState.fullWidth)
    viewX = (viewState.fullWidth - currentViewWidth) / 2;
  else
    viewX = THREE.MathUtils.clamp(viewX, 0, viewState.fullWidth - currentViewWidth);

  if (currentViewHeight >= viewState.fullHeight)
    viewY = (viewState.fullHeight - currentViewHeight) / 2;
  else
    viewY = THREE.MathUtils.clamp(viewY, 0, viewState.fullHeight - currentViewHeight);

  viewState.centerX = viewX + currentViewWidth / 2;
  viewState.centerY = viewY + currentViewHeight / 2;
  camera.setViewOffset(
    viewState.fullWidth,
    viewState.fullHeight,
    viewX,
    viewY,
    currentViewWidth,
    currentViewHeight
  );
  camera.updateProjectionMatrix();
}

function getCanvasAspect() {
  return canvas.clientWidth / Math.max(canvas.clientHeight, 1);
}

function updateFieldCameraProjection() {
  if (!fieldCameraFit || !fieldViewState) return;
  applyFieldViewOffset(fieldViewState, getCanvasAspect());
}

function resetFieldCameraView() {
  if (!fieldCameraFit || !currentModel) return;
  currentModel.scene.updateMatrixWorld(true);
  const fieldCamera = currentModel.scene.getObjectByProperty('isCamera', true);
  if (!fieldCamera?.isPerspectiveCamera) return;
  const position = fieldCamera.getWorldPosition(new THREE.Vector3());
  const orientation = fieldCamera.getWorldQuaternion(new THREE.Quaternion());
  camera.position.copy(position);
  camera.quaternion.copy(orientation);
  camera.up.set(0, 1, 0).applyQuaternion(orientation).normalize();
  camera.fov = fieldCameraFit.fov;
  camera.near = fieldCamera.near;
  camera.far = fieldCamera.far;
  fieldViewState = createFieldViewState(fieldCameraFit.fullWidth, fieldCameraFit.fullHeight, getCanvasAspect());
  const forward = new THREE.Vector3(0, 0, -1).applyQuaternion(orientation).normalize();
  controls?.target.copy(position).addScaledVector(forward, 1000);
  controls?.update();
  updateFieldCameraProjection();
  scheduleFrame();
}

function onFieldWheel(event) {
  if (controlsMode !== 'field' || !fieldViewState) return;
  event.preventDefault();
  const zoomFactor = event.deltaY > 0 ? 0.95 : 1.05;
  fieldViewState.targetZoom = THREE.MathUtils.clamp(
    fieldViewState.targetZoom * zoomFactor,
    fieldViewState.minZoom,
    fieldViewState.maxZoom
  );
  scheduleFrame();
}

function onFieldPointerDown(event) {
  if (controlsMode !== 'field' || !fieldViewState || event.button !== 0) return;
  fieldPan = {
    pointerId: event.pointerId,
    startX: event.clientX,
    startY: event.clientY,
    centerX: fieldViewState.centerX,
    centerY: fieldViewState.centerY,
  };
  canvas.classList.add('field-panning');
  if (canvas.setPointerCapture)
    canvas.setPointerCapture(event.pointerId);
  event.preventDefault();
}

function onFieldPointerMove(event) {
  if (controlsMode !== 'field' || !fieldPan || event.pointerId !== fieldPan.pointerId || !fieldViewState) return;
  const rect = canvas.getBoundingClientRect();
  if (!(rect.width > 0 && rect.height > 0)) return;
  const viewHeight = FF7_FIELD_VIEWPORT_HEIGHT / fieldViewState.zoom;
  const viewWidth = viewHeight * (rect.width / rect.height);
  const deltaX = event.clientX - fieldPan.startX;
  const deltaY = event.clientY - fieldPan.startY;
  fieldViewState.centerX = fieldPan.centerX - (deltaX / rect.width) * viewWidth;
  fieldViewState.centerY = fieldPan.centerY - (deltaY / rect.height) * viewHeight;
  applyFieldViewOffset(fieldViewState, rect.width / rect.height);
  scheduleFrame();
  event.preventDefault();
}

function onFieldPointerUp(event) {
  if (!fieldPan || event.pointerId !== fieldPan.pointerId) return;
  fieldPan = null;
  canvas.classList.remove('field-panning');
  if (canvas.hasPointerCapture?.(event.pointerId))
    canvas.releasePointerCapture(event.pointerId);
}

function onFieldDoubleClick(event) {
  if (controlsMode !== 'field') return;
  event.preventDefault();
  resetFieldCameraView();
}

function onFieldContextMenu(event) {
  if (controlsMode === 'field') event.preventDefault();
}

function ensureRenderer(cameraMode = 'orbit') {
  if (!renderer) {
    renderer = new THREE.WebGLRenderer({ canvas, antialias: false, alpha: false });
    renderer.setPixelRatio(1);
    renderer.outputColorSpace = THREE.SRGBColorSpace;
    renderer.toneMapping = THREE.NoToneMapping;
    resize();
  }

  const requestedMode = cameraMode === 'fly' ? 'fly' : cameraMode === 'field' ? 'field' : 'orbit';
  if (controls && controlsMode === requestedMode) return;
  controls?.dispose();
  controls = null;
  controlsMode = requestedMode;
  flyKeys.clear();
  flyDragging = false;
  flyPointerId = null;
  canvas.classList.remove('fly-dragging', 'field-panning');
  canvas.classList.toggle('field-preview', controlsMode === 'field');
  if (controlsMode === 'orbit' || controlsMode === 'field') {
    controls = new OrbitControls(camera, renderer.domElement);
    controls.enableDamping = true;
    controls.dampingFactor = 0.08;
    controls.screenSpacePanning = true;
    if (controlsMode === 'field') {
      controls.enableZoom = false;
      controls.enablePan = false;
      controls.enableRotate = true;
      controls.mouseButtons = {
        LEFT: null,
        MIDDLE: THREE.MOUSE.DOLLY,
        RIGHT: THREE.MOUSE.ROTATE,
      };
    }
  }
  controls?.addEventListener('change', scheduleFrame);
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

export function loadModel(base64, name, lighting = null, cameraZoom = 1.2, cameraMode = 'orbit') {
  const sequence = ++loadSequence;
  animationSequence++;
  isLoadingModel = true;
  pendingAnimation = null;
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
    ensureRenderer(cameraMode);
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
    refreshStaticLightingTransforms(gltf.scene);
    isLoadingModel = false;
    fitCamera(gltf.scene, cameraZoom, cameraMode);
    applyFieldLocationWireframe();
    setEmptyState('', false);
    scheduleFrame();
    postToHost({ type: 'preview-loaded' });
    if (pendingAnimation) {
      const nextAnimation = pendingAnimation;
      pendingAnimation = null;
      loadAnimation(nextAnimation.base64, nextAnimation.name);
    }
  }, (error) => {
    if (sequence !== loadSequence) return;
    isLoadingModel = false;
    pendingAnimation = null;
    resetCurrentModel();
    const detail = error?.message || 'The GLB could not be loaded.';
    setEmptyState(`Preview failed: ${detail}`);
    postToHost({ type: 'preview-error', message: detail });
  });
}

function refreshStaticLightingTransforms(root) {
  root.traverse((object) => {
    if (Array.isArray(object.userData.fieldLightingStates) &&
        object.userData.fieldLightingStates.every((state) => !state.dynamic))
      object.userData.refreshFieldLightingTransforms?.();
  });
}

export function loadAnimation(base64, name) {
  if (isLoadingModel || !currentModel || !mixer) {
    pendingAnimation = { base64, name };
    return;
  }

  const sequence = ++animationSequence;
  const binary = atob(base64);
  const data = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i++) data[i] = binary.charCodeAt(i);

  loader.parse(data.buffer, '', (gltf) => {
    if (sequence !== animationSequence || isLoadingModel || !currentModel || !mixer) {
      disposeObject(gltf.scene);
      return;
    }
    const clip = gltf.animations[0];
    if (!clip) {
      disposeObject(gltf.scene);
      console.warn(`No animation clip was found in ${name || 'the selected animation'}.`);
      return;
    }
    clip.name = name || clip.name || 'Animation';
    currentModel.animations = [clip];
    disposeObject(gltf.scene);
    setClip(0);
    postToHost({ type: 'preview-animation-loaded' });
  }, (error) => {
    if (sequence === animationSequence)
      console.error('Could not load animation preview:', error);
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
  if (fieldCameraFit) {
    updateFieldCameraProjection();
  } else {
    camera.aspect = width / height;
    camera.updateProjectionMatrix();
  }
  scheduleFrame();
}

canvas.addEventListener('wheel', onFieldWheel, { passive: false });
canvas.addEventListener('pointerdown', onFieldPointerDown, { capture: true });
window.addEventListener('pointermove', onFieldPointerMove);
window.addEventListener('pointerup', onFieldPointerUp);
window.addEventListener('pointercancel', onFieldPointerUp);
canvas.addEventListener('dblclick', onFieldDoubleClick);
canvas.addEventListener('contextmenu', onFieldContextMenu);

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
  let controlsMoving = false;
  let fieldZoomMoving = false;
  if (controlsMode === 'fly')
    controlsMoving = updateFlyControls(delta);
  else
    controlsMoving = controls?.update() === true;
  if (controlsMode === 'field' && fieldViewState) {
    const zoomDifference = fieldViewState.targetZoom - fieldViewState.zoom;
    if (Math.abs(zoomDifference) > 0.001) {
      fieldViewState.zoom += zoomDifference * 0.15;
      updateFieldCameraProjection();
      fieldZoomMoving = true;
    } else if (fieldViewState.zoom !== fieldViewState.targetZoom) {
      fieldViewState.zoom = fieldViewState.targetZoom;
      updateFieldCameraProjection();
    }
  }
  renderer.render(scene, camera);
  if (isPlaying || controlsMoving || fieldZoomMoving)
    scheduleFrame();
}

function onFlyKeyDown(event) {
  if (controlsMode !== 'fly' || event.altKey || event.ctrlKey || event.metaKey)
    return;
  if (['KeyW', 'KeyA', 'KeyS', 'KeyD', 'KeyQ', 'KeyE', 'ShiftLeft', 'ShiftRight'].includes(event.code)) {
    flyKeys.add(event.code);
    event.preventDefault();
    scheduleFrame();
  }
}

function onFlyKeyUp(event) {
  if (controlsMode !== 'fly') return;
  flyKeys.delete(event.code);
  scheduleFrame();
}

function onFlyPointerDown(event) {
  if (controlsMode !== 'fly' || event.button !== 0) return;
  flyDragging = true;
  flyPointerId = event.pointerId;
  flyLastPointer = { x: event.clientX, y: event.clientY };
  canvas.classList.add('fly-dragging');
  canvas.setPointerCapture(event.pointerId);
  event.preventDefault();
}

function onFlyPointerMove(event) {
  if (controlsMode !== 'fly' || !flyDragging || event.pointerId !== flyPointerId) return;
  const deltaX = event.clientX - flyLastPointer.x;
  const deltaY = event.clientY - flyLastPointer.y;
  flyLastPointer = { x: event.clientX, y: event.clientY };
  flyYaw -= deltaX * 0.003375;
  flyPitch = THREE.MathUtils.clamp(flyPitch - deltaY * 0.003375, -Math.PI / 2 + 0.01, Math.PI / 2 - 0.01);
  camera.rotation.order = 'YXZ';
  camera.rotation.set(flyPitch, flyYaw, 0, 'YXZ');
  scheduleFrame();
  event.preventDefault();
}

function onFlyPointerUp(event) {
  if (!flyDragging || event.pointerId !== flyPointerId) return;
  flyDragging = false;
  flyPointerId = null;
  canvas.classList.remove('fly-dragging');
  if (canvas.hasPointerCapture(event.pointerId))
    canvas.releasePointerCapture(event.pointerId);
}

function updateFlyControls(delta) {
  const forward = Number(flyKeys.has('KeyW')) - Number(flyKeys.has('KeyS'));
  const right = Number(flyKeys.has('KeyD')) - Number(flyKeys.has('KeyA'));
  const vertical = (Number(flyKeys.has('KeyE')) - Number(flyKeys.has('KeyQ'))) * 0.35;
  if (forward === 0 && right === 0 && vertical === 0)
    return false;

  const directionLength = Math.hypot(forward, right, vertical) || 1;
  const fast = flyKeys.has('ShiftLeft') || flyKeys.has('ShiftRight') ? 3 : 1;
  const distance = flyMovementSpeed * fast * delta / directionLength;
  camera.translateZ(-forward * distance);
  camera.translateX(right * distance);
  camera.translateY(vertical * distance);
  return true;
}

window.addEventListener('keydown', onFlyKeyDown);
window.addEventListener('keyup', onFlyKeyUp);
canvas.addEventListener('pointerdown', onFlyPointerDown);
canvas.addEventListener('pointermove', onFlyPointerMove);
canvas.addEventListener('pointerup', onFlyPointerUp);
canvas.addEventListener('pointercancel', onFlyPointerUp);

resize();

window.addEventListener('beforeunload', () => {
  cancelAnimationFrame(requestId);
  resizeObserver.disconnect();
  window.removeEventListener('keydown', onFlyKeyDown);
  window.removeEventListener('keyup', onFlyKeyUp);
  canvas.removeEventListener('pointerdown', onFlyPointerDown);
  canvas.removeEventListener('pointermove', onFlyPointerMove);
  canvas.removeEventListener('pointerup', onFlyPointerUp);
  canvas.removeEventListener('pointercancel', onFlyPointerUp);
  controls?.dispose();
  renderer?.dispose();
});
