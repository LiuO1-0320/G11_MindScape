using System;
using MindScape.Environment;

internal sealed class EmotionAmbienceLink
{
    readonly EmotionDecision decision = new EmotionDecision();

    public void HandleTurn(string emotion)
    {
        var controller = AmbienceController.Instance;
        if (controller == null) return;
        string proposal = decision.Consume(emotion, controller.CurrentPreset);
        if (string.IsNullOrEmpty(proposal)) return;
        var bridge = controller.GetComponent<AmbienceCommandBridge>();
        if (bridge != null)
            bridge.TryProposeJson("{\"action\":\"propose_ambience\",\"preset\":\"" + proposal + "\"}",
                "emo-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }
}
