// ARCHIVISION · visionneuse photo : image plein écran, glisser pour passer à la suivante, note, enregistrement dans la galerie du téléphone,
// partage et suppression (avec confirmation).
using System;
using System.Collections.Generic;
using StudioPrism.ArchiVision.Native;
using StudioPrism.ArchiVision.Services;
using UnityEngine;
using UnityEngine.UIElements;
using static StudioPrism.ArchiVision.UI.K;

namespace StudioPrism.ArchiVision
{
    public partial class App
    {
        VisualElement _photo; List<string> _photoIds; int _photoIdx; Texture2D _photoTex; PhotoMeta _photoMeta; TextField _photoNote;

        public void OpenPhoto(string id, List<string> ids)
        {
            _photoIds = ids != null && ids.Count > 0 ? new List<string>(ids) : new List<string> { id };
            _photoIdx = Mathf.Max(0, _photoIds.IndexOf(id));
            RenderPhoto();
        }

        public void ClosePhoto()
        {
            SavePhotoNote();
            _photo?.RemoveFromHierarchy(); _photo = null; _photoNote = null; _photoMeta = null;
            if (_photoTex) { Destroy(_photoTex); _photoTex = null; }
        }

        void SavePhotoNote()
        {
            if (_photoMeta == null || _photoNote == null) return;
            var n = _photoNote.value.Trim(); if (n == (_photoMeta.Note ?? "")) return;
            _photoMeta.Note = n; Library.SavePhotoMeta(_photoMeta);
        }

        void StepPhoto(int d)
        {
            if (_photoIds == null || _photoIds.Count < 2) return;
            SavePhotoNote(); _photoIdx = (_photoIdx + d + _photoIds.Count) % _photoIds.Count; RenderPhoto();
        }

        void RenderPhoto()
        {
            var p = Library.GetPhoto(_photoIds[_photoIdx]);
            if (p == null) { ClosePhoto(); Toast("Photo introuvable."); return; }
            _photoMeta = p;
            if (_photoTex) Destroy(_photoTex); _photoTex = Library.LoadImage(Library.PhotoPath(p.Id));
            _photo?.RemoveFromHierarchy();
            _photo = El("photo"); _photo.style.position = Position.Absolute; _photo.style.left = _photo.style.top = _photo.style.right = _photo.style.bottom = 0;
            _photo.style.paddingTop = _root.resolvedStyle.paddingTop; _photo.style.paddingBottom = _root.resolvedStyle.paddingBottom;
            _photo.style.paddingLeft = _root.resolvedStyle.paddingLeft; _photo.style.paddingRight = _root.resolvedStyle.paddingRight;

            // barre du haut
            var top = El(); top.style.flexDirection = FlexDirection.Row; top.style.alignItems = Align.Center; top.style.paddingLeft = top.style.paddingRight = 8; top.style.paddingTop = 6;
            var back = IconBtn("close", ClosePhoto, null, "Fermer"); back.Q(className: "ico").AddToClassList("white"); top.Add(back);
            var tt = El("grow"); tt.style.marginLeft = 8;
            var t1 = L(p.ModelName ?? "Photo", "b"); t1.style.color = Color.white; t1.style.fontSize = 15; tt.Add(t1);
            var t2 = L($"{(p.Mode == "ar" ? "Réalité augmentée" : "Visite 3D")} · {Fr.DateTime(p.CreatedUtc)}", "t-small"); t2.style.color = new Color(1, 1, 1, .7f); tt.Add(t2);
            top.Add(tt);
            if (_photoIds.Count > 1) { var n = L($"{_photoIdx + 1} / {_photoIds.Count}", "t-mono t-small"); n.style.color = new Color(1, 1, 1, .8f); n.style.marginRight = 6; top.Add(n); }
            _photo.Add(top);

            // image + panneau (côte à côte sur tablette)
            var main = El("grow"); main.style.flexDirection = _wide ? FlexDirection.Row : FlexDirection.Column;
            var imgWrap = El("grow"); imgWrap.style.flexDirection = FlexDirection.Row; imgWrap.style.alignItems = Align.Center;
            var img = El("photo-img"); Tex(img, _photoTex); img.style.alignSelf = Align.Stretch; imgWrap.Add(img);
            if (_photoIds.Count > 1)
            {
                var prev = IconBtn("back", () => StepPhoto(-1)); prev.Q(className: "ico").AddToClassList("white"); prev.style.position = Position.Absolute; prev.style.left = 6;
                var next = IconBtn("chevron", () => StepPhoto(1)); next.Q(className: "ico").AddToClassList("white"); next.style.position = Position.Absolute; next.style.right = 6;
                if (_wide) { imgWrap.Add(prev); imgWrap.Add(next); }
                WireSwipe(img);
            }
            main.Add(imgWrap);

            var side = El("photo-side");
            if (_wide) { side.style.width = 360; side.style.borderTopRightRadius = 0; side.style.borderBottomLeftRadius = 18; side.style.marginTop = 8; }
            _photoNote = Input(p.Note, false, true, 600); _photoNote.style.maxHeight = 96; _photoNote.RegisterCallback<FocusOutEvent>(_ => SavePhotoNote());
            side.Add(Field("Note", _photoNote, $"{p.W} × {p.H} px · {Fr.Size(p.Size)}"));
            var acts = El(); acts.style.flexDirection = FlexDirection.Row; acts.style.marginTop = 10; acts.style.flexWrap = Wrap.Wrap;
            Button A(string text, string icon, Action a, string cls) { var b = Btn(text, a, "small " + cls, icon); b.style.flexGrow = 1; b.style.marginRight = 6; b.style.marginBottom = 6; return b; }
            acts.Add(A("Galerie", "download", SavePhotoToPhone, ""));
            acts.Add(A("Partager", "share", SharePhoto, ""));
            acts.Add(A("Supprimer", "trash", DeleteCurrentPhoto, "danger"));
            side.Add(acts);
            main.Add(side);
            _photo.Add(main);
            _root.Add(_photo);
        }

