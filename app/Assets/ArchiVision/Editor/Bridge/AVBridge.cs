// ARCHIVISION · pont de pilotage de l'éditeur (outil de développement, éditeur uniquement, jamais dans l'APK).
// Lit les commandes déposées dans <projet>/AVBridge/cmd/*.json, les exécute et écrit le résultat dans <projet>/AVBridge/out/.
// Journal de la console Unity : <projet>/AVBridge/console.log ; erreurs de compilation : <projet>/AVBridge/compile.log
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace StudioPrism.ArchiVision.EditorBridge
{
    [InitializeOnLoad]
    public static class AVBridge
    {
        static readonly string Root = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "AVBridge"));
        static readonly string CmdDir = Path.Combine(Root, "cmd");
        static readonly string OutDir = Path.Combine(Root, "out");
        static double _next;
        static Request _pkgReq; static string _pkgId;
        static readonly object _lock = new object();

        static AVBridge()
        {
            Directory.CreateDirectory(CmdDir); Directory.CreateDirectory(OutDir);
            EditorApplication.update += Tick;
            Application.logMessageReceivedThreaded += OnLog;
            CompilationPipeline.assemblyCompilationFinished += OnCompiled;
            CompilationPipeline.compilationStarted += _ => File.WriteAllText(Path.Combine(Root, "compile.log"), $"[{DateTime.Now:HH:mm:ss}] compilation…\n");
            File.WriteAllText(Path.Combine(Root, "alive.txt"), $"{DateTime.Now:O} Unity {Application.unityVersion}\n");
        }

        static void OnLog(string msg, string stack, LogType type)
        {
            if (type == LogType.Log && !msg.StartsWith("[AV]")) return;
            lock (_lock)
            {
                try { File.AppendAllText(Path.Combine(Root, "console.log"), $"[{DateTime.Now:HH:mm:ss}] {type}: {msg}\n" + (type == LogType.Exception || type == LogType.Error ? stack + "\n" : "")); } catch { }
            }
        }

        static void OnCompiled(string asm, CompilerMessage[] msgs)
        {
            var errs = msgs.Where(m => m.type == CompilerMessageType.Error).ToArray();
            var sb = new StringBuilder();
            sb.AppendLine($"[{DateTime.Now:HH:mm:ss}] {Path.GetFileName(asm)} : {errs.Length} erreur(s)");
            foreach (var m in errs) sb.AppendLine($"  {m.file}({m.line},{m.column}): {m.message}");
            try { File.AppendAllText(Path.Combine(Root, "compile.log"), sb.ToString()); } catch { }
        }

        static void Tick()
        {
            if (EditorApplication.timeSinceStartup < _next) return;
            _next = EditorApplication.timeSinceStartup + 1.0;
            File.WriteAllText(Path.Combine(Root, "heartbeat.txt"), $"{DateTime.Now:O} compiling={EditorApplication.isCompiling} updating={EditorApplication.isUpdating}\n");
            if (_pkgReq != null)
            {
                if (!_pkgReq.IsCompleted) return;
                Write(_pkgId, _pkgReq.Status == StatusCode.Success ? "ok" : "error", _pkgReq.Status == StatusCode.Success ? DescribePkg(_pkgReq) : _pkgReq.Error?.message);
                _pkgReq = null; return;
            }
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            var f = Directory.GetFiles(CmdDir, "*.json").OrderBy(x => x).FirstOrDefault();
            if (f == null) return;
            string id = Path.GetFileNameWithoutExtension(f), json = File.ReadAllText(f);
            File.Delete(f);
            try { Run(id, JsonUtility.FromJson<Cmd>(json)); }
            catch (Exception e) { Write(id, "error", e.ToString()); }
        }

        [Serializable] class Cmd { public string op; public string[] args; }

        static void Run(string id, Cmd c)
        {
            switch (c.op)
            {
                case "ping": Write(id, "ok", $"Unity {Application.unityVersion} · cible {EditorUserBuildSettings.activeBuildTarget} · Android installé : {BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android)}"); break;
                case "refresh": AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate); Write(id, "ok", "refresh"); break;
                case "packages": _pkgId = id; _pkgReq = Client.AddAndRemove(c.args ?? new string[0], null); break;
                case "list": _pkgId = id; _pkgReq = Client.List(true, false); break;
                case "invoke":
                    {   // args[0] = type complet (avec assembly éventuelle), args[1] = méthode statique sans paramètre (ou string[])
                        var t = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(c.args[0])).FirstOrDefault(x => x != null);
                        if (t == null) { Write(id, "error", "type introuvable : " + c.args[0]); break; }
                        var m = t.GetMethod(c.args[1], BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                        if (m == null) { Write(id, "error", "méthode introuvable : " + c.args[1]); break; }
                        var ps = m.GetParameters();
                        object r = ps.Length == 0 ? m.Invoke(null, null) : m.Invoke(null, new object[] { c.args.Skip(2).ToArray() });
                        Write(id, "ok", r?.ToString() ?? "(null)"); break;
                    }
                case "menu": Write(id, EditorApplication.ExecuteMenuItem(c.args[0]) ? "ok" : "error", c.args[0]); break;
                default: Write(id, "error", "op inconnue : " + c.op); break;
            }
        }

        static string DescribePkg(Request r)
        {
            if (r is AddAndRemoveRequest a) return string.Join("\n", a.Result.Select(p => $"{p.name}@{p.version}"));
            if (r is ListRequest l) return string.Join("\n", l.Result.Select(p => $"{p.name}@{p.version}  [{p.source}] latest={p.versions.latestCompatible}"));
            return "ok";
        }

        static void Write(string id, string status, string body)
        {
            File.WriteAllText(Path.Combine(OutDir, id + ".txt"), $"{status}\n{DateTime.Now:O}\n{body}\n");
        }
    }
}
