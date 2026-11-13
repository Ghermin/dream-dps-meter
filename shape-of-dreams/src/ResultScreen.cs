using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;

namespace DreamDpsMeter
{
    /// <summary>
    /// Integrates the damage recap into the game's own end-of-run screen:
    /// damage under each memory / essence of the hero panel, and extra lines in the stats list.
    /// </summary>
    public class ResultScreen
    {
        private const string Tag = "DreamDpsMeter Result";

        private class IconLabel
        {
            public TextMeshProUGUI text;
            public RectTransform pill;
            public UnityEngine.UI.Image bg;
            public UnityEngine.UI.Image underline;
            public UI_InGame_Result_HeroSkillItem skill;
            public UI_InGame_Result_HeroGemItem gem;
        }

        private class StatLine
        {
            public GameObject root;
            public TextMeshProUGUI header;
            public TextMeshProUGUI content;
            public TextMeshProUGUI score;
        }

        /// <summary>Sword icon for the basic attack (set by the mod from the stats window sprites).</summary>
        public static Sprite swordIcon;

        private class Cell
        {
            public GameObject root;
            public UnityEngine.UI.Image icon;
            public TextMeshProUGUI value;
            public TextMeshProUGUI pct;
            public UnityEngine.UI.Image bar;
            public UnityEngine.UI.Image veil;
        }

        private RectTransform _strip;
        private readonly List<Cell> _cells = new List<Cell>();
        private TMP_Text _style;
        private float _stripWidth = 800f;
        private float _cellWidth = 64f;
        private RectTransform _grid;
        private TextMeshProUGUI _title;
        private const float GridPadding = 18f;

        private readonly List<IconLabel> _labels = new List<IconLabel>();
        private readonly List<StatLine> _lines = new List<StatLine>();
        private bool _built;

        public void Clear()
        {
            foreach (IconLabel l in _labels)
            {
                if (l.pill != null) UnityEngine.Object.Destroy(l.pill.gameObject);
            }
            foreach (StatLine s in _lines)
            {
                if (s.root != null) UnityEngine.Object.Destroy(s.root);
            }
            _labels.Clear();
            _lines.Clear();
            if (_strip != null) UnityEngine.Object.Destroy(_strip.gameObject);
            _strip = null;
            _cells.Clear();
            _built = false;
        }

        private bool Alive()
        {
            if (!_built) return false;
            foreach (IconLabel l in _labels)
            {
                if (l.pill == null) return false;
            }
            foreach (StatLine s in _lines)
            {
                if (s.root == null) return false;
            }
            return _strip != null;
        }

