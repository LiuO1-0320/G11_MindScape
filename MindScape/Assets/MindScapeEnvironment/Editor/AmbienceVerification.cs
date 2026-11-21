using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace MindScape.Environment.Editor
{
    public static class AmbienceVerification
    {
        static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
        static void Complete(AmbienceController c)
        {
            typeof(AmbienceController).GetField("started", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(c, Time.unscaledTime - 3f);
            typeof(AmbienceController).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(c, null);
        }
        [MenuItem("MindScape/Verify Environment Module")]
        public static void Run()
        {
            var root = new GameObject("Environment verification");
            try
            {
                var lamp = root.AddComponent<Light>(); lamp.intensity = 2; lamp.color = Color.white;
                var c = root.AddComponent<AmbienceController>(); c.lights = new[] { lamp };
                c.sadnessSound = Resources.Load<AudioClip>("MindScapeAmbience/SoftRain");
                c.angerSound = Resources.Load<AudioClip>("MindScapeAmbience/QuietStream");
                c.calmSound = Resources.Load<AudioClip>("MindScapeAmbience/GentleAir");
                Check(c.sadnessSound && c.angerSound && c.calmSound, "Missing ambience audio");
                c.Initialize();
                Check(!c.ProposePreset("unknown", "t0"), "Unknown preset accepted");
                Check(!c.ProposePreset("anger", ""), "Empty request accepted");
                Check(c.ProposePreset("sadness", "t1"), "Valid proposal rejected");
                Check(lamp.intensity == 2 && c.CurrentPreset == "baseline", "Proposal changed environment before consent");
                c.ConfirmProposal(); Complete(c);
                Check(Mathf.Abs(lamp.intensity - 1.44f) < .001f, "Sadness intensity incorrect");
                Check(c.CurrentPreset == "sadness" && c.PendingPreset == "", "Confirmation state incorrect");
                c.ProposePreset("anger", "t2"); c.RejectProposal();
                Check(c.CurrentPreset == "sadness", "Rejected proposal applied");
                c.ApplyManual("anger"); Complete(c);
                Check(Mathf.Abs(lamp.intensity - 1.24f) < .001f, "Anger intensity incorrect");
                c.ApplyManual("sadness"); c.ApplyManual("calm"); Complete(c);
                Check(c.CurrentPreset == "calm" && Mathf.Abs(lamp.intensity - 2f) < .001f, "Interrupted transition failed");
                c.SetMuted(true); c.ApplyManual("anger"); Complete(c);
                Check(c.Muted, "Preset overrode mute preference");
                c.SetBrightness(float.NaN); c.SetBrightness(999); Complete(c);
                Check(Mathf.Abs(lamp.intensity - 2.5f) < .001f, "Brightness bounds failed");
                c.RestoreBaseline(); Complete(c);
                Check(lamp.color == Color.white && lamp.intensity == 2, "Baseline did not restore");
                var state = JsonUtility.FromJson<AmbienceController.State>(c.GetStateJson());
                Check(state.preset == "baseline" && !state.transitioning, "Feedback state incorrect");
                Debug.Log("MINDSCAPE_ENVIRONMENT_VERIFICATION_PASS");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
    }
}
