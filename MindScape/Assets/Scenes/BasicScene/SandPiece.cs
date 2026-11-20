using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

[RequireComponent(typeof(XRGrabInteractable))]
public class SandPiece : MonoBehaviour
{
    public string displayName;
    public AudioClip spokenName;

    public bool IsOn(BoxCollider sand)
    {
        var grab = GetComponent<XRGrabInteractable>();
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
