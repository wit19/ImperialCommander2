#!/usr/bin/env python3
"""M12d: generate the classification table asset
  ImperialCommander2/Assets/Resources/Languages/En/tactical_imperial_card_effects.json

The table maps EVERY card of the shipped deck asset (109 cards / 12 class
decks, tactical_imperial_decks.json) into exactly one of the three M12d
buckets, with machine-readable hooks for the implemented ones:

  (a) PLANER-MODIFIER  - printed effect lands on the activation-order /
      target-strength pipeline (OrderPlanner / focus surcharge).
  (b) STAT-MODIFIER    - printed effect lands on the existing value seams
      (threat model engagement, deployment economy) or on the store-model
      seams (attachment lifecycle, effective Health).
  (c) ENGINE-DETAIL    - needs real rules work beyond planner+values
      (dice manipulation, token/condition operations, spatial legality).
      NOT half-implemented: no hooks, one-line reason, still shown in the
      UI as "aktiv, aber ohne KI-Modell" (M12e reads this table).

THE VALUE LAW (every hook value traces to a printed RR fact; header of
ImperialCardEffects.cs carries the same law):
  * order_boost   5/4 (documented strength-efficiency stage, pinned once per
                  group, MAX not sum) - cards that reliably add an ACTION /
                  extra activation cycle to the owning side:
                  extra action, extra attack, interrupt move/attack,
                  ready-another-card-for-this-activation, hidden-as-free-
                  activation-cost (free action preserved).
  * order_boost   1/8 - activation-timed movement-point prints (mini action
                  economy, same stage vocabulary, one conservative step).
  * atk_ev_bonus  EXACT dice EV only when the printed effect always touches
                  the attack pool: added die (Green 3/2, Yellow 5/6), the
                  conditional green Focus die (Focused = +1 green die, RR
                  keyword gloss "Focus Fire"), printed surge abilities via
                  the BLUE-die surge floor 1/3 (P(blue shows >=1 surge)=1/3,
                  Dice.AttackDieFaces - conservative for every printed
                  "{B}:" line, M11e floor law), printed rerolls: may reroll
                  1 die floors at 1/8, uncapped rerolls at 1/4, the
                  explosive-munitions G->R upgrade + Blast floor = 5/12,
                  the blue-die upgrade floor 1/12.
  * focus_surcharge +1 on the M11d/M11h light surcharge slot (M10b
                  objective-actor precedent "erhoeht sich leicht"; MAX,
                  never a sum) - printed target/attack-declare conditions
                  that make the conditioned hero the cheaper target.
  * engage_acc_delta +/-1 - accuracy pips and {H}/{C}/{B} result shifts
                  printed ON ATTACKS/DEFENSE that hit the hero's expected
                  exchange (signed, applied to the threat v2 attack pool /
                  pierce seam). Defensive-direction prints subtract;
                  attack-side declare bonuses add (the Overlord's attacks
                  hit harder -> the engagement costs the hero more).
  * engage_pierce +1 exact printed "Pierce 1".
  * deploy_cost N - printed threat-cost reduction, floored at 0 by
                  ThreatLedger.EffectiveCost (ledger stays balanced: the
                  Empire really spends the reduced cost).
  * free_group    - a group arrives WITHOUT paying its deployment cost
                  (ThreatEconomy cost-0 seam).
  * attach_lifecycle - store-model persistence (Rules Reference
                  "Attachment": attach at deployment, effect on every
                  figure, returns when the last figure falls, re-attachable
                  at FUTURE deployments; trait restrictions are hard).
  * stat_health N - printed "+N Health" -> effective-health seam
                  (OrderPlanner attrition weight via ImperialCardEffects
                  .EffectiveVitality); attachment lifecycles ride along.
  * stat_speed / stat_note - printed stat lines with NO hex-world / attack
                  seam today (Speed/Mobile, defense-die additions without
                  a threat-seam translation) - audit entries, model value
                  0. A card only reaches (b) through a CONSUMED hook
                  (attach_lifecycle/stat_health count); pure stat_note
                  cards without any attachment are bucket (c).

PARTIALS: a hook implements the CONSERVATIVE FLOOR of a card; the remaining
printed halves (conditions the model cannot see, dice mechanics, condition/
token operations) are 'partial' notes - they never add model value and never
silently drop the card (M11e: "every option value is a CONSERVATIVE floor").

Selection law from the task body: ALL 12 basic cards of the campaign corpus
(basics are active from round 1) and >= 60% of the PURCHASED cards, selection
by frequency in the corpus = 7 shipped MissionData campaigns x the app's own
21-hero roster x the campaigns' printed villain pools, deterministic M12b
deck choice + M12c greedy purchase ladder over the full XP budget 0..42.
The tool ASSERTS the threshold and prints the evidence.

Usage:
  gen_imperial_card_effects.py            # write the asset + print stats
  gen_imperial_card_effects.py --check    # validate only (CI/gate mirror)
"""
import importlib.util
import json
import sys
from fractions import Fraction
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
ASSET = (ROOT / "ImperialCommander2/Assets/Resources/Languages/En/"
         "tactical_imperial_decks.json")
OUT = (ROOT / "ImperialCommander2/Assets/Resources/Languages/En/"
       "tactical_imperial_card_effects.json")
MISSION_DIR = ROOT / "ImperialCommander2/Assets/Resources/MissionData"
HEROES = ROOT / "ImperialCommander2/Assets/Resources/CardData/heroes.json"

_spec = importlib.util.spec_from_file_location(
    "tacai_deck_twin", ROOT / "wiki_data/tools/tacai_deck_twin.py")
assert _spec is not None and _spec.loader is not None
dt = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(dt)
_spec2 = importlib.util.spec_from_file_location(
    "tacai_purchase_twin", ROOT / "wiki_data/tools/tacai_purchase_twin.py")
assert _spec2 is not None and _spec2.loader is not None
pt = importlib.util.module_from_spec(_spec2)
_spec2.loader.exec_module(pt)

