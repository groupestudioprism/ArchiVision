// ARCHIVISION · maquette affichée : mise à l'échelle (mètres), base au sol, styles d'affichage, sélection, visibilité, coupe, collisions de visée.
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using StudioPrism.ArchiVision.Services;
using UnityEngine;
using UnityEngine.Rendering;

namespace StudioPrism.ArchiVision.Viewer
{
    public enum ViewStyle { Materials, Shaded, Wire, XRay }

    public class ModelView : MonoBehaviour
    {
        public static ModelView I { get; private set; }
        public Transform Pivot { get; private set; }        // ancré au sol (AR) ; la maquette est posée dessus
        public GameObject Root { get; private set; }
        public ModelMeta Meta { get; private set; }
        public Bounds Box { get; private set; }
        public Vector3 Size => Box.size;
        public float Radius => Mathf.Max(0.5f, Box.extents.magnitude);
        public float UnitScale { get; private set; } = 1;
        public int Triangles { get; private set; }
        public List<Renderer> Renderers { get; } = new List<Renderer>();
        public GameObject Selected { get; private set; }
        public ViewStyle Style { get; private set; } = ViewStyle.Materials;
        public float? SectionHeight { get; private set; }
        public event Action<GameObject, string> SelectionChanged;
        public event Action ModelChanged;

        readonly Dictionary<Renderer, Material[]> _orig = new Dictionary<Renderer, Material[]>();
        readonly Dictionary<Material, Material> _shaded = new Dictionary<Material, Material>();
        readonly Dictionary<Renderer, GameObject> _wires = new Dictionary<Renderer, GameObject>();
        readonly HashSet<Renderer> _sel = new HashSet<Renderer>();
        Material _xray, _wireMat, _hl, _hlX;
        GameObject _shadowBlob;

        void Awake()
        {
            I = this;
            Pivot = new GameObject("Pivot maquette").transform;
            var lit = Shader.Find("Universal Render Pipeline/Lit"); var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            _xray = Transparent(unlit, new Color(0.165f, 0.486f, 0.576f, 0.12f));
            _wireMat = new Material(unlit) { color = new Color(0.12f, 0.35f, 0.53f) }; _wireMat.SetColor("_BaseColor", new Color(0.12f, 0.35f, 0.53f));
            _hl = new Material(lit); _hl.SetColor("_BaseColor", new Color(0.18f, 0.83f, 0.71f)); _hl.EnableKeyword("_EMISSION"); _hl.SetColor("_EmissionColor", new Color(0.05f, 0.35f, 0.3f));
            _hlX = Transparent(unlit, new Color(0.18f, 0.83f, 0.71f, 0.75f));
        }
        static Material Transparent(Shader s, Color c)
        {
            var m = new Material(s); m.SetFloat("_Surface", 1); m.SetOverrideTag("RenderType", "Transparent");
            m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha); m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha); m.SetInt("_ZWrite", 0); m.SetInt("_Cull", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.renderQueue = (int)RenderQueue.Transparent; m.SetColor("_BaseColor", c); return m;
        }

