// Port of sim/doctrine.py — Engagement-Doktrinen (concept §3.3.1 + §3.3.3
// v3.6, M2a.2). A doctrine = ordered target rule + geographic Konten-Punkt,
// rendered as card text. SELECTION (SelectDoctrine) is the App's layer: it
// sees ONLY the AppView (posture + counter + pattern + zone belief +
// geometry) — never rebel positions. EXECUTION (candidate filters on board
// truth) emulates the human applying the card on the real board; the
// candidate set is handed to the focus layer as world.FocusCandidates
// (tiebreaks stay the GH cascade §3.3.2, printed as card footnote).
// All German card text is BYTE-EXACT (App hand-off).
// Golden vectors: Assets/Tests/Fixtures/tacai/doctrine.json.
//
// Kept sim-only: the theater scenario harness (load_theater_scenarios /
// run_theater_scenario) — the C# test harness drives it directly.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace TacticalAI.Core
{
    public sealed class Doctrine
    {
        // GH p.39-40 Tiebreak-Kaskade — Fußzeile JEDER Doktrin (§3.3.2; der
        // Mensch löst Gleichstände lokal auf dem Brett, die App behauptet
        // nichts).
        public const string GhCascadeFootnote =
            "Gleichstands-Kaskade (GH S.39-40): 1) wenigeste Bewegungspunkte, " +
            "2) kürzeste Distanz nach Geländeregeln, 3) Missionsrollen-Gewicht, " +
            "4) frühere Aktivierungsreihenfolge, 5) geringere Vitalität.";

        // Path-Priority-Klausel als Doktrin-Regeltext (§3.3.1
        // Auslieferungspunkt).
        public const string PathPriorityClause =
            "Bewegung nur innerhalb der gedruckten Grenzen " +
            "(Speed, Reichweite, Legalität); Overwatch-Felder " +
            "meiden außer alternativlos.";

        // Zielregeln: id -> Kartentext
        public static readonly Dictionary<string, string> RuleText =
            new Dictionary<string, string>
        {
            { "gewinntraeger", "Zuerst jeder Rebell, der ein Auftragsziel berührt " +
                               "oder vor einer offenen Flucht steht — " +
                               "Gewinnverhinderung sticht jeden Zähler" },
            { "gesunde_zuerst", "Dann gesunde Rebellen vor bereits Verwundeten: " +
                                "jeder Treffer zählt und Zähler-Fortschritt ist " +
                                "unumkehrbar" },
            { "verwundete", "Dann der nächste bereits verwundete Rebell" },
            { "killbare", "Zuerst Rebellen, deren sichtbare Vitalität vom gedruckten " +
                          "Schaden erreicht wird — niedrigste Vitalität zuerst" },
            { "heiler_hammer", "Innerhalb des Zählers: Heiler vor " +
                               "Schadensschwerpunkt, dann Rest" },
            { "naechster", "Sonst der nächste Rebell (Entscheidung nach Kaskade)" },
            { "timer_bediener", "Angriff nur gegen Rebell(en), die das Terminal " +
                                "{terminal} unmittelbar erreichen — sonst Knoten " +
                                "besetzen statt Angriff" },
            { "objektbedroher", "Zuerst jeder Rebell, der {schutz} bedroht" },
            { "objektnaehe", "Dann der nächste Rebell in Richtung des Schutzobjekts" },
            { "laeufer_abfangen", "Fange den Rebellen mit dem kürzesten Weg zum " +
                                  "Verwehrziel {deny} ab" },
        };

        public static readonly Dictionary<string, string> StanceText =
            new Dictionary<string, string>
        {
            { "aggressive", "Vorstoß zum Kontenpunkt" },
            { "guard", "Wachzone halten; nur eigene Linie räumen, nicht locken lassen" },
            { "blocker", "Engstelle halten, Angreifende abfangen" },
            { "hunter", "Schwerpunktzone abkämmen" },
            { "support", "Abstand halten und aus der Deckung angreifen" },
            { "ambusher", "Kontenpunkt versperren, bis eine Zielperson in Reichweite " +
                          "ist; zuvor kein Schritt in Sichtlinien" },
        };

        // Regeln ohne Brett-Filter sind reine Text-/Wertlage-Regeln: die
        // Reihenfolge innerhalb ihres Kandidatenfelds erledigen W (Zähler)
        // und die GH-Kaskade — der Filter lässt dann alles durch
        // (fehlt = ungefiltert).
        static readonly Dictionary<string, Func<World, Figure, Figure, Doctrine, bool>> RuleFilters =
            new Dictionary<string, Func<World, Figure, Figure, Doctrine, bool>>
        {
            { "gewinntraeger", FBearer },
            { "gesunde_zuerst", FHealthy },
            { "verwundete", FWounded },
            { "killbare", FKillable },
            { "timer_bediener", FTimerImmediate },
            { "objektbedroher", FObjectThreatener },
            { "laeufer_abfangen", FDenyRunner },
        };

        // ------------------------------------------------------------------
        public string Posture;
        public string Counter;
        public bool Meta;
        public string Pattern;
        public List<string> Rules;
        public Dictionary<string, object> Move;   // {zone, door, path, targets}
        public List<string> Refs;
        public string Stance;
        public int RoundNo;
        public string Why;
        public List<string> DenyIds;
        public List<string> ProtectIds;
        public string CardText;

        // Planning horizon (Konzept §6): TimerWindow null = race maths are
        // not run (Skill S1, purely reactive); 2 = proactive window
        // (S4/S5). Unset = reference level (window of 1 round). The mission
        // layer assigns skill.TimerWindow() onto the doctrine.
        public int? TimerWindow = 1;

        // RiskPosture layer fields (set by RiskPosture.RiskVariant):
        public string Risk;               // informational
        public string RiskFallback;       // mandatory branch (Pflichttest iii)

        public Doctrine(string posture, string counter, bool meta, string pattern,
                        IEnumerable<string> rules, Dictionary<string, object> move,
                        IEnumerable<string> refs, string stance, int roundNo, string why,
                        IEnumerable<string> denyIds = null, IEnumerable<string> protectIds = null)
        {
            Posture = posture;
            Counter = counter;
            Meta = meta;
            Pattern = pattern;
            Rules = new List<string>(rules);
            Move = move;
            Refs = new List<string>(refs);
            Stance = stance;
            RoundNo = roundNo;
            Why = why;
            DenyIds = new List<string>(denyIds ?? Array.Empty<string>());
            ProtectIds = new List<string>(protectIds ?? Array.Empty<string>());
            CardText = Render();
        }

        /// Shallow copy (Python copy.copy) for the risk variant render.
        public Doctrine Clone()
        {
            var d = (Doctrine)MemberwiseClone();
            d.Rules = Rules;          // shared like Python shallow copy
            d.Refs = Refs;
            d.Move = Move;
            d.DenyIds = DenyIds;
            d.ProtectIds = ProtectIds;
            return d;
        }

        // ------------------------------------------------------------------
        string Render()
        {
            var targets = Move.TryGetValue("targets", out var t) ? (List<string>)t : null;
            targets = targets ?? new List<string>();
            string named = string.Join(", ", targets);
            var placeholders = new Dictionary<string, string>
            {
                { "{terminal}", named.Length > 0 ? named : "das Terminal" },
                { "{schutz}", named.Length > 0 ? named : "das Schutzobjekt" },
                { "{deny}", named.Length > 0 ? named : "das Verwehrziel" },
            };
            var lines = new List<string>
            {
                "Engagement-Doktrin — Posture " + Cap(Posture) + ", Zähler " + Counter
                + (Meta ? ", Kampagnen-Meta" : "")
                + ", Muster " + Pattern + ".",
            };
            var goalBits = new List<string>();
            foreach (var rid in Rules)
            {
                string text = RuleText[rid];
                foreach (var kv in placeholders) text = text.Replace(kv.Key, kv.Value);
                goalBits.Add(text);
            }
            lines.Add("Zielregel: " + string.Join("; ", goalBits) + ".");
            string move = Stance;
            if (Move.TryGetValue("door", out var door) && door is string ds && ds.Length > 0)
                move += " über " + ds;
            if (Move.TryGetValue("zone", out var zone) && zone is string zs2 && zs2.Length > 0)
                move += " in den/ins " + zs2;
            move += ". Halte von dort Reichweitenrand zum Ziel.";
            lines.Add("Bewegung: " + move);
            lines.Add(PathPriorityClause);
            lines.Add(GhCascadeFootnote);
            return string.Join("\n", lines);
        }

        static string Cap(string s) =>
            string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        public Dictionary<string, object> AsDict()
        {
            return new Dictionary<string, object>
            {
                { "posture", Posture },
                { "counter", Counter },
                { "meta", Meta },
                { "pattern", Pattern },
                { "rules", new List<string>(Rules) },
                { "move", new Dictionary<string, object>(Move) },
                { "refs", new List<string>(Refs) },
                { "stance", Stance },
                { "round", RoundNo },
                { "why", Why },
                { "card_text", CardText },
                { "footnote", GhCascadeFootnote },
            };
        }

        // ---- execution layer (emulates the human applying the card) ------
        /// Ordered rule application on board truth: the FIRST rule with a
        /// non-empty result fixes the candidate set; the focus cascade
        /// orders within it. null = unfiltered (rules are value-ordering
        /// only).
        ///
        /// Skill δ (Konzept §6): a counter-blind profile (S1/S2) does not
        /// read the counter semantics — counter rules are skipped during
        /// execution (SkillProfile.RuleGated), never the legal candidates.
        public List<string> Candidates(World world, Figure fig)
        {
            var rebels = new List<Figure>();
            foreach (var r in world.Figures)
                if (r.Alive && r.Team != fig.Team && r.Team != "npc") rebels.Add(r);
            if (rebels.Count == 0) return null;
            foreach (var rid in Rules)
            {
                if (!RuleFilters.TryGetValue(rid, out var f)) continue;
                if (world.Skill != null && world.Skill.RuleGated(rid)) continue;
                var sel = new List<string>();
                foreach (var r in rebels)
                    if (f(world, fig, r, this)) sel.Add(r.Id);
                sel.Sort(StringComparer.Ordinal);
                if (sel.Count > 0) return sel;
            }
            return null;
        }

        // ------------------------------------------------------- filters
        static bool FBearer(World w, Figure fig, Figure r, Doctrine d) =>
            VictoryHelpers.VictoryBearer(w, r);

        static bool FHealthy(World w, Figure fig, Figure r, Doctrine d) =>
            !VictoryHelpers.IsWounded(r);

        static bool FWounded(World w, Figure fig, Figure r, Doctrine d) =>
            VictoryHelpers.IsWounded(r);

        // M9-F6: 'killbare' = ERLEDBAR this activation (wounded STATUS +
        // printed EV >= printed bar). Healthy-EV>=bar heroes are only
        // 'verwundbar' and no longer match. FINAL-QA audit #6 exact EV
        // stays inside VictoryHelpers.Killable (mirror of sim _f_killable).
        static bool FKillable(World w, Figure fig, Figure r, Doctrine d) =>
            VictoryHelpers.Killable(fig, r);

        static bool FTimerImmediate(World w, Figure fig, Figure r, Doctrine d)
        {
            int? win = d.TimerWindow;   // missing attribute == reference 1
            if (win is null) return false;
            foreach (var (id, cells, left) in VictoryHelpers.OpenTimerObjectives(w))
                if (VictoryHelpers.ArrivalRounds(w, r, cells, left) <= win.Value) return true;
            return false;
        }

        static bool FObjectThreatener(World w, Figure fig, Figure r, Doctrine d)
        {
            foreach (var e in w.Board.ProtectedEntities)
            {
                var cells = VictoryHelpers.EntityCells(e);
                if (cells.Count > 0 && VictoryHelpers.Threatens(w, r, cells)) return true;
            }
            return false;
        }

        static bool FDenyRunner(World w, Figure fig, Figure r, Doctrine d)
        {
            var rt = w.Objectives;
            if (rt is null) return false;
            foreach (var oid in d.DenyIds)
            {
                var o = rt.ById(oid);
                if (o != null && !o.Done
                    && VictoryHelpers.ArrivalRounds(w, r, o.Cells, o.InteractionsLeft) < 1000000)
                    return true;
            }
            return false;
        }

        // ------------------------------------------------------ selection
        /// Selection — AppView ONLY (type boundary).
        public static Doctrine SelectDoctrine(AppView view, string pattern = "aggressive",
                                              IEnumerable<string> denyIds = null)
        {
            AppView.AssertAppView(view);
            string posture = string.IsNullOrEmpty(view.Posture) ? "eliminate" : view.Posture;
            string counter = "incapacitateAll";
            if (view.Counter != null)
            {
                var vc = view.Counter.TryGetValue("victoryCounter", out var vcv) ? vcv as string : null;
                if (!string.IsNullOrEmpty(vc)) counter = vc;
            }
            bool meta = view.Counter != null
                && view.Counter.TryGetValue("campaignMetaBonus", out var mb) && mb is true;

            var rules = new List<string> { "gewinntraeger" };
            switch (posture)
            {
                case "eliminate":
                    if (counter == "woundAll") rules.AddRange(new[] { "gesunde_zuerst", "verwundete" });
                    else if (counter == "incapacitateAll" || counter == "capture") rules.Add("killbare");
                    rules.AddRange(new[] { "heiler_hammer", "naechster" });
                    break;
                case "stall":
                    rules.AddRange(new[] { "timer_bediener", "heiler_hammer", "naechster" });
                    break;
                case "protect":
                    rules.AddRange(new[] { "objektbedroher", "objektnaehe" });
                    break;
                case "deny":
                    rules.AddRange(new[] { "laeufer_abfangen", "heiler_hammer", "naechster" });
                    break;
            }

            Dictionary<string, object> contest = new Dictionary<string, object>();
            if (view.Contest != null && view.Contest.TryGetValue(posture, out var c)) contest = c;
            object ContestVal(string key) => contest.TryGetValue(key, out var v) ? v : null;
            string zone = ContestVal("zone") as string;
            string door = ContestVal("door") as string;
            var path = (List<string>)(ContestVal("path")) ?? new List<string>();
            var targets = (List<string>)(ContestVal("targets")) ?? new List<string>();
            var move = new Dictionary<string, object>
            {
                { "zone", zone },
                { "door", door },
                { "path", new List<string>(path) },
                { "targets", new List<string>(targets) },
            };

            var refs = new List<string>();
            if (view.Objectives != null)
                foreach (var o in view.Objectives)
                {
                    bool done = o.TryGetValue("done", out var dv) && dv is true;
                    string oZone = o.TryGetValue("zone", out var zv) ? zv as string : null;
                    string oId = (string)o["id"];
                    if (!done && (oZone == zone || targets.Contains(oId))) refs.Add(oId);
                }
            foreach (var t in targets) if (!refs.Contains(t)) refs.Add(t);
            if (!string.IsNullOrEmpty(door)) refs.Add(door);
            if (!string.IsNullOrEmpty(zone)) refs.Add(zone);

            string stance = StanceText.TryGetValue(pattern, out var st) ? st : StanceText["aggressive"];
            var denyList = new List<string>(denyIds ?? Array.Empty<string>());
            if (denyList.Count == 0) denyList = (view.DenyIds != null && view.DenyIds.Count > 0) ? view.DenyIds : denyList;
            var why = "Doktrin-Auswahl Runde " + view.RoundNo.ToString(CultureInfo.InvariantCulture)
                      + ": Posture " + posture + " (" + PyStr(view.PostureReason) + "), Zähler " + counter
                      + (meta ? " mit Kampagnen-Meta" : "")
                      + ", Muster " + pattern + ", Zonen-Schwerpunkt "
                      + (!string.IsNullOrEmpty(view.FocusZone) ? view.FocusZone : "—")
                      + ", Kontenpunkt "
                      + (!string.IsNullOrEmpty(door) ? door
                         : (!string.IsNullOrEmpty(zone) ? zone : "—"));
            return new Doctrine(posture, counter, meta, pattern, rules, move, refs,
                stance, view.RoundNo, why,
                denyIds: denyList,
                protectIds: view.ProtectIds ?? new List<string>());
        }

        /// Python f-string None rendering ("None"), for byte-exact why text.
        static string PyStr(object v) => v switch
        {
            null => "None",
            bool b => b ? "True" : "False",
            _ => Convert.ToString(v, CultureInfo.InvariantCulture),
        };

        // ----------------------------------------------------- leak scanner
        static readonly Regex CoordRe =
            new Regex(@"\(\s*\d+\s*,\s*\d+\s*\)|\[\s*\d+\s*,\s*\d+\s*\]");

        /// Every '(x, y)' / '[x, y]' occurrence inside TEXT fields of a
        /// doctrine payload — the leak scanner for Pflichttest (i).
        public static List<string> FindCoordinates(object payload)
        {
            var hits = new List<string>();
            Walk(payload, hits);
            return hits;
        }

        static void Walk(object x, List<string> hits)
        {
            switch (x)
            {
                case string s:
                    foreach (Match m in CoordRe.Matches(s)) hits.Add(m.Value);
                    break;
                case IDictionary<string, object> dict:
                    foreach (var v in dict.Values) Walk(v, hits);
                    break;
                case System.Collections.IEnumerable en when !(x is string):
                    foreach (var v in en) Walk(v, hits);
                    break;
            }
        }
    }
}