        public void Update(DamageTracker tracker, float now)
        {
            if (!Alive())
            {
                Clear();
                Build();
            }
            if (!_built) return;

            DewGameResult data = null;
            int playerIndex = 0;
            foreach (IconLabel l in _labels)
            {
                if (l.skill != null && l.skill.currentData != null)
                {
                    data = l.skill.currentData;
                    playerIndex = l.skill.currentPlayerIndex;
                    break;
                }
            }
            if (data == null || data.players == null || playerIndex < 0 || playerIndex >= data.players.Count) return;
            DewGameResult.PlayerData pd = data.players[playerIndex];
            PlayerStats ps = FindPlayer(tracker, pd);

            // ---- damage under each memory / essence
            foreach (IconLabel l in _labels)
            {
                string text = "";
                uint netId = 0;
                bool found = false;
                if (l.skill != null && pd.TryGetSkillData(l.skill.type, out DewGameResult.SkillData sd))
                {
                    netId = sd.netId;
                    found = true;
                }
                else if (l.gem != null && l.gem.parent != null)
                {
                    HeroSkillLocation loc = l.gem.parent.type;
                    int idx = pd.gems.FindIndex(g => g.location.skill == loc && g.location.index == l.gem.index);
                    if (idx >= 0)
                    {
                        netId = pd.gems[idx].netId;
                        found = true;
                    }
                }
                SourceStats src = found && ps != null ? FindSource(ps, netId) : null;
                Color element = Color.white;
                if (src != null && src.runDamage > 0 && ps.runDamage > 0)
                {
                    string pct = Pct(src.runDamage, ps.runDamage);
                    text = l.skill != null
                        ? "<b>" + DpsMeterMod.Fmt(src.runDamage) + "</b> <size=82%><color=#FFFFFFA0>" + pct + "</color></size>"
                        : "<b>" + pct + "</b>";
                    element = DamageKinds.Colors[DamageKinds.Dominant(src.runByKind)];
                }
                SetPill(l, text, element);
            }

            // ---- every source of the run (equipped or not), sorted by damage
            if (_strip != null && ps != null)
            {
                List<SourceStats> all = new List<SourceStats>();
                foreach (SourceStats src in ps.sources.Values)
                {
                    if (src.runDamage > 0) all.Add(src);
                }
                all.Sort((a, b) => b.runDamage.CompareTo(a.runDamage));
                HashSet<uint> equipped = new HashSet<uint>();
                foreach (DewGameResult.SkillData sk in pd.skills) equipped.Add(sk.netId);
                foreach (DewGameResult.GemData gm in pd.gems) equipped.Add(gm.netId);
                LayoutStrip(all, ps.runDamage, equipped);
            }

            // ---- extra lines in the stats list
            if (_lines.Count >= 3)
            {
                double combat = tracker.RunCombatDuration(now);
                double bossDamage = 0, bossTime = 0;
                foreach (BossFight b in tracker.bosses)
                {
                    double d = ps != null ? b.Of(ps.player) : 0;
                    if (d <= 0) continue;
                    bossDamage += d;
                    bossTime += b.Duration(now);
                }
                SetLine(_lines[0], "DPS moyen en combat", ps != null ? DpsMeterMod.Fmt(ps.runDamage / combat) : "—");
                SetLine(_lines[1], "Meilleur DPS sur un combat", ps != null ? DpsMeterMod.Fmt(ps.bestFightDps) : "—");
                SetLine(_lines[2], "DPS moyen sur les boss", bossTime > 0 ? DpsMeterMod.Fmt(bossDamage / bossTime) : "—");
            }
        }

