#!/usr/bin/env python3
"""M12d2 merge: wiki parse (authoritative structure) + vision cross-check
(raw/agenda_cards/vision/) -> wiki_data/agenda_cards.json (working copy) and
Assets/Resources/Languages/En/tactical_agenda_cards.json (shipped asset).

Schema 'tacai.agenda_cards/1' is fixed by the controller; see the source and
conventions strings written below. This script merges, it does not invent:
every field traces either to the fandom wiki wikitext, the wiki set pages
(set membership), or the VLM transcription of the imperial-assault.com scans
(time_period is vision-only). Conflicts keep the wiki value and set
needs_review. Run after vision_agenda_cards.py (missing cache entries mark
the card needs_review 'vision pending').

Run:  python3 wiki_data/tools/gen_agenda_cards.py
"""
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
RAW = os.path.join(HERE, "..", "raw", "agenda_cards")
PARSED = os.path.join(RAW, "parsed.json")
VISION_CACHE = os.path.join(RAW, "vision")
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
OUT_WORKING = os.path.join(REPO, "wiki_data", "agenda_cards.json")
OUT_ASSET = os.path.join(REPO, "ImperialCommander2", "Assets", "Resources",
                         "Languages", "En", "tactical_agenda_cards.json")
OUT_REPORT = os.path.join(RAW, "pipeline_report.json")
# targeted second vision pass over bracketed icon placeholders left in
# vision-sourced texts (raw/agenda_cards/icon_recheck.json, see the M12d2
# commit notes): deterministic post-processing over the cached answers only.
ICON_RECHECK = os.path.join(RAW, "icon_recheck.json")

SOURCE = (
    "Card names, printed rules texts, types and Influence costs were "
    "transcribed 1:1 from imperial-assault.fandom.com (the fan wiki is named "
    "as transcription medium only, per wiki_data/LICENSE_DATA.md Punkt 5; the "
    "printed ability texts stay build content so the player knows what the "
    "card does). Set membership is authoritative on the wiki Agenda Set pages "
    "(RR: each Agenda set consists of three cards). Each field was "
    "cross-checked against a VLM transcription of the native card scans on "
    "imperial-assault.com/Agenda_Decks (see raw/agenda_cards/vision/ for the "
    "per-card cache incl. image sha256); disagreements keep the wiki value "
    "and raise needs_review. 'time_period' is a vision-only field: it is "
    "read off the printed scan and null when no time period is printed. "
    "Honest scope: the 30 scan images in the Agenda_Decks inventory that "
    "have no wiki card page (expansion / Villain-Pack strays) are NOT in "
    "this corpus; the corpus is the 42 complete 3-card Agenda sets "
    "documented on the wiki (126 cards)."
)

CONVENTIONS = {
    "types": "side_mission|forced_mission|ongoing|kept_secret (RR 'Agenda Cards' types)",
    "ids": "card = <set_slug>__<card_slug>, set = slug(set name); slugs lowercase ascii, non-alnum->'_'",
    "tokens": "house token set per tactical_imperial_decks.json: {H} damage {B} surge {C} strain {D} threat {A} action {U} upgrade {O} ranged {P} melee {G} block {F} evade; dice colors as words ('1 blue die'); Strength/Tech/Insight tests as words",
    "wiki_token_map": "wiki template -> house: {{D}}->{H} {{Z}}->{B} {{S}}->{C} {{H}}->{D} {{A}}->{A} {{P}}->{O} {{L}}->{P} {{O}}->{G} {{V}}/{{X}}->{F} (wiki V='evade', X='dodge' icons); {{F}}->'Strength' {{C}}->'Tech' {{I}}->'Insight' test words; {{B}}/{{R}}/{{G}}/{{Y}}/{{W}}/{{K}} -> 'blue|red|green|yellow|white|black defense die'",
    "time_period": "null = none printed (RR: card then legal in every campaign); [lo,hi] ints as printed; a single printed number N is stored as [N,N]; sourced from the vision transcription only",
    "cost": "printed Influence purchase cost (RR 'Agenda Cards'); resolution-time extra costs stay inside 'text', machine-parsed into resolution_cost only via the documented regexes below",
    "resolution_cost": "regexes applied verbatim to the converted text: r'spend (\\d+) threat' -> threat; r'spend (\\d+) Force tokens?' (also bare 'spend (\\d+) Force') -> force; null if neither matches; never inferred from icons",
}

# wiki template letter -> house rendering (verified against the wiki's own
# Template: pages; V/X are the evade/dodge defense icons, I the Insight test)
TOKEN_MAP = {
    "D": "{H}", "Z": "{B}", "S": "{C}", "H": "{D}", "A": "{A}",
    "P": "{O}", "L": "{P}", "O": "{G}", "V": "{F}", "X": "{F}",
    "F": "Strength", "C": "Tech", "I": "Insight",
    "B": "blue die", "R": "red die", "G": "green die", "Y": "yellow die",
    "W": "white die", "K": "black defense die",
}

