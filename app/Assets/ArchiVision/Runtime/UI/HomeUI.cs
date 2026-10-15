// ARCHIVISION · accueil « Mes maquettes » et « Galerie » : import, maquette d'exemple, stockage de l'appareil, offre découverte.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using StudioPrism.ArchiVision.AR;
using StudioPrism.ArchiVision.Native;
using StudioPrism.ArchiVision.Services;
using StudioPrism.ArchiVision.Viewer;
using UnityEngine;
using UnityEngine.UIElements;
using static StudioPrism.ArchiVision.UI.K;

namespace StudioPrism.ArchiVision
{
    public partial class App
    {
        int _homeTab;
        VisualElement _homeBody, _homeQuota, _homeOffer; Label _hello, _homeSub, _arSub;
        readonly Dictionary<string, Texture2D> _thumbs = new Dictionary<string, Texture2D>();

        public void ShowHome()
        {
            if (_ar.Running) ExitAR(false);
            tourCamera.enabled = false;
            var scr = El("screen");
            // en-tête
            var top = El("topbar");
            var brand = El("brand"); var mk = El("brand-mark"); mk.Add(El()); brand.Add(mk);
            var bn = El(); var nm = El("brand-name"); nm.Add(L("ARCHI")); nm.Add(L("VISION", "t2")); bn.Add(nm); bn.Add(L("Studio Prism · Prism App", "brand-sub")); brand.Add(bn);
            top.Add(brand);
            if (!Account.Full && Account.Access.Level != AccessLevel.Pending) { var lic = Btn("Découverte", () => ShowLicence(), "small"); lic.style.marginRight = 8; top.Add(lic); }
            var av = new Button(AccountMenu) { text = Account.Me?.Initials ?? "?" }; av.Cls("avatar"); top.Add(av);
            scr.Add(top);

            var sv = Scroll("home-scroll");
            var hero = El("home-hero");
            hero.Add(L("ESPACE MAQUETTES", "eyebrow"));
            int h = DateTime.Now.Hour; _hello = L($"{(h < 18 && h > 4 ? "Bonjour" : "Bonsoir")} {Account.Me?.FirstName}".Trim(), "h1"); hero.Add(_hello);
            _homeSub = L("", "sub"); _homeSub.style.marginTop = 4; hero.Add(_homeSub);
            var q = El("quick");
            q.Add(Quick("Importer", "GLB, glTF, IFC, ZIP", "upload", ImportPick, true));
            q.Add(Quick("Exemple", "Maison à visiter", "house", () => OpenSample(false)));
            _arSub = null; var arq = Quick("Voir en AR", "Dernière maquette", "ar", () => { var m = Library.Models().FirstOrDefault(); if (m != null) OpenModel(m, true); else OpenSample(true); }); _arSub = arq.Q<Label>(className: "s"); q.Add(arq);
            q.Add(Quick("ARCHIPLAN", "Export Archi Vision", "archiplan", ArchiPlanHelp));
            hero.Add(q);
            _homeQuota = El("quota"); hero.Add(_homeQuota);
            sv.Add(hero);
            _homeOffer = El("offer"); sv.Add(_homeOffer);
            var tabs = El("home-tabs"); tabs.Add(Seg(new[] { "Mes maquettes", "Galerie" }, _homeTab, i => { _homeTab = i; RenderHome(); })); sv.Add(tabs);
            _homeBody = El("home-body"); sv.Add(_homeBody);
            sv.Add(L("STUDIO PRISM · CREATIVE & TECHNICAL SOLUTIONS · ARCHIVISION", "footer"));
            scr.Add(sv);
            ShowScreen("home", scr);
            RenderHome();
        }

        VisualElement Quick(string title, string sub, string icon, Action act, bool primary = false)
        {
            var w = El("hq"); var b = new Button(() => act()) { text = "" }; b.Cls("btn" + (primary ? " grad" : ""));
            var ic = El("hq-ico"); ic.Add(Icon(icon, primary ? "white" : "accent")); b.Add(ic);
            var t = El("hq-txt"); t.Add(L(title, "b")); t.Add(L(sub, "s")); b.Add(t); w.Add(b); return w;
        }