        private void Build()
        {
            UI_InGame_ResultView view = UnityEngine.Object.FindObjectOfType<UI_InGame_ResultView>();
            if (view == null) return;

            // Every line of the official stats list (game-wide and per-player lines).
            List<Component> stats = new List<Component>();
            stats.AddRange(view.GetComponentsInChildren<UI_InGame_Result_GameStatsItem>(false));
            stats.AddRange(view.GetComponentsInChildren<UI_InGame_Result_PlayerStatsItem>(false));
            TMP_Text styleText = stats.Count > 0 ? ContentOf(stats[0]) : null;

            foreach (UI_InGame_Result_HeroSkillItem s in view.GetComponentsInChildren<UI_InGame_Result_HeroSkillItem>(true))
            {
                // Not inside the icon: it is clipped to a circle by a mask.
                IconLabel label = CreateLabel((RectTransform)s.transform, styleText, above: true, 0.22f);
                label.skill = s;
                _labels.Add(label);
            }
            foreach (UI_InGame_Result_HeroGemItem g in view.GetComponentsInChildren<UI_InGame_Result_HeroGemItem>(true))
            {
                IconLabel label = CreateLabel((RectTransform)g.transform, styleText, above: false, 0.42f);
                label.gem = g;
                _labels.Add(label);
            }

            // Window with every source of the run, under the two result windows (left of "Continue").
            _style = styleText;
            UI_InGame_Result_HeroSkillItem[] skills = view.GetComponentsInChildren<UI_InGame_Result_HeroSkillItem>(true);
            RectTransform frame = CommonAncestor(skills);
            Canvas canvas = view.GetComponentInParent<Canvas>();
            RectTransform window = frame != null ? WindowOf(frame) : null;
            if (frame != null && canvas != null && window != null)
            {
                RectTransform canvasRt = (RectTransform)canvas.rootCanvas.transform;
                Vector3[] wc = new Vector3[4];
                window.GetWorldCorners(wc); // 0 = bottom-left, 2 = top-right
                Vector3 bl = canvasRt.InverseTransformPoint(wc[0]);
                Vector3 tr = canvasRt.InverseTransformPoint(wc[2]);
                float windowWidth = tr.x - bl.x;
                _cellWidth = Mathf.Clamp(frame.rect.width / 11f, 54f, 90f);

                GameObject go = new GameObject(Tag + " Sources", typeof(RectTransform));
                go.transform.SetParent(canvasRt, false);
                _strip = (RectTransform)go.transform;
                _strip.anchorMin = _strip.anchorMax = new Vector2(0.5f, 0.5f);
                _strip.pivot = new Vector2(0f, 0f);
                // Spans both windows minus room for the "Continue" button on the right.
                _stripWidth = Mathf.Max(400f, (canvasRt.rect.xMax - bl.x) - (bl.x - canvasRt.rect.xMin) - windowWidth * 0.3f);
                _strip.anchoredPosition = new Vector2(bl.x, canvasRt.rect.yMin + 26f);
                _strip.sizeDelta = new Vector2(_stripWidth, 10f);
                UiSkin.BuildWindowFrame(_strip);

                GameObject titleGo = new GameObject("Title", typeof(RectTransform));
                titleGo.transform.SetParent(_strip, false);
                _title = titleGo.AddComponent<TextMeshProUGUI>();
                if (styleText != null) _title.font = styleText.font;
                _title.fontSize = Mathf.Clamp(_cellWidth * 0.32f, 18f, 26f);
                _title.fontStyle = FontStyles.Bold;
                _title.color = new Color32(0x7F, 0xD5, 0xF3, 0xFF); // objective title blue
                _title.alignment = TextAlignmentOptions.TopLeft;
                _title.textWrappingMode = TextWrappingModes.NoWrap;
                _title.raycastTarget = false;
                _title.SetText("Dégâts par source");
                RectTransform trt = _title.rectTransform;
                trt.anchorMin = trt.anchorMax = trt.pivot = new Vector2(0f, 1f);
                trt.anchoredPosition = new Vector2(18f, -10f);
                trt.sizeDelta = new Vector2(600f, 40f);

                GameObject gridGo = new GameObject("Grid", typeof(RectTransform));
                gridGo.transform.SetParent(_strip, false);
                _grid = (RectTransform)gridGo.transform;
                _grid.anchorMin = _grid.anchorMax = _grid.pivot = new Vector2(0f, 1f);
                _grid.anchoredPosition = new Vector2(GridPadding, -(_title.fontSize * 1.6f + 10f));
            }

            // The list is the container holding the most stat lines; clone its last line for our values.
            Dictionary<Transform, int> counts = new Dictionary<Transform, int>();
            Transform list = null;
            foreach (Component c in stats)
            {
                Transform parent = c.transform.parent;
                counts.TryGetValue(parent, out int n);
                counts[parent] = ++n;
                if (list == null || n > counts[list]) list = parent;
            }
            if (list != null)
            {
                Component last = null;
                foreach (Component c in stats)
                {
                    if (c.transform.parent != list) continue;
                    if (last == null || c.transform.GetSiblingIndex() > last.transform.GetSiblingIndex()) last = c;
                }
                for (int i = 0; i < 3; i++) _lines.Add(CloneLine(last));
            }
            _built = true;
        }

        private static TMP_Text ContentOf(Component c)
        {
            switch (c)
            {
                case UI_InGame_Result_GameStatsItem g: return g.contentText;
                case UI_InGame_Result_PlayerStatsItem p: return p.contentText;
            }
            return null;
        }