TYPE_MAP = {
    "keptsecret": "kept_secret", "secret": "kept_secret",
    "sidemission": "side_mission", "side": "side_mission",
    "forcedmission": "forced_mission", "forced": "forced_mission",
    "ongoing": "ongoing",
}


def norm(s):
    if not s:
        return ""
    s = s.replace("\u2019", "'").replace("\u2018", "'")
    return re.sub(r"[^a-z0-9]+", "", s.lower())


def slug(s):
    return re.sub(r"[^a-z0-9]+", "_",
                  s.lower()).strip("_").encode("ascii", "ignore").decode()


def clean_link(v):
    """'[[Foo Bar (X)|Foo Bar]]' -> 'Foo Bar'; plain text passes through."""
    m = re.match(r"\s*\[\[(?:[^|\]]*\|)?([^\]]+)\]\]\s*$", v or "")
    if m:
        return m.group(1).strip()
    return (v or "").strip()


def parse_infobox(raw):
    m = re.search(r"\{\{\s*Agenda[_ ]Card[_ ]Infobox(.*?)\}\}", raw, re.S | re.I)
    if not m:
        return {}
    body = m.group(1)
    info = {}
    for key in ("card", "agenda_set", "cost", "type", "source"):
        km = re.search(r"\|\s*" + key + r"\s*=\s*(\[\[[^\]]*\]\]|[^|\n]*)", body)
        if km:
            info[key] = km.group(1).strip()
    return info


def action_body(raw, card_type=""):
    """Rules-text section body (the M12d2 scraper stored only the section
    NAME in parsed.json['action']; re-extract from raw wikitext)."""
    m = re.search(r"==\s*(?:Action|Play)\s*==\s*(.*?)(?:\n==|\Z)", raw, re.S | re.I)
    if not m and card_type:
        m = re.search(r"==\s*" + re.escape(card_type) + r"\s*==\s*(.*?)(?:\n==|\Z)",
                      raw, re.S | re.I)
    if not m:
        for t in ("Side Mission", "Forced Mission", "Ongoing", "Kept Secret"):
            m = re.search(r"==\s*" + t + r"\s*==\s*(.*?)(?:\n==|\Z)", raw, re.S | re.I)
            if m:
                break
    if not m:
        return ""
    return strip_markup(m.group(1))


def desc_body(raw):
    m = re.search(r"==\s*Description\s*==\s*(.*?)(?:\n==|\Z)", raw, re.S | re.I)
    return strip_markup(m.group(1)) if m else ""


def strip_markup(s):
    s = re.sub(r"\[\[Category:[^\]]*\]\]", "", s)           # audit trails
    # [[File:...]] embeds can nest; peel innermost repeatedly
    prev = None
    while prev != s:
        prev = s
        s = re.sub(r"\[\[(?:File|Image):[^\[\]]*\]\]", "", s, flags=re.I)
    s = re.sub(r"\[\[(?:[^|\]]*\|)?([^\]]+)\]\]", r"\1", s)  # wiki links
    s = s.replace("'''", "").replace("''", "")               # bold/italics
    s = re.sub(r"<br\s*/?>", " ", s, flags=re.I)
    return s


def convert_tokens(s, warnings):
    """wiki {{X}} icon templates -> house tokens / words per TOKEN_MAP."""
    def sub(m):
        k = m.group(1)
        if k in TOKEN_MAP:
            return TOKEN_MAP[k]
        warnings.append("unmapped token {{%s}}" % k)
        return m.group(0)
    return re.sub(r"\{\{\s*([A-Za-z]+)\s*\}\}", sub, s)


def finalize_text(s):
    return re.sub(r"\s+", " ", s).strip()


def word_overlap(a, b):
    """Fraction of words of a present in b (multiset, min-count)."""
    wa = re.findall(r"[a-z0-9{}]+", (a or "").lower())
    wb = re.findall(r"[a-z0-9{}]+", (b or "").lower())
    if not wa:
        return 1.0 if not wb else 0.0
    from collections import Counter
    ca, cb = Counter(wa), Counter(wb)
    inter = sum(min(ca[w], cb[w]) for w in ca)
    return inter / len(wa)


RESOLUTION_THREAT = re.compile(r"spend (\d+) threat", re.I)
RESOLUTION_FORCE = re.compile(r"spend (\d+) Force(?: tokens?)?(?![a-z])", re.I)


