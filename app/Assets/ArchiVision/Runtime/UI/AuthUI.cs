// ARCHIVISION · écrans du compte Studio Prism : intro, connexion, création de compte, mot de passe oublié, fiche client, code PIN,
// verrouillage (code PIN, empreinte ou visage), licence, sécurité, menu du compte.
using System;
using System.Linq;
using System.Threading.Tasks;
using StudioPrism.ArchiVision.Native;
using StudioPrism.ArchiVision.Services;
using UnityEngine;
using UnityEngine.UIElements;
using static StudioPrism.ArchiVision.UI.K;

namespace StudioPrism.ArchiVision
{
    public partial class App
    {
        bool _onboarding;

        // ------------------------------------------------------------ Démarrage
        async void Boot()
        {
            var sp = El("screen splash");
            sp.Add(El("splash-mark")); var w = El("splash-word"); w.Add(L("ARCHI")); w.Add(L("VISION", "t2")); sp.Add(w);
            sp.Add(L("Architecture en réalité augmentée", "splash-tag")); var bar = El("splash-bar"); bar.Add(El()); sp.Add(bar);
            sp.Add(L("Ouverture des maquettes…", "splash-msg"));
            ShowScreen("splash", sp);
            sp.schedule.Execute(() => sp.AddToClassList("in")).StartingIn(60);
            var t0 = Time.realtimeSinceStartup;
            var me = Account.RestoreLocal();
            while (Time.realtimeSinceStartup - t0 < 1.9f) await Task.Yield();
            if (me == null) { ShowLogin(); return; }
            VerifyInBackground();
            if (Account.Lock?.Pin != null) ShowLock(); else EnterApp();
        }

        async void VerifyInBackground()
        {
            try { await Account.Verify(); _ = Account.SyncPin(); await Account.RefreshAccess(); if (_screen == "home") RenderHome(); }
            catch (AVException e) when (e.Code == "app/suspended" || e.Code.StartsWith("INVALID_REFRESH_TOKEN") || e.Code.StartsWith("USER_NOT_FOUND") || e.Code.StartsWith("USER_DISABLED") || e.Code.StartsWith("TOKEN_EXPIRED"))
            { Account.SignOutLocal(); ShowLogin(err: e.Message); }
            catch (Exception) { await Account.RefreshAccess(); }   // hors ligne : derniers droits connus
        }

        // ------------------------------------------------------------ Carte du compte
        ScrollView AuthScreen(Action<VisualElement> build, bool logo = true)
        {
            var scr = El("screen"); var sv = Scroll("auth-scroll"); var wrap = El("auth-wrap");
            if (logo) wrap.Add(El("auth-logo"));
            build(wrap); sv.Add(wrap); scr.Add(sv); ShowScreen("auth", scr); return sv;
        }
        static VisualElement Steps(int i) { var s = El("steps"); for (int k = 0; k < 3; k++) s.Add(El("step" + (k <= i ? " on" : ""))); return s; }
        static Label Err(string t = "") { var l = L(t, "err"); l.Show(!string.IsNullOrEmpty(t)); return l; }
        static void SetErr(Label l, string t) { l.text = t ?? ""; l.Show(!string.IsNullOrEmpty(t)); }
        static VisualElement PasswordField(string label, out TextField f)
        {
            var tf = Input("", true, false, 128); f = tf;
            var row = El("row"); tf.style.flexGrow = 1; row.Add(tf);
            var eye = Link("Afficher", null); eye.style.position = Position.Absolute; eye.style.right = 6; eye.style.top = 6;
            eye.clicked += () => { tf.isPasswordField = !tf.isPasswordField; eye.text = tf.isPasswordField ? "Afficher" : "Masquer"; };
            row.Add(eye); return Field(label, row);
        }
        async Task<bool> Run(Button b, Func<Task> work, Label err)
        {
            var old = (b.Q<Label>()?.text) ?? b.text; b.SetEnabled(false); var lab = b.Q<Label>(); if (lab != null) lab.text = "Patientez…";
            try { await work(); return true; }
            catch (AVException e) { SetErr(err, e.Message); return false; }
            catch (Exception e) { Debug.LogException(e); SetErr(err, "Une erreur est survenue. Réessayez dans un instant."); return false; }
            finally { b.SetEnabled(true); if (lab != null) lab.text = old; }
        }