        public void RenderHome()
        {
            if (_homeBody == null || _screen != "home") return;
            var models = Library.Models(); var photos = Library.Photos();
            _homeSub.text = _homeTab == 0 ? "Vos maquettes BIM, à visiter en 3D ou à poser en réalité augmentée sur le chantier." : "Captures du chantier et de la maquette, avec vos annotations.";
            if (_arSub != null) _arSub.text = models.FirstOrDefault()?.Name ?? "Maquette d'exemple";
            // stockage
            _homeQuota.Clear();
            long used = Library.UsedBytes();
            var hd = El("row sp-between"); hd.Add(L("SUR CET APPAREIL", "eyebrow")); hd.Add(L(Account.Access.Level == AccessLevel.Admin ? "Administrateur" : Account.Full ? "Licence ARCHIVISION" : "Découverte", "pill")); _homeQuota.Add(hd);
            long free = 0; try { free = new DriveInfo(Path.GetPathRoot(Application.persistentDataPath) ?? "/").AvailableFreeSpace; } catch { }
            var num = El("row"); num.style.alignItems = Align.FlexEnd; num.Add(L(Fr.Size(used), "quota-num")); if (free > 0) { var f = L($" / {Fr.Size(free)} libres", "t-small t-mut"); f.style.marginBottom = 4; num.Add(f); } _homeQuota.Add(num);
            var bar = El("bar"); var fill = El(); fill.style.width = Length.Percent(free > 0 ? Mathf.Clamp(used / (float)(used + free) * 100, 1, 100) : 1); bar.Add(fill); _homeQuota.Add(bar);
            _homeQuota.Add(KV("Maquettes", models.Count.ToString())); _homeQuota.Add(KV("Photos", photos.Count.ToString()));
            _homeQuota.Add(KV("Réalité augmentée", ARController.Availability == ArAvailability.Supported ? "disponible" : ARController.Availability == ArAvailability.Unsupported ? "non prise en charge" : ARController.Availability == ArAvailability.NeedsInstall ? "ARCore à installer" : "vérification…"));
            // offre découverte
            _homeOffer.Clear(); bool showOffer = !Account.Full && Account.Access.Level != AccessLevel.Pending && PlayerPrefs.GetInt("av.offerHide." + Account.Me?.Uid, 0) == 0;
            _homeOffer.Show(showOffer);
            if (showOffer)
            {
                _homeOffer.Add(L("Mode découverte", "t-bold"));
                _homeOffer.Add(L("Visitez la maquette d'exemple en 3D. L'import de vos maquettes, la réalité augmentée et les exports demandent une licence ARCHIVISION.", "t-small t-wrap"));
                var r = El("row"); r.style.marginTop = 8; r.style.flexWrap = Wrap.Wrap;
                r.Add(Link("Voir les offres", () => Application.OpenURL(AVConfig.OffersUrl))); r.Add(Link("J'ai une clé de licence", () => ShowLicence()));
                r.Add(Link("Masquer", () => { PlayerPrefs.SetInt("av.offerHide." + Account.Me?.Uid, 1); RenderHome(); })); _homeOffer.Add(r);
            }
            _homeBody.Clear();
            if (_homeTab == 0)
            {
                if (models.Count == 0)
                {
                    var e = El("empty"); e.Add(Icon("cube", "l mut")); var t = L("Aucune maquette trouvée.", "h3"); t.style.marginTop = 10; e.Add(t);
                    e.Add(L("Importez un fichier GLB, glTF ou IFC pour commencer (export « Archi Vision » d'ARCHIPLAN, Revit, ArchiCAD, SketchUp…), ou ouvrez la maquette d'exemple.", "sub"));
                    var r = El("row"); r.Add(Btn("Importer une maquette", ImportPick, "primary small")); var s = Btn("Maquette d'exemple", () => OpenSample(false), "small"); s.style.marginLeft = 8; r.Add(s); e.Add(r);
                    _homeBody.Add(e); return;
                }
                _homeBody.Add(L("MAQUETTES RÉCENTES", "sec-title"));
                var g = El("grid");
                foreach (var m in models) g.Add(ModelCard(m, photos.Count(p => p.ModelId == m.Id)));
                _homeBody.Add(g);
                _homeBody.Add(L("Les maquettes sont gardées sur cet appareil, pour votre compte.", "t-small t-mut t-wrap"));
            }
            else
            {
                if (photos.Count == 0)
                {
                    var e = El("empty"); e.Add(Icon("image", "l mut")); var t = L("Aucune photo pour le moment.", "h3"); t.style.marginTop = 10; e.Add(t);
                    e.Add(L("Pendant la visite 3D ou en réalité augmentée, touchez « Capture » : la vue est enregistrée ici, avec vos annotations.", "sub")); _homeBody.Add(e); return;
                }
                _homeBody.Add(L("CAPTURES DU CHANTIER ET DE LA MAQUETTE", "sec-title"));
                var g = El("grid"); var ids = photos.Select(p => p.Id).ToList();
                foreach (var p in photos) g.Add(PhotoCard(p, ids, "gthumb"));
                _homeBody.Add(g);
            }
        }

