// M1 settings UI for the SettingsPanel (concept 4.2 "UI (SettingsPanel):
// global switch 'Use Tactical AI'; sub-menu deployment mode (hidden when
// off)"). Builds its rows at runtime under a supplied panel - the same
// dynamic-UI pattern the game already uses in EnemyActivationPopup
// (.ParseInstructions/FindOrCreateBlock instantiate TMP texts at runtime),
// so no prefab surgery is needed.
//
// Dev 15 restyle (Thomas playtest 27.10: "die Darstellung sieht anders aus
// als der Rest des Screens"): the old self-built TMP buttons with green/red
// blocks stuck out like a debug panel. The screen's own look (Rundenlimit
// group) is serialized in the prefab with the game font, so instead of
// inventing widgets we CLONE the real ones at runtime: the group title text,
// the bordered group panel (found via SettingsPanel.roundLimitToggleOn's
// ancestor chain) and one option-label text. That inherits font, colour and
// panel art with zero prefab surgery. If any template is missing the old
// TMP-button block remains as fallback.
//
// Dev 15 mode list (Thomas): 'legacy' is no longer offered in the global
// selector - master ON + LEGACY is indistinguishable from Tactical AI OFF.
// Per-mission 'legacy' (badge OFF) stays legal and is unaffected.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace TacticalAI
{
    public static class TacticalAISettingsUI
    {
        static readonly ColorBlock greenBlock = new ColorBlock
        {
            normalColor = new Color32(0, 162, 0, 255),
            selectedColor = new Color32(0, 162, 0, 192),
            highlightedColor = new Color32(0, 255, 0, 255),
            pressedColor = new Color32(0, 192, 0, 255),
            disabledColor = new Color32(0, 64, 0, 192),
            colorMultiplier = 1f,
            fadeDuration = 0.05f
        };
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

        /// <summary>A row label may be a cloned legacy Text or - in the TMP
        /// fallback path - a TextMeshProUGUI. One of both is set.</summary>
        class Row
        {
            public GameObject go;
            public Text legacy;
            public TMPro.TextMeshProUGUI tmp;
            public void Set(string s) { if (legacy != null) legacy.text = s; else if (tmp != null) tmp.text = s; }
            public bool Interactable(bool b)
            {
                var btn = go.GetComponent<Button>();
                if (btn != null) btn.interactable = b;
                return b;
            }
        }

        static Transform _root;
        static GameObject _subPanel;
        static LayoutElement _subPanelLe;
        static readonly Dictionary<string, Row> _rows = new Dictionary<string, Row>();

        // cloned style templates (null => TMP fallback)
        static GameObject _labelTemplate;
        static GameObject _titleTemplate;
        static GameObject _panelTemplate;

        /// <summary>Installs (or refreshes) the Tactical AI rows under the
        /// given panel. Safe to call on every SettingsPanel.Show(). Returns
        /// false when the panel cannot host UI (missing TMP font etc.).</summary>
        public static bool TryInstall(GameObject panel)
        {
            if (panel == null) return false;
            if (TMPro.TMP_Settings.defaultFontAsset == null)
            {
                Debug.Log("TacticalAISettingsUI::no TMP default font asset, skipping install");
                return false;
            }

            var existing = panel.transform.Find("tacaiRoot");
            if (existing != null) { Refresh(existing.gameObject); return true; }

            var go = new GameObject("tacaiRoot", typeof(RectTransform));
            // Playtest feedback (27.10, Thomas): the old anchor floated the block
            // over the settings dialog. Install INSIDE the tab's scroll content
            // instead, so the rows continue the existing option list directly.
            var sr = panel.GetComponentInChildren<ScrollRect>(true);
            var host = (sr != null && sr.content != null) ? sr.content : panel.transform;
            go.transform.SetParent(host, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, 0f);
            float width = 420f;
            if (host is RectTransform hostRT && hostRT.rect.width > 80f)
                width = hostRT.rect.width;
            rt.sizeDelta = new Vector2(width, 200f);

            var vlg = go.AddComponent<VerticalLayoutGroup>();
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.spacing = 8f;
            var le = go.AddComponent<LayoutElement>();
            if (host.GetComponent<LayoutGroup>() == null)
            {
                // scroll content without an auto-layout: pin our block just
                // under the last positioned row so it still reads as appended.
                float lowest = rt.anchoredPosition.y;
                foreach (Transform row in host)
                    if (row != go.transform && row.localPosition.y < lowest)
                        lowest = row.localPosition.y;
                rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, lowest - 110f);
            }

            _root = go.transform;
            _rows.Clear();
            _subPanel = null;

            FindStyleTemplates(panel);

            if (_labelTemplate != null && _panelTemplate != null)
            {
                // native-clone layout: section title + panel(Master) +
                // panel(Mode/Skill/Deployment/Explain) like the Rundenlimit group
                MakeTitle();
                var masterPanel = MakePanel(go.transform, 1);
                MakeRow(masterPanel, "master", () =>
                {
                    TacticalAISettings.Enabled = !TacticalAISettings.Enabled;
                    Refresh(_root.parent.gameObject);
                });
                _subPanel = MakePanel(go.transform, ModeChoices.Length > 0 ? 4 : 0);
                _subPanelLe = _subPanel.GetComponent<LayoutElement>();
                _subPanelLe.preferredHeight = 4 * 36f + 16f;
                MakeRow(_subPanel, "mode", () =>
                {
                    TacticalAISettings.Mode = Next(TacticalAISettings.Mode, ModeChoices);
                    Refresh(_root.parent.gameObject);
                });
                MakeRow(_subPanel, "skill", () =>
                {
                    TacticalAISettings.SkillLevel = Next(TacticalAISettings.SkillLevel, TacticalAISettings.SkillValues);
                    Refresh(_root.parent.gameObject);
                });
                MakeRow(_subPanel, "deployment", () =>
                {
                    TacticalAISettings.DeploymentMode = Next(TacticalAISettings.DeploymentMode, TacticalAISettings.DeploymentModeValues);
                    Refresh(_root.parent.gameObject);
                });
                MakeRow(_subPanel, "explain", () =>
                {
                    TacticalAISettings.ExplainLevel = Next(TacticalAISettings.ExplainLevel, TacticalAISettings.ExplainLevelValues);
                    Refresh(_root.parent.gameObject);
                });
            }
            else
            {
                // fallback: the old TMP-button block (works without templates)
                MakeButton(go.transform, "TACTICAL AI", "master", () =>
                {
                    TacticalAISettings.Enabled = !TacticalAISettings.Enabled;
                    Refresh(_root.parent.gameObject);
                });
                MakeButton(go.transform, "AI MODE", "mode", () =>
                {
                    TacticalAISettings.Mode = Next(TacticalAISettings.Mode, ModeChoices);
                    Refresh(_root.parent.gameObject);
                }, true);
                MakeButton(go.transform, "SKILL", "skill", () =>
                {
                    TacticalAISettings.SkillLevel = Next(TacticalAISettings.SkillLevel, TacticalAISettings.SkillValues);
                    Refresh(_root.parent.gameObject);
                }, true);
                MakeButton(go.transform, "DEPLOYMENT", "deployment", () =>
                {
                    TacticalAISettings.DeploymentMode = Next(TacticalAISettings.DeploymentMode, TacticalAISettings.DeploymentModeValues);
                    Refresh(_root.parent.gameObject);
                }, true);
                MakeButton(go.transform, "EXPLAIN", "explain", () =>
                {
                    TacticalAISettings.ExplainLevel = Next(TacticalAISettings.ExplainLevel, TacticalAISettings.ExplainLevelValues);
                    Refresh(_root.parent.gameObject);
                }, true);
            }

            Refresh(go);
            return true;
        }

        /// <summary>Grab the Rundenlimit group's real widgets as style
        /// templates: its bordered panel background (ancestor Image), one of
        /// its option labels (legacy Text) and the group title text.</summary>
        static void FindStyleTemplates(GameObject panel)
        {
            _labelTemplate = null; _titleTemplate = null; _panelTemplate = null;
            var sp = panel.GetComponentInParent<SettingsPanel>();
            if (sp == null || sp.roundLimitToggleOn == null) return;

            // panel background: first ancestor with an Image below the tab panel
            Transform bg = null;
            var t = sp.roundLimitToggleOn.transform.parent;
            while (t != null && t != panel.transform && t.parent != panel.transform)
            {
                if (t.GetComponent<Image>() != null) { bg = t; break; }
                t = t.parent;
            }
            if (bg == null) return;
            _panelTemplate = bg.gameObject;

            // label: any Text inside the toggle's row container, else any
            // Text inside the panel, else any Text in the tab
            var lbl = FirstTextInChildren(sp.roundLimitToggleOn.transform.parent != null
                ? sp.roundLimitToggleOn.transform.parent : sp.roundLimitToggleOn.transform);
            if (lbl == null) lbl = FirstTextInChildren(bg);
            if (lbl == null) lbl = FirstTextInChildren(panel.transform);
            if (lbl != null) _labelTemplate = lbl.gameObject;

            // title: a Text on the panel's wrapper but NOT inside the panel bg
            var wrapper = bg.parent as RectTransform;
            if (wrapper != null)
            {
                foreach (var txt in wrapper.GetComponentsInChildren<Text>(true))
                {
                    if (!txt.transform.IsChildOf(bg.transform)) { _titleTemplate = txt.gameObject; break; }
                }
            }
            if (_titleTemplate == null) _titleTemplate = _labelTemplate;
        }

        static Text FirstTextInChildren(Transform root)
        {
            if (root == null) return null;
            foreach (var txt in root.GetComponentsInChildren<Text>(true))
                return txt;
            return null;
        }

        static void MakeTitle()
        {
            var go = UnityEngine.Object.Instantiate(_titleTemplate, _root);
            go.name = "tacai_title";
            var le = EnsureLayout(go);
            le.preferredHeight = 44f; le.minHeight = 44f;
            var txt = go.GetComponentInChildren<Text>();
            if (txt != null)
            {
                txt.text = DataStore.uiLanguage != null ? DataStore.uiLanguage.uiTacticalAI.setHeading : "TACTICAL AI";
                var rt = go.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f);
                rt.sizeDelta = Vector2.zero;
            }
            // strip any language-controller-driven children we do not own
            foreach (var b in go.GetComponentsInChildren<Button>(true)) b.interactable = false;
        }

        static GameObject MakePanel(Transform parent, int rowCount)
        {
            var go = UnityEngine.Object.Instantiate(_panelTemplate, parent);
            go.name = "tacai_panel";
            // strip the template's serialized children (toggles, outlines...)
            for (int i = go.transform.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(go.transform.GetChild(i).gameObject);
            var vlg = go.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(10, 10, 6, 6);
            vlg.spacing = 2f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = false;
            vlg.childForceExpandWidth = true;
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = rowCount * 36f + 16f;
            return go;
        }

        static void MakeRow(GameObject panel, string kind, Action onClick)
        {
            var rowGo = new GameObject("tacai_row_" + kind, typeof(RectTransform));
            rowGo.transform.SetParent(panel.transform, false);
            var b = rowGo.AddComponent<Button>();
            var le = rowGo.AddComponent<LayoutElement>();
            le.preferredHeight = 36f; le.minHeight = 36f;

            var lbl = UnityEngine.Object.Instantiate(_labelTemplate, rowGo.transform);
            lbl.name = "label";
            var lrt = lbl.GetComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
            lrt.offsetMin = Vector2.zero; lrt.offsetMax = Vector2.zero;
            var txt = lbl.GetComponentInChildren<Text>(true);
            if (txt != null) txt.alignment = TextAnchor.MiddleCenter;

            b.onClick.AddListener(() =>
            {
                GameObject.FindObjectOfType<Sound>()?.PlaySound(FX.Click);
                onClick();
            });

            var row = new Row { go = rowGo, legacy = txt };
            _rows[kind] = row;
        }

        static LayoutElement EnsureLayout(GameObject go)
        {
            var le = go.GetComponent<LayoutElement>();
            if (le == null) le = go.AddComponent<LayoutElement>();
            return le;
        }

        static void Refresh(GameObject panelOrRoot)
        {
            if (_root == null) return;
            var tl = DataStore.uiLanguage != null ? DataStore.uiLanguage.uiTacticalAI : null;
            bool on = TacticalAISettings.Enabled;

            // clone-layout
            if (_rows.Count > 0)
            {
                foreach (var kv in _rows)
                {
                    kv.Value.Set(RowText(kv.Key, tl, on));
                    if (kv.Key != "master") kv.Value.Interactable(on);
                }
                if (_subPanel != null)
                {
                    _subPanel.SetActive(on);
                    if (_subPanelLe != null)
                        _subPanelLe.preferredHeight = on ? 4 * 36f + 16f : 0f;
                }
                var rootLe = _root.GetComponent<LayoutElement>();
                if (rootLe != null)
                    rootLe.preferredHeight = 44f + (36f + 16f) + 8f + (on ? 4 * 36f + 16f + 8f : 0f);
                return;
            }

            // TMP-fallback layout
            foreach (var child in _root.GetComponentsInChildren<UnityEngine.UI.Button>(true))
            {
                var label = child.GetComponentInChildren<TMPro.TextMeshProUGUI>();
                string kind = child.name.Replace("tacai_", "");
                bool master = kind == "master";
                Row r = new Row { go = child.gameObject, tmp = label };
                string s = RowText(kind, tl, on);
                if (!master && kind != "title")
                {
                    child.gameObject.SetActive(on);
                }
                if (kind == "master" || on)
                {
                    var colors = child.colors;
                    colors.normalColor = on ? greenBlock.normalColor : redBlock.normalColor;
                    colors.selectedColor = colors.normalColor;
                    colors.highlightedColor = Color.Lerp(colors.normalColor, Color.white, 0.5f);
                    colors.pressedColor = colors.normalColor;
                    child.colors = colors;
                    child.interactable = master || on;
                    if (label != null) label.text = s;
                }
            }
        }

        static string RowText(string kind, UITacticalAI tl, bool on)
        {
            string value, heading;
            switch (kind)
            {
                case "master": value = on ? (tl != null ? tl.setOn : "ON") : (tl != null ? tl.setOff : "OFF"); break;
                case "mode": value = TacticalAISettings.Mode.ToUpperInvariant(); break;
                case "skill": value = TacticalAISettings.SkillLevel; break;
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
            // PR convention (Thomas' review 27.10): labels from the language
            // files. Enum VALUES stay uppercase developer tokens (REFERENCE/
            // OVERLORD, S1-S5, ...) - they match the docs and console log.
            return (tl != null ? tl.setRowFormat : "{0}:  {1}")
                   .Replace("{0}", heading).Replace("{1}", value);
        }

        static void MakeButton(Transform parent, string title, string kind, Action onClick, bool sub = false)
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
            if (sub) { } // sub-rows are gated centrally in Refresh()
        }

        static string Next(string current, string[] values)
        {
            for (int i = 0; i < values.Length; i++)
                if (values[i] == current) return values[(i + 1) % values.Length];
            return values[0];
        }
    }
}
