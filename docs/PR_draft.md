# PR Draft — Fair Tactical AI mode for Imperial Commander 2

Branch: `feature/tactical-ai` (local only). Four reviewable units; each is a
standalone commit, every one Default-OFF. Design source of truth:
`CONCEPT_fair_imperial_ai.md` section 4 (hook table 4.2). Core port contract:
`CONTRACT_tacticalai_core.md`.

## Ground rules honored by every unit

* **Default-OFF gate.** Every new behavior sits behind
  `if (TacticalAI.TacticalAISettings.Enabled)` (global switch, default
  `false`, mode default `"legacy"`). Legacy code is preserved **verbatim as
  the else/default branch** at every hook site.
* **Fairness invariants.**
  * No stat buffs: hidden `bonuseffects.json` rolls never fire in Tactical AI
    mode (the popup bonus branch is hard-empty); the printed stat card is the
    only source of enemy power (`Core/DeploymentCard` has no buff fields).
  * Printed values only: the planner catalog is built exclusively from fields
    on `DeploymentCard.CardFieldWhitelist` (id/name/size/speed/health/dice
    colors/cost/elite).
  * Determinism: activation order via `OrderPlanner` with a session-seeded
    `DeterministicRandom`; DP choice and target selection are deterministic
    (no `GlowEngine` RNG in tactical branches); replaying a session replays
    decisions.
  * Why-strings: every plan slot carries a public German `Why` string;
    `ExplainLevel = none | post_hoc | full` renders round summary / per-slot
    why + printed rules on the activation popup — nothing hidden is ever
    explained.
* **No restructured legacy.** Where a hook point in the concept table does
  not exist in this codebase, the nearest faithful equivalent is gated in and
  listed under *Deviations* — no legacy refactor PRs.

---

## M1 — Planner seam + settings, zero behavior change

New files:

| File | Role |
|---|---|
| `ImperialCommander2/Assets/Scripts/TacticalAI/Core/TacticalAISettings.cs` | Pure-C# static settings: `Enabled` (default **false**), `Mode` (`legacy`/`reference`/`overlord`), `SkillLevel` (S1..S5), `DeploymentMode` (`secret`/`open`), `ExplainLevel` (`none`/`post_hoc`/`full`), validation, JSON (de)serialization via Core `Json`, `SetSaveHook(Func<string,string>)`, `EffectiveMode(missionAiMode)`. Corrupt save data resets to defaults — **can never enable the mode**. |
| `ImperialCommander2/Assets/Scripts/TacticalAI/TacticalAISettingsUnity.cs` | `[RuntimeInitializeOnLoadMethod]` boot: PlayerPrefs persistence hook (`tacai_settings`) + load. |
| `ImperialCommander2/Assets/Scripts/TacticalAI/TacticalAISettingsUI.cs` | SettingsPanel rows built at runtime (same dynamic-UI pattern the popups already use): master toggle always visible; submenu hidden while off. |

Edited (no behavior):

* `Common/SettingsPanel.cs` — one installer call at the end of
  `OnSettingsLoaded` (builds rows only; gate logic inside the installer).
* `GameCore/SagaSetupOptions.cs` — `aiMode` field + `ToggleAiMode()` + reset.
* `GameCore/CampaignStructure.cs` — `aiMode` field (Newtonsoft-persisted,
  older saves deserialize to `"legacy"`).
* Rebuilt `tools/*/bin` artifacts (tracked): Core now contains the settings
  class; harness re-run green (see Verification).

## M2 — Core + vector fixtures + harness + tests

Already on the branch: `Assets/Scripts/TacticalAI/Core/**` (port),
`Assets/Tests/Fixtures/tacai/**` (golden vectors),
`tools/TacticalAiHarness` (console vector runner). This commit adds the
NUnit mirror suite:

