// ARCHIVISION · réalité augmentée (AR Foundation + ARCore) : détection des surfaces, réticule, pose de la maquette, échelle (1:200 à 1:1),
// hauteur, rotation, gestes à deux doigts, inspection d'un élément visé, capture (image caméra + maquette), lumière estimée, occlusion facultative.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using StudioPrism.ArchiVision.Viewer;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

namespace StudioPrism.ArchiVision.AR
{
    public enum ArAvailability { Unknown, Checking, Supported, NeedsInstall, Unsupported }

    public class ARController : MonoBehaviour
    {
        public ARSession Session; public XROrigin Origin; public Light Sun;
        public bool Running { get; private set; }
        public bool Placed { get; private set; }
        public float Scale { get; private set; } = 1;
        public float Height { get; private set; }
        public float Rotation { get; private set; }
        public bool Occlusion { get; private set; }
        public static ArAvailability Availability { get; private set; } = ArAvailability.Unknown;
        public event Action<string> Status;            // message d'aide (à afficher en haut de l'écran)
        public event Action StateChanged;
        public Func<Vector2, bool> IsOverUI = _ => false;

        ARRaycastManager _ray; ARPlaneManager _planes; ARCameraManager _cam; AROcclusionManager _occ;
        readonly List<ARRaycastHit> _hits = new List<ARRaycastHit>();
        GameObject _reticle; Pose _lastPose; bool _hasPose; float _startT, _lastHitT;
        Vector3 _base; float _baseYaw; string _lastStatus;
        float _g0d, _g0ang, _g0s, _g0r; bool _gesture;
        Quaternion _sunRot0; float _sunI0; Color _sunC0;

        public const float MinScale = 0.005f;
        public static string Ratio(float s) => s >= .999f ? "1:1" : "1:" + Mathf.RoundToInt(1 / s);

        void Awake()
        {
            _sunRot0 = Sun ? Sun.transform.rotation : Quaternion.identity; if (Sun) { _sunI0 = Sun.intensity; _sunC0 = Sun.color; }
        }

        /// <summary>Vérifie une fois si l'appareil prend en charge ARCore (sans ouvrir la caméra).</summary>
        public IEnumerator CheckAvailability()
        {
            if (Availability == ArAvailability.Supported || Availability == ArAvailability.Unsupported) yield break;
            Availability = ArAvailability.Checking;
#if UNITY_EDITOR
            Availability = ArAvailability.Supported; yield break;   // XR Simulation dans l'éditeur
#else
            if (ARSession.state == ARSessionState.None || ARSession.state == ARSessionState.CheckingAvailability) yield return ARSession.CheckAvailability();
            Availability = ARSession.state switch
            {
                ARSessionState.Unsupported => ArAvailability.Unsupported,
                ARSessionState.NeedsInstall => ArAvailability.NeedsInstall,
                _ => ArAvailability.Supported,
            };
#endif
        }

        public IEnumerator Begin()
        {
            yield return CheckAvailability();
            if (Availability == ArAvailability.NeedsInstall)
            {
                Say("Installation de « Services Google Play pour la RA »…");
                yield return ARSession.Install();
                Availability = ARSession.state == ARSessionState.Ready || ARSession.state >= ARSessionState.SessionInitializing ? ArAvailability.Supported : ArAvailability.Unsupported;
            }
            if (Availability == ArAvailability.Unsupported) { Say("Réalité augmentée non prise en charge par cet appareil."); yield break; }

            Origin.gameObject.SetActive(true); Session.gameObject.SetActive(true); Session.enabled = true;
            _ray = Origin.GetComponent<ARRaycastManager>(); _planes = Origin.GetComponent<ARPlaneManager>();
            _cam = Origin.Camera.GetComponent<ARCameraManager>(); _occ = Origin.Camera.GetComponent<AROcclusionManager>();
            if (_cam) _cam.frameReceived += OnFrame;
            if (_planes) _planes.enabled = true;
            SetOcclusion(Occlusion);
            if (!_reticle) MakeReticle();
            Running = true; Placed = false; Height = 0; Rotation = 0; _hasPose = false; _startT = Time.unscaledTime;
            var mv = ModelView.I; mv.Pivot.gameObject.SetActive(false); mv.ShowShadowBlob(true);
            EnhancedTouchSupport.Enable();
            Say("Activation de la caméra...");
            StateChanged?.Invoke();
        }

