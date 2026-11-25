using static MindScape.Environment.RoomLanguage;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Whisper;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

public class RoomVoiceChat : MonoBehaviour
{
    [SerializeField] AudioClip[] replies = new AudioClip[0];
    [SerializeField] TextAsset llmSettings;
    SiliconFlowChat cloud;
    readonly EmotionAmbienceLink ambienceLink = new EmotionAmbienceLink();
    int modeVersion;
    static readonly string[] EnglishPrompts = {
        "I hear you. What drew you to that piece?",
        "What name would you give this scene?",
        "Take your time. Is there anything else you would like to share?"
    };
    static readonly string[] Prompts = {
        "我听到了。你愿意说说，刚才为什么选了这件沙具吗？",
        "如果给这个小场景起个名字，你会选什么呢？",
        "你可以慢慢来。还有什么想和我分享的吗？"
    };
    const int MaxSeconds = 30;
    readonly List<AudioClip> messages = new List<AudioClip>();
    readonly List<string> transcripts = new List<string>();
    WhisperManager recognizer;
    Task recognitionQueue = Task.CompletedTask;
    readonly List<Text> userBubbles = new List<Text>();
    readonly List<Text> timeline = new List<Text>();
    readonly List<AudioClip> generatedVoices = new List<AudioClip>();
    readonly Dictionary<Text, RawImage> photos = new Dictionary<Text, RawImage>();
    Coroutine narration;
    ScrollRect history;
    RectTransform content;
    Text greeting;
    Text ambienceNotice;
    string displayedAmbience = "";
    readonly Color ink = new Color32(73, 47, 21, 255);
    readonly Color honey = new Color32(255, 204, 89, 255);
    Sprite rounded;
    Font font;
    RectTransform panel;
    Image paper;
    Text mode, status, action;
    AudioSource speaker;
    AudioClip recording;
    string device;
    float started;
    bool llm = true;
    bool pending;

