// ARCHIVISION · compte Studio Prism : session, profil users/{uid}, fiche client clientProfiles/{uid}, licence licenses/{uid}_archivision,
// code PIN du compte userSecrets/{uid} (PBKDF2, identique au site : même code sur tous les appareils), stockage local protégé.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;

namespace StudioPrism.ArchiVision.Services
{
    /// <summary>Fichiers locaux chiffrés (AES, clé liée à l'appareil et à l'app) : session, profil mémorisé, code PIN haché.</summary>
    public static class SecureStore
    {
        static byte[] _key;
        static byte[] Key => _key ??= new Rfc2898DeriveBytes(Encoding.UTF8.GetBytes(SystemInfo.deviceUniqueIdentifier + "|archivision|" + Application.identifier),
                                                             Encoding.UTF8.GetBytes("StudioPrism.ArchiVision.v1"), 2000, HashAlgorithmName.SHA256).GetBytes(32);
        static string PathOf(string name) => System.IO.Path.Combine(Application.persistentDataPath, "secure", name + ".bin");
        public static void Save<T>(string name, T value)
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.Combine(Application.persistentDataPath, "secure"));
                using var aes = Aes.Create(); aes.Key = Key; aes.GenerateIV();
                var plain = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(value));
                using var enc = aes.CreateEncryptor(); var c = enc.TransformFinalBlock(plain, 0, plain.Length);
                File.WriteAllBytes(PathOf(name), aes.IV.Concat(c).ToArray());
            }
            catch (Exception e) { Debug.LogWarning("[AV] SecureStore.Save " + name + " : " + e.Message); }
        }
        public static T Load<T>(string name) where T : class
        {
            try
            {
                var p = PathOf(name); if (!File.Exists(p)) return null;
                var all = File.ReadAllBytes(p); using var aes = Aes.Create(); aes.Key = Key; aes.IV = all.Take(16).ToArray();
                using var dec = aes.CreateDecryptor(); var plain = dec.TransformFinalBlock(all, 16, all.Length - 16);
                return JsonConvert.DeserializeObject<T>(Encoding.UTF8.GetString(plain));
            }
            catch { return null; }
        }
        public static void Delete(string name) { try { File.Delete(PathOf(name)); } catch { } }
    }

    public enum AccessLevel { Pending, Discovery, Licensed, Admin }

    public class Me { public string Uid, Email, FirstName = "", LastName = ""; public bool Verified; public string FullName => $"{FirstName} {LastName}".Trim() is var n && n.Length > 0 ? n : Email; public string Initials => ((FirstName.Length > 0 ? FirstName[0].ToString() : "") + (LastName.Length > 0 ? LastName[0].ToString() : "")).ToUpper() is var s && s.Length > 0 ? s : (Email ?? "?").Substring(0, 1).ToUpper(); }

    public class LicenseInfo { public string Type, Status; public DateTime? ExpiresUtc; public bool Expired, Free; }
    public class AccessInfo { public AccessLevel Level; public LicenseInfo License; public string AccountKind = "individual", Subscription = "free", Role = "user", CompanyName = "", OrgName = ""; public DateTime CheckedUtc; public bool Offline; }
    public class LockRecord { public string Pin, Salt; public int Iter; public int Failed; public bool Declined, Bio, Unsynced; }
    public class ClientFiche { public string FirstName = "", LastName = "", Phone = "", Profile = "Particulier", Company = "", Address = "", PostalCode = "", City = "", Country = "France"; public bool Exists; public Firestore.RawTimestamp CreatedAt; }

    public class SessionData { public string Uid, Email, RefreshToken, FirstName, LastName; }

    public static class Account
    {
        public static Me Me { get; private set; }
        public static AuthTokens Tokens { get; private set; }
        public static AccessInfo Access { get; private set; } = new AccessInfo { Level = AccessLevel.Pending };
        public static bool ExistedBefore { get; private set; } = true;
        public static event Action AccessChanged;
        public static bool Full => Access.Level == AccessLevel.Licensed || Access.Level == AccessLevel.Admin;
        public static readonly string[] Profiles = { "Particulier", "Architecte ou maître d'œuvre", "Artisan ou entreprise du bâtiment", "Bureau d'études", "Industrie ou atelier", "Étudiant ou enseignant", "Autre" };

        // ------------------------------------------------------------ Session
        /// <summary>Session mémorisée : l'écran du code PIN s'affiche sans attendre le réseau ; le profil est vérifié en arrière-plan.</summary>
        public static Me RestoreLocal()
        {
            var s = SecureStore.Load<SessionData>("session"); if (s == null || string.IsNullOrEmpty(s.RefreshToken)) return null;
            Tokens = new AuthTokens { Uid = s.Uid, Email = s.Email, RefreshToken = s.RefreshToken, ExpiresUtc = DateTime.MinValue };
            Me = new Me { Uid = s.Uid, Email = s.Email, FirstName = s.FirstName ?? "", LastName = s.LastName ?? "", Verified = true };
            var a = SecureStore.Load<AccessInfo>("access." + s.Uid);
            if (a != null && (DateTime.UtcNow - a.CheckedUtc).TotalDays < AVConfig.OfflineDays) { a.Offline = true; Access = a; }
            return Me;
        }
        static void SaveSession() => SecureStore.Save("session", new SessionData { Uid = Tokens.Uid, Email = Tokens.Email, RefreshToken = Tokens.RefreshToken, FirstName = Me?.FirstName, LastName = Me?.LastName });

        public static async Task<AuthTokens> Fresh()
        {
            if (Tokens == null) throw new AVException("USER_NOT_FOUND");
            if (string.IsNullOrEmpty(Tokens.IdToken) || DateTime.UtcNow >= Tokens.ExpiresUtc) { await FirebaseAuth.Refresh(Tokens); SaveSession(); }
            return Tokens;
        }

        /// <summary>Vérifie la session en ligne (profil actif, compte non désactivé) ; renvoie le profil à jour.</summary>
        public static async Task<Me> Verify()
        {
            var t = await Fresh(); await FirebaseAuth.Lookup(t);
            var p = await ReadProfile(t);
            Me.FirstName = p.Str("firstName", Me.FirstName); Me.LastName = p.Str("lastName", Me.LastName); Me.Verified = t.Verified; Me.Email = t.Email;
            SaveSession(); return Me;
        }

        public static async Task<Me> SignIn(string email, string password)
        {
            var t = await FirebaseAuth.SignIn(email, password);
            Tokens = t; Me = new Me { Uid = t.Uid, Email = t.Email, Verified = t.Verified };
            try
            {
                var p = await ReadProfile(t);
                Me.FirstName = p.Str("firstName"); Me.LastName = p.Str("lastName");
                _ = Firestore.Commit(t, new Firestore.Write { Path = "users/" + t.Uid, Fields = new Dictionary<string, object> { ["lastLogin"] = Firestore.ServerTime }, Mask = new[] { "lastLogin" } }).ContinueWith(_ => { });
            }
            catch { Tokens = null; Me = null; throw; }
            SaveSession(); return Me;
        }

        public static async Task<Me> SignUp(string first, string last, string email, string password)
        {
            var t = await FirebaseAuth.SignUp(email, password);
            Tokens = t;
            try
            {
                await Firestore.Commit(t, new Firestore.Write { Path = "users/" + t.Uid, MustNotExist = true, Fields = DefaultProfile(first, last, t.Email, true) });
            }
            catch { try { await FirebaseAuth.DeleteAccountRaw(t); } catch { } Tokens = null; throw; }   // pas de compte à moitié créé
            _ = FirebaseAuth.UpdateDisplayName(t, $"{first} {last}".Trim()).ContinueWith(_ => { });
            _ = FirebaseAuth.SendVerification(t).ContinueWith(_ => { });
            ExistedBefore = false;
            Me = new Me { Uid = t.Uid, Email = t.Email, FirstName = first, LastName = last, Verified = false };
            SaveSession(); return Me;
        }

        static string Cut80(string s) => s.Length > 80 ? s.Substring(0, 80) : s;
        static Dictionary<string, object> DefaultProfile(string first, string last, string email, bool terms)
        {
            var d = new Dictionary<string, object>
            {
                ["firstName"] = Cut80(string.IsNullOrWhiteSpace(first) ? "Utilisateur" : first.Trim()), ["lastName"] = Cut80((last ?? "").Trim()), ["email"] = email,
                ["accountKind"] = "individual", ["subscription"] = "free", ["role"] = "user", ["status"] = "active",
                ["createdAt"] = Firestore.ServerTime, ["updatedAt"] = Firestore.ServerTime, ["lastLogin"] = Firestore.ServerTime,
            };
            if (terms) d["termsAcceptedAt"] = Firestore.ServerTime;
            return d;
        }

        /// <summary>Lit (ou crée) le profil Studio Prism ; refuse un compte désactivé, comme le site.</summary>
        static async Task<Dictionary<string, object>> ReadProfile(AuthTokens t)
        {
            var p = await Firestore.Get(t, "users/" + t.Uid); ExistedBefore = p != null;
            if (p == null)
            {
                var words = (t.DisplayName ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (words.Length == 0) words = (t.Email ?? "utilisateur").Split('@')[0].Split('.', '_', '-', '+').Where(w => w.Length > 0).Select(w => char.ToUpper(w[0]) + w.Substring(1)).ToArray();
                try { await Firestore.Commit(t, new Firestore.Write { Path = "users/" + t.Uid, MustNotExist = true, Fields = DefaultProfile(words.FirstOrDefault() ?? "Utilisateur", string.Join(" ", words.Skip(1)), t.Email, false) }); } catch { }
                p = await Firestore.Get(t, "users/" + t.Uid); if (p == null) throw new AVException("app/profile-invalid");
            }
            var st = p.Str("status");
            if (st == "suspended") { SignOutLocal(); throw new AVException("app/suspended"); }
            if (st != "active") throw new AVException("app/profile-invalid");
            return p;
        }

        public static async Task SendReset(string email) => await FirebaseAuth.SendReset(email);
        public static async Task ResendVerification() => await FirebaseAuth.SendVerification(await Fresh());
        public static async Task<bool> ReloadVerified() { var t = await Fresh(); await FirebaseAuth.Lookup(t); Me.Verified = t.Verified; return t.Verified; }

        public static void SignOutLocal()
        {
            SecureStore.Delete("session"); Tokens = null; Me = null; Access = new AccessInfo { Level = AccessLevel.Pending }; AccessChanged?.Invoke();
        }

        // ------------------------------------------------------------ Droits (licence ARCHIVISION)
        public static async Task<AccessInfo> RefreshAccess()
        {
            if (Me == null) return Access;
            string uid = Me.Uid;
            try
            {
                var t = await Fresh();
                var p = await Firestore.Get(t, "users/" + uid) ?? new Dictionary<string, object>();
                var a = new AccessInfo { AccountKind = p.Str("accountKind", "individual"), Subscription = p.Str("subscription", "free"), Role = p.Str("role", "user"), CompanyName = p.Str("companyName"), OrgName = p.Str("orgName"), CheckedUtc = DateTime.UtcNow };
                var lic = await Firestore.Get(t, $"licenses/{uid}_{AVConfig.LicenseProduct}");
                if (lic != null)
                {
                    var exp = lic.Time("expiresAt");
                    a.License = new LicenseInfo { Type = lic.Str("licenseType"), Status = lic.Str("status"), ExpiresUtc = exp, Expired = exp.HasValue && exp.Value <= DateTime.UtcNow };
                }
                if (a.Role == "admin" && p.Str("status") == "active") a.Level = AccessLevel.Admin;
                else if (a.License != null && a.License.Status == "active" && !a.License.Expired) a.Level = AccessLevel.Licensed;
                else if (AVConfig.FreeForAll && a.License?.Status != "suspended") { a.Level = AccessLevel.Licensed; a.License = new LicenseInfo { Type = "free", Status = "active", Free = true }; }
                else a.Level = AccessLevel.Discovery;
                if (Me?.Uid != uid) return Access;
                SecureStore.Save("access." + uid, a); Access = a;
            }
            catch (Exception)
            {
                if (Me?.Uid != uid) return Access;
                // hors ligne : derniers droits vérifiés il y a moins de 7 jours, sinon mode découverte
                var c = SecureStore.Load<AccessInfo>("access." + uid);
                Access = c != null && (DateTime.UtcNow - c.CheckedUtc).TotalDays < AVConfig.OfflineDays ? c : new AccessInfo { Level = AccessLevel.Discovery, CheckedUtc = DateTime.UtcNow };
                Access.Offline = true;
            }
            AccessChanged?.Invoke();
            return Access;
        }

        public static (string cls, string shortText, string title, string text) Summary()
        {
            var a = Access; var l = a.License;
            string Lab(string t) => t switch { "free" => "Gratuite", "pro" => "Pro", "pro_entreprise" => "Pro Entreprise", "trial" => "Essai", "demo" => "Démo", "education" => "Éducation", "standard" => "Standard", _ => string.IsNullOrEmpty(t) ? "—" : char.ToUpper(t[0]) + t.Substring(1).Replace('_', ' ') };
            if (a.Level == AccessLevel.Admin) return ("ok", "Administrateur · tous les droits", "Accès administrateur", "Votre compte Studio Prism est administrateur : ARCHIVISION est entièrement débloqué.");
            if (a.Level == AccessLevel.Licensed && l != null && l.Free) return ("ok", "Licence gratuite · toutes les fonctions", "Licence ARCHIVISION gratuite", "Import des maquettes, visite 3D, réalité augmentée, galerie et exports sont ouverts.");
            if (a.Level == AccessLevel.Licensed) return ("ok", $"Licence {Lab(l.Type)}" + (l.ExpiresUtc.HasValue ? " · jusqu'au " + Fr.Date(l.ExpiresUtc.Value) : ""), $"Licence ARCHIVISION {Lab(l.Type)}", l.ExpiresUtc.HasValue ? $"Active jusqu'au {Fr.Date(l.ExpiresUtc.Value)}." : "Active, sans date de fin.");
            if (a.Level == AccessLevel.Pending) return ("", "Vérification de la licence…", "Vérification…", "");
            var why = l == null ? "Aucune licence ARCHIVISION n'est associée à votre compte Studio Prism." : l.Expired ? $"Votre licence {Lab(l.Type)} a expiré le {Fr.Date(l.ExpiresUtc.Value)}." : l.Status == "suspended" ? $"Votre licence {Lab(l.Type)} est suspendue." : $"Votre licence {Lab(l.Type)} n'est pas active.";
            return ("warn", "Mode découverte · maquette d'exemple", "Mode découverte", why);
        }

        /// <summary>Activation d'une clé de licence (même procédure et mêmes vérifications que le site).</summary>
        public static async Task<string> RedeemKey(string raw)
        {
            var n = new string((raw ?? "").ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
            if (n.Length < 12 || n.Length > 40) throw new AVException("app/key-format");
            string id; using (var sha = SHA256.Create()) id = string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(n)).Select(b => b.ToString("x2")));
            var t = await Fresh(); var k = await Firestore.Get(t, "licenseKeys/" + id);
            if (k == null) throw new AVException("app/key-unknown");
            var st = k.Str("status");
            if (st == "redeemed") throw new AVException(k.Str("redeemedBy") == Me.Uid ? "app/key-mine" : "app/key-used");
            if (st != "available") throw new AVException("app/key-revoked");
            var kexp = k.Time("expiresAt"); if (kexp.HasValue && kexp.Value < DateTime.UtcNow) throw new AVException("app/key-expired");
            string product = k.Str("productId");
            Dictionary<string, object> prev = null; try { prev = await Firestore.Get(t, $"licenses/{Me.Uid}_{product}"); } catch { }
            object expires = kexp.HasValue ? (object)new Firestore.RawTimestamp(k["expiresAt"] is Firestore.RawTimestamp r ? r.Value : null)
                : (k.TryGetValue("durationDays", out var dd) && dd is long days && days > 0 ? (object)DateTime.UtcNow.AddDays(days) : null);
            var lic = new Dictionary<string, object>
            {
                ["userId"] = Me.Uid, ["productId"] = product, ["productName"] = k.Str("productName"), ["licenseType"] = k.Str("licenseType"), ["status"] = "active",
                ["activatedAt"] = Firestore.ServerTime, ["expiresAt"] = expires, ["keyId"] = id, ["orgId"] = k.Str("orgId"),
                ["createdAt"] = prev != null && prev.TryGetValue("createdAt", out var ca) ? ca : Firestore.ServerTime, ["updatedAt"] = Firestore.ServerTime,
            };
            var writes = new List<Firestore.Write>
            {
                new Firestore.Write { Path = "licenseKeys/" + id, Merge = true, Fields = new Dictionary<string, object> { ["status"] = "redeemed", ["redeemedBy"] = Me.Uid, ["redeemedEmail"] = (Me.Email ?? "").ToLowerInvariant(), ["redeemedAt"] = Firestore.ServerTime, ["updatedAt"] = Firestore.ServerTime },
                    Mask = new[] { "status", "redeemedBy", "redeemedEmail" } },
                new Firestore.Write { Path = $"licenses/{Me.Uid}_{product}", Fields = lic },
            };
            if (!string.IsNullOrEmpty(k.Str("orgId")))
                writes.Add(new Firestore.Write { Path = "users/" + Me.Uid, Fields = new Dictionary<string, object> { ["orgId"] = k.Str("orgId"), ["orgName"] = k.Str("orgName"), ["orgKeyId"] = id, ["orgRole"] = k.Str("role"), ["orgClass"] = k.Str("classe"), ["updatedAt"] = Firestore.ServerTime }, Mask = new[] { "orgId", "orgName", "orgKeyId", "orgRole", "orgClass" } });
            await Firestore.Commit(t, writes.ToArray());
            await RefreshAccess();
            return k.Str("productName", "ARCHIVISION");
        }

        // ------------------------------------------------------------ Fiche client
        public static async Task<ClientFiche> GetFiche()
        {
            var t = await Fresh(); var f = await Firestore.Get(t, "clientProfiles/" + Me.Uid);
            var c = new ClientFiche { FirstName = Me.FirstName, LastName = Me.LastName };
            if (f != null)
            {
                c.Exists = true; c.Phone = f.Str("phone"); c.Profile = f.Str("profile", "Particulier"); c.Company = f.Str("company"); c.Address = f.Str("address");
                c.PostalCode = f.Str("postalCode"); c.City = f.Str("city"); c.Country = f.Str("country", "France");
                if (f.TryGetValue("createdAt", out var ca)) c.CreatedAt = ca as Firestore.RawTimestamp;
            }
            return c;
        }
        public static async Task SaveFiche(ClientFiche c)
        {
            var t = await Fresh();
            string Cut(string s, int n) => (s ?? "").Trim().Length > n ? s.Trim().Substring(0, n) : (s ?? "").Trim();
            var data = new Dictionary<string, object>
            {
                ["phone"] = Cut(c.Phone, 40), ["profile"] = Cut(c.Profile, 120), ["company"] = Cut(c.Company, 120), ["address"] = Cut(c.Address, 200), ["postalCode"] = Cut(c.PostalCode, 20),
                ["city"] = Cut(c.City, 120), ["country"] = Cut(c.Country, 120), ["updatedAt"] = Firestore.ServerTime,
                ["createdAt"] = c.CreatedAt != null ? (object)c.CreatedAt : Firestore.ServerTime,
            };
            var writes = new List<Firestore.Write> { new Firestore.Write { Path = "clientProfiles/" + Me.Uid, Fields = data } };
            if (c.FirstName != Me.FirstName || c.LastName != Me.LastName)
                writes.Add(new Firestore.Write { Path = "users/" + Me.Uid, Fields = new Dictionary<string, object> { ["firstName"] = Cut(c.FirstName, 80), ["lastName"] = Cut(c.LastName, 80), ["updatedAt"] = Firestore.ServerTime }, Mask = new[] { "firstName", "lastName" } });
            await Firestore.Commit(t, writes.ToArray());
            Me.FirstName = c.FirstName; Me.LastName = c.LastName; c.Exists = true; SaveSession();
        }

        // ------------------------------------------------------------ Code PIN du compte (haché PBKDF2-SHA256, 200 000 itérations, comme le site)
        public static LockRecord Lock { get => Me == null ? null : SecureStore.Load<LockRecord>("lock." + Me.Uid); set { if (Me != null) SecureStore.Save("lock." + Me.Uid, value); } }
        public static string Pbkdf2(string pin, string saltB64, int iter)
        {
            using var d = new Rfc2898DeriveBytes(Encoding.UTF8.GetBytes(pin), Convert.FromBase64String(saltB64), iter, HashAlgorithmName.SHA256);
            return Convert.ToBase64String(d.GetBytes(32));
        }
        public static bool WeakPin(string p) => p.Distinct().Count() == 1 || "01234567890".Contains(p) || "09876543210".Contains(p);

        public static async Task SetPin(string pin)
        {
            var salt = new byte[16]; using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(salt);
            string s = Convert.ToBase64String(salt);
            string hash = await Task.Run(() => Pbkdf2(pin, s, AVConfig.PinIterations));
            var rec = Lock ?? new LockRecord(); rec.Pin = hash; rec.Salt = s; rec.Iter = AVConfig.PinIterations; rec.Failed = 0; rec.Declined = false; rec.Unsynced = true; Lock = rec;
            try { await PushPin(rec); rec.Unsynced = false; Lock = rec; } catch { }
        }
        static async Task PushPin(LockRecord r)
        {
            var t = await Fresh();
            await Firestore.Commit(t, new Firestore.Write { Path = "userSecrets/" + Me.Uid, Fields = new Dictionary<string, object> { ["pinHash"] = r.Pin, ["pinSalt"] = r.Salt, ["pinIter"] = (long)r.Iter, ["updatedAt"] = Firestore.ServerTime } });
        }
        public static async Task<bool> CheckPin(string pin)
        {
            var l = Lock; if (l?.Pin == null) return false;
            var h = await Task.Run(() => Pbkdf2(pin, l.Salt, l.Iter));
            bool ok = h == l.Pin; l.Failed = ok ? 0 : l.Failed + 1; Lock = l; return ok;
        }
        /// <summary>Synchronise le code PIN du compte (même code sur tous les appareils).</summary>
        public static async Task SyncPin()
        {
            var cur = Lock;
            if (cur?.Unsynced == true && cur.Pin != null) { await PushPin(cur); cur.Unsynced = false; Lock = cur; return; }
            var t = await Fresh(); var r = await Firestore.Get(t, "userSecrets/" + Me.Uid);
            if (r == null) return;
            var rec = cur ?? new LockRecord();
            string h = r.Str("pinHash");
            if (!string.IsNullOrEmpty(h) && rec.Pin != h && !rec.Declined) { rec.Pin = h; rec.Salt = r.Str("pinSalt"); rec.Iter = (int)(r.TryGetValue("pinIter", out var it) && it is long li ? li : AVConfig.PinIterations); rec.Failed = 0; Lock = rec; }
        }
        public static async Task<bool> AccountHasPin() { try { var t = await Fresh(); var r = await Firestore.Get(t, "userSecrets/" + Me.Uid); return r != null && r.Str("pinHash").Length > 0; } catch { return false; } }
    }
}
