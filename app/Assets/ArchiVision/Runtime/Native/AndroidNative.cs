// ARCHIVISION · pont vers le code Android (Plugins/Android/AVNative.java). Dans l'éditeur : équivalents simples pour les tests.
using System;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;

namespace StudioPrism.ArchiVision.Native
{
    public class AndroidNative : MonoBehaviour
    {
        public static AndroidNative I { get; private set; }
        TaskCompletionSource<string[]> _pick; TaskCompletionSource<string> _bio;
        void Awake() { I = this; gameObject.name = "AVNative"; DontDestroyOnLoad(gameObject); }

#if UNITY_ANDROID && !UNITY_EDITOR
        static AndroidJavaClass _cls;
        static AndroidJavaClass Cls => _cls ??= new AndroidJavaClass("com.studioprism.archivision.AVNative");
#endif

        /// <summary>Choix d'un ou plusieurs fichiers ; les fichiers sont copiés dans le cache de l'app. Tableau vide si annulé.</summary>
        public Task<string[]> PickFiles()
        {
            _pick?.TrySetResult(new string[0]); _pick = new TaskCompletionSource<string[]>();
#if UNITY_ANDROID && !UNITY_EDITOR
            Cls.CallStatic("pickFiles");
#elif UNITY_EDITOR
            var p = UnityEditor.EditorUtility.OpenFilePanelWithFilters("Importer une maquette", "", new[] { "Maquettes", "glb,gltf,ifc,zip,bin", "Tous", "*" });
            _pick.TrySetResult(string.IsNullOrEmpty(p) ? new string[0] : new[] { p });
#else
            _pick.TrySetResult(new string[0]);
#endif
            return _pick.Task;
        }
        public void OnFilesPicked(string msg)
        {
            if (_pick == null) return;
            if (string.IsNullOrEmpty(msg)) _pick.TrySetResult(new string[0]);
            else if (msg.StartsWith("!")) _pick.TrySetException(new Exception(msg.Substring(1)));
            else _pick.TrySetResult(JsonConvert.DeserializeObject<string[]>(msg));
        }

        /// <summary>Enregistre une image dans la galerie du téléphone (Images/ArchiVision). Renvoie l'URI de contenu, ou null.</summary>
        public string SaveImageToGallery(string path, string displayName)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (AndroidVersion() < 29 && !UnityEngine.Android.Permission.HasUserAuthorizedPermission("android.permission.WRITE_EXTERNAL_STORAGE"))
            { UnityEngine.Android.Permission.RequestUserPermission("android.permission.WRITE_EXTERNAL_STORAGE"); return null; }
            var r = Cls.CallStatic<string>("saveImage", path, displayName);
            return r != null && !r.StartsWith("!") ? r : null;
#else
            var dst = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "ArchiVision"); System.IO.Directory.CreateDirectory(dst);
            var f = System.IO.Path.Combine(dst, displayName); System.IO.File.Copy(path, f, true); return f;
#endif
        }
        public void Share(string contentUri, string mime, string text)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            Cls.CallStatic("share", contentUri ?? "", mime, text ?? "", "Partager");
#else
            Debug.Log("[AV] Partage (éditeur) : " + contentUri + " " + text);
#endif
        }

        public bool CanBiometric()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try { return Cls.CallStatic<bool>("canBiometric"); } catch { return false; }
#else
            return false;
#endif
        }
        /// <summary>"ok", "cancel", "unavailable" ou "error:…".</summary>
        public Task<string> Biometric(string title, string subtitle)
        {
            _bio?.TrySetResult("cancel"); _bio = new TaskCompletionSource<string>();
#if UNITY_ANDROID && !UNITY_EDITOR
            Cls.CallStatic("biometric", title, subtitle);
#else
            _bio.TrySetResult("unavailable");
#endif
            return _bio.Task;
        }
        public void OnBiometric(string r) => _bio?.TrySetResult(r);
        public void OnIfcProgress(string m) => IfcConverter.OnProgress(m);
        public void OnIfcDone(string r) => IfcConverter.OnDone(r);
        public void OnNativeError(string m) => Debug.LogWarning("[AV] natif : " + m);

        public static int AndroidVersion()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using var v = new AndroidJavaClass("android.os.Build$VERSION"); return v.GetStatic<int>("SDK_INT");
#else
            return 34;
#endif
        }
    }
}