    void Awake()
    {
        cloud = new SiliconFlowChat(llmSettings);
        font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "Microsoft YaHei UI", "Noto Sans CJK SC", "Noto Sans SC", "Droid Sans Fallback", "PingFang SC", "Arial" }, 28);
        speaker = gameObject.AddComponent<AudioSource>();
        recognizer = gameObject.AddComponent<WhisperManager>();
        recognizer.language = "auto";
        recognizer.translateToEnglish = false;
        speaker.playOnAwake = false;
        rounded = RoundedPanel();
        var canvasObject = new GameObject("Conversation", typeof(RectTransform), typeof(Canvas), typeof(TrackedDeviceGraphicRaycaster));
        panel = (RectTransform)canvasObject.transform;
        panel.SetParent(transform, false);
        panel.localPosition = new Vector3(0, 0, -.055f);
        panel.localScale = Vector3.one * .0021f;
        panel.sizeDelta = new Vector2(480, 960);
        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = Camera.main;
        var paperImage = panel.gameObject.AddComponent<Image>();
        paperImage.sprite = rounded;
        paperImage.type = Image.Type.Sliced;
        paperImage.color = PaperBaseline;
        paperImage.raycastTarget = false;
        paper = paperImage;
        var heading = Label(panel, "", 18, 18, 155, 56, 28);
        heading.fontStyle = FontStyle.Bold;
        Bind(heading, "小葵 / 会话", "Xiaokui");
        Bind(ButtonAt("", 177, 18, 109, 56, Toggle), "English", "中文");
        mode = ButtonAt(Pick("本地模式 ⇄", "Local ⇄"), 296, 18, 166, 56, SwitchMode);
        status = Label(panel, "", 18, 78, 444, 68, 21);
        action = ButtonAt("", 18, 870, 444, 68, TapRecord, highlightOnAim: true);

        var viewport = Box(panel, "Chat history", 12, 150, 456, 702);
        viewport.gameObject.AddComponent<Image>().color = Color.clear;
        viewport.gameObject.AddComponent<RectMask2D>();
        history = viewport.gameObject.AddComponent<ScrollRect>();
        history.horizontal = false;
        history.movementType = ScrollRect.MovementType.Clamped;
        history.scrollSensitivity = 42;
        history.viewport = viewport;
        content = Box(viewport, "Messages", 0, 0, 456, 702);
        history.content = content;
        greeting = Label(content, "", 8, 8, 434, 60, 24);
        Bind(greeting, "小葵   想从哪件沙具聊起？", "Xiaokui   Which piece shall we talk about?");
        ResetStatus();
    }

    void OnEnable()
    {
        MindScape.Environment.RoomLanguage.Changed += RefreshLanguage;
        if (action != null) ResetStatus();
    }

    void RefreshLanguage()
    {
        RefreshAmbienceNotice(languageChanged: true);
        modeVersion++;
        StopNarration();
        speaker.Stop();
        ResetStatus();
        if (recording != null) action.text = Pick("结束并发送", "Finish and send");
    }

    RectTransform Box(Transform parent, string title, float x, float y, float w, float h)
    {
        var rect = (RectTransform)new GameObject(title, typeof(RectTransform)).transform;
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(w, h);
        return rect;
    }

    // A shared nine-slice keeps the same soft corners on short and long bubbles.
    static Sprite RoundedPanel()
    {
        const int size = 32;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.wrapMode = TextureWrapMode.Clamp;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            var delta = new Vector2(Mathf.Max(0, Mathf.Abs(x - 15.5f) - 4),
                Mathf.Max(0, Mathf.Abs(y - 15.5f) - 4));
            texture.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(12 - delta.magnitude)));
        }
        texture.Apply(false, true);
        return Sprite.Create(texture, new Rect(0, 0, size, size), Vector2.one * .5f,
            100, 0, SpriteMeshType.FullRect, Vector4.one * 12);
    }

    Text Label(Transform parent, string title, int x, int y, int w, int h, int size)
    {
        var text = Box(parent, "Label", x, y, w, h).gameObject.AddComponent<Text>();
        text.font = font;
        text.text = title;
        text.fontSize = size;
        text.color = ink;
        text.alignment = TextAnchor.MiddleLeft;
        text.raycastTarget = false;
        return text;
    }

    Text ButtonAt(string title, int x, int y, int w, int h, UnityEngine.Events.UnityAction click, Transform parent = null, bool highlightOnAim = false)
    {
        var rect = Box(parent == null ? panel : parent, "Button", x, y, w, h);
        var image = rect.gameObject.AddComponent<Image>();
        image.sprite = rounded;
        image.type = Image.Type.Sliced;
        image.color = new Color32(255, 252, 239, 255);
        Button button = highlightOnAim ? (Button)rect.gameObject.AddComponent<TrayActionButton>() : rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(click);
        var label = Label(rect, title, 0, 0, w, h, 24);
        label.alignment = TextAnchor.MiddleCenter;
        if (button is TrayActionButton highlighted) highlighted.SetLabel(label);
        return label;
    }

    void ResetStatus()
    {
        mode.text = llm ? Pick("LLM 模式 ⇄", "AI chat ⇄") : Pick("本地模式 ⇄", "Local ⇄");
        status.text = llm ? (cloud.Ready ? Pick("发送后，录音将上传至 SiliconFlow 识别。", "Sending uploads your recording to SiliconFlow.") : Pick("请填写 SiliconFlowConfig.json 中的地址和密钥。", "Set the URL and key in SiliconFlowConfig.json.")) : Pick("上下滑动查看记录 · 点击气泡回听", "Scroll for history. Tap a message to replay.");
        action.text = llm && !cloud.Ready ? Pick("等待接口配置", "Awaiting setup") : Pick("点击录音", "Tap to record");
    }

    void SwitchMode()
    {
        StopAllCoroutines();
        pending = false;
        CancelRecording();
        speaker.Stop();
        llm = !llm;
        modeVersion++;
        ResetStatus();
    }

    void TapRecord()
    {
        if ((llm && !cloud.Ready) || pending) return;
        if (recording != null) FinishRecording();
        else StartCoroutine(StartRecording());
    }

    IEnumerator StartRecording()
    {
        pending = true;
        StopNarration();
        speaker.Stop();
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone))
        {
            UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Microphone);
            status.text = Pick("请允许麦克风权限，然后再次点击录音。", "Allow microphone access, then tap record again.");
            pending = false;
            yield break;
        }
