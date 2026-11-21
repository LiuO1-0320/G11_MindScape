using System;
using UnityEngine;

namespace MindScape.Environment
{
    // Execution boundary for task 3. No API key, network request, or emotion inference lives here.
    public sealed class AmbienceCommandBridge : MonoBehaviour
    {
        [Serializable] public sealed class Command { public string action, preset; }
        AmbienceController controller;
        public void Bind(AmbienceController value) { controller = value; }
        public bool TryProposeJson(string modelOutput, string requestId)
        {
            if (controller == null || string.IsNullOrWhiteSpace(modelOutput) || modelOutput.Length > 4096) return false;
            string json = modelOutput.Trim();
            if (!json.StartsWith("{", StringComparison.Ordinal) || !json.EndsWith("}", StringComparison.Ordinal)) return false;
            try
            {
                var command = JsonUtility.FromJson<Command>(json);
                return command != null && command.action == "propose_ambience"
                    && controller.ProposePreset(command.preset, requestId);
            }
            catch (ArgumentException) { return false; }
        }
        public string GetContextJson() => controller == null ? "{}" : controller.GetStateJson();
    }
}
