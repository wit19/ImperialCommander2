# TacticalAI.Core — BINDING cross-module contracts (T4)

All files: `ImperialCommander2/Assets/Scripts/TacticalAI/Core/`, namespace
`TacticalAI.Core`, plain C# (no UnityEngine), Unity-2020-safe (C# 7.3-ish:
NO records, NO System.Text.Json, NO Span). Use `Json` (MiniJson.cs),
`Rational` (exact rationals), `Cell/Geometry`, `Board`, `Figure`,
`DeploymentCard`, `Legality`, `SpaceLegality`, `PathGraph/PathResult` —
read those files first, they are ALREADY ported and frozen.

Golden vectors for self-checking: `Assets/Tests/Fixtures/tacai/*.json`
(produced by the Python reference `tacai_build/tools/export_csharp_vectors.py`).
Fraction values in vectors are Python str(Fraction) strings ("3", "3/4").

## Shared types (World.cs — written by coordinator)

```csharp
public interface IRandom { int RandBelow(int n); double NextUnit(); }

public sealed class World {
    public Board Board;
    public List<Figure> Figures;
    public PathGraph Graph;                 // occupants provider wired in ctor
    public Dictionary<Cell, List<Figure>> Occupants();
    public Los Los;                          // class below
    public ObjectiveRuntime Objectives;      // class below (A)
    public MissionVictoryModel Victory;      // class below (B), may be null
    public HashSet<string> FocusCandidates;  // null = no doctrine filter
    public Func<Figure, double> RoleWeight;  // null -> 0.0
    public SkillProfile Skill;               // class below (C), may be null
    public IRandom SkillRng;                 // may be null
    public int RoundNo;
    public static readonly int INF = 1 << 30;
}
```

## A) Los.cs, Focus.cs, Patterns.cs

```csharp
public sealed class Los {
    public Los(Board board);
    // verbatim sim/los.py: corner-pair LOS, figure cells opaque unless
    // ignore-listed, Massive/Mobile keep LOS through blocking cells.
    public bool HasLos(IEnumerable<Cell> sourceCells, IEnumerable<Cell> targetCells,
                       IEnumerable<Cell> figureCells = null,
                       IEnumerable<Cell> ignoreBlockingCells = null);
}

public sealed class FocusDecision {
    public Figure Target; public int FocusCostMp; public int Dist;
    public List<string> TiebreakTrace; public string Why;
    public string Posture = ""; public List<Rational> W;   // null = pure GH
    public string SkillLevel = ""; public int SkillRank;
    public Dictionary<string, object> AsDict();   // keys EXACTLY like focus.json rows
}

public static class FocusResolver {
    public static int CountSpaces(Board b, IEnumerable<Cell> a, IEnumerable<Cell> b2);
    public static bool AttackGeometryOk(World w, Figure attacker, HashSet<Cell> targetCells,
                                        HashSet<Cell> cells, bool withLos,
                                        IEnumerable<Cell> figureCells = null);
    public static FocusDecision ComputeFocus(World w, Figure fig, bool vitalityTiebreak = true);
}

public static class Patterns {  // sim/patterns.py (stance names + shackle math)
    public static readonly string[] PatternsList;
    public static readonly Dictionary<string, string> PatternText;
    public static HashSet<string> CandidateFilter(World w, Figure fig, string pattern,
                                                  IReadOnlyCollection<string> rebelsIds);
    public static bool AttackAllowed(World w, Figure fig, Figure target, int distCount);
    // + whatever else patterns.py needs; no new external deps
}
```

## B) Victory.cs, Belief.cs, Doctrine.cs, RiskPosture.cs

