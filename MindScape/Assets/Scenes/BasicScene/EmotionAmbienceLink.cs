using System;
using UnityEngine;

// Task 3 execution seam: turns each finished dialogue turn into a debounced,
// consent-gated ambience proposal. Classification failures never break the dialogue.
internal sealed class EmotionAmbienceLink
{
    readonly MindScape.Environment.EmotionDecision decision = new MindScape.Environment.EmotionDecision();
    bool busy, queued;
    [Serializable] sealed class Command { public string action, preset; }

    public void HandleTurn(SiliconFlowChat cloud, Func<bool> active)
    {
        // While a classification is in flight, remember the turn instead of dropping it;
        // the queued pass reads the latest shared history, so rapid turns are coalesced.
        if (busy) { queued = true; return; }
        if (cloud == null || !cloud.Ready || !active()) return;
        Run(cloud, active);
    }
    async void Run(SiliconFlowChat cloud, Func<bool> active)
    {
        busy = true;
        try
        {
            do
            {
                queued = false;
                string json = await cloud.ClassifyEmotion(active);
                if (!active()) return;
                Command command = null;
                if (!string.IsNullOrEmpty(json))
                    try { command = JsonUtility.FromJson<Command>(json); } catch (ArgumentException) { }
                if (command == null || command.action != "propose_ambience") continue;
                var controller = MindScape.Environment.AmbienceController.Instance;
                var bridge = controller != null
                    ? controller.GetComponent<MindScape.Environment.AmbienceCommandBridge>() : null;
                if (bridge == null) continue;
                string proposal = decision.Consume(command.preset, controller.CurrentPreset);
                Debug.Log("[MindScape.Environment] Emotion turn: label=" + command.preset
                    + " proposal=" + (proposal ?? "none") + " streak=" + decision.Streak);
                if (string.IsNullOrEmpty(proposal)) continue;
                bridge.TryProposeJson("{\"action\":\"propose_ambience\",\"preset\":\"" + proposal + "\"}",
                    "emo-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            } while (queued && active());
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { Debug.LogWarning("[MindScape.Environment] Emotion classification skipped: " + error.Message); }
        finally { busy = false; queued = false; }
    }
}
