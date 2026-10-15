/* ============ ARCHIVISION · moteur 3D (module ES, Three.js r160 comme ARCHIPLAN) ============
   Visite 3D (orbite, marche, immersion 360°), hiérarchie BIM, données IFC / ARCHIPLAN, mesure, coupe,
   réalité augmentée WebXR (pose sur surface, échelle, hauteur, rotation, capture, inspection), galerie annotée. */
import * as THREE from 'three';
import {GLTFLoader} from 'three/addons/loaders/GLTFLoader.js';
import {DRACOLoader} from 'three/addons/loaders/DRACOLoader.js';
import {MeshoptDecoder} from 'three/addons/libs/meshopt_decoder.module.js';
import {GLTFExporter} from 'three/addons/exporters/GLTFExporter.js';
import {OrbitControls} from 'three/addons/controls/OrbitControls.js';
import {RoomEnvironment} from 'three/addons/environments/RoomEnvironment.js';
import * as BufferGeometryUtils from 'three/addons/utils/BufferGeometryUtils.js';

const WEB_IFC_VERSION = '0.0.77';
const WEB_IFC_URL = `https://cdn.jsdelivr.net/npm/web-ifc@${WEB_IFC_VERSION}/`;
const DRACO_URL = 'https://cdn.jsdelivr.net/npm/three@0.160.0/examples/jsm/libs/draco/gltf/';
const APP_VERSION = '1.0.0';
