// ARCHIVISION · application : démarrage, navigation entre les écrans, thème clair/sombre, mise en page téléphone/tablette, messages et dialogues.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using StudioPrism.ArchiVision.AR;
using StudioPrism.ArchiVision.Native;
using StudioPrism.ArchiVision.Services;
using StudioPrism.ArchiVision.UI;
using StudioPrism.ArchiVision.Viewer;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;
using UnityEngine.XR.ARFoundation;
using static StudioPrism.ArchiVision.UI.K;

namespace StudioPrism.ArchiVision
{
    public partial class App : MonoBehaviour
    {
        public static App Instance { get; private set; }
        [SerializeField] Camera tourCamera;
        [SerializeField] Light sun;
        [SerializeField] ARSession arSession;
        [SerializeField] XROrigin xrOrigin;
        [SerializeField] StyleSheet styleSheet;

        UIDocument _doc; VisualElement _root, _layer, _overlay;
        ModelView _mv; TourCamera _tour; ARController _ar;
        readonly Queue<Action> _next = new Queue<Action>();
        bool _wide; Rect _safe; ScreenOrientation _orient;
        public bool Dark { get; private set; }
        float _lastInteraction;

        void Awake()
        {
            Instance = this;
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;
            UnityEngine.Input.multiTouchEnabled = true;
            _mv = gameObject.AddComponent<ModelView>();
            _tour = tourCamera.gameObject.AddComponent<TourCamera>(); _tour.Cam = tourCamera;
            _ar = gameObject.AddComponent<ARController>(); _ar.Session = arSession; _ar.Origin = xrOrigin; _ar.Sun = sun;
            if (!FindAnyObjectByType<AndroidNative>()) new GameObject("AVNative").AddComponent<AndroidNative>();
            _doc = GetComponent<UIDocument>();
        }

        void Start()
        {
            _root = _doc.rootVisualElement; _root.Clear();
            if (styleSheet) _root.styleSheets.Add(styleSheet);
            _root.AddToClassList("av-root");
            _root.style.flexGrow = 1;
            _layer = El("grow"); _layer.style.position = Position.Absolute; _layer.style.left = _layer.style.top = _layer.style.right = _layer.style.bottom = 0;
            _overlay = El(); _overlay.style.position = Position.Absolute; _overlay.style.left = _overlay.style.top = _overlay.style.right = _overlay.style.bottom = 0; _overlay.pickingMode = PickingMode.Ignore;
            _root.Add(_layer); _root.Add(_overlay);
            _root.RegisterCallback<PointerDownEvent>(_ => _lastInteraction = Time.unscaledTime, TrickleDown.TrickleDown);
            SetTheme(PlayerPrefs.GetInt("av.dark", 0) == 1);
            Layout(true);
            _tour.IsOverUI = IsOverUI; _ar.IsOverUI = IsOverUI;
            _tour.OnTap = OnViewTap; _tour.OnDoubleTap = OnViewDoubleTap;
            tourCamera.enabled = false;
            StartCoroutine(_ar.CheckAvailability());
            Boot();
        }

        void Update()
        {
            while (_next.Count > 0) _next.Dequeue()?.Invoke();
            Layout(false);
            // économie de batterie en visite : rendu ralenti quand rien ne bouge
            bool idle = !_ar.Running && !_tour.Moving && Time.unscaledTime - _lastInteraction > 1.5f && UnityEngine.InputSystem.EnhancedTouch.Touch.activeTouches.Count == 0 && _toastT <= 0;
            OnDemandRendering.renderFrameInterval = idle && _screen == "viewer" ? 4 : 1;
            if (_toastT > 0) { _toastT -= Time.unscaledDeltaTime; if (_toastT <= 0) _toast?.RemoveFromClassList("show"); }
            UpdateViewerFrame();
        }
        public void RunNextFrame(Action a) => _next.Enqueue(a);

        // ------------------------------------------------------------ Mise en page
        void Layout(bool force)
        {
            #if UNITY_EDITOR
            ApplyEditorDpi();
#endif
            float dpW = Screen.width / Mathf.Max(.1f, Dpi / 160f);
            bool wide = dpW >= 840;
            if (force || wide != _wide) { _wide = wide; _root.EnableInClassList("wide", wide); if (!force) OnLayoutChanged(); }
            if (force || Screen.safeArea != _safe || Screen.orientation != _orient)
            {
                _safe = Screen.safeArea; _orient = Screen.orientation;
                // marges de sécurité (encoche, barre de navigation) en unités du panneau
                float k = (_root.panel != null && _root.layout.width > 0) ? _root.layout.width / Screen.width : 160f / Mathf.Max(100, Screen.dpi);
                _root.style.paddingTop = (Screen.height - _safe.yMax) * k; _root.style.paddingBottom = _safe.yMin * k;
                _root.style.paddingLeft = _safe.xMin * k; _root.style.paddingRight = (Screen.width - _safe.xMax) * k;
            }
        }
        public bool Wide => _wide;

