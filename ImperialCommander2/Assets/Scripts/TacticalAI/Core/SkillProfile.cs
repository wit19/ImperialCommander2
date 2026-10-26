// Port of sim/skill.py — Skill-Skala S1-S5: Schwierigkeit = Spielstaerke
// (Konzept §6, v3.7-Umbau).
//
// Grundsatz (Thomas 2026-10-26): Aufstellungskontingente und Threat-Budget
// sind Missionsdesign, KEINE KI-Stellschraube. Alle Skill-Profile operieren
// auf der IDENTISCHEN legalen Optionsmenge; der Skill veraendert
// ausschliesslich die AUSWAHL innerhalb legaler Optionen (Rangwahl-Band,
// Filter, Verzug) — nie Aktionsbudget, Reichweiten, Stats, Karten-Specials
// oder Informationszugang.
//
// Parameter (Konzept §6 Tabelle, Karte M2b.1):
//
//   delta  Fokus-Disziplin   — Rangband aus der W-/Kaskaden-Rangliste;
//                              S1/S2 sind counter-blind (gesunde/verwundete/
//                              Killbare-Zaehler werden NICHT gelesen -> auch
//                              kein Volley-/Focus-Fire), Rangwahl ist ein
//                              Draw aus dem Missions-RNG.
//   h      Planungs-Horizont — Timer-/Renn-Rechnung: h=1 reaktiv
//                              (Rennrechnung ignoriert), h=2 Fenster 1 Runde
//                              (Referenzniveau), h=3 proaktiv (Fenster 2
//                              Runden, Intercept-Vorwegnahme).
//   beta   Glaubens-Traegheit — Event-Feed-Verzug in Runden (S1/S2: 2, S3: 1,
//                              S4/S5: 0); S1 verarbeitet Ereignisse
//                              zusaetzlich halbiert (Hitze-Delta x 1/2).
//   pi     Positionierungs-Guete — "first" = erster legaler Kandidat,
//                              "hillclimb" = gieriger Schritt statt
//                              Wegplanung, "noavoid" = kürzester Weg ohne
//                              Overwatch-Meiden, "full" = Referenzplaner
//                              (LOS/Deckung/Meiden).
//   theta  Ökonomie           — Sparziel (save_rounds), Anti-Stau-Schwelle,
//                              Gier-Dump (sofort alles zahlbare), Rauschen
//                              (zoegernd/impulsiv) auf dem Sparpfad.
//
// Determinismus: alle Fehler sind Draws aus einem EIGENEN, aus dem
// Missions-Seed abgeleiteten RNG — der Referenz-RNG (Shuffle/Wuerfel) wird
// vom Skill-Layer NICHT angefasst, dadurch bleibt der skill=None-Pfad
// bitgenau das T1/T2-Referenzverhalten. Gleicher Seed + gleiche Stufe =>
// identischer Log-Hash. (The Python make_skill_rng derives the stream from
// random.Random("tacai-skill|fixture|level|seed|level") — MT19937 stays in
// the exporter; the C# side receives the stream through the IRandom seam.)
using System;
using System.Collections.Generic;

namespace TacticalAI.Core
{
    public class SkillSpecError : ArgumentException
    {
        public SkillSpecError(string message) : base(message) { }
    }

    public sealed class SkillProfile
    {
        public string Level;                 // "S1".."S5"
        // ---- delta Fokus-Disziplin --------------------------------------------
        public int RankLo = 1;               // Rangband (1-basiert, wird an Zeilenzahl geklemmt)
        public int RankHi = 1;               // S1: 2..4, S3: 1..2, S5: 1..1
        public double MissRate = 0.0;        // seltener Aussetzer: Band bis MissHi verbreitern
        public int MissHi = 1;
        public bool CounterBlind = false;    // Counter-Semantik (gesund/verwundet/killbar)
                                             // wird nicht gelesen -> kein Volley/Focus-Fire
        // ---- h Planungs-Horizont ----------------------------------------------
        public int Horizon = 2;              // 1/2/3; Timer-Fenster: None/1/2 Runden
        // ---- beta Event-Feed --------------------------------------------------
        public int FeedDelay = 0;            // Runden Verzug
        public bool FeedHalve = false;       // S1: Ereignisse nur halbiert verarbeitet
        // ---- pi Positionierung -------------------------------------------------
        public string Positioning = "full";  // first | hillclimb | noavoid | full
        // ---- theta Ökonomie ----------------------------------------------------
        public int EconSave = 2;             // save_rounds des ThreatLedger
        public int EconIdle = 2;             // force_after_idle
        public double EconNoise = 0.0;       // S3: Sparregeln mit Rauschen (je 12 %
                                             // zoegernd / impulsiv, Draw aus dem Skill-RNG)
        public bool EconDump = false;        // S1: "verschenkt Threat sofort greedy" —
                                             // alle zahlbaren Gruppen pro Runde (alte App)
        // ---- rho Risiko-Urteil (M2a.3, Konzept §3.5.1/§6) ----------------------
        public int Rho = 3;                  // 0=immer press (S1) | 1=nur Extremwunde (S2)
                                             // 2=Schwellen ohne Ökonomie (S3/S4)
                                             // 3=voll inkl. Threat-Wert + Kampagnen-Bonus (S5)
                                             // Nur Auswahlverdichtung — Action-Legalitaet
                                             // bleibt ausschliesslich beim Runner-Profil-A-
                                             // Validator (Optionsmengen-Diff == leer).