        // ------------------------------------------------------------ Connexion
        public void ShowLogin(string email = "", string err = null, string msg = null)
        {
            AuthScreen(w =>
            {
                w.Add(Seg(new[] { "Se connecter", "Créer un compte" }, 0, i => { if (i == 1) ShowSignup(); }));
                w.Add(Sp(18)); w.Add(L("Bon retour", "h2"));
                w.Add(L("Votre compte Studio Prism vous connecte à ARCHIVISION, à ARCHIPLAN, à ARCHITRAFIC et aux prochaines apps du studio.", "sub"));
                if (msg != null) w.Add(L(msg, "note ok"));
                var em = Input(email, false, false, 120); em.keyboardType = TouchScreenKeyboardType.EmailAddress; w.Add(Field("Email", em));
                w.Add(PasswordField("Mot de passe", out var pw));
                var forgot = Link("Mot de passe oublié ?", () => ShowReset(em.value)); forgot.style.alignSelf = Align.FlexEnd; forgot.style.marginTop = -6; w.Add(forgot);
                var e = Err(err); w.Add(e);
                Button go = null; go = Btn("Se connecter", async () =>
                {
                    SetErr(e, null);
                    if (em.value.Trim().Length == 0 || pw.value.Length == 0) { SetErr(e, "Veuillez renseigner l'adresse e-mail et le mot de passe."); return; }
                    if (await Run(go, async () => await Account.SignIn(em.value, pw.value), e)) AfterLogin();
                }, "primary wide"); w.Add(go);
                w.Add(Sp(26)); w.Add(L("COMPTE STUDIO PRISM", "eyebrow")); w.Add(L("Un seul compte pour toutes les apps du studio.", "h3"));
                var apps = El("auth-apps");
                foreach (var (n, d, tag) in new[] { ("ARCHIVISION", "Maquettes BIM en réalité augmentée", "Vous êtes ici"), ("ARCHIPLAN", "Conception de plans et modélisation 3D", "Même compte"), ("ARCHITRAFIC", "Trafic, déviations et gestion de chantier", "Même compte") })
                { var r = El("auth-app"); var c = El("grow"); c.Add(L(n, "t-bold")); c.Add(L(d, "t-small t-mut")); r.Add(c); r.Add(L(tag, "pill")); apps.Add(r); }
                w.Add(apps); w.Add(L("Studio Prism · Creative & Technical Solutions", "footer"));
            });
        }
        static VisualElement Sp(float h) { var e = new VisualElement(); e.style.height = h; e.pickingMode = PickingMode.Ignore; return e; }

