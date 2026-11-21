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
                throw new InvalidOperationException($"云端转写失败（{req.responseCode}），仍可回听。");
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

    public async Task<string> Reply(string text, Func<bool> active)
    {
        var user = new Message("user", text);
        const string rolePrompt = "你现在扮演一名心理疏导师。像面对面聊天一样，用自然、温和的口吻接住对方刚说的话。"
            + "每次回复尽量不超过200词，能用一两句话说清楚时就简短回应。"
            + "直接用连贯的句子交流，不列清单，不加编号、标题或 Markdown 格式。标点只用逗号和句号。";
        var context = new List<Message> { new Message("system", rolePrompt) };
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
                + "像小葵在和对方聊天一样，用简短的自然句，尽量不超过200词，不列点，只用逗号和句号。"
        };
        var image = new VisionImage { image_url = new ImageUrl { url = "data:image/jpeg;base64," + Convert.ToBase64String(jpg) } };
        string body = "{\"model\":\"moonshotai/Kimi-K2.7-Code\",\"messages\":[{\"role\":\"user\",\"content\":["
            + JsonUtility.ToJson(instruction) + "," + JsonUtility.ToJson(image) + "]}]}";
        string answer = await Complete(body, active);
        Remember(new Message("user", "请聊聊这次沙盘摆放。" + inventory), answer);
        return answer;
    }

    void Remember(Message user, string answer)
    {
        history.Add(user);
        history.Add(new Message("assistant", answer));
        if (history.Count > 20) history.RemoveRange(0, 2);
    }

    async Task<string> Complete(string body, Func<bool> active)
    {
        using (var req = new UnityWebRequest(config.endpoint, "POST"))
        {
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
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
                throw new InvalidOperationException($"连接失败（{req.responseCode}），请检查网络、模型名称和接口配置。");
            var choices = JsonUtility.FromJson<Response>(req.downloadHandler.text)?.choices;
            string answer = choices != null && choices.Length > 0 ? choices[0]?.message?.content : null;
            if (string.IsNullOrWhiteSpace(answer)) throw new InvalidOperationException("服务没有返回回复正文。");
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
                throw new InvalidOperationException($"语音合成失败（{req.responseCode}）");
            var clip = DownloadHandlerAudioClip.GetContent(req);
            if (clip == null) throw new InvalidOperationException("未收到有效音频");
            return clip;
        }
    }
}
