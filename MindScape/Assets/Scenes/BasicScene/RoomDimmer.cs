using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public class RoomDimmer : XRSimpleInteractable
{
    [SerializeField] float[] brightness = { 3f, 6f, 0.45f };
    int level;

    protected override void OnSelectEntered(SelectEnterEventArgs args)
    {
        base.OnSelectEntered(args);
        CycleBrightness();
    }

    public void CycleBrightness()
    {
        var ambience = MindScape.Environment.AmbienceController.Instance;
        if (ambience != null && ambience.isActiveAndEnabled)
        {
            level = (level + 1) % 3;
            ambience.SetBrightness(new[] { .65f, 1f, 1.2f }[level]);
            return;
        }
        var lamp = GetComponentInChildren<Light>();
        if (lamp == null || brightness.Length == 0) return;
        level = (level + 1) % brightness.Length;
        lamp.intensity = brightness[level];
    }
}