```csharp
public sealed class VictoryCounter {
    public string CounterType;
    public VictoryCounter(string type, bool metaBonus = false);
    public static VictoryCounter FromBoard(Board b);       // null when none
    public Dictionary<string, object> PublicFields();
    public List<Rational> TargetValue(World w, Figure attacker, Figure target);
}
public sealed class MissionVictoryModel {
    public string Posture; public VictoryCounter Counter;
    public static MissionVictoryModel FromBoard(Board b);
    public string DeriveMissionClass();
    public Dictionary<string, object> PublicFields();
    public List<PostureSwitch> Evaluate(int roundNo, World w);
    public List<Rational> PostureValue(World w, Figure attacker, Figure target);
    public Cell? ContestedNode(World w, Figure fig = null, Figure target = null);
    public bool TargetIsImmediate(World w, Figure target);
}
public static class VictoryHelpers {
    public static bool IsWounded(Figure t);
    public static bool VictoryBearer(World w, Figure target);
    public static double PrintedDamageEv(Figure f);        // uses Dice (C)
    public static double Threatens(World w, Figure f, HashSet<Cell> cells);
    public static List<(string id, List<Cell> cells, int interactionsLeft)>
        OpenTimerObjectives(World w);
    public static int ArrivalRounds(World w, Figure f, List<Cell> objCells, int interactionsLeft);
    public static double TerrainCost(World w, IEnumerable<Cell> fromCells, IEnumerable<Cell> toCells);
}

public sealed class ZoneMap { /* sim/belief.py ZoneMap */ }
public sealed class BeliefState { /* heat/score/ranked/focus_zone/hot/observe/round_end */ }
public sealed class AppView {
    public int RoundNo; public string Posture; public string PostureReason;
    public Dictionary<string, object> Counter; public List<object> Objectives;
    public string FocusZone; public List<string> Belief; public HashSet<string> HotZones;
    public Dictionary<string, object> Contest; public List<object> Rings;
    public Dictionary<string, object> Public();
    public void AssertPositionFree();     // no cell coordinates may appear
}
public sealed class WorldFeed { /* truth->events; verbatim belief.py WorldFeed */ }
public sealed class ActivationInference { /* verbatim: begin_round, instruction,
    hero_activated, damage, wound_flip, on_defeat, damage_tap, active_rings */ }

public sealed class Doctrine {
    public string CardText;               // verbatim German render
    public Dictionary<string, object> AsDict();   // keys like doctrine.json
    public List<string> Candidates(World w, Figure fig);
}
public static class DoctrineSelector {
    public static Doctrine SelectDoctrine(AppView view, string pattern = "aggressive",
                                          IEnumerable<string> denyIds = null);
}

public sealed class RiskFacts { /* mirrors vec_risk facts */ }
public sealed class RiskDecision { public Dictionary<string, object> AsDict(); }
public static class RiskPosture {
    public static RiskDecision Decide(RiskFacts f, int rho = 3);
    public static List<(string label, List<Cell> cells)> NamedAnchors(Board b);
    public static string PickAnchor(Board b, List<(string, List<Cell>)> anchors, List<Cell> nearCells);
}
```

## C) Dice.cs, OrderDeck.cs, ThreatEconomy.cs, OrderPlan.cs, SkillProfile.cs

```csharp
public static class Dice {
    public static readonly string[] DieColors;   // incl. "Grey"
    public static Dictionary<string, Rational> DieExpectations(string color); // dmg/surge/acc/p_surge_ge1
    public static Rational PrintedEv(DeploymentCard card);
    public static Rational DefenseExpectation(string color);
}

public static class OrderCards {
    public static readonly string[] BlockVocabulary;
    public static List<string> ValidateCard(OrderCard c);   // [] == valid
    public static List<OrderCard> DeckFromMix(string cardId, Dictionary<string,int> mix,
                                              int[] shuffleSlots, string specialBlock = "rest");
}
public sealed class OrderDeck {  // draw/discard, deterministic via IRandom
    public OrderDeck(List<OrderCard> cards, IRandom rng);
    public OrderCard Draw(); public void FinishCard(OrderCard c);
    public List<object> History;
}
public sealed class OrderDeckManager {  // data/order_decks.json
    public static OrderDeckManager Load(Dictionary<string, object> root);
    public Dictionary<string, OrderDeck> DrawRoundAll(IEnumerable<string> groupCardIds, IRandom rng);
}

public sealed class ReserveGroup { public string GroupId, CardId, Pattern; public int Cost; public int? DeployedRound; }
public sealed class ThreatLedger {
    public int SaveRounds = 2, ForceAfterIdle = 2;
    public long Earned, Spent; public int LastReleaseRound;
    public long Balance { get; }
    public long ProjectIncome(int aliveImperials);
    public long Tick(int roundNo, int aliveImperials);
    public ReserveGroup ReleaseDecision(int roundNo, List<ReserveGroup> reserves, int aliveImperials);
    public void Release(int roundNo, ReserveGroup g, bool forced);
    public bool WaitPressure(List<ReserveGroup> reserves);
    public Dictionary<string, object> AsDict();
}

public sealed class GroupFacts { /* ctor mirrors order_plan.GroupFacts */ }
public sealed class OrderPlan {
    public Dictionary<string, object> AsDict(); public string PublicLine(...)
}
public static class OrderPlanner {
    public static OrderPlan PlanRound(int roundNo, List<GroupFacts> groups,
        Dictionary<string, DeploymentCard> cards, string posture,
        SkillProfile skill, IRandom rng, int issuedTurn = 0,
        IEnumerable<string> ringZones = null, string contestZone = null,
        string counter = null);
    public static void AssertPermutation(OrderPlan plan, List<string> groupIds);
}

public sealed class SkillProfile {   // PROFILES S1..S5 verbatim sim/skill.py
    public string Level; /* all knobs */
    public static SkillProfile For(string level);   // "S1".."S5"; null/"" -> null
    public static SkillProfile Reference();         // profile A baseline
    public int RankChoice(IRandom rng, int n);
    public bool RuleGated(string ruleId);           // doctrine rule gating
    public int? TimerWindow();
    public string DoctrineNote();
    public Dictionary<string, object> AsDict();
}
```

Cross-file calls allowed ONLY via the signatures above. When the Python
module references world attributes (skill, victory, objectives), read them
from `World`. Port semantics verbatim: same ordering, same tie-breaks, same
strings (German card text!). Every public behaviour must reproduce its
`Fixtures/tacai/*.json` vector.