VOCAB = {
    "order_boost": {
        "consumed_by": "OrderPlanner.PlanRound(cardEffects) - Staerke-Effizienz stage",
        "unit": "stage",
        "values": ["5/4", "1/8"],
        "law": "printes an extra action / extra activation cycle (5/4) or "
               "activation-timed movement points (1/8) to the owning side; "
               "applied MAX-once per group, public tag + why-text",
    },
    "atk_ev_bonus": {
        "consumed_by": "OrderPlanner.PlanRound(cardEffects) - printed strength",
        "unit": "rational damage EV",
        "values": ["1/12", "1/8", "1/4", "1/3", "2/3", "5/12", "3/4", "1", "3/2", "5/6"],
        "law": "printed always-on attack-pool improvement, conservative "
               "floor (surge prints -> blue surge floor 1/3; a printed fixed "
               "'+1 {H}/{C} to the attack results' is the exact pip 1)",
    },
    "focus_surcharge": {
        "consumed_by": "OverlordFocus.Rank(cardEffects) - light +1 econ slot (M11d/M11h, MAX)",
        "unit": "stage",
        "values": ["1"],
        "law": "printed targeting/attack-declare condition marks the conditioned "
               "hero as the cheaper finish (light, never a primary signal)",
    },
    "engage_acc_delta": {
        "consumed_by": "HeroThreatModelV2.Threat(engage) via OverlordFocus.Rank(engage)",
        "unit": "accuracy pips (signed)",
        "values": ["1", "-1"],
        "law": "printed accuracy pips or attack/defense result shifts on the "
               "hero exchange; + = owning side's attacks hit harder (threat "
               "of the hero UP), - = hero attacks the owning side worse "
               "(threat DOWN); pip floor 1, never inflated",
    },
    "engage_pierce": {
        "consumed_by": "HeroThreatModelV2.Threat(engage) via OverlordFocus.Rank(engage)",
        "unit": "printed Pierce",
        "values": ["1"],
        "law": "printed 'Pierce N' on attacks of the owning side",
    },
    "deploy_cost": {
        "consumed_by": "ThreatLedger.EffectiveCost (reserve release / anti-stall)",
        "unit": "threat reduction",
        "values": ["1", "5"],
        "law": "printed deployment-cost reduction for a legal group, floored at 0",
    },
    "free_group": {
        "consumed_by": "ThreatLedger.EffectiveCost at 0 / bridge deploy option (M12e wiring)",
        "unit": "sentinel",
        "values": ["1"],
        "law": "a group arrives without paying its deployment cost",
    },
    "attach_lifecycle": {
        "consumed_by": "Saga.ImperialDeckState attachment lifecycle (store model)",
        "unit": "sentinel",
        "values": ["1"],
        "law": "RR 'Attachment': attach at deployment on the deploy card, acts "
               "on ALL figures of the group, returns when the last figure is "
               "defeated, re-attachable at future deployments (NOT consumed)",
    },
    "stat_health": {
        "consumed_by": "ImperialCardEffects.EffectiveVitality (bridge attrition input)",
        "unit": "Health",
        "values": ["2"],
        "law": "printed '+N Health' -> effective Health = Health + N in the "
               "vitality fraction the OrderPlanner attrition weight consumes",
    },
    "stat_speed": {
        "consumed_by": "none yet (no hex world) - audit",
        "unit": "audit",
        "values": ["1"],
        "law": "printed Speed/Mobile print without a movement seam in the "
               "app-side model; model value 0, kept for M12e UI honesty",
    },
}

# ------------------------------------------------------------------ decisions
# key: "<deck>__<card>" -> (bucket, [(kind, value), ...], partial notes, reason)
# Basic cards are keyed WITHOUT the decision entry requirement: every card is
# listed. Reasons are one sentence (German, UI-facing via M12e).

D = {}


def dec(card, bucket, hooks=(), partial=(), reason=""):
    D[card] = (bucket, list(hooks), list(partial), reason)


# ------------------------------------------------------------ armored_onslaught
dec("armored_onslaught__armor_corps", "c", [],
    ["defense reroll is dice manipulation"],
    "Reaktiv-Wurf manipulation (1 Wuerfel neu) braucht den Wuerfelpfad, kein Planer-/Werte-Hook.")
dec("armored_onslaught__automated_repairs", "b",
    [("attach_lifecycle", "1")],
    ["Strain-/Recovery-Waehrung nicht im Modell (M11d-Seam)"],
    "Attachment-Lebenszyklus (Store) umgesetzt; Erholungs-Effekt braucht Strain-Waehrung.")
dec("armored_onslaught__reactive_armor", "b",
    [("attach_lifecycle", "1"), ("stat_health", "2")],
    ["{F}->{+2G} auf Verteidigung ist Wuerfel-Manipulation"],
    "+2 Health auf die Effektiv-Gesundheit (Attrition) + Attachment-Lebenszyklus; Verteidigungs-Wurf bleibt Detail.")
dec("armored_onslaught__explosive_entry", "c", [],
    ["Deploy-Trigger-Wuerfel + Flaeche + Zustand"],
    "Deploy-ausgelöster Wuerfel-/Flächen-/Zustandseffekt jenseits von Planer und Werten.")
dec("armored_onslaught__explosive_munitions", "a",
    [("atk_ev_bonus", "5/12")],
    ["Blast-Streuung nur bei angrenzenden Helden (kein Hex-Welt-Seam)"],
    "Angriffs-Upgrade (1 Wuerfel -> Rot + Blast) als konservativer EV-Boden auf der Staerke-Schiene.")
dec("armored_onslaught__heavy_firepower", "c", [],
    ["Wuerfel-ersetzen-mit-Handwerk ist Wuerfel-Manipulation"],
    "Freie Wuerfel-Substitution im Angriffspool braucht den Wuerfelpfad (Wahl, kein fester EV).")
dec("armored_onslaught__mortar", "c", [],
    ["neue Wuerfel-Mechanik (getrennter Pool)"],
    "Zusaetzlicher Wuerfelpool mit Blast ist echte Wuerfel-Mechanik, kein Hook-Wert.")
dec("armored_onslaught__power_to_shields", "c", [],
    ["Schluesselwort-Negation auf Verteidigung"],
    "Keyword-Negation (Blast/…) ist eine Wuerfel-/Regeloperation ohne Werte-Seam.")
dec("armored_onslaught__rapid_dominance", "a",
    [("order_boost", "5/4"), ("attach_lifecycle", "1")],
    ["Trait-Fenster HEAVY WEAPON/VEHICLE (GroupApplies, Fixture gepinnt)"],
    "Zusatz-Aktion waehrend der Aktivierung = Aktivoekonomik auf der Staerke-Effizienz-Schiene.")
