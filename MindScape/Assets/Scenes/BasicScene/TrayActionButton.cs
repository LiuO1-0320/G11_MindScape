using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

public class TrayActionButton : Button
{
    Text label;
    NearFarInteractor[] rays;
    bool aimed;


    void LateUpdate()
    {
        if (label == null) return;
        bool hit = false;
        var rect = (RectTransform)transform;

        foreach (var hand in rays)
        {
            if (!hand.isActiveAndEnabled || !hand.enableUIInteraction) continue;
            if (hand.TryGetCurrentUIRaycastResult(out var ui))
            {
                if (ui.gameObject.transform.IsChildOf(transform)) { hit = true; break; }
                continue;
            }
            var origin = hand.curveOrigin;
            var ray = new Ray(origin.position, origin.forward);
            var plane = new Plane(rect.forward, rect.position);
            if (Vector3.Dot(rect.forward, ray.direction) <= 0 || !plane.Raycast(ray, out float distance) || distance > 8f) continue;
            if (!rect.rect.Contains(rect.InverseTransformPoint(ray.GetPoint(distance)))) continue;
            if (Physics.Raycast(ray, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;
            hit = true;
            break;
        }
        if (aimed == hit) return;
        aimed = hit;
        DoStateTransition(currentSelectionState, true);
    }

    public void SetLabel(Text value)
    {
        rays = FindObjectsOfType<NearFarInteractor>(true);
        label = value;
        transition = Transition.None;
        DoStateTransition(currentSelectionState, true);
    }

    protected override void DoStateTransition(SelectionState state, bool instant)
    {
        if (label == null || targetGraphic == null) return;
        bool pressed = state == SelectionState.Pressed;
        bool focused = aimed || state == SelectionState.Highlighted || state == SelectionState.Selected;
        targetGraphic.color = pressed ? new Color32(152, 13, 83, 255)
            : focused ? new Color32(18, 65, 141, 255) : new Color32(255, 207, 91, 255);
        label.color = pressed || focused ? Color.white : (Color)new Color32(73, 47, 21, 255);
        label.fontStyle = pressed || focused ? FontStyle.Bold : FontStyle.Normal;
    }
}
