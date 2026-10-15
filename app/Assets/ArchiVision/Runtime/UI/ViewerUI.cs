// ARCHIVISION · visite 3D : barre du haut, outils (orbite, marche, 360°, inspecter, mesure, coupe, recentrer), styles d'affichage,
// panneau (Maquette, Données, Galerie, Infos), mesures, coupe, joystick, captures.
using System;
using System.Collections.Generic;
using System.Linq;
using StudioPrism.ArchiVision.AR;
using StudioPrism.ArchiVision.Services;
using StudioPrism.ArchiVision.Viewer;
using UnityEngine;
using UnityEngine.UIElements;
using static StudioPrism.ArchiVision.UI.K;

namespace StudioPrism.ArchiVision
{
    public partial class App
    {
        enum Tool { Orbit, Walk, Pano, Inspect, Measure, Section }
        Tool _tool = Tool.Orbit;
        VisualElement _vp, _tools, _sheet, _pbody, _ptabs, _joy, _joyKnob, _secBar, _labels, _styles, _cams;
        Label _badge, _prompt, _vtSub, _secVal;
        bool _sheetOpen; string _ptab = "tree";
        readonly List<(Vector3 a, Vector3 b, float d, LineRenderer line, Label lab)> _measures = new List<(Vector3, Vector3, float, LineRenderer, Label)>();
        Vector3? _mPending; GameObject _mDot, _measModel;