# -------------------------------------------------------------- hutt_mercenaries
dec("hutt_mercenaries__cheap_shot", "c", [],
    ["Test-Wuerfel + Zustand Weakened"],
    "Attributswurf mit Zustandsfolge braucht den Wuerfelpfad.")
dec("hutt_mercenaries__guild_hunters", "c", [],
    ["Deploy -> Hidden; Surge nur gegen Hidden-Opfer"],
    "Hidden-Bedingung und Surge-Gewinn haengen an der (nicht quantifizierten) Hidden-Bedingung.")
dec("hutt_mercenaries__most_wanted", "b",
    [("engage_acc_delta", "1")],
    ["Bounty-Zusaatz +1B und Ready-Bedingung sind Zusatzpfade"],
    "Angriffs-Genauigkeits-Buff im Abwehrfall des Helden senkt/erhoeht den Tauschwert — Threat-Hook.")
dec("hutt_mercenaries__nowhere_to_hide", "a",
    [("atk_ev_bonus", "1/3")],
    ["LOS-Freigabe braucht Hex-Welt; Bounty-Bedingung oeffentlich (Angriff)"],
    "Blaue Wuerfel zum Angriffspool als Surge-Boden; LOS-Teil bleibt Modell-Ausnahme.")
dec("hutt_mercenaries__nowhere_to_run", "a",
    [("focus_surcharge", "1")],
    ["+1B bei >=3 Strain und Hutt-Zusaatz sind Wuerfelpfad-Werte"],
    "Zielbedingung 'Held mit >=3 Strain' als leichter Ziel-Aufschlag (M11d-Slot, MAX).")
dec("hutt_mercenaries__wanted_dead", "b",
    [("engage_acc_delta", "1")],
    ["Bounty-Pflicht-Passive ist oeffentlich (alle Helden tragen Token); "
     "{C}-Alternative und Defeat-Discard sind Detail"],
    "Angriffe gegen Helden mit Bounty verlieren eine Tauschstufe — Threat-Hook nach unten (Pip-Boden 1).")
dec("hutt_mercenaries__savage_motivation", "c", [],
    ["Aktivierungs-Aktion mit Wuerfel"],
    "Aktivierungs-Aktion mit echtem Wuerfelwurf (gelb/blau) ist Wuerfel-Mechanik.")
dec("hutt_mercenaries__scouted", "c", [],
    ["Defense-reroll + Flucht-Transport"],
    "Reaktive Wuerfel-Manipulation plus Bewegung, beides ohne Planer-/Werte-Hook.")
dec("hutt_mercenaries__vendetta", "a",
    [("atk_ev_bonus", "1/8")],
    ["Ready gegen {A} ist Aktionsbuchhaltung am Rand"],
    "Ein Wuerfel neu im Angriff = konservativer Reroll-Boden auf der Staerke-Schiene.")
# ---------------------------------------------------------- imperial_black_ops
dec("imperial_black_ops__execution_squad", "a",
    [("order_boost", "5/4"), ("attach_lifecycle", "1")],
    ["'gleiche {A}-Faehigung zweimal' ist Angriffsverdopplung im Detail"],
    "{A}-Faehigungen zaehlen im Modell als Extra-Aktionsokonomie (Aktivierungs-Stufe).")
dec("imperial_black_ops__in_the_shadows", "a",
    [("order_boost", "5/4")],
    ["Hidden-Bedingung selbst ist nicht quantifiziert (Core-RR ohne Hidden-Glossar)"],
    "Kostenfreie Aktivierung + Hidden = versteckte Einheit bleibt handlungsfaehig — Aktivierungs-Hook.")
dec("imperial_black_ops__shadow_armor", "b",
    [("engage_acc_delta", "-1")],
    ["-1G/-1B-Alternativen bleiben unmodelliert (Genauigkeits-Boden)"],
    "Angriff gegen die eigene Seite verliert eine Genauigkeitsstufe — Threat-Hook nach unten.")
dec("imperial_black_ops__shadow_corps", "c", [],
    ["Hidden-Vervielfaeltigung ueber {A}"],
    "Hidden ist als Bedingung nicht quantifiziert; kein Planer-/Werte-Hook moeglich.")
dec("imperial_black_ops__stealth", "b",
    [("engage_acc_delta", "-1"), ("attach_lifecycle", "1")],
    ["Hidden-am-Aktivierungsende unquantifiziert"],
    "'{O}-Angriffe gegen dich brauchen 1 Genauigkeit' ist exakt der Hidden-Treffer-Regelwert — Threat-Hook.")
dec("imperial_black_ops__strategic_redeployment", "c", [],
    ["{B}-Ergebnisse als Bedrohung ausgeben"],
    "Wuerfelergebnis-zu-Bedrohung-Umwandlung haengt am Wuerfel- und Wirtschaftspfad zugleich.")
dec("imperial_black_ops__surprise_attack", "b",
    [("engage_acc_delta", "1")],
    ["LOS-Bedingung der Zielgruppe (kein Hex-Welt-Seam, public-feed-naeh)"],
    "Genauigkeits-Buff auf eigenem Angriff erhoeht den Tauschwert gegen Helden — Threat-Hook.")
dec("imperial_black_ops__true_shadow", "b",
    [("attach_lifecycle", "1")],
    ["Hidden bei Deploy + Defense-Wuerfel tauschen sind Engine-Detail"],
    "Attachment-Lebenszyklus umgesetzt; Hidden und Defense-Wuerfel-Tausch bleiben Engine-Detail.")
dec("imperial_black_ops__versatility", "c", [],
    ["Bedingungs-tausch + Hidden"],
    "Bedingungen als Waehrung tauschen ist echtes Regelwerk jenseits von Planer und Werten.")
# --------------------------------------------------------- inspiring_leadership
dec("inspiring_leadership__field_general", "a",
    [("order_boost", "5/4"), ("attach_lifecycle", "1")],
    ["Executive Order = Move oder Attack (nur Move-Seam gezählt)"],
    "Executive Order: zusaetzliche Bewegung/Angriffs-Aktion = Aktivierungsokonomie (Attach an Leader).")
dec("inspiring_leadership__field_officer", "a",
    [("order_boost", "5/4"), ("attach_lifecycle", "1")],
    ["Interrupt-Timing bleibt M12e-UI-Hinweis"],
    "Order: befreundete Figur unterbricht fuer Bewegung = Extra-Aktion; Attachment-Lebenszyklus dazu.")