        public void ShowSignup()
        {
            AuthScreen(w =>
            {
                w.Add(Seg(new[] { "Se connecter", "Créer un compte" }, 1, i => { if (i == 0) ShowLogin(); }));
                w.Add(Sp(18)); w.Add(Steps(0)); w.Add(L("Créer votre compte", "h2")); w.Add(L("Un seul compte pour toutes les apps Studio Prism.", "sub"));
                var fn = Input("", false, false, 80); var ln = Input("", false, false, 80);
                var row = El("row"); var a = Field("Prénom", fn); a.style.flexGrow = 1; a.style.flexBasis = 0; a.style.marginRight = 8; var b = Field("Nom", ln); b.style.flexGrow = 1; b.style.flexBasis = 0; row.Add(a); row.Add(b); w.Add(row);
                var em = Input("", false, false, 120); em.keyboardType = TouchScreenKeyboardType.EmailAddress; w.Add(Field("Email", em));
                w.Add(PasswordField("Mot de passe", out var pw));
                var rules = El("rules row"); var r1 = L("✓ 8 caractères", "rule"); var r2 = L("✓ une lettre", "rule"); var r3 = L("✓ un chiffre", "rule");
                r1.style.marginRight = r2.style.marginRight = 14; rules.Add(r1); rules.Add(r2); rules.Add(r3); w.Add(rules);
                pw.RegisterValueChangedCallback(ev => { var p = ev.newValue; r1.EnableInClassList("ok", p.Length >= 8); r2.EnableInClassList("ok", p.Any(char.IsLetter)); r3.EnableInClassList("ok", p.Any(char.IsDigit)); });
                w.Add(PasswordField("Confirmer le mot de passe", out var pw2));
                w.Add(Check("J'accepte les conditions d'utilisation et la politique de confidentialité de Studio Prism.", null, out var cgu));
                var e = Err(); w.Add(e);
                Button go = null; go = Btn("Créer mon compte", async () =>
                {
                    SetErr(e, null); var p = pw.value;
                    if (fn.value.Trim().Length == 0) { SetErr(e, "Indiquez votre prénom."); return; }
                    if (!em.value.Contains("@")) { SetErr(e, "Cette adresse email n'est pas valide."); return; }
                    if (p.Length < 8 || !p.Any(char.IsLetter) || !p.Any(char.IsDigit)) { SetErr(e, "Mot de passe trop faible : 8 caractères minimum, avec une lettre et un chiffre."); return; }
                    if (p != pw2.value) { SetErr(e, "Les deux mots de passe ne correspondent pas."); return; }
                    if (!cgu()) { SetErr(e, "Acceptez les conditions d'utilisation pour créer votre compte."); return; }
                    if (await Run(go, async () => await Account.SignUp(fn.value.Trim(), ln.value.Trim(), em.value.Trim(), p), e)) { _onboarding = true; ShowClientForm(false); }
                }, "primary wide"); w.Add(go);
                w.Add(L("Un e-mail de confirmation vous sera envoyé.", "t-small t-mut t-wrap"));
            });
        }

        public void ShowReset(string email = "")
        {
            AuthScreen(w =>
            {
                w.Add(L("Mot de passe oublié", "h2"));
                w.Add(L("Saisissez l'adresse e-mail de votre compte : nous vous enverrons un lien pour choisir un nouveau mot de passe.", "sub"));
                var em = Input(email, false, false, 120); em.keyboardType = TouchScreenKeyboardType.EmailAddress; w.Add(Field("Adresse e-mail", em));
                var e = Err(); w.Add(e);
                Button go = null; go = Btn("Envoyer le lien", async () =>
                {
                    if (!em.value.Contains("@")) { SetErr(e, "Adresse e-mail invalide."); return; }
                    if (await Run(go, async () => await Account.SendReset(em.value), e))
                        ShowLogin(em.value, null, "Si un compte existe pour cette adresse, un e-mail de réinitialisation vient d'être envoyé. Pensez à vérifier vos courriers indésirables.");
                }, "primary wide"); w.Add(go);
                var back = Link("Retour à la connexion", () => ShowLogin(em.value)); back.style.alignSelf = Align.Center; w.Add(back);
            });
        }

        async void AfterLogin()
        {
            // nouvel appareil : code PIN du compte (s'il existe) récupéré ; sinon proposé
            Busy("Ouverture de votre compte…");
            try
            {
                _ = Account.RefreshAccess();
                ClientFiche f = null; try { f = await Account.GetFiche(); } catch { }
                await Account.SyncPin().ContinueWith(_ => { });
                Busy(null);
                if (f != null && !f.Exists) { _onboarding = true; ShowClientForm(false, f); return; }
                var l = Account.Lock;
                if (l?.Pin == null && !(l?.Declined ?? false)) { ShowPinOffer(); return; }
                EnterApp($"Bienvenue {Account.Me.FirstName}".Trim());
            }
            catch (Exception) { Busy(null); EnterApp(); }
        }