        // ---------------------------------------------------------------- draws
        /// Draw a 1-based rank from the handicap band (clamped to the list).
        ///
        /// Two draws per call, ALWAYS (uniform stream, auch bei Band (1,1)):
        /// erst der Aussetzer-Wurf, dann die Rangwahl — so ist der RNG-Verbrauch
        /// pro Aktivierung stufensymmetrisch und reproduzierbar.
        ///
        /// Python randrange(lo, hi+1) always consumes ONE _randbelow(hi-lo+1)
        /// (no width==1 short-circuit in Python >= 3.10 — the exported streams
        /// show [1, 0] entries), so C# calls RandBelow(hi - lo + 1) + lo.
        public int RankChoice(IRandom rng, int nRows)
        {
            double u = rng.NextUnit();
            int hi = RankHi;
            if (u < MissRate)
                hi = Math.Max(hi, MissHi);
            int lo = Math.Max(1, Math.Min(RankLo, nRows));
            hi = Math.Max(lo, Math.Min(hi, nRows));
            return rng.RandBelow(hi - lo + 1) + lo;
        }

        // ----------------------------------------------------------------- misc
        /// Race-Fenster für Timer-/Rennregeln: null = Rennrechnung wird
        /// nicht gefuehrt (h=1, rein reaktiv); 1 = Referenz; 2 = proaktiv.
        public int? TimerWindow()
        {
            switch (Horizon)
            {
                case 1: return null;
                case 2: return 1;
                case 3: return 2;
            }
            throw new KeyNotFoundException("horizon " + Horizon);
        }

        public Dictionary<string, object> AsDict()
        {
            return new Dictionary<string, object>
            {
                { "level", Level },
                { "delta_rank_band", new List<object> { (long)RankLo, (long)RankHi } },
                { "delta_miss", new List<object> { (double)MissRate, (long)MissHi } },
                { "counter_blind", CounterBlind },
                { "horizon", (long)Horizon },
                { "feed_delay", (long)FeedDelay },
                { "feed_halve", FeedHalve },
                { "positioning", Positioning },
                { "econ_save", (long)EconSave },
                { "econ_idle", (long)EconIdle },
                { "econ_noise", (double)EconNoise },
                { "econ_dump", EconDump },
                { "rho", (long)Rho },
            };
        }

        // ---------------------------------------------------------------- levels
        /// Counter-Semantik-Regeln (Zaehler-Lese-Regeln der Doktrin, §6 delta):
        /// ein counter-blindes Profil (S1/S2) kann diese Regeln nicht ausfuehren
        /// — sie werden im Vollzug uebersprungen (Kartentext bleibt, Ausführung
        /// degradiert, why-Payload nennt den Grund). Gilt für
        /// Doctrine.candidates() und den Inline-Vollzug in doctrine_plan (beide
        /// laufen durch RuleGated()).
        public static readonly HashSet<string> CounterRules =
            new HashSet<string> { "gesunde_zuerst", "verwundete", "killbare" };

        /// True = diese Doktrin-Regel ist für das aktive Skill-Profil ungelesen.
        public bool RuleGated(string ruleId)
        {
            return CounterBlind && CounterRules.Contains(ruleId);
        }