| File | Covers |
|---|---|
| `Assets/Tests/TacticalAI/TacaiFixtures.cs` | Fixture discovery + the harness' Norm/Eq normalization and scenario-world twin (ported 1:1). |
| `Assets/Tests/TacticalAI/GeometryAndBoardTests.cs` | Geometry sanity vs golden; **all 4 base boards** + every scenario board load. |
| `Assets/Tests/TacticalAI/VectorRowTests.cs` | One **Focus tie-break row byte-exact** (`focus.json`/`counter_bearer`, incl. `tiebreak_trace` + `why`) and one **doctrine card-text row byte-exact** (`doctrine.json`/`eliminate_duel` r1, all 6 patterns). |
| `Assets/Tests/TacticalAI/DeckThreatAssetTests.cs` | Shipped `order_decks.json`: every deck card passes `ValidateCard`; `OrderDeckManager.Load` path identical to the Unity bridge; `decks.json` violation rows; **ThreatLedger** full replay byte-exact + conservation assertion. |
| `Assets/Tests/TacticalAI/PlannerTests.cs` | `OrderPlanner` permutation law for every posture × skill band, seed-invariance, first golden row byte-exact; **LegacyPlanner pass-through** (identity, null-safe); settings Default-OFF gate tests. |
| `Assets/Tests/TacticalAI/HeroProfileTests.cs` | M3.1: curated `hero_profiles.json` loader rows byte-exact; disengage flag-rule grid; `risk.json` `profile_cases` full replay; `hero_w.json` variant `none` = store-empty regression pin (T1/T2 parity); campaign loadouts scored against the app's OWN `CampaignData/{items,skills}.json`. |

The repo has **no test framework wired** (no asmdef, no NUnit package —
verified), so all fixtures carry
`[Explicit("Requires Test Framework")]` and ship for later CI. Honesty note:
they were executed locally by building a throwaway NUnitLite runner in a
scratch directory against the real fixtures: **24 / 24 passed** (the
`[Explicit]` attributes were stripped only in the scratch copies; shipping
files are untouched).

## M3 — Unity-side hooks (all Default-OFF)

New:

* `Assets/Scripts/TacticalAI/TacticalAIBridge.cs` — round planner
  (`OrderPlanner.PlanRound` + `AssertPermutation`), fairness law
  `OrderDeckManager.ReapUnused` → `DrawRoundAll` **at round start**,
  deterministic DP/target selection, explain strings, `WorldProvider` seam
  for the future hex adapter.
* `Assets/Scripts/TacticalAI/TacticalAIAssets.cs` — lazy loaders for the two
  public data assets (M4); since M3.1 also `HeroProfilesRoot` +
  `CampaignItemsRoot`/`CampaignSkillsRoot` (the app's OWN CampaignData
  tables, trailing-comma tolerant) and the pure `ProfileFor(heroName, hero)`
  store seam (curated table + campaign loadout, concept 3.3.1a v3.11).

Hook diffs (each: gate + new branch + untouched legacy path as else):

| Site | Gate behavior when ON | Legacy preserved as |
|---|---|---|
| `MainGame/EnemyActivationPopup.cs` `ShowSaga` (3 branches) | printed order-deck card blocks replace the random instruction option (+ mission-override instructions still applied, no reposition rewrite); deterministic focus target; bonus boxes hard-empty (hidden buff roll never fires) | `tacPlan == null` (switch OFF or plan unavailable) keeps every original block verbatim, incl. the "RE-USING PREVIOUS ACTIVATION DATA" replay |
| `Screens/SagaController.cs` `OnActivateImperial` | next group in the planner's round order | planner returns false → original 70%-priority random draw verbatim |
| `Screens/SagaController.cs` `IncreaseRound` | `TacticalAIBridge.OnNewRound(round)` — round order + deal-all cards + `post_hoc` round summary (QuickMessage) | switch OFF: no call |
| `Saga/Managers/MapEntityManager.cs` `GetActiveDeploymentPoint` | deterministic pick among the already-filtered DPs (name/GUID ordinal) | original `GlowEngine.GenerateRandomNumbers` line verbatim |
| `Saga/Managers/DeploymentManager.cs` `HandleMapDeployment` | `secret` mode: no DP reveal highlight / camera move | `!tacSecret` → original two lines verbatim |
| `Screens/SagaSetup.cs` | campaign aiMode copied into the session; single missions inherit the global mode; `OnAiMode(Button)` 3-state handler + `ResetSession` on new session | switch OFF: every block is a no-op |
| `Screens/CampaignScreen/MissionItemPrefab.cs` | optional `aiModeIcon` (null-safe) shows `AI:OFF/REF/ORD`, `OnAiModeClicked` cycles | icon hidden while switch OFF; handler early-returns |

Editor wiring steps (cannot be done from code, prefab YAML untouched):
1. optionally wire a Button → `SagaSetup.OnAiMode` and a `Text` →
   `SagaSetup.aiModeText`;