        // ------------------------------------------------------------ Fiche client
        public async void ShowClientForm(bool edit, ClientFiche fiche = null)
        {
            if (fiche == null) { Busy("Recherche de votre fiche client…"); try { fiche = await Account.GetFiche(); } catch { fiche = new ClientFiche { FirstName = Account.Me.FirstName, LastName = Account.Me.LastName }; } Busy(null); }
            AuthScreen(w =>
            {
                if (!edit) w.Add(Steps(1));
                w.Add(L("Votre fiche client", "h2"));
                w.Add(L(edit ? "Ces informations sont partagées entre les apps Studio Prism." : "Quelques informations pour vos devis, vos commandes et le suivi de vos projets.", "sub"));
                if (!edit && !Account.ExistedBefore) { } else if (!edit && fiche.Exists) w.Add(L("Nous avons retrouvé vos informations Studio Prism. Vérifiez-les et complétez votre fiche.", "note ok"));
                var em = Input(Account.Me.Email); em.SetEnabled(false); w.Add(Field("Email du compte", em));
                var fn = Input(fiche.FirstName, false, false, 80); var ln = Input(fiche.LastName, false, false, 80);
                var row = El("row"); var a = Field("Prénom", fn); a.style.flexGrow = 1; a.style.flexBasis = 0; a.style.marginRight = 8; var b = Field("Nom", ln); b.style.flexGrow = 1; b.style.flexBasis = 0; row.Add(a); row.Add(b); w.Add(row);
                var ph = Input(fiche.Phone, false, false, 40); ph.keyboardType = TouchScreenKeyboardType.PhonePad; w.Add(Field("Téléphone", ph));
                string prof = string.IsNullOrEmpty(fiche.Profile) ? Account.Profiles[0] : fiche.Profile;
                var sel = El("av-select"); var selL = L(prof); sel.Add(selL); sel.Add(Icon("chevron", "s mut"));
                sel.RegisterCallback<ClickEvent>(_ => Menu("Vous êtes", Account.Profiles.Select(p => ((string)null, p, (string)null, false, (Action)(() => { prof = p; selL.text = p; }))).ToArray()));
                w.Add(Field("Vous êtes", sel));
                var co = Input(fiche.Company, false, false, 120); w.Add(Field("Société (facultatif)", co));
                var ad = Input(fiche.Address, false, false, 200); w.Add(Field("Adresse (facultatif)", ad));
                var zp = Input(fiche.PostalCode, false, false, 20); zp.keyboardType = TouchScreenKeyboardType.NumberPad; var ci = Input(fiche.City, false, false, 120);
                var r2 = El("row"); var c1 = Field("Code postal", zp); c1.style.width = 130; c1.style.marginRight = 8; var c2 = Field("Ville", ci); c2.style.flexGrow = 1; r2.Add(c1); r2.Add(c2); w.Add(r2);
                var cn = Input(fiche.Country, false, false, 120); w.Add(Field("Pays", cn));
                var e = Err(); w.Add(e);
                Button go = null; go = Btn(edit ? "Enregistrer" : "Enregistrer et continuer", async () =>
                {
                    if (fn.value.Trim().Length == 0) { SetErr(e, "Indiquez votre prénom."); return; }
                    if (ph.value.Trim().Length < 6) { SetErr(e, "Indiquez un numéro de téléphone."); return; }
                    var f = new ClientFiche { FirstName = fn.value.Trim(), LastName = ln.value.Trim(), Phone = ph.value.Trim(), Profile = prof, Company = co.value, Address = ad.value, PostalCode = zp.value, City = ci.value, Country = cn.value, Exists = fiche.Exists, CreatedAt = fiche.CreatedAt };
                    if (!await Run(go, () => Account.SaveFiche(f), e)) return;
                    if (edit) { Toast("Fiche client enregistrée"); EnterApp(); return; }
                    var l = Account.Lock; if (l?.Pin == null) ShowPinOffer(); else EnterApp($"Bienvenue {Account.Me.FirstName}".Trim());
                }, "primary wide"); w.Add(go);
                if (edit) { var c = Link("Annuler", () => EnterApp()); c.style.alignSelf = Align.Center; w.Add(c); }
            });
        }

