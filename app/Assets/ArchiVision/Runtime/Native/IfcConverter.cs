// ARCHIVISION · conversion IFC → GLB (téléphone) : le convertisseur web d'ARCHIVISION (web-ifc + Three.js) tourne dans une WebView invisible.
// Le GLB produit garde la hiérarchie spatiale (projet, site, bâtiment, niveaux) et les données IFC de chaque élément (« extras »).
using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace StudioPrism.ArchiVision.Native
{
    public static class IfcConverter
    {
        public const string PageUrl = "https://groupestudioprism.github.io/ArchiVision/ifc-convert.html";
        static TaskCompletionSource<string> _tcs; static Action<string> _progress;

        public static async Task<byte[]> Convert(string ifcPath, Action<string> progress)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            _tcs?.TrySetResult("!annulé"); _tcs = new TaskCompletionSource<string>(); _progress = progress;
            var outPath = Path.Combine(Application.temporaryCachePath, "ifc-" + DateTime.UtcNow.Ticks + ".glb");
            using (var c = new AndroidJavaClass("com.studioprism.archivision.AVIfc")) c.CallStatic("convert", ifcPath, outPath, PageUrl);
            var t = await Task.WhenAny(_tcs.Task, Task.Delay(TimeSpan.FromMinutes(10)));
            if (t != _tcs.Task) throw new AVException("app/ifc", "La conversion IFC a pris trop de temps.");
            var r = _tcs.Task.Result;
            if (r.StartsWith("!")) throw new AVException("app/ifc", "Échec de la conversion IFC : " + r.Substring(1));
            var p = r.Split('|')[0]; var bytes = File.ReadAllBytes(p); try { File.Delete(p); } catch { }
            return bytes;
#else
            await Task.Yield();
            throw new AVException("app/ifc", "La conversion IFC se fait sur le téléphone ou la tablette (Android). Dans l'éditeur, importez un GLB.");
#endif
        }
        public static void OnProgress(string m) => _progress?.Invoke(m);
        public static void OnDone(string r) => _tcs?.TrySetResult(r ?? "!");
    }
}
