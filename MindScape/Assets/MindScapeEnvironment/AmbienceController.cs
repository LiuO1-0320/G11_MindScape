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
            public bool muted, transitioning, brightnessOverridden, audioAvailable, breathing;
            public int sadnessPhase;
            public LampState[] lights;
        }
        public static AmbienceController Instance { get; private set; }
        public Light[] lights = new Light[0];
        public AudioClip sadnessIntroSound, sadnessSound, angerSound, calmSound;
        // Sadness plays the intro clip first, then crossfades into the nature clip.
        public float sadnessIntroSeconds = 90f;
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
        // Slow light swell for the sadness preset only; other presets stay perfectly still.
        public const float BreathingPeriod = 8f, BreathingAmplitude = .05f;
        const float IntroCrossfadeSeconds = 3f;
        public bool SadnessIntroActive => initialized && session.Preset == "sadness"
            && sadnessIntroSound != null && sadnessPhase == 0;

        readonly AmbienceSession session = new AmbienceSession();
        readonly AudioSource[] voices = new AudioSource[4];
        readonly float[] fromVolume = new float[4], toVolume = new float[4];
        float[] baselineIntensity, fromIntensity, targetIntensity, baselineTemperature;
        bool[] baselineUseTemperature;
        Color[] baselineColor, fromColor, targetColor;
        float started, settledAt, introSince = -1, audioStarted;
        bool moving, initialized, breathing, audioMoving;
        int sadnessPhase;
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
            var clips = new[] { sadnessIntroSound, sadnessSound, angerSound, calmSound };
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
            audioMoving = false;
            if (session.Preset != "sadness") sadnessPhase = 0;
            else if (completedPreset != "sadness")
            { sadnessPhase = sadnessIntroSound == null ? 1 : 0; introSince = -1; }
            int selected = session.Preset == "sadness"
                ? sadnessIntroSound != null && sadnessPhase == 0 ? 0 : 1
                : session.Preset == "anger" ? 2 : session.Preset == "calm" ? 3 : -1;
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
            if (!initialized) return;
            if (moving) { EvaluateTransition(Mathf.Clamp01((Time.unscaledTime - started) / TransitionSeconds)); return; }
            if (session.Preset == "sadness" && sadnessPhase == 0 && sadnessIntroSound != null && voices[0].clip != null)
            {
                if (introSince < 0) introSince = Time.unscaledTime;
                else if (Time.unscaledTime - introSince >= sadnessIntroSeconds) StartIntroCrossfade();
            }
            if (audioMoving) EvaluateIntroCrossfade(Mathf.Clamp01((Time.unscaledTime - audioStarted) / IntroCrossfadeSeconds));
            if (breathing) ApplyBreathing();
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
            breathing = session.Preset == "sadness"; settledAt = Time.unscaledTime;
            if (breathing && sadnessPhase == 0 && introSince < 0) introSince = Time.unscaledTime;
            Trace("transition-complete");
        }
        void StartIntroCrossfade()
        {
            sadnessPhase = 1; introSince = 0;
            fromVolume[0] = voices[0].volume; fromVolume[1] = voices[1].volume;
            toVolume[0] = 0; toVolume[1] = voices[1].clip != null ? session.Volume : 0;
            if (toVolume[1] > 0 && !voices[1].isPlaying) voices[1].Play();
            audioStarted = Time.unscaledTime; audioMoving = true;
            Trace("intro-crossfade-start");
        }
        void EvaluateIntroCrossfade(float t)
        {
            float eased = t * t * (3 - 2 * t);
            voices[0].volume = Mathf.Lerp(fromVolume[0], toVolume[0], eased);
            voices[1].volume = Mathf.Lerp(fromVolume[1], toVolume[1], eased);
            if (t < 1) return;
            audioMoving = false; voices[0].Stop();
            Trace("intro-crossfade-complete");
        }
        void ApplyBreathing()
        {
            float factor = 1f + BreathingAmplitude * Mathf.Sin(2f * Mathf.PI * (Time.unscaledTime - settledAt) / BreathingPeriod);
            for (int i = 0; i < lights.Length; i++) if (lights[i] != null)
                lights[i].intensity = targetIntensity[i] * factor;
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
                kelvin = session.Kelvin, muted = session.Muted, transitioning = moving, breathing = breathing,
                sadnessPhase = sadnessPhase, brightnessOverridden = session.BrightnessOverridden,
                audioAvailable = audioAvailable, lights = lampStates });
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
            moving = false; audioMoving = false; breathing = false; introSince = -1; sadnessPhase = 0;
            foreach (var voice in voices) { voice.Stop(); voice.volume = 0; }
            for (int i = 0; i < lights.Length; i++) if (lights[i] != null) lights[i].intensity = baselineIntensity[i];
            RestoreColorSettings(); session.Restore("disabled"); completedPreset = "baseline";
            Trace("disabled");
        }
    }
}