        public void ShowViewer(bool startAr)
        {
            if (!_mv.Root) { ShowHome(); return; }
            tourCamera.enabled = true;
            var scr = El("screen clear"); scr.pickingMode = PickingMode.Ignore;
            // barre du haut
            var top = El("viewer-top");
            top.Add(IconBtn("back", ShowHome, null, "Accueil"));
            var tt = El("vt-title"); tt.Add(L(_mv.Meta?.Name ?? "Maquette", "n")); _vtSub = L($"Visite 3D · {Fr.N(_mv.Size.x, 1)} × {Fr.N(_mv.Size.z, 1)} × {Fr.N(_mv.Size.y, 1)} m", "s"); tt.Add(_vtSub); top.Add(tt);
            top.Add(IconBtn("camera", () => Capture(), null, "Capture"));
            if (!_wide) top.Add(IconBtn("panel", () => OpenSheet(!_sheetOpen), null, "Panneau"));
            var arb = Btn("AR", EnterAR, "grad ar-cta", "ar"); top.Add(arb);
            scr.Add(top);
            // zone de la vue 3D (transparente aux gestes)
            _vp = El("vp-area"); _vp.userData = "viewport"; _vp.pickingMode = PickingMode.Ignore;
            var pad = El("grow vp-pad"); pad.userData = "viewport"; pad.pickingMode = PickingMode.Ignore; _vp.Add(pad);
            _labels = El(); _labels.style.position = Position.Absolute; _labels.style.left = _labels.style.top = _labels.style.right = _labels.style.bottom = 0; _labels.pickingMode = PickingMode.Ignore; pad.Add(_labels);
            if (_measModel != _mv.Root) { ClearMeasures(); _measModel = _mv.Root; } else foreach (var m in _measures) _labels.Add(m.lab);
            _styles = El("floating styles");
            foreach (var (st, col, tip) in new[] { (ViewStyle.Materials, "#C96F4E", "Matériaux"), (ViewStyle.Shaded, "#B8C0C8", "Ombré"), (ViewStyle.Wire, "#1F5A86", "Filaire"), (ViewStyle.XRay, "#7FD0C4", "Rayons X") })
            {
                var s = st; var b = new Button(() => { _mv.SetStyle(s); RefreshStyleButtons(); }) { tooltip = tip }; b.Cls("style-btn"); b.userData = s;
                var sw = El("swatch"); ColorUtility.TryParseHtmlString(col, out var c); sw.style.backgroundColor = s == ViewStyle.Wire ? Color.clear : c;
                if (s == ViewStyle.Wire) { sw.style.borderTopWidth = sw.style.borderBottomWidth = sw.style.borderLeftWidth = sw.style.borderRightWidth = 2; sw.style.borderTopColor = sw.style.borderBottomColor = sw.style.borderLeftColor = sw.style.borderRightColor = c; }
                if (s == ViewStyle.XRay) sw.style.opacity = .6f;
                b.Add(sw); _styles.Add(b);
            }
            pad.Add(_styles);
            _cams = El("floating cams");
            foreach (var (k, lab) in new[] { ("iso", "3D"), ("top", "Dessus"), ("front", "Face") }) { var kk = k; var b = new Button(() => { SetTool(Tool.Orbit); _tour.Frame(kk); }) { text = lab }; b.Cls("cam-btn"); _cams.Add(b); }
            pad.Add(_cams);
            _badge = L("", "badge"); _badge.Show(false); pad.Add(_badge);
            _prompt = L("", "prompt"); _prompt.pickingMode = PickingMode.Ignore; pad.Add(_prompt);
            _secBar = El("floating secbar"); _secBar.Show(false);
            _secBar.Add(L("Coupe", "t-bold t-small")); var sl = new Slider(0, 1) { value = .45f }; sl.Cls("panel-slider grow"); sl.style.marginLeft = 10; sl.style.marginRight = 10;
            var sv = L("—", "t-mono t-small"); _secVal = sv; sv.style.minWidth = 60;
            sl.RegisterValueChangedCallback(e => { var h = e.newValue * _mv.Size.y; _mv.SetSection(h); sv.text = Fr.M(h); });
            _secBar.Add(sl); _secBar.Add(sv); _secBar.Add(IconBtn("close", () => { _mv.SetSection(null); SetTool(Tool.Orbit); }));
            pad.Add(_secBar);
            _joy = El("joy"); _joyKnob = El("joy-knob"); _joy.Add(_joyKnob); _joy.Show(false); WireJoystick(); pad.Add(_joy);
            scr.Add(_vp);
            // outils
            _tools = El("tools");
            foreach (var (t, ico, lab) in new[] { (Tool.Orbit, "orbit", "Orbite"), (Tool.Walk, "walk", "Marche"), (Tool.Pano, "pano", "360°"), (Tool.Inspect, "inspect", "Inspecter"), (Tool.Measure, "measure", "Mesure"), (Tool.Section, "section", "Coupe") })
            {
                var tt2 = t; var b = new Button(() => SetTool(tt2)) { text = "" }; b.Cls("tool"); b.userData = t; b.Add(Icon(ico)); b.Add(L(lab)); _tools.Add(b);
            }
            var fit = new Button(() => { SetTool(Tool.Orbit); _tour.Frame("iso"); }) { text = "" }; fit.Cls("tool"); fit.Add(Icon("fit")); fit.Add(L("Recentrer")); _tools.Add(fit);
            scr.Add(_tools);
            // panneau
            _sheet = El("sheet"); _sheet.Add(El("sheet-grip"));
            _sheet.Q(className: "sheet-grip").RegisterCallback<ClickEvent>(_ => OpenSheet(false));
            _ptabs = El("ptabs");
            foreach (var (k, lab) in new[] { ("tree", "Maquette"), ("props", "Données"), ("gallery", "Galerie"), ("info", "Infos") }) { var kk = k; var b = new Button(() => { _ptab = kk; RenderPanel(); }) { text = lab }; b.Cls("ptab"); b.userData = k; _ptabs.Add(b); }
            _sheet.Add(_ptabs);
            var ps = Scroll("pbody"); _pbody = El("pbody-in"); ps.Add(_pbody); _sheet.Add(ps);
            scr.Add(_sheet);
            ShowScreen("viewer", scr);
            _sheetOpen = _wide; _sheet.EnableInClassList("open", _sheetOpen);
            _mv.SelectionChanged -= OnSelection; _mv.SelectionChanged += OnSelection;
            _treeOpen.Clear(); _treeQ = "";
            RefreshStyleButtons(); SetTool(Tool.Orbit); _tour.Frame("iso", true); RenderPanel(); FitCameraRect();
            if (startAr) _root.schedule.Execute(EnterAR).StartingIn(250);
        }
        partial void OnLayoutChanged() { if (_screen == "viewer" && !_ar.Running) ShowViewer(false); else if (_screen == "home") ShowHome(); }

