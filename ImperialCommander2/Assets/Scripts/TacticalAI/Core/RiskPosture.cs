// Port of sim/risk_posture.py — RiskPosture-Layer (concept 3.5.1, v3.9):
// "Angriff oder Deckung?" for wounded units, driven ONLY by printed/visible
// facts. Four positions (press | hold | guard_hold | disengage), threshold-
// driven, deterministic, no rng. Every decision carries a FALLBACK branch
// with a named Reference Point of the fixture geometry (doors / wall edges /
// objective edges — anchor labels, never coordinates).
// Golden vectors: Assets/Tests/Fixtures/tacai/risk.json.
//
// Kept sim-only: mission-loop wiring (world.risk_mode / risk_log) — this
// file carries the pure decision + the doctrine variant render.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;

namespace TacticalAI.Core
{
    public sealed class RiskFacts
    {
        public string GroupId;               // own group - command authority (§3.3.3 Nr. 2b)
        public string Pattern = "aggressive";
        public string Posture = "eliminate"; // public game state
        public string Counter;               // active VictoryCounter ("woundAll"...) or null
        public Rational Vitality;            // remaining vitality (woundTracker)
        public bool RingNear;                // a hero-ring belief crosses own zone
        public bool OnAnchor = false;        // anchors objective/chokepoint
        public int? ReplacesAt = null;       // printed threat cost to re-buy (3.9)
        public int LedgerBalance = 0;
        public bool MetaBonus = false;       // optional campaign vitality bonus
        public bool VictoryBlocker = false;  // target could block rebel victory
    }

    public sealed class RiskDecision
    {
        public string Position;
        public string Why;
        public string Fallback;              // mandatory branch text
        public string Anchor = "";

        public RiskDecision() { }
        public RiskDecision(string position, string why, string fallback, string anchor = "")
        {
            Position = position; Why = why; Fallback = fallback; Anchor = anchor;
        }

        public Dictionary<string, object> AsDict()
        {
            return new Dictionary<string, object>
            {
                { "risk", Position },
                { "why", Why },
                { "fallback", Fallback },
                { "anchor", Anchor },
            };
        }
    }

    public static class RiskPosture
    {
        public static readonly string[] RiskPositions =
            { "press", "hold", "guard_hold", "disengage" };

        // curated thresholds (documented in docs/balance_sim.md, M2a.3)
        public static readonly Rational Hurt = Rational.One;   // vitality_frac < 1
        public static readonly Rational GuardHoldAt = new Rational(3, 4);
        public static readonly Rational DisengageAt = new Rational(1, 2);
        public static readonly Rational ExtremeAt = new Rational(1, 4);  // rho 1 bar
        public const int EconomyCost = 4;    // replace cost of "valuable"
        public static readonly HashSet<string> ProtectivePostures =
            new HashSet<string> { "protect", "stall" };
        public static readonly HashSet<string> LossOkPostures =
            new HashSet<string> { "eliminate" };

        // ------------------------------------------------------------------
        // lethality priors (curated asset: sim/data/hero_lethality.json /
        // Assets/Tests/Fixtures/tacai/hero_lethality.json)
        // ------------------------------------------------------------------
        public static Dictionary<string, object> LoadHeroLethality(string path) =>
            Json.AsDict(Json.Parse(File.ReadAllText(path, System.Text.Encoding.UTF8)));

        /// hero name -> printed range band tuple [minR, maxR].
        public static Dictionary<string, int[]> HeroBands(string path)
        {
            var doc = LoadHeroLethality(path);
            var outp = new Dictionary<string, int[]>();
            foreach (var h in Json.AsList(doc["heroes"]))
            {
                var rec = Json.AsDict(h);
                var band = Json.AsList(rec["range_band"]);
                outp[Json.AsStr(rec["name"])] = new[] { Json.AsInt(band[0]), Json.AsInt(band[1]) };
            }
            return outp;
        }

        /// hero name -> lethality prior Fraction.
        public static Dictionary<string, Rational> HeroWeights(string path)
        {
            var doc = LoadHeroLethality(path);
            var outp = new Dictionary<string, Rational>();
            foreach (var h in Json.AsList(doc["heroes"]))
            {
                var rec = Json.AsDict(h);
                outp[Json.AsStr(rec["name"])] = ToRational(rec["lethality_prior"]);
            }
            return outp;
        }

        /// Python Fraction(x) parity for JSON values ("n/d" / "n" / int / float).
        public static Rational ToRational(object v)
        {
            switch (v)
            {
                case string s: return Rational.Parse(s);
                case long l: return new Rational(l);
                case int i: return new Rational(i);
                case double d: return FromDouble(d);
                default:
                    throw new InvalidDataException("cannot make a Fraction out of " + v);
            }
        }

