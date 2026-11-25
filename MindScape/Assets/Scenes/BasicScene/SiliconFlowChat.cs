using static MindScape.Environment.RoomLanguage;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

internal class SiliconFlowChat
{
    [Serializable] class Config { public string endpoint, apiKey, model; }
    [Serializable] class Message
    {
        public string role, content;
        public Message(string role, string content) { this.role = role; this.content = content; }
    }
    [Serializable] class Payload { public string model; public Message[] messages; }
    [Serializable] class VisionText { public string type = "text"; public string text; }
    [Serializable] class ImageUrl { public string url; }
    [Serializable] class VisionImage { public string type = "image_url"; public ImageUrl image_url; }
    [Serializable] class SpeechPayload
    {
        public string input;
        public string model = "FunAudioLLM/CosyVoice2-0.5B";
        public string voice = "FunAudioLLM/CosyVoice2-0.5B:claire";
        public string response_format = "wav";
    }
    [Serializable] class Choice { public Message message; }
    [Serializable] class Response { public Choice[] choices; }
    [Serializable] class Transcript { public string text; }
    [Serializable] sealed class EmotionResult { public string action, preset; }
    public string CurrentEmotion { get; private set; }
    readonly Config config;
    readonly List<Message> history = new List<Message>();
    public bool Ready => config != null && Uri.TryCreate(config.endpoint, UriKind.Absolute, out var url)
        && url.Scheme == "https" && !string.IsNullOrWhiteSpace(config.model)
        && !string.IsNullOrWhiteSpace(config.apiKey) && config.apiKey != "YOUR_API_KEY";

    public SiliconFlowChat(TextAsset settings)
    {
        if (settings == null) return;
        try { config = JsonUtility.FromJson<Config>(settings.text); }
        catch (ArgumentException) { Debug.LogWarning("Invalid SiliconFlow configuration."); }
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        if (config != null && string.IsNullOrWhiteSpace(config.apiKey)
            && Uri.TryCreate(config.endpoint, UriKind.Absolute, out var endpoint)
            && endpoint.Scheme == Uri.UriSchemeHttps && endpoint.IsDefaultPort
            && string.Equals(endpoint.Host, "api.siliconflow.cn", StringComparison.OrdinalIgnoreCase))
            config.apiKey = Environment.GetEnvironmentVariable("MINDSCAPE_SILICONFLOW_API_KEY", EnvironmentVariableTarget.User);
#endif
    }

    public async Task<string> TranscribeRecording(float[] samples, int rate, int channels, Func<bool> active)
    {
        if (!active()) throw new OperationCanceledException();
        var form = new List<IMultipartFormSection> {
            new MultipartFormDataSection("model", "FunAudioLLM/SenseVoiceSmall"),
            new MultipartFormFileSection("file", EncodeWave(samples, rate, channels), "recording.wav", "audio/wav")
        };
        var url = new Uri(new Uri(config.endpoint), "../audio/transcriptions").AbsoluteUri;
        using (var req = UnityWebRequest.Post(url, form))
        {
            req.SetRequestHeader("Authorization", "Bearer " + config.apiKey.Trim());
            req.timeout = 60;
            req.redirectLimit = 0;
            var op = req.SendWebRequest();
            while (!op.isDone)
            {
                if (!active()) { req.Abort(); throw new OperationCanceledException(); }
                await Task.Yield();
            }
            if (!active()) throw new OperationCanceledException();
            if (req.result != UnityWebRequest.Result.Success)
                throw new InvalidOperationException(Pick($"云端转写失败（{req.responseCode}），仍可回听。", $"Transcription failed ({req.responseCode}). Replay is available."));
            return JsonUtility.FromJson<Transcript>(req.downloadHandler.text)?.text?.Trim();
        }
    }