        /// <summary>Fixed-size cells laid out in rows (wrapping), the window grows with the number of rows.</summary>
        private void LayoutStrip(List<SourceStats> sources, double total, HashSet<uint> equipped)
        {
            if (swordIcon == null) swordIcon = FindSword();
            float avail = _stripWidth - GridPadding * 2f;
            float cw = _cellWidth;
            int perRow = Mathf.Max(1, Mathf.FloorToInt(avail / cw));
            if (sources.Count > perRow * 2)
            {
                // Too many for two rows at the normal size: shrink the cells instead of adding rows.
                perRow = Mathf.CeilToInt(sources.Count / 2f);
                cw = Mathf.Max(38f, avail / perRow);
                perRow = Mathf.Max(1, Mathf.FloorToInt(avail / cw));
            }
            float iconSize = cw * 0.62f;
            float valueSize = Mathf.Clamp(cw * 0.27f, 12f, 21f);
            float cellHeight = iconSize + 8f + valueSize * 2.4f;
            for (int i = 0; i < sources.Count; i++)
            {
                if (i >= _cells.Count) _cells.Add(CreateCell());
                Cell c = _cells[i];
                SourceStats src = sources[i];
                c.root.SetActive(true);
                RectTransform rt = (RectTransform)c.root.transform;
                rt.sizeDelta = new Vector2(cw, cellHeight);
                rt.anchoredPosition = new Vector2((i % perRow) * cw + cw * 0.5f, -(i / perRow) * cellHeight);

                c.icon.rectTransform.sizeDelta = new Vector2(iconSize, iconSize);
                RectTransform brt = c.bar.rectTransform;
                brt.anchoredPosition = new Vector2(0f, -iconSize - 3f);
                brt.sizeDelta = new Vector2(iconSize * 0.75f, 3f);
                PlaceText(c.value, valueSize, -iconSize - 8f, cw);
                PlaceText(c.pct, valueSize * 0.78f, -iconSize - 8f - valueSize * 1.15f, cw);

                Sprite icon = src.icon != null ? src.icon : src.category == SourceCategory.Attack ? swordIcon : null;
                c.icon.sprite = icon;
                c.icon.enabled = icon != null;
                // Memories / essences thrown away during the run are shown dimmed.
                bool gone = (src.category == SourceCategory.Memory || src.category == SourceCategory.Essence) && !IsEquipped(src, equipped);
                c.icon.color = Color.white;
                c.veil.rectTransform.sizeDelta = new Vector2(iconSize, iconSize);
                c.veil.enabled = gone && icon != null; // dark veil: thrown away during the run
                string value = (gone ? "<alpha=#80>" : "") + (icon == null ? "<size=75%>" + Short(src.label) + "</size> " : "") + DpsMeterMod.Fmt(src.runDamage);
                if (c.value.text != value) c.value.SetText(value);
                c.value.color = new Color32(0xD5, 0xF8, 0xFF, 0xFF);
                c.bar.color = DamageKinds.Colors[DamageKinds.Dominant(src.runByKind)];
                string pct = Pct(src.runDamage, total);
                if (c.pct.text != pct) c.pct.SetText(pct);
            }
            for (int i = sources.Count; i < _cells.Count; i++) _cells[i].root.SetActive(false);
            if (_strip.gameObject.activeSelf != (sources.Count > 0)) _strip.gameObject.SetActive(sources.Count > 0); // nothing to show yet
            int rows = Mathf.Max(1, (sources.Count + perRow - 1) / perRow);
            float top = -_grid.anchoredPosition.y;
            _strip.sizeDelta = new Vector2(_stripWidth, top + rows * cellHeight + 12f);
        }

        private static void PlaceText(TextMeshProUGUI t, float size, float y, float width)
        {
            if (!Mathf.Approximately(t.fontSize, size)) t.fontSize = size;
            RectTransform rt = t.rectTransform;
            rt.anchoredPosition = new Vector2(0f, y);
            rt.sizeDelta = new Vector2(width + 20f, size * 1.3f);
        }

        /// <summary>The AD sword of the stats window (sprite 2 of its sprite asset).</summary>
        private static Sprite FindSword()
        {
            TMP_SpriteAsset asset = null;
            UI_InGame_HeroDetailWindow window = ManagerBase<UI_InGame_HeroDetailWindow>.softInstance;
            Transform t = window != null ? window.transform.Find("AD/Icon Text") : null;
            TMP_Text tmp = t != null ? t.GetComponent<TMP_Text>() : null;
            if (tmp != null) asset = tmp.spriteAsset;
            if (asset == null) asset = TMP_Settings.defaultSpriteAsset;
            return DpsMeterMod.SpriteFromAsset(asset, 2);
        }