        /// Exact binary decomposition == Python Fraction(float).
        public static Rational FromDouble(double d)
        {
            if (d == 0.0) return Rational.Zero;
            long bits = BitConverter.DoubleToInt64Bits(d);
            int exp = (int)((bits >> 52) & 0x7FFL);
            long frac = bits & unchecked(0xFFFFFFFFFFFFFL);
            long sig;
            int e;
            if (exp == 0) { sig = frac; e = -1074; }              // subnormal
            else { sig = frac | 0x10000000000000L; e = exp - 1075; }
            var num = new BigInteger((ulong)sig);
            if (bits < 0) num = -num;
            return e >= 0 ? new Rational(num << e) : new Rational(num, BigInteger.One << -e);
        }

        // ------------------------------------------------------------------
        // named reference points (fixture geometry anchors, label-only)
        // ------------------------------------------------------------------
        /// Deterministic human-legible anchors of a fixture: 'Tür A/B'
        /// (doors), 'Mauerblock N' (blocking/impassable terrain components =
        /// cover edges), 'Objektkante <id>' (objective edges). Returns
        /// [(label, [cells])] in deterministic order; labels only ever leave
        /// this module.
        public static List<(string Label, List<Cell> Cells)> NamedAnchors(Board board)
        {
            var outp = new List<(string, List<Cell>)>();
            var dlabels = Belief.DoorLabels(board);
            var keys = new List<(Cell, Cell)>(dlabels.Keys);
            keys.Sort((a, b) =>
            {
                int c = string.CompareOrdinal(dlabels[a], dlabels[b]);   // str(label)
                return c != 0 ? c : string.CompareOrdinal(Belief.EdgeKeyStr(a), Belief.EdgeKeyStr(b));
            });
            foreach (var k in keys)
                outp.Add((dlabels[k], new List<Cell> { k.Item1, k.Item2 }));

            var cover = new HashSet<Cell>(board.FlaggedCells("blocking"));
            cover.UnionWith(board.FlaggedCells("impassable"));
            var seen = new HashSet<Cell>();
            int n = 0;
            var coverSorted = new List<Cell>(cover);
            coverSorted.Sort();
            foreach (var c in coverSorted)
            {
                if (seen.Contains(c)) continue;
                n++;
                var stack = new List<Cell> { c };
                var comp = new List<Cell>();
                while (stack.Count > 0)
                {
                    var cur = stack[stack.Count - 1];
                    stack.RemoveAt(stack.Count - 1);
                    if (seen.Contains(cur)) continue;
                    seen.Add(cur);
                    comp.Add(cur);
                    foreach (var dv in new[] { new Cell(0, 1), new Cell(0, -1), new Cell(1, 0), new Cell(-1, 0) })
                    {
                        var nb = new Cell(cur.X + dv.X, cur.Y + dv.Y);
                        if (cover.Contains(nb) && !seen.Contains(nb)) stack.Add(nb);
                    }
                }
                comp.Sort();
                outp.Add(("Mauerblock " + n.ToString(CultureInfo.InvariantCulture), comp));
            }
            foreach (var o in board.Objectives)
            {
                var cs = new List<Cell>(o.Cells);
                cs.Sort();
                outp.Add(("Objektkante " + o.Id, cs));
            }
            return outp;
        }

        /// The anchor closest to the group (graph distance, deterministic;
        /// only the LABEL is returned).
        public static string PickAnchor(Board board,
            List<(string Label, List<Cell> Cells)> anchors, IEnumerable<Cell> nearCells)
        {
            var src = new List<Cell>(nearCells);
            string best = null;
            int bestD = 1000000000;
            foreach (var (label, cells) in anchors)
                foreach (var c in cells)
                {
                    int d = FocusResolver.CountSpaces(board, src, new List<Cell> { c });
                    if (d < bestD) { best = label; bestD = d; }
                }
            return best ?? "naechste Deckungskante";
        }