        // ------------------------------------------------------------ Code PIN
        void ShowPinOffer()
        {
            AuthScreen(w =>
            {
                w.Add(Steps(2)); w.Add(L("Protéger l'accès sur cet appareil", "h2"));
                w.Add(L("Avec un code PIN, ARCHIVISION s'ouvre sans retaper votre mot de passe, et personne d'autre ne peut l'ouvrir sur cet appareil.", "sub"));
                w.Add(Bullet("Un seul code pour votre compte : il vous sera demandé sur vos autres appareils et pour confirmer une suppression de compte."));
                w.Add(Bullet("Il est enregistré chiffré (haché), jamais en clair, et n'est jamais envoyé par email ni par SMS."));
                if (AndroidNative.I.CanBiometric()) w.Add(Bullet("Empreinte ou visage : facultatif, vous pourrez l'activer ensuite."));
                w.Add(Bullet("Code oublié : reconnectez-vous avec votre mot de passe, puis choisissez-en un nouveau."));
                w.Add(Sp(10)); w.Add(Btn("Créer un code PIN", () => ShowPinSetup(false), "primary wide"));
                var skip = Link("Plus tard", () => { var l = Account.Lock ?? new LockRecord(); l.Declined = true; Account.Lock = l; EnterApp($"Bienvenue {Account.Me.FirstName}".Trim()); }); skip.style.alignSelf = Align.Center; w.Add(skip);
            });
        }

        VisualElement PinPad(Action<string> onDigit, VisualElement extra = null)
        {
            var pad = El("pin-pad");
            foreach (var k in new[] { "1", "2", "3", "4", "5", "6", "7", "8", "9", "bio", "0", "del" })
            {
                if (k == "bio") { if (extra != null) pad.Add(extra); else { var sp = El("pin-key fn"); sp.style.opacity = 0; pad.Add(sp); } continue; }
                var kk = k; var b = new Button(() => onDigit(kk)) { text = kk == "del" ? "" : kk }; b.Cls("pin-key" + (kk == "del" ? " fn" : ""));
                if (kk == "del") b.Add(Icon("back", "mut"));
                pad.Add(b);
            }
            return pad;
        }
        static VisualElement Dots(out Action<int> set)
        {
            var d = El("pin-dots"); var dots = Enumerable.Range(0, AVConfig.PinLength).Select(_ => El("pin-dot")).ToArray(); foreach (var x in dots) d.Add(x);
            set = n => { for (int i = 0; i < dots.Length; i++) dots[i].EnableInClassList("on", i < n); };
            return d;
        }
        void Shake(VisualElement e) { e.AddToClassList("shake"); e.schedule.Execute(() => e.RemoveFromClassList("shake")).StartingIn(120); Handheld.Vibrate(); }

        void ShowPinSetup(bool fromSettings, int stage = 1, string first = null, string err = null)
        {
            string buf = ""; Action<int> setDots = null; Label e = null; VisualElement dots = null;
            AuthScreen(w =>
            {
                if (!fromSettings && _onboarding) w.Add(Steps(2));
                w.Add(L(stage == 1 ? "Choisissez votre code PIN" : "Confirmez votre code PIN", "h2"));
                w.Add(L(stage == 1 ? "6 chiffres. Évitez les suites et les répétitions." : "Saisissez le même code une seconde fois.", "sub"));
                dots = Dots(out setDots); w.Add(dots); e = Err(err); e.style.alignSelf = Align.Center; w.Add(e);
                w.Add(PinPad(async k =>
                {
                    if (k == "del") { if (buf.Length > 0) buf = buf.Substring(0, buf.Length - 1); setDots(buf.Length); return; }
                    if (buf.Length >= AVConfig.PinLength) return;
                    buf += k; setDots(buf.Length);
                    if (buf.Length < AVConfig.PinLength) return;
                    var pin = buf; buf = "";
                    if (stage == 1)
                    {
                        if (Account.WeakPin(pin)) { setDots(0); Shake(dots); SetErr(e, "Évitez les suites et les répétitions (123456, 000000…)."); return; }
                        ShowPinSetup(fromSettings, 2, pin); return;
                    }
                    if (pin != first) { ShowPinSetup(fromSettings, 1, null, "Les deux codes ne correspondent pas. Recommencez."); return; }
                    Busy("Enregistrement du code PIN…");
                    try { await Account.SetPin(pin); } finally { Busy(null); }
                    Toast("Code PIN enregistré");
                    if (AndroidNative.I.CanBiometric() && !(Account.Lock?.Bio ?? false)) ShowBioOffer(fromSettings);
                    else if (fromSettings) ShowSecurity(); else EnterApp($"Bienvenue {Account.Me.FirstName}".Trim());
                }));
                if (fromSettings) { var c = Link("Annuler", ShowSecurity); c.style.alignSelf = Align.Center; w.Add(c); }
            }, false);
        }