2. optionally add a `TextMeshProUGUI` child → `MissionItemPrefab.aiModeIcon`
   with a Button → `OnAiModeClicked`.
Both are null-safe: unwired prefabs behave exactly as before.

## M3.1 — HeroThreatProfile C# port (concept 3.3.1a v3.10/v3.11, opt-in)

Port of the M2a.4 Python layer (`sim/hero_profiles.py`) — small cut: profile
loader + score function + disengage-flag rule + golden-vector tests.
Default OFF everywhere: `World.HeroProfiles` null/empty ⇒ every W tuple and
every RiskPosture decision stays **byte-identical to the T1/T2/M2 pins**
(re-run of all pre-existing harness sections proves it).

| File | Role |
|---|---|
| `Assets/Scripts/TacticalAI/Core/HeroProfiles.cs` | `HeroThreatProfile` (+ `ChipKey`/`RunnerValue`/`AsDict`), asset doc loader/cache, `ProfileFocusKey` (branch order verbatim from the sim), `CoverIsWorthless` (disengage utility = 0 vs `ignoreLos`/range over `cover_shelter_max_range`), `ProfilesToInference` (lethality prior → profile rings/weights), `ApplyCampaignLoadouts`/`ApplyLoadout` (Thomas proxies: tier×cost type-weighted, xp-cost investment index, `teure-faehigkeit`/`lastout-unvollstaendig` flags, knowledge flips to `bestaetigt`). |
| `Assets/Scripts/TacticalAI/Core/World.cs` | `HeroProfiles` store seam (`{figure_id: profile}`, null = reference path). |
| `Assets/Scripts/TacticalAI/Core/Victory.cs` | 5 insertion hooks: profile component lands BEHIND bearer/killable/healthy/arrival semantics, never before (woundAll chip below healthy-first; runner value between arrival and terrain terms; attrition ev in the plain hunt; surviveRounds/capture/incapacitate variants). |
| `Assets/Scripts/TacticalAI/Core/RiskPosture.cs` | `RiskFacts.ThreatIgnoreLos/ThreatRangeMax` + cover-worthless branch (rho-1 press clause + dedicated `hold` rationale "Deckung wertlos gegen die bekannte Bedrohung"); melee heroes keep disengage at full value bit-identically. |
| `Assets/Resources/Languages/En/tactical_hero_profiles.json` | curated public table — **byte-identical** to the sim asset (sha256 pinned in `hero_profiles.json` vectors and asserted by the harness). |
| `Assets/Tests/Fixtures/tacai/{hero_profiles,hero_w,campaign_loadouts}.json` + `risk.json` `profile_cases` | golden vectors exported by the sim (`tools/export_csharp_vectors.py`, M3.1 sections): loader rows, cover grid, focus-key branch rows, inference rows, loadout rows (incl. the hero_id-only-skip parity), `W` rows with variants `none`/`synth`/`asset`, 1920 risk profile rows. |
| `tools/TacticalAiHarness/SectionsC.cs` (+ Program/SectionsA) | harness sections `hero_profiles.json` + `hero_w.json` + `profile_cases` replay; Languages-dir discovery for the app's own CampaignData tables; asset doc primed before all sections (sim `_doc()` parity). |

## M4 — Public data assets + this draft

* `ImperialCommander2/Assets/Resources/TacticalAI/order_decks.json` — copy of
  the vector fixture `Assets/Tests/Fixtures/tacai/order_decks.json`
  (`tacai.order_decks/1`, 95 group decks). Every card validated by
  `ValidateCard` at load (loader throws otherwise) and by the M2 test.