        /// <summary>Tablette : la vue 3D occupe la zone libre entre le rail d'outils et le panneau.</summary>
        void FitCameraRect()
        {
            if (!_wide || _ar.Running) { tourCamera.rect = new Rect(0, 0, 1, 1); return; }
            float px = Dpi / 160f;
            float l = 72 * px, r = 360 * px, t = (58 * px) + (Screen.height - Screen.safeArea.yMax);
            tourCamera.rect = new Rect(l / Screen.width, 0, Mathf.Max(.2f, (Screen.width - l - r) / Screen.width), Mathf.Max(.2f, (Screen.height - t) / Screen.height));
        }

        void RefreshStyleButtons() { if (_styles == null) return; foreach (var b in _styles.Children()) b.EnableInClassList("on", b.userData is ViewStyle s && s == _mv.Style); }

        void OpenSheet(bool open) { _sheetOpen = open || _wide; _sheet?.EnableInClassList("open", _sheetOpen); }

        static readonly Dictionary<Tool, string> Hints = new Dictionary<Tool, string>
        {
            { Tool.Orbit, "Orbite : glissez pour tourner, pincez pour zoomer, deux doigts pour déplacer. Touchez un élément pour voir ses données." },
            { Tool.Walk, "Marche : glissez pour regarder, joystick pour avancer. La caméra suit les sols et les escaliers. Touchez deux fois un point pour vous y rendre." },
            { Tool.Pano, "Immersion 360° : glissez pour regarder autour de vous, pincez pour zoomer. Touchez deux fois le sol pour vous y déplacer." },
            { Tool.Inspect, "Inspecter : touchez un élément de la maquette pour afficher ses données BIM." },
            { Tool.Measure, "Mesure : touchez deux points de la maquette." },
            { Tool.Section, "Coupe : réglez la hauteur ; les éléments situés au-dessus sont masqués." },
        };
        void SetTool(Tool t)
        {
            var prev = _tool; _tool = t;
            if (_tools != null) foreach (var b in _tools.Children()) b.EnableInClassList("on", b.userData is Tool x && x == t);
            _tour.SetMode(t == Tool.Walk ? TourMode.Walk : t == Tool.Pano ? TourMode.Pano : TourMode.Orbit);
            _joy?.Show(t == Tool.Walk);
            _secBar?.Show(t == Tool.Section || _mv.SectionHeight.HasValue);
            _cams?.Show(t != Tool.Section);
            if (t == Tool.Section && !_mv.SectionHeight.HasValue) { var s = _secBar.Q<Slider>(); var h = s.value * _mv.Size.y; _mv.SetSection(h); if (_secVal != null) _secVal.text = Fr.M(h); }
            if (prev == Tool.Measure && t != Tool.Measure) { _mPending = null; if (_mDot) _mDot.SetActive(false); }
            if (_prompt != null) { _prompt.text = Hints[t]; _prompt.Show(true); _prompt.schedule.Execute(() => { if (_tool == t) _prompt.Show(false); }).StartingIn(6000); }
            if (t == Tool.Walk) Toast("Marche : la caméra est à hauteur d'yeux (1,60 m)");
        }

