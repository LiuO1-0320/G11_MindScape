using System;
using MindScape.Environment;
public static class SessionChecks
{
    static int count;
    static void Check(bool ok, string name) { if (!ok) throw new Exception(name); count++; }
    public static void Main()
    {
        var s = new AmbienceSession();
        Check(s.Preset == "baseline" && s.Volume == 0, "Initial silence");
        Check(!s.Propose("unknown", "x") && !s.Propose("anger", " "), "Reject invalid proposal");
        Check(!s.Propose("sadness", new string('x', 97)), "Bound correlation ID");
        Check(s.Propose("sadness", "turn1") && s.Preset == "baseline", "Consent before execution");
        Check(s.Propose("anger", "turn2") && !s.Confirm("turn1"), "Stale consent rejected");
        Check(s.Confirm("turn2") && s.Preset == "anger" && s.PendingId == "", "Latest consent accepted");
        Check(s.Brightness == .62f && s.Kelvin == 3500 && s.Volume == .09f, "Anger parameters");
        Check(s.Manual("sadness") && s.Brightness == .72f && s.Kelvin == 2900, "Sadness parameters");
        Check(!s.Manual("baseline") && s.Preset == "sadness", "Unknown manual preset cannot mutate state");
        s.Propose("anger", "turn3"); s.Reject(); Check(!s.Confirm("turn3") && s.Preset == "sadness", "Reject proposal");
        Check(!s.SetBrightness(float.NaN) && !s.SetBrightness(float.PositiveInfinity), "Reject nonfinite brightness");
        Check(s.SetBrightness(99) && s.Brightness == 1.8f, "Brightness upper bound");
        s.Manual("anger"); Check(s.Brightness == 1.8f, "Preserve manual brightness across AI presets");
        s.SetBrightness(-99); Check(s.Brightness == .35f, "Brightness lower bound");
        s.Propose("calm", "turn4"); s.SetBrightness(1.15f); Check(s.PendingId == "", "Feedback invalidates stale suggestion");
        s.SetMuted(true); s.Manual("calm"); Check(s.Muted, "Preserve mute");
        Check(!s.SetSoundLevel(float.NaN), "Reject invalid volume");
        s.SetSoundLevel(-9); Check(s.Volume == 0 && s.SoundLevel == 0, "Silence lower bound");
        s.SetSoundLevel(9); Check(s.SoundLevel == 1, "Volume upper bound");
        s.Restore(); Check(s.Preset == "baseline" && s.Volume == 0 && s.Brightness == 1 && s.Muted, "Restore baseline and preserve mute");
        s.Manual("anger"); Check(s.Brightness == .62f, "Restore clears brightness override");
        s.Propose("calm", "turn5"); s.Restore("disabled");
        Check(s.Source == "disabled" && !s.Confirm("turn5"), "Disable clears stale proposals");
        Console.WriteLine("PASS: " + count + " environment session checks");
    }
}