        // ------------------------------------------------------------ Mise en place
        public void SetModel(GameObject root, ModelMeta meta)
        {
            Clear();
            Root = root; Meta = meta; root.transform.SetParent(Pivot, false);
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer) continue;
                Renderers.Add(r); _orig[r] = r.sharedMaterials;
                r.shadowCastingMode = ShadowCastingMode.On; r.receiveShadows = true;
                var mf = r.GetComponent<MeshFilter>(); if (mf && mf.sharedMesh) Triangles += (int)(mf.sharedMesh.triangles.LongLength / 3);
            }
            Normalize(meta?.UnitScale > 0 ? meta.UnitScale : 0);
            BuildColliders();
            MakeShadowBlob();
            Style = ViewStyle.Materials; ApplyMaterials();
            ModelChanged?.Invoke();
        }

        public void Clear()
        {
            Select(null);
            foreach (var w in _wires.Values) if (w) Destroy(w);
            _wires.Clear(); _orig.Clear(); _sel.Clear(); Renderers.Clear(); Triangles = 0; SectionHeight = null;
            foreach (var m in _shaded.Values) if (m) Destroy(m); _shaded.Clear();
            if (Root) Destroy(Root); Root = null; Meta = null;
            Pivot.SetParent(null); Pivot.position = Vector3.zero; Pivot.rotation = Quaternion.identity; Pivot.localScale = Vector3.one;
        }

        static float GuessUnit(Bounds b) { float m = Mathf.Max(b.size.x, b.size.y, b.size.z); return m > 3000 ? 0.001f : m > 600 ? 0.01f : 1f; }

        /// <summary>Unités → mètres, base de la maquette au centre et en y = 0.</summary>
        public void Normalize(float forced = 0)
        {
            if (!Root) return;
            var t = Root.transform; t.localPosition = Vector3.zero; t.localScale = Vector3.one; t.localRotation = Quaternion.identity;
            var raw = WorldBounds(); if (raw.size == Vector3.zero) raw = new Bounds(Vector3.up * .5f, Vector3.one);
            float k = forced > 0 ? forced : GuessUnit(raw); UnitScale = k;
            t.localScale = Vector3.one * k;
            var c = raw.center - Pivot.position;
            t.localPosition = new Vector3(-c.x * k, -(raw.min.y - Pivot.position.y) * k, -c.z * k);
            Box = LocalBounds();
            if (_shadowBlob) PlaceBlob();
        }
        Bounds WorldBounds()
        {
            bool any = false; var b = new Bounds();
            foreach (var r in Renderers) { if (!r) continue; if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds); }
            return b;
        }
        /// <summary>Boîte englobante dans le repère du pivot (indépendante de la pose en AR).</summary>
        public Bounds LocalBounds()
        {
            var w = WorldBounds(); var inv = Pivot.worldToLocalMatrix; var b = new Bounds(inv.MultiplyPoint3x4(w.center), Vector3.zero);
            foreach (var r in Renderers) { if (!r) continue; var rb = r.bounds; for (int i = 0; i < 8; i++) b.Encapsulate(inv.MultiplyPoint3x4(rb.center + Vector3.Scale(rb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)))); }
            return b;
        }

        void BuildColliders()
        {
            foreach (var r in Renderers)
            {
                var mf = r.GetComponent<MeshFilter>(); if (!mf || !mf.sharedMesh) continue;
                if (!mf.sharedMesh.isReadable) continue;   // maillage non lisible : non sélectionnable (rare)
                var mc = (r.gameObject.TryGetComponent(out MeshCollider __meshcollider) ? __meshcollider : r.gameObject.AddComponent<MeshCollider>());
                mc.sharedMesh = mf.sharedMesh;
            }
        }

        // ombre de contact (réalité augmentée et visite) : disque flou sous la maquette
        void MakeShadowBlob()
        {
            if (!_shadowBlob)
            {
                _shadowBlob = GameObject.CreatePrimitive(PrimitiveType.Quad); _shadowBlob.name = "Ombre de contact"; Destroy(_shadowBlob.GetComponent<Collider>());
                var tex = new Texture2D(128, 128, TextureFormat.RGBA32, false); var px = new Color32[128 * 128];
                for (int y = 0; y < 128; y++) for (int x = 0; x < 128; x++) { float d = Vector2.Distance(new Vector2(x, y), new Vector2(63.5f, 63.5f)) / 64f; byte a = (byte)(Mathf.Clamp01(1 - Mathf.SmoothStep(0.55f, 1f, d)) * 120); px[y * 128 + x] = new Color32(10, 18, 30, a); }
                tex.SetPixels32(px); tex.Apply();
                var m = Transparent(Shader.Find("Universal Render Pipeline/Unlit"), Color.white); m.SetTexture("_BaseMap", tex); m.renderQueue = 2900;
                var r = _shadowBlob.GetComponent<MeshRenderer>(); r.sharedMaterial = m; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
                _shadowBlob.transform.rotation = Quaternion.Euler(90, 0, 0);
            }
            _shadowBlob.transform.SetParent(Pivot, false); PlaceBlob();
        }
        void PlaceBlob() { _shadowBlob.transform.localPosition = new Vector3(Box.center.x, 0.004f, Box.center.z); _shadowBlob.transform.localRotation = Quaternion.Euler(90, 0, 0); _shadowBlob.transform.localScale = new Vector3(Box.size.x * 1.35f + .2f, Box.size.z * 1.35f + .2f, 1); }
        public void ShowShadowBlob(bool on) { if (_shadowBlob) _shadowBlob.SetActive(on); }

        // ------------------------------------------------------------ Styles d'affichage
        public void SetStyle(ViewStyle s) { Style = s; ApplyMaterials(); }
        Material ShadedOf(Material o)
        {
            if (!o) return _wireMat;
            if (_shaded.TryGetValue(o, out var m)) return m;
            Color c = o.HasProperty("_BaseColor") ? o.GetColor("_BaseColor") : o.HasProperty("baseColorFactor") ? o.GetColor("baseColorFactor") : o.HasProperty("_Color") ? o.color : new Color(.82f, .83f, .85f);
            m = new Material(Shader.Find("Universal Render Pipeline/Lit")); m.SetColor("_BaseColor", Color.Lerp(new Color(c.r, c.g, c.b, 1), Color.white, .25f)); m.SetFloat("_Smoothness", .15f);
            _shaded[o] = m; return m;
        }
        void ApplyMaterials()
        {
            foreach (var r in Renderers)
            {
                if (!r) continue; var o = _orig[r]; bool sel = _sel.Contains(r);
                bool wire = Style == ViewStyle.Wire;
                if (wire) EnsureWire(r);
                if (_wires.TryGetValue(r, out var w) && w) w.SetActive(wire && r.gameObject.activeInHierarchy);
                r.enabled = !wire || sel;
                if (sel) r.sharedMaterials = Enumerable.Repeat(Style == ViewStyle.XRay ? _hlX : _hl, o.Length).ToArray();
                else r.sharedMaterials = Style switch
                {
                    ViewStyle.Shaded => o.Select(ShadedOf).ToArray(),
                    ViewStyle.XRay => Enumerable.Repeat(_xray, o.Length).ToArray(),
                    _ => o,
                };
                r.shadowCastingMode = Style == ViewStyle.Materials || Style == ViewStyle.Shaded ? ShadowCastingMode.On : ShadowCastingMode.Off;
            }
            ApplySection();
        }
        void EnsureWire(Renderer r)
        {
            if (_wires.ContainsKey(r)) return;
            var mf = r.GetComponent<MeshFilter>(); if (!mf || !mf.sharedMesh || !mf.sharedMesh.isReadable) { _wires[r] = null; return; }
            var src = mf.sharedMesh; var tris = src.triangles; var edges = new HashSet<long>(); var idx = new List<int>();
            for (int i = 0; i < tris.Length; i += 3) for (int k = 0; k < 3; k++)
            {
                int a = tris[i + k], b = tris[i + (k + 1) % 3]; long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                if (edges.Add(key)) { idx.Add(a); idx.Add(b); }
            }
            var m = new Mesh { indexFormat = src.vertexCount > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16, vertices = src.vertices };
            m.SetIndices(idx, MeshTopology.Lines, 0);
            var go = new GameObject("Filaire"); go.transform.SetParent(r.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = m; var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = _wireMat; mr.shadowCastingMode = ShadowCastingMode.Off;
            _wires[r] = go;
        }

        // ------------------------------------------------------------ Sélection
        /// <summary>Élément BIM d'un objet touché : l'ancêtre le plus proche qui porte des données (ARCHIPLAN, IFC) ; sinon l'objet lui-même.</summary>
        public GameObject ElementOf(GameObject hit)
        {
            for (var t = hit.transform; t && Root && t != Root.transform; t = t.parent)
            {
                var b = t.GetComponent<BimNode>();
                if (b && b.HasInfo && !(b.ArchiPlan?["projet"] != null && b.ArchiPlan?["type"] == null)) return t.gameObject;
            }
            return hit;
        }
        public void Select(GameObject go, string from = "view")
        {
            Selected = go; _sel.Clear();
            if (go) foreach (var r in go.GetComponentsInChildren<Renderer>()) if (_orig.ContainsKey(r)) _sel.Add(r);
            ApplyMaterials();
            SelectionChanged?.Invoke(go, from);
        }
        public bool Pick(Ray ray, out RaycastHit hit, float max = 5000)
        {
            hit = default;
            var hits = Physics.RaycastAll(ray, max).Where(h => h.collider is MeshCollider && Root && h.collider.transform.IsChildOf(Root.transform) && IsShown(h.collider.gameObject)).OrderBy(h => h.distance);
            foreach (var h in hits) { if (SectionHeight.HasValue && Pivot.InverseTransformPoint(h.point).y > SectionHeight.Value + 1e-3f) continue; hit = h; return true; }
            return false;
        }
        public bool IsShown(GameObject g) { var r = g.GetComponent<Renderer>(); return g.activeInHierarchy && (!r || r.enabled || Style == ViewStyle.Wire); }

        // ------------------------------------------------------------ Visibilité
        public void SetVisible(GameObject g, bool v) { g.SetActive(v); ApplyMaterials(); }
        public void Isolate(GameObject g)
        {
            foreach (Transform t in Root.GetComponentsInChildren<Transform>(true)) if (t != Root.transform && !t.name.StartsWith("Filaire")) t.gameObject.SetActive(false);
            for (var t = g.transform; t && t != Root.transform; t = t.parent) t.gameObject.SetActive(true);
            foreach (Transform t in g.GetComponentsInChildren<Transform>(true)) if (!t.name.StartsWith("Filaire")) t.gameObject.SetActive(true);
            ApplyMaterials();
        }
        public void ShowAll() { if (!Root) return; foreach (Transform t in Root.GetComponentsInChildren<Transform>(true)) if (!t.name.StartsWith("Filaire")) t.gameObject.SetActive(true); ApplyMaterials(); }

        /// <summary>Coupe horizontale : masque les éléments situés entièrement au-dessus du plan (hauteur en mètres au-dessus de la base).</summary>
        public void SetSection(float? h) { SectionHeight = h; ApplySection(); }
        void ApplySection()
        {
            var inv = Pivot.worldToLocalMatrix;
            foreach (var r in Renderers)
            {
                if (!r) continue;
                bool above = SectionHeight.HasValue && inv.MultiplyPoint3x4(r.bounds.min).y > SectionHeight.Value;
                bool wire = Style == ViewStyle.Wire && !_sel.Contains(r);
                r.enabled = !above && !wire;
                if (_wires.TryGetValue(r, out var w) && w) w.SetActive(!above && Style == ViewStyle.Wire);
            }
        }

        // ------------------------------------------------------------ Libellés
        static readonly Dictionary<string, string> KindFr = new Dictionary<string, string> { { "wall", "Mur" }, { "slab", "Dalle" }, { "roof", "Toiture" }, { "col", "Poteau" }, { "beam", "Poutre" }, { "stair", "Escalier" }, { "inst", "Composant" }, { "zone", "Volume" }, { "terrain", "Terrain" }, { "socle", "Socle" }, { "environnement", "Environnement" } };
        public static readonly Dictionary<string, string> IfcFr = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            {"IfcProject","Projet"},{"IfcSite","Site"},{"IfcBuilding","Bâtiment"},{"IfcBuildingStorey","Niveau"},{"IfcSpace","Espace"},{"IfcWall","Mur"},{"IfcWallStandardCase","Mur"},{"IfcSlab","Dalle"},{"IfcRoof","Toiture"},
            {"IfcDoor","Porte"},{"IfcWindow","Fenêtre"},{"IfcColumn","Poteau"},{"IfcBeam","Poutre"},{"IfcStair","Escalier"},{"IfcStairFlight","Volée d'escalier"},{"IfcRailing","Garde-corps"},{"IfcCovering","Revêtement"},
            {"IfcFurnishingElement","Mobilier"},{"IfcFurniture","Mobilier"},{"IfcMember","Membrure"},{"IfcPlate","Plaque"},{"IfcCurtainWall","Mur-rideau"},{"IfcBuildingElementProxy","Élément"},{"IfcFooting","Fondation"},
            {"IfcPile","Pieu"},{"IfcRamp","Rampe"},{"IfcOpeningElement","Ouverture"},{"IfcFlowTerminal","Terminal"},{"IfcFlowSegment","Réseau"},{"IfcFlowFitting","Raccord"},{"IfcSanitaryTerminal","Sanitaire"},
        };
        public string Label(GameObject g)
        {
            if (!g) return "";
            if (Root && g == Root) return Meta?.Name ?? "Maquette";
            var b = g.GetComponent<BimNode>(); var ap = b?.ArchiPlan; var ifc = b?.Ifc;
            if (ifc != null) { var n = (string)ifc["nom"]; if (!string.IsNullOrEmpty(n)) return n; var c = (string)ifc["classe"] ?? ""; return IfcFr.TryGetValue(c, out var f) ? f : c; }
            if (ap?["famille"] != null) return $"{ap["famille"]}{(ap["id"] != null ? " " + ap["id"] : "")}";
            if (ap?["type"] != null && ap["id"] != null && KindFr.TryGetValue((string)ap["type"], out var k)) return (g.name.StartsWith("Cloison") ? "Cloison" : k) + " " + ap["id"];
            return (b?.NodeName ?? g.name).Replace('_', ' ');
        }
        public string Kind(GameObject g)
        {
            var b = g ? g.GetComponent<BimNode>() : null; var ap = b?.ArchiPlan; var ifc = b?.Ifc;
            if (ifc != null) { var c = (string)ifc["classe"] ?? ""; return IfcFr.TryGetValue(c, out var f) ? f : c; }
            if (ap?["type"] != null) return KindFr.TryGetValue((string)ap["type"], out var k) ? k : (string)ap["type"];
            if (b?.Extras?["niveau"] != null) return "Niveau";
            if (g && g.GetComponent<Renderer>()) return "Maillage";
            return "";
        }
        public int CountElements()
        {
            if (!Root) return 0; int n = 0;
            foreach (var b in Root.GetComponentsInChildren<BimNode>(true)) if (b.gameObject != Root && (b.ArchiPlan?["type"] != null || (b.Ifc != null && !new[] { "IfcProject", "IfcSite", "IfcBuilding", "IfcBuildingStorey" }.Contains((string)b.Ifc["classe"])))) n++;
            return n;
        }
    }
}
