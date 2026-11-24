using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;
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
    Vector3 spawnPosition, spawnEyeWorldPosition, spawnEyeLocalPosition;
    Quaternion spawnRotation, spawnEyeRotation;
    Color savedSky;
    CameraClearFlags savedClear;
    Vector2 limit;
    Font font;
    const float WalkSpeed = 5f;
    const float TurnSpeed = 75f;
    const float StickDeadZone = .16f;
    const float BodyRadius = .23f;
    const float ContactGap = .002f;
    Vector3 safeEye;
    bool exitHeld, desktop;
    static readonly Rect ExitRect = new Rect(10, 10, 90, 33);

    // Mouse mode sets its opening camera pose in Start. Capture it once after initialization.
    void LateUpdate()
    {
        if (origin == null) CaptureStartPose();
        if (active == this) ConstrainHead();
    }

    void CaptureStartPose()
    {
        eye = Camera.main;
        if (eye == null) return;
        rig = eye.GetComponentInParent<XROrigin>();
        if (rig == null) return;
        origin = rig.Origin.transform;
        spawnPosition = origin.position;
        spawnRotation = origin.rotation;
        spawnEyeWorldPosition = eye.transform.position;
        spawnEyeLocalPosition = eye.transform.localPosition;
        spawnEyeRotation = eye.transform.rotation;
    }

    public void Enter(BoxCollider sand, Font roomFont)
    {
        if (IsTouring || sand == null) return;
        if (origin == null) CaptureStartPose();
        if (origin == null) return;
        RoomSeat.StandIfOccupied(rig);
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
        exitHeld = true;
        if (!desktop) CreateExitButton(sand.GetComponent<MeshRenderer>().sharedMaterial);
        safeEye = eye.transform.position;
        if (IsBlocked(safeEye))
        {
            // An oversized piece near the rim may cover the usual entrance.
            Vector3 nearest = safeEye;
            float best = float.PositiveInfinity;
            for (float x = -limit.x - 4f; x <= limit.x + 4f; x += .5f)
            for (float z = -limit.y - 4f; z <= limit.y + 4f; z += .5f)
            {
                Vector3 candidate = TourCenter + new Vector3(x, TourEyeHeight, z);
                float distance = (candidate - safeEye).sqrMagnitude;
                if (distance < best && !IsBlocked(candidate)) { nearest = candidate; best = distance; }
            }
            if (float.IsPositiveInfinity(best)) { Leave(); return; }
            rig.MoveCameraToWorldLocation(nearest);
            safeEye = eye.transform.position;
        }
        Application.onBeforeRender += ConstrainHead;
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
        obstacles.Add(face.bounds);

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

        left.TryGetFeatureValue(CommonUsages.primary2DAxis, out Vector2 move);
        right.TryGetFeatureValue(CommonUsages.primary2DAxis, out Vector2 turn);
        if (InputController.leftHand is XRSimulatedController simulatedLeft)
        {
            otherBack = simulatedLeft.secondaryButton.isPressed;
            move = simulatedLeft.primary2DAxis.ReadValue();
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
        move = FilterStick(move);
        float yaw = FilterStick(new Vector2(turn.x, 0f)).x;
        if (desktop)
        {
            var pad = Gamepad.current;
            if (pad != null)
            {
                move += FilterStick(pad.leftStick.ReadValue());
                yaw += FilterStick(new Vector2(pad.rightStick.x.ReadValue(), 0f)).x;
            }
            if (keys != null)
            {
                if (keys.wKey.isPressed || keys.upArrowKey.isPressed) move.y += 1;
                if (keys.sKey.isPressed || keys.downArrowKey.isPressed) move.y -= 1;
                if (keys.dKey.isPressed || keys.rightArrowKey.isPressed) move.x += 1;
                if (keys.aKey.isPressed || keys.leftArrowKey.isPressed) move.x -= 1;
            }
            var mouse = Mouse.current;
            if (mouse != null && mouse.rightButton.isPressed)
            {
                Vector2 delta = mouse.delta.ReadValue() * .18f;
                var angles = eye.transform.eulerAngles;
                eye.transform.rotation = Quaternion.Euler(Mathf.Clamp(Mathf.DeltaAngle(0, angles.x) - delta.y, -80, 80), angles.y + delta.x, 0);
            }
        }
        WalkAndTurn(Vector2.ClampMagnitude(move, 1f), Mathf.Clamp(yaw, -1f, 1f), Time.deltaTime);
    }

    static Vector2 FilterStick(Vector2 stick)
    {
        float magnitude = stick.magnitude;
        return magnitude <= StickDeadZone ? Vector2.zero :
            stick / magnitude * Mathf.InverseLerp(StickDeadZone, 1f, magnitude);
    }

    void WalkAndTurn(Vector2 move, float yaw, float deltaTime)
    {
        // Cap a single step after a frame stall so it cannot jump across an obstacle.
        float step = Mathf.Min(deltaTime, .1f);
        if (yaw != 0f) rig.RotateAroundCameraUsingOriginUp(yaw * TurnSpeed * step);
        if (move == Vector2.zero) return;
        Vector3 forward = Vector3.ProjectOnPlane(eye.transform.forward, Vector3.up);
        if (forward.sqrMagnitude < .0001f)
            forward = Vector3.Cross(eye.transform.right, Vector3.up);
        forward.Normalize();
        Vector3 delta = (forward * move.y + Vector3.Cross(Vector3.up, forward) * move.x) * (WalkSpeed * step);
        MoveEyeTo(eye.transform.position + delta);
    }

    // Tracking can move the camera after Update, even when neither stick is touched.
    [BeforeRenderOrder(10000)]
    void ConstrainHead()
    {
        if (active == this) MoveEyeTo(eye.transform.position);
    }

    bool IsBlocked(Vector3 point)
    {
        foreach (Bounds box in obstacles)
            if (point.x >= box.min.x - BodyRadius && point.x <= box.max.x + BodyRadius &&
                point.z >= box.min.z - BodyRadius && point.z <= box.max.z + BodyRadius) return true;
        return false;
    }

    void MoveEyeTo(Vector3 target)
    {
        Vector3 current = eye.transform.position;
        target.x = Mathf.Clamp(target.x, TourCenter.x - limit.x - 5f + BodyRadius, TourCenter.x + limit.x + 5f - BodyRadius);
        target.z = Mathf.Clamp(target.z, TourCenter.z - limit.y - 5f + BodyRadius, TourCenter.z + limit.y + 5f - BodyRadius);
        Vector3 delta = new Vector3(target.x - safeEye.x, 0, target.z - safeEye.z);
        // One contact can remove each horizontal movement component. Sweep the remaining tangent again.
        for (int pass = 0; pass < 2 && delta.sqrMagnitude > 0f; pass++)
        {
            float first = 1f;
            Vector3 normal = Vector3.zero;
            foreach (Bounds box in obstacles)
            {
                float enter = 0f, leave = 1f;
                Vector3 side = Vector3.zero;
                if (!ClipAxis(safeEye.x, delta.x, box.min.x - BodyRadius, box.max.x + BodyRadius,
                        Vector3.right, ref enter, ref leave, ref side) ||
                    !ClipAxis(safeEye.z, delta.z, box.min.z - BodyRadius, box.max.z + BodyRadius,
                        Vector3.forward, ref enter, ref leave, ref side)) continue;
                if (side != Vector3.zero && enter <= first) { first = enter; normal = side; }
            }
            safeEye += delta * first;
            if (normal == Vector3.zero) break;
            safeEye += normal * ContactGap;
            delta = Vector3.ProjectOnPlane(delta * (1f - first), normal);
        }
        origin.position += new Vector3(safeEye.x - current.x, 0, safeEye.z - current.z);
    }

    // Intersect the whole motion segment with the inflated footprint, including thin and diagonal crossings.
    internal static bool ClipAxis(float start, float motion, float low, float high, Vector3 axis,
        ref float enter, ref float leave, ref Vector3 normal)
    {
        if (motion == 0f) return start > low && start < high;
        float near = ((motion > 0f ? low : high) - start) / motion;
        float far = ((motion > 0f ? high : low) - start) / motion;
        if (near >= enter) { enter = near; normal = motion > 0f ? -axis : axis; }
        leave = Mathf.Min(leave, far);
        return enter <= leave && leave > 0f;
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
        const string hint = "WASD / 方向键移动  ·  右键环视  ·  Esc 返回";
        float hintWidth = Mathf.Ceil(tourHint.CalcSize(new GUIContent(hint)).x) + 8;
        if (margin * 2 + hintWidth < Screen.width)
            GUI.Box(new Rect(margin, bottom, hintWidth, height), hint, tourHint);
    }

    void OnDisable() { if (active == this) Leave(); }

    void Leave()
    {
        if (active != this) return;
        Application.onBeforeRender -= ConstrainHead;
        active = null;
        if (origin != null) origin.SetPositionAndRotation(spawnPosition, spawnRotation);
        if (eye != null)
        {
            if (origin != null)
            {
                if (desktop)
                {
                    eye.transform.localPosition = spawnEyeLocalPosition;
                    eye.transform.rotation = spawnEyeRotation;
                }
                else rig.MatchOriginUpCameraForward(origin.up, Vector3.ProjectOnPlane(spawnEyeRotation * Vector3.forward, origin.up));
                // Tracking owns the local head pose. Translate the rig so the current head returns to the opening view.
                Vector3 correction = spawnEyeWorldPosition - eye.transform.position;
                origin.position += desktop ? correction : Vector3.ProjectOnPlane(correction, origin.up);
                Physics.SyncTransforms();
            }
            eye.backgroundColor = savedSky;
            eye.clearFlags = savedClear;
        }
        if (controller != null) controller.enabled = true;
        foreach (var provider in paused) if (provider != null) provider.enabled = true;
        paused.Clear();
        rig.GetComponent<RoomBodyCollision>()?.ResetPose();
        obstacles.Clear();
        Destroy(landscape);
        foreach (var item in generated) Destroy(item);
        generated.Clear();
    }
}