        /// <summary>Densité d'écran utilisée pour la mise en page. Dans l'éditeur : téléphone simulé (412 dp de large, ou EditorDp).</summary>
        public static float Dpi => Application.isEditor ? Mathf.Min(Screen.width, Screen.height) / EditorDp * 160f : (Screen.dpi > 0 ? Screen.dpi : 160);
        public static float EditorDp = 412;
#if UNITY_EDITOR
        float _edDpi;
        void ApplyEditorDpi()
        {
            if (Mathf.Approximately(_edDpi, Dpi) || Screen.dpi <= 0) return;
            _edDpi = Dpi;
            if (_doc.panelSettings.name.IndexOf("(Clone)", StringComparison.Ordinal) < 0) _doc.panelSettings = Instantiate(_doc.panelSettings);
            _doc.panelSettings.referenceDpi = Screen.dpi * 160f / Dpi; _doc.panelSettings.fallbackDpi = _doc.panelSettings.referenceDpi;
        }
        public static string DevDp(string[] a) { EditorDp = float.Parse(a[0], System.Globalization.CultureInfo.InvariantCulture); return "dp=" + EditorDp; }
#endif
        partial void OnLayoutChanged();

        public void SetTheme(bool dark)
        {
            Dark = dark; _root.EnableInClassList("dark", dark); PlayerPrefs.SetInt("av.dark", dark ? 1 : 0);
            if (tourCamera) tourCamera.backgroundColor = dark ? new Color(.067f, .086f, .118f) : new Color(.949f, .965f, .976f);
        }

        /// <summary>Le doigt est-il sur un élément d'interface (et non sur la vue 3D) ?</summary>
        public bool IsOverUI(Vector2 screenPos)
        {
            var panel = _root?.panel; if (panel == null) return false;
            var p = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(screenPos.x, Screen.height - screenPos.y));
            var e = panel.Pick(p);
            return e != null && e != _root && e != _layer && !(e.userData is string s && s == "viewport");
        }

        // ------------------------------------------------------------ Écrans
        string _screen = "";
        VisualElement _current;
        void ShowScreen(string name, VisualElement content)
        {
            _screen = name;
            _layer.Clear(); _current = content; _layer.Add(content);
            CloseMenu();
        }

        // ------------------------------------------------------------ Messages, attente, dialogues, menus
        Label _toast; float _toastT;
        public void Toast(string msg, float sec = 2.8f)
        {
            if (_toast == null) { _toast = L("", "toast"); _toast.pickingMode = PickingMode.Ignore; }
            _overlay.Add(_toast); _toast.text = msg; _toast.AddToClassList("show"); _toastT = sec; _lastInteraction = Time.unscaledTime;
        }
        VisualElement _busy; Label _busyL; IVisualElementScheduledItem _spin;
        public void Busy(string text)
        {
            if (text == null) { _busy?.RemoveFromHierarchy(); _spin?.Pause(); return; }
            if (_busy == null)
            {
                _busy = El("busy"); var sp = El("spinner"); _busyL = L("", ""); _busy.Add(sp); _busy.Add(_busyL);
                float a = 0; _spin = sp.schedule.Execute(() => { a += 12; sp.style.rotate = new Rotate(a); }).Every(16);
            }
            _busyL.text = text; _spin.Resume(); if (_busy.parent == null) _root.Add(_busy); _lastInteraction = Time.unscaledTime;
        }

        VisualElement _dlg;
        public void Dialog(string title, string text, Action<VisualElement> body, params (string label, string cls, Func<bool> act)[] buttons)
        {
            CloseDialog();
            _dlg = El("scrim"); var card = El("dlg"); _dlg.Add(card);
            _dlg.RegisterCallback<PointerDownEvent>(e => { if (e.target == _dlg) CloseDialog(); });
            if (title != null) card.Add(L(title, "h3"));
            if (text != null) card.Add(L(text, "sub t-wrap"));
            body?.Invoke(card);
            var acts = El("dlg-acts");
            foreach (var b in buttons) { var bb = b; acts.Add(Btn(bb.label, () => { if (bb.act == null || bb.act()) CloseDialog(); }, "small " + bb.cls)); }
            card.Add(acts); _root.Add(_dlg);
        }
        public void CloseDialog() { _dlg?.RemoveFromHierarchy(); _dlg = null; }
        public void Confirm(string title, string text, string ok, Action onOk) => Dialog(title, text, null, ("Annuler", "", null), (ok, "danger", () => { onOk(); return true; }));
        public void Ask(string title, string label, string value, string ok, Func<string, bool> onOk)
        {
            TextField f = null;
            Dialog(title, null, c => { f = Input(value, false, false, 120); c.Add(Field(label, f)); f.schedule.Execute(() => { f.Focus(); f.SelectAll(); }).StartingIn(80); },
                ("Annuler", "", null), (ok, "primary", () => { var v = f.value.Trim(); return v.Length > 0 && onOk(v); }));
        }

