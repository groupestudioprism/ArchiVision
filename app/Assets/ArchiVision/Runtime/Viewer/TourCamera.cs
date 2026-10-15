// ARCHIVISION · caméra de visite : orbite (1 doigt tourne, 2 doigts zoom + déplacement), marche à hauteur d'yeux (joystick, suit les sols),
// immersion 360° (regarder autour, pincer pour zoomer, double toucher pour se déplacer). Souris et clavier dans l'éditeur.
using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

namespace StudioPrism.ArchiVision.Viewer
{
    public enum TourMode { Orbit, Walk, Pano }

    public class TourCamera : MonoBehaviour
    {
        public Camera Cam;
        public TourMode Mode { get; private set; } = TourMode.Orbit;
        public Func<Vector2, bool> IsOverUI = _ => false;      // fourni par l'interface
        public Action<Vector2> OnTap, OnDoubleTap;
        public Vector2 Joystick;                               // -1..1, fourni par l'interface (mode marche)
        public bool Moving { get; private set; }

        public const float Eye = 1.6f;
        Vector3 _target, _tTarget; float _dist = 20, _tDist = 20, _yaw = 45, _tYaw = 45, _pitch = 28, _tPitch = 28;
        float _lookYaw, _lookPitch, _fov = 50;
        Vector2 _lastPos; bool _dragging; float _downT; Vector2 _downPos; float _lastTap;
        float _pinchD; Vector2 _pinchMid; bool _pinch;
        Vector3? _flyFrom; Vector3 _flyTo; float _flyT;

        void OnEnable() { EnhancedTouchSupport.Enable(); }
        void OnDisable() { EnhancedTouchSupport.Disable(); }

        public void SetMode(TourMode m)
        {
            var prev = Mode; Mode = m;
            if (m == TourMode.Orbit)
            {
                Cam.fieldOfView = _fov = 50;
                if (prev != TourMode.Orbit) { var fwd = Cam.transform.forward; _target = _tTarget = Cam.transform.position + fwd * Mathf.Max(2f, ModelView.I.Radius * .5f); var e = Quaternion.LookRotation(_target - Cam.transform.position).eulerAngles; _yaw = _tYaw = e.y - 180; _pitch = _tPitch = NormA(e.x); _dist = _tDist = Vector3.Distance(Cam.transform.position, _target); }
                return;
            }
            Cam.fieldOfView = _fov = m == TourMode.Pano ? 75 : 60;
            if (prev == TourMode.Orbit)
            {
                var b = ModelView.I.Box; var inside = b.Contains(Cam.transform.position) && Cam.transform.position.y < b.max.y;
                if (!inside)
                {
                    Vector3 p;
                    if (m == TourMode.Walk) { var dir = Cam.transform.position - _target; dir.y = 0; if (dir.sqrMagnitude < 1e-4f) dir = Vector3.forward; p = b.center + dir.normalized * (Mathf.Max(b.extents.x, b.extents.z) + 3f); }
                    else p = new Vector3(b.center.x, 0, b.center.z);
                    p.y = FloorAt(p) + Eye; Cam.transform.position = p;
                }
                var f = new Vector3(b.center.x, Cam.transform.position.y, b.center.z) - Cam.transform.position; if (f.sqrMagnitude < 1e-3f) f = Vector3.forward;
                _lookYaw = Quaternion.LookRotation(f).eulerAngles.y; _lookPitch = 0; ApplyLook();
            }
        }
        static float NormA(float a) => a > 180 ? a - 360 : a;

        public void Frame(string view, bool instant = false)
        {
            if (Mode != TourMode.Orbit) SetMode(TourMode.Orbit);
            var mv = ModelView.I; var b = mv.Box; float r = mv.Radius;
            _tTarget = new Vector3(b.center.x, b.size.y * .5f, b.center.z);
            float fov = Cam.fieldOfView * Mathf.Deg2Rad; float aspect = Mathf.Max(.45f, Cam.aspect);
            _tDist = r / Mathf.Sin(fov / 2) * (aspect < 1 ? 1.15f / aspect : 1.05f);
            (_tYaw, _tPitch) = view switch { "top" => (0f, 89f), "front" => (0f, 8f), _ => (40f, 30f) };
            if (instant) { _target = _tTarget; _dist = _tDist; _yaw = _tYaw; _pitch = _tPitch; Apply(); }
        }
        public void Focus(Bounds b)
        {
            if (Mode != TourMode.Orbit) SetMode(TourMode.Orbit);
            _tTarget = b.center; _tDist = Mathf.Max(.8f, b.extents.magnitude) / Mathf.Sin(Cam.fieldOfView * Mathf.Deg2Rad / 2) * 1.1f;
        }
        public void MoveTo(Vector3 point, Vector3 normal)
        {
            Vector3 q = point;
            if (normal.y > .6f) q.y += Eye; else { var back = (Cam.transform.position - point); back.y = 0; q += back.normalized * .6f; q.y = FloorAt(q) + Eye; }
            _flyFrom = Cam.transform.position; _flyTo = q; _flyT = 0;
        }

