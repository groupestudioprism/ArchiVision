// ARCHIVISION · constantes, utilitaires communs, requêtes HTTP asynchrones.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace StudioPrism.ArchiVision
{
    public static class AVConfig
    {
        // Projet Firebase de Studio Prism : le même que le site, ARCHIPLAN et ARCHITRAFIC.
        // Clé d'API web publique par conception (la sécurité repose sur les règles Firestore) ; aucune clé secrète dans l'app.
        public const string FirebaseApiKey = "AIzaSyAAD5rrHuVh6_PC2_45L3y9F7zWe5ACW6E";
        public const string FirebaseProject = "archivision-studioprism";
        public const string LicenseProduct = "archivision";
        public const bool FreeForAll = false;                 // même réglage que le site : licence requise
        public const string SiteUrl = "https://groupestudioprism.github.io/Studio-Prism/";
        public const string OffersUrl = SiteUrl + "#/produits/archivision";
        public const string AccountUrl = SiteUrl + "#/compte";
        public const string DeleteUrl = SiteUrl + "compte-suppression.html?from=archivision-app";
        public const string ArchiPlanUrl = "https://groupestudioprism.github.io/ArchiPlan/";
        public const int PinLength = 6, PinTries = 5, PinIterations = 200000;
        public const int OfflineDays = 7;
        public static string Version => Application.version;
    }

    public static class Fr
    {
        public static readonly CultureInfo C = CultureInfo.GetCultureInfo("fr-FR");
        public static string N(double x, int d = 2) => x.ToString("N" + d, C);
        public static string M(double x) => Math.Abs(x) < 1 ? N(x * 100, 1) + " cm" : N(x, 2) + " m";
        public static string Size(long b) => b >= 1_000_000 ? (b / 1e6).ToString("0.0", C) + " Mo" : Math.Max(1, b / 1000) + " Ko";
        public static string Ago(System.DateTime t)
        {
            var s = (System.DateTime.UtcNow - t.ToUniversalTime()).TotalSeconds;
            if (s < 60) return "à l'instant"; if (s < 3600) return $"il y a {(int)(s / 60)} min"; if (s < 86400) return $"il y a {(int)(s / 3600)} h";
            return t.ToLocalTime().ToString(t.Year == System.DateTime.Now.Year ? "d MMM" : "d MMM yyyy", C);
        }
        public static string Date(System.DateTime t) => t.ToLocalTime().ToString("d MMMM yyyy", C);
        public static string DateTime(System.DateTime t) => t.ToLocalTime().ToString("d MMMM yyyy 'à' HH:mm", C);
    }

    /// <summary>Erreur applicative avec un code (comme le site : auth/…, app/…) et un message en français.</summary>
    public class AVException : Exception
    {
        public readonly string Code;
        public AVException(string code, string message = null) : base(message ?? Errors.Message(code)) { Code = code; }
    }

    public static class Errors
    {
        static readonly Dictionary<string, string> M = new Dictionary<string, string>
        {
            {"INVALID_LOGIN_CREDENTIALS", "Email ou mot de passe incorrect."}, {"INVALID_PASSWORD", "Email ou mot de passe incorrect."}, {"EMAIL_NOT_FOUND", "Email ou mot de passe incorrect."},
            {"INVALID_EMAIL", "Cette adresse email n'est pas valide."}, {"EMAIL_EXISTS", "Un compte Studio Prism existe déjà avec cet email. Connectez-vous, ou utilisez « Mot de passe oublié »."},
            {"WEAK_PASSWORD", "Mot de passe trop faible : 8 caractères minimum, avec une lettre et un chiffre."}, {"TOO_MANY_ATTEMPTS_TRY_LATER", "Trop de tentatives. Patientez quelques minutes, puis réessayez."},
            {"USER_DISABLED", "Ce compte est désactivé. Contactez Studio Prism."}, {"TOKEN_EXPIRED", "Session expirée, veuillez vous reconnecter."}, {"INVALID_REFRESH_TOKEN", "Session expirée, veuillez vous reconnecter."},
            {"USER_NOT_FOUND", "Session expirée, veuillez vous reconnecter."}, {"network", "Connexion impossible. Vérifiez votre réseau, puis réessayez."}, {"timeout", "Le service de comptes ne répond pas. Vérifiez votre connexion, puis réessayez."},
            {"PERMISSION_DENIED", "Accès refusé. Vérifiez que votre email est bien confirmé."}, {"app/suspended", "Ce compte Studio Prism est désactivé. Contactez Studio Prism."},
            {"app/profile-invalid", "Ce compte n'est pas encore actif (compte entreprise en attente de validation)."}, {"app/pin-wrong", "Code PIN incorrect."},
            {"app/key-format", "Clé invalide : vérifiez la saisie (lettres et chiffres, au moins 12 caractères)."}, {"app/key-unknown", "Cette clé n'existe pas. Vérifiez la saisie."},
            {"app/key-used", "Cette clé a déjà été activée par un autre compte."}, {"app/key-mine", "Cette clé est déjà activée sur votre compte."}, {"app/key-revoked", "Cette clé a été désactivée. Contactez Studio Prism."},
            {"app/key-expired", "Cette clé a expiré."},
        };
        public static string Message(string code)
        {
            if (string.IsNullOrEmpty(code)) return "Une erreur est survenue. Réessayez dans un instant.";
            foreach (var kv in M) if (code.StartsWith(kv.Key)) return kv.Value;
            return "Une erreur est survenue. Réessayez dans un instant.";
        }
    }

    public struct HttpResult { public long Status; public string Text; public byte[] Data; public string Error; public bool Ok => Status >= 200 && Status < 300; }

    public static class Http
    {
        public static Task<HttpResult> Send(string method, string url, string json = null, Dictionary<string, string> headers = null, int timeoutSec = 20)
        {
            var tcs = new TaskCompletionSource<HttpResult>();
            var req = new UnityWebRequest(url, method) { downloadHandler = new DownloadHandlerBuffer(), timeout = timeoutSec };
            if (json != null) { req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json)); req.SetRequestHeader("Content-Type", "application/json"); }
            if (headers != null) foreach (var h in headers) req.SetRequestHeader(h.Key, h.Value);
            req.SendWebRequest().completed += _ =>
            {
                var r = new HttpResult { Status = req.responseCode, Text = req.downloadHandler?.text, Data = req.downloadHandler?.data, Error = req.result == UnityWebRequest.Result.ConnectionError ? "network" : req.error };
                req.Dispose(); tcs.TrySetResult(r);
            };
            return tcs.Task;
        }
        public static async Task<T> WithTimeout<T>(Task<T> t, int ms, string code = "timeout")
        {
            if (await Task.WhenAny(t, Task.Delay(ms)) != t) throw new AVException(code);
            return await t;
        }
    }

    /// <summary>Petites aides de temps (attente sur le fil principal).</summary>
    public static class AVTime
    {
        public static Task Delay(int ms) => Task.Delay(ms);
        public static Task NextFrame() { var tcs = new TaskCompletionSource<bool>(); App.Instance.RunNextFrame(() => tcs.TrySetResult(true)); return tcs.Task; }
    }
}
