using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;
using InputController = UnityEngine.InputSystem.XR.XRController;
using CommonUsages = UnityEngine.XR.CommonUsages;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;

public class SandTrayTour : MonoBehaviour
{
    // A 9 cm model door becomes roughly human-sized.
    const float Scale = 22f;
    const float TourEyeHeight = 3.4f;
    static readonly Vector3 TourCenter = new Vector3(0, 0, 3000);
    static SandTrayTour active;
    public static bool IsTouring => active != null;

    readonly List<Bounds> obstacles = new List<Bounds>();
    readonly List<LocomotionProvider> paused = new List<LocomotionProvider>();
    readonly List<Object> generated = new List<Object>();
    GameObject landscape;
    XROrigin rig;
    Camera eye;
    Transform origin;
    CharacterController controller;
    Vector3 savedPosition, savedEyePosition;
    Quaternion savedRotation, savedEyeRotation;
    Color savedSky;
    CameraClearFlags savedClear;
    Vector2 limit;
    Font font;
    bool snapHeld, exitHeld, desktop;
    NearFarInteractor pointer;
    LineRenderer beam, landingRing;
    static readonly Rect ExitRect = new Rect(10, 10, 90, 33);

    public void Enter(BoxCollider sand, Font roomFont)
    {
        if (IsTouring || sand == null) return;
        eye = Camera.main;
        if (eye == null) return;
        rig = eye.GetComponentInParent<XROrigin>();
        if (rig == null) return;
        origin = rig.Origin.transform;
        savedPosition = origin.position;
        savedRotation = origin.rotation;
        savedEyePosition = eye.transform.localPosition;
        savedEyeRotation = eye.transform.localRotation;
        savedSky = eye.backgroundColor;
        savedClear = eye.clearFlags;
        font = roomFont;
        active = this;
        desktop = !UnityEngine.XR.InputDevices.GetDeviceAtXRNode(XRNode.Head).isValid
            && !(InputController.rightHand is XRSimulatedController);
#if UNITY_EDITOR
        if (eye.GetComponent<RoomMouseTest>() != null) desktop = true;
#endif
        landscape = new GameObject("沙盘游览");
        landscape.transform.position = TourCenter;
        var surface = sand.transform;
        Vector3 top = surface.TransformPoint(sand.center + Vector3.up * sand.size.y * .5f);
        Quaternion worldToTray = Quaternion.Inverse(surface.rotation);
        limit = new Vector2(surface.TransformVector(Vector3.right * sand.size.x).magnitude,
            surface.TransformVector(Vector3.forward * sand.size.z).magnitude) * (Scale * .5f);
        BuildGround(sand.GetComponent<MeshRenderer>().sharedMaterial);
        foreach (var piece in FindObjectsOfType<SandPiece>())
        {
            if (!piece.IsOn(sand)) continue;
            var replica = new GameObject(piece.name).transform;
            replica.SetParent(landscape.transform, false);
            Bounds bounds = default;
            bool hasBounds = false;
            foreach (var source in piece.GetComponentsInChildren<MeshFilter>())
            {
                if (!source.TryGetComponent<MeshRenderer>(out var renderer) || !renderer.enabled) continue;
                var model = new GameObject(source.name, typeof(MeshFilter), typeof(MeshRenderer));
                model.transform.SetParent(replica, false);
                model.transform.localPosition = worldToTray * (source.transform.position - top) * Scale;
                model.transform.localRotation = worldToTray * source.transform.rotation;
                model.transform.localScale = source.transform.lossyScale * Scale;
                model.GetComponent<MeshFilter>().sharedMesh = source.sharedMesh;
                var copy = model.GetComponent<MeshRenderer>();
                copy.sharedMaterials = renderer.sharedMaterials;
                if (hasBounds) bounds.Encapsulate(copy.bounds);
                else { bounds = copy.bounds; hasBounds = true; }
            }
            if (hasBounds)
            {
                var foot = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
                const float scale = .85f;
                replica.localScale = Vector3.one * scale;
                replica.position += (foot - replica.position) * (1f - scale);
                bounds.center = foot + (bounds.center - foot) * scale;
                bounds.size *= scale;
                bounds.Expand(.35f);
                obstacles.Add(bounds);
            }
        }

        foreach (var provider in rig.GetComponentsInChildren<LocomotionProvider>())
        {
            if (!provider.enabled) continue;
            paused.Add(provider);
            provider.enabled = false;
        }
        controller = origin.GetComponent<CharacterController>();
        if (controller != null && controller.enabled) controller.enabled = false;
        else controller = null;
        eye.backgroundColor = new Color(.44f, .76f, .86f);
        eye.clearFlags = CameraClearFlags.SolidColor;
        if (desktop) eye.transform.rotation = Quaternion.Euler(8f, 0f, 0f);
        rig.MatchOriginUpCameraForward(Vector3.up, Vector3.forward);
        // Start on the path outside the arrangement, facing into it.
        rig.MoveCameraToWorldLocation(TourCenter + new Vector3(0, TourEyeHeight, -limit.y - 4f));
        exitHeld = snapHeld = true;
        if (!desktop)
        {
            foreach (var hand in rig.GetComponentsInChildren<NearFarInteractor>(true))
            {
                if (hand.handedness == InteractorHandedness.Right) pointer = hand;
            }
            CreateExitButton(sand.GetComponent<MeshRenderer>().sharedMaterial);
        }
        var markerMaterial = Tint(sand.GetComponent<MeshRenderer>().sharedMaterial, new Color(.1f, 1f, .65f));
        beam = MakeLine("瞬移射线", markerMaterial, .012f, 2);
        landingRing = MakeLine("瞬移落点", markerMaterial, .045f, 40);
        landingRing.loop = true;
        landingRing.useWorldSpace = false;
        for (int i = 0; i < landingRing.positionCount; i++)
        {
            float a = i * Mathf.PI * 2 / landingRing.positionCount;
            landingRing.SetPosition(i, new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * .23f);
        }
    }