        Texture2D Thumb(string path)
        {
            if (_thumbs.TryGetValue(path, out var t) && t) return t;
            t = Library.LoadImage(path); if (t) _thumbs[path] = t; return t;
        }

        VisualElement ModelCard(ModelMeta m, int nPhotos)
        {
            var c = El("mcard"); var inn = El("mcard-in"); c.Add(inn);
            var th = El("mthumb"); Tex(th, Thumb(Library.ThumbPath(m.Id))); var kind = L(m.Kind.ToUpper(), "kind" + (m.Kind == "ifc" ? " ifc" : "")); th.Add(kind); inn.Add(th);
            var meta = El("mmeta"); var tx = El("grow"); tx.style.overflow = Overflow.Hidden;
            tx.Add(L(m.Name, "name")); tx.Add(L($"{Fr.Size(m.SourceSize > 0 ? m.SourceSize : m.GlbSize)} · {Fr.Ago(m.OpenedUtc > m.CreatedUtc ? m.OpenedUtc : m.CreatedUtc)}{(nPhotos > 0 ? $" · {nPhotos} photo{(nPhotos > 1 ? "s" : "")}" : "")}", "meta"));
            meta.Add(tx); meta.Add(IconBtn("dots", () => ModelMenu(m)));
            inn.Add(meta);
            th.RegisterCallback<ClickEvent>(_ => OpenModel(m, false)); tx.RegisterCallback<ClickEvent>(_ => OpenModel(m, false));
            return c;
        }
        VisualElement PhotoCard(PhotoMeta p, List<string> ids, string cls)
        {
            var c = El(cls); var inn = El(cls == "gthumb" ? "gthumb-in" : "pthumb-in"); c.Add(inn);
            var img = El("gthumb-img"); if (cls != "gthumb") img.style.height = 96; Tex(img, Thumb(Library.PhotoThumbPath(p.Id))); inn.Add(img);
            var t = El(); t.style.paddingLeft = 10; t.style.paddingTop = 7; t.style.paddingBottom = 8; t.style.paddingRight = 8;
            if (cls == "gthumb") t.Add(L(p.ModelName, "t-bold t-small"));
            t.Add(L($"{(p.Mode == "ar" ? "AR · " : "")}{Fr.Ago(p.CreatedUtc)}{(string.IsNullOrEmpty(p.Note) ? "" : " · annotée")}", "t-xs t-mut")); inn.Add(t);
            c.RegisterCallback<ClickEvent>(_ => OpenPhoto(p.Id, ids));
            return c;
        }