        void Update()
        {
            if (!Cam || !Cam.isActiveAndEnabled || ModelView.I?.Root == null) return;
            HandleInput();
            float k = 1 - Mathf.Exp(-Time.unscaledDeltaTime * 12);
            if (Mode == TourMode.Orbit)
            {
                var before = Cam.transform.position;
                _target = Vector3.Lerp(_target, _tTarget, k); _dist = Mathf.Lerp(_dist, _tDist, k); _yaw = Mathf.LerpAngle(_yaw, _tYaw, k); _pitch = Mathf.Lerp(_pitch, _tPitch, k);
                Apply();
                Moving = (Cam.transform.position - before).sqrMagnitude > 1e-8f || _dragging;
            }
            else
            {
                Moving = _dragging || Joystick.sqrMagnitude > .002f || _flyFrom.HasValue;
                if (_flyFrom.HasValue) { _flyT = Mathf.Min(1, _flyT + Time.unscaledDeltaTime / .38f); Cam.transform.position = Vector3.Lerp(_flyFrom.Value, _flyTo, _flyT * (2 - _flyT)); if (_flyT >= 1) _flyFrom = null; }
                if (Mode == TourMode.Walk) Walk(Time.unscaledDeltaTime);
                Cam.fieldOfView = Mathf.Lerp(Cam.fieldOfView, _fov, k);
            }
        }
        void Apply()
        {
            var rot = Quaternion.Euler(_pitch, _yaw + 180, 0);
            Cam.transform.position = _target - rot * Vector3.forward * _dist;
            Cam.transform.rotation = rot;
            float r = ModelView.I ? ModelView.I.Radius : 10; Cam.nearClipPlane = Mathf.Clamp(_dist / 400f, 0.03f, 1f); Cam.farClipPlane = Mathf.Max(200, r * 60);
        }
        void ApplyLook() { Cam.transform.rotation = Quaternion.Euler(_lookPitch, _lookYaw, 0); Cam.nearClipPlane = 0.05f; }

