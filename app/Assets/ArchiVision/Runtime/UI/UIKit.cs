// ARCHIVISION · petits outils pour construire l'interface UI Toolkit en C# (éléments, boutons, champs, icônes, listes clé/valeur).
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace StudioPrism.ArchiVision.UI
{
    public static class K
    {
        static readonly Dictionary<string, Texture2D> Icons = new Dictionary<string, Texture2D>();
        public static Texture2D IconTex(string name) { if (!Icons.TryGetValue(name, out var t)) Icons[name] = t = Resources.Load<Texture2D>("Icons/" + name); return t; }

        public static VisualElement El(string cls = null, params VisualElement[] kids)
        {
            var e = new VisualElement(); if (!string.IsNullOrEmpty(cls)) foreach (var c in cls.Split(' ')) if (c.Length > 0) e.AddToClassList(c);
            foreach (var k in kids) if (k != null) e.Add(k); return e;
        }
        public static T Cls<T>(this T e, string cls) where T : VisualElement { if (!string.IsNullOrEmpty(cls)) foreach (var c in cls.Split(' ')) if (c.Length > 0) e.AddToClassList(c); return e; }
        public static T Kids<T>(this T e, params VisualElement[] kids) where T : VisualElement { foreach (var k in kids) if (k != null) e.Add(k); return e; }
        public static T Show<T>(this T e, bool on) where T : VisualElement { e.style.display = on ? DisplayStyle.Flex : DisplayStyle.None; return e; }
        public static T Name<T>(this T e, string n) where T : VisualElement { e.name = n; return e; }

        public static Label L(string text, string cls = null) { var l = new Label(text); l.Cls(cls); return l; }
        public static VisualElement Icon(string name, string cls = null)
        {
            var e = El("ico " + (cls ?? "")); var t = IconTex(name); if (t) e.style.backgroundImage = new StyleBackground(t); e.pickingMode = PickingMode.Ignore; return e;
        }
        public static Button Btn(string text, Action onClick, string cls = null, string icon = null)
        {
            var b = new Button(() => onClick?.Invoke()) { text = "" }; b.Cls("btn " + (cls ?? ""));
            if (icon != null) b.Add(Icon(icon, "s"));
            if (!string.IsNullOrEmpty(text)) { var l = L(text); l.pickingMode = PickingMode.Ignore; b.Add(l); }
            return b;
        }
        public static Button IconBtn(string icon, Action onClick, string cls = null, string tooltip = null)
        {
            var b = new Button(() => onClick?.Invoke()) { text = "", tooltip = tooltip }; b.Cls("ib " + (cls ?? "")); b.Add(Icon(icon)); return b;
        }
        public static Button Link(string text, Action onClick) { var b = new Button(() => onClick?.Invoke()) { text = text }; b.Cls("link"); b.style.backgroundColor = Color.clear; b.style.borderTopWidth = b.style.borderBottomWidth = b.style.borderLeftWidth = b.style.borderRightWidth = 0; return b; }

        public static TextField Input(string value = "", bool password = false, bool multiline = false, int max = 200)
        {
            var t = new TextField { value = value ?? "", isPasswordField = password, multiline = multiline, maxLength = max }; t.Cls("av-input" + (multiline ? " multi" : ""));
            if (password) t.maskChar = '•';
            return t;
        }
        public static VisualElement Field(string label, VisualElement input, string hint = null)
        {
            var f = El("field"); f.Add(L(label, "field-label")); f.Add(input); if (hint != null) f.Add(L(hint, "t-small t-mut t-wrap")); return f;
        }
        public static VisualElement KV(string k, string v)
        {
            if (string.IsNullOrEmpty(v)) return null;
            var r = El("kv-row"); r.Add(L(k, "k")); r.Add(L(v, "v")); return r;
        }
        public static VisualElement Bullet(string text) { var r = El("bullet"); r.Add(El("dot")); r.Add(L(text)); return r; }

        /// <summary>Bouton « case à cocher » simple (la case native d'UI Toolkit est trop petite au doigt).</summary>
        public static VisualElement Check(string text, Action<bool> onChange, out Func<bool> get)
        {
            bool on = false; var row = El("check-row"); var box = El("check"); var lab = L(text, "t-wrap grow t-small");
            void Set(bool v) { on = v; box.EnableInClassList("on", v); box.Clear(); if (v) box.Add(Icon("check", "s white")); onChange?.Invoke(v); }
            row.RegisterCallback<ClickEvent>(_ => Set(!on));
            row.Add(box); row.Add(lab); get = () => on; return row;
        }

        public static VisualElement Seg(string[] labels, int sel, Action<int> onChange)
        {
            var s = El("seg"); var btns = new List<Button>();
            for (int i = 0; i < labels.Length; i++)
            {
                int k = i; var b = new Button(() => { for (int j = 0; j < btns.Count; j++) btns[j].EnableInClassList("on", j == k); onChange?.Invoke(k); }) { text = labels[i] };
                b.Cls("seg-btn"); b.EnableInClassList("on", i == sel); btns.Add(b); s.Add(b);
            }
            return s;
        }
        public static ScrollView Scroll(string cls = null)
        {
            var s = new ScrollView(ScrollViewMode.Vertical); s.Cls(cls); s.horizontalScrollerVisibility = ScrollerVisibility.Hidden; s.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            s.touchScrollBehavior = ScrollView.TouchScrollBehavior.Elastic; s.mouseWheelScrollSize = 60; return s;
        }
        public static void Tex(VisualElement e, Texture2D t) { if (t) e.style.backgroundImage = new StyleBackground(t); }
    }
}