        private static bool IsEquipped(SourceStats src, HashSet<uint> equipped)
        {
            if (src.key == null) return true;
            int hash = src.key.LastIndexOf('#');
            if (hash < 0) return true;
            return uint.TryParse(src.key.Substring(hash + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out uint id) && equipped.Contains(id);
        }

        private static string Short(string label)
        {
            if (string.IsNullOrEmpty(label)) return "";
            return label.Length > 8 ? label.Substring(0, 7) + "." : label;
        }

        private Cell CreateCell()
        {
            GameObject root = new GameObject("Cell", typeof(RectTransform));
            root.transform.SetParent(_grid, false);
            RectTransform rt = (RectTransform)root.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);

            GameObject iconGo = new GameObject("Icon", typeof(RectTransform));
            iconGo.transform.SetParent(root.transform, false);
            UnityEngine.UI.Image img = iconGo.AddComponent<UnityEngine.UI.Image>();
            img.preserveAspect = true;
            img.raycastTarget = false;
            RectTransform irt = img.rectTransform;
            irt.anchorMin = irt.anchorMax = irt.pivot = new Vector2(0.5f, 1f);
            irt.anchoredPosition = Vector2.zero;

            GameObject veilGo = new GameObject("Veil", typeof(RectTransform));
            veilGo.transform.SetParent(root.transform, false);
            UnityEngine.UI.Image veil = veilGo.AddComponent<UnityEngine.UI.Image>();
            veil.raycastTarget = false;
            veil.color = new Color32(0x07, 0x13, 0x1D, 0xB0);
            veil.enabled = false;
            RectTransform vrt = veil.rectTransform;
            vrt.anchorMin = vrt.anchorMax = vrt.pivot = new Vector2(0.5f, 1f);
            vrt.anchoredPosition = Vector2.zero;

            GameObject barGo = new GameObject("Element", typeof(RectTransform));
            barGo.transform.SetParent(root.transform, false);
            UnityEngine.UI.Image bar = barGo.AddComponent<UnityEngine.UI.Image>();
            bar.raycastTarget = false;
            RectTransform brt = bar.rectTransform;
            brt.anchorMin = brt.anchorMax = brt.pivot = new Vector2(0.5f, 1f);

            TextMeshProUGUI value = CellText(root.transform, _style, 16f, 0f, FontStyles.Bold);
            TextMeshProUGUI pct = CellText(root.transform, _style, 13f, 0f, FontStyles.Normal);
            pct.color = new Color(1f, 1f, 1f, 0.6f);
            return new Cell { root = root, icon = img, value = value, pct = pct, bar = bar, veil = veil };
        }

        private static TextMeshProUGUI CellText(Transform parent, TMP_Text style, float size, float y, FontStyles fs)
        {
            GameObject go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, y);
            rt.sizeDelta = new Vector2(140f, size * 1.3f);
            TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();
            t.font = style != null && style.font != null ? style.font : TMP_Settings.defaultFontAsset;
            if (style != null && style.fontSharedMaterial != null) t.fontSharedMaterial = style.fontSharedMaterial;
            t.fontSize = size;
            t.fontStyle = fs;
            t.alignment = TextAlignmentOptions.Top;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Overflow;
            t.raycastTarget = false;
            t.richText = true;
            return t;
        }

        /// <summary>The result window containing the memories frame (first ancestor much taller than the frame).</summary>
        private static RectTransform WindowOf(RectTransform frame)
        {
            Transform t = frame.parent;
            while (t != null)
            {
                if (t is RectTransform rt && rt.rect.height > frame.rect.height * 2.5f) return rt;
                t = t.parent;
            }
            return null;
        }

        /// <summary>Deepest transform containing every memory item (the frame around them).</summary>
        private static RectTransform CommonAncestor(UI_InGame_Result_HeroSkillItem[] items)
        {
            if (items == null || items.Length == 0) return null;
            Transform t = items[0].transform.parent;
            while (t != null)
            {
                bool all = true;
                foreach (UI_InGame_Result_HeroSkillItem it in items)
                {
                    if (!it.transform.IsChildOf(t)) all = false;
                }
                if (all) return t as RectTransform;
                t = t.parent;
            }
            return null;
        }

        private static StatLine CloneLine(Component source)
        {
            GameObject go = UnityEngine.Object.Instantiate(source.gameObject, source.transform.parent, false);
            go.name = Tag + " Line";
            StatLine line = new StatLine { root = go };
            UI_InGame_Result_GameStatsItem g = go.GetComponent<UI_InGame_Result_GameStatsItem>();
            UI_InGame_Result_PlayerStatsItem p = go.GetComponent<UI_InGame_Result_PlayerStatsItem>();
            if (g != null)
            {
                line.header = g.headerText;
                line.content = g.contentText;
                line.score = g.scoreText;
                UnityEngine.Object.DestroyImmediate(g);
            }
            if (p != null)
            {
                line.header = p.headerText;
                line.content = p.contentText;
                line.score = p.scoreText;
                if (p.bestObject != null) p.bestObject.SetActive(false);
                UnityEngine.Object.DestroyImmediate(p);
            }
            // No game script on our copy: the result view must not rewrite our texts.
            foreach (DewLocalizedText loc in go.GetComponentsInChildren<DewLocalizedText>(true)) UnityEngine.Object.DestroyImmediate(loc);
            go.transform.SetAsLastSibling();
            return line;
        }