def resolution_cost(text):
    t = RESOLUTION_THREAT.search(text)
    f = RESOLUTION_FORCE.search(text)
    if not t and not f:
        return None
    return {"threat": int(t.group(1)) if t else 0,
            "force": int(f.group(1)) if f else 0}


def load_vision(card):
    p = os.path.join(VISION_CACHE, slug(card) + ".json")
    if not os.path.exists(p):
        return None
    return json.load(open(p, encoding="utf-8"))


def main():
    d = json.load(open(PARSED, encoding="utf-8"))
    raw_cards = {k: v for k, v in d["cards"].items()
                 if not k.startswith("Category:")}
    sets = d["sets"]

    # ---- set membership: authoritative on the set pages, agenda1..3 order
    cards_norm = {norm(t): t for t in raw_cards}
    set_of = {}          # card title -> set page title
    set_order = {}       # set page title -> [card titles in agenda1..3 order]
    for sname, s in sets.items():
        ordered = []
        for c in (s["parsed"] or {}).get("cards", []):
            t = cards_norm.get(norm(c))
            if t is None:
                sys.exit("set page '%s' names unknown card '%s'" % (sname, c))
            set_of[t] = sname
            ordered.append(t)
        set_order[sname] = ordered

    long_warns, warnings_log = [], {}
    icon_map = {}
    if os.path.exists(ICON_RECHECK):
        icon_map = json.load(open(ICON_RECHECK, encoding="utf-8"))
    cards_out = []
    needs_review_rows = []
    for title, e in raw_cards.items():
        raw = e["raw"]
        info = parse_infobox(raw)
        sname = set_of[title]
        sinfo = sets[sname]["parsed"] or {}
        warns = []
        review = []

        cats = e.get("categories") or []
        # ---- type: infobox, else '<X> Agenda Card' category
        wtype = info.get("type", "")
        if not wtype:
            for c in cats:
                m = re.match(r"(.+) Agenda Card$", c)
                if m and norm(m.group(1)) in TYPE_MAP:
                    wtype = m.group(1)
                    break
        wtype_norm = TYPE_MAP.get(norm(wtype)) if wtype else None
        if not info.get("type") or not info.get("cost"):
            review.append("infobox fields incomplete (type/cost from category+vision)")

        # ---- cost (wiki authoritative)
        wiki_cost = None
        if info.get("cost", "").strip().isdigit():
            wiki_cost = int(info["cost"].strip())

        # ---- text: action section of the wiki page, token-mapped
        text = finalize_text(convert_tokens(action_body(raw, wtype), warns))
        flavor = finalize_text(desc_body(raw))

        # ---- vision cross-check
        vis = load_vision(title)
        vobj = (vis or {}).get("obj") or {}
        source = clean_link(info.get("source", "")) or clean_link(sinfo.get("source", ""))
        if vis is None:
            review.append("vision pending")
        else:
            if not vis.get("ok"):
                review.append(vis.get("reason", "vision failed"))
            vc, vt, vs = vobj.get("cost"), norm(vobj.get("type", "")), norm(vobj.get("set", ""))
            if wiki_cost is not None and isinstance(vc, int) and vc != wiki_cost:
                review.append("cost mismatch wiki=%s vision=%s (wiki kept)" % (wiki_cost, vc))
            if wiki_cost is None and isinstance(vc, int):
                wiki_cost = vc  # only source available; already flagged above
            vt_norm = TYPE_MAP.get(vt)
            if wtype_norm and vt_norm and vt_norm != wtype_norm:
                review.append("type mismatch wiki=%s vision=%s (wiki kept)" % (wtype_norm, vt_norm))
            # printed set labels drop disambiguation suffixes, e.g. wiki page
            # 'Guild Hunters (Imperial Agenda)' prints 'GUILD HUNTERS'
            if vs and vs not in norm(sname) and norm(sname) not in vs:
                review.append("set label mismatch wiki=%s vision=%s (wiki kept)" % (sname, vobj.get("set")))
            vtext = vobj.get("text") or ""
            if not text and vtext:
                text = finalize_text(vtext)
                review.append("wiki action section empty; text vision-sourced")
            elif text and vtext and word_overlap(text, vtext) < 0.7:
                review.append("text divergence (wiki kept, overlap=%.2f)" %
                              word_overlap(text, vtext))
        if not text:
            review.append("no rules text available")

        if text and len(text) > 1200:
            long_warns.append("%s: %d chars" % (title, len(text)))
        for bad in ("{{", "[[", "</"):
            if bad in text:
                review.append("markup residue %r in text" % bad)
        if warns:
            review.extend(sorted(set(warns)))

        # ---- icon placeholder normalization (cached second vision pass).
        # Only placeholders with an explicit resolved answer are replaced;
        # unresolved ones keep their brackets and raise needs_review.
        card_id = slug(sname) + "__" + slug(title)
        ic = icon_map.get(card_id) or {}
        if isinstance(ic, dict) and text:
            for ph, val in ic.items():
                if isinstance(val, str) and not ic.get("error"):
                    text = text.replace(ph, val)
            still = re.findall(r"\[[^\]]{1,30}\]", text)
            if still:
                review.append("unresolved icon placeholder %s" % ",".join(still))

        time_period = vobj.get("time_period") if vis else None
        card = {
            "id": slug(sname) + "__" + slug(title),
            "name": title,
            "set": slug(sname),
            "source": source,
            "type": wtype_norm or "?",
            "cost": wiki_cost,
            "time_period": time_period,
            "text": text,
            "flavor": flavor,
            "resolution_cost": resolution_cost(text),
            "needs_review": bool(review),
            "review_reason": "; ".join(review),
        }
        cards_out.append(card)
        warnings_log[card["id"]] = review
        if review:
            needs_review_rows.append((card["id"], card["review_reason"]))

    cards_out.sort(key=lambda c: c["id"])
    sets_out = []
    for sname in sorted(sets):
        sinfo = sets[sname]["parsed"] or {}
        sets_out.append({
            "id": slug(sname),
            "name": sname,
            "source": clean_link(sinfo.get("source", "")),
            "cards": [slug(sname) + "__" + slug(t) for t in set_order[sname]],
        })
    sets_out.sort(key=lambda s: s["id"])

    doc = {"schema": "tacai.agenda_cards/1", "source": SOURCE,
           "conventions": CONVENTIONS, "sets": sets_out, "cards": cards_out}

    for path in (OUT_WORKING, OUT_ASSET):
        os.makedirs(os.path.dirname(path), exist_ok=True)
        with open(path, "w", encoding="utf-8") as f:
            json.dump(doc, f, ensure_ascii=False, indent=1)
            f.write("\n")

    # ---- stats
    from collections import Counter
    by_type = dict(sorted(Counter(c["type"] for c in cards_out).items()))
    by_cost = {str(k): v for k, v in
               sorted(Counter(str(c["cost"]) for c in cards_out).items())}
    tp_nonnull = sum(1 for c in cards_out if c["time_period"])
    vision_ok = sum(1 for t in raw_cards
                    if (load_vision(t) or {}).get("ok"))
    res_costs = sum(1 for c in cards_out if c["resolution_cost"])
    report = {
        "generated_by": "gen_agenda_cards.py",
        "sets": len(sets_out), "cards": len(cards_out),
        "by_type": by_type, "by_cost": by_cost,
        "time_period_nonnull": tp_nonnull,
        "vision_crosschecked": vision_ok,
        "vision_no_image": sum(
            1 for t in raw_cards
            if (load_vision(t) or {}).get("reason") == "no image"),
        "vision_pending": sum(1 for t in raw_cards if load_vision(t) is None),
        "needs_review_count": sum(1 for c in cards_out if c["needs_review"]),
        "needs_review_rows": [{"id": i, "reason": r} for i, r in needs_review_rows],
        "resolution_cost_nonnull": res_costs,
        "long_text_warnings": long_warns,
    }
    json.dump(report, open(OUT_REPORT, "w", encoding="utf-8"),
              ensure_ascii=False, indent=1)

    print("sets=%d cards=%d" % (len(sets_out), len(cards_out)))
    print("by_type:", json.dumps(by_type))
    print("by_cost:", json.dumps(by_cost))
    print("time_period non-null:", tp_nonnull)
    print("resolution_cost non-null:", res_costs)
    print("vision: crosschecked=%d pending=%d no_image=%d" %
          (vision_ok, report["vision_pending"], report["vision_no_image"]))
    print("needs_review=%d" % report["needs_review_count"])
    for i, r in needs_review_rows:
        print("  REVIEW %-55s %s" % (i, r))
    for w in long_warns:
        print("  LONG  ", w)
    print("wrote:", OUT_WORKING)
    print("wrote:", OUT_ASSET)
    print("wrote:", OUT_REPORT)

    # ---- spot-checks
    def dump(card):
        print("SPOT-CHECK", json.dumps(card, ensure_ascii=False, indent=1))
    ks = next((c for c in cards_out if c["type"] == "kept_secret"
               and c["time_period"]), None)
    if ks is None:  # no Kept Secret prints a time period; show one anyway
        ks = next((c for c in cards_out if c["time_period"]), None)
    sm = next((c for c in cards_out if c["type"] == "side_mission"), None)
    og = next((c for c in cards_out if c["type"] == "ongoing"), None)
    for c in (ks, sm, og):
        if c:
            dump(c)


if __name__ == "__main__":
    main()