        void WireJoystick()
        {
            int id = -1; Vector2 c = default;
            _joy.RegisterCallback<PointerDownEvent>(e => { id = e.pointerId; _joy.CapturePointer(id); c = _joy.worldBound.center; Move(e.position); e.StopPropagation(); });
            _joy.RegisterCallback<PointerMoveEvent>(e => { if (e.pointerId == id) Move(e.position); });
            void End(int pid) { if (pid != id) return; _joy.ReleasePointer(id); id = -1; _joyKnob.style.translate = new Translate(0, 0); _tour.Joystick = Vector2.zero; }
            _joy.RegisterCallback<PointerUpEvent>(e => End(e.pointerId)); _joy.RegisterCallback<PointerCancelEvent>(e => End(e.pointerId));
            void Move(Vector2 p) { float R = _joy.resolvedStyle.width / 2; var d = p - c; if (d.magnitude > R) d = d.normalized * R; _joyKnob.style.translate = new Translate(d.x, d.y); _tour.Joystick = d / R; _lastInteraction = Time.unscaledTime; }
        }

        // ------------------------------------------------------------ Toucher dans la vue
        void OnViewTap(Vector2 pos)
        {
            if (_screen != "viewer" || _ar.Running) return;
            var ray = tourCamera.ScreenPointToRay(pos);
            bool hit = _mv.Pick(ray, out var h);
            if (_tool == Tool.Measure) { if (hit) AddMeasure(h.point); return; }
            if (hit) { _mv.Select(_mv.ElementOf(h.collider.gameObject)); if (_tool == Tool.Inspect || !_wide) { _ptab = "props"; RenderPanel(); OpenSheet(true); } }
            else _mv.Select(null);
        }
        void OnViewDoubleTap(Vector2 pos)
        {
            if (_screen != "viewer" || _ar.Running) return;
            if ((_tool == Tool.Pano || _tool == Tool.Walk) && _mv.Pick(tourCamera.ScreenPointToRay(pos), out var h)) _tour.MoveTo(h.point, h.normal);
            else OnViewTap(pos);
        }
        void OnSelection(GameObject g, string from)
        {
            if (_badge != null) { _badge.text = g ? _mv.Label(g) : ""; _badge.Show(g != null && _screen == "viewer"); }
            if (from != "tree" && g) for (var t = g.transform.parent; t && _mv.Root && t != _mv.Root.transform; t = t.parent) _treeOpen.Add(t.gameObject);
            if (_screen == "viewer" && (_ptab == "props" || (_ptab == "tree" && from != "tree"))) RenderPanel();
            if (_ar.Running) RenderArCard(g);
        }

        // ------------------------------------------------------------ Mesures
        void AddMeasure(Vector3 p)
        {
            if (!_mDot) { _mDot = GameObject.CreatePrimitive(PrimitiveType.Sphere); Destroy(_mDot.GetComponent<Collider>()); _mDot.GetComponent<MeshRenderer>().sharedMaterial = LineMat(); }
            if (!_mPending.HasValue) { _mPending = p; _mDot.SetActive(true); _mDot.transform.position = p; _mDot.transform.localScale = Vector3.one * Mathf.Max(.04f, _mv.Radius / 250); ShowPrompt("Mesure : touchez le second point."); return; }
            var a = _mPending.Value; _mPending = null; _mDot.SetActive(false);
            var go = new GameObject("Mesure"); var lr = go.AddComponent<LineRenderer>(); lr.useWorldSpace = true; lr.positionCount = 2; lr.SetPositions(new[] { a, p });
            lr.widthMultiplier = Mathf.Max(.012f, _mv.Radius / 600); lr.sharedMaterial = LineMat(); lr.numCapVertices = 4;
            var d = Vector3.Distance(a, p); var lab = L(Fr.M(d), "measure-label"); lab.pickingMode = PickingMode.Ignore; _labels.Add(lab);
            _measures.Add((a, p, d, lr, lab));
            ShowPrompt($"Mesure : {Fr.M(d)} · horizontale {Fr.M(Vector2.Distance(new Vector2(a.x, a.z), new Vector2(p.x, p.z)))} · verticale {Fr.M(Mathf.Abs(p.y - a.y))}", 9000);
        }
        Material _lineMat;
        Material LineMat() { if (!_lineMat) { _lineMat = new Material(Shader.Find("Universal Render Pipeline/Unlit")); _lineMat.SetColor("_BaseColor", new Color(.18f, .83f, .71f)); } return _lineMat; }
        void ClearMeasures() { foreach (var m in _measures) { if (m.line) Destroy(m.line.gameObject); m.lab.RemoveFromHierarchy(); } _measures.Clear(); _mPending = null; if (_mDot) _mDot.SetActive(false); }
        void ShowPrompt(string s, int ms = 6000) { if (_prompt == null) return; _prompt.text = s; _prompt.Show(true); var t = s; _prompt.schedule.Execute(() => { if (_prompt.text == t) _prompt.Show(false); }).StartingIn(ms); }