        private static void SetLine(StatLine line, string header, string value)
        {
            if (line.header != null && line.header.text != header) line.header.SetText(header);
            if (line.content != null && line.content.text != value) line.content.SetText(value);
            if (line.score != null && line.score.text.Length > 0) line.score.SetText("");
        }

        private static void SetPill(IconLabel l, string text, Color element)
        {
            bool show = !string.IsNullOrEmpty(text);
            if (l.pill.gameObject.activeSelf != show) l.pill.gameObject.SetActive(show);
            if (!show) return;
            if (l.text.text != text) l.text.SetText(text);
            Vector2 pref = l.text.GetPreferredValues(text);
            l.pill.sizeDelta = new Vector2(pref.x + l.text.fontSize * 1.0f, l.text.fontSize * 1.5f);
            l.underline.color = element;
        }

        private static IconLabel CreateLabel(RectTransform parent, TMP_Text style, bool above, float sizeRatio)
        {
            float size = Mathf.Clamp(parent.rect.height * sizeRatio, 15f, 24f);
            GameObject pillGo = new GameObject(Tag + " Label", typeof(RectTransform));
            pillGo.transform.SetParent(parent, false);
            RectTransform pill = (RectTransform)pillGo.transform;
            pill.anchorMin = pill.anchorMax = new Vector2(0.5f, above ? 1f : 0f);
            pill.pivot = new Vector2(0.5f, 0.5f);
            pill.anchoredPosition = new Vector2(0f, above ? size * 0.35f : -size * 0.45f);
            UnityEngine.UI.Image bg = UiSkin.KeyHintBackground(pillGo);
            // Thin element-colored line at the bottom of the tag.
            GameObject lineGo = new GameObject("Element", typeof(RectTransform));
            lineGo.transform.SetParent(pill, false);
            RectTransform lrt = (RectTransform)lineGo.transform;
            lrt.anchorMin = new Vector2(0.15f, 0f);
            lrt.anchorMax = new Vector2(0.85f, 0f);
            lrt.pivot = new Vector2(0.5f, 0f);
            lrt.offsetMin = new Vector2(0f, 2f);
            lrt.offsetMax = new Vector2(0f, 4f);
            UnityEngine.UI.Image underline = lineGo.AddComponent<UnityEngine.UI.Image>();
            underline.raycastTarget = false;

            GameObject go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(pill, false);
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();
            t.font = style != null && style.font != null ? style.font : TMP_Settings.defaultFontAsset;
            if (style != null && style.fontSharedMaterial != null) t.fontSharedMaterial = style.fontSharedMaterial;
            t.fontSize = size;
            t.fontStyle = FontStyles.Normal;
            t.color = new Color32(0xD5, 0xF8, 0xFF, 0xFF);
            t.alignment = TextAlignmentOptions.Center;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Overflow;
            t.raycastTarget = false;
            t.richText = true;
            pillGo.SetActive(false);
            return new IconLabel { text = t, pill = pill, bg = bg, underline = underline };
        }

        private static PlayerStats FindPlayer(DamageTracker tracker, DewGameResult.PlayerData pd)
        {
            foreach (PlayerStats p in tracker.players)
            {
                if (pd.isLocalPlayer && p.player == DewPlayer.local) return p;
            }
            foreach (PlayerStats p in tracker.players)
            {
                if (!string.IsNullOrEmpty(pd.playerProfileName) && p.name == pd.playerProfileName) return p;
            }
            return tracker.players.Count == 1 ? tracker.players[0] : null;
        }

        private static SourceStats FindSource(PlayerStats p, uint netId)
        {
            string suffix = "#" + netId.ToString(CultureInfo.InvariantCulture);
            foreach (SourceStats s in p.sources.Values)
            {
                if (s.key != null && s.key.EndsWith(suffix, StringComparison.Ordinal)) return s;
            }
            return null;
        }

        private static string Pct(double part, double whole)
        {
            double pct = whole > 0 ? part / whole * 100.0 : 0;
            return (pct < 1 ? "<1" : Math.Round(pct).ToString("0", CultureInfo.InvariantCulture)) + "%";
        }
    }
}
