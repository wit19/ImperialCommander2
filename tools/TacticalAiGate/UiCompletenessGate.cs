// M13d live gate (zero fixtures): the EMPIRE12 playtest (De, 28.10.) logged
// "LoadUILanguage()::Found missing UI translation [De]" for 20 keys on EVERY
// screen load. DataStore.LoadUILanguage warns exactly when an EN string field
// has no non-empty counterpart in the active language file - so this gate
// pins the shipped-assets contract the same way: EVERY key of the shipped
// En/ui.json must exist and be non-empty in EVERY shipped language ui.json
// (missing count must be 0 for all 11), the 20 playtest keys are additionally
// listed explicitly, and the German reference lines from the ticket are
// pinned verbatim (Thomas: De is the shipped reference translation).
using System;
using System.Collections.Generic;
using System.IO;
using TacticalAI.Core;

public static class UiCompletenessGate
{
    static int fails = 0;
    static void Ok(bool cond, string what)
    {
        Console.WriteLine((cond ? "PASS " : "FAIL ") + what);
        if (!cond) { fails++; Environment.ExitCode = 1; }
    }

    static readonly string App =
        "/home/open/work/ia-ai/ImperialCommander2/ImperialCommander2";

    // The exact 20 keys the EMPIRE12 Player.log named (1 x uiSetup, 19 x uiSettings).
    static readonly Dictionary<string, string[]> TicketKeys = new Dictionary<string, string[]>
    {
        { "uiSetup", new[] { "appMissionFormat" } },
        { "uiSettings", new[] {
            "enemyGroupsColor", "colorRegular", "colorElite", "colorVillain",
            "audioHeader", "uiHeader", "graphicsHeader", "mapperHeader",
            "reset", "pressKeyMessage",
            "mapActivateImperialsLabel", "mapToggleCamViewLabel", "mapToggleMapVisibilityLabel",
            "mapNavForwardLabel", "mapNavBackLabel", "mapNavLeftLabel", "mapNavRightLabel",
            "mapNavCWLabel", "mapNavCCWLabel" } },
    };

    // German Fachsprache reference lines (ticket wording: Vor/Zurueck,
    // im Uhrzeigersinn; key-mapper context for reset/pressKeyMessage).
    static readonly Dictionary<string, string> DeReference = new Dictionary<string, string>
    {
        { "uiSetup.appMissionFormat", "APP-MISSIONSFORMAT:" },
        { "uiSettings.mapperHeader", "TASTENBELEGUNG" },
        { "uiSettings.mapNavForwardLabel", "KARTE VOR" },
        { "uiSettings.mapNavBackLabel", "KARTE ZURUECK" },
        { "uiSettings.mapNavCWLabel", "KAMERA IM UHRZEIGERSINN DREHEN" },
        { "uiSettings.mapNavCCWLabel", "KAMERA GEGEN DEN UHRZEIGERSINN DREHEN" },
        { "uiSettings.reset", "STANDARDBELEGUNG" },
        { "uiSettings.pressKeyMessage", "TASTE DRUECKEN..." },
    };

    public static int Run()
    {
        var langRoot = Path.Combine(App, "Assets", "Resources", "Languages");
        var langs = Directory.GetDirectories(langRoot);
        Array.Sort(langs);
        Ok(langs.Length == 11, "11 shipped language dirs (found " + langs.Length + ")");

        var enFile = Path.Combine(langRoot, "En", "ui.json");
        var enFlat = Flat(Json.AsDict(Json.Parse(File.ReadAllText(enFile))), "");
        Ok(enFlat.Count > 0, "En/ui.json parses flat (" + enFlat.Count + " leaf keys)");

        foreach (var dir in langs)
        {
            var file = Path.Combine(dir, "ui.json");
            if (!File.Exists(file)) { Ok(false, "ui.json exists: " + Path.GetFileName(dir)); continue; }
            var flat = Flat(Json.AsDict(Json.Parse(File.ReadAllText(file))), "");

            // 1) loader mirror: every EN leaf key present + non-empty
            var missing = new List<string>();
            foreach (var kv in enFlat)
            {
                string v;
                if (!flat.TryGetValue(kv.Key, out v) || string.IsNullOrEmpty(v)) missing.Add(kv.Key);
            }
            Ok(missing.Count == 0, Path.GetFileName(dir)
                + " completeness vs En: missing=" + missing.Count
                + (missing.Count > 0 ? " first=" + missing[0] : ""));

            // 2) the 20 EMPIRE12 ticket keys, explicitly
            int ticketMissing = 0;
            foreach (var sec in TicketKeys)
                foreach (var k in sec.Value)
                {
                    string v;
                    if (!flat.TryGetValue(sec.Key + "." + k, out v) || string.IsNullOrEmpty(v)) ticketMissing++;
                }
            Ok(ticketMissing == 0, Path.GetFileName(dir)
                + " EMPIRE12 ticket keys 20/20 present (missing=" + ticketMissing + ")");
        }

        // 3) DE reference lines verbatim
        var de = Json.AsDict(Json.Parse(File.ReadAllText(Path.Combine(langRoot, "De", "ui.json"))));
        var deFlat = Flat(de, "");
        foreach (var kv in DeReference)
        {
            string v;
            deFlat.TryGetValue(kv.Key, out v);
            Ok(v == kv.Value, "De/" + kv.Key + " == \"" + kv.Value + "\" (actual: " + (v ?? "<null>") + ")");
        }

        Console.WriteLine(fails == 0 ? "UiCompletenessGate: ALL PASS" : "UiCompletenessGate: FAILURES=" + fails);
        return fails;
    }

    // flatten "section.key" -> string (ui.json leaves are strings)
    static Dictionary<string, string> Flat(Dictionary<string, object> d, string prefix)
    {
        var outp = new Dictionary<string, string>();
        foreach (var kv in d)
        {
            var path = prefix.Length == 0 ? kv.Key : prefix + "." + kv.Key;
            var sub = kv.Value as Dictionary<string, object>;
            if (sub != null)
                foreach (var inner in Flat(sub, path)) outp[inner.Key] = inner.Value;
            else
                outp[path] = kv.Value as string;
        }
        return outp;
    }
}
