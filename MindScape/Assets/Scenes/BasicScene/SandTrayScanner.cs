using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

public class SandTrayScanner : MonoBehaviour
{
    [SerializeField] BoxCollider sand;
    public BoxCollider SandSurface => sand;
    [SerializeField] AudioClip introduction, emptyTray;
    Camera photoCamera;
    Font font;

    void Start()
    {
        var cameraObject = new GameObject("Tray photo camera", typeof(Camera));
        cameraObject.transform.SetParent(transform, false);
        photoCamera = cameraObject.GetComponent<Camera>();
        photoCamera.enabled = false;
        photoCamera.stereoTargetEye = StereoTargetEyeMask.None;
        photoCamera.GetUniversalAdditionalCameraData().allowXRRendering = false;
        photoCamera.orthographic = true;
        photoCamera.transform.localPosition = new Vector3(0, 1.35f, 0);
        photoCamera.transform.localRotation = Quaternion.Euler(90, 0, 0);
        photoCamera.orthographicSize = .49f * transform.lossyScale.z;
        photoCamera.nearClipPlane = .8f * transform.lossyScale.y;
        photoCamera.farClipPlane = 1.48f * transform.lossyScale.y;

        font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "Noto Sans CJK SC", "Droid Sans Fallback", "Arial" }, 36);
        var rig = Camera.main ? Camera.main.GetComponentInParent<Unity.XR.CoreUtils.XROrigin>() : null;
        if (rig && !rig.GetComponent<RoomBodyCollision>()) rig.gameObject.AddComponent<RoomBodyCollision>();
        var tour = gameObject.AddComponent<SandTrayTour>();
        AddButton("识别沙盘", .53f, Recognize);
        AddButton("游览沙盘", .21f, () => tour.Enter(sand, font));
        AddButton("重置沙盘", -.53f, ResetTray);
        photoCamera.cullingMask &= ~(1 << 5);
    }

    void AddButton(string title, float x, UnityEngine.Events.UnityAction click)
    {
        var canvas = new GameObject(title, typeof(RectTransform), typeof(Canvas),
            typeof(TrackedDeviceGraphicRaycaster)).GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = Camera.main;
        var rect = (RectTransform)canvas.transform;
        rect.SetParent(transform, false);
        rect.localPosition = new Vector3(x, .16f, -.475f);
        rect.localRotation = Quaternion.Euler(55, 0, 0);
        rect.localScale = Vector3.one * .0013f;
        rect.sizeDelta = new Vector2(210, 72);
        var background = canvas.gameObject.AddComponent<Image>();
        background.color = new Color32(255, 207, 91, 255);
        var button = canvas.gameObject.AddComponent<TrayActionButton>();
        button.targetGraphic = background;
        button.onClick.AddListener(click);
        var label = new GameObject("Label", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
        label.rectTransform.SetParent(rect, false);
        label.rectTransform.sizeDelta = rect.sizeDelta;
        label.font = font;
        label.fontSize = 34;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = new Color32(73, 47, 21, 255);
        label.raycastTarget = false;
        label.text = title;
        canvas.gameObject.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 2f;
        button.SetLabel(label);
        canvas.gameObject.layer = label.gameObject.layer = 5;
    }

    void ResetTray()
    {
        foreach (var piece in FindObjectsOfType<SandPiece>())
            if (piece.gameObject.scene == gameObject.scene) piece.ReturnToShelf();
        Physics.SyncTransforms();
    }

    void Recognize()
    {
        var chat = transform.root.GetComponentInChildren<RoomVoiceChat>();
        if (chat == null) return;
        var counts = new SortedDictionary<string, int>();
        var names = new SortedDictionary<string, AudioClip>();
        foreach (var piece in FindObjectsOfType<SandPiece>())
        {
            if (!piece.IsOn(sand)) continue;
            counts.TryGetValue(piece.displayName, out int count);
            counts[piece.displayName] = count + 1;
            names[piece.displayName] = piece.spokenName;
        }
        var items = new List<string>();
        foreach (var entry in counts) items.Add(entry.Key + " × " + entry.Value);
        string caption = items.Count == 0 ? "沙盘现在是空的。" : "我看到沙盘上有：" + string.Join("、", items) + "。";
        var voice = new List<AudioClip> { items.Count == 0 ? emptyTray : introduction };
        voice.AddRange(names.Values);
        chat.PostTrayPhoto(caption, TakePhoto(), voice);
    }

    Texture2D TakePhoto()
    {
        const int width = 768, height = 432;
        var target = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
        var previous = RenderTexture.active;
        try
        {
            photoCamera.aspect = (float)width / height;
            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
            RenderPipeline.SubmitRenderRequest(photoCamera, request);
            RenderTexture.active = target;
            var photo = new Texture2D(width, height, TextureFormat.RGB24, false);
            photo.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            photo.Apply(false, false);
            return photo;
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
        }
    }

    void OnDestroy()
    {
        if (font != null) Destroy(font);
    }
}
