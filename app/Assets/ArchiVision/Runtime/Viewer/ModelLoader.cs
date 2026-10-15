// ARCHIVISION · chargement des maquettes (glTFast) avec leurs données BIM (« extras » glTF : ARCHIPLAN, IFC converti, Revit…).
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using GLTFast;
using GLTFast.Logging;
using Newtonsoft.Json.Linq;
using StudioPrism.ArchiVision.Services;
using UnityEngine;

namespace StudioPrism.ArchiVision.Viewer
{
    /// <summary>Données BIM d'un nœud de la maquette (copie des « extras » glTF).</summary>
    public class BimNode : MonoBehaviour
    {
        public string NodeName;
        public string ExtrasJson;
        JObject _extras; bool _parsed;
        public JObject Extras { get { if (!_parsed) { _parsed = true; try { _extras = string.IsNullOrEmpty(ExtrasJson) ? null : JObject.Parse(ExtrasJson); } catch { } } return _extras; } }
        public JObject ArchiPlan => Extras?["archiplan"] as JObject;
        public JObject Ifc => Extras?["ifc"] as JObject;
        public bool HasInfo => ArchiPlan != null || Ifc != null || Extras?["bim"] != null;
    }

    /// <summary>Instancieur glTFast qui garde la correspondance « index de nœud glTF → GameObject ».</summary>
    public class BimInstantiator : GameObjectInstantiator
    {
        public BimInstantiator(IGltfReadable gltf, Transform parent) : base(gltf, parent) { }
        /// <summary>Nœuds créés (index glTF → GameObject), disponibles après l'instanciation.</summary>
        public Dictionary<uint, GameObject> NodeObjects => m_Nodes != null ? new Dictionary<uint, GameObject>(m_Nodes) : new Dictionary<uint, GameObject>();
    }

    public static class ModelLoader
    {
        /// <summary>Charge un GLB (octets) sous un nouveau GameObject « Maquette ».</summary>
        public static async Task<GameObject> LoadGlb(byte[] bytes, string name, Action<string> step = null)
        {
            step?.Invoke("Lecture de la 3D…");
            var gltf = new GltfImport(null, null, null, new ConsoleLogger());
            bool ok = await gltf.LoadGltfBinary(bytes, null, new ImportSettings { GenerateMipMaps = true, AnisotropicFilterLevel = 2 });
            if (!ok) { gltf.Dispose(); throw new AVException("app/glb", "Le fichier reçu n'est pas un GLB valide."); }
            var root = new GameObject(string.IsNullOrEmpty(name) ? "Maquette" : name);
            var inst = new BimInstantiator(gltf, root.transform);
            step?.Invoke("Construction de la maquette…");
            ok = await gltf.InstantiateMainSceneAsync(inst);
            if (!ok) { UnityEngine.Object.Destroy(root); gltf.Dispose(); throw new AVException("app/glb", "glTFast n'a pas pu instancier la scène."); }
            AttachBim(root, inst, ReadGlbJson(bytes));
            root.AddComponent<GltfKeeper>().Import = gltf;   // libère textures et maillages à la destruction
            return root;
        }

        /// <summary>Charge un glTF (JSON + fichiers .bin et textures dans le même dossier).</summary>
        public static async Task<GameObject> LoadGltfFile(string path, string name, Action<string> step = null)
        {
            step?.Invoke("Lecture du glTF…");
            var gltf = new GltfImport(null, null, null, new ConsoleLogger());
            bool ok = await gltf.Load(new Uri(Path.GetFullPath(path)).AbsoluteUri, new ImportSettings { GenerateMipMaps = true });
            if (!ok) { gltf.Dispose(); throw new AVException("app/gltf", "glTF illisible (vérifiez que les fichiers .bin et les textures sont choisis avec)."); }
            var root = new GameObject(string.IsNullOrEmpty(name) ? "Maquette" : name);
            var inst = new BimInstantiator(gltf, root.transform);
            ok = await gltf.InstantiateMainSceneAsync(inst);
            if (!ok) { UnityEngine.Object.Destroy(root); throw new AVException("app/gltf", "Le glTF n'a pas pu être construit."); }
            JObject json = null; try { json = JObject.Parse(File.ReadAllText(path)); } catch { }
            AttachBim(root, inst, json);
            root.AddComponent<GltfKeeper>().Import = gltf;
            return root;
        }

        static JObject ReadGlbJson(byte[] b)
        {
            try
            {
                if (b.Length < 20 || BitConverter.ToUInt32(b, 0) != 0x46546C67) return null;
                int len = (int)BitConverter.ToUInt32(b, 12); if (BitConverter.ToUInt32(b, 16) != 0x4E4F534A) return null;
                return JObject.Parse(Encoding.UTF8.GetString(b, 20, len));
            }
            catch { return null; }
        }

        static void AttachBim(GameObject root, BimInstantiator inst, JObject json)
        {
            var nodes = json?["nodes"] as JArray;
            foreach (var kv in inst.NodeObjects)
            {
                var n = nodes != null && kv.Key < nodes.Count ? nodes[(int)kv.Key] as JObject : null;
                var b = kv.Value.AddComponent<BimNode>();
                b.NodeName = (string)n?["name"] ?? kv.Value.name;
                if (!string.IsNullOrEmpty(b.NodeName)) kv.Value.name = b.NodeName;
                var ex = n?["extras"];
                if (ex != null && ex.Type == JTokenType.Object && ex.HasValues) b.ExtrasJson = ex.ToString(Newtonsoft.Json.Formatting.None);
            }
            // extras de la scène glTF (ARCHIPLAN : projet, lieu…) → racine
            var sc = json?["scenes"]?[(int?)json["scene"] ?? 0]?["extras"];
            var rb = root.AddComponent<BimNode>(); rb.NodeName = root.name;
            if (sc != null && sc.HasValues) rb.ExtrasJson = sc.ToString(Newtonsoft.Json.Formatting.None);
            else
            {   // l'export ARCHIPLAN met ses informations de projet sur le premier nœud racine
                var top = root.transform.childCount == 1 ? root.transform.GetChild(0).GetComponent<BimNode>() : null;
                if (top?.ArchiPlan?["projet"] != null) rb.ExtrasJson = top.ExtrasJson;
            }
        }

        /// <summary>Paquet ZIP d'ARCHIPLAN (GLB + IFC + métré) : renvoie le chemin du GLB (ou de l'IFC) extrait.</summary>
        public static string ExtractFromZip(string zipPath)
        {
            using var z = ZipFile.OpenRead(zipPath);
            var e = z.Entries.FirstOrDefault(x => x.Name.EndsWith(".glb", StringComparison.OrdinalIgnoreCase)) ?? z.Entries.FirstOrDefault(x => x.Name.EndsWith(".ifc", StringComparison.OrdinalIgnoreCase));
            if (e == null) throw new AVException("app/zip", "L'archive ne contient ni GLB ni IFC.");
            var dst = Path.Combine(Application.temporaryCachePath, "zip", e.Name); Directory.CreateDirectory(Path.GetDirectoryName(dst));
            e.ExtractToFile(dst, true); return dst;
        }
    }

    /// <summary>Garde l'import glTFast vivant tant que la maquette existe, puis libère sa mémoire.</summary>
    public class GltfKeeper : MonoBehaviour
    {
        public GltfImport Import;
        void OnDestroy() { Import?.Dispose(); Import = null; }
    }
}
