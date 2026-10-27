// M1 settings UI for the SettingsPanel (concept 4.2 "UI (SettingsPanel):
// global switch 'Use Tactical AI'; sub-menu deployment mode (hidden when
// off)"). Builds its rows at runtime under a supplied panel - the same
// dynamic-UI pattern the game already uses in EnemyActivationPopup
// (.ParseInstructions/FindOrCreateBlock instantiate TMP texts at runtime),
// so no prefab surgery is needed. Editor polish (fonts/positions) may
// follow; the logic and Default-off gate are complete here.
//
// The MASTER toggle row is always visible (that is how the user turns the
// feature on); every submenu row is hidden while Enabled == false. Turning
// it on changes NO behaviour until a per-mission aiMode selects a tactical
// level - the planner hooks all re-check TacticalAISettings.Enabled.
using System;
using System.Collections.Generic;
using TMPro;
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

        static Transform _root;
        static readonly List<GameObject> _subRows = new List<GameObject>();

        /// <summary>Installs (or refreshes) the Tactical AI rows under the
        /// given panel. Safe to call on every SettingsPanel.Show(). Returns
        /// false when the panel cannot host UI (missing TMP font etc.).</summary>
        public static bool TryInstall(GameObject panel)
        {
            if (panel == null) return false;
            if (TMP_Settings.defaultFontAsset == null)
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
            var sr = panel.GetComponentInChildren<UnityEngine.UI.ScrollRect>(true);
            var host = (sr != null && sr.content != null) ? sr.content : panel.transform;
            go.transform.SetParent(host, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, 0f);
            rt.sizeDelta = new Vector2(420f, 200f);

            var vlg = go.AddComponent<VerticalLayoutGroup>();
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.spacing = 6f;
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = 34f;      // master toggle alone (submenu hidden)
            le.preferredHeight = 160f; // all rows visible
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
            _subRows.Clear();

            // master toggle - visible even while OFF
            MakeButton(go.transform, "TACTICAL AI", "master", () =>
            {
                TacticalAISettings.Enabled = !TacticalAISettings.Enabled;
                Refresh(_root.parent.gameObject);
            });

            // submenu (hidden while off)
            MakeButton(go.transform, "AI MODE", "mode", () =>
            {
                TacticalAISettings.Mode = Next(TacticalAISettings.Mode, TacticalAISettings.ModeValues);
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

            Refresh(go);
            return true;
        }

        static void Refresh(GameObject panelOrRoot)
        {
            if (_root == null) return;
            foreach (var child in _root.GetComponentsInChildren<Button>(true))
            {
                var label = child.GetComponentInChildren<TextMeshProUGUI>();
                string kind = child.name.Replace("tacai_", "");
                bool master = kind == "master";
                bool on = TacticalAISettings.Enabled;
                string value;
                var tl = DataStore.uiLanguage != null ? DataStore.uiLanguage.uiTacticalAI : null;
                switch (kind)
                {
                    case "master": value = on ? (tl != null ? tl.setOn : "ON") : (tl != null ? tl.setOff : "OFF"); break;
                    case "mode": value = TacticalAISettings.Mode.ToUpperInvariant(); break;
                    case "skill": value = TacticalAISettings.SkillLevel; break;
                    case "deployment": value = TacticalAISettings.DeploymentMode.ToUpperInvariant(); break;
                    case "explain": value = TacticalAISettings.ExplainLevel.ToUpperInvariant(); break;
                    default: continue;
                }
                // PR convention (Thomas' review 27.10): labels from the language
                // files. Enum VALUES stay uppercase developer tokens (LEGACY/
                // REFERENCE/OVERLORD, S1-S4, ...) - they match the docs and the
                // console log; translating them is a translator follow-up.
                string heading;
                if (tl == null) heading = master ? "Tactical AI" : kind.ToUpperInvariant();
                else if (master) heading = tl.setHeading;
                else if (kind == "mode") heading = tl.setMode;
                else if (kind == "skill") heading = tl.setSkill;
                else if (kind == "deployment") heading = tl.setDeployment;
                else heading = tl.setExplain;
                if (label != null) label.text = (tl != null ? tl.setRowFormat : "{0}:  {1}")
                                                    .Replace("{0}", heading).Replace("{1}", value);
                var colors = child.colors;
                colors.normalColor = on ? greenBlock.normalColor : redBlock.normalColor;
                colors.selectedColor = colors.normalColor;
                colors.highlightedColor = Color.Lerp(colors.normalColor, Color.white, 0.5f);
                colors.pressedColor = colors.normalColor;
                child.colors = colors;
                child.interactable = master || on;
            }
            foreach (var row in _subRows)
                if (row != null) row.SetActive(TacticalAISettings.Enabled);
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
            var t = textGo.AddComponent<TextMeshProUGUI>();
            t.text = title;
            t.fontSize = 16f;
            t.alignment = TextAlignmentOptions.Center;
            var trt = textGo.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;

            if (sub) _subRows.Add(bg);
        }

        static string Next(string current, string[] values)
        {
            for (int i = 0; i < values.Length; i++)
                if (values[i] == current) return values[(i + 1) % values.Length];
            return values[0];
        }
    }
}