        VisualElement _menu;
        public void Menu(string title, params (string icon, string label, string sub, bool danger, Action act)[] items)
        {
            CloseMenu();
            _menu = El("scrim"); _menu.style.justifyContent = Justify.FlexEnd; _menu.style.paddingLeft = _menu.style.paddingRight = _menu.style.paddingBottom = 0;
            _menu.RegisterCallback<PointerDownEvent>(e => { if (e.target == _menu) CloseMenu(); });
            var sheet = El("menu-sheet"); sheet.style.position = Position.Relative; sheet.Add(El("sheet-grip"));
            if (_wide) { sheet.style.width = 440; sheet.style.alignSelf = Align.Center; sheet.style.marginBottom = 16; sheet.style.borderBottomLeftRadius = sheet.style.borderBottomRightRadius = 20; }
            if (title != null) { var t = L(title, "h3"); t.style.marginLeft = 12; t.style.marginBottom = 4; t.style.marginTop = 4; sheet.Add(t); }
            foreach (var it in items)
            {
                if (it.label == null) { sheet.Add(El("sep")); continue; }
                var i = it; var b = new Button(() => { CloseMenu(); i.act?.Invoke(); }) { text = "" }; b.Cls("menu-item" + (i.danger ? " danger" : ""));
                b.Add(Icon(i.icon ?? "chevron")); var tx = El("grow"); tx.Add(L(i.label, "b")); if (!string.IsNullOrEmpty(i.sub)) tx.Add(L(i.sub, "s")); b.Add(tx);
                sheet.Add(b);
            }
            _menu.Add(sheet); _root.Add(_menu);
        }
        public void CloseMenu() { _menu?.RemoveFromHierarchy(); _menu = null; }

        public bool NeedLicence()
        {
            if (Account.Full) return false;
            ShowLicence("feature"); return true;
        }

        void OnApplicationPause(bool paused)
        {
            if (paused) _pausedAt = Time.realtimeSinceStartup;
            else if (Account.Me != null && Account.Lock?.Pin != null && Time.realtimeSinceStartup - _pausedAt > 300 && _screen != "auth" && !_ar.Running) ShowLock();
        }
        float _pausedAt;

        void OnApplicationQuit() { PlayerPrefs.Save(); }

#if UNITY_EDITOR
        // Raccourcis de test dans l'éditeur (pilotés par le pont AVBridge ; absents de l'APK).
        public static string DevSample() { Instance.OpenSample(false); return "ok"; }
        public static string DevAR() { Instance.EnterAR(); return "ok"; }
        public static string DevHome() { try { Instance.ShowHome(); return "ok"; } catch (Exception e) { return e.ToString(); } }
        public static string DevTool(string[] a) { Instance.SetTool((Tool)Enum.Parse(typeof(Tool), a[0])); return "ok"; }
        public static string DevTab(string[] a) { Instance._ptab = a[0]; Instance.RenderPanel(); Instance.OpenSheet(true); return "ok"; }
        public static string DevPlace() { Instance._ar.DevForcePlace(); return Instance._ar.Placed ? "placé" : "pas de surface"; }
        public static string DevCapture() { Instance.Capture(); return "ok"; }
        public static string DevPhotos() { var ps = Services.Library.Photos(); if (ps.Count == 0) return "aucune"; Instance.OpenPhoto(ps[0].Id, ps.ConvertAll(p => p.Id)); return ps.Count + " photo(s)"; }
        public static string DevBack() { Instance.Back(); return "ok"; }
#endif

        /// <summary>Bouton « retour » d'Android (touche Échap dans l'éditeur).</summary>
        void LateUpdate()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) Back();
        }
        void Back()
        {
            if (_menu != null) { CloseMenu(); return; }
            if (_dlg != null) { CloseDialog(); return; }
            if (_photo != null) { ClosePhoto(); return; }
            if (_ar.Running) { ExitAR(); return; }
            if (_screen == "viewer") { if (_sheetOpen && !_wide) { OpenSheet(false); return; } ShowHome(); return; }
            if (_screen == "home") { Application.Quit(); }
        }
    }
}