        void ShowBioOffer(bool fromSettings)
        {
            AuthScreen(w =>
            {
                w.Add(L("Empreinte ou visage", "h2"));
                w.Add(L("Facultatif : déverrouillez ARCHIVISION en un geste sur cet appareil. Votre code PIN reste toujours disponible.", "sub"));
                var e = Err(); w.Add(e);
                w.Add(Btn("Activer", async () =>
                {
                    var r = await AndroidNative.I.Biometric("ARCHIVISION", "Confirmez pour activer le déverrouillage");
                    if (r == "ok") { var l = Account.Lock; l.Bio = true; Account.Lock = l; Toast("Déverrouillage biométrique activé"); if (fromSettings) ShowSecurity(); else EnterApp(); }
                    else SetErr(e, r == "cancel" ? "Activation annulée." : "La biométrie n'est pas disponible sur cet appareil.");
                }, "primary wide", "finger"));
                var s = Link("Plus tard", () => { if (fromSettings) ShowSecurity(); else EnterApp(); }); s.style.alignSelf = Align.Center; w.Add(s);
            });
        }

        public void ShowLock()
        {
            if (Account.Me == null) { ShowLogin(); return; }
            string buf = ""; Action<int> setDots = null; Label e = null; VisualElement dots = null;
            var l = Account.Lock; bool bio = l?.Bio == true && AndroidNative.I.CanBiometric();
            Button bioBtn = null;
            if (bio) { bioBtn = new Button(() => TryBio()) { text = "" }; bioBtn.Cls("pin-key fn"); bioBtn.Add(Icon("finger", "l accent")); }
            AuthScreen(w =>
            {
                var av = L(Account.Me.Initials, "avatar"); av.style.alignSelf = Align.Center; av.style.width = av.style.height = 64; av.style.borderTopLeftRadius = av.style.borderTopRightRadius = av.style.borderBottomLeftRadius = av.style.borderBottomRightRadius = 32; av.style.fontSize = 20; w.Add(av);
                var h = L($"Bonjour {Account.Me.FirstName}".Trim(), "h2"); h.style.unityTextAlign = TextAnchor.MiddleCenter; h.style.marginTop = 10; w.Add(h);
                var s = L("Saisissez votre code PIN pour ouvrir ARCHIVISION.", "sub"); s.style.unityTextAlign = TextAnchor.MiddleCenter; w.Add(s);
                dots = Dots(out setDots); w.Add(dots); e = Err(); e.style.alignSelf = Align.Center; w.Add(e);
                w.Add(PinPad(async k =>
                {
                    if (k == "del") { if (buf.Length > 0) buf = buf.Substring(0, buf.Length - 1); setDots(buf.Length); return; }
                    if (buf.Length >= AVConfig.PinLength) return;
                    buf += k; setDots(buf.Length);
                    if (buf.Length < AVConfig.PinLength) return;
                    var pin = buf; buf = "";
                    if (await Account.CheckPin(pin)) { EnterApp(); return; }
                    setDots(0); Shake(dots);
                    var f = Account.Lock?.Failed ?? 0;
                    if (f >= AVConfig.PinTries) { var lr = Account.Lock; lr.Failed = 0; Account.Lock = lr; Account.SignOutLocal(); ShowLogin(err: "Trop d'essais : reconnectez-vous avec votre mot de passe."); return; }
                    SetErr(e, $"Code PIN incorrect. {AVConfig.PinTries - f} essai(s) restant(s).");
                }, bioBtn));
                var r = El("row"); r.style.justifyContent = Justify.Center; r.style.marginTop = 12;
                r.Add(Link("Code oublié ?", () => { var lr = Account.Lock; if (lr != null) { lr.Pin = null; Account.Lock = lr; } Account.SignOutLocal(); ShowLogin(msg: "Reconnectez-vous avec votre mot de passe, puis choisissez un nouveau code PIN."); }));
                r.Add(Link("Se déconnecter", SignOut)); w.Add(r);
            }, false);
            if (bio) _root.schedule.Execute(TryBio).StartingIn(400);
            async void TryBio()
            {
                var res = await AndroidNative.I.Biometric("ARCHIVISION", "Déverrouiller avec l'empreinte ou le visage");
                if (res == "ok") EnterApp();
            }
        }

