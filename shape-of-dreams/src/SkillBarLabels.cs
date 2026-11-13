using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;

namespace DreamDpsMeter
{
    /// <summary>
    /// Small damage-share percentages on the in-game skill bar:
    /// above each memory button, and under each essence slot. Local player only.
    /// </summary>
    public class SkillBarLabels
    {
        private class Label
        {
            public TextMeshProUGUI text;
            public UI_InGame_SkillButton button; // memory label
            public UI_InGame_GemSlot slot; // essence label
        }

        private const string LabelName = "DreamDpsMeter Pct";
        private readonly List<Label> _labels = new List<Label>();
        private UI_InGame_SkillButtons _bar;

        public void Ensure(TextStyle style)
        {
            UI_InGame_SkillButtons bar = ManagerBase<UI_InGame_SkillButtons>.softInstance;
            if (bar == null)
            {
                Clear();
                return;
            }
            if (bar == _bar && _labels.Count > 0 && _labels.TrueForAll(l => l.text != null)) return;

            Clear();
            _bar = bar;
            if (bar.skillButtons != null)
            {
                foreach (UI_InGame_SkillButton b in bar.skillButtons)
                {
                    if (b == null) continue;
                    RectTransform rt = (RectTransform)b.transform;
                    float size = Mathf.Clamp(rt.rect.height * 0.3f, 16f, 30f);
                    TextMeshProUGUI t = CreateLabel(rt, style, size, above: true);
                    _labels.Add(new Label { text = t, button = b });
                }
            }
            foreach (UI_InGame_GemSlot g in bar.GetComponentsInChildren<UI_InGame_GemSlot>(true))
            {
                if (g == null || g.button == null) continue;
                RectTransform rt = (RectTransform)g.transform;
                float size = Mathf.Clamp(rt.rect.height * 0.42f, 13f, 22f);
                TextMeshProUGUI t = CreateLabel(rt, style, size, above: false);
                _labels.Add(new Label { text = t, slot = g });
            }
        }

        public void Clear()
        {
            foreach (Label l in _labels)
            {
                if (l.text != null) Object.Destroy(l.text.gameObject);
            }
            _labels.Clear();
            _bar = null;
        }

        public void Update(PlayerStats local, Hero hero)
        {
            foreach (Label l in _labels)
            {
                if (l.text == null) continue;
                string s = "";
                Color color = Color.white;
                if (local != null && hero != null && local.runDamage > 0)
                {
                    Actor actor = null;
                    try
                    {
                        if (l.button != null)
                        {
                            actor = hero.Skill.GetSkill(l.button.skillType);
                        }
                        else if (l.slot != null && l.slot.button != null)
                        {
                            hero.Skill.gems.TryGetValue(new GemLocation(l.slot.button.skillType, l.slot.slotIndex), out Gem gem);
                            actor = gem;
                        }
                    }
                    catch
                    {
                        actor = null;
                    }
                    SourceStats src = Find(local, actor);
                    if (src != null && src.runDamage > 0)
                    {
                        double pct = src.runDamage / local.runDamage * 100.0;
                        s = (pct < 1 ? "<1" : Mathf.RoundToInt((float)pct).ToString(CultureInfo.InvariantCulture)) + "%";
                        color = DamageKinds.Colors[DamageKinds.Dominant(src.runByKind)];
                    }
                }
                if (l.text.text != s) l.text.SetText(s);
                l.text.color = color;
            }
        }

        private static SourceStats Find(PlayerStats p, Actor actor)
        {
            if (actor == null) return null;
            foreach (SourceStats s in p.sources.Values)
            {
                if (s.actor == actor) return s;
            }
            return null;
        }

        private static TextMeshProUGUI CreateLabel(RectTransform parent, TextStyle style, float size, bool above)
        {
            GameObject go = new GameObject(LabelName, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, above ? 1f : 0f);
            rt.pivot = new Vector2(0.5f, above ? 0f : 1f);
            rt.anchoredPosition = new Vector2(0f, above ? 2f : -1f);
            rt.sizeDelta = new Vector2(120f, size * 1.3f);
            TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();
            t.font = style != null && style.font != null ? style.font : TMP_Settings.defaultFontAsset;
            if (style != null && style.material != null) t.fontSharedMaterial = style.material;
            t.fontSize = size;
            t.fontStyle = FontStyles.Bold;
            t.alignment = TextAlignmentOptions.Center;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Overflow;
            t.raycastTarget = false;
            t.richText = true;
            // Dark outline so the number stays readable over the world (needs a material instance).
            try
            {
                if (t.fontSharedMaterial != null)
                {
                    t.fontMaterial = new Material(t.fontSharedMaterial);
                    t.outlineWidth = 0.22f;
                    t.outlineColor = new Color32(0, 0, 0, 220);
                }
            }
            catch
            {
                // outline is cosmetic
            }
            return t;
        }
    }
}
