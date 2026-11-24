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
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
    static readonly Color FurnitureBaseline = new Color(.45f, .28f, .16f);
    static readonly Color WallBaseline = new Color(.82f, .78f, .72f);
    static readonly Color FloorBaseline = new Color(.5f, .36f, .24f);
    static readonly Color LampBaseline = new Color(1f, .92f, .76f);
    static readonly Color LampBaselineEmission = new Color(1.14f, .82f, .52f);
    static readonly Color PillowBaseline = new Color(.84f, .53f, .36f);
    static readonly Color FrameBaseline = new Color(.99f, .73f, .12f);
    static readonly Color ScreenBaseline = new Color(.88f, .92f, .88f);
    static readonly Color CabinetGlowBaseline = new Color(1.6f, 1.16f, .73f);
    GameObject root;
    AmbienceController controller;
    Light lamp, coldLamp;
    GameObject furniturePiece, wallPiece, floorPiece, lampPiece, pillowPiece, screenPiece, framePiece, glowPiece;
    GameObject plantOriginal, plantTulip;
    Material furnitureMaterial, wallMaterial, floorMaterial, lampMaterial, pillowMaterial, screenMaterial, frameMaterial, glowMaterial;
    [SetUp] public void Setup()
    {
        Assert.IsNull(AmbienceController.Instance, "Run tests in the Test Runner's isolated scene.");
        root = new GameObject("Environment test");
        lamp = new GameObject("Test lamp").AddComponent<Light>(); lamp.transform.SetParent(root.transform);
        lamp.intensity = 2; lamp.color = Color.white;
        furniturePiece = CreateTintedCube("Test furniture", FurnitureBaseline, out furnitureMaterial);
        wallPiece = CreateTintedCube("Test wall", WallBaseline, out wallMaterial);
        floorPiece = CreateTintedCube("Test floor", FloorBaseline, out floorMaterial);
        lampPiece = CreateTintedCube("Test lamp shade", LampBaseline, out lampMaterial);
        lampMaterial.EnableKeyword("_EMISSION");
        lampMaterial.SetColor("_EmissionColor", LampBaselineEmission);
        pillowPiece = CreateTintedCube("Test pillow", PillowBaseline, out pillowMaterial);
        screenPiece = CreateTintedCube("Test screen", ScreenBaseline, out screenMaterial);
        framePiece = CreateTintedCube("Test frame", FrameBaseline, out frameMaterial);
        glowPiece = CreateTintedCube("Test cabinet glow", CabinetGlowBaseline, out glowMaterial);
        plantOriginal = GameObject.CreatePrimitive(PrimitiveType.Cube);
        plantOriginal.name = "Test plant"; plantOriginal.transform.SetParent(root.transform);
        plantTulip = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        plantTulip.name = "Test tulip"; plantTulip.transform.SetParent(root.transform);
        plantTulip.transform.localScale = Vector3.one * 1.1f; plantTulip.SetActive(false);
        coldLamp = new GameObject("Test window lamp").AddComponent<Light>();
        coldLamp.transform.SetParent(root.transform);
        coldLamp.intensity = 2; coldLamp.color = Color.white;
        controller = root.AddComponent<AmbienceController>();
        controller.lights = new[] { lamp, coldLamp };
        controller.coldWhiteLights = new[] { coldLamp };
        controller.furniture = new[] { furniturePiece.GetComponent<Renderer>() };
        controller.walls = new[] { wallPiece.GetComponent<Renderer>() };
        controller.floors = new[] { floorPiece.GetComponent<Renderer>() };
        controller.lamps = new[] { lampPiece.GetComponent<Renderer>() };
        controller.pillows = new[] { pillowPiece.GetComponent<Renderer>() };
        controller.screens = new[] { screenPiece.GetComponent<Renderer>() };
        controller.screenFrames = new[] { framePiece.GetComponent<Renderer>() };
        controller.cabinetGlows = new[] { glowPiece.GetComponent<Renderer>() };
        controller.plants = new[] { plantOriginal };
        controller.plantReplacements = new[] { plantTulip };
        controller.sadnessIntroSound = Resources.Load<AudioClip>("MindScapeAmbience/SadPiano");
        controller.sadnessSound = Resources.Load<AudioClip>("MindScapeAmbience/SoftRain");
        controller.angerSound = Resources.Load<AudioClip>("MindScapeAmbience/QuietStream");
        controller.calmSound = Resources.Load<AudioClip>("MindScapeAmbience/GentleAir");
        Assert.IsNotNull(controller.sadnessSound); Assert.IsNotNull(controller.angerSound); Assert.IsNotNull(controller.calmSound);
        controller.Initialize();
    }
    GameObject CreateTintedCube(string name, Color baseline, out Material material)
    {
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = name; cube.transform.SetParent(root.transform);
        material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        material.SetColor("_BaseColor", baseline);
        cube.GetComponent<Renderer>().sharedMaterial = material;
        return cube;
    }
    [TearDown] public void Teardown()
    {
        Object.DestroyImmediate(root);
        if (furnitureMaterial != null) Object.DestroyImmediate(furnitureMaterial);
        if (wallMaterial != null) Object.DestroyImmediate(wallMaterial);
        if (floorMaterial != null) Object.DestroyImmediate(floorMaterial);
        if (lampMaterial != null) Object.DestroyImmediate(lampMaterial);
        if (pillowMaterial != null) Object.DestroyImmediate(pillowMaterial);
        if (screenMaterial != null) Object.DestroyImmediate(screenMaterial);
        if (frameMaterial != null) Object.DestroyImmediate(frameMaterial);
        if (glowMaterial != null) Object.DestroyImmediate(glowMaterial);
    }
    [UnityTest] public IEnumerator ConsentAndGradualTransition()
    {
        Assert.IsFalse(controller.ProposePreset("unknown", "bad"));
        Assert.IsTrue(controller.ProposePreset("sadness", "turn1"));
        Assert.AreEqual(2, lamp.intensity, "Proposal must not execute before consent.");
        Assert.IsFalse(controller.ConfirmProposal("stale"));
        Assert.IsTrue(controller.ConfirmProposal("turn1"));
        yield return null;
        Assert.That(lamp.intensity, Is.GreaterThan(1.2f), "No instant dimming.");
        yield return new WaitForSecondsRealtime(2.7f);
        Assert.That(lamp.intensity, Is.InRange(1.2f * .95f, 1.2f * 1.05f), "Settled sadness brightness left the breathing band.");
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
        var desktop = root.transform.Find("Ambience Desktop Controls");
        var sheet = desktop.Find("Control sheet").gameObject;
        Assert.IsFalse(sheet.activeSelf, "The opening view must be clear of the settings panel.");
        desktop.Find("Ambience").GetComponent<Button>().onClick.Invoke();
        Assert.IsTrue(sheet.activeSelf);
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
    [UnityTest] public IEnumerator FurnitureAndSurfacesTintWithPresetAndRestoreOnBaseline()
    {
        var furniture = furniturePiece.GetComponent<Renderer>();
        var wall = wallPiece.GetComponent<Renderer>();
        var floor = floorPiece.GetComponent<Renderer>();
        var lampFixture = lampPiece.GetComponent<Renderer>();
        var pillow = pillowPiece.GetComponent<Renderer>();
        Color furnitureBaseline = furniture.sharedMaterial.GetColor(BaseColorId);
        Color wallBaseline = wall.sharedMaterial.GetColor(BaseColorId);
        Color floorBaseline = floor.sharedMaterial.GetColor(BaseColorId);
        ColorUtility.TryParseHtmlString("#BFD4F2", out var blueWhite);
        ColorUtility.TryParseHtmlString("#D9D9D6", out var lightGray);
        ColorUtility.TryParseHtmlString("#2B3A5C", out var navy);
        var block = new MaterialPropertyBlock();
        controller.ApplyManual("sadness"); yield return new WaitForSecondsRealtime(2.7f);
        AssertTint(furniture, block, (Color)blueWhite.linear, "Sadness must settle furniture on the blue-white tint.");
        AssertTint(wall, block, AmbienceController.WallSadnessTint, "Sadness must drive walls toward pure white.");
        AssertTint(floor, block, (Color)navy.linear, "Sadness must settle floors on navy.");
        AssertTint(lampFixture, block, AmbienceController.LampSadnessTint, "Sadness must turn the pendant lamp light blue.");
        AssertEmissionTint(lampFixture, block, AmbienceController.EmissionTint(AmbienceController.LampSadnessTint, LampBaselineEmission),
            "Sadness must glow the lamp glass in light blue at its baseline level.");
        AssertTint(pillow, block, AmbienceController.PillowSadnessTint, "Sadness must settle throw pillows on white.");
        AssertTint(screenPiece.GetComponent<Renderer>(), block, AmbienceController.ScreenSadnessTint, "Sadness must turn the conversation screen white.");
        AssertTint(framePiece.GetComponent<Renderer>(), block, AmbienceController.FrameSadnessTint, "Sadness must turn the screen frame light blue.");
        AssertTint(glowPiece.GetComponent<Renderer>(), block, CabinetGlowBaseline, "Sadness must keep the cabinet glow warm.");
        controller.ApplyManual("anger"); yield return new WaitForSecondsRealtime(2.7f);
        AssertTint(furniture, block, (Color)lightGray.linear, "Anger must settle furniture on the light-gray tint.");
        AssertTint(wall, block, AmbienceController.WallAngerTint, "Anger must settle walls on light gray.");
        AssertTint(floor, block, AmbienceController.FloorAngerTint, "Anger must settle floors on dark gray.");
        AssertTint(lampFixture, block, AmbienceController.LampAngerTint, "Anger must turn the pendant lamp navy.");
        AssertEmissionTint(lampFixture, block, AmbienceController.EmissionTint(AmbienceController.LampAngerTint, LampBaselineEmission),
            "Anger must glow the lamp glass in navy at its baseline level.");
        AssertTint(pillow, block, AmbienceController.PillowAngerTint, "Anger must settle throw pillows on light gray.");
        AssertTint(screenPiece.GetComponent<Renderer>(), block, AmbienceController.ScreenAngerTint, "Anger must turn the conversation screen light gray.");
        AssertTint(framePiece.GetComponent<Renderer>(), block, AmbienceController.FrameAngerTint, "Anger must turn the screen frame dark blue.");
        AssertTint(glowPiece.GetComponent<Renderer>(), block, AmbienceController.CabinetGlowAngerTint, "Anger must cool the cabinet glow to cold white.");
        var state = JsonUtility.FromJson<AmbienceController.State>(controller.GetStateJson());
        Assert.AreEqual(6500f, state.kelvin, 1f, "Anger light must run cold white.");
        controller.RestoreBaseline(); yield return new WaitForSecondsRealtime(2.7f);
        AssertTint(furniture, block, furnitureBaseline, "Baseline recovery must return furniture to its original colors.");
        AssertTint(wall, block, wallBaseline, "Baseline recovery must return walls to their original colors.");
        AssertTint(floor, block, floorBaseline, "Baseline recovery must return floors to their original colors.");
        AssertTint(lampFixture, block, LampBaseline, "Baseline recovery must return the pendant lamp to its original color.");
        AssertEmissionTint(lampFixture, block, LampBaselineEmission, "Baseline recovery must return the lamp glow to warm.");
        AssertTint(pillow, block, PillowBaseline, "Baseline recovery must return throw pillows to their original color.");
        AssertTint(screenPiece.GetComponent<Renderer>(), block, ScreenBaseline, "Baseline recovery must return the screen to its original color.");
        AssertTint(framePiece.GetComponent<Renderer>(), block, FrameBaseline, "Baseline recovery must return the frame to its original color.");
        AssertTint(glowPiece.GetComponent<Renderer>(), block, CabinetGlowBaseline, "Baseline recovery must return the cabinet glow to warm.");
    }
    static void AssertTint(Renderer renderer, MaterialPropertyBlock block, Color expected, string message)
    {
        renderer.GetPropertyBlock(block, 0);
        AssertColorNear(expected, block.GetColor(BaseColorId), message);
    }
    static void AssertEmissionTint(Renderer renderer, MaterialPropertyBlock block, Color expected, string message)
    {
        renderer.GetPropertyBlock(block, 0);
        AssertColorNear(expected, block.GetColor(EmissionColorId), message);
    }
    // NUnit's Within tolerance does not apply to the Color struct, so compare channels.
    static void AssertColorNear(Color expected, Color actual, string message)
    {
        Assert.That(Mathf.Abs(expected.r - actual.r), Is.LessThan(.01f), message);
        Assert.That(Mathf.Abs(expected.g - actual.g), Is.LessThan(.01f), message);
        Assert.That(Mathf.Abs(expected.b - actual.b), Is.LessThan(.01f), message);
    }
    [UnityTest] public IEnumerator SadnessSwapsPottedPlantsForTulipsAndRestoresOnRecovery()
    {
        Assert.IsTrue(plantOriginal.activeSelf);
        Assert.IsFalse(plantTulip.activeSelf, "Tulips stay hidden at baseline.");
        controller.ApplyManual("sadness"); yield return new WaitForSecondsRealtime(.4f);
        Assert.IsTrue(plantOriginal.activeSelf && plantTulip.activeSelf, "Both sides must be visible while cross-growing.");
        Assert.That(plantTulip.transform.localScale.x, Is.GreaterThan(0f).And.LessThan(1.1f), "Tulips must grow in gradually.");
        Assert.That(plantOriginal.transform.localScale.x, Is.GreaterThan(0f).And.LessThan(1f), "Originals must shrink gradually.");
        yield return new WaitForSecondsRealtime(2.4f);
        Assert.IsFalse(plantOriginal.activeSelf, "Sadness must hide the original plants.");
        Assert.IsTrue(plantTulip.activeSelf, "Sadness must reveal the tulips.");
        Assert.That(plantTulip.transform.localScale.x, Is.EqualTo(1.1f).Within(.001f), "Tulips must settle at full size.");
        controller.ApplyManual("anger"); yield return new WaitForSecondsRealtime(2.7f);
        Assert.IsTrue(plantOriginal.activeSelf, "Non-sadness presets must bring the plants back.");
        Assert.IsFalse(plantTulip.activeSelf, "Non-sadness presets must hide the tulips.");
        Assert.That(plantOriginal.transform.localScale.x, Is.EqualTo(1f).Within(.001f));
    }
    [UnityTest] public IEnumerator WindowLampRunsColdWhiteInSadnessAndAnger()
    {
        Color warmWhite = KelvinColor(3300), coldWhite = KelvinColor(AmbienceController.ColdWhiteKelvin);
        controller.ApplyManual("sadness"); yield return new WaitForSecondsRealtime(2.7f);
        AssertColorNear(coldWhite, coldLamp.color, "Sadness must keep the window lamp cold white.");
        AssertColorNear(warmWhite, lamp.color, "Sadness must keep the room lights warm white.");
        controller.ApplyManual("anger"); yield return new WaitForSecondsRealtime(2.7f);
        AssertColorNear(coldWhite, coldLamp.color, "Anger must keep the window lamp cold white.");
        AssertColorNear(coldWhite, lamp.color, "Anger must run the room lights cold white.");
        controller.RestoreBaseline(); yield return new WaitForSecondsRealtime(2.7f);
        AssertColorNear(Color.white, coldLamp.color, "Baseline recovery must return the window lamp to its original color.");
        AssertColorNear(Color.white, lamp.color, "Baseline recovery must return the room lights to their original color.");
    }
    static Color KelvinColor(float kelvin) => Mathf.CorrelatedColorTemperatureToRGB(kelvin).gamma;
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
        Assert.That(lamp.intensity, Is.InRange(1.2f * .95f, 1.2f * 1.05f), "Breathing left the sadness band.");
        float earlier = lamp.intensity;
        yield return new WaitForSecondsRealtime(2f);
        Assert.That(Mathf.Abs(lamp.intensity - earlier), Is.GreaterThan(.02f), "Sadness lighting must keep breathing.");
        Assert.That(lamp.intensity, Is.InRange(1.2f * .95f, 1.2f * 1.05f));
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
        Assert.That(controller.walls.Length, Is.GreaterThan(5), "Expected shell walls, ceiling and window plaster.");
        Assert.That(controller.floors.Length, Is.GreaterThan(0), "Expected the room floor.");
        Assert.That(controller.furniture.Length, Is.GreaterThan(5), "Expected sofa, armchair and case goods.");
        Assert.That(controller.lamps.Length, Is.GreaterThan(2), "Expected the pendant fixture renderers.");
        Assert.That(controller.pillows.Length, Is.EqualTo(3), "Expected the three square linen cushions.");
        Assert.That(controller.plants.Length, Is.EqualTo(1), "Expected only the mirror-side corner foliage to swap.");
        Assert.That(controller.plantReplacements.Length, Is.EqualTo(1), "Expected one tulip for the corner foliage.");
        Assert.That(controller.coldWhiteLights.Length, Is.EqualTo(1), "Expected the window-side floor lamp.");
        Assert.That(controller.screens.Length, Is.EqualTo(1), "Expected the conversation screen display.");
        Assert.That(controller.screenFrames.Length, Is.EqualTo(1), "Expected the conversation screen frame.");
        Assert.That(controller.cabinetGlows.Length, Is.EqualTo(16), "Expected the warm diffusers across all cabinets.");
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