    static byte[] EncodeWave(float[] samples, int rate, int channels)
    {
        int dataSize = samples.Length * 2;
        using (var stream = new MemoryStream(44 + dataSize))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + dataSize);
            writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)channels);
            writer.Write(rate);
            writer.Write(rate * channels * 2);
            writer.Write((short)(channels * 2));
            writer.Write((short)16);
            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(dataSize);
            foreach (float sample in samples)
                writer.Write((short)Mathf.RoundToInt(Mathf.Clamp(sample, -1f, 1f) * 32767));
            return stream.ToArray();
        }
    }

    static string LocaleContext => Pick("当前界面语言为简体中文（zh-CN）。", "Current interface language is English (en). ");
    static string ReplyLanguage => LocaleContext + Pick("面向用户的回复请使用简体中文，即使对话历史使用其他语言。", "Write user-facing replies in English, even if earlier messages or item names use another language.");

    static string RoomFeatures => Pick(
        "咨询室操作指南，仅在访客询问或与当前需要直接相关时，简短说明下一步。"
        + "玩家可抓取和摆放沙具，点「识别沙盘」将照片和沙具清单发到对话中供你讨论，点「游览沙盘」进入作品，点「退出游览」返回，点「重置沙盘」将沙具送回架上。"
        + "环境面板可以手动选择「低落 / 钢琴与雨声」「紧张 / 溪流」「平静 / 微风」，也可调亮、调暗、调音量、静音或恢复原状。"
        + "有待确认的环境建议时，对话框会提示玩家到环境面板选择「接受 AI 建议」或「保持当前设置」。"
        + "这些操作由玩家完成。仅凭聊天文字不能查看沙盘或操作房间，不要声称已执行操作或已有待确认建议。",
        "Room guide. Give a brief next step only when asked or directly relevant. "
        + "Players can place pieces, use Scan tray to add a photo and inventory for discussion, Enter tray to explore, Exit tour to return, or Reset tray to send pieces back to the shelf. "
        + "On the Ambience panel, players can select Sadness / piano then rain, Anger / quiet stream, or Calm / gentle air. They can adjust brightness and volume, mute sound, or select Restore original. "
        + "When an ambience proposal is pending, a chat notice directs the player to Accept AI proposal or Keep current on that panel. "
        + "The player operates these controls. Chat text alone cannot inspect the tray or change the room. Do not claim an action was performed or a proposal is pending.");

    public async Task<string> Reply(string text, Func<bool> active)
    {
        var user = new Message("user", text);
        string observation = await ClassifyEmotion(user, active);
        if (!active()) throw new OperationCanceledException();
        CurrentEmotion = observation;
        string emotionContext = observation == null
            ? "本轮没有可用的情绪观察，请直接根据访客的话回应。"
            : "本轮情绪观察（暂定）：" + observation
                + "。baseline 表示中性或不确定。仅据此调整语气，以访客原话和自我描述为准，不直接宣告标签，不作诊断。";
        const string rolePrompt = "你现在扮演一名心理疏导师。像面对面聊天一样，用自然、温和的口吻接住对方刚说的话。"
            + "每次回复尽量不超过200词，能用一两句话说清楚时就简短回应。"
            + "直接用连贯的句子交流，不列清单，不加编号、标题或 Markdown 格式。标点只用逗号和句号。";
        var context = new List<Message> { new Message("system", rolePrompt + "\n" + emotionContext + "\n" + RoomFeatures + "\n" + ReplyLanguage) };
        context.AddRange(history);
        context.Add(user);
        var body = JsonUtility.ToJson(new Payload { model = config.model, messages = context.ToArray() });
        string answer = await Complete(body, active);
        Remember(user, answer);
        return answer;
    }

    public async Task<string> AnalyzeTray(byte[] jpg, string inventory, Func<bool> active)
    {
        var instruction = new VisionText {
            text = "这个是沙盘的图片和上面的东西，你可以作为一个心理咨询师分析一下这个吗？\n沙具清单：" + inventory
                + "\n请结合图中看得见的位置、距离和组合来谈，分清观察和猜想，不凭摆法判断心理状态或诊断。"
                + "像小葵在和对方聊天一样，用简短的自然句，尽量不超过200词，不列点，只用逗号和句号。" + "\n" + RoomFeatures + "\n" + ReplyLanguage
        };
        var image = new VisionImage { image_url = new ImageUrl { url = "data:image/jpeg;base64," + Convert.ToBase64String(jpg) } };
        string body = "{\"model\":\"moonshotai/Kimi-K2.7-Code\",\"messages\":[{\"role\":\"user\",\"content\":["
            + JsonUtility.ToJson(instruction) + "," + JsonUtility.ToJson(image) + "]}]}";
        string answer = await Complete(body, active);
        Remember(new Message("user", "请聊聊这次沙盘摆放。" + inventory), answer);
        return answer;
    }

    async Task<string> ClassifyEmotion(Message user, Func<bool> active)
    {
        const string rules = "你是心理咨询室的氛围观察器，不参与对话，只判断访客当前的主导情绪。"
            + "只输出一行 JSON，不要输出任何其他文字，格式：{\"action\":\"propose_ambience\",\"preset\":\"sadness\"}。"
            + "preset 只能取 sadness、anger、calm、baseline 之一：sadness 表示低落、想哭、孤独正在发生；"
            + "anger 表示此刻正激动、紧绷、想爆发，已经过去的愤怒算 baseline；calm 表示情绪明显平复、放松下来；"
            + "baseline 表示中性、轻微、混合或拿不准，一律选 baseline。"
            + "注意否定与好转的表达，例如“我恨过他，现在好多了”应判 calm。宁可保守，不要过度判断。";
        var context = new List<Message> { new Message("system", rules + "\n上一轮情绪观察：" + (CurrentEmotion ?? "未知")
            + "。请根据访客最新发言重新判断，助手的话仅作对话背景。\n" + LocaleContext + " JSON keys and preset values must stay exactly as specified.") };
        int take = Math.Min(6, history.Count);
        if (take > 0) context.AddRange(history.GetRange(history.Count - take, take));
        context.Add(user);
        var body = JsonUtility.ToJson(new Payload { model = config.model, messages = context.ToArray() });
        try
        {
            string raw = await Complete(body, active, timeoutSeconds: 8);
            int open = raw.IndexOf('{');
            int close = raw.LastIndexOf('}');
            if (open < 0 || close <= open) return null;
            var result = JsonUtility.FromJson<EmotionResult>(raw.Substring(open, close - open + 1));
            if (result == null || result.action != "propose_ambience") return null;
            return result.preset == "baseline" || MindScape.Environment.AmbienceSession.IsPreset(result.preset)
                ? result.preset : null;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception error)
        {
            Debug.LogWarning("Emotion observation unavailable: " + error.GetType().Name);
            return null;
        }
    }

    void Remember(Message user, string answer)
    {
        history.Add(user);
        history.Add(new Message("assistant", answer));
        if (history.Count > 20) history.RemoveRange(0, 2);
    }

    async Task<string> Complete(string body, Func<bool> active, int timeoutSeconds = 60)
    {
        using (var req = new UnityWebRequest(config.endpoint, "POST"))
        {
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("Authorization", "Bearer " + config.apiKey.Trim());
            req.timeout = timeoutSeconds;
            req.redirectLimit = 0;
            var op = req.SendWebRequest();
            while (!op.isDone)
            {
                if (!active()) { req.Abort(); throw new OperationCanceledException(); }
                await Task.Yield();
            }
            if (!active()) throw new OperationCanceledException();
            if (req.result != UnityWebRequest.Result.Success)
                throw new InvalidOperationException(Pick($"连接失败（{req.responseCode}），请检查网络、模型名称和接口配置。", $"Connection failed ({req.responseCode}). Check your network and service settings."));
            var choices = JsonUtility.FromJson<Response>(req.downloadHandler.text)?.choices;
            string answer = choices != null && choices.Length > 0 ? choices[0]?.message?.content : null;
            if (string.IsNullOrWhiteSpace(answer)) throw new InvalidOperationException(Pick("服务没有返回回复正文。", "The service returned an empty reply."));
            return answer.Trim();
        }
    }

    public async Task<AudioClip> Synthesize(string answer, Func<bool> active)
    {
        var url = new Uri(new Uri(config.endpoint), "../audio/speech").AbsoluteUri;
        using (var req = UnityWebRequestMultimedia.GetAudioClip(url, AudioType.WAV))
        {
            req.method = "POST";
            var json = JsonUtility.ToJson(new SpeechPayload { input = answer });
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("Authorization", "Bearer " + config.apiKey.Trim());
            req.timeout = 90;
            req.redirectLimit = 0;
            var op = req.SendWebRequest();
            while (!op.isDone)
            {
                if (!active()) { req.Abort(); throw new OperationCanceledException(); }
                await Task.Yield();
            }
            if (!active()) throw new OperationCanceledException();
            if (req.result != UnityWebRequest.Result.Success)
                throw new InvalidOperationException(Pick($"语音合成失败（{req.responseCode}）", $"Speech synthesis failed ({req.responseCode})."));
            var clip = DownloadHandlerAudioClip.GetContent(req);
            if (clip == null) throw new InvalidOperationException(Pick("未收到有效音频", "No valid audio received."));
            return clip;
        }
    }
}