        void ModelMenu(ModelMeta m)
        {
            Menu(m.Name,
                ("orbit", "Ouvrir la visite 3D", "Orbite, marche, immersion 360°", false, () => OpenModel(m, false)),
                ("ar", "Voir en réalité augmentée", "Poser la maquette sur le sol ou une table", false, () => OpenModel(m, true)),
                ("note", "Renommer", null, false, () => Ask("Renommer la maquette", "Nom", m.Name, "Renommer", v => { m.Name = v; Library.SaveModel(m); RenderHome(); return true; })),
                (null, null, null, false, null),
                ("trash", "Supprimer", "La maquette et ses photos, sur cet appareil", true, () => Confirm("Supprimer ?", $"« {m.Name} » et ses photos seront effacées de cet appareil. Cette action est définitive.", "Supprimer", () => { Library.DeleteModel(m.Id); _thumbs.Clear(); Toast($"Supprimé : {m.Name}"); RenderHome(); })));
        }

        void ArchiPlanHelp()
        {
            Dialog("Une maquette d'ARCHIPLAN", null, c =>
            {
                c.Add(Bullet("Dans ARCHIPLAN, ouvrez votre projet puis Exporter → Archi Vision (.glb), ou le paquet .zip (GLB + IFC + métré)."));
                c.Add(Bullet("Ici, touchez Importer et choisissez le fichier .glb, .ifc ou .zip (dans Téléchargements, Drive…)."));
                c.Add(Bullet("Les niveaux, éléments, compositions de parois, U et familles d'ARCHIPLAN apparaissent dans l'onglet Données."));
            }, ("Ouvrir ARCHIPLAN", "", () => { Application.OpenURL(AVConfig.ArchiPlanUrl); return true; }), ("Importer un fichier", "primary", () => { ImportPick(); return true; }));
        }

        // ------------------------------------------------------------ Import
        async void ImportPick()
        {
            if (NeedLicence()) return;
            string[] files;
            try { files = await AndroidNative.I.PickFiles(); }
            catch (Exception e) { Toast("Sélecteur de fichiers indisponible sur cet appareil. " + e.Message); return; }
            if (files == null || files.Length == 0) return;
            await ImportFiles(files);
        }

        async Task ImportFiles(string[] files)
        {
            string Ext(string p) => Path.GetExtension(p).TrimStart('.').ToLowerInvariant();
            var main = files.FirstOrDefault(f => Ext(f) == "glb") ?? files.FirstOrDefault(f => Ext(f) == "gltf") ?? files.FirstOrDefault(f => Ext(f) == "zip") ?? files.FirstOrDefault(f => Ext(f) == "ifc");
            if (main == null) { Toast("Format non pris en charge. Choisissez un fichier .ifc, .gltf, .glb ou .zip."); return; }
            Busy("Lecture du fichier…");
            var meta = new ModelMeta { Id = Library.NewId("m_"), Name = BaseName(main), CreatedUtc = DateTime.UtcNow, OpenedUtc = DateTime.UtcNow };
            string dir = Library.ModelDir(meta.Id);
            try
            {
                Directory.CreateDirectory(dir);
                if (Ext(main) == "zip") { var x = ModelLoader.ExtractFromZip(main); meta.Name = BaseName(x); main = x; }
                meta.Kind = Ext(main); meta.SourceSize = new FileInfo(main).Length;
                GameObject root;
                if (meta.Kind == "ifc")
                {
                    File.Copy(main, Library.SourcePath(meta.Id, "ifc"), true);
                    Busy("Conversion IFC sur l'appareil…");
                    var glb = await IfcConverter.Convert(main, s => Busy(s));
                    File.WriteAllBytes(Library.GlbPath(meta.Id), glb);
                    root = await ModelLoader.LoadGlb(glb, meta.Name, s => Busy(s));
                }
                else if (meta.Kind == "glb")
                {
                    File.Copy(main, Library.GlbPath(meta.Id), true);
                    root = await ModelLoader.LoadGlb(File.ReadAllBytes(main), meta.Name, s => Busy(s));
                }
                else
                {   // glTF : JSON + fichiers liés, copiés ensemble
                    foreach (var f in files) File.Copy(f, Path.Combine(dir, Path.GetFileName(f)), true);
                    root = await ModelLoader.LoadGltfFile(Path.Combine(dir, Path.GetFileName(main)), meta.Name, s => Busy(s));
                }
                var ap = root.GetComponent<BimNode>()?.ArchiPlan;
                if (ap?["projet"] != null && (meta.Name == "Maquette" || meta.Name.StartsWith("archivision", StringComparison.OrdinalIgnoreCase))) meta.Name = (string)ap["projet"];
                Busy("Aperçu…");
                _mv.SetModel(root, meta);
                meta.UnitScale = _mv.UnitScale; meta.Objects = _mv.Renderers.Count; meta.Triangles = _mv.Triangles; meta.Dims = new[] { _mv.Size.x, _mv.Size.y, _mv.Size.z };
                meta.GlbSize = File.Exists(Library.GlbPath(meta.Id)) ? new FileInfo(Library.GlbPath(meta.Id)).Length : meta.SourceSize;
                await SaveThumb(meta.Id);
                Library.SaveModel(meta);
                Busy(null); Toast($"Importé : {meta.Name}");
                ShowViewer(false);
            }
            catch (Exception e)
            {
                Busy(null); Debug.LogException(e);
                try { Directory.Delete(dir, true); } catch { }
                Toast("Import impossible : " + (e is AVException ? e.Message : "le fichier n'a pas pu être lu."), 4.5f);
            }
        }
        static string BaseName(string p)
        {
            var n = Path.GetFileNameWithoutExtension(p).Replace('_', ' ').Trim();
            if (n.EndsWith(" archivision", StringComparison.OrdinalIgnoreCase)) n = n.Substring(0, n.Length - 12).Trim();
            return n.Length > 0 ? n : "Maquette";
        }

