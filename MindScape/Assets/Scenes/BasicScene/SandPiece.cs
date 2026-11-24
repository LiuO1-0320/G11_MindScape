using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

[RequireComponent(typeof(XRGrabInteractable))]
public class SandPiece : MonoBehaviour
{
    public string displayName;
    public AudioClip spokenName;

    Rigidbody body;
    Transform homeParent;
    Vector3 homePosition, homeScale;
    Quaternion homeRotation;
    bool recoveredGrab, previousTrackRotation, mouseSelected;
    RigidbodyConstraints previousConstraints;

    static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
    static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
    XRGrabInteractable grab;
    MeshRenderer[] surfaces;
    MeshFilter[] edgeMeshes;
    Material edgeMaterial;
    MaterialPropertyBlock edgeWidth;
    MaterialPropertyBlock properties;
    MaterialPropertyBlock[] originals;
    public bool MouseSelected
    {
        get => mouseSelected;
        set
        {
            if (value && !mouseSelected && IsTipped)
            {
                float bottom = PieceBounds().min.y;
                StandUpright();
                body.position += Vector3.up * (bottom - PieceBounds().min.y);
            }
            mouseSelected = value;
        }
    }
    int glowLevel;

    NearFarInteractor placementHand;
    BoxCollider sand;
    LineRenderer landingRing;
    Material ringMaterial;

    void Awake()
    {
        homeParent = transform.parent;
        homePosition = transform.localPosition;
        homeRotation = transform.localRotation;
        homeScale = transform.localScale;
        body = GetComponent<Rigidbody>();
        grab = GetComponent<XRGrabInteractable>();
        grab.selectEntered.AddListener(BeginPlacement);
        grab.selectExited.AddListener(EndPlacement);
        surfaces = GetComponentsInChildren<MeshRenderer>();
        edgeMeshes = GetComponentsInChildren<MeshFilter>();
        var edgeShader = Resources.Load<Shader>("SandPieceGlowEdge");
        if (edgeShader != null) edgeMaterial = new Material(edgeShader);
        edgeWidth = new MaterialPropertyBlock();
        properties = new MaterialPropertyBlock();
        originals = new MaterialPropertyBlock[surfaces.Length];
        for (int i = 0; i < surfaces.Length; i++) originals[i] = new MaterialPropertyBlock();
    }

    void LateUpdate()
    {
        if (placementHand != null)
        {
            bool valid = TryLanding(out Vector3 point);
            landingRing.enabled = valid;
            if (valid) landingRing.transform.position = point + sand.transform.up * .006f;
        }
        int level = MouseSelected || grab.isSelected ? 2 : grab.isHovered ? 1 : 0;
        SetGlow(level);
        if (level == 0 || edgeMaterial == null || SandTrayTour.IsTouring) return;
        foreach (var part in edgeMeshes)
        {
            var mesh = part.sharedMesh;
            if (mesh == null) continue;
            for (int i = 0; i < mesh.subMeshCount; i++)
                Graphics.DrawMesh(mesh, part.transform.localToWorldMatrix, edgeMaterial,
                    part.gameObject.layer, null, i, edgeWidth,
                    UnityEngine.Rendering.ShadowCastingMode.Off, false);
        }
    }

    void OnDisable()
    {
        RestoreGrabSettings();
        placementHand = null;
        if (landingRing != null) landingRing.enabled = false;
        MouseSelected = false;
        SetGlow(0);
    }

