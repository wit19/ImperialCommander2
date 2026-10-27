// M1 settings UI for the settings dialog (concept 4.2 "Settings"): global
// switch + sub-menu (deployment mode hidden while off). Built at runtime -
// the same dynamic-UI pattern the game already uses elsewhere, so no prefab
// surgery is needed.
//
// Dev 18 restyle v2 (Thomas playtest 27.10, screenshot: clone v1 overlapped
// into a mess). Root causes fixed:
//  * v1 cloned an ANCESTOR panel found by walking up from a toggle - that
//    ancestor was the whole dialog background (huge frame art). v2 clones
//    ONLY leaf widgets addressed BY NAME inside the settings prefab:
//    "round limit Label" (the yellow group title) and the toggles' "Label"
//    (yellow option text). Both are leaf Text GOs - nothing can overlap.
//  * every clone is sanitized: children destroyed, non-text graphics
//    stripped, explicit LayoutElement - serialized leftovers (Checkmark,
//    Outline, stale language text) cannot survive.
//  * no per-group panel art: one title + flat text rows - exactly how the
//    Rundenlimit group reads: same font, same color, same rhythm.
//
// Dev 15: 'legacy' is NOT offered globally (master ON + legacy == off);
// per-mission badge OFF stays legal. Dev 18: skill shows themed names
// (Rekrut .. 5-Sterne-General) from the language files; stored/log values
// stay the developer tokens s1..s5 (PR convention: display via language
// files, tokens in docs/console).
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace TacticalAI
{
    public static class TacticalAISettingsUI
    {
        static readonly ColorBlock redBlock = new ColorBlock
        {
            normalColor = new Color32(162, 0, 0, 255),
            selectedColor = new Color32(162, 0, 0, 192),
            highlightedColor = new Color32(255, 0, 0, 255),
            pressedColor = new Color32(192, 0, 0, 255),
            disabledColor = new Color32(64, 0, 0, 192),
            colorMultiplier = 1f,
            fadeDuration = 0.05f
        };

        // 'legacy' deliberately NOT offered (see header). Next() falls back to
        // [0] for unknown values, so a persisted 'legacy' cycles to reference.
        static readonly string[] ModeChoices = { "reference", "overlord" };

        class Row
        {
            public GameObject go;
            public Text legacy;
            public TMPro.TextMeshProUGUI tmp;
            public void Set(string s) { if (legacy != null) legacy.text = s; else if (tmp != null) tmp.text = s; }
        }

        static Transform _root;
        static GameObject _subPanel;
        static readonly Dictionary<string, Row> _rows = new Dictionary<string, Row>();

        static GameObject _labelTemplate;   // leaf yellow option text ("Label")
        static GameObject _titleTemplate;   // leaf yellow group title ("round limit Label")

        public static bool TryInstall(GameObject panel)
        {
            if (panel == null) return false;
            if (TMPro.TMP_Settings.defaultFontAsset == null)
            {
                Debug.Log("TacticalAISettingsUI::no TMP default font asset, skipping install");
                return false;
            }

            var existing = FindDeep(panel.transform, "tacaiRoot");
            if (existing != null) { Refresh(); return true; }

            // host: the tab's scroll content if it has one, else the panel
            // itself (the new settings screen hosts its rows directly).
            var sr = panel.GetComponentInChildren<ScrollRect>(true);
            var host = (sr != null && sr.content != null) ? sr.content : panel.transform;

            var go = new GameObject("tacaiRoot", typeof(RectTransform));
            go.transform.SetParent(host, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = Vector2.zero;
            float width = 420f;
            if (host is RectTransform hostRT && hostRT.rect.width > 80f)
                width = hostRT.rect.width;
            rt.sizeDelta = new Vector2(width, 200f);

            var vlg = go.AddComponent<VerticalLayoutGroup>();
            vlg.childControlWidth = true;
            vlg.childControlHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.spacing = 4f;
            var le = go.AddComponent<LayoutElement>();

            // Host without an auto-layout (old scroll variant): pin the root
            // under the lowest existing row so it reads as appended.
            if (host.GetComponent<LayoutGroup>() == null)
            {
                float lowest = rt.anchoredPosition.y;
                foreach (Transform row in host)
                    if (row != go.transform && row.localPosition.y < lowest)
                        lowest = row.localPosition.y;
                rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, lowest - 140f);
            }

            _root = go.transform;
            _rows.Clear();
            _subPanel = null;
            _labelTemplate = null;
            _titleTemplate = null;
            FindStyleTemplates(panel);

            if (_labelTemplate != null && _titleTemplate != null)
            {
                var tl = DataStore.uiLanguage != null ? DataStore.uiLanguage.uiTacticalAI : null;
                var title = CloneText(_titleTemplate, _root,
                    tl != null ? tl.setHeading : "TACTICAL AI", 42f);
                title.name = "tacai_title";
                foreach (var b in title.GetComponentsInChildren<Button>(true)) b.interactable = false;

                MakeRow(go.transform, "master", () =>
                {
                    TacticalAISettings.Enabled = !TacticalAISettings.Enabled;
                    Refresh();
                });

                _subPanel = new GameObject("tacai_sub", typeof(RectTransform));
                _subPanel.transform.SetParent(go.transform, false);
                var svlg = _subPanel.AddComponent<VerticalLayoutGroup>();
                svlg.childControlWidth = true;
                svlg.childControlHeight = false;
                svlg.childForceExpandWidth = true;
                svlg.spacing = 2f;
                var sle = _subPanel.AddComponent<LayoutElement>();
                sle.preferredHeight = 4 * 32f;

                MakeRow(_subPanel.transform, "mode", () =>
                {
                    TacticalAISettings.Mode = Next(TacticalAISettings.Mode, ModeChoices);
                    Refresh();
                });
                MakeRow(_subPanel.transform, "skill", () =>
                {
                    TacticalAISettings.SkillLevel = Next(TacticalAISettings.SkillLevel, TacticalAISettings.SkillValues);
                    Refresh();
                });
                MakeRow(_subPanel.transform, "deployment", () =>
                {
                    TacticalAISettings.DeploymentMode = Next(TacticalAISettings.DeploymentMode, TacticalAISettings.DeploymentModeValues);
                    Refresh();
                });
                MakeRow(_subPanel.transform, "explain", () =>
                {
                    TacticalAISettings.ExplainLevel = Next(TacticalAISettings.ExplainLevel, TacticalAISettings.ExplainLevelValues);
                    Refresh();
                });
            }
            else
            {
                // fallback: old TMP-button block when the name templates are
                // missing (unexpected dialog prefab) - ugly but functional
                MakeButton(go.transform, "TACTICAL AI", "master", () =>
                {
                    TacticalAISettings.Enabled = !TacticalAISettings.Enabled;
                    Refresh();
                });
                MakeButton(go.transform, "AI MODE", "mode", () =>
                {
                    TacticalAISettings.Mode = Next(TacticalAISettings.Mode, ModeChoices);
                    Refresh();
                });
                MakeButton(go.transform, "SKILL", "skill", () =>
                {
                    TacticalAISettings.SkillLevel = Next(TacticalAISettings.SkillLevel, TacticalAISettings.SkillValues);
                    Refresh();
                });
                MakeButton(go.transform, "DEPLOYMENT", "deployment", () =>
                {
                    TacticalAISettings.DeploymentMode = Next(TacticalAISettings.DeploymentMode, TacticalAISettings.DeploymentModeValues);
                    Refresh();
                });
                MakeButton(go.transform, "EXPLAIN", "explain", () =>
                {
                    TacticalAISettings.ExplainLevel = Next(TacticalAISettings.ExplainLevel, TacticalAISettings.ExplainLevelValues);
                    Refresh();
                });
            }

            Refresh();
            return true;
        }

        static Transform FindDeep(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }

        /// <summary>Templates are leaf Text widgets addressed by prefab name -
        /// no ancestor walking, no container cloning (dev 18 lesson).</summary>
        static void FindStyleTemplates(GameObject panel)
        {
            var title = FindDeep(panel.transform, "round limit Label");
            _titleTemplate = title != null ? title.gameObject : null;

            Transform lbl = null;
            var row = FindDeep(panel.transform, "round limit on");
            if (row != null)
            {
                var l = FindDeep(row, "Label");
                if (l != null) lbl = l;
            }
            if (lbl == null) lbl = FindDeep(panel.transform, "Label");
            _labelTemplate = lbl != null ? lbl.gameObject : null;
        }

        /// <summary>Instantiate a leaf text template, destroy its children,
        /// strip every non-text graphic/interactable, stretch the Text -
        /// serialized leftovers can never render (dev 18 lesson).</summary>
        static GameObject CloneText(GameObject template, Transform parent, string text, float height)
        {
            var go = UnityEngine.Object.Instantiate(template, parent);
            for (int i = go.transform.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(go.transform.GetChild(i).gameObject);

            var t = go.GetComponentInChildren<Text>(true);
            foreach (var c in go.GetComponents<Component>())
            {
                if (c == null || c is RectTransform) continue;
                if (c == t) continue;
                if (c is CanvasRenderer) continue;
                if (c is Graphic || c is Selectable || c is ILayoutElement
                    || c is ILayoutSelfController || c is Outline)
                    UnityEngine.Object.Destroy(c);
            }

            if (t != null)
            {
                t.text = text;
                t.alignment = TextAnchor.MiddleCenter;
                t.horizontalOverflow = HorizontalWrapMode.Overflow;
                t.verticalOverflow = VerticalWrapMode.Overflow;
                t.raycastTarget = false;
                var trf = t.rectTransform;
                trf.anchorMin = Vector2.zero;
                trf.anchorMax = Vector2.one;
                trf.offsetMin = Vector2.zero;
                trf.offsetMax = Vector2.zero;
            }
            var le = go.GetComponent<LayoutElement>();
            if (le == null) le = go.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            le.minHeight = height;
            return go;
        }

        static void MakeRow(Transform parent, string kind, Action onClick)
        {
            var rowGo = new GameObject("tacai_row_" + kind, typeof(RectTransform));
            rowGo.transform.SetParent(parent, false);
            var le = rowGo.AddComponent<LayoutElement>();
            le.preferredHeight = 32f; le.minHeight = 32f;
            var b = rowGo.AddComponent<Button>();
            b.transition = Selectable.Transition.None;

            var tl = DataStore.uiLanguage != null ? DataStore.uiLanguage.uiTacticalAI : null;
            var lbl = CloneText(_labelTemplate, rowGo.transform,
                RowText(kind, tl, TacticalAISettings.Enabled), 32f);
            lbl.name = "label";

            b.onClick.AddListener(() =>
            {
                GameObject.FindObjectOfType<Sound>()?.PlaySound(FX.Click);
                onClick();
            });
            _rows[kind] = new Row { go = rowGo, legacy = lbl.GetComponentInChildren<Text>(true) };
        }

        static void Refresh()
        {
            if (_root == null) return;
            var tl = DataStore.uiLanguage != null ? DataStore.uiLanguage.uiTacticalAI : null;
            bool on = TacticalAISettings.Enabled;

            if (_subPanel != null) _subPanel.SetActive(on);

            foreach (var kv in _rows)
            {
                kv.Value.Set(RowText(kv.Key, tl, on));
                var btn = kv.Value.go.GetComponent<Button>();
                if (btn != null && kv.Key != "master") btn.interactable = on;
            }

            var rootLe = _root.GetComponent<LayoutElement>();
            if (rootLe != null)
                rootLe.preferredHeight = 42f + 32f + 8f + (on ? 4 * 32f + 3 * 2f + 8f : 0f);
        }

        static string RowText(string kind, UITacticalAI tl, bool on)
        {
            string value, heading;
            switch (kind)
            {
                case "master": value = on ? (tl != null ? tl.setOn : "ON") : (tl != null ? tl.setOff : "OFF"); break;
                case "mode": value = TacticalAISettings.Mode.ToUpperInvariant(); break;
                case "skill": value = SkillDisplay(TacticalAISettings.SkillLevel, tl); break;
                case "deployment": value = TacticalAISettings.DeploymentMode.ToUpperInvariant(); break;
                case "explain": value = TacticalAISettings.ExplainLevel.ToUpperInvariant(); break;
                default: return kind;
            }
            bool master = kind == "master";
            if (tl == null) heading = master ? "Tactical AI" : kind.ToUpperInvariant();
            else if (master) heading = tl.setHeading;
            else if (kind == "mode") heading = tl.setMode;
            else if (kind == "skill") heading = tl.setSkill;
            else if (kind == "deployment") heading = tl.setDeployment;
            else heading = tl.setExplain;
            return (tl != null ? tl.setRowFormat : "{0}:  {1}")
                   .Replace("{0}", heading).Replace("{1}", value);
        }

        /// <summary>PR convention: DISPLAY via language files (themed ladder
        /// Rekrut..5-Sterne-General, Thomas' suggestion 27.10); the stored and
        /// logged token stays s1..s5 (docs/console unchanged).</summary>
        static string SkillDisplay(string token, UITacticalAI tl)
        {
            if (tl == null) return token.ToUpperInvariant();
            switch (token)
            {
                case "s1": return string.IsNullOrEmpty(tl.skillS1) ? "S1" : tl.skillS1;
                case "s2": return string.IsNullOrEmpty(tl.skillS2) ? "S2" : tl.skillS2;
                case "s3": return string.IsNullOrEmpty(tl.skillS3) ? "S3" : tl.skillS3;
                case "s4": return string.IsNullOrEmpty(tl.skillS4) ? "S4" : tl.skillS4;
                case "s5": return string.IsNullOrEmpty(tl.skillS5) ? "S5" : tl.skillS5;
                default: return token.ToUpperInvariant();
            }
        }

        static void MakeButton(Transform parent, string title, string kind, Action onClick)
        {
            var bg = new GameObject("Button_tacai_" + kind, typeof(RectTransform));
            bg.transform.SetParent(parent, false);
            var b = bg.AddComponent<Button>();
            b.name = "tacai_" + kind;
            var img = bg.AddComponent<Image>();
            img.color = redBlock.normalColor;
            b.targetGraphic = img;
            b.colors = redBlock;
            b.onClick.AddListener(() =>
            {
                GameObject.FindObjectOfType<Sound>()?.PlaySound(FX.Click);
                onClick();
            });
            var le = bg.AddComponent<LayoutElement>();
            le.minHeight = 34f; le.preferredHeight = 34f;

            var textGo = new GameObject("Text (TMP)", typeof(RectTransform));
            textGo.transform.SetParent(bg.transform, false);
            var t = textGo.AddComponent<TMPro.TextMeshProUGUI>();
            t.text = title;
            t.fontSize = 16f;
            t.alignment = TMPro.TextAlignmentOptions.Center;
            var trt = textGo.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;
            _rows[kind] = new Row { go = bg, tmp = t };
        }

        static string Next(string current, string[] values)
        {
            for (int i = 0; i < values.Length; i++)
                if (values[i] == current) return values[(i + 1) % values.Length];
            return values[0];
        }
    }
}