        async Task SaveThumb(string id)
        {
            tourCamera.enabled = true; _tour.Frame("iso", true); await Task.Yield();
            var rt = RenderTexture.GetTemporary(480, 300, 24, RenderTextureFormat.ARGB32); rt.antiAliasing = 4;
            var prev = tourCamera.targetTexture; tourCamera.targetTexture = rt; var bg = tourCamera.backgroundColor; tourCamera.backgroundColor = new Color(.949f, .965f, .976f);
            tourCamera.Render(); tourCamera.targetTexture = prev; tourCamera.backgroundColor = bg;
            var a = RenderTexture.active; RenderTexture.active = rt; var t = new Texture2D(480, 300, TextureFormat.RGB24, false); t.ReadPixels(new Rect(0, 0, 480, 300), 0, 0); t.Apply(); RenderTexture.active = a;
            RenderTexture.ReleaseTemporary(rt);
            File.WriteAllBytes(Library.ThumbPath(id), t.EncodeToJPG(80)); Destroy(t);
            _thumbs.Remove(Library.ThumbPath(id));
        }

        // ------------------------------------------------------------ Ouverture
        public async void OpenModel(ModelMeta m, bool ar)
        {
            if (_mv.Meta?.Id != m.Id || !_mv.Root)
            {
                Busy($"Ouverture de « {m.Name} »…");
                try
                {
                    GameObject root;
                    var glb = Library.GlbPath(m.Id);
                    if (File.Exists(glb)) root = await ModelLoader.LoadGlb(File.ReadAllBytes(glb), m.Name, s => Busy(s));
                    else { var gltf = Directory.GetFiles(Library.ModelDir(m.Id), "*.gltf").FirstOrDefault(); if (gltf == null) throw new AVException("app/missing", "Maquette introuvable sur cet appareil."); root = await ModelLoader.LoadGltfFile(gltf, m.Name, s => Busy(s)); }
                    _mv.SetModel(root, m);
                    m.OpenedUtc = DateTime.UtcNow; Library.SaveModel(m);
                }
                catch (Exception e) { Busy(null); Debug.LogException(e); Toast(e is AVException ? e.Message : "Impossible d'ouvrir la maquette."); return; }
                Busy(null);
            }
            ShowViewer(ar);
        }
        public void OpenSample(bool ar)
        {
            if (_mv.Meta?.Id != "sample") _mv.SetModel(SampleHouse.Build(), new ModelMeta { Id = "sample", Name = "Maison d'exemple", Kind = "exemple", CreatedUtc = DateTime.UtcNow });
            ShowViewer(ar);
        }

    }
}
