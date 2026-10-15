// ARCHIVISION · bibliothèque sur l'appareil : maquettes (GLB, IFC d'origine) et photos annotées, rangées par compte Studio Prism.
// <persistentDataPath>/users/<uid>/models/<id>/{model.glb, source.ifc, thumb.jpg, meta.json} et .../photos/<id>/{photo.jpg, thumb.jpg, meta.json}
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

namespace StudioPrism.ArchiVision.Services
{
    public class ModelMeta
    {
        public string Id, Name, Kind = "glb"; public long SourceSize, GlbSize; public DateTime CreatedUtc, OpenedUtc; public float UnitScale; public int Objects, Triangles; public float[] Dims;
        public bool IsSample => Id == "sample";
    }
    public class PhotoMeta { public string Id, ModelId, ModelName, Note = "", Mode = "tour"; public int W, H; public long Size; public DateTime CreatedUtc; }

    public static class Library
    {
        static string UserDir => Path.Combine(Application.persistentDataPath, "users", Account.Me?.Uid ?? "_");
        static string ModelsDir => Path.Combine(UserDir, "models");
        static string PhotosDir => Path.Combine(UserDir, "photos");
        public static string NewId(string p) => p + DateTime.UtcNow.Ticks.ToString("x") + UnityEngine.Random.Range(0x1000, 0xffff).ToString("x");
        public static event Action Changed;
        public static void NotifyChanged() => Changed?.Invoke();

        static T ReadJson<T>(string path) where T : class { try { return File.Exists(path) ? JsonConvert.DeserializeObject<T>(File.ReadAllText(path)) : null; } catch { return null; } }
        static void WriteJson(string path, object o) { Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, JsonConvert.SerializeObject(o, Formatting.Indented)); }

        // ------------------------------------------------------------ Maquettes
        public static List<ModelMeta> Models()
        {
            if (!Directory.Exists(ModelsDir)) return new List<ModelMeta>();
            return Directory.GetDirectories(ModelsDir).Select(d => ReadJson<ModelMeta>(Path.Combine(d, "meta.json"))).Where(m => m != null)
                .OrderByDescending(m => m.OpenedUtc > m.CreatedUtc ? m.OpenedUtc : m.CreatedUtc).ToList();
        }
        public static string ModelDir(string id) => Path.Combine(ModelsDir, id);
        public static string GlbPath(string id) => Path.Combine(ModelDir(id), "model.glb");
        public static string SourcePath(string id, string ext) => Path.Combine(ModelDir(id), "source." + ext);
        public static string ThumbPath(string id) => Path.Combine(ModelDir(id), "thumb.jpg");
        public static ModelMeta GetModel(string id) => ReadJson<ModelMeta>(Path.Combine(ModelDir(id), "meta.json"));
        public static void SaveModel(ModelMeta m) { WriteJson(Path.Combine(ModelDir(m.Id), "meta.json"), m); Changed?.Invoke(); }
        public static void DeleteModel(string id)
        {
            try { Directory.Delete(ModelDir(id), true); } catch { }
            foreach (var p in Photos().Where(p => p.ModelId == id)) DeletePhoto(p.Id, false);
            Changed?.Invoke();
        }

        // ------------------------------------------------------------ Photos
        public static List<PhotoMeta> Photos(string modelId = null)
        {
            if (!Directory.Exists(PhotosDir)) return new List<PhotoMeta>();
            return Directory.GetDirectories(PhotosDir).Select(d => ReadJson<PhotoMeta>(Path.Combine(d, "meta.json"))).Where(p => p != null && (modelId == null || p.ModelId == modelId))
                .OrderByDescending(p => p.CreatedUtc).ToList();
        }
        public static string PhotoPath(string id) => Path.Combine(PhotosDir, id, "photo.jpg");
        public static string PhotoThumbPath(string id) => Path.Combine(PhotosDir, id, "thumb.jpg");
        public static PhotoMeta GetPhoto(string id) => ReadJson<PhotoMeta>(Path.Combine(PhotosDir, id, "meta.json"));
        public static void SavePhotoMeta(PhotoMeta p) { WriteJson(Path.Combine(PhotosDir, p.Id, "meta.json"), p); Changed?.Invoke(); }
        public static PhotoMeta AddPhoto(Texture2D tex, string modelId, string modelName, string mode)
        {
            var p = new PhotoMeta { Id = NewId("p_"), ModelId = modelId, ModelName = modelName, Mode = mode, W = tex.width, H = tex.height, CreatedUtc = DateTime.UtcNow };
            Directory.CreateDirectory(Path.Combine(PhotosDir, p.Id));
            var jpg = tex.EncodeToJPG(90); File.WriteAllBytes(PhotoPath(p.Id), jpg); p.Size = jpg.Length;
            File.WriteAllBytes(PhotoThumbPath(p.Id), Thumb(tex, 480).EncodeToJPG(78));
            SavePhotoMeta(p); return p;
        }
        public static void DeletePhoto(string id, bool notify = true) { try { Directory.Delete(Path.Combine(PhotosDir, id), true); } catch { } if (notify) Changed?.Invoke(); }

        // ------------------------------------------------------------ Stockage
        public static long UsedBytes()
        {
            try { return Directory.Exists(UserDir) ? new DirectoryInfo(UserDir).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length) : 0; } catch { return 0; }
        }

        /// <summary>Réduction d'une texture (vignettes), sur le GPU.</summary>
        public static Texture2D Thumb(Texture2D src, int maxW)
        {
            float k = Mathf.Min(1f, maxW / (float)src.width); int w = Mathf.Max(1, (int)(src.width * k)), h = Mathf.Max(1, (int)(src.height * k));
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32); Graphics.Blit(src, rt);
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var t = new Texture2D(w, h, TextureFormat.RGB24, false); t.ReadPixels(new Rect(0, 0, w, h), 0, 0); t.Apply();
            RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt); return t;
        }
        public static Texture2D LoadImage(string path)
        {
            if (!File.Exists(path)) return null;
            var t = new Texture2D(2, 2, TextureFormat.RGB24, false); t.LoadImage(File.ReadAllBytes(path)); t.wrapMode = TextureWrapMode.Clamp; return t;
        }
    }
}