        public void End()
        {
            if (!Running) return;
            Running = false; Placed = false;
            if (_cam) _cam.frameReceived -= OnFrame;
            Session.enabled = false; Session.gameObject.SetActive(false); Origin.gameObject.SetActive(false);
            if (_reticle) _reticle.SetActive(false);
            var mv = ModelView.I; mv.Pivot.SetParent(null); mv.Pivot.gameObject.SetActive(true);
            mv.Pivot.position = Vector3.zero; mv.Pivot.rotation = Quaternion.identity; mv.Pivot.localScale = Vector3.one;
            if (Sun) { Sun.transform.rotation = _sunRot0; Sun.intensity = _sunI0; Sun.color = _sunC0; }
            RenderSettings.ambientIntensity = 1;
            StateChanged?.Invoke();
        }

        void MakeReticle()
        {
            _reticle = GameObject.CreatePrimitive(PrimitiveType.Quad); _reticle.name = "Réticule"; Destroy(_reticle.GetComponent<Collider>());
            var m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            m.SetFloat("_Surface", 1); m.SetOverrideTag("RenderType", "Transparent"); m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha); m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0); m.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always); m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.renderQueue = 4000;
            m.SetTexture("_BaseMap", Resources.Load<Texture2D>("Images/reticle"));
            var r = _reticle.GetComponent<MeshRenderer>(); r.sharedMaterial = m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _reticle.transform.localScale = Vector3.one * .24f; _reticle.SetActive(false);
        }

        void Update()
        {
            if (!Running) return;
            // cible au centre de l'écran sur les surfaces détectées
            var center = new Vector2(Screen.width / 2f, Screen.height / 2f);
            if (_ray && _ray.Raycast(center, _hits, TrackableType.PlaneWithinPolygon | TrackableType.PlaneEstimated))
            {
                _lastPose = _hits[0].pose; _hasPose = true; _lastHitT = Time.unscaledTime;
                _reticle.SetActive(true);
                _reticle.transform.SetPositionAndRotation(_lastPose.position + _lastPose.up * .003f, Quaternion.LookRotation(-_lastPose.up, Vector3.ProjectOnPlane(Origin.Camera.transform.forward, _lastPose.up).normalized + Vector3.up * 1e-4f));
                float dist = Vector3.Distance(Origin.Camera.transform.position, _lastPose.position); _reticle.transform.localScale = Vector3.one * Mathf.Clamp(dist * .09f, .08f, .5f);
            }
            else if (Time.unscaledTime - _lastHitT > .4f) { _hasPose = false; if (_reticle) _reticle.SetActive(false); }
            if (!Placed)
                Say(_hasPose ? "Visez une surface plane puis appuyez sur PLACER" : Time.unscaledTime - _startT > 6 ? "Aucune surface détectée. Déplacez lentement le téléphone vers le sol ou une table." : "Visez une surface plane pour placer la maquette");
            Gestures();
        }

        void Gestures()
        {
            var t = Touch.activeTouches;
            if (t.Count == 1 && t[0].phase == UnityEngine.InputSystem.TouchPhase.Ended && !IsOverUI(t[0].screenPosition) && t[0].time - t[0].startTime < .35f && !Placed && _hasPose) { Place(); return; }
            if (t.Count < 2 || !Placed) { _gesture = false; return; }
            if (IsOverUI(t[0].screenPosition) && !_gesture) return;
            var a = t[0].screenPosition; var b = t[1].screenPosition; float d = Vector2.Distance(a, b), ang = Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg;
            if (!_gesture) { _gesture = true; _g0d = d; _g0ang = ang; _g0s = Scale; _g0r = Rotation; return; }
            SetScale(_g0s * d / Mathf.Max(1, _g0d)); Rotation = _g0r + Mathf.DeltaAngle(_g0ang, ang); Apply();
        }

        void Say(string s) { if (s == _lastStatus) return; _lastStatus = s; Status?.Invoke(s); }

        public void Place()
        {
            if (!_hasPose) { Say("Aucune surface détectée. Déplacez lentement le téléphone vers le sol ou une table."); return; }
            _base = _lastPose.position;
            var cp = Origin.Camera.transform.position; _baseYaw = Mathf.Atan2(cp.x - _base.x, cp.z - _base.z) * Mathf.Rad2Deg;   // la façade « avant » regarde l'utilisateur
            Placed = true; ModelView.I.Pivot.gameObject.SetActive(true); Apply();
            Say($"Maquette placée · {Ratio(Scale)}");
#if UNITY_ANDROID && !UNITY_EDITOR
            Handheld.Vibrate();
#endif
            StateChanged?.Invoke();
        }
