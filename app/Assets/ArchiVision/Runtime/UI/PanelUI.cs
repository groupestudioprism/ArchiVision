// ARCHIVISION · panneau de la visite : hiérarchie BIM (masquer, isoler, centrer), données de l'élément (IFC, ARCHIPLAN, extras glTF),
// galerie de la maquette, informations (format, dimensions, unités, réalité augmentée).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;
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
        readonly HashSet<GameObject> _treeOpen = new HashSet<GameObject>(); string _treeQ = "";

        void RenderPanel()
        {
            if (_pbody == null || _screen != "viewer") return;
            foreach (var b in _ptabs.Children()) b.EnableInClassList("on", (string)b.userData == _ptab);
            _pbody.Clear();
            switch (_ptab) { case "tree": RenderTree(); break; case "props": RenderProps(); break; case "gallery": RenderGallery(); break; default: RenderInfo(); break; }
        }

        // ------------------------------------------------------------ Hiérarchie
        static readonly System.Text.RegularExpressions.Regex Anon = new System.Text.RegularExpressions.Regex(@"^(node|group|object|scene|root|primitive)?[_ ]?\d*$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        /// <summary>Enfants « utiles » : on saute les groupes intermédiaires sans nom ni données (fréquents dans les GLB) et les filaires.</summary>
        List<Transform> Kids(Transform t)
        {
            var o = new List<Transform>();
            foreach (Transform c in t)
            {
                if (c.name == "Filaire") continue;
                var b = c.GetComponent<BimNode>();
                bool anon = !c.GetComponent<Renderer>() && (b == null || !b.HasInfo) && b?.Extras?["niveau"] == null && Anon.IsMatch(c.name) && c.childCount > 0;
                if (anon) o.AddRange(Kids(c)); else o.Add(c);
            }
            return o;
        }
        void RenderTree()
        {
            var root = _mv.Root; if (!root) { _pbody.Add(L("Aucun élément à afficher. Placez ou ouvrez d'abord une maquette.", "sub")); return; }
            var ph = El("ph"); ph.Add(L(_mv.Meta?.Name ?? "Maquette", "h3")); ph.Add(L((_mv.Meta?.Kind ?? "glb").ToUpper(), "tag")); _pbody.Add(ph);
            if (_mv.Renderers.Count <= 1) _pbody.Add(L("Cette maquette ne contient qu'un seul élément (maillage fusionné, ou bloc provisoire d'un IFC non converti).", "note warn"));
            var q = Input(_treeQ, false, false, 60); q.textEdition.placeholder = "Filtrer : mur, porte, IfcSlab…"; q.style.marginBottom = 8; _pbody.Add(q);
            var list = El(); _pbody.Add(list);
            q.RegisterValueChangedCallback(e => { _treeQ = e.newValue; list.Clear(); FillTree(list, root.transform); });
            if (_treeOpen.Count == 0)
            {
                _treeOpen.Add(root);
                void Walk(Transform t, int d)
                {
                    foreach (var c in Kids(t))
                    {
                        var ifc = c.GetComponent<BimNode>()?.Ifc; string cls = (string)ifc?["classe"] ?? "";
                        bool spatial = cls == "IfcProject" || cls == "IfcSite" || cls == "IfcBuilding" || (cls == "IfcBuildingStorey" && Kids(c).Count < 30);
                        if ((d == 0 && Kids(c).Count < 40) || spatial) { _treeOpen.Add(c.gameObject); Walk(c, d + 1); }
                    }
                }
                Walk(root.transform, 0);
            }
            FillTree(list, root.transform);
            var r = El("row"); r.style.marginTop = 12;
            r.Add(Btn("Tout afficher", () => { _mv.ShowAll(); RenderPanel(); }, "small")); var f = Btn("Recentrer", () => { SetTool(Tool.Orbit); _tour.Frame("iso"); }, "small"); f.style.marginLeft = 8; r.Add(f);
            _pbody.Add(r);
        }
        int _rows;
        void FillTree(VisualElement list, Transform root) { _rows = 0; Row(list, root, 0, _treeQ.Trim().ToLowerInvariant()); if (_rows > 900) list.Add(L("Liste tronquée : filtrez pour retrouver un élément.", "t-small t-mut")); }
        bool Row(VisualElement list, Transform t, int depth, string q)
        {
            if (_rows > 900) return false;
            var g = t.gameObject; var kids = Kids(t); string label = _mv.Label(g), meta = depth == 0 ? $"{Math.Max(_mv.CountElements(), 0)} él." : _mv.Kind(g);
            bool open = _treeOpen.Contains(g) || (q.Length > 0 && depth < 6);
            bool match = q.Length == 0 || label.ToLowerInvariant().Contains(q) || meta.ToLowerInvariant().Contains(q);
            var sub = new List<VisualElement>(); var tmp = new VisualElement();
            bool any = false; if (kids.Count > 0 && open) foreach (var c in kids) any |= Row(tmp, c, depth + 1, q);
            if (q.Length > 0 && !match && !any) return false;
            var row = El("tr"); row.style.paddingLeft = depth * 14; row.EnableInClassList("on", _mv.Selected == g); row.EnableInClassList("off", !g.activeSelf);
            bool hasKids = kids.Count > 0 && !(g.GetComponent<Renderer>() && kids.All(k => k.GetComponent<Renderer>() && k.childCount == 0) && kids.Count < 2);
            var tw = new Button(() => { if (!_treeOpen.Remove(g)) _treeOpen.Add(g); RenderPanel(); }) { text = "" }; tw.Cls("tw" + (open ? " open" : "")); if (hasKids) tw.Add(Icon("chevron", "mut")); else tw.SetEnabled(false);
            row.Add(tw);
            var ifc = g.GetComponent<BimNode>()?.Ifc; var ap = g.GetComponent<BimNode>()?.ArchiPlan;
            string ico = depth == 0 ? "cube" : (g.GetComponent<BimNode>()?.Extras?["niveau"] != null || (string)ifc?["classe"] == "IfcBuildingStorey") ? "level" : g.GetComponent<Renderer>() ? "mesh" : (ap != null || ifc != null) ? "element" : "folder";
            row.Add(Icon(ico, "s mut"));
            var lab = L(label, "tl" + (depth == 0 ? " t-bold" : "")); row.Add(lab);
            if (!string.IsNullOrEmpty(meta)) row.Add(L(meta, "tm"));
            if (depth > 0) row.Add(IconBtn(g.activeSelf ? "eye" : "eyeoff", () => { _mv.SetVisible(g, !g.activeSelf); RenderPanel(); }, "te"));
            row.Add(IconBtn("dots", () => TreeMenu(g), "te"));
            lab.RegisterCallback<ClickEvent>(e => { _mv.Select(depth == 0 ? null : g, "tree"); RenderPanel(); if (e.clickCount == 2 && depth > 0) _tour.Focus(BoundsOf(g)); });
            list.Add(row); _rows++;
            foreach (var c in tmp.Children().ToList()) list.Add(c);
            return true;
        }
        static Bounds BoundsOf(GameObject g) { var rs = g.GetComponentsInChildren<Renderer>(); if (rs.Length == 0) return new Bounds(g.transform.position, Vector3.one); var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); return b; }
        void TreeMenu(GameObject g)
        {
            Menu(_mv.Label(g),
                ("fit", "Centrer la vue", "Zoom sur l'élément", false, () => { _mv.Select(g, "tree"); _tour.Focus(BoundsOf(g)); if (!_wide) OpenSheet(false); }),
                ("eye", "Isoler", "Masquer tout le reste", false, () => { _mv.Isolate(g); _mv.Select(g, "tree"); RenderPanel(); }),
                (g.activeSelf ? "eyeoff" : "eye", g.activeSelf ? "Masquer" : "Afficher", null, false, () => { _mv.SetVisible(g, !g.activeSelf); RenderPanel(); }),
                ("data", "Voir les données", "Onglet Données", false, () => { _mv.Select(g, "tree"); _ptab = "props"; RenderPanel(); }),
                ("refresh", "Tout afficher", null, false, () => { _mv.ShowAll(); RenderPanel(); }));
        }

        // ------------------------------------------------------------ Données
        static readonly Dictionary<string, string> ApLabel = new Dictionary<string, string> { {"id","Identifiant"},{"type","Type"},{"composition","Composition"},{"U","U (W/m²·K)"},{"Uw","Uw (W/m²·K)"},{"Ud","Ud (W/m²·K)"},{"lot","Lot"},{"famille","Famille"},{"categorie","Catégorie"},{"typeFamille","Type de famille"},{"materiau","Matériau"},{"hote","Hôte"},{"longueur","Longueur (m)"},{"pente","Pente"},{"source","Source"},{"niveau","Niveau"},{"altitude","Altitude (m)"},{"objet","Objet"} };
        static readonly (string k, string l)[] IfcLabel = { ("classe", "Classe IFC"), ("nom", "Nom"), ("nomLong", "Nom long"), ("description", "Description"), ("typeObjet", "Type d'objet"), ("type", "Type"), ("typePredefini", "Type prédéfini"), ("reference", "Référence"), ("globalId", "GlobalId"), ("expressID", "Identifiant STEP"), ("altitude", "Altitude (m)") };
        static readonly Dictionary<string, string> PsetLabel = new Dictionary<string, string> { {"FireRating","Résistance au feu"},{"IsExternal","Extérieur"},{"LoadBearing","Porteur"},{"ThermalTransmittance","U (W/m²·K)"},{"AcousticRating","Indice acoustique"},{"Reference","Référence"},{"Status","Statut"},{"Width","Largeur"},{"Height","Hauteur"},{"Length","Longueur"},{"Depth","Profondeur"},{"Thickness","Épaisseur"},{"Area","Surface"},{"NetArea","Surface nette"},{"GrossArea","Surface brute"},{"Volume","Volume"},{"NetVolume","Volume net"},{"GrossVolume","Volume brut"},{"NetSideArea","Surface latérale nette"},{"GrossSideArea","Surface latérale brute"},{"Perimeter","Périmètre"},{"Combustible","Combustible"},{"HandicapAccessible","Accessible PMR"},{"FireExit","Sortie de secours"} };
        static string FmtVal(JToken v)
        {
            if (v == null) return null;
            switch (v.Type)
            {
                case JTokenType.Boolean: return (bool)v ? "Oui" : "Non";
                case JTokenType.Integer: return ((long)v).ToString(CultureInfo.InvariantCulture);
                case JTokenType.Float: { double d = (double)v; return d.ToString(Math.Abs(d) < 10 ? "0.###" : "0.##", Fr.C); }
                case JTokenType.String: { var s = (string)v; return s == ".T." || s == "T" ? "Oui" : s == ".F." || s == "F" ? "Non" : s; }
                case JTokenType.Array: return string.Join(", ", v.Select(FmtVal));
                default: return null;
            }
        }
        VisualElement Group(string title) { var g = El("grp"); g.Add(L(title.ToUpper(), "sec-title")); return g; }
        VisualElement Layers(JArray couches)
        {
            if (couches == null || couches.Count == 0) return null;
            var g = Group("Couches (extérieur → intérieur)"); double tot = couches.Sum(c => (double?)c["epaisseur"] ?? 0);
            foreach (var c in couches)
            {
                double e = (double?)c["epaisseur"] ?? 0; var r = El("layer");
                var bar = El("bar-l"); bar.style.width = Mathf.Clamp((float)(e / Math.Max(tot, 1e-6) * 60), 6, 60); r.Add(bar);
                var n = L((string)c["materiau"] ?? "—", "grow t-small"); r.Add(n); r.Add(L(e > 0 ? Fr.N(e * 100, 1) + " cm" : "—", "t-mono t-small t-mut")); g.Add(r);
            }
            if (tot > 0) g.Add(L($"Épaisseur totale : {Fr.N(tot * 100, 1)} cm", "t-small t-mut"));
            return g;
        }
        void RenderProps()
        {
            var g = _mv.Selected;
            if (!g)
            {
                _pbody.Add(L("Données de l'élément", "h3"));
                var e = El("empty"); e.Add(Icon("inspect", "l accent")); e.Add(L("Touchez un élément de la maquette (outil Inspecter, ou en orbite) pour afficher ses données BIM : classe IFC, matériaux, composition, propriétés.", "sub")); _pbody.Add(e); return;
            }
            var b = g.GetComponent<BimNode>(); var ap = b?.ArchiPlan; var ifc = b?.Ifc;
            var ph = El("ph"); ph.Add(L(_mv.Label(g), "h3")); var k = _mv.Kind(g); if (!string.IsNullOrEmpty(k)) ph.Add(L(k.ToUpper(), "tag")); _pbody.Add(ph);
            if (ifc != null)
            {
                var gr = Group("IFC Object Data"); foreach (var (key, lab) in IfcLabel) gr.Add(KV(lab, FmtVal(ifc[key])));
                if (ifc["materiaux"] is JArray mats && mats.Count > 0) gr.Add(KV("Matériaux", FmtVal(mats)));
                _pbody.Add(gr); _pbody.Add(Layers(ifc["couches"] as JArray));
                if (ifc["psets"] is JObject ps)
                    foreach (var p in ps.Properties())
                    {
                        var box = El("pset"); var hd = El("pset-h"); hd.Add(L(p.Name, "t-bold t-small")); var body = El(); bool open = p.Name.Contains("Common") || p.Name.Contains("Quantit");
                        var chev = Icon("chevron", "s mut"); chev.style.rotate = new Rotate(open ? 90 : 0); hd.Add(chev); body.Show(open);
                        hd.RegisterCallback<ClickEvent>(_ => { bool o = body.resolvedStyle.display == DisplayStyle.None; body.Show(o); chev.style.rotate = new Rotate(o ? 90 : 0); });
                        foreach (var kv in (p.Value as JObject)?.Properties() ?? Enumerable.Empty<JProperty>()) body.Add(KV(PsetLabel.TryGetValue(kv.Name, out var pl) ? pl : kv.Name, FmtVal(kv.Value)));
                        box.Add(hd); box.Add(body); _pbody.Add(box);
                    }
            }
            if (ap != null)
            {
                var gr = Group("Données ARCHIPLAN");
                foreach (var p in ap.Properties()) { if (p.Value.Type == JTokenType.Object || p.Value.Type == JTokenType.Array || p.Name == "version") continue; var v = FmtVal(p.Value); if (p.Name == "type") v = _mv.Kind(g); gr.Add(KV(ApLabel.TryGetValue(p.Name, out var l) ? l : p.Name, v)); }
                _pbody.Add(gr); _pbody.Add(Layers(ap["couches"] as JArray));
                if (ap["parametres"] is JObject prm) { var pg = Group("Paramètres de la famille"); foreach (var p in prm.Properties()) pg.Add(KV(p.Name, FmtVal(p.Value))); _pbody.Add(pg); }
            }
            var other = b?.Extras?.Properties().Where(p => p.Name != "archiplan" && p.Name != "ifc" && p.Name != "bim" && p.Value.Type != JTokenType.Object).ToList();
            if (other != null && other.Count > 0) { var og = Group("Propriétés (extras glTF)"); foreach (var p in other) og.Add(KV(ApLabel.TryGetValue(p.Name, out var l) ? l : p.Name, FmtVal(p.Value))); _pbody.Add(og); }
            // géométrie
            var bb = BoundsOf(g); var inv = _mv.Pivot; var s = bb.size / Mathf.Max(1e-6f, _mv.Pivot.lossyScale.x);
            var mats3d = g.GetComponentsInChildren<Renderer>().SelectMany(r => r.sharedMaterials).Where(m => m && !m.name.StartsWith("IFC ")).Select(m => m.name.Replace(" (Instance)", "")).Distinct().Take(8).ToList();
            var geo = Group("Géométrie"); geo.Add(KV("Dimensions (L × l × h)", $"{Fr.N(s.x, 2)} × {Fr.N(s.z, 2)} × {Fr.N(s.y, 2)} m"));
            geo.Add(KV("Base (altitude)", Fr.M(inv.InverseTransformPoint(bb.min).y))); geo.Add(KV("Sous-éléments", g.GetComponentsInChildren<Renderer>().Length.ToString()));
            if (mats3d.Count > 0) geo.Add(KV("Matériaux 3D", string.Join(", ", mats3d)));
            _pbody.Add(geo);
            if (ap == null && ifc == null && (other == null || other.Count == 0)) _pbody.Add(L("Cet objet ne porte pas de données BIM : le fichier ne contient que la géométrie. Exportez en IFC, ou en GLB depuis ARCHIPLAN, pour garder les informations.", "note"));
            var r2 = El("row"); r2.style.marginTop = 14; r2.style.flexWrap = Wrap.Wrap;
            r2.Add(Btn("Centrer", () => { _tour.Focus(BoundsOf(g)); if (!_wide) OpenSheet(false); }, "small")); var i2 = Btn("Isoler", () => { _mv.Isolate(g); }, "small"); i2.style.marginLeft = 8; r2.Add(i2);
            var h2 = Btn("Masquer", () => { _mv.SetVisible(g, false); _mv.Select(null); }, "small"); h2.style.marginLeft = 8; r2.Add(h2); var a2 = Btn("Tout afficher", () => _mv.ShowAll(), "small"); a2.style.marginLeft = 8; r2.Add(a2);
            _pbody.Add(r2);
        }

        /// <summary>Fiche courte de l'élément visé (réalité augmentée).</summary>
        List<VisualElement> ShortInfo(GameObject g)
        {
            var o = new List<VisualElement>(); var b = g.GetComponent<BimNode>(); var ap = b?.ArchiPlan; var ifc = b?.Ifc;
            o.Add(L(_mv.Label(g), "h3"));
            if (ifc != null)
            {
                o.Add(KV("Classe", (string)ifc["classe"])); o.Add(KV("Type", FmtVal(ifc["type"] ?? ifc["typeObjet"])));
                o.Add(KV("Matériau", ifc["materiaux"] is JArray m && m.Count > 0 ? FmtVal(m) : string.Join(" / ", (ifc["couches"] as JArray)?.Select(c => (string)c["materiau"]) ?? new string[0])));
                var fr = (ifc["psets"] as JObject)?.Properties().Select(p => p.Value["FireRating"]).FirstOrDefault(x => x != null); if (fr != null) o.Add(KV("Résistance au feu", FmtVal(fr)));
            }
            if (ap != null) { o.Add(KV("Type", _mv.Kind(g))); o.Add(KV("Composition", (string)(ap["composition"] ?? ap["famille"]))); if (ap["U"] != null) o.Add(KV("U", FmtVal(ap["U"]) + " W/m²·K")); o.Add(KV("Lot", (string)ap["lot"])); }
            var s = BoundsOf(g).size / Mathf.Max(1e-6f, _mv.Pivot.lossyScale.x); o.Add(KV("Dimensions", $"{Fr.N(s.x, 2)} × {Fr.N(s.z, 2)} × {Fr.N(s.y, 2)} m"));
            return o;
        }

        // ------------------------------------------------------------ Galerie
        void RenderGallery()
        {
            var ph = El("ph"); ph.Add(L("Galerie", "h3")); ph.Add(L("MAQUETTE", "tag")); _pbody.Add(ph);
            _pbody.Add(L("Captures du chantier et de la maquette.", "t-small t-mut"));
            var b = Btn("Nouvelle capture (vue actuelle)", () => Capture(), "grad wide", "camera"); b.style.marginTop = 10; _pbody.Add(b);
            var ps = Library.Photos(_mv.Meta?.Id ?? "sample");
            if (ps.Count == 0) { var e = L("Aucune photo pour le moment.", "sub"); e.style.marginTop = 14; _pbody.Add(e); return; }
            var g = El("pgrid"); var ids = ps.Select(p => p.Id).ToList(); foreach (var p in ps) g.Add(PhotoCard(p, ids, "pthumb")); _pbody.Add(g);
        }

        // ------------------------------------------------------------ Infos
        void RenderInfo()
        {
            var m = _mv.Meta; var rootB = _mv.Root ? _mv.Root.GetComponent<BimNode>() : null; var ap = rootB?.ArchiPlan;
            var ph = El("ph"); ph.Add(L(m?.Name ?? "Maquette", "h3")); ph.Add(L("INFOS", "tag")); _pbody.Add(ph);
            var f = Group("Fichier");
            f.Add(KV("Format", m?.Kind switch { "ifc" => "IFC (converti en GLB à l'import)", "glb" => "GLB (glTF binaire)", "gltf" => "glTF", _ => "Maquette d'exemple" }));
            if (m != null && m.SourceSize > 0) f.Add(KV("Taille d'origine", Fr.Size(m.SourceSize)));
            if (m != null && !m.IsSample) f.Add(KV("Importée le", Fr.DateTime(m.CreatedUtc)));
            f.Add(KV("Projet ARCHIPLAN", (string)ap?["projet"])); f.Add(KV("Adresse", (string)ap?["lieu"]?["adresse"]));
            _pbody.Add(f);
            var g = Group("Maquette");
            g.Add(KV("Dimensions (L × l × h)", $"{Fr.N(_mv.Size.x, 2)} × {Fr.N(_mv.Size.z, 2)} × {Fr.N(_mv.Size.y, 2)} m"));
            g.Add(KV("Objets 3D", _mv.Renderers.Count.ToString())); g.Add(KV("Triangles", _mv.Triangles.ToString("N0", Fr.C))); g.Add(KV("Éléments BIM", _mv.CountElements().ToString()));
            _pbody.Add(g);
            _pbody.Add(L("Unités du fichier", "field-label"));
            int sel = Mathf.Approximately(_mv.UnitScale, 1) ? 0 : Mathf.Approximately(_mv.UnitScale, .01f) ? 1 : 2;
            _pbody.Add(Seg(new[] { "Mètres", "Centimètres", "Millimètres" }, sel, i =>
            {
                float k = i == 0 ? 1 : i == 1 ? .01f : .001f; _mv.Normalize(k); _tour.Frame("iso", true);
                if (m != null && !m.IsSample) { m.UnitScale = k; Library.SaveModel(m); }
                _vtSub.text = $"Visite 3D · {Fr.N(_mv.Size.x, 1)} × {Fr.N(_mv.Size.z, 1)} × {Fr.N(_mv.Size.y, 1)} m"; RenderPanel();
            }));
            _pbody.Add(L("1 = maquette en mètres. Si la maquette paraît géante ou minuscule en réalité augmentée, changez l'unité.", "t-small t-mut t-wrap"));
            var a = Group("Réalité augmentée");
            a.Add(KV("Cet appareil", ARController.Availability switch { ArAvailability.Supported => "ARCore disponible", ArAvailability.NeedsInstall => "ARCore à installer", ArAvailability.Unsupported => "Non pris en charge", _ => "Vérification…" }));
            a.Add(KV("Échelle de pose", ARController.Ratio(_ar.Scale)));
            _pbody.Add(a);
        }
    }
}
