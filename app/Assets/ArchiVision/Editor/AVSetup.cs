// ARCHIVISION · configuration automatique du projet Unity et construction de l'APK.
// Menu « ArchiVision » de l'éditeur, ou commandes du pont (AVBridge) : invoke StudioPrism.ArchiVision.EditorTools.AVSetup <Méthode>.
using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.Management;
using Unity.XR.CoreUtils;

namespace StudioPrism.ArchiVision.EditorTools
{
    public static class AVSetup
    {
        public const string Root = "Assets/ArchiVision";
        public const string ScenePath = Root + "/Scenes/Main.unity";
        static readonly StringBuilder Log = new StringBuilder();
        static void L(string s) { Log.AppendLine(s); Debug.Log("[AV] " + s); }

        [MenuItem("ArchiVision/1. Configurer le projet (Android, XR, URP)")]
        public static string ConfigureProject()
        {
            Log.Clear();
            // --- Identité et Android
            PlayerSettings.companyName = "Studio Prism";
            PlayerSettings.productName = "ArchiVision";
            PlayerSettings.bundleVersion = "1.0.0";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.studioprism.archivision");
            PlayerSettings.Android.bundleVersionCode = Math.Max(1, PlayerSettings.Android.bundleVersionCode);
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.OpenGLES3 });
            PlayerSettings.Android.forceInternetPermission = true;
            PlayerSettings.Android.ARCoreEnabled = false; // ARCore est fourni par le paquet XR (ARCore XR Plugin)
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToPortrait = true;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.Android.renderOutsideSafeArea = true;
            PlayerSettings.Android.startInFullscreen = false;
            PlayerSettings.statusBarHidden = false;
            PlayerSettings.SplashScreen.backgroundColor = new Color32(11, 31, 69, 255);
            try { PlayerSettings.SplashScreen.showUnityLogo = false; } catch { }
            PlayerSettings.SplashScreen.show = false;
            PlayerSettings.gcIncremental = true;
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.Android, ManagedStrippingLevel.Low);
            SetIcons();
            L("Réglages Android : com.studioprism.archivision, IL2CPP ARM64, API 26+, OpenGL ES 3, rotation automatique.");

            ConfigureXR();
            ConfigureURP();
            ConfigureShaders();
            AssetDatabase.SaveAssets();
            return Log.ToString();
        }

        static void SetIcons()
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Resources/Images/app-icon.png");
            if (!tex) { L("Icône introuvable."); return; }
            var sizes = PlayerSettings.GetIconSizes(NamedBuildTarget.Unknown, IconKind.Any);
            PlayerSettings.SetIcons(NamedBuildTarget.Unknown, Enumerable.Repeat(tex, Math.Max(1, sizes.Length)).ToArray(), IconKind.Any);
            L("Icône de l'app : emblème ArchiVision.");
        }

        static void ConfigureXR()
        {
            if (!EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey, out XRGeneralSettingsPerBuildTarget perTarget) || perTarget == null)
            {
                Directory.CreateDirectory("Assets/XR");
                perTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                AssetDatabase.CreateAsset(perTarget, "Assets/XR/XRGeneralSettingsPerBuildTarget.asset");
                EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, perTarget, true);
            }
            foreach (var (group, loader) in new[] { (BuildTargetGroup.Android, "UnityEngine.XR.ARCore.ARCoreLoader"), (BuildTargetGroup.Standalone, "UnityEngine.XR.Simulation.SimulationLoader") })
            {
                if (!perTarget.HasSettingsForBuildTarget(group)) perTarget.CreateDefaultSettingsForBuildTarget(group);
                if (!perTarget.HasManagerSettingsForBuildTarget(group)) perTarget.CreateDefaultManagerSettingsForBuildTarget(group);
                var s = perTarget.SettingsForBuildTarget(group);
                s.InitManagerOnStart = true;
                bool ok = s.AssignedSettings.activeLoaders.Any(l => l && l.GetType().FullName == loader) || XRPackageMetadataStore.AssignLoader(s.AssignedSettings, loader, group);
                EditorUtility.SetDirty(s); EditorUtility.SetDirty(s.AssignedSettings);
                L($"XR {group} : {loader.Split('.').Last()} {(ok ? "activé" : "NON activé")}");
            }
            // ARCore facultatif : l'app s'installe aussi sur les appareils sans ARCore (visite 3D seule)
            var arcoreSettingsType = Type.GetType("UnityEditor.XR.ARCore.ARCoreSettings, Unity.XR.ARCore.Editor");
            if (arcoreSettingsType != null)
            {
                var get = arcoreSettingsType.GetMethod("GetOrCreateSettings", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                var st = get?.Invoke(null, null) as UnityEngine.Object;
                if (st)
                {
                    var so = new SerializedObject(st);
                    var req = so.FindProperty("m_Requirement"); if (req != null) req.intValue = 1; // 0 = Required, 1 = Optional
                    var depth = so.FindProperty("m_Depth"); if (depth != null) depth.intValue = 1;
                    so.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(st);
                    L("ARCore : facultatif (installation possible sans ARCore), profondeur facultative.");
                }
            }
        }

        static void ConfigureURP()
        {
            foreach (var path in new[] { "Assets/Settings/Mobile_Renderer.asset", "Assets/Settings/PC_Renderer.asset" })
            {
                var rd = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
                if (!rd) { L("Renderer introuvable : " + path); continue; }
                if (rd.rendererFeatures.Any(f => f is ARBackgroundRendererFeature)) { L("Image caméra AR déjà branchée : " + path); continue; }
                var feat = ScriptableObject.CreateInstance<ARBackgroundRendererFeature>(); feat.name = "ARBackgroundRendererFeature";
                AssetDatabase.AddObjectToAsset(feat, rd);
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feat, out string _, out long lid);
                var so = new SerializedObject(rd);
                var feats = so.FindProperty("m_RendererFeatures"); var map = so.FindProperty("m_RendererFeatureMap");
                feats.arraySize++; feats.GetArrayElementAtIndex(feats.arraySize - 1).objectReferenceValue = feat;
                map.arraySize++; map.GetArrayElementAtIndex(map.arraySize - 1).longValue = lid;
                so.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(rd);
                L("Image caméra AR (ARBackgroundRendererFeature) ajoutée : " + path);
            }
            var mobile = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/Mobile_RPAsset.asset");
            if (mobile)
            {
                mobile.msaaSampleCount = 4; mobile.supportsHDR = false; mobile.renderScale = 1f; mobile.shadowDistance = 60f;
                EditorUtility.SetDirty(mobile);
                // Android : niveau de qualité « Mobile »
                var names = QualitySettings.names; int idx = Array.FindIndex(names, n => n.ToLower().Contains("mobile"));
                if (idx >= 0)
                {
                    var qs = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset")[0]);
                    var per = qs.FindProperty("m_PerPlatformDefaultQuality");
                    for (int i = 0; i < per.arraySize; i++) { var e = per.GetArrayElementAtIndex(i); if (e.FindPropertyRelative("first").stringValue == "Android") e.FindPropertyRelative("second").intValue = idx; }
                    qs.ApplyModifiedPropertiesWithoutUndo();
                    L($"Qualité Android : {names[idx]} (MSAA 4x, sans HDR).");
                }
            }
        }

        static void ConfigureShaders()
        {
            var gs = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")[0]);
            var always = gs.FindProperty("m_AlwaysIncludedShaders");
            var wanted = AssetDatabase.FindAssets("t:Shader", new[] { "Packages/com.unity.cloud.gltfast" })
                .Select(AssetDatabase.GUIDToAssetPath).Where(p => p.EndsWith(".shadergraph") && !p.Contains("/Tests/"))
                .Select(AssetDatabase.LoadAssetAtPath<Shader>).Where(s => s).ToList();
            foreach (var n in new[] { "Universal Render Pipeline/Lit", "Universal Render Pipeline/Unlit", "Universal Render Pipeline/Simple Lit", "Universal Render Pipeline/Particles/Unlit" })
            { var s = Shader.Find(n); if (s) wanted.Add(s); }
            int added = 0;
            foreach (var s in wanted.Distinct())
            {
                bool has = false; for (int i = 0; i < always.arraySize; i++) if (always.GetArrayElementAtIndex(i).objectReferenceValue == s) has = true;
                if (has) continue; always.arraySize++; always.GetArrayElementAtIndex(always.arraySize - 1).objectReferenceValue = s; added++;
            }
            gs.ApplyModifiedPropertiesWithoutUndo();
            L($"Shaders toujours inclus : +{added} ({string.Join(", ", wanted.Select(s => s.name).Distinct())})");
        }

        [MenuItem("ArchiVision/0. Passer la cible en Android")]
        public static string SwitchToAndroid()
        {
            if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android) return "Déjà sur Android.";
            bool ok = EditorUserBuildSettings.SwitchActiveBuildTargetAsync(BuildTargetGroup.Android, BuildTarget.Android);
            return ok ? "Passage à Android lancé." : "Échec du passage à Android.";
        }

        // ---------------------------------------------------------------- Scène principale
        [MenuItem("ArchiVision/2. Construire la scène")]
        public static string BuildScene()
        {
            Log.Clear();
            Directory.CreateDirectory(Root + "/Scenes"); Directory.CreateDirectory(Root + "/Generated");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Lumière et ambiance
            var sun = new GameObject("Soleil", typeof(Light)).GetComponent<Light>();
            sun.type = LightType.Directional; sun.intensity = 1.25f; sun.color = new Color(1f, .97f, .92f); sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(52, -35, 0);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.82f, .87f, .93f); RenderSettings.ambientEquatorColor = new Color(.66f, .70f, .75f); RenderSettings.ambientGroundColor = new Color(.42f, .44f, .47f);

            // AR : session + XR Origin (mobile), créés par les menus d'AR Foundation pour un câblage conforme
            EditorApplication.ExecuteMenuItem("GameObject/XR/AR Session");
            EditorApplication.ExecuteMenuItem("GameObject/XR/XR Origin (Mobile AR)");
            var session = UnityEngine.Object.FindAnyObjectByType<ARSession>();
            var origin = UnityEngine.Object.FindAnyObjectByType<XROrigin>();
            if (!session || !origin) { L("ERREUR : AR Session ou XR Origin non créés (menus AR Foundation introuvables)."); return Log.ToString(); }
            var arCam = origin.Camera;
            arCam.gameObject.name = "AR Camera"; arCam.tag = "Untagged";
            arCam.nearClipPlane = 0.05f; arCam.farClipPlane = 500f;
            var camMgr = arCam.GetComponent<ARCameraManager>();
            if (camMgr) { camMgr.requestedLightEstimation = UnityEngine.XR.ARFoundation.LightEstimation.AmbientIntensity | UnityEngine.XR.ARFoundation.LightEstimation.AmbientColor | UnityEngine.XR.ARFoundation.LightEstimation.MainLightDirection; camMgr.autoFocusRequested = true; }
            var occ = (arCam.TryGetComponent(out AROcclusionManager __arocclusionmanager) ? __arocclusionmanager : arCam.gameObject.AddComponent<AROcclusionManager>());
            occ.requestedEnvironmentDepthMode = UnityEngine.XR.ARSubsystems.EnvironmentDepthMode.Disabled;
            var planes = (origin.TryGetComponent(out ARPlaneManager __arplanemanager) ? __arplanemanager : origin.gameObject.AddComponent<ARPlaneManager>());
            planes.requestedDetectionMode = UnityEngine.XR.ARSubsystems.PlaneDetectionMode.Horizontal | UnityEngine.XR.ARSubsystems.PlaneDetectionMode.Vertical;
            planes.planePrefab = MakePlanePrefab();
            if (!origin.GetComponent<ARRaycastManager>()) origin.gameObject.AddComponent<ARRaycastManager>();
            if (!origin.GetComponent<ARAnchorManager>()) origin.gameObject.AddComponent<ARAnchorManager>();
            // nettoyage des éléments de démonstration éventuels du menu XR Origin
            foreach (Transform t in origin.transform.Cast<Transform>().ToArray()) if (t.name.Contains("Interaction") || t.name.Contains("Controller") || t.name.Contains("Screen Space")) UnityEngine.Object.DestroyImmediate(t.gameObject);
            session.gameObject.SetActive(false); origin.gameObject.SetActive(false);   // activés à l'entrée en réalité augmentée
            L("AR : AR Session + XR Origin (caméra, plans horizontaux et verticaux, raycasts, ancres, occlusion facultative, lumière estimée).");

            // Caméra de visite 3D
            var tourCam = new GameObject("Camera Visite", typeof(Camera), typeof(AudioListener)).GetComponent<Camera>();
            tourCam.tag = "MainCamera"; tourCam.clearFlags = CameraClearFlags.SolidColor; tourCam.backgroundColor = new Color(.949f, .965f, .976f);
            tourCam.nearClipPlane = 0.05f; tourCam.farClipPlane = 5000f; tourCam.fieldOfView = 50;
            tourCam.transform.position = new Vector3(12, 9, 14); tourCam.transform.LookAt(Vector3.zero);
            var camData = tourCam.GetUniversalAdditionalCameraData(); camData.renderShadows = true; camData.antialiasing = AntialiasingMode.None;

            // Application : interface (UI Toolkit) + contrôleur
            var panel = MakePanelSettings();
            var app = new GameObject("ArchiVision");
            var doc = app.AddComponent<UIDocument>(); doc.panelSettings = panel;
            var appType = Type.GetType("StudioPrism.ArchiVision.App, StudioPrism.ArchiVision");
            if (appType == null) { L("ERREUR : la classe App (runtime) n'est pas compilée."); }
            else
            {
                var comp = app.AddComponent(appType);
                var so = new SerializedObject(comp);
                void Set(string p, UnityEngine.Object v) { var sp = so.FindProperty(p); if (sp != null) sp.objectReferenceValue = v; else L("Champ absent : " + p); }
                Set("tourCamera", tourCam); Set("sun", sun); Set("arSession", session); Set("xrOrigin", origin);
                Set("styleSheet", AssetDatabase.LoadAssetAtPath<StyleSheet>(Root + "/UI/archivision.uss"));
                so.ApplyModifiedPropertiesWithoutUndo();
                L("Application branchée (App + UIDocument).");
            }
            var native = new GameObject("AVNative"); // reçoit les retours du code Android (UnitySendMessage)
            var nativeType = Type.GetType("StudioPrism.ArchiVision.Native.AndroidNative, StudioPrism.ArchiVision");
            if (nativeType != null) native.AddComponent(nativeType);

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            L("Scène enregistrée : " + ScenePath);
            AssetDatabase.SaveAssets();
            return Log.ToString();
        }

        static PanelSettings MakePanelSettings()
        {
            string path = Root + "/Generated/ArchiVisionPanel.asset";
            var ps = AssetDatabase.LoadAssetAtPath<PanelSettings>(path);
            if (!ps) { ps = ScriptableObject.CreateInstance<PanelSettings>(); AssetDatabase.CreateAsset(ps, path); }
            ps.scaleMode = PanelScaleMode.ConstantPhysicalSize; ps.referenceDpi = 160; ps.fallbackDpi = 160;
            ps.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(Root + "/UI/ArchiVisionTheme.tss");
            ps.clearColor = false; ps.sortingOrder = 10;
            EditorUtility.SetDirty(ps);
            return ps;
        }

        static GameObject MakePlanePrefab()
        {
            string path = Root + "/Generated/ARPlane.prefab";
            var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            mat.name = "PlanDetecte";
            mat.SetFloat("_Surface", 1); mat.SetFloat("_Blend", 0); mat.SetOverrideTag("RenderType", "Transparent");
            mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha); mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha); mat.SetInt("_ZWrite", 0);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); mat.renderQueue = (int)RenderQueue.Transparent;
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Resources/Images/plane-dots.png");
            mat.SetTexture("_BaseMap", tex); mat.SetTextureScale("_BaseMap", new Vector2(6, 6)); mat.SetColor("_BaseColor", new Color(1, 1, 1, .9f));
            AssetDatabase.CreateAsset(mat, Root + "/Generated/PlanDetecte.mat");
            var go = new GameObject("AR Plane", typeof(ARPlane), typeof(ARPlaneMeshVisualizer), typeof(MeshFilter), typeof(MeshRenderer), typeof(LineRenderer));
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            var lr = go.GetComponent<LineRenderer>(); lr.widthMultiplier = 0.006f; lr.useWorldSpace = false; lr.loop = true;
            var lm = new Material(Shader.Find("Universal Render Pipeline/Unlit")); lm.SetColor("_BaseColor", new Color(.18f, .83f, .71f, 1)); AssetDatabase.CreateAsset(lm, Root + "/Generated/BordPlan.mat");
            lr.sharedMaterial = lm;
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path); UnityEngine.Object.DestroyImmediate(go);
            return prefab;
        }

        // ---------------------------------------------------------------- APK
        [MenuItem("ArchiVision/3. Construire l'APK")]
        public static string BuildApk()
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android) return "Cible active : " + EditorUserBuildSettings.activeBuildTarget + " — lancer d'abord « Passer la cible en Android ».";
            Directory.CreateDirectory("Builds");
            EditorUserBuildSettings.buildAppBundle = false;
            var opts = new BuildPlayerOptions { scenes = new[] { ScenePath }, locationPathName = "Builds/ArchiVision.apk", target = BuildTarget.Android, targetGroup = BuildTargetGroup.Android, options = BuildOptions.None };
            var report = BuildPipeline.BuildPlayer(opts);
            var s = report.summary; var sb = new StringBuilder();
            sb.AppendLine($"Résultat : {s.result} · {s.totalSize / 1e6:0.0} Mo · {s.totalTime.TotalMinutes:0.0} min · erreurs {s.totalErrors} · avertissements {s.totalWarnings}");
            foreach (var st in report.steps) foreach (var m in st.messages.Where(m => m.type == LogType.Error || m.type == LogType.Exception)) sb.AppendLine($"[{st.name}] {m.content}");
            File.WriteAllText("AVBridge/build.txt", sb.ToString());
            return sb.ToString();
        }
    }
}
