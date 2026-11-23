using System.Collections;
using System.IO;
using MindScape.Environment;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Collections.Generic;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public sealed class AmbiencePlayModeTests
{
    GameObject root;
    AmbienceController controller;
    Light lamp;
    [SetUp] public void Setup()
    {
        Assert.IsNull(AmbienceController.Instance, "Run tests in the Test Runner's isolated scene.");
        root = new GameObject("Environment test");
        lamp = new GameObject("Test lamp").AddComponent<Light>(); lamp.transform.SetParent(root.transform);
        lamp.intensity = 2; lamp.color = Color.white;
        controller = root.AddComponent<AmbienceController>(); controller.lights = new[] { lamp };
        controller.sadnessIntroSound = Resources.Load<AudioClip>("MindScapeAmbience/SadPiano");
        controller.sadnessSound = Resources.Load<AudioClip>("MindScapeAmbience/SoftRain");
        controller.angerSound = Resources.Load<AudioClip>("MindScapeAmbience/QuietStream");
        controller.calmSound = Resources.Load<AudioClip>("MindScapeAmbience/GentleAir");
        Assert.IsNotNull(controller.sadnessSound); Assert.IsNotNull(controller.angerSound); Assert.IsNotNull(controller.calmSound);
        controller.Initialize();
    }
    [TearDown] public void Teardown() { Object.DestroyImmediate(root); }
    [UnityTest] public IEnumerator ConsentAndGradualTransition()
    {
        Assert.IsFalse(controller.ProposePreset("unknown", "bad"));
        Assert.IsTrue(controller.ProposePreset("sadness", "turn1"));
        Assert.AreEqual(2, lamp.intensity, "Proposal must not execute before consent.");
        Assert.IsFalse(controller.ConfirmProposal("stale"));
        Assert.IsTrue(controller.ConfirmProposal("turn1"));
        yield return null;
        Assert.That(lamp.intensity, Is.GreaterThan(1.44f), "No instant dimming.");
        yield return new WaitForSecondsRealtime(2.7f);
        Assert.That(lamp.intensity, Is.InRange(1.44f * .95f, 1.44f * 1.05f), "Settled sadness brightness left the breathing band.");
        Assert.IsFalse(controller.IsTransitioning);
        var state = JsonUtility.FromJson<AmbienceController.State>(controller.GetStateJson());
        Assert.AreEqual("sadness", state.completedPreset); Assert.AreEqual("llm-confirmed", state.source);
        Assert.That(state.lights[0].intensity, Is.EqualTo(lamp.intensity));
    }
    [UnityTest] public IEnumerator RapidSwitchRetainsAudioAndFinishesLatestTarget()
    {
        controller.ApplyManual("sadness"); yield return new WaitForSecondsRealtime(.5f);
        var voices = controller.GetComponents<AudioSource>(); Assert.AreEqual(4, voices.Length);
        float before = voices[0].volume;
        controller.ApplyManual("anger"); controller.ApplyManual("calm");
        Assert.AreEqual(before, voices[0].volume, "Switching may not truncate a playing source.");
        yield return new WaitForSecondsRealtime(2.7f);
        Assert.AreEqual("calm", controller.CurrentPreset);
        Assert.That(lamp.intensity, Is.EqualTo(2).Within(.001f));
        Assert.That(voices[0].volume, Is.EqualTo(0)); Assert.That(voices[1].volume, Is.EqualTo(0));
        Assert.That(voices[2].volume, Is.EqualTo(0)); Assert.That(voices[3].volume, Is.EqualTo(.12f).Within(.001f));
    }
    [Test] public void PreferencesAndDisableStayConsistent()
    {
        controller.SetBrightness(1.15f); controller.SetMuted(true); controller.SetSoundLevel(.4f);
        controller.ApplyManual("anger");
        Assert.AreEqual(1.15f, controller.Brightness); Assert.IsTrue(controller.Muted);
        controller.ProposePreset("calm", "obsolete"); controller.enabled = false;
        var state = JsonUtility.FromJson<AmbienceController.State>(controller.GetStateJson());
        Assert.AreEqual(0, state.effectiveVolume); Assert.AreEqual(0, state.volume);
        Assert.AreEqual("baseline", state.completedPreset); Assert.AreEqual("", state.pendingRequestId);
        Assert.AreEqual(2, lamp.intensity); Assert.AreEqual(Color.white, lamp.color);
        Assert.IsNull(AmbienceController.Instance);
        controller.enabled = true; Assert.AreSame(controller, AmbienceController.Instance);
    }
    [Test] public void CommandBridgeIsProposalOnlyAndRejectsInvalidCommands()
    {
        var bridge = root.AddComponent<AmbienceCommandBridge>(); bridge.Bind(controller);
        Assert.IsFalse(bridge.TryProposeJson("not json", "x"));
        Assert.IsFalse(bridge.TryProposeJson("{\"action\":\"execute_shell\",\"preset\":\"anger\"}", "x"));
        Assert.IsFalse(bridge.TryProposeJson("{\"action\":\"propose_ambience\",\"preset\":\"red_strobe\"}", "x"));
        Assert.IsTrue(bridge.TryProposeJson("{\"action\":\"propose_ambience\",\"preset\":\"anger\"}", "fixture-only"));
        Assert.AreEqual(2, lamp.intensity); Assert.AreEqual("baseline", controller.CurrentPreset);
        controller.RejectProposal(); Assert.AreEqual("", controller.PendingPreset);
    }
    [UnityTest] public IEnumerator DesktopPanelButtonsOperateTheController()
    {
        var panel = root.AddComponent<AmbiencePanel>(); panel.Bind(controller); yield return null;
        var buttons = root.GetComponentsInChildren<Button>(true);
        Button calm = null, accept = null;
        foreach (var button in buttons)
        {
            if (button.name == "Calm / gentle air") calm = button;
            if (button.name == "Accept AI proposal") accept = button;
        }
        Assert.IsNotNull(calm); Assert.IsNotNull(accept); Assert.IsFalse(accept.interactable);
        var events = new GameObject("Test pointer events").AddComponent<EventSystem>(); events.transform.SetParent(root.transform);
        Canvas.ForceUpdateCanvases(); yield return null;
        var pointer = new PointerEventData(events) { position = RectTransformUtility.WorldToScreenPoint(null, calm.transform.TransformPoint(calm.GetComponent<RectTransform>().rect.center)) };
        var hits = new List<RaycastResult>(); events.RaycastAll(pointer, hits);
        Assert.IsNotEmpty(hits, "Desktop buttons must receive screen-space raycasts.");
        Assert.AreSame(calm, hits[0].gameObject.GetComponentInParent<Button>(), "Another UI element blocked the desktop button.");
        ExecuteEvents.Execute(calm.gameObject, pointer, ExecuteEvents.pointerClickHandler);
        Assert.AreEqual("calm", controller.CurrentPreset);
        controller.ProposePreset("anger", "fixture-panel"); yield return new WaitForSecondsRealtime(.2f);
        Assert.IsTrue(accept.interactable); ExecuteEvents.Execute(accept.gameObject, pointer, ExecuteEvents.pointerClickHandler);
        Assert.AreEqual("anger", controller.CurrentPreset);
    }
    [Test] public void EmotionDecisionDebouncesAndNeverRepeatsTheCurrentPreset()
    {
        var decision = new EmotionDecision();
        Assert.IsNull(decision.Consume("baseline", "baseline"), "Neutral turns must never propose.");
        Assert.IsNull(decision.Consume("bored", "baseline"), "Unknown labels must never propose.");
        Assert.IsNull(decision.Consume("sadness", "baseline"), "A single turn is not enough.");
        Assert.IsNull(decision.Consume("anger", "baseline"), "Switching emotions resets the streak.");
        Assert.IsNull(decision.Consume("anger", "anger"), "The active preset is never re-proposed.");
        Assert.IsNull(decision.Consume("anger", "baseline"), "The streak restarts after matching the active preset.");
        Assert.AreEqual("anger", decision.Consume("anger", "baseline"), "Two consecutive turns should propose.");
        Assert.IsNull(decision.Consume("sadness", "baseline"), "The streak restarts after a proposal.");
        Assert.AreEqual("sadness", decision.Consume("sadness", "baseline"), "A renewed two-turn streak proposes again.");
        Assert.AreEqual("anger", decision.Consume("anger", "sadness"), "A switch away from an active preset is proposed at once.");
        Assert.IsNull(decision.Consume("anger", "anger"), "The active preset is never re-proposed.");
        Assert.AreEqual("calm", decision.Consume("calm", "anger"), "Recovery to calm is also proposed at once.");
    }
    [UnityTest] public IEnumerator SadnessBreathesWhileAngerStaysPerfectlyStill()
    {
        controller.ApplyManual("sadness"); yield return new WaitForSecondsRealtime(2.7f);
        Assert.That(lamp.intensity, Is.InRange(1.44f * .95f, 1.44f * 1.05f), "Breathing left the sadness band.");
        float earlier = lamp.intensity;
        yield return new WaitForSecondsRealtime(2f);
        Assert.That(Mathf.Abs(lamp.intensity - earlier), Is.GreaterThan(.02f), "Sadness lighting must keep breathing.");
        Assert.That(lamp.intensity, Is.InRange(1.44f * .95f, 1.44f * 1.05f));
        controller.ApplyManual("anger"); yield return new WaitForSecondsRealtime(2.7f);
        float steady = lamp.intensity;
        yield return new WaitForSecondsRealtime(2f);
        Assert.AreEqual(steady, lamp.intensity, "Anger lighting must be perfectly stable.");
        Assert.That(lamp.intensity, Is.EqualTo(1.24f).Within(.001f));
    }
    [UnityTest] public IEnumerator SadnessCrossfadesFromEmpathyMusicToNatureSounds()
    {
        if (controller.sadnessIntroSound == null) Assert.Ignore("SadPiano clip not imported yet; two-phase check skipped.");
        controller.sadnessIntroSeconds = .5f;
        controller.ApplyManual("sadness");
        yield return new WaitForSecondsRealtime(2.7f);
        var voices = controller.GetComponents<AudioSource>();
        Assert.AreEqual(4, voices.Length);
        Assert.That(voices[0].volume, Is.EqualTo(.12f).Within(.001f), "The empathy clip should be audible first.");
        Assert.That(voices[1].volume, Is.EqualTo(0), "The nature clip must wait for the second phase.");
        yield return new WaitForSecondsRealtime(3.8f);
        Assert.That(voices[0].volume, Is.EqualTo(0), "The empathy clip must fade out.");
        Assert.IsFalse(voices[0].isPlaying);
        Assert.That(voices[1].volume, Is.EqualTo(.12f).Within(.001f), "The nature clip must take over.");
        var state = JsonUtility.FromJson<AmbienceController.State>(controller.GetStateJson());
        Assert.AreEqual(1, state.sadnessPhase);
        controller.SetBrightness(controller.Brightness - .1f); yield return new WaitForSecondsRealtime(2.7f);
        Assert.That(voices[1].volume, Is.EqualTo(.12f).Within(.001f), "Brightness feedback must not restart the intro phase.");
        Assert.That(voices[0].volume, Is.EqualTo(0));
    }

#if UNITY_EDITOR
    [UnityTest] public IEnumerator BasicSceneRunsBothEnvironmentScenarios()
    {
        Object.DestroyImmediate(root); root = null;
        var load = UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(
            "Assets/Scenes/BasicScene.unity", new LoadSceneParameters(LoadSceneMode.Additive));
        while (!load.isDone) yield return null;
        yield return null;
        controller = AmbienceController.Instance;
        Assert.IsNotNull(controller, "BasicScene must auto-install the environment module.");
        Assert.That(controller.lights.Length, Is.GreaterThan(1), "Expected room lamp(s) and scene light.");
        var bridge = controller.GetComponent<AmbienceCommandBridge>(); Assert.IsNotNull(bridge);
        Assert.IsNotNull(controller.GetComponent<AmbiencePanel>());
        var previewObject = new GameObject("Environment verification camera");
        SceneManager.MoveGameObjectToScene(previewObject, SceneManager.GetSceneByName("BasicScene"));
        var camera = previewObject.AddComponent<Camera>(); camera.fieldOfView = 70;
        camera.transform.position = new Vector3(0, 1.6f, -2.5f);
        camera.transform.LookAt(new Vector3(0, 1.3f, 1.5f));
        string report = Path.GetFullPath(Path.Combine(Application.dataPath, "../Library/MindScapeEnvironment"));
        Directory.CreateDirectory(report);
        File.WriteAllText(Path.Combine(report, "room-baseline.json"), controller.GetStateJson());
        Capture(camera, Path.Combine(report, "room-baseline.png"));
        Assert.IsTrue(bridge.TryProposeJson("{\"action\":\"propose_ambience\",\"preset\":\"sadness\"}", "fixture-room-sadness"));
        controller.ConfirmProposal(); yield return new WaitForSecondsRealtime(2.7f);
        File.WriteAllText(Path.Combine(report, "room-sadness.json"), controller.GetStateJson());
        Capture(camera, Path.Combine(report, "room-sadness.png"));
        controller.SetMuted(true);
        Assert.IsTrue(bridge.TryProposeJson("{\"action\":\"propose_ambience\",\"preset\":\"anger\"}", "fixture-room-anger"));
        controller.ConfirmProposal(); yield return new WaitForSecondsRealtime(2.7f);
        var state = JsonUtility.FromJson<AmbienceController.State>(controller.GetStateJson());
        Assert.AreEqual("anger", state.completedPreset); Assert.IsTrue(state.muted); Assert.AreEqual(0, state.effectiveVolume);
        File.WriteAllText(Path.Combine(report, "room-anger.json"), controller.GetStateJson());
        Capture(camera, Path.Combine(report, "room-anger.png"));
        controller.RestoreBaseline(); yield return new WaitForSecondsRealtime(2.7f);
        var initial = JsonUtility.FromJson<AmbienceController.State>(File.ReadAllText(Path.Combine(report, "room-baseline.json")));
        var restored = JsonUtility.FromJson<AmbienceController.State>(controller.GetStateJson());
        for (int i = 0; i < initial.lights.Length; i++)
            Assert.That(restored.lights[i].intensity, Is.EqualTo(initial.lights[i].intensity).Within(.001f));
        File.WriteAllText(Path.Combine(report, "room-restored.json"), controller.GetStateJson());
        var unload = SceneManager.UnloadSceneAsync("BasicScene"); while (!unload.isDone) yield return null;
    }
    static void Capture(Camera camera, string destination)
    {
        var target = RenderTexture.GetTemporary(1280, 720, 24);
        var previous = RenderTexture.active;
        var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        try
        {
            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
            RenderPipeline.SubmitRenderRequest(camera, request);
            RenderTexture.active = target; image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); image.Apply();
            File.WriteAllBytes(destination, image.EncodeToPNG());
        }
        finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target); Object.DestroyImmediate(image); }
    }
#endif
}