        /// Python rule_gated(world, rid): reads world.skill.
        public static bool RuleGatedFor(World world, string ruleId)
        {
            var skill = world == null ? null : world.Skill;
            return skill != null && skill.RuleGated(ruleId);
        }

        /// Python active_skill(world).
        public static SkillProfile ActiveSkill(World world)
        {
            return world == null ? null : world.Skill;
        }

        /// Kurzer Skill-Handicap-Grund für die Doktrin-Ebene (why-Payload,
        /// §6 "Erklaerbarkeit"). Leer ohne Profil. Wortwahl leak-audit-fest:
        /// kein Unterstring der Hidden-Info-Tokens (z.B. kein "hand*" in
        /// "Handicap").
        public static string DoctrineNote(SkillProfile profile)
        {
            if (profile == null) return "";
            var bits = new List<string>();
            var win = profile.TimerWindow();
            if (win == null)
                bits.Add("Timer-Rennrechnung ohne Horizont \u2014 nur reagiert");
            else if (win == 2)
                bits.Add("Timer-Rennrechnung proaktiv (Fenster 2 Runden)");
            if (profile.CounterBlind)
                bits.Add("Counter-Regeln ungelesen");
            if (bits.Count == 0)
                return " | Skill " + profile.Level + " am Steuer";
            return " | Skill " + profile.Level + " am Steuer: " + string.Join("; ", bits);
        }

        public string DoctrineNote() { return DoctrineNote(this); }

        /// Accept null (Referenzverhalten), a level string or a profile object
        /// (Python resolve_skill).
        public static SkillProfile Resolve(object skill)
        {
            if (skill == null) return null;
            if (skill is SkillProfile) return (SkillProfile)skill;
            return For(skill.ToString());
        }

        /// "S1".."S5" (case-insensitive); null/"" -> null (reference path).
        public static SkillProfile For(string level)
        {
            if (level == null || level.Length == 0) return null;
            string key = level.ToUpperInvariant();
            SkillProfile prof;
            if (!Profiles.TryGetValue(key, out prof))
                throw new SkillSpecError("unknown skill level '" + level +
                                         "' (known: " + string.Join(", ", SkillLevels) + ")");
            return prof;
        }

        /// Profil-A baseline: the reference behaviour's profile parameters
        /// (Python dataclass defaults; skill=None keeps the T1/T2 reference
        /// path, this object only makes those defaults addressable).
        public static SkillProfile Reference()
        {
            return new SkillProfile { Level = "ref" };
        }

        // ----------------------------------------------------------------------
        // Stufen (Konzept §6 Tabelle; S2/S4 als Interpolation dokumentiert in
        // docs/balance_skill.md). S5 = Referenzplaner plus proaktiver Horizont —
        // skill=None bleibt das T1/T2-Referenzverhalten (Fenster 1), damit die
        // pin-nieten Hashes der Nicht-Skill-Pfade unveraendert bleiben.
        // ----------------------------------------------------------------------
        public static readonly Dictionary<string, SkillProfile> Profiles =
            new Dictionary<string, SkillProfile>
        {
            { "S1", new SkillProfile { Level = "S1", RankLo = 2, RankHi = 4, CounterBlind = true,
                                       Horizon = 1, FeedDelay = 2, FeedHalve = true,
                                       Positioning = "first", EconSave = 0, EconIdle = 1,
                                       EconDump = true, Rho = 0 } },
            { "S2", new SkillProfile { Level = "S2", RankLo = 1, RankHi = 2, MissRate = 0.30,
                                       MissHi = 3, CounterBlind = true, Horizon = 2,
                                       FeedDelay = 2, Positioning = "hillclimb",
                                       EconSave = 1, Rho = 1 } },
            { "S3", new SkillProfile { Level = "S3", RankLo = 1, RankHi = 2, MissRate = 0.10,
                                       MissHi = 3, Horizon = 2, FeedDelay = 1,
                                       Positioning = "noavoid", EconNoise = 0.12, Rho = 2 } },
            { "S4", new SkillProfile { Level = "S4", RankLo = 1, RankHi = 1, MissRate = 0.05,
                                       MissHi = 2, Horizon = 3, Rho = 2 } },
            { "S5", new SkillProfile { Level = "S5", RankLo = 1, RankHi = 1, Horizon = 3, Rho = 3 } },
        };

        public static readonly string[] SkillLevels = { "S1", "S2", "S3", "S4", "S5" };
    }
}
