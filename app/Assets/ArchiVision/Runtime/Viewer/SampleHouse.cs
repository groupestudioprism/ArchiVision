// ARCHIVISION · maquette d'exemple : petite maison (structure et données au format de l'export ARCHIPLAN), identique à celle du site.
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace StudioPrism.ArchiVision.Viewer
{
    public static class SampleHouse
    {
        static Material M(string nom, Color c, float smooth = .15f, float alpha = 1, float metal = 0)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = nom };
            m.SetColor("_BaseColor", new Color(c.r, c.g, c.b, alpha)); m.SetFloat("_Smoothness", smooth); m.SetFloat("_Metallic", metal);
            if (alpha < 1)
            {
                m.SetFloat("_Surface", 1); m.SetOverrideTag("RenderType", "Transparent"); m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha); m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                m.SetInt("_ZWrite", 0); m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.renderQueue = (int)RenderQueue.Transparent;
            }
            return m;
        }
        static Color H(string hex) { ColorUtility.TryParseHtmlString(hex, out var c); return c; }

        public static GameObject Build()
        {
            var enduit = M("Enduit ciment", H("#EDE6DA")); var beton = M("Béton armé C30/37", H("#B9B9B4")); var parquet = M("Parquet chêne", H("#B98B5E"), .4f);
            var tuile = M("Tuile terre cuite", H("#A6533A"), .25f); var bois = M("Bois peint", H("#3E4A57"), .5f); var alu = M("Aluminium laqué", H("#2B3440"), .6f, 1, .4f);
            var verre = M("Vitrage", H("#9FC9E0"), .95f, .32f); var platre = M("Plaque de plâtre BA13", H("#F4F3EF")); var gazon = M("Gazon", H("#7C9A5A"), 0);

            var root = new GameObject("Maison d'exemple");
            Bim(root, new JObject { ["archiplan"] = new JObject { ["version"] = "exemple", ["projet"] = "Maison d'exemple", ["unites"] = "m" } });
            var lv = new GameObject("RDC"); lv.transform.SetParent(root.transform, false); Bim(lv, new JObject { ["niveau"] = "RDC", ["altitude"] = 0 });
            int id = 1;
            GameObject El(string name, JObject info, params (Material m, float x0, float y0, float z0, float x1, float y1, float z1)[] parts)
            {
                var g = new GameObject($"{name} {id}"); g.transform.SetParent(lv.transform, false);
                info["id"] = id++; Bim(g, new JObject { ["archiplan"] = info });
                foreach (var p in parts)
                {
                    var b = Box();
                    b.name = $"{g.name} - {p.m.name}"; b.transform.SetParent(g.transform, false);
                    b.transform.localPosition = new Vector3((p.x0 + p.x1) / 2, (p.y0 + p.y1) / 2, (p.z0 + p.z1) / 2); b.transform.localScale = new Vector3(p.x1 - p.x0, p.y1 - p.y0, p.z1 - p.z0);
                    b.GetComponent<MeshRenderer>().sharedMaterial = p.m;
                }
                return g;
            }
            JArray Couches(params (string m, double e)[] c) { var a = new JArray(); foreach (var x in c) a.Add(new JObject { ["materiau"] = x.m, ["epaisseur"] = x.e }); return a; }
            const float W = 10, D = 7, Ht = 2.7f, T = .3f;
            JObject Mur(float len) => new JObject { ["type"] = "wall", ["composition"] = "Parpaing 20 + doublage laine minérale 100 + BA13", ["couches"] = Couches(("Enduit ciment", .015), ("Bloc béton creux", .2), ("Laine minérale", .1), ("Plaque de plâtre BA13", .013)), ["U"] = 0.31, ["lot"] = "02 Gros œuvre", ["longueur"] = len };
            El("Dalle", new JObject { ["type"] = "slab", ["composition"] = "Dallage béton 12 cm sur isolant", ["couches"] = Couches(("Parquet chêne", .015), ("Béton armé C30/37", .12), ("Polystyrène extrudé", .1)), ["U"] = 0.27, ["lot"] = "02 Gros œuvre" },
                (beton, -.1f, -.25f, -.1f, W + .1f, -.015f, D + .1f), (parquet, T, -.015f, T, W - T, 0, D - T));
            (Material, float, float, float, float, float, float)[] WallX(float z0, params (float a, float b, float s, float h)[] holes)
            {
                var l = new List<(Material, float, float, float, float, float, float)>(); float x = 0;
                foreach (var o in holes) { l.Add((enduit, x, 0, z0, o.a, Ht, z0 + T)); if (o.s > 0) l.Add((enduit, o.a, 0, z0, o.b, o.s, z0 + T)); l.Add((enduit, o.a, o.h, z0, o.b, Ht, z0 + T)); x = o.b; }
                l.Add((enduit, x, 0, z0, W, Ht, z0 + T)); return l.ToArray();
            }
            (Material, float, float, float, float, float, float)[] WallZ(float x0, params (float a, float b, float s, float h)[] holes)
            {
                var l = new List<(Material, float, float, float, float, float, float)>(); float z = T;
                foreach (var o in holes) { l.Add((enduit, x0, 0, z, x0 + T, Ht, o.a)); if (o.s > 0) l.Add((enduit, x0, 0, o.a, x0 + T, o.s, o.b)); l.Add((enduit, x0, o.h, o.a, x0 + T, Ht, o.b)); z = o.b; }
                l.Add((enduit, x0, 0, z, x0 + T, Ht, D - T)); return l.ToArray();
            }
            El("Mur", Mur(W), WallX(D - T, (1.6f, 2.5f, 0, 2.15f), (4.4f, 7.4f, 0, 2.15f)));
            El("Mur", Mur(W), WallX(0, (1.8f, 3f, 1f, 2.15f), (6.6f, 7.8f, 1f, 2.15f)));
            El("Mur", Mur(D), WallZ(0, (2.9f, 4.1f, 1f, 2.15f)));
            El("Mur", Mur(D), WallZ(W - T, (2.9f, 4.1f, 1f, 2.15f)));
            El("Cloison", new JObject { ["type"] = "wall", ["composition"] = "Cloison 72/48", ["couches"] = Couches(("Plaque de plâtre BA13", .013), ("Laine minérale", .045), ("Plaque de plâtre BA13", .013)), ["U"] = 0.62, ["lot"] = "05 Plâtrerie" },
                (platre, 6.2f, 0, T, 6.27f, Ht, 3.2f), (platre, 6.2f, 2.15f, 3.2f, 6.27f, Ht, 4.1f), (platre, 6.2f, 0, 4.1f, 6.27f, Ht, D - T));
            void Win(string name, float x0, float y0, float z0, float x1, float y1, float z1, string fam, string dims)
            {
                float th = .06f, fr = .06f; bool horiz = x1 - x0 > z1 - z0; var p = new List<(Material, float, float, float, float, float, float)>();
                if (horiz) { float zc = (z0 + z1) / 2; p.Add((alu, x0, y0, zc - th, x1, y0 + fr, zc + th)); p.Add((alu, x0, y1 - fr, zc - th, x1, y1, zc + th)); p.Add((alu, x0, y0, zc - th, x0 + fr, y1, zc + th)); p.Add((alu, x1 - fr, y0, zc - th, x1, y1, zc + th)); p.Add((verre, x0 + fr, y0 + fr, zc - .01f, x1 - fr, y1 - fr, zc + .01f)); }
                else { float xc = (x0 + x1) / 2; p.Add((alu, xc - th, y0, z0, xc + th, y0 + fr, z1)); p.Add((alu, xc - th, y1 - fr, z0, xc + th, y1, z1)); p.Add((alu, xc - th, y0, z0, xc + th, y1, z0 + fr)); p.Add((alu, xc - th, y0, z1 - fr, xc + th, y1, z1)); p.Add((verre, xc - .01f, y0 + fr, z0 + fr, xc + .01f, y1 - fr, z1 - fr)); }
                El(name, new JObject { ["type"] = "inst", ["famille"] = fam, ["categorie"] = "Menuiseries", ["typeFamille"] = dims, ["parametres"] = new JObject { ["largeur"] = System.Math.Round(horiz ? x1 - x0 : z1 - z0, 2), ["hauteur"] = System.Math.Round(y1 - y0, 2) }, ["Uw"] = 1.3 }, p.ToArray());
            }
            Win("Baie coulissante", 4.4f, 0, D - T, 7.4f, 2.15f, D, "Baie coulissante alu", "300×215");
            Win("Fenêtre", 1.8f, 1, 0, 3, 2.15f, T, "Fenêtre 2 vantaux", "120×115"); Win("Fenêtre", 6.6f, 1, 0, 7.8f, 2.15f, T, "Fenêtre 2 vantaux", "120×115");
            Win("Fenêtre", 0, 1, 2.9f, T, 2.15f, 4.1f, "Fenêtre 2 vantaux", "120×115"); Win("Fenêtre", W - T, 1, 2.9f, W, 2.15f, 4.1f, "Fenêtre 2 vantaux", "120×115");
            El("Porte d'entrée", new JObject { ["type"] = "inst", ["famille"] = "Porte d'entrée bois", ["categorie"] = "Menuiseries", ["typeFamille"] = "90×215", ["Ud"] = 1.4 }, (bois, 1.6f, 0, D - T + .1f, 2.5f, 2.15f, D - T + .16f));
            // toiture à deux pans
            var roof = new GameObject($"Toiture {id}"); roof.transform.SetParent(lv.transform, false);
            Bim(roof, new JObject { ["archiplan"] = new JObject { ["id"] = id++, ["type"] = "roof", ["composition"] = "Charpente bois, laine 300, tuiles", ["couches"] = Couches(("Tuile terre cuite", .02), ("Laine minérale", .3), ("Plaque de plâtre BA13", .013)), ["U"] = 0.14, ["lot"] = "03 Charpente couverture", ["pente"] = "35°" } });
            float ov = .4f, rise = Mathf.Tan(35 * Mathf.Deg2Rad) * (D / 2 + ov), len = Mathf.Sqrt((D / 2 + ov) * (D / 2 + ov) + rise * rise);
            foreach (int s in new[] { -1, 1 })
            {
                var pan = Box(); pan.name = "Pan de toiture - Tuile terre cuite"; pan.transform.SetParent(roof.transform, false);
                pan.transform.localScale = new Vector3(W + 2 * ov, .12f, len); pan.transform.localPosition = new Vector3(W / 2, Ht + rise / 2 + .05f, D / 2 + s * (D / 2 + ov) / 2);
                pan.transform.localRotation = Quaternion.Euler(s * Mathf.Atan2(rise, D / 2 + ov) * Mathf.Rad2Deg, 0, 0); pan.GetComponent<MeshRenderer>().sharedMaterial = tuile;
            }
            float gr = rise * (D / 2) / (D / 2 + ov);
            foreach (float x in new[] { 0f, W - T })
            {
                var g = new GameObject("Pignon - Enduit ciment"); g.transform.SetParent(roof.transform, false);
                g.AddComponent<MeshFilter>().sharedMesh = Prism(x, x + T, Ht, gr, D); g.AddComponent<MeshRenderer>().sharedMaterial = enduit;
            }
            El("Table de séjour", new JObject { ["type"] = "inst", ["famille"] = "Table de séjour", ["categorie"] = "Mobilier", ["typeFamille"] = "180×90" }, (bois, 2.2f, .72f, 2f, 4f, .76f, 2.9f), (bois, 2.3f, 0, 2.1f, 2.36f, .72f, 2.16f), (bois, 3.84f, 0, 2.1f, 3.9f, .72f, 2.16f), (bois, 2.3f, 0, 2.74f, 2.36f, .72f, 2.8f), (bois, 3.84f, 0, 2.74f, 3.9f, .72f, 2.8f));
            El("Cuisine linéaire", new JObject { ["type"] = "inst", ["famille"] = "Cuisine linéaire", ["categorie"] = "Mobilier", ["typeFamille"] = "300×65" }, (platre, T, 0, .35f, T + 3f, .9f, .95f), (beton, T, .9f, .33f, T + 3f, .94f, .97f));
            var site = new GameObject("Terrain"); site.transform.SetParent(root.transform, false); Bim(site, new JObject { ["archiplan"] = new JObject { ["type"] = "terrain", ["source"] = "Maquette d'exemple" } });
            var t = Box(); t.name = "Terrain - Gazon"; t.transform.SetParent(site.transform, false);
            t.transform.localScale = new Vector3(W + 8, .1f, D + 8); t.transform.localPosition = new Vector3(W / 2, -.3f, D / 2); t.GetComponent<MeshRenderer>().sharedMaterial = gazon;
            // les cubes primitifs partagent un maillage lisible : il sert aux collisions de visée
            return root;
        }

        static Mesh Prism(float x0, float x1, float y0, float h, float d)
        {
            var v = new List<Vector3>(); var tr = new List<int>();
            Vector3 A(float x) => new Vector3(x, y0, 0); Vector3 B(float x) => new Vector3(x, y0, d); Vector3 C(float x) => new Vector3(x, y0 + h, d / 2);
            void Tri(Vector3 a, Vector3 b, Vector3 c) { int i = v.Count; v.Add(a); v.Add(b); v.Add(c); tr.Add(i); tr.Add(i + 1); tr.Add(i + 2); }
            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 e) { Tri(a, b, c); Tri(a, c, e); }
            Tri(A(x0), C(x0), B(x0)); Tri(A(x1), B(x1), C(x1));
            Quad(A(x0), A(x1), C(x1), C(x0)); Quad(B(x0), C(x0), C(x1), B(x1)); Quad(A(x0), B(x0), B(x1), A(x1));
            var m = new Mesh { name = "Pignon" }; m.SetVertices(v); m.SetTriangles(tr, 0); m.RecalculateNormals(); m.RecalculateBounds(); return m;
        }

        static Mesh _cube;
        /// <summary>Cube unité généré par le code (maillage lisible : sert aussi aux collisions de visée).</summary>
        static GameObject Box()
        {
            if (!_cube)
            {
                var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var tr = new List<int>();
                Vector3[] dirs = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
                foreach (var d in dirs)
                {
                    var u = Mathf.Abs(d.y) > .5f ? Vector3.right : Vector3.up; var w = Vector3.Cross(d, u); int i = v.Count;
                    foreach (var (a, c) in new[] { (-1f, -1f), (1f, -1f), (1f, 1f), (-1f, 1f) }) { v.Add((d + u * a + w * c) * .5f); n.Add(d); uv.Add(new Vector2((a + 1) / 2, (c + 1) / 2)); }
                    tr.AddRange(new[] { i, i + 2, i + 1, i, i + 3, i + 2 });
                }
                _cube = new Mesh { name = "Cube" }; _cube.SetVertices(v); _cube.SetNormals(n); _cube.SetUVs(0, uv); _cube.SetTriangles(tr, 0); _cube.RecalculateBounds();
                // orientation des faces : vers l'extérieur
                var tris = _cube.triangles; var verts = _cube.vertices;
                for (int k = 0; k < tris.Length; k += 3) { var c0 = verts[tris[k]]; var nn = Vector3.Cross(verts[tris[k + 1]] - c0, verts[tris[k + 2]] - c0); if (Vector3.Dot(nn, c0) < 0) { (tris[k + 1], tris[k + 2]) = (tris[k + 2], tris[k + 1]); } }
                _cube.triangles = tris;
            }
            var g = new GameObject("Boîte"); g.AddComponent<MeshFilter>().sharedMesh = _cube; g.AddComponent<MeshRenderer>(); return g;
        }
        static void Bim(GameObject g, JObject extras) { var b = g.AddComponent<BimNode>(); b.NodeName = g.name; b.ExtrasJson = extras.ToString(Newtonsoft.Json.Formatting.None); }
    }
}
