// ARCHIVISION · Firebase par l'API REST (Authentication + Firestore) : même projet, mêmes documents et mêmes règles que le site Studio Prism,
// ARCHIPLAN et ARCHITRAFIC. Pas de SDK natif : l'app reste légère et le comportement est identique sur tous les appareils.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace StudioPrism.ArchiVision.Services
{
    public class AuthTokens
    {
        public string Uid, Email, IdToken, RefreshToken, DisplayName; public bool Verified; public DateTime ExpiresUtc;
    }

    public static class FirebaseAuth
    {
        const string Base = "https://identitytoolkit.googleapis.com/v1/accounts:";
        static string Url(string op) => Base + op + "?key=" + AVConfig.FirebaseApiKey;

        static async Task<JObject> Post(string url, JObject body)
        {
            var r = await Http.WithTimeout(Http.Send("POST", url, body.ToString(Newtonsoft.Json.Formatting.None)), 20000);
            if (r.Error == "network" && r.Status == 0) throw new AVException("network");
            JObject j = null; try { j = string.IsNullOrEmpty(r.Text) ? new JObject() : JObject.Parse(r.Text); } catch { }
            if (!r.Ok) throw new AVException((string)j?["error"]?["message"] ?? (r.Status == 0 ? "network" : "http/" + r.Status));
            return j ?? new JObject();
        }

        static AuthTokens FromSignIn(JObject j) => new AuthTokens
        {
            Uid = (string)j["localId"], Email = (string)j["email"], IdToken = (string)j["idToken"], RefreshToken = (string)j["refreshToken"], DisplayName = (string)j["displayName"],
            ExpiresUtc = DateTime.UtcNow.AddSeconds(int.Parse((string)j["expiresIn"] ?? "3600") - 120),
        };

        public static async Task<AuthTokens> SignIn(string email, string password)
        {
            var t = FromSignIn(await Post(Url("signInWithPassword"), new JObject { ["email"] = email.Trim(), ["password"] = password, ["returnSecureToken"] = true }));
            await Lookup(t); return t;
        }
        public static async Task<AuthTokens> SignUp(string email, string password)
        {
            var t = FromSignIn(await Post(Url("signUp"), new JObject { ["email"] = email.Trim(), ["password"] = password, ["returnSecureToken"] = true }));
            t.Verified = false; return t;
        }
        public static Task UpdateDisplayName(AuthTokens t, string name) => Post(Url("update"), new JObject { ["idToken"] = t.IdToken, ["displayName"] = name, ["returnSecureToken"] = false });
        public static async Task SendReset(string email)
        {
            try { await Post(Url("sendOobCode"), new JObject { ["requestType"] = "PASSWORD_RESET", ["email"] = email.Trim() }); }
            catch (AVException e) when (e.Code.StartsWith("EMAIL_NOT_FOUND")) { } // même réponse qu'un compte existant (pas de fuite d'information)
        }
        public static Task SendVerification(AuthTokens t) => Post(Url("sendOobCode"), new JObject { ["requestType"] = "VERIFY_EMAIL", ["idToken"] = t.IdToken });
        public static async Task Lookup(AuthTokens t)
        {
            var j = await Post(Url("lookup"), new JObject { ["idToken"] = t.IdToken });
            var u = j["users"]?.FirstOrDefault(); if (u == null) throw new AVException("USER_NOT_FOUND");
            t.Verified = (bool?)u["emailVerified"] ?? false; t.DisplayName = (string)u["displayName"] ?? t.DisplayName; t.Email = (string)u["email"] ?? t.Email;
            if ((bool?)u["disabled"] == true) throw new AVException("USER_DISABLED");
        }
        public static Task DeleteAccountRaw(AuthTokens t) => Post(Url("delete"), new JObject { ["idToken"] = t.IdToken });
        public static async Task Refresh(AuthTokens t)
        {
            var r = await Http.WithTimeout(Http.Send("POST", "https://securetoken.googleapis.com/v1/token?key=" + AVConfig.FirebaseApiKey,
                new JObject { ["grant_type"] = "refresh_token", ["refresh_token"] = t.RefreshToken }.ToString()), 20000);
            JObject j = null; try { j = JObject.Parse(r.Text ?? "{}"); } catch { }
            if (!r.Ok) throw new AVException((string)j?["error"]?["message"] ?? (r.Status == 0 ? "network" : "http/" + r.Status));
            t.IdToken = (string)j["id_token"]; t.RefreshToken = (string)j["refresh_token"]; t.Uid = (string)j["user_id"];
            t.ExpiresUtc = DateTime.UtcNow.AddSeconds(int.Parse((string)j["expires_in"] ?? "3600") - 120);
        }
    }

    /// <summary>Firestore REST : lecture de documents (valeurs converties en types simples) et écritures atomiques (commit) avec l'heure du serveur.</summary>
    public static class Firestore
    {
        static string DocsBase => $"https://firestore.googleapis.com/v1/projects/{AVConfig.FirebaseProject}/databases/(default)/documents";
        static Dictionary<string, string> Auth(AuthTokens t) => new Dictionary<string, string> { { "Authorization", "Bearer " + t.IdToken } };
        public static readonly object ServerTime = new object();

        /// <summary>Lit un document ; null s'il n'existe pas ou si les règles refusent la lecture d'un document absent.</summary>
        public static async Task<Dictionary<string, object>> Get(AuthTokens t, string path)
        {
            var r = await Http.WithTimeout(Http.Send("GET", $"{DocsBase}/{path}", null, Auth(t)), 15000);
            if (r.Status == 404) return null;
            if (r.Status == 403) return null;
            if (!r.Ok) throw new AVException(r.Status == 0 ? "network" : "http/" + r.Status);
            var j = JObject.Parse(r.Text);
            var d = Decode(j["fields"] as JObject); d["__updateTime"] = (string)j["updateTime"]; return d;
        }

        public class Write { public string Path; public Dictionary<string, object> Fields; public bool Merge; public string[] Mask; public bool MustNotExist; }

        /// <summary>Écritures groupées (même lot) ; les valeurs ServerTime deviennent request.time (exigé par les règles : createdAt, updatedAt…).</summary>
        public static async Task Commit(AuthTokens t, params Write[] writes)
        {
            var arr = new JArray();
            foreach (var w in writes)
            {
                var fields = new JObject(); var transforms = new JArray();
                foreach (var kv in w.Fields)
                    if (kv.Value == ServerTime) transforms.Add(new JObject { ["fieldPath"] = kv.Key, ["setToServerValue"] = "REQUEST_TIME" });
                    else fields[kv.Key] = Encode(kv.Value);
                var o = new JObject { ["update"] = new JObject { ["name"] = $"projects/{AVConfig.FirebaseProject}/databases/(default)/documents/{w.Path}", ["fields"] = fields } };
                var mask = w.Mask ?? (w.Merge ? w.Fields.Where(kv => kv.Value != ServerTime).Select(kv => kv.Key).ToArray() : null);
                if (mask != null) o["updateMask"] = new JObject { ["fieldPaths"] = new JArray(mask) };
                if (transforms.Count > 0) o["updateTransforms"] = transforms;
                if (w.MustNotExist) o["currentDocument"] = new JObject { ["exists"] = false };
                arr.Add(o);
            }
            var body = new JObject { ["writes"] = arr }.ToString(Newtonsoft.Json.Formatting.None);
            var r = await Http.WithTimeout(Http.Send("POST", $"https://firestore.googleapis.com/v1/projects/{AVConfig.FirebaseProject}/databases/(default)/documents:commit", body, Auth(t)), 20000);
            if (!r.Ok)
            {
                string st = null; try { st = (string)JObject.Parse(r.Text)["error"]?["status"]; } catch { }
                throw new AVException(st ?? (r.Status == 0 ? "network" : "http/" + r.Status));
            }
        }

        static JToken Encode(object v)
        {
            switch (v)
            {
                case null: return new JObject { ["nullValue"] = null };
                case string s: return new JObject { ["stringValue"] = s };
                case bool b: return new JObject { ["booleanValue"] = b };
                case int i: return new JObject { ["integerValue"] = i.ToString() };
                case long l: return new JObject { ["integerValue"] = l.ToString() };
                case double d: return new JObject { ["doubleValue"] = d };
                case float f: return new JObject { ["doubleValue"] = f };
                case DateTime dt: return new JObject { ["timestampValue"] = dt.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'") };
                case RawTimestamp rt: return new JObject { ["timestampValue"] = rt.Value };
                case Dictionary<string, object> m: { var f = new JObject(); foreach (var kv in m) f[kv.Key] = Encode(kv.Value); return new JObject { ["mapValue"] = new JObject { ["fields"] = f } }; }
                default: return new JObject { ["stringValue"] = v.ToString() };
            }
        }
        /// <summary>Horodatage relu tel quel (pour réécrire createdAt sans le modifier).</summary>
        public class RawTimestamp { public string Value; public RawTimestamp(string v) { Value = v; } }

        static Dictionary<string, object> Decode(JObject fields)
        {
            var d = new Dictionary<string, object>(); if (fields == null) return d;
            foreach (var p in fields.Properties()) d[p.Name] = Val(p.Value as JObject);
            return d;
        }
        static object Val(JObject v)
        {
            if (v == null) return null;
            if (v["stringValue"] != null) return (string)v["stringValue"];
            if (v["booleanValue"] != null) return (bool)v["booleanValue"];
            if (v["integerValue"] != null) return long.Parse((string)v["integerValue"]);
            if (v["doubleValue"] != null) return (double)v["doubleValue"];
            if (v["timestampValue"] != null) return new RawTimestamp((string)v["timestampValue"]);
            if (v["mapValue"] != null) return Decode(v["mapValue"]["fields"] as JObject);
            if (v["arrayValue"] != null) return (v["arrayValue"]["values"] as JArray)?.Select(x => Val(x as JObject)).ToList() ?? new List<object>();
            return null;
        }
        public static string Str(this Dictionary<string, object> d, string k, string def = "") => d != null && d.TryGetValue(k, out var v) && v != null ? v.ToString() : def;
        public static DateTime? Time(this Dictionary<string, object> d, string k)
            => d != null && d.TryGetValue(k, out var v) && v is RawTimestamp rt && DateTime.TryParse(rt.Value, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var t) ? t : (DateTime?)null;
    }
}
