using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

// Constrain the final tracked eye, including poses supplied after the locomotion providers run.
[DefaultExecutionOrder(10000)]
public sealed class RoomBodyCollision : MonoBehaviour
{
    const float Radius = .18f;
    const float Clearance = .003f;
    struct Solid
    {
        public MeshRenderer surface;
        public Bounds bounds;
        public XRGrabInteractable grab;
        public RoomSeat seat;
    }

    readonly List<Solid> solids = new List<Solid>();
    XROrigin rig;
    Bounds floor;
    float ceiling;
    Vector3 safe;
    bool ready;

    void Start()
    {
        rig = GetComponent<XROrigin>();
        var room = gameObject.scene.GetRootGameObjects();
        Transform shell = null;
        foreach (var root in room)
        {
            shell = root.transform.Find("RoomShell/Floor");
            if (shell != null) break;
        }
        if (shell == null || rig.Camera == null) return;
        floor = shell.GetComponent<Renderer>().bounds;
        ceiling = shell.parent.Find("Cream ceiling").GetComponent<Renderer>().bounds.min.y;
        foreach (var surface in shell.parent.parent.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (surface.transform.IsChildOf(rig.transform) || surface.transform == shell ||
                surface.name == "West meadow and wooded hills" || surface.GetComponentInParent<Canvas>() != null)
                continue;
            var box = surface.bounds;
            if (box.max.y <= floor.max.y + .04f || box.min.y >= ceiling - .005f) continue;
            solids.Add(new Solid {surface = surface, bounds = box,
                grab = surface.GetComponentInParent<XRGrabInteractable>(),
                seat = surface.GetComponentInParent<RoomSeat>()});
        }
        ResetPose();
        ready = true;
    }

    public void ResetPose()
    {
        if (rig != null && rig.Camera != null) safe = rig.Camera.transform.position;
    }

    void OnEnable() => Application.onBeforeRender += Constrain;
    void OnDisable() => Application.onBeforeRender -= Constrain;
    void LateUpdate() => Constrain();

    [BeforeRenderOrder(11000)]
    void Constrain()
    {
        if (!ready || SandTrayTour.IsTouring) return;
        Vector3 current = rig.Camera.transform.position;
        Vector3 target = new Vector3(
            Mathf.Clamp(current.x, floor.min.x + Radius, floor.max.x - Radius),
            Mathf.Clamp(current.y, floor.max.y + .06f, ceiling - .10f),
            Mathf.Clamp(current.z, floor.min.z + Radius, floor.max.z - Radius));
        Vector3 delta = target - safe;
        for (int pass = 0; pass < 3 && delta.sqrMagnitude > 1e-12f; pass++)
        {
            float first = 1f;
            Vector3 normal = Vector3.zero;
            foreach (var solid in solids)
            {
                if (!solid.surface || !solid.surface.enabled || !solid.surface.gameObject.activeInHierarchy ||
                    (solid.grab && solid.grab.isSelected) || (solid.seat && solid.seat.IsOccupied)) continue;
                var box = solid.grab ? solid.surface.bounds : solid.bounds;
                // The body occupies the space from floor to head. The head can pass under an overhead object.
                float enter = 0f, leave = 1f;
                Vector3 side = Vector3.zero;
                if (!SandTrayTour.ClipAxis(safe.x, delta.x, box.min.x - Radius, box.max.x + Radius,
                        Vector3.right, ref enter, ref leave, ref side) ||
                    !SandTrayTour.ClipAxis(safe.z, delta.z, box.min.z - Radius, box.max.z + Radius,
                        Vector3.forward, ref enter, ref leave, ref side) ||
                    !SandTrayTour.ClipAxis(safe.y, delta.y, box.min.y - .10f, ceiling + 1f,
                        Vector3.up, ref enter, ref leave, ref side)) continue;
                if (side != Vector3.zero && enter < first) { first = enter; normal = side; }
            }
            safe += delta * first;
            if (normal == Vector3.zero) break;
            safe += normal * Clearance;
            delta = Vector3.ProjectOnPlane(delta * (1f - first), normal);
        }
        Vector3 correction = safe - current;
        if (correction.sqrMagnitude > 1e-12f) rig.Origin.transform.position += correction;
    }
}