        // ------------------------------------------------------------ Licence
        public void ShowLicence(string reason = null)
        {
            var (cls, sh, title, text) = Account.Summary(); var a = Account.Access;
            AuthScreen(w =>
            {
                w.Add(L(title, "h2"));
                if (reason == "feature") w.Add(L("Cette fonction est réservée aux comptes disposant d'une licence ARCHIVISION active.", "sub"));
                var box = El("lic-box " + cls); box.Add(L(sh, "t-bold t-wrap")); if (!string.IsNullOrEmpty(text)) box.Add(L(text, "t-small t-wrap"));
                if (a.Offline) box.Add(L("Hors ligne : derniers droits connus.", "t-small t-mut"));
                w.Add(box);
                if (Account.Full)
                {
                    w.Add(Bullet("Import de vos maquettes GLB, glTF et IFC (ARCHIPLAN, Revit, ArchiCAD…), gardées sur l'appareil."));
                    w.Add(Bullet("Visite 3D (orbite, marche, immersion 360°), hiérarchie BIM, données IFC, mesure et coupe."));
                    w.Add(Bullet("Réalité augmentée : pose sur le sol ou une table, échelle 1:1 à 1:200, captures annotées."));
                }
                else
                {
                    w.Add(Bullet("Mode découverte : maquette d'exemple, visite 3D, hiérarchie et données BIM."));
                    w.Add(Bullet("L'import de vos maquettes, la réalité augmentée et les exports demandent une licence ARCHIVISION, à obtenir sur le site Studio Prism et activée sur ce même compte."));
                    w.Add(Btn("Voir les offres ARCHIVISION", () => Application.OpenURL(AVConfig.OffersUrl), "primary wide"));
                }
                w.Add(Sp(14)); w.Add(L("J'ai une clé de licence", "h3"));
                var key = Input("", false, false, 48); key.textEdition.placeholder = "ARVI-XXXX-XXXX-XXXX"; w.Add(Field("Clé reçue de Studio Prism ou de votre établissement", key));
                var e = Err(); w.Add(e);
                Button act = null; act = Btn("Activer la clé", async () =>
                {
                    string prod = null;
                    if (await Run(act, async () => prod = await Account.RedeemKey(key.value), e)) { Toast($"Clé activée : {prod}"); ShowLicence(); }
                }, "wide"); w.Add(act);
                var r = El("row"); r.style.justifyContent = Justify.Center; r.style.flexWrap = Wrap.Wrap; r.style.marginTop = 10;
                r.Add(Link("Vérifier à nouveau", async () => { Busy("Vérification…"); await Account.RefreshAccess(); Busy(null); ShowLicence(reason); Toast(Account.Full ? "Licence active : ARCHIVISION est débloqué" : "Toujours aucune licence active"); }));
                r.Add(Link("Mon compte Studio Prism", () => Application.OpenURL(AVConfig.AccountUrl)));
                r.Add(Link(Account.Full ? "Retour" : "Continuer en mode découverte", () => EnterApp()));
                w.Add(r);
            });
        }

