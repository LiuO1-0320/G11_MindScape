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
        public Renderer[] furniture = new Renderer[0];
        public Renderer[] walls = new Renderer[0];
        public Renderer[] floors = new Renderer[0];
        public Renderer[] lamps = new Renderer[0];
        public Renderer[] pillows = new Renderer[0];
        public Renderer[] screens = new Renderer[0];
        public Renderer[] screenFrames = new Renderer[0];
        public Renderer[] cabinetGlows = new Renderer[0];
        public GameObject[] plants = new GameObject[0];
        public GameObject[] plantReplacements = new GameObject[0];
        // Window-side exceptions: these lights run cold white through sadness AND anger,
        // while every other light follows the preset kelvin (sadness stays warm white).
        public Light[] coldWhiteLights = new Light[0];
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
        public const float ColdWhiteKelvin = 6500f;
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
        // Surface tinting rides the same 2.5s transition as the lights. Slots map to
        // (renderer, material index) pairs so multi-material objects keep per-slot colors,
        // and every slot belongs to a group (furniture, walls, floors, lamps, pillows,
        // screens or screen frames) with its own palette.
        const byte GroupFurniture = 0, GroupWall = 1, GroupFloor = 2, GroupLamp = 3, GroupPillow = 4, GroupScreen = 5, GroupFrame = 6, GroupCabinetGlow = 7;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int LegacyColorId = Shader.PropertyToID("_Color");
        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        // Tints read as the designer's hex values; .linear matches what URP stores in materials.
        // Furniture cools toward blue-white; floors go navy when sad. Walls start from a white
        // base color over a cream texture, so hex-white would be a no-op multiplier — instead
        // they use HDR multipliers that brighten the cream texture toward pure white (sadness)
        // or dim it into light gray (anger), keeping the texture and the smooth 2.5s lerp.
        // The pendant lamp is untextured, so its tint is the absolute fixture color, and its
        // glowing opal glass also lerps _EmissionColor to the same hue at its baseline HDR level.
        public static readonly Color SadnessTint = Linear("#BFD4F2"), AngerTint = Linear("#D9D9D6");
        public static readonly Color FloorSadnessTint = Linear("#2B3A5C");
        // Surface tints are multiplicative over warm textures, so the wall and floor grays
        // are texture-compensated: target gray divided by the average Limewash/Oak color,
        // otherwise the cream and tan textures would read beige and coffee instead of gray.
        public static readonly Color FloorAngerTint = new Color(.088f, .138f, .281f, 1f);
        public static readonly Color WallSadnessTint = new Color(1.15f, 1.25f, 1.6f, 1f);
        public static readonly Color WallAngerTint = new Color(.63f, .712f, .884f, 1f);
        public static readonly Color LampSadnessTint = Linear("#ADD8E6"), LampAngerTint = Linear("#2B3A5C");
        // Throw pillows read white in both sadness and anger; only recovery restores color.
        public static readonly Color PillowSadnessTint = Linear("#FFFFFF"), PillowAngerTint = Linear("#FFFFFF");
        // The conversation screen and its frame are untextured, so these are absolute colors.
        public static readonly Color ScreenSadnessTint = Linear("#FFFFFF"), ScreenAngerTint = Linear("#D3D3D3");
        public static readonly Color FrameSadnessTint = Linear("#A8C8E8"), FrameAngerTint = Linear("#1F3A5F");
        // Cabinet diffusers only change when angry: cold white at their baseline HDR glow level.
        public static readonly Color CabinetGlowAngerTint = new Color(1.5f, 1.58f, 1.66f, 1f);
        Renderer[] tintRenderer;
        int[] tintSlot, tintPropertyId;
        byte[] tintGroup;
        bool[] tintEmissive;
        Color[] tintBase, tintFrom, tintTarget, tintApplied;
        Color[] tintEmissionBase, tintEmissionFrom, tintEmissionTarget, tintEmissionApplied;
        MaterialPropertyBlock tintBlock;
        // The sadness preset swaps the potted plants for blue tulips: the originals shrink
        // to zero while the tulips grow in over the same 2.5s curve; recovery reverses it.
        Vector3[] plantScale, plantFrom, plantTarget, replacementScale, replacementFrom, replacementTarget;
        float started, settledAt, introSince = -1, audioStarted;
        bool moving, initialized, breathing, audioMoving;
        bool[] coldLight;
        public bool AdjustRoomAmbient { get; set; }
        Color sky, equator, ground;
        float ambientScale = 1f, fromAmbientScale;
        int sadnessPhase;
        string completedPreset = "baseline";
        public static bool IsPreset(string id) => AmbienceSession.IsPreset(id);
        // Unity's temperature utility returns linear RGB; Light.color uses the color-field representation.
        static Color TemperatureColor(float kelvin) => Mathf.CorrelatedColorTemperatureToRGB(kelvin).gamma;
        // Cold-white exceptions (the window-side lamp) override the preset kelvin in
        // sadness and anger; every other preset keeps the session's temperature.
        float EffectiveKelvin(int i) => coldLight != null && i < coldLight.Length && coldLight[i]
            && (session.Preset == "sadness" || session.Preset == "anger") ? ColdWhiteKelvin : session.Kelvin;
        static Color Linear(string hex) =>
            ColorUtility.TryParseHtmlString(hex, out var srgb) ? (Color)srgb.linear : Color.white;
        static bool TryGetBaseColor(Material material, out int propertyId, out Color color)
        {
            if (material != null && material.HasProperty(BaseColorId)) { propertyId = BaseColorId; color = material.GetColor(BaseColorId); return true; }
            if (material != null && material.HasProperty(LegacyColorId)) { propertyId = LegacyColorId; color = material.GetColor(LegacyColorId); return true; }
            propertyId = BaseColorId; color = Color.white; return false;
        }
        static Color? TintFor(byte group, string preset)
        {
            // Cabinet diffusers keep their warm glow through sadness; only anger cools them.
            if (group == GroupCabinetGlow) return preset == "anger" ? CabinetGlowAngerTint : (Color?)null;
            if (preset == "sadness") return group == GroupWall ? WallSadnessTint
                : group == GroupFloor ? FloorSadnessTint
                : group == GroupLamp ? LampSadnessTint
                : group == GroupPillow ? PillowSadnessTint
                : group == GroupScreen ? ScreenSadnessTint
                : group == GroupFrame ? FrameSadnessTint : SadnessTint;
            if (preset == "anger") return group == GroupWall ? WallAngerTint
                : group == GroupFloor ? FloorAngerTint
                : group == GroupLamp ? LampAngerTint
                : group == GroupPillow ? PillowAngerTint
                : group == GroupScreen ? ScreenAngerTint
                : group == GroupFrame ? FrameAngerTint : AngerTint;
            return null;
        }
        // Emission target keeps the fixture glowing at its baseline HDR level but in the
        // tint's hue: the tint scaled so its brightest channel matches the baseline's.
        public static Color EmissionTint(Color tint, Color baseline)
        {
            float level = baseline.maxColorComponent;
            if (level <= 0f) return baseline;
            return tint * (level / Mathf.Max(tint.maxColorComponent, 1e-4f));
        }

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
            if (AdjustRoomAmbient && gameObject.scene == UnityEngine.SceneManagement.SceneManager.GetActiveScene())
            {
                sky = RenderSettings.ambientSkyColor;
                equator = RenderSettings.ambientEquatorColor;
                ground = RenderSettings.ambientGroundColor;
            }
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
            coldLight = new bool[lights == null ? 0 : lights.Length];
            for (int i = 0; i < coldLight.Length; i++)
                coldLight[i] = lights[i] != null && System.Array.IndexOf(coldWhiteLights, lights[i]) >= 0;
            var clips = new[] { sadnessIntroSound, sadnessSound, angerSound, calmSound };
            for (int i = 0; i < voices.Length; i++)
            {
                voices[i] = gameObject.AddComponent<AudioSource>();
                voices[i].playOnAwake = false; voices[i].loop = true; voices[i].spatialBlend = 0;
                voices[i].volume = 0; voices[i].clip = clips[i]; voices[i].mute = session.Muted;
            }
            CacheTintTargets();
            CachePlantSwap();
            initialized = true;
        }
        void CachePlantSwap()
        {
            plantScale = ScaleCache(plants);
            plantFrom = (Vector3[])plantScale.Clone();
            plantTarget = (Vector3[])plantScale.Clone();
            replacementScale = ScaleCache(plantReplacements);
            replacementFrom = (Vector3[])replacementScale.Clone();
            replacementTarget = (Vector3[])replacementScale.Clone();
        }
        static Vector3[] ScaleCache(GameObject[] objects)
        {
            var scales = new Vector3[objects == null ? 0 : objects.Length];
            for (int i = 0; i < scales.Length; i++)
                scales[i] = objects[i] != null ? objects[i].transform.localScale : Vector3.zero;
            return scales;
        }
        void RestorePlantSwap()
        {
            if (plantScale == null) return;
            for (int i = 0; i < plantScale.Length; i++)
                if (plants[i] != null) { plants[i].SetActive(true); plants[i].transform.localScale = plantScale[i]; }
            for (int i = 0; i < replacementScale.Length; i++)
                if (plantReplacements[i] != null) { plantReplacements[i].SetActive(false); plantReplacements[i].transform.localScale = replacementScale[i]; }
        }
        void CacheTintTargets()
        {
            // Flatten (renderer, slot, group) triples; shaders without a color property are skipped.
            var renderers = new System.Collections.Generic.List<Renderer>();
            var slots = new System.Collections.Generic.List<int>();
            var properties = new System.Collections.Generic.List<int>();
            var groups = new System.Collections.Generic.List<byte>();
            var colors = new System.Collections.Generic.List<Color>();
            var emissive = new System.Collections.Generic.List<bool>();
            var emissions = new System.Collections.Generic.List<Color>();
            CacheGroup(furniture, GroupFurniture, renderers, slots, properties, groups, colors, emissive, emissions);
            CacheGroup(walls, GroupWall, renderers, slots, properties, groups, colors, emissive, emissions);
            CacheGroup(floors, GroupFloor, renderers, slots, properties, groups, colors, emissive, emissions);
            CacheGroup(lamps, GroupLamp, renderers, slots, properties, groups, colors, emissive, emissions);
            CacheGroup(pillows, GroupPillow, renderers, slots, properties, groups, colors, emissive, emissions);
            CacheGroup(screens, GroupScreen, renderers, slots, properties, groups, colors, emissive, emissions);
            CacheGroup(screenFrames, GroupFrame, renderers, slots, properties, groups, colors, emissive, emissions);
            CacheGroup(cabinetGlows, GroupCabinetGlow, renderers, slots, properties, groups, colors, emissive, emissions);
            tintRenderer = renderers.ToArray();
            tintSlot = slots.ToArray();
            tintPropertyId = properties.ToArray();
            tintGroup = groups.ToArray();
            tintBase = colors.ToArray();
            tintFrom = (Color[])tintBase.Clone();
            tintTarget = (Color[])tintBase.Clone();
            tintApplied = (Color[])tintBase.Clone();
            tintEmissive = emissive.ToArray();
            tintEmissionBase = emissions.ToArray();
            tintEmissionFrom = (Color[])tintEmissionBase.Clone();
            tintEmissionTarget = (Color[])tintEmissionBase.Clone();
            tintEmissionApplied = (Color[])tintEmissionBase.Clone();
            tintBlock = new MaterialPropertyBlock();
        }
        static void CacheGroup(Renderer[] pieces, byte group,
            System.Collections.Generic.List<Renderer> renderers, System.Collections.Generic.List<int> slots,
            System.Collections.Generic.List<int> properties, System.Collections.Generic.List<byte> groups,
            System.Collections.Generic.List<Color> colors,
            System.Collections.Generic.List<bool> emissive, System.Collections.Generic.List<Color> emissions)
        {
            if (pieces == null) return;
            for (int i = 0; i < pieces.Length; i++)
            {
                var piece = pieces[i];
                if (piece == null) continue;
                var materials = piece.sharedMaterials;
                for (int slot = 0; slot < materials.Length; slot++)
                    if (TryGetBaseColor(materials[slot], out var propertyId, out var color))
                    {
                        renderers.Add(piece); slots.Add(slot); properties.Add(propertyId);
                        groups.Add(group); colors.Add(color);
                        // Only the pendant glass actually glows; other groups never
                        // touch emission so their rendering is bit-for-bit unchanged.
                        bool glow = group == GroupLamp && materials[slot] != null
                            && materials[slot].IsKeywordEnabled("_EMISSION")
                            && materials[slot].HasProperty(EmissionColorId);
                        emissive.Add(glow);
                        emissions.Add(glow ? materials[slot].GetColor(EmissionColorId) : Color.black);
                    }
            }
        }
        void RestoreTintColors()
        {
            if (tintRenderer == null) return;
            for (int i = 0; i < tintRenderer.Length; i++)
            {
                var piece = tintRenderer[i];
                if (piece == null) continue;
                tintApplied[i] = tintBase[i];
                tintEmissionApplied[i] = tintEmissionBase[i];
                ApplyTint(i, piece);
            }
        }
        void ApplyTint(int i, Renderer piece)
        {
            piece.GetPropertyBlock(tintBlock, tintSlot[i]);
            tintBlock.SetColor(tintPropertyId[i], tintApplied[i]);
            if (tintEmissive[i]) tintBlock.SetColor(EmissionColorId, tintEmissionApplied[i]);
            piece.SetPropertyBlock(tintBlock, tintSlot[i]);
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
                    : TemperatureColor(EffectiveKelvin(i));
            }
            audioMoving = false;
            if (session.Preset != "sadness") sadnessPhase = 0;
            else if (completedPreset != "sadness")
            { sadnessPhase = sadnessIntroSound == null ? 1 : 0; introSince = -1; }
            // Each surface group picks its own preset tint: sadness cools furniture toward
            // blue-white and washes walls pure white over a navy floor, anger settles grays,
            // and the pendant lamp itself turns light blue or navy. Every other preset
            // (including calm recovery and baseline) returns the originals.
            if (tintRenderer != null)
                for (int i = 0; i < tintRenderer.Length; i++)
                {
                    Color? tint = TintFor(tintGroup[i], session.Preset);
                    tintFrom[i] = tintApplied[i];
                    tintTarget[i] = tint ?? tintBase[i];
                    // Only hue-shift the glow while a tint is active; recovery returns the
                    // original emission untouched (its hue is not the base color's hue).
                    tintEmissionFrom[i] = tintEmissionApplied[i];
                    tintEmissionTarget[i] = tintEmissive[i] && tint != null
                        ? EmissionTint(tint.Value, tintEmissionBase[i]) : tintEmissionBase[i];
                }
            // The plant swap rides the same transition: tulips bloom in for sadness only.
            bool tulips = session.Preset == "sadness";
            if (plantScale != null)
                for (int i = 0; i < plantScale.Length; i++)
                {
                    var plant = plants[i];
                    if (plant == null) continue;
                    plantFrom[i] = plant.activeSelf ? plant.transform.localScale : Vector3.zero;
                    plantTarget[i] = tulips ? Vector3.zero : plantScale[i];
                    if (plantFrom[i] != Vector3.zero || plantTarget[i] != Vector3.zero) plant.SetActive(true);
                }
            if (replacementScale != null)
                for (int i = 0; i < replacementScale.Length; i++)
                {
                    var tulip = plantReplacements[i];
                    if (tulip == null) continue;
                    replacementFrom[i] = tulip.activeSelf ? tulip.transform.localScale : Vector3.zero;
                    replacementTarget[i] = tulips ? replacementScale[i] : Vector3.zero;
                    if (replacementFrom[i] != Vector3.zero || replacementTarget[i] != Vector3.zero) tulip.SetActive(true);
                }
            int selected = session.Preset == "sadness"
                ? sadnessIntroSound != null && sadnessPhase == 0 ? 0 : 1
                : session.Preset == "anger" ? 2 : session.Preset == "calm" ? 3 : -1;
            for (int i = 0; i < voices.Length; i++)
            {
                var voice = voices[i]; fromVolume[i] = voice.volume;
                toVolume[i] = i == selected && voice.clip != null ? session.Volume : 0;
                if (toVolume[i] > 0 && !voice.isPlaying) voice.Play();
            }
            fromAmbientScale = ambientScale;
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
            if (AdjustRoomAmbient && gameObject.scene == UnityEngine.SceneManagement.SceneManager.GetActiveScene())
                SetRoomAmbient(Mathf.Lerp(fromAmbientScale, session.BrightnessOverridden ? session.Brightness : 1f, eased));
            for (int i = 0; i < voices.Length; i++) voices[i].volume = Mathf.Lerp(fromVolume[i], toVolume[i], eased);
            if (tintRenderer != null)
                for (int i = 0; i < tintRenderer.Length; i++)
                {
                    var piece = tintRenderer[i];
                    if (piece == null) continue;
                    tintApplied[i] = Color.Lerp(tintFrom[i], tintTarget[i], eased);
                    tintEmissionApplied[i] = Color.Lerp(tintEmissionFrom[i], tintEmissionTarget[i], eased);
                    ApplyTint(i, piece);
                }
            if (plantScale != null)
                for (int i = 0; i < plantScale.Length; i++)
                    if (plants[i] != null) plants[i].transform.localScale = Vector3.Lerp(plantFrom[i], plantTarget[i], eased);
            if (replacementScale != null)
                for (int i = 0; i < replacementScale.Length; i++)
                    if (plantReplacements[i] != null) plantReplacements[i].transform.localScale = Vector3.Lerp(replacementFrom[i], replacementTarget[i], eased);
            if (t < 1) return;
            moving = false; completedPreset = session.Preset;
            for (int i = 0; i < voices.Length; i++) if (toVolume[i] == 0) voices[i].Stop();
            if (session.Preset == "baseline") RestoreColorSettings();
            // Hide whichever side of the plant swap settled at zero scale.
            if (plantTarget != null)
                for (int i = 0; i < plantTarget.Length; i++)
                    if (plantTarget[i] == Vector3.zero && plants[i] != null) plants[i].SetActive(false);
            if (replacementTarget != null)
                for (int i = 0; i < replacementTarget.Length; i++)
                    if (replacementTarget[i] == Vector3.zero && plantReplacements[i] != null) plantReplacements[i].SetActive(false);
            breathing = session.Preset == "sadness"; settledAt = Time.unscaledTime;
            if (breathing && sadnessPhase == 0 && introSince < 0) introSince = Time.unscaledTime;
            Trace("transition-complete");
        }
        void SetRoomAmbient(float scale)
        {
            if (scale == ambientScale) return;
            ambientScale = scale;
            RenderSettings.ambientSkyColor = new Color(sky.r * scale, sky.g * scale, sky.b * scale, sky.a);
            RenderSettings.ambientEquatorColor = new Color(equator.r * scale, equator.g * scale, equator.b * scale, equator.a);
            RenderSettings.ambientGroundColor = new Color(ground.r * scale, ground.g * scale, ground.b * scale, ground.a);
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
            if (AdjustRoomAmbient && gameObject.scene == UnityEngine.SceneManagement.SceneManager.GetActiveScene()) SetRoomAmbient(1f);
            RestoreColorSettings(); RestoreTintColors(); RestorePlantSwap(); session.Restore("disabled"); completedPreset = "baseline";
            Trace("disabled");
        }
    }
}
