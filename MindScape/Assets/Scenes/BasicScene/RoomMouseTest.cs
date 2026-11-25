#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.InputSystem.XR;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;
using UnityEngine.XR.Interaction.Toolkit.UI;

public class RoomMouseTest : MonoBehaviour
{
    const string MenuPath = "MindScape/鼠标测试模式";
    const string Preference = "MindScape.BasicScene.MouseTest";
    Camera view;
    XROrigin rig;
    SandPiece selected;
    readonly List<RaycastResult> uiHits = new List<RaycastResult>();

    [MenuItem(MenuPath)]
    static void Toggle() => SessionState.SetBool(Preference, !SessionState.GetBool(Preference, false));

    [MenuItem(MenuPath, true)]
    static bool Validate()
    {
        Menu.SetChecked(MenuPath, SessionState.GetBool(Preference, false));
        return !EditorApplication.isPlayingOrWillChangePlaymode;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Begin()
    {
        if (!SessionState.GetBool(Preference, false) ||
            SceneManager.GetActiveScene().path != "Assets/Scenes/BasicScene.unity") return;
        var camera = Camera.main;
        if (camera != null && camera.GetComponent<RoomMouseTest>() == null)
            camera.gameObject.AddComponent<RoomMouseTest>();
    }

    IEnumerator Start()
    {
        view = GetComponent<Camera>();
        rig = view.GetComponentInParent<XROrigin>();
        foreach (var simulator in FindObjectsOfType<XRInteractionSimulator>())
            simulator.gameObject.SetActive(false);
        var tracking = GetComponent<TrackedPoseDriver>();
        if (tracking != null) tracking.enabled = false;
        if (rig != null)
        {
            foreach (var provider in rig.GetComponentsInChildren<LocomotionProvider>())
                provider.enabled = false;
            rig.MoveCameraToWorldLocation(rig.Origin.transform.position + Vector3.up * 1.6f);
            var scanner = FindObjectOfType<SandTrayScanner>();
            if (scanner != null && scanner.SandSurface != null)
                view.transform.LookAt(scanner.SandSurface.bounds.center, Vector3.up);
        }
        view.stereoTargetEye = StereoTargetEyeMask.None;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // The tray builds its world-space canvas in Start.
        yield return null;
        foreach (var canvasRaycaster in FindObjectsOfType<TrackedDeviceGraphicRaycaster>())
        {
            if (!canvasRaycaster.TryGetComponent<GraphicRaycaster>(out _))
                canvasRaycaster.gameObject.AddComponent<GraphicRaycaster>();
        }
        var events = EventSystem.current;
        if (events != null && events.TryGetComponent<XRUIInputModule>(out var xrInput))
        {
            xrInput.enabled = false;
            events.gameObject.AddComponent<InputSystemUIInputModule>();
        }
    }

    void Update()
    {
        if (selected != null && !selected.MouseSelected) selected = null;
        var mouse = Mouse.current;
        if (SandTrayTour.IsTouring) { SelectPiece(null); return; }
        if (view == null || mouse == null || !Application.isFocused) return;
        if (mouse.rightButton.isPressed)
        {
            Vector2 delta = mouse.delta.ReadValue() * .18f;
            Vector3 angles = view.transform.eulerAngles;
            float pitch = Mathf.Clamp(Mathf.DeltaAngle(0, angles.x) - delta.y, -85, 85);
            view.transform.rotation = Quaternion.Euler(pitch, angles.y + delta.x, 0);
        }
        var keys = Keyboard.current;
        if (keys != null)
        {
            Vector3 move = new Vector3(keys.dKey.ReadValue() - keys.aKey.ReadValue(), 0,
                keys.wKey.ReadValue() - keys.sKey.ReadValue());
            var movementRoot = rig ? rig.Origin.transform : view.transform;
            movementRoot.position += Quaternion.Euler(0, view.transform.eulerAngles.y, 0)
                * Vector3.ClampMagnitude(move, 1) * (2f * Time.unscaledDeltaTime);
            if (keys.escapeKey.wasPressedThisFrame) SelectPiece(null);
        }
        if (!mouse.leftButton.wasPressedThisFrame) return;
        Vector2 point = mouse.position.ReadValue();
        if (!view.pixelRect.Contains(point)) return;
        if (EventSystem.current != null)
        {
            var pointer = new PointerEventData(EventSystem.current) { position = point };
            EventSystem.current.RaycastAll(pointer, uiHits);
            if (uiHits.Exists(hit => hit.module is GraphicRaycaster)) return;
        }
        if (!Physics.Raycast(view.ScreenPointToRay(point), out var hitInfo, view.farClipPlane,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) return;
        var target = hitInfo.collider;
        var piece = target.GetComponentInParent<SandPiece>();
        if (piece != null) { SelectPiece(selected == piece ? null : piece); return; }
        var tray = target.GetComponentInParent<SandTrayScanner>();
        if (selected != null && tray != null && target == tray.SandSurface && hitInfo.normal.y > .8f)
        {
            selected.PlaceOnSand(hitInfo.point);
            SelectPiece(null);
            return;
        }
        target.GetComponentInParent<RoomDimmer>()?.CycleBrightness();
        target.GetComponentInParent<RoomSeat>()?.ToggleSeat(rig);
    }

    void SelectPiece(SandPiece piece)
    {
        if (selected != null) selected.MouseSelected = false;
        selected = piece;
        if (selected != null) selected.MouseSelected = true;
    }

    void OnDisable() => SelectPiece(null);

    void OnGUI()
    {
        if (selected != null)
            GUI.Box(new Rect(12, 12, 320, 30), selected.LocalizedName + MindScape.Environment.RoomLanguage.Pick("  ·  点击沙面放置，Esc 取消", "  ·  Click sand to place, Esc to cancel"));
    }
}
#endif
