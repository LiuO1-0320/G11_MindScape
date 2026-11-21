using System;
using UnityEngine;

namespace MindScape.Environment
{
    public sealed class AmbienceController : MonoBehaviour
    {
        [Serializable] public sealed class LampState { public string name; public float intensity; public Color color; }
        [Serializable] public sealed class State
        {
            public string preset, completedPreset, pendingPreset, pendingRequestId, requestId, source;
            public float brightness, volume, effectiveVolume, kelvin, soundLevel;
            public bool muted, transitioning, brightnessOverridden, audioAvailable;
            public LampState[] lights;
        }
        public static AmbienceController Instance { get; private set; }
        public Light[] lights = new Light[0];
        public AudioClip sadnessSound, angerSound, calmSound;
        public event Action<string> StateChanged;
        public string LastTrace { get; private set; } = "Ready";
        public string PendingPreset => session.PendingPreset;
        public string PendingRequestId => session.PendingId;
        public bool IsTransitioning => moving;
        public string CurrentPreset => session.Preset;
        public bool Muted => session.Muted;
        public float Brightness => session.Brightness;
        public float SoundLevel => session.SoundLevel;
        public const float TransitionSeconds = 2.5f;

        readonly AmbienceSession session = new AmbienceSession();
        readonly AudioSource[] voices = new AudioSource[3];
        readonly float[] fromVolume = new float[3], toVolume = new float[3];
        float[] baselineIntensity, fromIntensity, targetIntensity, baselineTemperature;
        bool[] baselineUseTemperature;
        Color[] baselineColor, fromColor, targetColor;
        float started;
        bool moving, initialized;
        string completedPreset = "baseline";
        public static bool IsPreset(string id) => AmbienceSession.IsPreset(id);
        // Unity's temperature utility returns linear RGB; Light.color uses the color-field representation.
        static Color TemperatureColor(float kelvin) => Mathf.CorrelatedColorTemperatureToRGB(kelvin).gamma;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatic() { Instance = null; }
        void OnEnable()
        {
            if (Instance != null && Instance != this) { enabled = false; return; }
            Instance = this;
        }
        public void Initialize()
        {
            if (initialized) return;
            if (lights == null) lights = new Light[0];
            baselineIntensity = new float[lights.Length]; baselineColor = new Color[lights.Length];
            baselineTemperature = new float[lights.Length]; baselineUseTemperature = new bool[lights.Length];
            fromIntensity = new float[lights.Length]; targetIntensity = new float[lights.Length];
            fromColor = new Color[lights.Length]; targetColor = new Color[lights.Length];
            for (int i = 0; i < lights.Length; i++) if (lights[i] != null)
            {
                baselineIntensity[i] = lights[i].intensity; baselineColor[i] = lights[i].color;
                baselineTemperature[i] = lights[i].colorTemperature;
                baselineUseTemperature[i] = lights[i].useColorTemperature;
            }
            var clips = new[] { sadnessSound, angerSound, calmSound };
            for (int i = 0; i < voices.Length; i++)
            {
                voices[i] = gameObject.AddComponent<AudioSource>();
                voices[i].playOnAwake = false; voices[i].loop = true; voices[i].spatialBlend = 0;
                voices[i].volume = 0; voices[i].clip = clips[i]; voices[i].mute = session.Muted;
            }
            initialized = true;
        }
        public bool ProposePreset(string id, string correlationId)
        {
            if (!isActiveAndEnabled || !session.Propose(id, correlationId)) return false;
            Initialize(); Trace("proposed"); return true;
        }
        public void ConfirmProposal() { ConfirmProposal(session.PendingId); }
        public bool ConfirmProposal(string expectedRequestId)
        {
            if (!isActiveAndEnabled || !session.Confirm(expectedRequestId)) return false;
            BeginTransition(); Trace("transition-start"); return true;
        }
        public void RejectProposal() { session.Reject(); Trace("rejected"); }
        public void ApplyManual(string id)
        {
            if (!isActiveAndEnabled || !session.Manual(id)) return;
            BeginTransition(); Trace("transition-start");
        }
        public void RestoreBaseline()
        {
            if (!isActiveAndEnabled) return;
            session.Restore(); BeginTransition(); Trace("restore-start");
        }
        public void SetMuted(bool value)
        {
            if (!isActiveAndEnabled) return;
            Initialize(); session.SetMuted(value);
            foreach (var voice in voices) voice.mute = value;
            Trace(value ? "muted" : "unmuted");
        }
        public void SetBrightness(float value)
        {
            if (!isActiveAndEnabled || !session.SetBrightness(value)) return;
            BeginTransition(); Trace("brightness-feedback");
        }
        public void SetSoundLevel(float value)
        {
            if (!isActiveAndEnabled || !session.SetSoundLevel(value)) return;
            BeginTransition(); Trace("volume-feedback");
        }
        void BeginTransition()
        {
            Initialize();
            for (int i = 0; i < lights.Length; i++) if (lights[i] != null)
            {
                var lamp = lights[i];
                fromIntensity[i] = lamp.intensity;
                fromColor[i] = lamp.color * (lamp.useColorTemperature ? TemperatureColor(lamp.colorTemperature) : Color.white);
                lamp.color = fromColor[i]; lamp.useColorTemperature = false;
                targetIntensity[i] = baselineIntensity[i] * session.Brightness;
                targetColor[i] = session.Preset == "baseline"
                    ? baselineColor[i] * (baselineUseTemperature[i] ? TemperatureColor(baselineTemperature[i]) : Color.white)
                    : TemperatureColor(session.Kelvin);
            }
            int selected = session.Preset == "sadness" ? 0 : session.Preset == "anger" ? 1 : session.Preset == "calm" ? 2 : -1;
            for (int i = 0; i < voices.Length; i++)
            {
                var voice = voices[i]; fromVolume[i] = voice.volume;
                toVolume[i] = i == selected && voice.clip != null ? session.Volume : 0;
                if (toVolume[i] > 0 && !voice.isPlaying) voice.Play();
            }
            started = Time.unscaledTime; moving = true;
        }
        void Update()
        {
            if (!initialized || !moving) return;
            EvaluateTransition(Mathf.Clamp01((Time.unscaledTime - started) / TransitionSeconds));
        }
        void EvaluateTransition(float t)
        {
            float eased = t * t * (3 - 2 * t);
            for (int i = 0; i < lights.Length; i++) if (lights[i] != null)
            {
                lights[i].intensity = Mathf.Lerp(fromIntensity[i], targetIntensity[i], eased);
                lights[i].color = Color.Lerp(fromColor[i], targetColor[i], eased);
            }
            for (int i = 0; i < voices.Length; i++) voices[i].volume = Mathf.Lerp(fromVolume[i], toVolume[i], eased);
            if (t < 1) return;
            moving = false; completedPreset = session.Preset;
            for (int i = 0; i < voices.Length; i++) if (toVolume[i] == 0) voices[i].Stop();
            if (session.Preset == "baseline") RestoreColorSettings();
            Trace("transition-complete");
        }
        void RestoreColorSettings()
        {
            for (int i = 0; i < lights.Length; i++) if (lights[i] != null)
            {
                lights[i].color = baselineColor[i]; lights[i].colorTemperature = baselineTemperature[i];
                lights[i].useColorTemperature = baselineUseTemperature[i];
            }
        }
        public string GetStateJson()
        {
            float actualVolume = 0; bool audioAvailable = false;
            if (initialized) foreach (var voice in voices)
            {
                audioAvailable |= voice.clip != null;
                if (voice.isPlaying && !voice.mute) actualVolume += voice.volume;
            }
            var lampStates = new LampState[lights == null ? 0 : lights.Length];
            for (int i = 0; i < lampStates.Length; i++) if (lights[i] != null)
                lampStates[i] = new LampState { name = lights[i].name, intensity = lights[i].intensity, color = lights[i].color };
            return JsonUtility.ToJson(new State { preset = session.Preset, completedPreset = completedPreset,
                pendingPreset = session.PendingPreset, pendingRequestId = session.PendingId,
                requestId = session.RequestId, source = session.Source, brightness = session.Brightness,
                volume = session.Volume, soundLevel = session.SoundLevel, effectiveVolume = actualVolume,
                kelvin = session.Kelvin, muted = session.Muted, transitioning = moving,
                brightnessOverridden = session.BrightnessOverridden, audioAvailable = audioAvailable, lights = lampStates });
        }
        void Trace(string action)
        {
            string state = GetStateJson(); LastTrace = action + " " + state;
            Debug.Log("[MindScape.Environment] " + LastTrace, this);
            if (StateChanged == null) return;
            foreach (Action<string> subscriber in StateChanged.GetInvocationList())
                try { subscriber(state); } catch (Exception ex) { Debug.LogException(ex, this); }
        }
        void OnDisable()
        {
            if (Instance == this) Instance = null;
            if (!initialized) return;
            moving = false;
            foreach (var voice in voices) { voice.Stop(); voice.volume = 0; }
            for (int i = 0; i < lights.Length; i++) if (lights[i] != null) lights[i].intensity = baselineIntensity[i];
            RestoreColorSettings(); session.Restore("disabled"); completedPreset = "baseline";
            Trace("disabled");
        }
    }
}