    void CreateExitButton(Material surface)
    {
        var stand = new GameObject("入口返回路牌").transform;
        stand.SetParent(landscape.transform, false);
        stand.localPosition = new Vector3(-1.25f, 0, -limit.y - 2.5f);
        stand.localRotation = Quaternion.Euler(0, -35f, 0);
        var wood = Tint(surface, new Color(.54f, .35f, .20f));
        var post = SignBoard(stand, new Vector3(0, 1.13f, 0), new Vector3(.11f, 2.26f, .11f), wood);
        obstacles.Add(post.bounds);
        var face = SignBoard(stand, new Vector3(0, 2.30f, 0), new Vector3(.91f, .40f, .065f), wood);
        face.transform.localRotation = Quaternion.Euler(19f, 0, 0);

        var exitPanel = new GameObject("退出游览", typeof(RectTransform), typeof(Canvas),
            typeof(TrackedDeviceGraphicRaycaster), typeof(Image), typeof(Button));
        var canvas = exitPanel.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = eye;
        var rect = (RectTransform)exitPanel.transform;
        rect.SetParent(stand, false);
        rect.localRotation = face.transform.localRotation;
        rect.localPosition = face.transform.localPosition - rect.localRotation * Vector3.forward * .035f;
        rect.localScale = Vector3.one * .00425f;
        rect.sizeDelta = new Vector2(200, 80);
        var background = exitPanel.GetComponent<Image>();
        background.color = new Color(.12f, .19f, .20f, .95f);
        var button = exitPanel.GetComponent<Button>();
        button.targetGraphic = background;
        button.onClick.AddListener(Leave);

        var label = new GameObject("Label", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
        label.rectTransform.SetParent(rect, false);
        label.rectTransform.anchorMin = Vector2.zero;
        label.rectTransform.anchorMax = Vector2.one;
        label.rectTransform.offsetMin = label.rectTransform.offsetMax = Vector2.zero;
        label.font = font;
        label.fontSize = 34;
        label.color = Color.white;
        label.alignment = TextAnchor.MiddleCenter;
        label.raycastTarget = false;
        label.text = "退出游览";
        exitPanel.layer = label.gameObject.layer = 5;
    }

    static Renderer SignBoard(Transform stand, Vector3 position, Vector3 size, Material material)
    {
        var board = GameObject.CreatePrimitive(PrimitiveType.Cube);
        board.transform.SetParent(stand, false);
        board.transform.localPosition = position;
        board.transform.localScale = size;
        var collider = board.GetComponent<Collider>();
        collider.enabled = false;
        Destroy(collider);
        var renderer = board.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        return renderer;
    }

    void BuildGround(Material sandMaterial)
    {
        // Concentric strips share a horizon. Separate triangle vertices give hills flat facets.
        var vertices = new List<Vector3>();
        var strips = new List<int>[] { new List<int>(), new List<int>(), new List<int>(), new List<int>() };
        const int count = 48;
        Vector3[] previous = new Vector3[count];
        for (int ring = 0; ring < 5; ring++)
        {
            var current = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                float angle = i * 2f * Mathf.PI / count;
                var direction = new Vector2(Mathf.Sin(angle), Mathf.Cos(angle));
                float ridge = Mathf.PerlinNoise(3f + direction.x * 4f, 8f + direction.y * 4f);
                float radius = ring == 0 ? Mathf.Max(limit.magnitude + 6f, 28f) :
                    ring == 1 ? 44f + 4f * ridge : ring == 2 ? 60f : ring == 3 ? 81f + ridge * 7f : 105f;
                float y = ring < 2 ? 0 : ring == 2 ? 1f + ridge * 3f : ring == 3 ? 8f + 23f * ridge : 0;
                current[i] = new Vector3(direction.x * radius, y, direction.y * radius);
            }
            for (int i = 0; i < count; i++)
            {
                int j = (i + 1) % count;
                int material = Mathf.Min(ring, 3);
                Triangle(vertices, strips[material], previous[i], current[i], current[j]);
                if (ring > 0) Triangle(vertices, strips[material], previous[i], current[j], previous[j]);
            }
            previous = current;
        }
        var mesh = new Mesh { name = "Sand and foothills", subMeshCount = strips.Length };
        generated.Add(mesh);
        mesh.SetVertices(vertices);
        for (int i = 0; i < strips.Length; i++) mesh.SetTriangles(strips[i], i);
        mesh.RecalculateNormals();
        landscape.AddComponent<MeshFilter>().sharedMesh = mesh;
        var palette = new[] { sandMaterial, Tint(sandMaterial, new Color(.53f,.66f,.37f)),
            Tint(sandMaterial, new Color(.38f,.56f,.34f)), Tint(sandMaterial, new Color(.49f,.56f,.54f)) };
        landscape.AddComponent<MeshRenderer>().sharedMaterials = palette;
    }