dec("inspiring_leadership__imperial_dedication", "c", [],
    ["Angriffsziel-Umleitung"],
    "Ein fremdes Angriffsziel umzubiegen braucht den Wuerfel-/Zielpfad der Heldenangriffe.")
dec("inspiring_leadership__lead_by_example", "a",
    [("order_boost", "5/4")],
    ["Executive Order darf auch attackieren"],
    "Order/Executive Order erhalten Move(=Aktion) hinterher = Aktivierungsokonomie.")
dec("inspiring_leadership__noble_sacrifice", "c", [],
    ["Defeat-Trigger, 5 Figuren Focused"],
    "Szenario-Trigger mit grossflaechigem Zustands-Gewinn ist Engine-Detail.")
dec("inspiring_leadership__optimal_tactics", "a",
    [("atk_ev_bonus", "1")],
    ["Reichweite 3 um LEADER (Gruppen-Bedingung), Reroll nicht gezaehlt"],
    "+1 Schaden auf dem Angriff ist der exakte gedruckte Pip — Staerken-Hook.")
dec("inspiring_leadership__press_on", "c", [],
    ["Strain-Heilung ohne Waehrung im Modell"],
    "Recovery wirkt auf Strain, das die App nicht fuehrt (M11d-Seam offen).")
dec("inspiring_leadership__strategic_planning", "a",
    [("order_boost", "5/4")],
    ["Move-Points der beiden Karten; Kostenkarte bleibt unbehandelt"],
    "Deployment-Karte fuere Runde bereit = extra Aktivierungszyklus auf der Schiene.")
dec("inspiring_leadership__supervisory_agent", "b",
    [("free_group", "1")],
    ["Offizier-Kostenwert der App; Karte vor Aktivierung erschöpft"],
    "Kostenfreier Deploy eines Offiziers = Gruppe ohne Kosten (BedrohungsOekonomie).")
# --------------------------------------------------------------- military_might
dec("military_might__assault_armor", "b",
    [("attach_lifecycle", "1"), ("stat_health", "2")],
    ["{A} Reroll schwarz ist Wuerfelmanipulation"],
    "+2 Health auf die Effektiv-Gesundheit (Attrition) + Attachment-Lebenszyklus.")
dec("military_might__combat_medic", "c", [],
    ["Recovery am Officer-Aktivierungsende"],
    "Recovery ohne Strain-Waehrung im Modell (M11d-Seam offen).")
dec("military_might__combat_veterans", "a",
    [("atk_ev_bonus", "1/3"), ("attach_lifecycle", "1")],
    ["+2 Genauigkeit nur am Threat-Seam denkbar (nicht doppelt gezaehlt), +1G defense unmodelliert"],
    "+1 Blau im Angriff als Surge-Boden; Genauigkeits-Teil bleibt Konservativ-Abschlag.")
dec("military_might__endless_ranks", "b",
    [("deploy_cost", "1")],
    ["TROOPER-Fenster (GroupApplies, Fixture gepinnt)"],
    "Printed -1 Deploy-Kosten fuer Truppen auf der BedrohungsOekonomie (Kappe 0).")
dec("military_might__riot_grenades", "b",
    [("attach_lifecycle", "1")],
    ["Flaechen-Test + Schaden sind Wuerfel-Mechanik"],
    "Attachment-Lebenszyklus umgesetzt; Flaechenwurf bleibt UI-{A}-Block ohne Modell-Wert.")
dec("military_might__shock_troopers", "a",
    [("atk_ev_bonus", "1/3")],
    ["Trooper + Reichweite 2 (Gruppen-Bedingung)"],
    "+1 Blau im Angriff als Surge-Boden auf der Staerke-Schiene.")
dec("military_might__shock_and_awe", "a",
    [("atk_ev_bonus", "1/3")],
    ["Strain-Token-Wirtschaft der Karte (Erzeugung nicht modelliert)"],
    "Ausgeben der Token am Angriff = Schadensdruck-Pip; konservatives Surge-Pendant 1/3.")
dec("military_might__show_of_force", "a",
    [("atk_ev_bonus", "3/2")],
    ["Focused wird erst mit dem naechsten Angriff wirksam (RR Focus Fire)"],
    "Focused = eine gruene Wuerfel garantiert (RR-Glossar) = exakter EV auf der Staerke-Schiene.")
dec("military_might__sustained_fire", "a",
    [("order_boost", "5/4")],
    ["Stunned danach ist Zustands-Engine-Detail"],
    "Zusatz-Angriff = Zusatz-Aktion; im Modell einheitlich als 5/4-Aktivierungsokonomie.")
# -------------------------------------------------------------------- nemeses
dec("nemeses__devastating_legion", "b",
    [("engage_acc_delta", "-1")],
    ["Reichweite 3 um Schurken; -1F-Alternative"],
    "Verteidigungs-Eingriff senkt den Heldenangriff — Threat-Hook nach unten.")
dec("nemeses__fearsome_presence", "c", [],
    ["Strain nach Aktivierungsmarkern"],
    "Strain-Injektion ohne Modell-Waehrung (M11d-Seam offen).")
dec("nemeses__im_on_the_leader", "b",
    [("engage_acc_delta", "1")],
    ["Zielwahl 'gesuender Held mit am wenigsten G' bleibt UI-Pfad"],
    "+2 G auf dem Angriff = erhoehter Tauschwert gegen Helden — Threat-Hook (Pip-Boden 1).")
dec("nemeses__indomitable", "b",
    [("attach_lifecycle", "1")],
    ["+1 schwarzer Defense-Wuerfel hat keinen Threat-Block-Seam (konservativ 0)"],
    "Attachment-Lebenszyklus umgesetzt; Defense-Wuerfel und Zustands-Schutz bleiben Engine-Detail.")
dec("nemeses__inspirational", "a",
    [("atk_ev_bonus", "3/4")],
    ["Trait-Gleichheit + Schurken-Wahl (Gruppen-Bedingung)"],
    "+1 Schaden (exakt) und +1 Genauigkeit (nicht gezaehlt) auf dem Angriff — Staerken-Hook.")
dec("nemeses__leave_them_to_me", "b",
    [("deploy_cost", "5")],
    ["Villain-Gruppen muessen im Reservestand sein (Fixture)"],
    "Printed -5 Schurken-Deploy-Kosten auf der BedrohungsOekonomie (Kappe 0).")
