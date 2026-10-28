#!/usr/bin/env python3
"""M12d2 scraper: Agenda cards + Agenda sets from imperial-assault.fandom.com.

Doctrine note (M11g / LICENSE Punkt 5): the printed card texts stay BUILD
content (player information); the fan wiki is named as the transcription
medium only. This scraper captures the RAW wikitext of every Agenda Set page
and every referenced Agenda Card page into ../raw/agenda_cards/ (audit
trail), and parses a structured intermediate into
../raw/agenda_cards/parsed.json. It performs NO merging into the shipped
asset - that is gen_agenda_cards.py's job (separate audit step, same split
as M12a's scrape/extract/validate pipeline).

Set membership is authoritative on the SET pages (RR "Building the Agenda
Deck": "Each Agenda set consists of three cards. The card's set is written
on the upper right corner of the card."). The card-page infobox repeats the
set link; both are captured and must agree (mismatch -> needs_review).

Run:  python3 wiki_data/tools/scrape_agenda_cards.py
"""
import json
import os
import re
import urllib.parse
import urllib.request

HERE = os.path.dirname(os.path.abspath(__file__))
RAW = os.path.join(HERE, "..", "raw", "agenda_cards")
API = "https://imperial-assault.fandom.com/api.php"
UA = {"User-Agent": "Mozilla/5.0 (M12d2 data curation; contact werth IT)"}


def api_get(params):
    q = urllib.parse.urlencode(params)
    req = urllib.request.Request(API + "?" + q, headers=UA)
    with urllib.request.urlopen(req, timeout=60) as r:
        return json.load(r)


def category_members(cat):
    out, cont = [], {}
    while True:
        params = {"action": "query", "list": "categorymembers",
                  "cmtitle": "Category:" + cat, "cmlimit": "500",
                  "format": "json"}
        params.update(cont)
        d = api_get(params)
        out += d["query"]["categorymembers"]
        if "continue" not in d:
            return out
        cont = d["continue"]


def revisions(titles):
    """Batched wikitext fetch (50 titles per call). {title: wikitext}."""
    out = {}
    pages = api_get({"action": "query", "prop": "revisions",
                     "rvprop": "content", "rvslots": "main",
                     "redirects": 1, "format": "json",
                     "titles": "|".join(titles)})["query"]["pages"]
    for k, v in pages.items():
        if "missing" in v or "revisions" not in v:
            out[v.get("title", k)] = None
            continue
        rt = v.get("redirected")
        title = rt if rt else v["title"]
        out[title] = v["revisions"][0]["slots"]["main"]["*"]
    return out


def norm_title(t):
    return t.replace("_", " ").strip()


# ---------------------------------------------------------------- set pages
def parse_set_page(wt):
    """{{Agenda_Set_Infobox|source = ...|agenda1 = [[..]]|agenda2=..|agenda3=..}}"""
    m = re.search(r"\{\{Agenda_Set_Infobox(.*?)\}\}", wt, re.S | re.I)
    if not m:
        return None
    body = m.group(1)
    src = re.search(r"\|\s*source\s*=\s*(.*?)(?:\||\n)", body)
    cards = re.findall(r"\|\s*agenda[123]\s*=\s*\[\[([^\]|]+)(?:\|[^\]]+)?\]\]", body)
    src = src.group(1).strip().strip("[]") if src else ""
    return {"source": src, "cards": [norm_title(c) for c in cards]}


# --------------------------------------------------------------- card pages
def parse_card_page(title, wt):
    m = re.search(r"\{\{Agenda_Card_Infobox(.*?)\}\}", wt, re.S | re.I)
    info = {}
    if m:
        body = m.group(1)
        for key in ("card", "agenda_set", "cost", "type", "source",
                    "time_period", "timeperiod"):
            km = re.search(r"\|\s*" + key + r"\s*=\s*(.*?)(?:\||\n)", body, re.S)
            if km:
                info[key] = km.group(1).strip()
    desc = re.search(r"==\s*Description\s*==(.*?)(?:\n==|\Z)", wt, re.S | re.I)
    action = re.search(r"==\s*(Action|Play)\s*==(.*?)(?:\n==|\Z)", wt, re.S | re.I)
    cats = re.findall(r"\[\[Category:([^\]|]+)", wt)
    # the wiki uses one section per card; some print it under the type word
    if not action:
        for t in ("Side Mission", "Forced Mission", "Ongoing", "Kept Secret"):
            action = re.search(r"==\s*" + t + r"\s*==(.*?)(?:\n==|\Z)", wt, re.S | re.I)
            if action:
                break
    return {
        "title": title,
        "raw": wt,
        "infobox": info,
        "description": desc.group(1).strip() if desc else "",
        "action": action.group(1).strip() if action else "",
        "categories": cats,
    }


def main():
    os.makedirs(RAW, exist_ok=True)
    sets = category_members("Agenda Set")
    set_pages = {}
    for s in sets:
        t = norm_title(s["title"])
        wt = api_get({"action": "parse", "page": t, "redirects": 1,
                      "prop": "wikitext", "format": "json"})["parse"]["wikitext"]["*"]
        set_pages[t] = wt

    card_titles = []
    parsed_sets = {}
    for t, wt in set_pages.items():
        p = parse_set_page(wt)
        parsed_sets[t] = {"raw": wt, "parsed": p}
        if p:
            for c in p["cards"]:
                if c not in card_titles:
                    card_titles.append(c)

    # plus everything in the small Category:Agenda Card (may add strays)
    for s in category_members("Agenda Card"):
        t = norm_title(s["title"])
        if t not in card_titles:
            card_titles.append(t)

    card_pages = {}
    for i in range(0, len(card_titles), 50):
        batch = card_titles[i:i + 50]
        card_pages.update(revisions(batch))

    parsed_cards = {}
    for t, wt in card_pages.items():
        parsed_cards[t] = parse_card_page(t, wt) if wt else {"title": t, "raw": None}

    out = {"sets": parsed_sets, "cards": parsed_cards,
           "set_count": len(parsed_sets),
           "card_title_count": len(card_titles)}
    with open(os.path.join(RAW, "parsed.json"), "w", encoding="utf-8") as f:
        json.dump(out, f, ensure_ascii=False, indent=1)
    for t, wt in set_pages.items():
        fn = re.sub(r"[^\w \-'.()]", "_", t) + ".set.wikitext"
        with open(os.path.join(RAW, fn), "w", encoding="utf-8") as f:
            f.write(wt)
    for t, wt in card_pages.items():
        if wt is None:
            continue
        fn = re.sub(r"[^\w \-'.()]", "_", t) + ".wikitext"
        with open(os.path.join(RAW, fn), "w", encoding="utf-8") as f:
            f.write(wt)
    missing = [t for t, wt in card_pages.items() if wt is None]
    print("sets=%d card_titles=%d cards_missing=%d" %
          (len(parsed_sets), len(card_titles), len(missing)))
    if missing:
        print("MISSING:", missing)


if __name__ == "__main__":
    main()
