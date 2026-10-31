using UnityEngine;
using UnityEngine.EventSystems;
using IntersectionMaster.MapEditing;
using IntersectionMaster.Traffic;
using IntersectionMaster.UI;

namespace IntersectionMaster.Core
{
    /// <summary>
    /// Arranque automático: crea cámara, sistemas e interfaz por código al cargar cualquier escena,
    /// de modo que el proyecto funciona con una escena vacía (no hay que montar nada a mano en el editor).
    /// </summary>
    public static class GameBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
#if UNITY_2023_1_OR_NEWER
            if (Object.FindFirstObjectByType<GameController>() != null) return;
#else
            if (Object.FindObjectOfType<GameController>() != null) return;
#endif
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            Screen.orientation = ScreenOrientation.AutoRotation;
            Screen.autorotateToLandscapeLeft = true;
            Screen.autorotateToLandscapeRight = true;
            Screen.autorotateToPortrait = false;
            Screen.autorotateToPortraitUpsideDown = false;

            Camera camera = Camera.main;
            if (camera == null)
            {
                var camGo = new GameObject("Main Camera");
                camGo.tag = "MainCamera";
                camera = camGo.AddComponent<Camera>();
                camGo.AddComponent<AudioListener>();
            }
            camera.orthographic = true;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color32(24, 28, 36, 255);
            camera.orthographicSize = 6f;
            camera.transform.position = new Vector3(7f, 4f, -10f);

            var root = new GameObject("IntersectionMaster");
            var camCtl = root.AddComponent<CameraController>();
            camCtl.cam = camera;
            var renderer = root.AddComponent<MapRenderer>();
            var sim = root.AddComponent<TrafficSimulationManager>();
            var editor = root.AddComponent<MapEditorController>();
            editor.cam = camCtl;
            editor.mapRenderer = renderer;
            var game = root.AddComponent<GameController>();
            var ui = root.AddComponent<MobileUIController>();

            game.cam = camCtl;
            game.mapRenderer = renderer;
            game.sim = sim;
            game.editor = editor;
            game.ui = ui;
            ui.game = game;

#if UNITY_2023_1_OR_NEWER
            bool hasEventSystem = Object.FindFirstObjectByType<EventSystem>() != null;
#else
            bool hasEventSystem = Object.FindObjectOfType<EventSystem>() != null;
#endif
            if (!hasEventSystem)
            {
                var es = new GameObject("EventSystem");
                es.AddComponent<EventSystem>();
                es.AddComponent<StandaloneInputModule>();
            }
        }
    }
}
