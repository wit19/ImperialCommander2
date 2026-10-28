using System;
using TacticalAI;
class P {
    static void Eq(object a, object b, string m) { if (!Equals(a, b)) { Console.WriteLine("FAIL: " + m + " (" + a + " != " + b + ")"); Environment.ExitCode = 1; } else Console.WriteLine("ok: " + m); }
    static void Main() {
        TacticalAISettings.ResetDefaults();
        Eq("reference", TacticalAISettings.Mode, "S1 derives reference");
        TacticalAISettings.Enabled = true;
        // dev 21 cycle
        string t = "";
        var seen = new System.Collections.Generic.List<string>();
        for (int i = 0; i < 8; i++) { t = TacticalAISettings.NextMissionToken(t); seen.Add(t); }
        Eq("off,S1,S2,S3,S4,S5,,off", string.Join(",", seen), "cycle AUTO->OFF->S1..S5->AUTO->OFF");
        Eq("legacy", TacticalAISettings.EffectiveMode("off"), "OFF badge plays legacy");
        Eq("reference", TacticalAISettings.EffectiveMode("S2"), "S2 -> reference");
        Eq("overlord", TacticalAISettings.EffectiveMode("S4"), "S4 -> overlord");
        Eq("S2", TacticalAISettings.EffectiveSkill("S2"), "rung pins skill");
        TacticalAISettings.SkillLevel = "S3";
        Eq("S3", TacticalAISettings.EffectiveSkill(""), "AUTO follows global");
        Eq("overlord", TacticalAISettings.EffectiveMode("overlord"), "deprecated overlord playable");
        Eq("reference", TacticalAISettings.EffectiveMode("reference"), "deprecated reference playable");
        Eq("legacy", TacticalAISettings.EffectiveMode("legacy"), "deprecated legacy playable");
        Eq(TacticalAISettings.NextMissionToken("bogus"), "off", "unknown -> OFF");
        WorldFeedGate.Run();
        OverlordGate.Run();
        ImperialDeckGate.Run();
        ImperialPurchaseGate.Run();
        ImperialCardEffectsGate.Run();
        AgendaGate.Run();
        ActionTreeGate.Run();
        Console.WriteLine(Environment.ExitCode == 0 ? "ALL PASS" : "FAILURES");
    }
}