    Material Tint(Material source, Color color)
    {
        var material = new Material(source);
        material.color = color;
        generated.Add(material);
        return material;
    }

    static void Triangle(List<Vector3> vertices, List<int> indices, Vector3 a, Vector3 b, Vector3 c)
    {
        int first = vertices.Count;
        vertices.Add(a); vertices.Add(b); vertices.Add(c);
        indices.Add(first); indices.Add(first + 1); indices.Add(first + 2);
    }

    void Update()
    {
        if (active != this) return;
        var left = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
        var right = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        right.TryGetFeatureValue(CommonUsages.secondaryButton, out bool back);
        left.TryGetFeatureValue(CommonUsages.secondaryButton, out bool otherBack);

        right.TryGetFeatureValue(CommonUsages.primary2DAxis, out Vector2 turn);
        if (InputController.leftHand is XRSimulatedController simulatedLeft)
        {
            otherBack = simulatedLeft.secondaryButton.isPressed;

        }
        if (InputController.rightHand is XRSimulatedController simulatedRight)
        {
            back = simulatedRight.secondaryButton.isPressed;
            turn = simulatedRight.primary2DAxis.ReadValue();
        }
        back |= otherBack;
        var keys = Keyboard.current;
        if ((back && !exitHeld) || (keys != null && keys.escapeKey.wasPressedThisFrame))
        {
            Leave();
            return;
        }
        exitHeld = back;
        if (Mathf.Abs(turn.x) > .7f && !snapHeld) rig.RotateAroundCameraUsingOriginUp(Mathf.Sign(turn.x) * 30f);
        snapHeld = Mathf.Abs(turn.x) > .4f;
        if (desktop)
        {
            var mouse = Mouse.current;
            if (mouse != null && mouse.rightButton.isPressed)
            {
                Vector2 delta = mouse.delta.ReadValue() * .18f;
                var angles = eye.transform.eulerAngles;
                eye.transform.rotation = Quaternion.Euler(Mathf.Clamp(Mathf.DeltaAngle(0, angles.x) - delta.y, -80, 80), angles.y + delta.x, 0);
            }
        }
        UpdateTeleport();
    }