        float _floorT;
        void Walk(float dt)
        {
            Vector2 mv = Joystick;
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.wKey.isPressed || kb.zKey.isPressed || kb.upArrowKey.isPressed) mv.y -= 1; if (kb.sKey.isPressed || kb.downArrowKey.isPressed) mv.y += 1;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) mv.x += 1; if (kb.aKey.isPressed || kb.qKey.isPressed || kb.leftArrowKey.isPressed) mv.x -= 1;
            }
            if (mv.sqrMagnitude < .0025f) return;
            float run = kb != null && kb.leftShiftKey.isPressed ? 2.5f : 1f, v = 1.4f * run * dt * Mathf.Min(1, mv.magnitude * 1.2f);
            var fw = Quaternion.Euler(0, _lookYaw, 0) * Vector3.forward; var rt = Quaternion.Euler(0, _lookYaw, 0) * Vector3.right;
            var d = (fw * -mv.y + rt * mv.x); if (d.sqrMagnitude > 1) d.Normalize();
            Cam.transform.position += d * v;
            _floorT += dt;
            if (_floorT > .09f) { _floorT = 0; var p = Cam.transform.position; float want = FloorAt(p, p.y - Eye + .45f) + Eye; p.y += (want - p.y) * .5f; Cam.transform.position = p; }
        }
        /// <summary>Altitude du sol sous un point : premier élément sous les yeux (sols, escaliers), sinon 0.</summary>
        public float FloorAt(Vector3 p, float from = float.NaN)
        {
            float y0 = float.IsNaN(from) ? p.y + .6f : from;
            return ModelView.I.Pick(new Ray(new Vector3(p.x, y0, p.z), Vector3.down), out var h, 50) ? h.point.y : 0;
        }

        // ------------------------------------------------------------ Entrées
        void HandleInput()
        {
            var touches = Touch.activeTouches;
            if (touches.Count > 0) { TouchInput(touches); return; }
            var m = Mouse.current; if (m == null) return;
            Vector2 pos = m.position.ReadValue();
            if (m.leftButton.wasPressedThisFrame || m.rightButton.wasPressedThisFrame) { if (IsOverUI(pos)) return; _dragging = true; _lastPos = _downPos = pos; _downT = Time.unscaledTime; }
            if (_dragging && (m.leftButton.isPressed || m.rightButton.isPressed))
            {
                var d = pos - _lastPos; _lastPos = pos;
                if (m.rightButton.isPressed && Mode == TourMode.Orbit) Pan(d); else Rotate(d);
            }
            if (_dragging && (m.leftButton.wasReleasedThisFrame || m.rightButton.wasReleasedThisFrame)) { _dragging = false; if ((pos - _downPos).magnitude < 6 && Time.unscaledTime - _downT < .5f) Tap(pos); }
            float s = m.scroll.ReadValue().y; if (Mathf.Abs(s) > .01f && !IsOverUI(pos)) Zoom(s > 0 ? .88f : 1.14f);
        }
        void TouchInput(UnityEngine.InputSystem.Utilities.ReadOnlyArray<Touch> t)
        {
            if (t.Count == 1)
            {
                var a = t[0];
                if (a.phase == UnityEngine.InputSystem.TouchPhase.Began) { if (IsOverUI(a.screenPosition)) { _dragging = false; return; } _dragging = true; _pinch = false; _lastPos = _downPos = a.screenPosition; _downT = Time.unscaledTime; }
                else if (_dragging && a.phase == UnityEngine.InputSystem.TouchPhase.Moved) { var d = a.screenPosition - _lastPos; _lastPos = a.screenPosition; Rotate(d); }
                else if (_dragging && (a.phase == UnityEngine.InputSystem.TouchPhase.Ended || a.phase == UnityEngine.InputSystem.TouchPhase.Canceled))
                { _dragging = false; if (a.phase == UnityEngine.InputSystem.TouchPhase.Ended && (a.screenPosition - _downPos).magnitude < 14 * App.Dpi / 160f && Time.unscaledTime - _downT < .5f) Tap(a.screenPosition); }
            }
            else if (t.Count >= 2)
            {
                if (IsOverUI(t[0].screenPosition) && !_pinch) return;
                _dragging = false;
                var p0 = t[0].screenPosition; var p1 = t[1].screenPosition; float d = Vector2.Distance(p0, p1); var mid = (p0 + p1) / 2;
                if (!_pinch) { _pinch = true; _pinchD = d; _pinchMid = mid; return; }
                if (d > 1 && _pinchD > 1) Zoom(_pinchD / d);
                if (Mode == TourMode.Orbit) Pan(mid - _pinchMid);
                _pinchD = d; _pinchMid = mid;
                if (t[0].phase == UnityEngine.InputSystem.TouchPhase.Ended || t[1].phase == UnityEngine.InputSystem.TouchPhase.Ended) _pinch = false;
            }
        }
        void Tap(Vector2 pos)
        {
            bool dbl = Time.unscaledTime - _lastTap < .32f; _lastTap = Time.unscaledTime;
            if (dbl) OnDoubleTap?.Invoke(pos); else OnTap?.Invoke(pos);
        }
        void Rotate(Vector2 d)
        {
            float dpiK = 160f / Mathf.Max(60, App.Dpi);
            if (Mode == TourMode.Orbit) { _tYaw += d.x * .32f * dpiK; _tPitch = Mathf.Clamp(_tPitch - d.y * .25f * dpiK, -5f, 89f); }
            else { float k = Cam.fieldOfView / 60f * .22f * dpiK; _lookYaw -= d.x * k; _lookPitch = Mathf.Clamp(_lookPitch + d.y * k, -80, 80); ApplyLook(); }
        }
        void Pan(Vector2 d)
        {
            float k = _dist * Mathf.Tan(Cam.fieldOfView * Mathf.Deg2Rad / 2) * 2 / Mathf.Max(1, Screen.height);
            _tTarget -= (Cam.transform.right * d.x + Cam.transform.up * d.y) * k; _target = _tTarget;
        }
        void Zoom(float f)
        {
            if (Mode == TourMode.Orbit) { float r = ModelView.I.Radius; _tDist = Mathf.Clamp(_tDist * f, Mathf.Min(.3f, r / 20), r * 12); }
            else _fov = Mathf.Clamp(_fov * f, 25, 100);
        }
        public void SetLook(float yaw, float pitch) { _lookYaw = yaw; _lookPitch = pitch; ApplyLook(); }
    }
}
