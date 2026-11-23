namespace MindScape.Environment
{
    // No Unity dependency: proposal policy only. Emotion inference itself lives with the LLM caller.
    public sealed class EmotionDecision
    {
        public string LastClassification { get; private set; } = "";
        public int Streak { get; private set; }
        const int RequiredStreak = 2;
        // Debounced consent trigger: baseline and unknown labels never propose, and the
        // active preset is never re-proposed. A cold start needs two agreeing turns, but
        // a clear different emotion while a preset is already active switches at once —
        // sudden shifts are exactly when the room should keep up, and consent still gates it.
        public string Consume(string classification, string currentPreset)
        {
            if (!AmbienceSession.IsPreset(classification)) { Streak = 0; LastClassification = ""; return null; }
            if (classification == currentPreset) { Streak = 0; LastClassification = ""; return null; }
            if (currentPreset != "baseline")
            { Streak = 0; LastClassification = ""; return classification; }
            if (classification != LastClassification) { LastClassification = classification; Streak = 1; return null; }
            if (++Streak < RequiredStreak) return null;
            Streak = 0; LastClassification = "";
            return classification;
        }
    }
}
