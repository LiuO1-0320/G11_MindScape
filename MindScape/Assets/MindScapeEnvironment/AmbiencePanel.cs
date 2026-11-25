using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;

namespace MindScape.Environment
{
    public sealed class AmbiencePanel : MonoBehaviour
    {
        AmbienceController controller;
        readonly List<Text> statuses = new List<Text>();
        readonly List<Button> acceptButtons = new List<Button>();
        Font font;
        GameObject desktopCanvas;
        string displayedRequest = "";
        float nextRefresh;
        public void Bind(AmbienceController target)
        {
            if (controller != null) return;
            controller = target; font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "Noto Sans CJK SC", "Droid Sans Fallback", "Arial" }, 28);
            var world = CreateCanvas("Ambience VR Controls", RenderMode.WorldSpace);
            world.transform.position = new Vector3(-1.35f, 1.45f, -2.75f);
            world.transform.rotation = Quaternion.Euler(0, 180, 0);
            world.transform.localScale = Vector3.one * .0018f;
            // Optional XR adapter keeps the environment assembly usable in a non-XR test project.
            var xrType = Type.GetType("UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster, Unity.XR.Interaction.Toolkit");
            if (xrType != null) world.gameObject.AddComponent(xrType);
            else Debug.LogWarning("XR UI raycaster unavailable. Desktop controls remain available.", this);
            Layout(world);
            var desktop = CreateCanvas("Ambience Desktop Controls", RenderMode.ScreenSpaceOverlay);
            desktopCanvas = desktop.gameObject;
            desktop.gameObject.AddComponent<GraphicRaycaster>();
            var scaler = desktop.gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720); scaler.matchWidthOrHeight = .5f;
            var sheet = new GameObject("Control sheet", typeof(RectTransform)).GetComponent<RectTransform>();
            sheet.SetParent(desktop.transform, false);
            sheet.anchorMin = sheet.anchorMax = sheet.pivot = new Vector2(1, 1);
            sheet.anchoredPosition = new Vector2(-16, -60); sheet.localScale = Vector3.one * .8f;
            sheet.sizeDelta = new Vector2(680, 620); Layout(sheet);
            sheet.gameObject.SetActive(false);
            var toggle = MakeButton(desktop, "环境", "Ambience", 0, 0, () => sheet.gameObject.SetActive(!sheet.gameObject.activeSelf));
            var tab = (RectTransform)toggle.transform;
            tab.anchorMin = tab.anchorMax = tab.pivot = Vector2.one;
            tab.anchoredPosition = new Vector2(-16, -12);
            tab.sizeDelta = new Vector2(130, 40);
            var caption = toggle.GetComponentInChildren<Text>();
            caption.rectTransform.anchorMin = Vector2.zero;
            caption.rectTransform.anchorMax = Vector2.one;
            caption.rectTransform.offsetMin = caption.rectTransform.offsetMax = Vector2.zero;
            caption.alignment = TextAnchor.MiddleCenter;
            Refresh();
        }
        RectTransform CreateCanvas(string name, RenderMode mode)
        {
            var obj = new GameObject(name, typeof(RectTransform), typeof(Canvas));
            obj.transform.SetParent(transform, false);
            var canvas = obj.GetComponent<Canvas>(); canvas.renderMode = mode; canvas.worldCamera = Camera.main;
            canvas.sortingOrder = 20;
            var rect = obj.GetComponent<RectTransform>(); rect.sizeDelta = new Vector2(680, 620);
            return rect;
        }
        void Layout(RectTransform panel)
        {
            panel.gameObject.AddComponent<Image>().color = new Color(.97f, .94f, .85f, .98f);
            Label(panel, "模式 B / 环境", "MODE B  /  AMBIENCE", 20, 10, 640, 42, 28);
            Label(panel, "按个人舒适度调整环境", "Comfort preferences, not diagnosis or treatment", 20, 58, 640, 35, 19);
            MakeButton(panel, "低落 / 钢琴与雨声", "Sadness / piano then rain", 20, 108, () => controller.ApplyManual("sadness"));
            MakeButton(panel, "紧张 / 溪流", "Anger / quiet stream", 350, 108, () => controller.ApplyManual("anger"));
            MakeButton(panel, "平静 / 微风", "Calm / gentle air", 20, 176, () => controller.ApplyManual("calm"));
            MakeButton(panel, "静音 / 取消静音", "Mute / unmute", 350, 176, () => controller.SetMuted(!controller.Muted));
            MakeButton(panel, "调亮", "Brighter", 20, 244, () => controller.SetBrightness(controller.Brightness + .4f));
            MakeButton(panel, "调暗", "Dimmer", 350, 244, () => controller.SetBrightness(controller.Brightness - .4f));
            MakeButton(panel, "降低音量", "Sound softer", 20, 312, () => controller.SetSoundLevel(controller.SoundLevel - .2f));
            MakeButton(panel, "提高音量", "Sound louder", 350, 312, () => controller.SetSoundLevel(controller.SoundLevel + .2f));
            acceptButtons.Add(MakeButton(panel, "接受 AI 建议", "Accept AI proposal", 20, 380, () => controller.ConfirmProposal(displayedRequest)));
            MakeButton(panel, "保持当前设置", "Keep current", 350, 380, controller.RejectProposal);
            MakeButton(panel, "恢复原状", "Restore original", 20, 448, controller.RestoreBaseline);
            statuses.Add(Label(panel, "", "", 20, 518, 640, 94, 18));
        }
        RectTransform Box(Transform parent, string name, float x, float y, float w, float h)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false); rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(w, h); return rect;
        }
        Text Label(Transform parent, string chinese, string value, float x, float y, float w, float h, int size)
        {
            var text = Box(parent, "Label", x, y, w, h).gameObject.AddComponent<Text>();
            if (value.Length > 0) RoomLanguage.Bind(text, chinese, value); text.font = font; text.fontSize = size; text.color = new Color(.22f, .18f, .12f);
            text.raycastTarget = false; return text;
        }
        Button MakeButton(Transform parent, string chinese, string value, float x, float y, UnityEngine.Events.UnityAction action)
        {
            var rect = Box(parent, value, x, y, 310, 58);
            var image = rect.gameObject.AddComponent<Image>(); image.color = new Color(1, .8f, .36f);
            var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = image; button.onClick.AddListener(action);
            Label(rect, chinese, value, 8, 10, 295, 40, 22); return button;
        }
        void Refresh()
        {
            if (controller == null) return;
            if (desktopCanvas != null) desktopCanvas.SetActive(!XRSettings.isDeviceActive);
            displayedRequest = controller.PendingRequestId;
            foreach (var button in acceptButtons) button.interactable = displayedRequest.Length > 0;
            string value = RoomLanguage.Pick("亮度 ", "Brightness: ") + Mathf.RoundToInt(controller.Brightness * 100f) + "%   " + controller.CurrentPreset + RoomLanguage.Pick("   待确认 ", "   Pending: ") + controller.PendingPreset
                + "\n" + (controller.IsTransitioning ? RoomLanguage.Pick("过渡中（2.5 秒）", "Transitioning (2.5 seconds)") : RoomLanguage.Pick("就绪", "Ready"))
                + (controller.Muted ? RoomLanguage.Pick("   已静音", "   Sound muted") : "")
                + (controller.SadnessIntroActive ? RoomLanguage.Pick("\n先播放钢琴，再过渡到自然声", "\nSadness music: empathy now, nature sounds later") : "")
                + RoomLanguage.Pick("\n手动预设由你选择。", "\nManual presets are not LLM decisions.");
            foreach (var text in statuses) text.text = value;
        }
        void OnDestroy() { if (font != null) Destroy(font); }
        void Update() { if (Time.unscaledTime < nextRefresh) return; nextRefresh = Time.unscaledTime + .1f; Refresh(); }
    }
}