#elif UNITY_IOS || UNITY_WEBGL
        yield return Application.RequestUserAuthorization(UserAuthorization.Microphone);
#endif
        pending = false;
        var devices = Microphone.devices;
        if (devices.Length == 0) { status.text = Pick("未检测到麦克风，请检查设备和麦克风权限。", "Check your microphone and its permissions."); yield break; }
        device = devices[0];
        recording = Microphone.Start(device, false, MaxSeconds + 1, 16000);
        if (recording == null) { status.text = Pick("无法开始录音，请检查麦克风权限。", "Check microphone access and try again."); yield break; }
        started = Time.realtimeSinceStartup;
        action.text = Pick("结束并发送", "Finish and send");
        yield break;
    }

    void Update()
    {
        FadePaperToMood();
        RefreshAmbienceNotice();
        if (recording == null) return;
        float seconds = Time.realtimeSinceStartup - started;
        status.text = Pick($"正在录音  {Mathf.FloorToInt(seconds)} / {MaxSeconds} 秒", $"Recording  {Mathf.FloorToInt(seconds)} / {MaxSeconds} s");
        if (seconds >= MaxSeconds) FinishRecording();
    }

    // The conversation paper follows the ambience preset at the same 2.5s pace as the
    // room: white while sad, light gray while angry, back to warm cream when recovered.
    static readonly Color32 PaperBaseline = new Color32(255, 243, 207, 255);
    static readonly Color32 PaperSadness = new Color32(255, 255, 255, 255);
    static readonly Color32 PaperAnger = new Color32(211, 211, 211, 255);
    void FadePaperToMood()
    {
        if (paper == null) return;
        var ambience = MindScape.Environment.AmbienceController.Instance;
        if (ambience == null) return;
        var preset = ambience.CurrentPreset;
        var target = preset == "sadness" ? PaperSadness
            : preset == "anger" ? PaperAnger : PaperBaseline;
        float step = Time.deltaTime / 2.5f;
        paper.color = new Color32(
            (byte)Mathf.MoveTowards(((Color32)paper.color).r, target.r, step * 255f),
            (byte)Mathf.MoveTowards(((Color32)paper.color).g, target.g, step * 255f),
            (byte)Mathf.MoveTowards(((Color32)paper.color).b, target.b, step * 255f),
            (byte)Mathf.MoveTowards(((Color32)paper.color).a, target.a, step * 255f));
    }

    void FinishRecording()
    {
        int frames = Microphone.GetPosition(device);
        if (frames <= 0 && Time.realtimeSinceStartup - started >= MaxSeconds && !Microphone.IsRecording(device))
            frames = MaxSeconds * recording.frequency;
        frames = Mathf.Min(frames, MaxSeconds * recording.frequency);
        Microphone.End(device);
        var source = recording;
        recording = null;
        ResetStatus();
        if (frames < source.frequency / 4)
        {
            Destroy(source);
            status.text = Pick("录音太短，请再试一次。", "Recording too short. Please try again.");
            return;
        }
        var samples = new float[frames * source.channels];
        source.GetData(samples, 0);
        var clip = AudioClip.Create("Voice message", frames, source.channels, source.frequency, false);
        clip.SetData(samples, 0);
        Destroy(source);
        messages.Add(clip);
        transcripts.Add(Pick("正在识别语言和文字…", "Transcribing…"));
        Text cloudBubble = AppendBubbles(messages.Count - 1);
        recognitionQueue = Transcribe(tryAfter: recognitionQueue, index: messages.Count - 1,
            samples: samples, frequency: clip.frequency, channels: clip.channels, cloudBubble: cloudBubble, version: modeVersion);
        if (!llm && !English && replies.Length > (messages.Count - 1) % Prompts.Length) Play(replies[(messages.Count - 1) % Prompts.Length]);
    }

    void Play(AudioClip clip)
    {
        if (recording != null || clip == null) return;
        StopNarration();
        speaker.clip = clip;
        speaker.Play();
    }

    async Task Transcribe(Task tryAfter, int index, float[] samples, int frequency, int channels, Text cloudBubble, int version)
    {
        string recognized = null;
        try
        {
            await tryAfter;
            if (this == null) return;
            if (cloudBubble != null)
            {
                recognized = await cloud.TranscribeRecording(samples, frequency, channels, Active);
                if (this == null) return;
                transcripts[index] = string.IsNullOrWhiteSpace(recognized) ? Pick("未识别到清晰语音", "No clear speech detected") : recognized;
            }
            else
            {
                var result = await recognizer.GetTextAsync(samples, frequency, channels);
                if (this == null) return;
                if (result == null) transcripts[index] = Pick("本机识别不可用，仍可回听。", "Local transcription unavailable. Replay is available.");
                else if (string.IsNullOrWhiteSpace(result.Result)) transcripts[index] = Pick("未识别到清晰语音", "No clear speech detected");
                else
                {
                    string language = result.Language == "zh" ? Pick("中文", "Chinese") :
                        result.Language == "en" ? Pick("英语", "English") : result.Language;
                    transcripts[index] = language + "  ·  " + result.Result.Trim();
                }
            }
        }
        catch (System.Exception error)
        {
            if (this == null) return;
            Debug.LogWarning("Voice transcription failed: " + error.GetType().Name);
            transcripts[index] = error is System.OperationCanceledException ? Pick("已取消云端转写，仍可回听。", "Transcription cancelled. Replay is available.") :
                error is System.InvalidOperationException && cloudBubble != null ? error.Message : Pick("转写失败，仍可回听。", "Transcription failed. Replay is available.");
        }
        var label = userBubbles[index];
        label.text = VoiceCaption(index);
        LayoutHistory();
        if (cloudBubble == null) return;
        bool Active() => this != null && isActiveAndEnabled && version == modeVersion && llm;
        string answer = Pick("未取得有效转写，请重新录音。", "Please record again.");
        bool hasReply = false;
        if (!Active()) answer = Pick("本次请求已取消。", "Request cancelled.");
        else if (!string.IsNullOrWhiteSpace(recognized))
        {
            cloudBubble.text = Pick("小葵正在回复…", "Xiaokui is replying…");
            LayoutHistory();
            try { answer = await cloud.Reply(recognized, Active); hasReply = true; }
            catch (System.OperationCanceledException) { answer = Pick("请求已取消。", "Request cancelled."); }
            catch (System.InvalidOperationException error) { answer = error.Message; }
            catch (System.Exception) { answer = Pick("未能读取服务回复，请检查接口配置后重试。", "Could not read the reply. Check the service settings."); }
        }
        if (this == null || cloudBubble == null) return;
        cloudBubble.text = Pick("小葵\n", "Xiaokui\n") + answer;
        LayoutHistory();
        if (hasReply && Active()) ambienceLink.HandleTurn(cloud.CurrentEmotion);
        if (hasReply && Active())
            await VoiceReply(cloudBubble, answer, Active);
    }

    async Task VoiceReply(Text label, string answer, System.Func<bool> active)
    {
        var button = label.transform.parent.GetComponent<Button>();
        button.interactable = false;
        label.text = Pick("小葵 · 正在生成语音\n", "Xiaokui · Generating audio\n") + answer;
        LayoutHistory();
        try
        {
            var clip = await cloud.Synthesize(answer, active);
            generatedVoices.Add(clip);
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => Play(clip));
            label.text = Pick("小葵  ▶\n", "Xiaokui  ▶\n") + answer;
            button.interactable = true;
            if (!pending) Play(clip);
        }
        catch (System.Exception error)
        {
            if (this == null || label == null) return;
            string reason = error is System.InvalidOperationException ? error.Message : Pick("语音未生成", "Audio unavailable");
            label.text = Pick($"小葵 · {reason}，点此重试\n", $"Xiaokui · {reason}, tap to retry\n") + answer;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(async () =>
            {
                if (!llm) { status.text = Pick("请切换到 LLM 模式重试语音。", "Switch to AI chat to retry audio."); return; }
                int version = modeVersion;
                await VoiceReply(label, answer, () => this != null && isActiveAndEnabled && llm && modeVersion == version);
            });
            button.interactable = true;
        }
        LayoutHistory();
    }

    string VoiceCaption(int index)
    {
        return Pick("我", "Me") + $"  ▶  {Mathf.CeilToInt(messages[index].length)}" + Pick(" 秒", " s") + "\n" + transcripts[index];
    }

    Text AppendBubbles(int index)
    {
        greeting.gameObject.SetActive(false);
        var user = ButtonAt(VoiceCaption(index), 68, 0, 380, 60,
            () => Play(messages[index]), content);
        user.transform.parent.GetComponent<Image>().color = honey;
        user.supportRichText = false;
        userBubbles.Add(user);
        timeline.Add(user);
        if (llm)
        {
            return AddCloudBubble(Pick("等待语音转写…", "Waiting for transcription…"));
        }
        int reply = index % Prompts.Length;
        bool hasVoice = !English && reply < replies.Length;
        timeline.Add(ButtonAt(Pick("小葵  ▶\n", "Xiaokui\n") + Pick(Prompts[reply], EnglishPrompts[reply]), 8, 0, 380, 60,
            () => { if (hasVoice) Play(replies[reply]); }, content));
        LayoutHistory();
        return null;
    }

    void RefreshAmbienceNotice(bool languageChanged = false)
    {
        var controller = MindScape.Environment.AmbienceController.Instance;
        string preset = controller != null ? controller.PendingPreset : "";
        if (preset == displayedAmbience && !languageChanged) return;
        displayedAmbience = preset;
        if (string.IsNullOrEmpty(preset))
        {
            if (ambienceNotice == null) return;
            timeline.Remove(ambienceNotice);
            Destroy(ambienceNotice.transform.parent.gameObject);
            ambienceNotice = null;
            LayoutHistory();
            return;
        }

        string sound = preset == "sadness" ? Pick("钢琴和雨声", "piano and rain")
            : preset == "anger" ? Pick("溪流声", "stream sounds") : Pick("轻风声", "soft wind");
        string message = Pick(
            $"环境建议 · 想试试{sound}吗？请到环境面板选择「接受 AI 建议」，或选择「保持当前设置」。",
            $"Ambience suggestion · Try {sound}? On the Ambience panel, choose “Accept AI proposal” or “Keep current”.");
        if (ambienceNotice == null)
        {
            ambienceNotice = AddCloudBubble(message);
            ambienceNotice.transform.parent.GetComponent<Image>().color = honey;
        }
        else
        {
            ambienceNotice.text = message;
            LayoutHistory();
        }
    }

    Text AddCloudBubble(string initialText)
    {
        var rect = Box(content, "LLM reply", 8, 0, 380, 60);
        var background = rect.gameObject.AddComponent<Image>();
        background.sprite = rounded;
        background.type = Image.Type.Sliced;
        var replyText = Label(rect, initialText, 0, 0, 380, 60, 24);
        var replay = rect.gameObject.AddComponent<Button>();
        replay.targetGraphic = background;
        replay.interactable = false;
        replyText.supportRichText = false;
        timeline.Add(replyText);
        LayoutHistory();
        return replyText;
    }

    void LayoutHistory()
    {
        float viewHeight = history.viewport.rect.height;
        float offset = content.anchoredPosition.y;
        bool followLatest = content.rect.height - viewHeight - offset <= 12;
        float top = 8;
        float addedAbove = 0;
        foreach (var label in timeline)
        {
            PlaceBubble(label, ref top, offset, ref addedAbove);
        }
        float height = Mathf.Max(viewHeight, top);
        content.sizeDelta = new Vector2(content.sizeDelta.x, height);
        content.anchoredPosition = new Vector2(0, followLatest ? height - viewHeight :
            Mathf.Clamp(offset + addedAbove, 0, height - viewHeight));
    }

    public void PostTrayPhoto(string caption, Texture2D photo, List<AudioClip> voice)
    {
        greeting.gameObject.SetActive(false);
        var label = ButtonAt(Pick("小葵  ▶  沙盘照片\n", "Xiaokui · Tray photo\n") + caption, 8, 0, 380, 60,
            () => SpeakReport(voice), content);
        var image = Box(label.transform.parent, "Sand tray photo", 14, 0, 352, 198)
            .gameObject.AddComponent<RawImage>();
        image.texture = photo;
        image.raycastTarget = false;
        photos.Add(label, image);
        timeline.Add(label);
        LayoutHistory();
        SpeakReport(voice);
        if (llm && cloud.Ready)
        {
            var reply = AddCloudBubble(Pick("小葵正在查看沙盘照片和清单…", "Xiaokui is looking at your tray…"));
            recognitionQueue = AnalyzeTray(recognitionQueue, reply, caption, photo, modeVersion);
        }
        else if (llm) AddCloudBubble(Pick("已保存本次沙盘照片。填写 SiliconFlow 接口配置后可进行图片分析。", "Photo added. Configure SiliconFlow for image analysis."));
    }

    async Task AnalyzeTray(Task previous, Text label, string inventory, Texture2D photo, int version)
    {
        bool Active() => this != null && isActiveAndEnabled && llm && modeVersion == version;
        try
        {
            await previous;
            if (!Active()) throw new System.OperationCanceledException();
            string answer = await cloud.AnalyzeTray(photo.EncodeToJPG(88), inventory, Active);
            if (!Active()) throw new System.OperationCanceledException();
            label.text = Pick("小葵\n", "Xiaokui\n") + answer;
            LayoutHistory();
            await VoiceReply(label, answer, Active);
        }
        catch (System.Exception error)
        {
            if (this == null || label == null) return;
            label.text = error is System.OperationCanceledException ? Pick("本次沙盘分析已取消。", "Tray analysis cancelled.") :
                error is System.InvalidOperationException ? error.Message : Pick("未能分析这张照片，请再次点击识别沙盘。", "Could not analyze the photo. Scan the tray again.");
            LayoutHistory();
        }
    }

    void SpeakReport(List<AudioClip> voice)
    {
        if (English || recording != null || pending) return;
        StopNarration();
        speaker.Stop();
        narration = StartCoroutine(ReadReport(voice));
    }

    IEnumerator ReadReport(List<AudioClip> clips)
    {
        foreach (var clip in clips)
        {
            if (clip == null) continue;
            speaker.clip = clip;
            speaker.Play();
            while (speaker.isPlaying) yield return null;
        }
    }

    void StopNarration()
    {
        if (narration != null) StopCoroutine(narration);
        narration = null;
    }

    void PlaceBubble(Text label, ref float top, float offset, ref float addedAbove)
    {
        var bubble = (RectTransform)label.transform.parent;
        float oldHeight = bubble.rect.height;
        float oldBottom = -bubble.anchoredPosition.y + oldHeight;
        label.alignment = TextAnchor.MiddleLeft;
        label.rectTransform.anchoredPosition = new Vector2(14, -10);
        float width = bubble.rect.width - 28;
        label.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
        float height = Mathf.Ceil(label.preferredHeight) + 4;
        label.rectTransform.sizeDelta = new Vector2(width, height);
        if (photos.TryGetValue(label, out var photo))
        {
            photo.rectTransform.anchoredPosition = new Vector2(14, -height - 16);
            height += photo.rectTransform.rect.height + 6;
        }
        bubble.sizeDelta = new Vector2(bubble.rect.width, height + 20);
        if (oldBottom <= offset && Mathf.Abs(bubble.anchoredPosition.y) > 0)
            addedAbove += bubble.rect.height - oldHeight;
        bubble.anchoredPosition = new Vector2(bubble.anchoredPosition.x, -top);
        top += bubble.rect.height + 12;
    }

    void CancelRecording()
    {
        if (recording == null) return;
        Microphone.End(device);
        Destroy(recording);
        recording = null;
    }

    void OnDisable()
    {
        MindScape.Environment.RoomLanguage.Changed -= RefreshLanguage;
        modeVersion++;
        StopAllCoroutines();
        pending = false;
        CancelRecording();
        if (speaker != null) speaker.Stop();
        if (action != null) ResetStatus();
    }

    void OnDestroy()
    {
        foreach (var clip in generatedVoices) Destroy(clip);
        foreach (var clip in messages) Destroy(clip);
        foreach (var image in photos.Values) Destroy(image.texture);
        if (font != null) Destroy(font);
        if (rounded != null)
        {
            Destroy(rounded.texture);
            Destroy(rounded);
        }
    }
}