dec("nemeses__powerful_foes", "b",
    [("free_group", "1")],
    ["{D}-Gewinn und Deploy-Kosten-Kappung 'by threat level' (variabel)"],
    "Gratis-Schurke in der offenen Hand = Gruppe ohne Deploy-Kosten (BedrohungsOekonomie).")
dec("nemeses__prepare_the_ambush", "a",
    [("order_boost", "1/8")],
    ["Karte-statt-Gruppe-ueberspringen ist Tausch am Rand"],
    "Move-Points der ersten Gruppe pro Runde = Mini-Aktivationsoekonomik (Stufe 1/8).")
dec("nemeses__punishing_force", "a",
    [("order_boost", "5/4"), ("atk_ev_bonus", "1/4")],
    ["Attachment an Schurken; Readiness an Rebel-Aktivierung"],
    "Bereit nach Rebel-Aktivierung = zus. Zyklus; Reroll offen = Reroll-Boden 1/4.")
dec("nemeses__ringleader", "a",
    [("order_boost", "1/8"), ("attach_lifecycle", "1")],
    ["+1 Genauigkeit am Nachbar-Angriff nicht gezaehlt"],
    "Move-Points am Aktivierungsanfang (1/8) auf der Aktivierungs-Schiene.")
# -------------------------------------------------------- overwhelming_oppression
dec("overwhelming_oppression__commence_landing", "a",
    [("atk_ev_bonus", "1/3")],
    ["{C}-Token und Hand-Karten-Wirtschaft; offene Hand ist Info-Thema"],
    "Token auf der Deploy-Karte sind ausgebenbare Angriffs-Pips — Surge-Boden auf der Staerke-Schiene.")
dec("overwhelming_oppression__implacable", "b",
    [("engage_acc_delta", "-1")],
    ["Reichweite 3; {C}-Anteil; Hand-Karten-Wirtschaft"],
    "Fremde {D} defensiv ausgeben senkt den Helden-Tauschwert — Threat-Hook nach unten.")
dec("overwhelming_oppression__indomitable_force", "c", [],
    ["Token verschieben aus der Hand"],
    "Token-Verschiebe-Operation ist Waehrungs-Manipulation jenseits der Hooks.")
dec("overwhelming_oppression__lmitless_arsenal", "c", [],
    ["Pool-Austausch aus der Hand"],
    "Angriffspool gegen Karten-Pool tauschen braucht echte Wuerfel-Mechanik.")
dec("overwhelming_oppression__personal_flagship", "b",
    [("free_group", "1")],
    ["Token-Gewinn bei Schurken-Aktivierung (3 Raeume)"],
    "Gratis-Schurke in der offenen Hand = Gruppe ohne Deploy-Kosten (BedrohungsOekonomie).")
dec("overwhelming_oppression__redouble_our_efforts", "c", [],
    ["Token-Verteilung beim Tod"],
    "Token-Lebenszyklus-Operation (defeat-getriggert) ist Engine-Detail.")
dec("overwhelming_oppression__tie_fighter_patrol", "b",
    [("engage_acc_delta", "1")],
    ["Insight-Test des Helden (Wuerfelpfad) — nur der Schaden ist gezaehlt"],
    "Aktivierungsaktion mit Helden-Schaden erhoht den Tauschwert — Threat-Hook (Pip-Boden 1).")
dec("overwhelming_oppression__tie_landers", "a",
    [("atk_ev_bonus", "2/3")],
    ["Reichweite 5 nach Deploy; Aktivierungsmarker-Zusaatz"],
    "Deploy-Schaden an Helden = gelber Wuerfel (exakt 2/3 EV) auf der Staerke-Schiene.")
dec("overwhelming_oppression__tactical_mastery", "a",
    [("atk_ev_bonus", "1/3")],
    ["Reichweite 3; 'andere Figur' teilt ihre Faehigkeit"],
    "Geteilte {B}-Faehigkeit = Surge-Boden 1/3 auf der Staerke-Schiene.")
# ------------------------------------------------------ power_of_the_dark_side
dec("power_of_the_dark_side__dark_resurgence", "c", [],
    ["Defeat verhindert, 3C recover"],
    "Defeat-Verhinderung + Recovery ohne Modell-Waehrung ist Engine-Detail.")
dec("power_of_the_dark_side__embrace_anger", "a",
    [("atk_ev_bonus", "1")],
    ["Eigener {C}-Kosten wird nicht gegen den Boden gerechnet (konservativ)"],
    "+1 Schaden auf dem eigenen Angriff (exakt) — Staerken-Hook; Selbstkosten bleiben Hinweis.")
dec("power_of_the_dark_side__embrace_fear", "a",
    [("order_boost", "1/8")],
    ["Insight-Steuer am Rebel-Aktivierungsanfang (Wuerfelpfad)"],
    "Move-Point am Aktivierungsanfang = Mini-Aktivationsoekonomik (Stufe 1/8).")
dec("power_of_the_dark_side__embrace_hate", "c", [],
    ["Strain + {D} aus Statusphase"],
    "Strain-Injektion und Token-Erzeugung ohne Modell-Waehrung.")
dec("power_of_the_dark_side__embrace_suffering", "c", [],
    ["Push / fremde Figur angreifen lassen"],
    "Raumoperationen (Push, fremder Angriff) brauchen die echte Welt.")
dec("power_of_the_dark_side__manifest_agression", "a",
    [("atk_ev_bonus", "1/3")],
    ["Start-/Ende-der-Runde-Variante; Token-Ausgabe bleibt offen"],
    "1 {D} pro Aktivierung = ausgebenbarer Angriffspip — Surge-Boden auf der Staerke-Schiene.")
dec("power_of_the_dark_side__supernatural_vigor", "c", [],
    ["temporaere +3 Health bis Rundenende"],
    "Temporäre Gesundheit mit Runden-Ruecknahme ist Zustand-Buchfuehrung (Engine).")
dec("power_of_the_dark_side__the_power_of_passion", "a",
    [("atk_ev_bonus", "1/4")],
    ["+1B beim {D}-Ausgeben (Zusatz zum Token-Pfad)"],
    "Reroll offen am Angriff = Reroll-Boden 1/4 auf der Staerke-Schiene.")
dec("power_of_the_dark_side__unnatural_abilities", "b",
    [("attach_lifecycle", "1")],
    ["+1F/+1P bei Gruppen-groesse 1; Strain am Aktivierungsende"],
    "Attachment-Lebenszyklus umgesetzt; Stat-Zusatz und Strain bleiben Engine-Detail.")
