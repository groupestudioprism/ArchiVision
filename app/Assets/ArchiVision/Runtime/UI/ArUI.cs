// ARCHIVISION · écran de réalité augmentée : message d'aide, échelle (curseur + 1:1 à 1:200), hauteur, rotation, occlusion,
// RÉINITIALISER / PLACER (DÉPLACER ICI) / CAPTURE / INSPECTER, fiche de l'élément visé.
using System.Collections;
using System.Collections.Generic;
using StudioPrism.ArchiVision.AR;
using UnityEngine;
using UnityEngine.UIElements;
using static StudioPrism.ArchiVision.UI.K;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace StudioPrism.ArchiVision
{
    public partial class App
    {
        VisualElement _arUi, _arCard, _arAdjust, _arCross;
        Label _arStatus, _arScaleV, _arHeightV, _arRotV;
        Slider _arSlider; Button _arPlace, _arOcc, _arPlanes, _arAdjBtn;
        readonly List<Button> _arPresets = new List<Button>();
        bool _arStarting, _arAdjOpen, _arPlanesOn = true;
        static readonly int[] ArRatios = { 1, 20, 50, 100, 200 };

        public void EnterAR()
        {
            if (!_mv.Root || _ar.Running || _arStarting) return;
            if (ARController.Availability == ArAvailability.Unsupported) { Toast("La réalité augmentée n'est pas prise en charge par cet appareil."); return; }
            StartCoroutine(EnterArCo());
        }

        IEnumerator EnterArCo()
        {
            _arStarting = true;
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Permission.HasUserAuthorizedPermission(Permission.Camera))
            {
                bool done = false, ok = false; var cb = new PermissionCallbacks();
                cb.PermissionGranted += _ => { ok = true; done = true; }; cb.PermissionDenied += _ => done = true;
                Permission.RequestUserPermission(Permission.Camera, cb);
                float t0 = Time.unscaledTime; while (!done && Time.unscaledTime - t0 < 60) yield return null;
                if (!ok) { _arStarting = false; Toast("Autorisez l'accès à la caméra pour utiliser la réalité augmentée.", 4); yield break; }
            }
#endif
            Busy("Démarrage de la réalité augmentée…");
            ClearMeasures(); _mv.Select(null); SetTool(Tool.Orbit);
            _ar.SetScale(DefaultArScale());
            _ar.Status -= OnArStatus; _ar.Status += OnArStatus;
            _ar.StateChanged -= RefreshArUi; _ar.StateChanged += RefreshArUi;
            tourCamera.enabled = false; _tour.enabled = false;
            BuildArUi();
            yield return _ar.Begin();
            Busy(null); _arStarting = false;
            if (!_ar.Running)
            {
                _ar.Status -= OnArStatus; _ar.StateChanged -= RefreshArUi; _tour.enabled = true;
                Toast(ARController.Availability == ArAvailability.Unsupported ? "La réalité augmentée n'est pas prise en charge par cet appareil." : "Impossible de démarrer la réalité augmentée.", 4);
                ShowViewer(false); yield break;
            }
            RefreshArUi();
        }

        /// <summary>Échelle de départ : la maquette tient sur une table (≈ 60 cm) ; les petits objets restent à 1:1.</summary>
        float DefaultArScale()
        {
            float big = Mathf.Max(_mv.Size.x, Mathf.Max(_mv.Size.y, _mv.Size.z));
            if (big <= 1.5f) return 1;
            float want = .6f / big; float best = 1f / ArRatios[ArRatios.Length - 1];
            foreach (var r in ArRatios) { float s = 1f / r; if (s <= want * 1.6f) { best = s; break; } }
            return best;
        }

        /// <summary>Quitte la RA ; revient à la visite 3D sauf si un autre écran prend la suite.</summary>
        public void ExitAR(bool toViewer)
        {
            _ar.Status -= OnArStatus; _ar.StateChanged -= RefreshArUi;
            _ar.End(); _tour.enabled = true; _arUi = null; _arCard = null;
            _mv.Select(null);
            if (toViewer) ShowViewer(false);
        }
        public void ExitAR() => ExitAR(true);

        void OnArStatus(string s) { if (_arStatus != null) _arStatus.text = s; }

        void BuildArUi()
        {
            var scr = El("screen clear ar-ui"); scr.pickingMode = PickingMode.Ignore; _arUi = scr;
            scr.style.position = Position.Absolute; scr.style.left = scr.style.top = scr.style.right = scr.style.bottom = 0;
            // haut : message + fermer
            var top = El("ar-top"); _arStatus = L("Activation de la caméra...", "hud-chip"); top.Add(_arStatus);
            var x = new Button(() => ExitAR()) { text = "" }; x.Cls("ar-x"); x.Add(Icon("close", "white")); top.Add(x);
            scr.Add(top);
            _arCard = El("ar-card"); _arCard.Show(false); scr.Add(_arCard);

            // viseur central (INSPECTER vise ce point)
            _arCross = El(); _arCross.pickingMode = PickingMode.Ignore; _arCross.style.position = Position.Absolute;
            _arCross.style.left = new Length(50, LengthUnit.Percent); _arCross.style.top = new Length(50, LengthUnit.Percent);
            _arCross.style.width = _arCross.style.height = 22; _arCross.style.marginLeft = _arCross.style.marginTop = -11;
            _arCross.style.borderTopLeftRadius = _arCross.style.borderTopRightRadius = _arCross.style.borderBottomLeftRadius = _arCross.style.borderBottomRightRadius = 11;
            _arCross.style.borderTopWidth = _arCross.style.borderBottomWidth = _arCross.style.borderLeftWidth = _arCross.style.borderRightWidth = 2;
            var cc = new Color(1, 1, 1, .9f); _arCross.style.borderTopColor = _arCross.style.borderBottomColor = _arCross.style.borderLeftColor = _arCross.style.borderRightColor = cc;
            var dot = El(); dot.style.width = dot.style.height = 4; dot.style.backgroundColor = cc; dot.style.alignSelf = Align.Center; dot.style.marginTop = 7;
            dot.style.borderTopLeftRadius = dot.style.borderTopRightRadius = dot.style.borderBottomLeftRadius = dot.style.borderBottomRightRadius = 2; _arCross.Add(dot);
            scr.Add(_arCross);

            // réglages
            _arAdjust = El("ar-adjust");
            var r1 = El("ar-row"); r1.Add(L("Échelle"));
            _arSlider = new Slider(0, 1) { value = ScaleToSlider(_ar.Scale) }; _arSlider.Cls("av-slider");
            _arSlider.RegisterValueChangedCallback(e => _ar.SetScale(SliderToScale(e.newValue)));
            r1.Add(_arSlider); _arScaleV = L("", "ar-val"); r1.Add(_arScaleV); _arAdjust.Add(r1);
            var r2 = El("ar-row"); r2.style.justifyContent = Justify.Center; _arPresets.Clear();
            foreach (var r in ArRatios) { var rr = r; var b = new Button(() => _ar.SetScale(1f / rr)) { text = "1:" + r }; b.Cls("ar-step"); b.userData = r; _arPresets.Add(b); r2.Add(b); }
            _arAdjust.Add(r2);
            var r3 = El("ar-row"); r3.Add(L("Hauteur"));
            var hb = El(); hb.style.flexDirection = FlexDirection.Row; hb.style.alignItems = Align.Center;
            hb.Add(StepBtn("minus", () => _ar.AddHeight(-.05f))); _arHeightV = L("", "ar-val"); hb.Add(_arHeightV); hb.Add(StepBtn("plus", () => _ar.AddHeight(.05f)));
            r3.Add(hb); _arAdjust.Add(r3);
            var r4 = El("ar-row"); r4.Add(L("Rotation"));
            var rb = El(); rb.style.flexDirection = FlexDirection.Row; rb.style.alignItems = Align.Center;
            rb.Add(StepBtn("rotl", () => _ar.Rotate(-15))); _arRotV = L("", "ar-val"); rb.Add(_arRotV); rb.Add(StepBtn("rotr", () => _ar.Rotate(15)));
            r4.Add(rb); _arAdjust.Add(r4);
            var r5 = El("ar-row"); r5.style.justifyContent = Justify.Center;
            _arOcc = new Button(() => { _ar.SetOcclusion(!_ar.Occlusion); RefreshArUi(); Toast(_ar.Occlusion ? "Occlusion activée : les objets réels peuvent masquer la maquette (selon l'appareil)." : "Occlusion désactivée."); }) { text = "Occlusion" }; _arOcc.Cls("ar-step");
            _arPlanes = new Button(() => { _arPlanesOn = !_arPlanesOn; _ar.SetPlanesVisible(_arPlanesOn); RefreshArUi(); }) { text = "Surfaces" }; _arPlanes.Cls("ar-step");
            r5.Add(_arOcc); r5.Add(_arPlanes); _arAdjust.Add(r5);
            var foot = El(); foot.style.marginTop = StyleKeyword.Auto; foot.pickingMode = PickingMode.Ignore;
            _arAdjBtn = new Button(() => { _arAdjOpen = !_arAdjOpen; RefreshArUi(); }) { text = "Réglages" }; _arAdjBtn.Cls("ar-step");
            _arAdjBtn.style.alignSelf = Align.FlexEnd; _arAdjBtn.style.marginBottom = 8; foot.Add(_arAdjBtn);
            _arAdjust.style.marginTop = 0; foot.Add(_arAdjust);

            // actions
            var bot = El("ar-bottom");
            bot.Add(HudBtn("RÉINITIALISER", () => { _ar.ResetPlacement(); RenderArCard(null); }));
            _arPlace = HudBtn("PLACER", () => _ar.Place(), "place"); bot.Add(_arPlace);
            bot.Add(HudBtn("CAPTURE", () => Capture()));
            bot.Add(HudBtn("INSPECTER", () => { var g = _ar.Inspect(); if (!g) RenderArCard(null); }));
            foot.Add(bot); scr.Add(foot);

            ShowScreen("ar", scr);
            RefreshArUi();
        }

        Button StepBtn(string icon, System.Action a) { var b = new Button(() => a()) { text = "" }; b.Cls("ar-step"); b.Add(Icon(icon, "s")); return b; }
        Button HudBtn(string text, System.Action a, string cls = null) { var b = new Button(() => a()) { text = text }; b.Cls("btn-hud " + (cls ?? "")); return b; }

        static float ScaleToSlider(float s) => Mathf.Log(Mathf.Clamp(s, ARController.MinScale, 1) / ARController.MinScale) / Mathf.Log(1 / ARController.MinScale);
        static float SliderToScale(float v)
        {
            float s = ARController.MinScale * Mathf.Pow(1 / ARController.MinScale, Mathf.Clamp01(v));
            // aimantation sur les échelles usuelles
            foreach (var r in ArRatios) if (Mathf.Abs(Mathf.Log(s * r)) < .06f) return 1f / r;
            return s;
        }

        void RefreshArUi()
        {
            if (_arUi == null) return;
            bool placed = _ar.Placed;
            _arScaleV.text = ARController.Ratio(_ar.Scale);
            _arSlider.SetValueWithoutNotify(ScaleToSlider(_ar.Scale));
            foreach (var b in _arPresets) b.EnableInClassList("on", b.userData is int r && Mathf.Abs(_ar.Scale * r - 1) < .02f);
            _arHeightV.text = (_ar.Height >= 0 ? "+" : "−") + Fr.M(Mathf.Abs(_ar.Height));
            _arRotV.text = Mathf.RoundToInt(Mathf.Repeat(_ar.Rotation + 180, 360) - 180) + "°";
            _arOcc.EnableInClassList("on", _ar.Occlusion); _arPlanes.EnableInClassList("on", _arPlanesOn);
            _arPlace.text = placed ? "DÉPLACER" : "PLACER";
            if (placed) _arStatus.text = $"Maquette placée · {ARController.Ratio(_ar.Scale)} · pincez pour l'échelle, tournez à deux doigts";
            _arAdjust.Show(_arAdjOpen && placed); _arAdjBtn.Show(placed); _arAdjBtn.EnableInClassList("on", _arAdjOpen);
            _arCross.Show(placed);
            if (!placed) RenderArCard(null);
        }

        /// <summary>Fiche courte de l'élément inspecté en RA.</summary>
        void RenderArCard(GameObject g)
        {
            if (_arCard == null) return;
            _arCard.Clear(); _arCard.Show(g != null);
            if (!g) return;
            var head = El(); head.style.flexDirection = FlexDirection.Row; head.style.alignItems = Align.FlexStart;
            var items = ShortInfo(g); var body = El("grow");
            foreach (var e in items) if (e != null) body.Add(e);
            head.Add(body);
            var x = new Button(() => _mv.Select(null)) { text = "" }; x.Cls("ib"); x.Add(Icon("close")); x.style.marginLeft = 6;
            x.Q(className: "ico").style.unityBackgroundImageTintColor = new Color(.04f, .12f, .27f);
            head.Add(x); _arCard.Add(head);
        }
    }
}
