#!/usr/bin/env python3
"""Mission income editor (Option A, Thomas' decision 30.10.).

Inserts the Imperial-side income actions CM6 (type 31, imperial XP) and
CM7 (type 32, influence) into shipped saga missions, mirroring each CM1
CampaignModifyXP (type 24) anchor 1:1 - same event branch, same value.

Editorial policy (deterministic, see findings_level_phase_qa_2026-10-30.md):
- anchor: every type-24 action with xpToAdd > 0 (0-anchors skipped honestly)
- CM6: xpToAdd = anchor value, reason "mission-reward"
- CM7: influenceToAdd = anchor value, reason "mission-reward"
  (agenda market prices are 1-4 influence -> same tempo logic)
- the mission authors' CM1 decisions ARE the amounts: per mission, per
  outcome branch, author-intended; a rebel-victory branch pays the Imperial
  side too - there the Imperium DID achieve its objective.
- new actions carry NO player text, so translation files need no entries
  (TranslationController overrides texts by GUID only)
- idempotent: files that already contain type-31 are skipped
- validation gates run before anything hits disk; any failure aborts the
  whole file untouched

Usage: python3 tools/mission_income_editor.py   # from repo root ImperialCommander2/ [--dry] [NameFilter ...]
"""
import glob, json, re, sys, uuid

ROOT = 'ImperialCommander2/Assets'   # run from repo root

def anchors(raw):
    """(start, xp) spans of the flat type-24 action objects."""
    out = []
    for m in re.finditer(r'"eventActionType": 24', raw):
        start = raw.rfind('{', 0, m.start())
        end = raw.find('}', m.end())
        obj = raw[start:end + 1]
        if '{' in obj[1:-1]:
            sys.exit('anchor object not flat - abort, nothing edited')
        xpm = re.search(r'"xpToAdd": (\d+)', obj)
        if not xpm:
            sys.exit('anchor without xpToAdd - abort, nothing edited')
        out.append((start, int(xpm.group(1))))
    return sorted(out)

def pair(xp):
    """The two new action objects, sibling-ready (ends with ',<newline>indent')."""
    return ('{\n          "xpToAdd": %d,\n          "GUID": "%s",\n'
            '          "eventActionType": 31,\n          "displayName": "Modify Imperial XP",\n'
            '          "reason": "mission-reward"\n        },\n        '
            '{\n          "influenceToAdd": %d,\n          "GUID": "%s",\n'
            '          "eventActionType": 32,\n          "displayName": "Modify Imperial Influence",\n'
            '          "reason": "mission-reward"\n        },\n        ') % (xp, uuid.uuid4(), xp, uuid.uuid4())

def walk(o):
    if isinstance(o, dict):
        yield o
        for v in o.values(): yield from walk(v)
    elif isinstance(o, list):
        for v in o: yield from walk(v)

def edit(path, dry):
    raw = open(path, encoding='utf-8').read()
    if '"eventActionType": 31' in raw:
        return 'already edited'
    sp = anchors(raw)
    if not sp:
        return 'no CM1 anchor (untouched)'
    pos = [(s, x) for s, x in sp if x > 0]
    if not pos:
        return 'all anchors xp 0 (untouched)'
    out, off = raw, 0
    for start, xp in pos:                       # left-to-right, offset-tracked
        ins = pair(xp)
        i = start + off
        out = out[:i] + ins + out[i:]
        off += len(ins)
    if dry:
        return 'DRY %d inserts (%d anchors)' % (2 * len(pos), len(pos))
    try:
        d = json.loads(re.sub(r',(\s*[}\]])', r'\1', out))
    except Exception as e:
        sys.exit('post-edit JSON invalid in %s: %s - file NOT written' % (path, e))
    t24 = sum(1 for x in walk(d) if x.get('eventActionType') == 24)
    t31 = sum(1 for x in walk(d) if x.get('eventActionType') == 31)
    t32 = sum(1 for x in walk(d) if x.get('eventActionType') == 32)
    if t24 != len(sp) or t31 != len(pos) or t32 != len(pos):
        sys.exit('count mismatch %s (t24=%d want %d; t31=%d t32=%d want %d) - file NOT written'
                 % (path, t24, len(sp), t31, t32, len(pos)))
    open(path, 'w', encoding='utf-8').write(out)
    return 'inserted %d actions (%d anchors)' % (2 * len(pos), len(pos))

if __name__ == '__main__':
    args = sys.argv[1:]
    dry = '--dry' in args
    filt = [a for a in args if not a.startswith('--')] or ['']
    files = sorted({f for g in filt for f in glob.glob('%s/Resources/SagaMissions/**/*%s*.json' % (ROOT, g), recursive=True)})
    if not files:
        sys.exit('no files matched')
    touched = 0
    for p in files:
        msg = edit(p, dry)
        touched += 'inserted' in msg or 'DRY' in msg
        print('%-72s %s' % (p.replace(ROOT + '/', ''), msg))
    print('--- %d/%d files %s' % (touched, len(files), 'dry-run' if dry else 'edited'))