    LineRenderer MakeLine(string title, Material material, float width, int count)
    {
        var line = new GameObject(title).AddComponent<LineRenderer>();
        line.transform.SetParent(landscape.transform, false);
        line.sharedMaterial = material;
        line.widthMultiplier = width;
        line.positionCount = count;
        line.enabled = false;
        return line;
    }

    void UpdateTeleport()
    {
        beam.enabled = landingRing.enabled = false;
        Ray ray;
        bool confirm;
        if (desktop)
        {
            var mouse = Mouse.current;
            if (mouse == null || mouse.rightButton.isPressed) return;
            Vector2 pos = mouse.position.ReadValue();
            if (!eye.pixelRect.Contains(pos) || ExitRect.Contains(new Vector2(pos.x, Screen.height - pos.y))) return;
            ray = eye.ScreenPointToRay(pos);
            confirm = mouse.leftButton.wasPressedThisFrame;
        }
        else
        {
            if (pointer == null || !pointer.isActiveAndEnabled || pointer.TryGetCurrentUIRaycastResult(out _)) return;
            Transform hand = pointer.curveOrigin;
            ray = new Ray(hand.position, hand.forward);
            confirm = pointer.uiPressInput.ReadWasPerformedThisFrame();
        }
        if (!TryLanding(ray, out var destination)) return;
        landingRing.transform.position = destination + Vector3.up * .035f;
        landingRing.enabled = true;
        if (!desktop)
        {
            beam.SetPosition(0, ray.origin);
            beam.SetPosition(1, landingRing.transform.position);
            beam.enabled = true;
        }
        if (confirm)
        {
            Vector3 delta = destination - eye.transform.position;
            origin.position += new Vector3(delta.x, 0, delta.z);
        }
    }

    bool TryLanding(Ray ray, out Vector3 point)
    {
        point = default;
        var floor = new Plane(Vector3.up, TourCenter);
        if (ray.direction.y >= 0 || !floor.Raycast(ray, out float distance)) return false;
        point = ray.GetPoint(distance);
        Vector3 local = point - TourCenter;
        if (Mathf.Abs(local.x) + .23f > limit.x + 5 || Mathf.Abs(local.z) + .23f > limit.y + 5) return false;
        foreach (var bounds in obstacles)
        {
            if (bounds.IntersectRay(ray, out float hit) && hit <= distance) return false;
            if (point.x >= bounds.min.x - .23f && point.x <= bounds.max.x + .23f &&
                point.z >= bounds.min.z - .23f && point.z <= bounds.max.z + .23f) return false;
        }
        return true;
    }
    GUIStyle tourButton, tourHint;

    void OnGUI()
    {
        if (active != this || !desktop) return;
        if (tourButton == null)
        {
            tourButton = new GUIStyle(GUI.skin.button) { font = font, fontSize = 16, alignment = TextAnchor.MiddleCenter };
            tourHint = new GUIStyle(GUI.skin.box) { font = font, fontSize = 12, alignment = TextAnchor.MiddleCenter };
        }
        const float margin = 10, height = 26;
        if (GUI.Button(ExitRect, "退出游览", tourButton))
        {
            Leave();
            return;
        }

        float bottom = Screen.height - margin - height;
        const string hint = "右键环视  ·  左键点地面瞬移  ·  Esc 返回";
        float hintWidth = Mathf.Ceil(tourHint.CalcSize(new GUIContent(hint)).x) + 8;
        if (margin * 2 + hintWidth < Screen.width)
            GUI.Box(new Rect(margin, bottom, hintWidth, height), hint, tourHint);
    }

    void OnDisable() { if (active == this) Leave(); }

    void Leave()
    {
        if (active != this) return;
        active = null;
        if (origin != null) origin.SetPositionAndRotation(savedPosition, savedRotation);
        if (eye != null)
        {
            eye.transform.localPosition = savedEyePosition;
            eye.transform.localRotation = savedEyeRotation;
            eye.backgroundColor = savedSky;
            eye.clearFlags = savedClear;
        }
        if (controller != null) controller.enabled = true;
        foreach (var provider in paused) if (provider != null) provider.enabled = true;
        paused.Clear();
        obstacles.Clear();
        pointer = null;
        Destroy(landscape);
        foreach (var item in generated) Destroy(item);
        generated.Clear();
    }
}