#if UNITY_EDITOR
        /// <summary>Éditeur : pose forcée 1,5 m devant la caméra, 1,2 m plus bas (tests sans détection de surfaces).</summary>
        public void DevForcePlace()
        {
            var c = Origin.Camera.transform; var f = Vector3.ProjectOnPlane(c.forward, Vector3.up); if (f.sqrMagnitude < 1e-4f) f = Vector3.forward;
            var p = c.position + f.normalized * 1.5f; p.y = c.position.y - 1.2f;
            _lastPose = new Pose(p, Quaternion.identity); _hasPose = true; _lastHitT = float.MaxValue / 4; Place();
        }
#endif
        public void ResetPlacement() { Placed = false; Height = 0; Rotation = 0; ModelView.I.Pivot.gameObject.SetActive(false); ModelView.I.Select(null); _lastStatus = null; StateChanged?.Invoke(); }
        public void SetScale(float s) { Scale = Mathf.Clamp(s, MinScale, 1); if (Running) Apply(); StateChanged?.Invoke(); }
        public void AddHeight(float dh) { Height = Mathf.Round((Height + dh) * 100) / 100; Apply(); StateChanged?.Invoke(); }
        public void Rotate(float deg) { Rotation += deg; Apply(); StateChanged?.Invoke(); }
        void Apply()
        {
            var p = ModelView.I.Pivot;
            p.position = _base + Vector3.up * Height; p.rotation = Quaternion.Euler(0, _baseYaw + Rotation, 0); p.localScale = Vector3.one * Scale;
        }

        public void SetOcclusion(bool on)
        {
            Occlusion = on;
            if (_occ) _occ.requestedEnvironmentDepthMode = on ? EnvironmentDepthMode.Fastest : EnvironmentDepthMode.Disabled;
        }
        public void SetPlanesVisible(bool v) { if (!_planes) return; foreach (var pl in _planes.trackables) pl.gameObject.SetActive(v); }

        /// <summary>Élément visé au centre de l'écran (INSPECTER).</summary>
        public GameObject Inspect()
        {
            if (!Placed) { Say("Placez d'abord la maquette pour l'inspecter."); return null; }
            var cam = Origin.Camera.transform;
            if (!ModelView.I.Pick(new Ray(cam.position, cam.forward), out var hit, 200)) { Say("Visez un élément de la maquette puis appuyez sur INSPECTER."); return null; }
            var el = ModelView.I.ElementOf(hit.collider.gameObject);
            ModelView.I.Select(el, "ar"); Say("Élément : " + ModelView.I.Label(el));
            return el;
        }

        /// <summary>Capture de l'écran AR (image caméra + maquette), sans l'interface ni le réticule.</summary>
        public IEnumerator Capture(Action hideUi, Action showUi, Action<Texture2D> done)
        {
            bool ret = _reticle && _reticle.activeSelf; if (_reticle) _reticle.SetActive(false); SetPlanesVisible(false);
            var sel = ModelView.I.Selected; if (sel) ModelView.I.Select(null);
            hideUi?.Invoke();
            yield return null; yield return new WaitForEndOfFrame();
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            showUi?.Invoke(); if (_reticle) _reticle.SetActive(ret); SetPlanesVisible(true); if (sel) ModelView.I.Select(sel, "ar");
            done?.Invoke(tex);
        }

        void OnFrame(ARCameraFrameEventArgs a)
        {
            if (!Sun) return;
            var le = a.lightEstimation;
            if (le.averageBrightness.HasValue) Sun.intensity = Mathf.Clamp(le.averageBrightness.Value * 2.2f, .35f, 1.8f);
            if (le.colorCorrection.HasValue) Sun.color = le.colorCorrection.Value;
            if (le.mainLightDirection.HasValue) Sun.transform.rotation = Quaternion.LookRotation(le.mainLightDirection.Value);
            if (le.averageBrightness.HasValue) RenderSettings.ambientIntensity = Mathf.Clamp(le.averageBrightness.Value * 1.6f, .4f, 1.4f);
        }
    }
}
