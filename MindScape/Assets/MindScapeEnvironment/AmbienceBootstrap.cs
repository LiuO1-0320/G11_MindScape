using UnityEngine;
using UnityEngine.SceneManagement;

namespace MindScape.Environment
{
    public static class AmbienceBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            SceneManager.sceneLoaded -= Install;
            SceneManager.sceneLoaded += Install;
        }
        static void Install(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != "BasicScene" || Object.FindObjectOfType<AmbienceController>() != null) return;
            var room = GameObject.Find("Room");
            if (room == null) return;
            var root = new GameObject("Mode B Ambience");
            SceneManager.MoveGameObjectToScene(root, scene);
            var controller = root.AddComponent<AmbienceController>();
            // Deliberately scope to room lights plus the named scene sun, excluding NPC eye effects.
            var targets = new System.Collections.Generic.List<Light>(room.GetComponentsInChildren<Light>());
            var sun = GameObject.Find("Directional Light");
            if (sun != null && sun.TryGetComponent<Light>(out var light) && !targets.Contains(light)) targets.Add(light);
            controller.lights = targets.ToArray();
            controller.sadnessIntroSound = Resources.Load<AudioClip>("MindScapeAmbience/SadPiano");
            controller.sadnessSound = Resources.Load<AudioClip>("MindScapeAmbience/SoftRain");
            controller.angerSound = Resources.Load<AudioClip>("MindScapeAmbience/QuietStream");
            controller.calmSound = Resources.Load<AudioClip>("MindScapeAmbience/GentleAir");
            controller.Initialize();
            root.AddComponent<AmbienceCommandBridge>().Bind(controller);
            root.AddComponent<AmbiencePanel>().Bind(controller);
        }
    }
}