* `ImperialCommander2/Assets/Resources/Languages/En/tactical_hero_lethality.json`
  — copy of `hero_lethality.json` for the strict locale asset folder. The
  fixture has **no `copied_at` key** (nothing to strip — documented); the
  `schema`/`source`/`disclaimer`/`band_semantics` keys are kept on purpose:
  the disclaimer is the public fairness statement ("kuratierte Priors, keine
  App-Werte"), and the loader reads the file generically through Core `Json`.
* `docs/PR_draft.md` — this document.
* `ImperialCommander2/Assets/Resources/Languages/En/tactical_hero_profiles.json`
  — M3.1 curated HeroThreatProfile table (`tacai.hero_profiles/1`), byte-
  identical copy of the sim asset including the fairness-disclaimer head
  (same honesty policy as the lethality priors; a human reads the same cards).

## Verification

```
$ dotnet build tools/TacticalAiHarness   -> 0 Errors
$ dotnet run --project tools/TacticalAiHarness -- ImperialCommander2/Assets/Tests/Fixtures/tacai
PASS geometry.json (85 checks)
PASS board.json (56 checks)
PASS path.json (4242 checks)
PASS legality.json (925 checks)
PASS los.json (160 checks)
PASS dice.json (37 checks)
PASS threat.json (27 checks)
PASS decks.json (469 checks)
PASS skill.json (20 checks)
PASS risk.json (29589 checks)
PASS focus.json (270 checks)
PASS victory.json (135 checks)
PASS belief.json (52 checks)
PASS rings.json (28 checks)
PASS doctrine.json (75 checks)
PASS order_plan.json (100 checks)
PASS action_plan.json (159 checks)
PASS hero_profiles.json (76 checks)
PASS hero_w.json (125 checks)
== summary: ALL PASS | checks=36630 fails=0
```

NUnit mirror suite (`Assets/Tests/TacticalAI`): executed via a scratch
NUnitLite runner (Core sources + test sources, `[Explicit]` stripped only in
scratch copies) → `Test Count: 24, Passed: 24, Failed: 0`.

**Compile status honesty:** the console harness + NUnit suite build with
`dotnet` and pass. The Unity-side files (`TacticalAIAssets.cs`,
`TacticalAIBridge.cs`, `TacticalAISettingsUnity.cs`, `TacticalAISettingsUI.cs`
and the nine edited legacy files) reference `UnityEngine`/`TMPro`/DOTween and
**cannot be dotnet-built here — they are compile-verified-in-editor-only**
(symbol-level grep review only; brace deltas per file are balanced). Unity
`.meta` files are not committed, matching the existing `TacticalAI/` subtree
convention; Unity regenerates them on first editor open.

## Deviations from concept 4.2 (nearest faithful equivalents, recorded per rule (b))

1. **Settings namespace.** `TacticalAISettings` lives in namespace
   `TacticalAI` (file under `Core/`, pure C#) so the hook-table condition
   `TacticalAI.TacticalAISettings.Enabled` compiles verbatim everywhere.
2. **Round trigger.** There is no `deployment_finished` mission event in this
   codebase; the round planner is called from `SagaController.IncreaseRound()`
   (plus lazy self-heal before any activation).
3. **No hex world model in the Unity runtime.** The Saga map is a tilemap of
   deployment points; heroes/enemies are cards, not hex figures — there is
   nothing to snapshot into `Core.World`. Core Focus/belief/doctrine are
   exposed through `TacticalAIBridge.WorldProvider`; until a hex adapter
   registers, `overlord` plans at the reference ruleset (logged, never
   hidden).
4. **Targeting.** Without the board, focus = printed preferred-target traits
   first, then deployment order, wounded skipped — deterministic, no hidden
   info, never the legacy random draw while tactical is ON with an available
   plan. (Legacy random remains the OFF path.)
5. **Posture/pattern/vitality.** Fixed `eliminate` posture, `aggressive`
   pattern, full-vitality fraction until the mission-objective mapping and
   the printed-trait pattern classification (M4 follow-up asset) land.
6. **DP scoring.** Deterministic stable order (name, then GUID); spatial
   scoring (objective distance/zones/colors) needs the hex adapter.
7. **SagaDGPrefab** has no random-option text in the current code (the
   concept table predates its removal); the per-card text requirement is
   covered by the popup hook — no edit there.
8. **DataStore adapter** lives as `TacticalAIAssets.cs` (lazy, gated) to keep
   `DataStore.cs` untouched.
9. **Reinforcements** deployed mid-round get their order card at first
   activation (they were not part of the round-start deal); documented in the
   bridge.

## LICENSE

The repository is MIT-licensed (see `LICENSE`); this work keeps it that way.
The `TacticalAI.Core` code is an original C# port written for this repo —
the Python balance/reference simulation and its exported golden vectors remain
**out-of-tree** (only the exported JSON fixtures ship, as test data).
New public data assets (`order_decks.json`, `tactical_hero_lethality.json`)
carry their provenance fields inside the files themselves.
