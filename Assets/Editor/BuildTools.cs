#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace IntersectionMaster.EditorTools
{
    /// <summary>Configura Android y compila APK/AAB. Menú: Intersection Master > ...  (o por línea de comandos con -executeMethod).</summary>
    public static class BuildTools
    {
        const string Scene = "Assets/Scenes/Main.unity";

        // Compilaciones en la nube (Unity Cloud Build) no ejecutan el menú: se aplica la configuración sola.
        [InitializeOnLoadMethod]
        static void AutoConfigureInBatchMode()
        {
            if (!Application.isBatchMode) return;
            ApplyPlayerSettings();
        }

        static void ApplyPlayerSettings()
        {
            PlayerSettings.companyName = "IntersectionMaster";
            PlayerSettings.productName = "Intersection Master";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.intersectionmaster.game");
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToPortrait = true;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel24;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARMv7 | AndroidArchitecture.ARM64;
        }

        [MenuItem("Intersection Master/1. Configurar proyecto Android")]
        public static void Configure()
        {
            ApplyPlayerSettings();

            // Los scripts usan la clase Input clásica: "Both" evita errores si el paquete Input System está presente.
            var so = new SerializedObject(AssetDatabase.LoadMainAssetAtPath("ProjectSettings/ProjectSettings.asset"));
            var prop = so.FindProperty("activeInputHandler");
            if (prop != null) { prop.intValue = 2; so.ApplyModifiedProperties(); }

            CreateScene();
            Debug.Log("Intersection Master: proyecto configurado. Si cambió el manejo de entrada, reinicia Unity.");
        }

        static void CreateScene()
        {
            Directory.CreateDirectory("Assets/Scenes");
            if (!File.Exists(Scene))
            {
                var s = UnityEditor.SceneManagement.EditorSceneManager.NewScene(
                    UnityEditor.SceneManagement.NewSceneSetup.DefaultGameObjects,
                    UnityEditor.SceneManagement.NewSceneMode.Single);
                UnityEditor.SceneManagement.EditorSceneManager.SaveScene(s, Scene);
            }
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(Scene, true) };
        }

        [MenuItem("Intersection Master/2. Compilar APK")]
        public static void BuildApk() { Build(false); }

        [MenuItem("Intersection Master/3. Compilar AAB (Google Play)")]
        public static void BuildAab() { Build(true); }

        static void Build(bool aab)
        {
            Configure();
            Directory.CreateDirectory("Builds");
            EditorUserBuildSettings.buildAppBundle = aab;
            var opts = new BuildPlayerOptions
            {
                scenes = new[] { Scene },
                locationPathName = "Builds/IntersectionMaster" + (aab ? ".aab" : ".apk"),
                target = BuildTarget.Android,
                options = BuildOptions.None
            };
            BuildReport r = BuildPipeline.BuildPlayer(opts);
            Debug.Log("Compilación: " + r.summary.result + " -> " + opts.locationPathName);
            if (Application.isBatchMode) EditorApplication.Exit(r.summary.result == BuildResult.Succeeded ? 0 : 1);
        }
    }
}
#endif