    void SetGlow(int level)
    {
        if (glowLevel == level) return;
        float intensity = level == 2 ? 2.4f : 1.25f;
        edgeWidth.SetFloat("_EdgePixels", level == 2 ? 3.8f : 2.4f);
        for (int i = 0; i < surfaces.Length; i++)
        {
            var surface = surfaces[i];
            if (level == 0)
            {
                surface.SetPropertyBlock(originals[i].isEmpty ? null : originals[i]);
                continue;
            }
            if (glowLevel == 0) surface.GetPropertyBlock(originals[i]);
            var material = surface.sharedMaterial;
            if (material == null || !material.HasProperty(EmissionColor) || !material.HasProperty(BaseColor))
                continue;
            surface.GetPropertyBlock(properties);
            Color original = material.GetColor(BaseColor);
            properties.SetColor(EmissionColor, original * intensity);
            surface.SetPropertyBlock(properties);
        }
        glowLevel = level;
    }
    void BeginPlacement(SelectEnterEventArgs args)
    {
        if (!recoveredGrab && IsTipped)
        {
            previousTrackRotation = grab.trackRotation;
            previousConstraints = body.constraints;
            recoveredGrab = true;
            StandUpright();
            grab.trackRotation = false;
            body.constraints |= RigidbodyConstraints.FreezeRotation;
        }
        if (!(args.interactorObject is NearFarInteractor hand)) return;
        // Direct grabs stay physical. A distant grip gets a tray placement preview.
        if (Vector3.Distance(hand.curveOrigin.position, transform.position) < .30f) return;
        var tray = FindObjectOfType<SandTrayScanner>();
        if (tray == null) return;
        sand = tray.SandSurface;
        placementHand = hand;
        if (landingRing != null) return;
        var marker = new GameObject("沙具落点");
        marker.transform.SetParent(tray.transform, false);
        marker.transform.rotation = sand.transform.rotation;
        landingRing = marker.AddComponent<LineRenderer>();
        ringMaterial = new Material(sand.GetComponent<MeshRenderer>().sharedMaterial);
        ringMaterial.color = new Color(.12f, 1f, .62f);
        ringMaterial.EnableKeyword("_EMISSION");
        ringMaterial.SetColor(EmissionColor, new Color(.08f, .95f, .45f));
        landingRing.sharedMaterial = ringMaterial;
        landingRing.useWorldSpace = false;
        landingRing.loop = true;
        landingRing.widthMultiplier = .008f;
        landingRing.positionCount = 40;
        for (int i = 0; i < landingRing.positionCount; i++)
        {
            float a = i * 2f * Mathf.PI / landingRing.positionCount;
            landingRing.SetPosition(i, .055f * new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)));
        }
        landingRing.enabled = false;
    }

    bool TryLanding(out Vector3 point)
    {
        point = default;
        if (sand == null || placementHand == null || SandTrayTour.IsTouring) return false;
        Transform hand = placementHand.curveOrigin;
        if (!sand.Raycast(new Ray(hand.position, hand.forward), out var hit, Mathf.Infinity) ||
            Vector3.Dot(hit.normal, sand.transform.up) < .9f) return false;
        point = hit.point;
        return true;
    }

    void EndPlacement(SelectExitEventArgs args)
    {
        if (!grab.isSelected) RestoreGrabSettings();
        if (placementHand == null || args.interactorObject != (IXRSelectInteractor)placementHand) return;
        if (!args.isCanceled && !grab.isSelected && TryLanding(out var point)) PlaceOnSand(point);
        placementHand = null;
        if (landingRing != null) landingRing.enabled = false;
    }

    void OnDestroy()
    {
        if (edgeMaterial != null) Destroy(edgeMaterial);
        if (landingRing != null) Destroy(landingRing.gameObject);
        if (ringMaterial != null) Destroy(ringMaterial);
    }

    bool IsTipped => Vector3.Dot(transform.up, Vector3.up) < .985f;

    void RestoreGrabSettings()
    {
        if (!recoveredGrab) return;
        grab.trackRotation = previousTrackRotation;
        body.constraints = previousConstraints;
        recoveredGrab = false;
    }

    void StandUpright()
    {
        Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        if (forward.sqrMagnitude < .001f) forward = Vector3.Cross(transform.right, Vector3.up);
        var rotation = Quaternion.LookRotation(forward, Vector3.up);
        transform.rotation = rotation;
        body.rotation = rotation;
        if (!body.isKinematic) body.angularVelocity = Vector3.zero;
        Physics.SyncTransforms();
    }

    Bounds PieceBounds()
    {
        var shapes = grab.colliders;
        var bounds = shapes[0].bounds;
        for (int i = 1; i < shapes.Count; i++) bounds.Encapsulate(shapes[i].bounds);
        return bounds;
    }

    public void ReturnToShelf()
    {
        if (grab.isSelected)
            grab.interactionManager.CancelInteractableSelection((IXRSelectInteractable)grab);
        MouseSelected = false;
        if (transform.parent != homeParent) transform.SetParent(homeParent, false);
        transform.SetLocalPositionAndRotation(homePosition, homeRotation);
        transform.localScale = homeScale;
        body.position = transform.position;
        body.rotation = transform.rotation;
        if (!body.isKinematic)
        {
            body.velocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.Sleep();
        }
    }

    public void PlaceOnSand(Vector3 point)
    {
        StandUpright();
        var bounds = PieceBounds();
        var bottom = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
        body.position += point - bottom + Vector3.up * .002f;
        body.velocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        body.WakeUp();
    }

    public bool IsOn(BoxCollider sand)
    {
        if (grab.isSelected || grab.colliders.Count == 0) return false;
        Bounds bounds = grab.colliders[0].bounds;
        foreach (var collider in grab.colliders) bounds.Encapsulate(collider.bounds);
        Vector3 center = sand.transform.InverseTransformPoint(bounds.center) - sand.center;
        Vector3 bottom = bounds.center - sand.transform.up * Vector3.Dot(bounds.extents,
            new Vector3(Mathf.Abs(sand.transform.up.x), Mathf.Abs(sand.transform.up.y), Mathf.Abs(sand.transform.up.z)));
        float distance = Vector3.Dot(bottom - sand.transform.TransformPoint(sand.center + Vector3.up * sand.size.y / 2), sand.transform.up);
        return Mathf.Abs(center.x) <= sand.size.x / 2 && Mathf.Abs(center.z) <= sand.size.z / 2
            && distance >= -.03f && distance <= .04f;
    }
}
