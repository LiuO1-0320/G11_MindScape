using System;

namespace MindScape.Environment
{
    // No Unity dependency: the exact consent/preferences logic can also be tested offline.
    public sealed class AmbienceSession
    {
        public string Preset { get; private set; } = "baseline";
        public string PendingPreset { get; private set; } = "";
        public string PendingId { get; private set; } = "";
        public string RequestId { get; private set; } = "";
        public string Source { get; private set; } = "startup";
        public float Brightness { get; private set; } = 1;
        public float SoundLevel { get; private set; } = 1;
        public bool Muted { get; private set; }
        public bool BrightnessOverridden { get; private set; }
        public float Kelvin => Preset == "sadness" ? 2900 : Preset == "anger" ? 3500 : Preset == "calm" ? 3300 : 0;
        public float Volume => (Preset == "baseline" ? 0 : Preset == "anger" ? .09f : .12f) * SoundLevel;
        public static bool IsPreset(string id) => id == "sadness" || id == "anger" || id == "calm";
        public bool Propose(string id, string request)
        {
            if (!IsPreset(id) || string.IsNullOrWhiteSpace(request) || request.Length > 96) return false;
            PendingPreset = id; PendingId = request; return true;
        }
        public bool Confirm(string expectedRequestId)
        {
            if (PendingPreset.Length == 0 || expectedRequestId != PendingId) return false;
            Select(PendingPreset, PendingId, "llm-confirmed"); return true;
        }
        public bool Manual(string id)
        {
            if (!IsPreset(id)) return false;
            Select(id, "manual", "manual"); return true;
        }
        void Select(string id, string request, string source)
        {
            Preset = id; RequestId = request; Source = source;
            if (!BrightnessOverridden) Brightness = id == "sadness" ? .72f : id == "anger" ? .62f : 1;
            Reject();
        }
        public void Reject() { PendingPreset = PendingId = ""; }
        public bool SetBrightness(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return false;
            Brightness = Math.Max(.35f, Math.Min(1.8f, value));
            BrightnessOverridden = true; UserChange(); return true;
        }
        public bool SetSoundLevel(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return false;
            SoundLevel = Math.Max(0, Math.Min(1, value)); UserChange(); return true;
        }
        public void SetMuted(bool value) { Muted = value; UserChange(); }
        void UserChange() { Source = "user-feedback"; RequestId = "manual"; Reject(); }
        public void Restore(string source = "manual")
        {
            Preset = "baseline"; Brightness = 1; BrightnessOverridden = false;
            Source = source; RequestId = "restore"; Reject();
            // User sound preferences deliberately survive Restore.
        }
    }
}