        /// <summary>Chaque image : étiquettes des mesures à l'écran.</summary>
        void UpdateViewerFrame()
        {
            if (_screen != "viewer" || _labels == null || _measures.Count == 0) return;
            var cam = _ar.Running ? (Camera)xrOrigin.Camera : tourCamera; var panel = _root.panel; if (panel == null) return;
            foreach (var m in _measures)
            {
                var sp = cam.WorldToScreenPoint((m.a + m.b) / 2); bool vis = sp.z > 0; m.lab.Show(vis);
                if (!vis) continue;
                var pp = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(sp.x, Screen.height - sp.y)); var o = _labels.worldBound.position;
                m.lab.style.left = pp.x - o.x; m.lab.style.top = pp.y - o.y;
            }
        }

        // ------------------------------------------------------------ Capture
        public void Capture()
        {
            if (!_mv.Root) return;
            if (_ar.Running) { StartCoroutine(_ar.Capture(() => _root.style.display = DisplayStyle.None, () => _root.style.display = DisplayStyle.Flex, t => SavePhoto(t, "ar"))); return; }
            var sel = _mv.Selected; if (sel) _mv.Select(null);
            int w = Mathf.Min(Screen.width * 2, 3840), h = Mathf.Min(Screen.height * 2, 3840);
            var rect = tourCamera.rect; tourCamera.rect = new Rect(0, 0, 1, 1);
            var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32); rt.antiAliasing = 4;
            tourCamera.targetTexture = rt; tourCamera.Render(); tourCamera.targetTexture = null; tourCamera.rect = rect;
            var a = RenderTexture.active; RenderTexture.active = rt; var t2 = new Texture2D(w, h, TextureFormat.RGB24, false); t2.ReadPixels(new Rect(0, 0, w, h), 0, 0); t2.Apply(); RenderTexture.active = a;
            RenderTexture.ReleaseTemporary(rt);
            if (sel) _mv.Select(sel);
            SavePhoto(t2, "tour");
            var fl = El("flash"); fl.pickingMode = PickingMode.Ignore; _root.Add(fl); fl.style.opacity = .85f;
            fl.schedule.Execute(() => { fl.style.transitionDuration = new List<TimeValue> { new TimeValue(.4f) }; fl.style.transitionProperty = new List<StylePropertyName> { "opacity" }; fl.style.opacity = 0; }).StartingIn(30);
            fl.schedule.Execute(() => fl.RemoveFromHierarchy()).StartingIn(500);
        }
        void SavePhoto(Texture2D t, string mode)
        {
            try { Library.AddPhoto(t, _mv.Meta?.Id ?? "sample", _mv.Meta?.Name ?? "Maquette", mode); Toast("Capture enregistrée dans la galerie ArchiVision."); if (_ptab == "gallery" && !_ar.Running) RenderPanel(); }
            catch (Exception e) { Debug.LogException(e); Toast("ÉCHEC - Échec de la capture : " + e.Message); }
            finally { Destroy(t); }
        }
    }
}