        // ------------------------------------------------------------ Sécurité
        public void ShowSecurity()
        {
            var l = Account.Lock; bool has = l?.Pin != null, can = AndroidNative.I.CanBiometric();
            AuthScreen(w =>
            {
                w.Add(L("Code PIN et sécurité", "h2"));
                var c1 = El("card"); c1.style.marginBottom = 12;
                c1.Add(L("Code PIN du compte", "t-bold")); c1.Add(L(has ? "Demandé à chaque ouverture d'ARCHIVISION sur cet appareil." : "Aucun code PIN pour l'instant.", "t-small t-mut t-wrap"));
                var b1 = Btn(has ? "Changer le code PIN" : "Créer un code PIN", () => ShowPinSetup(true), "small"); b1.style.marginTop = 10; b1.style.alignSelf = Align.FlexStart; c1.Add(b1);
                if (has) { var off = Link("Ne plus demander sur cet appareil", () => { var lr = Account.Lock; lr.Pin = null; lr.Declined = true; lr.Bio = false; Account.Lock = lr; Toast("Code PIN désactivé sur cet appareil"); ShowSecurity(); }); off.style.alignSelf = Align.FlexStart; c1.Add(off); }
                w.Add(c1);
                var c2 = El("card"); c2.Add(L("Empreinte ou visage", "t-bold"));
                c2.Add(L(!can ? "Non disponible sur cet appareil." : l?.Bio == true ? "Activé sur cet appareil." : "Désactivé. Le code PIN reste toujours disponible.", "t-small t-mut t-wrap"));
                if (can && has) { var b2 = Btn(l?.Bio == true ? "Désactiver" : "Activer", () => { if (l?.Bio == true) { l.Bio = false; Account.Lock = l; ShowSecurity(); } else ShowBioOffer(true); }, "small"); b2.style.marginTop = 10; b2.style.alignSelf = Align.FlexStart; c2.Add(b2); }
                w.Add(c2);
                w.Add(Sp(14)); w.Add(Btn("Retour à ARCHIVISION", () => EnterApp(), "primary wide"));
            });
        }

        // ------------------------------------------------------------ Entrée dans l'app, menu du compte
        void EnterApp(string msg = null)
        {
            _onboarding = false;
            ShowHome();
            if (!string.IsNullOrEmpty(msg)) Toast(msg);
            if (Account.Access.Level == Services.AccessLevel.Pending) _ = RefreshAndRender();
        }
        async Task RefreshAndRender() { await Account.RefreshAccess(); if (_screen == "home") RenderHome(); }

        public void AccountMenu()
        {
            var me = Account.Me; if (me == null) return; var (cls, sh, _, _) = Account.Summary();
            Menu($"{me.FullName}",
                ("key", "Ma licence ARCHIVISION", sh, false, () => ShowLicence()),
                ("user", "Fiche client", "Coordonnées partagées entre les apps Studio Prism", false, () => ShowClientForm(true)),
                ("lock", "Code PIN et sécurité", "Code PIN du compte, empreinte ou visage", false, ShowSecurity),
                (Account.Lock?.Pin != null ? "lock" : null, Account.Lock?.Pin != null ? "Verrouiller" : null, "Le code PIN sera demandé pour revenir", false, ShowLock),
                (Dark ? "sun" : "moon", Dark ? "Thème clair" : "Thème sombre", "Change l'apparence de l'interface sur cet appareil", false, () => { SetTheme(!Dark); if (_screen == "home") RenderHome(); else if (_screen == "viewer") RenderPanel(); }),
                (null, null, null, false, null),
                ("logout", "Se déconnecter", me.Email, false, SignOut),
                ("trash", "Supprimer mon compte", "Sur le site Studio Prism, avec votre mot de passe et votre code PIN", true, () => Confirm("Supprimer mon compte Studio Prism",
                    "La suppression se fait sur le site Studio Prism, pour toutes les apps (ARCHIVISION, ARCHIPLAN, ARCHITRAFIC…). Votre mot de passe puis votre code PIN vous seront demandés. Les maquettes et photos gardées sur cet appareil y restent (supprimez-les depuis l'accueil).",
                    "Continuer sur le site", () => Application.OpenURL(AVConfig.DeleteUrl))));
        }

        void SignOut()
        {
            if (_ar.Running) ExitAR(false);
            var email = Account.Me?.Email; _mv.Clear(); Account.SignOutLocal(); ShowLogin(email, null, "Vous êtes déconnecté.");
        }
    }
}