        void WireSwipe(VisualElement e)
        {
            int id = -1; Vector2 start = default;
            e.RegisterCallback<PointerDownEvent>(ev => { id = ev.pointerId; start = ev.position; });
            e.RegisterCallback<PointerUpEvent>(ev =>
            {
                if (ev.pointerId != id) return; id = -1;
                var d = (Vector2)ev.position - start;
                if (Mathf.Abs(d.x) > 60 && Mathf.Abs(d.x) > Mathf.Abs(d.y) * 1.5f) StepPhoto(d.x < 0 ? 1 : -1);
            });
        }

        string PhotoFileName(PhotoMeta p) => $"ArchiVision_{p.CreatedUtc.ToLocalTime():yyyyMMdd_HHmmss}.jpg";

        void SavePhotoToPhone()
        {
            var p = _photoMeta; if (p == null) return;
            try
            {
                var uri = AndroidNative.I.SaveImageToGallery(Library.PhotoPath(p.Id), PhotoFileName(p));
                Toast(uri != null ? "Photo enregistrée dans la galerie (Images/ArchiVision)." : "Autorisez l'accès aux photos puis réessayez.");
            }
            catch (Exception e) { Debug.LogException(e); Toast("Échec de l'enregistrement : " + e.Message); }
        }

        void SharePhoto()
        {
            var p = _photoMeta; if (p == null) return;
            SavePhotoNote();
            try
            {
                var uri = AndroidNative.I.SaveImageToGallery(Library.PhotoPath(p.Id), PhotoFileName(p));
                if (uri == null) { Toast("Autorisez l'accès aux photos puis réessayez."); return; }
                var text = $"{p.ModelName} · {Fr.DateTime(p.CreatedUtc)}" + (string.IsNullOrEmpty(p.Note) ? "" : "\n" + p.Note) + "\nCapture ArchiVision";
                AndroidNative.I.Share(uri, "image/jpeg", text);
            }
            catch (Exception e) { Debug.LogException(e); Toast("Partage impossible : " + e.Message); }
        }

        void DeleteCurrentPhoto()
        {
            var p = _photoMeta; if (p == null) return;
            Confirm("Supprimer la photo ?", "Elle sera retirée de la galerie ArchiVision de cet appareil. Les copies déjà enregistrées dans la galerie du téléphone sont conservées.", "Supprimer", () =>
            {
                Library.DeletePhoto(p.Id); _photoIds.RemoveAt(_photoIdx); _photoMeta = null;
                if (_photoIds.Count == 0) ClosePhoto(); else { _photoIdx %= _photoIds.Count; RenderPhoto(); }
                if (_screen == "viewer" && _ptab == "gallery") RenderPanel(); else if (_screen == "home") RenderHome();
                Toast("Photo supprimée.");
            });
        }
    }
}