# ---------------------------------------------------------- precision_training
dec("precision_training__assassins", "a",
    [("atk_ev_bonus", "1/4"), ("attach_lifecycle", "1")],
    ["LOS-Negation braucht Welt"],
    "Reroll offen = Reroll-Boden 1/4; Attachment-Lebenszyklus dazu; LOS bleibt Modell-Ausnahme.")
dec("precision_training__exacting_strike", "c", [],
    ["{D}-Bezahlung, Verteidigungswuerfel entfernen"],
    "Bezahlte Verteidigungs-Schwächung ist Wuerfel-Manipulation am Wuerfelpfad.")
dec("precision_training__find_the_weakness", "b",
    [("engage_pierce", "1")],
    ["'+' Genauigkeit-Formel bleibt"],
    "Pierce 1 auf Heldenangriffen ist im Threat-Modell exakt vorhanden — Hook nach oben.")
dec("precision_training__knowledge_of_attack", "b",
    [("engage_acc_delta", "-1")],
    [],
    "Angriff des Helden verliert eine Stufe — Threat-Hook nach unten (exakt).")
dec("precision_training__pinpoint_accuracy", "b",
    [("engage_acc_delta", "1")],
    ["{D}-Bezahlung (Deplete)"],
    "Verteidigung verliert ein G = erhoehter Tauschwert gegen Helden — Threat-Hook.")
dec("precision_training__sharpshooters", "b",
    [("engage_acc_delta", "1")],
    ["Genauigkeit wirkt erst gegen Reichweite/Genauigkeits-Engpass"],
    "+1 Genauigkeit = eine Tauschstufe (Pip-Boden 1) am Threat-Seam.")
dec("precision_training__single_minded", "c", [],
    ["Wuerfelergebnis frei setzen"],
    "Ergebnis-Setzen statt Reroll ist freie Wuerfel-Manipulation.")
dec("precision_training__strike_force", "a",
    [("atk_ev_bonus", "1/8"), ("attach_lifecycle", "1")],
    [],
    "Ein Wuerfel neu im Angriff = konservativer Reroll-Boden auf der Staerke-Schiene; Attachment an die Gruppe.")
dec("precision_training__versatile_attack", "a",
    [("atk_ev_bonus", "5/6")],
    ["{P}-Waehrung fuer Zusatzoptionen (Weaken/Pierce2/+1B)"],
    "Gelber Wuerfel auf dem Angriff (exakt 5/6) auf der Staerke-Schiene; {P}-Optionen bleiben.")
# ------------------------------------------------------------ reactive_defenses
dec("reactive_defenses__active_surveillance", "a",
    [("order_boost", "5/4")],
    ["88-Z-Deploy der Mission; 88-Z {A}-Faehigkeiten"],
    "88-Z aktiviert zusaetzlich zu jeder Imperialen-Aktivierung = Zusatz-Zyklus (Stufe 5/4).")
dec("reactive_defenses__blaster_emplacements", "b",
    [("engage_acc_delta", "2")],
    ["Auswahl an Aktivierungsmarkern; Reichweite 3"],
    "Rundenender-Schaden auf einen Helden erhoht den Tauschwert — Threat-Hook (Pip-Boden 1/Zeile, 2 Zeilen).")
dec("reactive_defenses__electromagnetic_disruptors", "b",
    [("engage_acc_delta", "-1")],
    ["Reichweite 3 um 88-Z; zweite Faehigkeit (Token-Vergabe)"],
    "Heldenangriff verliert eine Genauigkeitsstufe — Threat-Hook nach unten.")
dec("reactive_defenses__infrared_scanners", "a",
    [("order_boost", "1/8")],
    ["88-Z {A}-Faehigkeit (Insight-Steuer)"],
    "Move-Point am Aktivierungsanfang jeder Figur = Mini-Aktivationsoekonomik (1/8).")
dec("reactive_defenses__mechanical_protocol", "b",
    [("engage_acc_delta", "1")],
    ["Droid-Fenster um 88-Z; 88-Z-Strain-Aktion der ersten Faehigkeit"],
    "Droiden-Angriffs-Buff erhoht den Tauschwert gegen Helden — Threat-Hook (Pip-Boden 1).")
dec("reactive_defenses__overclock", "a",
    [("order_boost", "5/4")],
    ["{D}-Gain-Aktion von 88-Z"],
    "88-Z mit 3 Aktionen je Aktivierung = Zusatz-Aktionszyklus (Stufe 5/4).")
dec("reactive_defenses__remote_activator", "a",
    [("order_boost", "5/4"), ("attach_lifecycle", "1")],
    ["Move-Point-Ausgabe als Kosten"],
    "Move-Points bezahlen fuer Aktion = Aktivierungsokonomie (5/4) + Attachment-Lebenszyklus.")
dec("reactive_defenses__shielded", "b",
    [("engage_acc_delta", "-1"), ("attach_lifecycle", "1")],
    ["Energieschilder als Platzhalter-Objekte (Engine)"],
    "Verteidigungs-Buff gegen Angriffe auf die eigene Seite — Threat-Hook nach unten.")
dec("reactive_defenses__targeting_sensors", "b",
    [("engage_acc_delta", "1")],
    ["Reichweite-2-Fenster um 88-Z; {D}-Token-Zusatz nicht gezaehlt"],
    "+1 Genauigkeit am eigenen Angriff = Tauschstufe am Threat-Seam (Pip-Boden 1).")
# ---------------------------------------------------------- subversive_tactics
dec("subversive_tactics__executioner", "b",
    [("engage_acc_delta", "1")],
    ["+1 Schaden pro erlittenem Strain (bis 3) ist der volle Wert; Boden 1"],
    "Angriff auf einen Helden mit Strain kostet ihn mehr — Threat-Hook (Konservativ-Boden).")
dec("subversive_tactics__exploit_weakness", "a",
    [("order_boost", "5/4")],
    ["Ruhe-Trigger (Helden-Zustand) bleibt UI-Pfad"],
    "Interrupt: Bewegung oder Angriff nach Held-Ruhe = Zusatz-Aktion (Stufe 5/4).")
dec("subversive_tactics__heavy_pressure", "c", [],
    ["Strain wird Bedrohung (Waehrungs-tausch)"],
    "Schadensumwandlung in eine andere Waehrung ist echtes Regelwerk jenseits der Hooks.")