        // ------------------------------------------------------------------
        // decision — threshold cascade (deterministic, no rng)
        // ------------------------------------------------------------------
        public static RiskDecision RiskPosition(RiskFacts f, int rho = 3)
        {
            if (rho <= 0)
                return new RiskDecision("press",
                    "Stufe rho 0: kein Risiko-Urteil — immer Angriff",
                    "falls außer Reichweite: vorrücken auf Konten-Punkt");
            // counters that want damage and victory-blocking targets outrank fear
            if (f.Counter == "woundAll")
                return DecidePress(f, "Zaehler verlangt frischen Schaden — " +
                                      "verwundete Einheiten bleiben im Angriff");
            if (f.VictoryBlocker)
                return DecidePress(f, "Ziel kann Rebellen-Sieg verhindern — " +
                                      "Ausnahmeregelung: volle Konzentration");
            bool hurt = f.Vitality < Hurt;
            if (rho == 1)
            {
                if (f.Vitality > ExtremeAt || !f.RingNear)
                    return DecidePress(f, "rho 1: nur extreme Verwundung bricht den Angriff");
                return DecideDisengage(f, "rho 1: nur extreme Verwundung bricht den Angriff");
            }
            if (!hurt) return DecidePress(f, "Vitalität voll: voller Angriff");
            if (f.RingNear && !LossOkPostures.Contains(f.Posture)
                && f.Vitality <= (!f.MetaBonus ? DisengageAt : GuardHoldAt))
            {
                if (rho >= 3 && f.ReplacesAt is { } rc)
                {
                    if (rc >= EconomyCost && f.LedgerBalance >= rc)
                        return DecideDisengage(f, "ökonomisch wertvoll (Wiederbeschaffung "
                            + rc.ToString(CultureInfo.InvariantCulture) + ") — Körper schützen");
                    // S5: expensive-to-replace logic ran and said NO
                    return Decide(f, "hold", "Verlust tragbar — Stellung halten statt weichen");
                }
                return DecideDisengage(f, "verwundet + Heldenring in Reichweite + " +
                                          "Posture verbietet Verluste");
            }
            if (ProtectivePostures.Contains(f.Posture) && f.OnAnchor
                && f.Vitality <= GuardHoldAt)
                return Decide(f, "guard_hold", "Objekt/Engstelle ist Anker — als Anker halten, " +
                                               "nicht in Deckung verschwinden");
            if (f.RingNear)
                return Decide(f, "hold", "verwundet, Gegner geortet — " +
                                         "Feuerlinie halten, sicherer Treffer");
            return Decide(f, "hold", "verwundet, Gegner nicht geortet — " +
                                     "Stellung mit Deckungsvorteil halten");
        }

        static RiskDecision DecidePress(RiskFacts f, string reason) =>
            new RiskDecision("press", "Press: " + reason,
                "falls außer Reichweite: auf Konten-Punkt vorrücken");

        static RiskDecision Decide(RiskFacts f, string pos, string reason)
        {
            var fb = Fallbacks[pos];
            return new RiskDecision(pos, pos + ": " + reason, fb.Text, fb.Anchor);
        }

        static RiskDecision DecideDisengage(RiskFacts f, string reason) =>
            new RiskDecision("disengage", "disengage: " + reason,
                "hinter der nächsten Deckungskante in Feuerhaltung; " +
                "falls blockiert: eigene Zone halten bis Verbündete naht",
                "Deckungskante");

        struct Fallback { public string Text; public string Anchor; public Fallback(string t, string a) { Text = t; Anchor = a; } }

        static readonly Dictionary<string, Fallback> Fallbacks =
            new Dictionary<string, Fallback>
        {
            { "hold", new Fallback(
                "falls Ziel gleich außer Feuerlinie: Kurzer Vorstoß auf die nächste Objektkante",
                "Objektkante") },
            { "guard_hold", new Fallback(
                "falls verdrängt: Rückzug an die nächste Tür/Deckungskante, Feuerlinie auf das Objekt halten",
                "Tuer") },
        };

        // ------------------------------------------------------------------
        // doctrine variant rendering (selection densification only!)
        // ------------------------------------------------------------------
        /// Render the doctrine VARIANT for the round: identical candidate
        /// vocabulary (the Optionsmengen-Diff stays empty — this layer only
        /// densifies WHICH clause is spoken, never which actions are legal),
        /// the Fallback-Zweig clause is appended to the card text and the
        /// why-text.
        public static Doctrine RiskVariant(Doctrine doctrine, RiskDecision decision)
        {
            var d = doctrine.Clone();
            string fallback = decision.Fallback;
            if (decision.Position == "press")
                fallback = "falls außer Reichweite: rücke auf Konten-Punkt vor; " +
                           "falls Ziel außer Feuerlinie: Deckungskante halten";
            d.Why = doctrine.Why + " | Risiko " + decision.Position + ": " + decision.Why
                    + " | Fallback: " + fallback;
            d.CardText = doctrine.CardText + " | Risiko: " + fallback;
            d.Risk = decision.Position;          // informational
            d.RiskFallback = fallback;           // mandatory branch (Pflichttest iii)
            return d;
        }

        static readonly Dictionary<string, int> LevelRho = new Dictionary<string, int>
        {
            { "S1", 0 }, { "S2", 1 }, { "S3", 2 }, { "S4", 2 }, { "S5", 3 },
        };

        /// rho from the T2b SkillProfile (attribute if pinned, else level
        /// map). Python's getattr(profile, "rho", None) ALWAYS finds the
        /// pinned Rho knob on a SkillProfile (level strings have no
        /// attribute and fall back to the map), and the pinned values match
        /// the map for S1..S5.
        public static int RhoOf(SkillProfile profile) =>
            profile is null ? 0 : (int)profile.Rho;

        public static int RhoOf(string level) =>
            LevelRho.TryGetValue(level ?? "", out var r) ? r : 0;
    }
}
