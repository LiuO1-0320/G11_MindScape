using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;

public class RoomSeat : XRSimpleInteractable
{
    static RoomSeat occupied;
    public bool IsOccupied => occupied == this;
    readonly List<LocomotionProvider> paused = new List<LocomotionProvider>();
    XROrigin rig;
    CharacterController controller;
    Vector3 previousEye;
    Quaternion previousRotation;

    protected override void OnSelectEntered(SelectEnterEventArgs args)
    {
        base.OnSelectEntered(args);
        ToggleSeat(args.interactorObject.transform.GetComponentInParent<XROrigin>());
    }

    public void ToggleSeat(XROrigin player)
    {
        if (occupied == this) { Stand(); return; }
        if (occupied != null) occupied.Stand();
        rig = player;
        if (rig == null || rig.Camera == null) return;
        occupied = this;
        previousEye = rig.Camera.transform.position;
        previousRotation = rig.Origin.transform.rotation;
        foreach (var provider in rig.GetComponentsInChildren<LocomotionProvider>())
        {
            if (!provider.enabled) continue;
            paused.Add(provider);
            provider.enabled = false;
        }
        controller = rig.Origin.GetComponent<CharacterController>();
        if (controller != null && controller.enabled) controller.enabled = false;
        else controller = null;
        rig.MatchOriginUpCameraForward(Vector3.up, transform.forward);
        rig.MoveCameraToWorldLocation(transform.TransformPoint(0, 1.19f, 0.04f));
        rig.GetComponent<RoomBodyCollision>()?.ResetPose();
    }

    public static void StandIfOccupied(XROrigin player)
    {
        if (occupied != null && occupied.rig == player) occupied.Stand();
    }

    void Stand()
    {
        if (rig != null)
        {
            rig.Origin.transform.rotation = previousRotation;
            rig.MoveCameraToWorldLocation(previousEye);
            rig.GetComponent<RoomBodyCollision>()?.ResetPose();
        }
        if (controller != null) controller.enabled = true;
        foreach (var provider in paused)
            if (provider != null) provider.enabled = true;
        paused.Clear();
        occupied = null;
        rig = null;
    }

    protected override void OnDisable()
    {
        if (occupied == this) Stand();
        base.OnDisable();
    }
}