dec("subversive_tactics__no_quarter", "a",
    [("focus_surcharge", "1")],
    ["+1B bei >=2 erlittenem B-Wuerfel ist Wuerfelpfad"],
    "Zielbedingung 'Held mit >=2 Schaden' als leichter Ziel-Aufschlag (M11d-Slot, MAX).")
dec("subversive_tactics__oppression", "b",
    [("engage_acc_delta", "-1")],
    ["Strain-Schwelle 2 des Helden"],
    "Verteidigungs-Buff gegen Angriffe erschöpfter Helden — Threat-Hook nach unten.")
dec("subversive_tactics__prey_upon_doubt", "a",
    [("focus_surcharge", "1")],
    ["Beide Helden-Wahlen (1C erleiden / +1F defense) bleiben Detail"],
    "Angriffs-Steuer bei jedem Rebel-Angriff = Reaktions-Druck auf den Ziel-Slot (M11d-Vokabel).")
dec("subversive_tactics__savage_weaponry", "b",
    [("engage_pierce", "1"), ("attach_lifecycle", "1")],
    ["{A}: Bleed ist Zustands-Engine-Detail"],
    "Pierce 1 (exakt) am Threat-Seam + Attachment-Lebenszyklus.")
dec("subversive_tactics__surgical_strike", "a",
    [("atk_ev_bonus", "1")],
    ["Zusaetzliches {C} am Ziel (Strain-Seam offen)"],
    "Fester +1 Schaden nach dem Angriff (exakt) auf der Staerke-Schiene.")
dec("subversive_tactics__weary_target", "b",
    [("engage_acc_delta", "2")],
    ["Auswahl 'Held unter Ausdauer-Schwelle'; Rundenende-Timing"],
    "Rundenender-Schaden auf einen Helden erhoht den Tauschwert — Threat-Hook (Pip-Boden 1/Zeile).")
# ----------------------------------------------------- technological_superiority
dec("technological_superiority__adaptive_weapons", "a",
    [("atk_ev_bonus", "1/12")],
    ["freie Wuerfelwahl; bester sicherer Fall ist Gruen->Blau"],
    "Konservativer Upgrade-Boden des Pool-Tauschs (1/12) auf der Staerke-Schiene.")
dec("technological_superiority__arc_blasters", "b",
    [("attach_lifecycle", "1")],
    ["Blast-Option + Stun-Test nach Fernkampf"],
    "Attachment-Lebenszyklus umgesetzt; Stun/Wuerfel-Option bleiben Engine-Detail.")
dec("technological_superiority__cloaking_device", "b",
    [("attach_lifecycle", "1")],
    ["+1 weisser Defense-Wuerfel hat keinen Threat-Block-Seam (konservativ 0)"],
    "Attachment-Lebenszyklus umgesetzt; Defense-Wuerfel ohne Werte-Seam im Threat-Modell.")
dec("technological_superiority__experimental_arms", "a",
    [("atk_ev_bonus", "1/3"), ("attach_lifecycle", "1")],
    ["Eigener {C} nach der Attacke (Selbstkosten, konservativ nicht abgezogen)"],
    "+1 Blau im Angriff = Surge-Boden auf der Staerke-Schiene; Attachment-Lebenszyklus dazu.")
dec("technological_superiority__failsafe", "c", [],
    ["Defeat verhindert, 3 Move-Points"],
    "Defeat-Verhinderung ist Engine-Regelwerk; Move-Points ohne Welt nichts wert.")
dec("technological_superiority__hidden_detonators", "c", [],
    ["Flaechen-Schaden + eigener Tod"],
    "Selbstzerstoerungs-Flaecheneffekt braucht Welt und Zustands-Engine.")
dec("technological_superiority__jetpacks", "b",
    [("attach_lifecycle", "1")],
    ["Mobile ignoriert Terrain-Kosten — kein Hex-Welt-Seam"],
    "Attachment-Lebenszyklus umgesetzt; Mobile braucht die echte Welt.")
dec("technological_superiority__superior_augments", "a",
    [("atk_ev_bonus", "1/3")],
    ["+1 Speed (keine Welt) als Bedingungs-Trigger"],
    "+1 Blau im Angriff bei Attachment = Surge-Boden auf der Staerke-Schiene.")
dec("technological_superiority__technical_support", "c", [],
    ["Speed + {A}-Aktion der Droiden"],
    "Speed ohne Welt und die {A}-Recovery-Aktion sind Engine-Detail.")


# ------------------------------------------------------------------ corpus
def corpus_saves():
    """Campaign corpus: 7 shipped MissionData campaigns x the app's own hero
    roster x the campaigns' printed villain pools (mission 'villain' fields).
    seedKey = campaign id (a real save's GUID is the same per campaign)."""
    heroes = json.load(open(HEROES))
    hero_count = len(heroes)
    saves = []
    for f in sorted(MISSION_DIR.glob("*.json")):
        missions = json.load(open(f))
        villains = []
        agenda = 0
        for m in missions:
            for v in (m.get("villain") or []):
                if v:
                    villains.append({"name": v})
            types = m.get("missionType") or []
            if isinstance(types, str):
                types = [types]
            if any(t.lower() == "agenda" for t in types):
                agenda += 1
        save = {
            "GUID": f.stem,
            "campaignHeroes": [{"heroID": h["id"]} for h in heroes],
            "campaignVillains": villains,
            "campaignStructure": (
                [{"missionType": 0, "isAgendaMission": True}] * agenda +
                [{"missionType": 0}] * (len(missions) - agenda)),
        }
        saves.append(save)
    return saves


def corpus_purchase_counts(table):
    """Per campaign: the M12b deck choice + the M12c greedy purchase ladder
    over mission-shaped XP budgets (0..42 in steps of 3 - a campaign books
    XP between missions; 42 covers the full paid deck 2x). The corpus view
    = the distinct set of paid cards the ladder ever touches."""
    freq = {}
    per_campaign = {}
    for save in corpus_saves():
        cid = save["GUID"]
        prof = dt.profile_from_save(save)
        seed = dt.deck_seed_for(cid)
        deck_id = dt.choose(table, prof, seed)[0]["deck"]
        deck = next(d for d in table["decks"] if d["id"] == deck_id)
        owned = {c["id"] for c in deck["cards"] if c.get("basic")}
        bought, balance = [], 0
        for _budget_step in range(14):          # 14 x +3 XP = 42
            balance += 3
            balance = pt.run_stage(deck, prof, owned, balance, seed, bought)
        per_campaign[cid] = {"deck": deck_id, "bought": sorted(set(bought))}
        for c in bought:
            freq[c] = freq.get(c, 0) + 1
    return freq, per_campaign


