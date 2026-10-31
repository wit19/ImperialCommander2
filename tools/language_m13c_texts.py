#!/usr/bin/env python3
"""M13c language edit for the tactical activation texts (Thomas 30.10.):

- moveFocus / moveFocusNoTarget: '... im Auge behalten / keep it in view'
  -> '... bis die Figur in Angriffsreichweite ist / until the figure is
  within attack range'
- doctrineTreeLead: DELETED (the 'Diese Karte zeigt...' paragraph dies, the
  'Grundregel:' paragraph MOVES into the info block as new key ordInfoRule)
- shuffleNote, ordRoundFocus, focusNoChance: DELETED (keys + code refs)
- setInfo: NEW settings-row label (De 'Info-Text'; the section is EN in all
  other shipped languages - established convention, verified per file)

Fact base: only De translates uiTacticalAI; all other languages carry the EN
texts (dumped per language before editing, not assumed).

Run from repo root: python3 tools/language_m13c_texts.py
Validation: strict JSON re-parse, key-count math, per-file needle audit
(none of the removed phrases may remain in uiTacticalAI).
"""
import json, sys

LANGS = ['Br', 'De', 'En', 'Es', 'Fr', 'Hu', 'It', 'Nl', 'No', 'Pl', 'Ru']
ROOT = 'ImperialCommander2/Assets/Resources/Languages'

REPL = {  # (old suffix, new suffix) exact substring pairs per wording variant
    'moveFocus': [
        ('\u2013 es dabei im Auge behalten.', '\u2013 bis die Figur in Angriffsreichweite ist.'),
        ('- keep it in view.', '- until the figure is within attack range.'),
    ],
    'moveFocusNoTarget': [
        ('\u2013 ihn dabei im Auge behalten.', '\u2013 bis die Figur in Angriffsreichweite ist.'),
        ('- keep him in view.', '- until the figure is within attack range.'),
    ],
}
DELETE = ['doctrineTreeLead', 'shuffleNote', 'ordRoundFocus', 'focusNoChance']

def language_label(l):
    return 'Info-Text' if l == 'De' else 'Info text'

changed = []
for l in LANGS:
    p = '%s/%s/ui.json' % (ROOT, l)
    d = json.load(open(p, encoding='utf-8'))
    t = d['uiTacticalAI']
    before = len(t)

    # 1) extract the Grundregel paragraph BEFORE deleting its carrier
    lead = t.get('doctrineTreeLead', '')
    paras = [x for x in lead.split('\n') if x.strip()]
    rule = next((x for x in paras if x.lower().startswith(('grundregel', 'basic rule'))), None)
    if rule is None and lead:
        sys.exit('%s: doctrineTreeLead without a rule paragraph - abort' % p)
    t['ordInfoRule'] = rule
    t['setInfo'] = language_label(l)

    # 2) suffix replacements (must hit exactly once per key+pair present)
    for key, pairs in REPL.items():
        v = t.get(key, '')
        for old, new in pairs:
            if old in v:
                if v.count(old) != 1:
                    sys.exit('%s %s: suffix %r count %d - abort' % (p, key, old, v.count(old)))
                v = v.replace(old, new)
        t[key] = v

    # 3) deletions
    for k in DELETE:
        t.pop(k, None)

    after = len(t)
    # strict expectations: +2 new (ordInfoRule,setInfo), -4 deleted -> -2
    if after != before - 2:
        sys.exit('%s: key count %d -> %d, expected -2 - abort' % (p, before, after))

    # 4) post-edit audit, field-precise (no total-blob string games):
    #    a) NO key except ordInfoRule may contain any removed phrase;
    #    b) ordInfoRule may only carry the moved basic-rule sentence.
    banned = ['im Auge behalten', 'keep it in view', 'keep him in view',
              'Diese Karte zeigt', 'This card shows',
              'Grundregel', 'Basic rule',
              'Gruppen-Deck', 'shuffle this', 'kein Zufall', 'no chance',
              'alle Gruppen', 'all groups converge']
    for k, v in t.items():
        if not isinstance(v, str) or k == 'ordInfoRule':
            continue
        for needle in banned:
            if needle in v:
                sys.exit('%s %s: stale phrase %r - abort' % (p, k, needle))
    rule_v = t['ordInfoRule']
    for needle in ['Diese Karte zeigt', 'This card shows', 'Gruppen-Deck',
                   'shuffle this', 'kein Zufall', 'no chance']:
        if needle in rule_v:
            sys.exit('%s ordInfoRule carries removed content %r - abort' % (p, needle))
    if not (rule_v.startswith('Grundregel:') or rule_v.startswith('Basic rule:')):
        sys.exit('%s: ordInfoRule is not the moved basic-rule paragraph: %r' % (p, rule_v[:60]))
    if 'nur ein Mal an' not in rule_v and 'attacks only once' not in rule_v:
        sys.exit('%s: ordInfoRule lacks the basic attack rule body' % p)

    with open(p, 'w', encoding='utf-8') as f:
        json.dump(d, f, ensure_ascii=False, indent=2)
        f.write('\n')
    changed.append(p)

print('\n'.join(changed))
print('--- %d/%d languages updated' % (len(changed), len(LANGS)))