# ------------------------------------------------------------------ main
def main():
    check_only = "--check" in sys.argv
    asset = json.load(open(ASSET))
    cards = {}
    order = []
    for deck in asset["decks"]:
        for c in deck["cards"]:
            cards[c["id"]] = c
            order.append((deck["id"], c["id"]))

    # ---- validation of the decision table against the shipped asset
    errs = []
    if set(D) != set(cards):
        missing = set(cards) - set(D)
        extra = set(D) - set(cards)
        errs.append("decision table mismatch: missing=%s extra=%s"
                    % (sorted(missing)[:5], sorted(extra)[:5]))
    for cid, (bucket, hooks, partial, reason) in D.items():
        c = cards.get(cid)
        if c is None:
            continue
        if bucket not in "abc" or len(bucket) != 1:
            errs.append(cid + ": bad bucket " + bucket)
        if bucket == "c" and hooks:
            errs.append(cid + ": bucket c must carry NO hooks")
        if bucket in "ab" and not hooks:
            errs.append(cid + ": bucket " + bucket + " needs >=1 consumed hook")
        consumed = [k for k, _ in hooks
                    if k not in ("stat_speed",)]
        if bucket in "ab" and not consumed:
            errs.append(cid + ": no consumed hook")
        for kind, value in hooks:
            v = VOCAB.get(kind)
            if v is None:
                errs.append(cid + ": unknown hook " + kind)
                continue
            if value not in v["values"]:
                # engage_acc_delta may carry any +/- pip print (floor 1)
                if kind == "engage_acc_delta" and abs(int(value)) >= 1:
                    continue
                if kind == "deploy_cost" and int(value) >= 1:
                    continue
                errs.append(cid + ": hook value " + value + " not in vocab " + kind)
        # restriction consistency: a printed Attachment restriction MUST be
        # carried by the lifecycle hook (hard rule, RR "Attachment"); the
        # hook may exist on unrestricted prints too (none shipped with one).
        restr = (c.get("restriction") or "").lower()
        has_attach = any(k == "attach_lifecycle" for k, _ in hooks)
        if "attachment" in restr and not has_attach:
            errs.append(cid + ": printed Attachment without attach_lifecycle")
        if not reason.strip():
            errs.append(cid + ": empty reason")

    # ---- corpus + threshold
    freq, per_campaign = corpus_purchase_counts(asset)
    paid = {cid for cid, c in cards.items() if not c.get("basic")}
    basics = {cid for cid, c in cards.items() if c.get("basic")}
    implemented = {cid for cid, (b, _, _, _) in D.items() if b in "ab"}
    bought_cards = set(freq)
    impl_bought = bought_cards & implemented
    share = (len(impl_bought) / len(bought_cards)) if bought_cards else 1.0
    basics_impl = basics & implemented
    if len(basics_impl) < len(basics):
        errs.append("basic coverage %d/%d" % (len(basics_impl), len(basics)))
    if share < 0.60:
        errs.append("purchase coverage %.1f%% < 60%%" % (share * 100))

    if errs:
        print("VALIDATION ERRORS:")
        for e in errs:
            print("  -", e)
        return 1

    # ---- emit
    out = {
        "schema": "tacai.imperial_card_effects/1",
        "source": "1:1 classification of tactical_imperial_decks.json "
                  "(RR Faehigkeiten-Matrix: no-XP ability = passive; horizontal-line = "
                  "independent ability; unpaid cost -> following text not executed)",
        "conventions": {
            "buckets": {
                "a": "planer-modifier: hook lands on the activation-order / target "
                     "strength pipeline (OrderPlanner)",
                "b": "stat-modifier: hook lands on an existing value seam (threat "
                     "engagement, deployment economy) or a store-model seam "
                     "(attachment lifecycle, effective Health)",
                "c": "engine-detail: real rules work needed (dice manipulation, "
                     "token/condition operations, spatial legality). ACTIVE in the "
                     "game, WITHOUT an AI model - shown honestly in the M12e UI.",
            },
            "determinism": "hooks only modify the existing assessment pipeline "
                           "(no new RNG pot, no hidden information, printed floors only)",
            "sides": "effects belong to the side owning the card; the engine "
                     "distinguishes faction only, never AI vs human hand",
        },
        "hooks": VOCAB,
        "cards": [
            {
                "id": cid,
                "deck": deck,
                "basic": bool(cards[cid].get("basic")),
                "xp": cards[cid].get("xp"),
                "bucket": D[cid][0],
                "hooks": [{"kind": k, "value": v} for k, v in D[cid][1]],
                "partial": D[cid][2],
                "reason": D[cid][3],
            }
            for deck, cid in order
        ],
    }

    a = sum(1 for v in D.values() if v[0] == "a")
    b = sum(1 for v in D.values() if v[0] == "b")
    cc = sum(1 for v in D.values() if v[0] == "c")
    per_deck = {}
    for deck, cid in order:
        d = per_deck.setdefault(deck, {"a": 0, "b": 0, "c": 0})
        d[D[cid][0]] += 1
    stats = {
        "buckets_total": {"a": a, "b": b, "c": cc, "cards": len(D)},
        "buckets_per_deck": per_deck,
        "basics_implemented": "%d/%d" % (len(basics_impl), len(basics)),
        "purchase_coverage": {
            "corpus_campaigns": sorted(per_campaign),
            "distinct_bought": len(bought_cards),
            "implemented_bought": len(impl_bought),
            "share": round(share * 100, 1),
            "implemented_list": sorted(impl_bought),
            "missed_list": sorted(bought_cards - implemented),
        },
    }
    print(json.dumps(stats, indent=1, ensure_ascii=False))
    if not check_only:
        OUT.write_text(json.dumps(out, indent=1, ensure_ascii=False) + "\n",
                       encoding="utf-8")
        print("wrote", OUT)
    print("OK: all 109 classified; basics %d/%d; purchase coverage %.1f%% (>=60%%)"
          % (len(basics_impl), len(basics), share * 100))
    return 0


if __name__ == "__main__":
    sys.exit(main())
