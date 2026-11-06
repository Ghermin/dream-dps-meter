using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DreamDpsMeter
{
    public struct RowSpec
    {
        public float height;
        public float indent;
        public string left;
        public string right;
        public float fill;
        public Color barColor;
        public bool thinBar;
        public Sprite icon;
        public Color iconColor;
        public float iconSize;
        /// <summary>Optional icon drawn just left of the right text (e.g. dream dust).</summary>
        public Sprite rightIcon;
        public float fontSize;
        public bool header;
        /// <summary>Optional stacked bar drawn between the left text/icon and the right column.</summary>
        public double[] segments;
        public Color[] segmentColors;
        public float segmentFill;
        public float segmentHeight;
    }

    public class TextStyle
    {
        public TMP_FontAsset font;
        public Material material;
        public Color color = new Color(0.92f, 0.94f, 0.97f, 1f);
        public FontStyles style = FontStyles.Normal;
        /// <summary>Sprite asset for inline &lt;sprite=N&gt; icons (the stats window's heart, sword, shield...).</summary>
        public TMP_SpriteAsset spriteAsset;

        public static TextStyle From(TMP_Text t, TextStyle fallback)
        {
            if (t == null) return fallback;
            return new TextStyle { font = t.font, material = t.fontSharedMaterial, color = t.color, style = FontStyles.Normal, spriteAsset = t.spriteAsset };
        }
    }

    /// <summary>
    /// A panel made of pooled rows (optional icon, left text, right text, bar).
    /// Its background is either a clone of a game window frame, or a plain dark box.
    /// </summary>
    public class MeterPanel
    {
        private class Row
        {
            public RectTransform rt;
            public RectTransform barRt;
            public Image bar;
            public Image icon;
            public Image rightIcon;
            public TextMeshProUGUI left;
            public TextMeshProUGUI right;
            public readonly List<Image> segs = new List<Image>();
        }

        public RectTransform rect { get; private set; }
        public LayoutElement layout { get; private set; }
        public bool hasSkin { get; private set; }
        public RectTransform skin { get; private set; }
        public bool alive => rect != null;

        public Vector4 padding = new Vector4(14f, 12f, 9f, 9f); // left, right, top, bottom
        public float rowGap = 1f;

        private GameObject _ownCanvas;
        private RectTransform _content;
        private readonly List<Row> _rows = new List<Row>();
        private int _used;
        private TextStyle _headerStyle;
        private TextStyle _bodyStyle;

        public static MeterPanel Create(string name, Transform parent, bool ownCanvas, Transform skinSource,
            string[] skinParts, TextStyle headerStyle, TextStyle bodyStyle)
        {
            MeterPanel p = new MeterPanel { _headerStyle = headerStyle, _bodyStyle = bodyStyle };
            Transform host = parent;
            if (ownCanvas)
            {
                p._ownCanvas = new GameObject(name + " Canvas", typeof(RectTransform));
                p._ownCanvas.transform.SetParent(parent, false);
                Canvas canvas = p._ownCanvas.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = -10;
                CanvasScaler scaler = p._ownCanvas.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.matchWidthOrHeight = 0.5f;
                host = p._ownCanvas.transform;
            }

            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(host, false);
            p.rect = (RectTransform)go.transform;
            p.layout = go.AddComponent<LayoutElement>();

            p.skin = skinSource != null ? UiSkin.CloneParts(skinSource, skinParts, p.rect) : null;
            if (p.skin != null)
            {
                p.hasSkin = true;
            }
            else
            {
                Image bg = go.AddComponent<Image>();
                bg.raycastTarget = false;
                bg.color = new Color(0.035f, 0.055f, 0.10f, 0.82f);
                Outline o = go.AddComponent<Outline>();
                o.effectColor = new Color(0.62f, 0.78f, 0.92f, 0.35f);
                o.effectDistance = new Vector2(1f, -1f);
            }

            GameObject content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(p.rect, false);
            p._content = (RectTransform)content.transform;
            p._content.anchorMin = Vector2.zero;
            p._content.anchorMax = Vector2.one;
            p._content.offsetMin = Vector2.zero;
            p._content.offsetMax = Vector2.zero;
            return p;
        }

        private Image _corner;

        /// <summary>
        /// An icon pinned to the panel's top-right corner (centre given from that corner), drawn above the content:
        /// used to sit the header star inside the circle of the mirrored objective ornament.
        /// </summary>
        public void SetCornerIcon(Sprite sprite, Color color, float size, Vector2 centreFromTopRight)
        {
            if (rect == null) return;
            if (_corner == null)
            {
                GameObject go = new GameObject("CornerIcon", typeof(RectTransform));
                go.transform.SetParent(rect, false);
                _corner = go.AddComponent<Image>();
                _corner.raycastTarget = false;
                _corner.preserveAspect = true;
            }
            _corner.transform.SetAsLastSibling();
            RectTransform crt = _corner.rectTransform;
            crt.anchorMin = crt.anchorMax = new Vector2(1f, 1f);
            crt.pivot = new Vector2(0.5f, 0.5f);
            crt.anchoredPosition = new Vector2(-centreFromTopRight.x, -centreFromTopRight.y);
            crt.sizeDelta = new Vector2(size, size);
            _corner.sprite = sprite;
            _corner.color = color;
            _corner.enabled = sprite != null;
        }

        public void Destroy()
        {
            if (_ownCanvas != null) Object.Destroy(_ownCanvas);
            else if (rect != null) Object.Destroy(rect.gameObject);
            _ownCanvas = null;
            rect = null;
            _rows.Clear();
        }

        public void SetActive(bool active)
        {
            GameObject go = _ownCanvas != null ? _ownCanvas : rect != null ? rect.gameObject : null;
            if (go != null && go.activeSelf != active) go.SetActive(active);
        }

        public void Render(List<RowSpec> specs, float width)
        {
            if (!alive) return;
            _used = 0;
            float inner = width - padding.x - padding.y;
            float y = padding.z;
            for (int i = 0; i < specs.Count; i++)
            {
                RowSpec s = specs[i];
                Row r = NextRow();
                Layout(r, s, padding.x + s.indent, y, inner - s.indent);
                y += s.height + rowGap;
            }
            for (int i = _used; i < _rows.Count; i++)
            {
                if (_rows[i].rt.gameObject.activeSelf) _rows[i].rt.gameObject.SetActive(false);
            }
            float height = y - rowGap + padding.w;
            rect.sizeDelta = new Vector2(width, height);
            if (layout != null && !layout.ignoreLayout)
            {
                layout.preferredWidth = width;
                layout.preferredHeight = height;
                layout.minHeight = height;
            }
        }

        private Row NextRow()
        {
            if (_used >= _rows.Count) _rows.Add(CreateRow());
            Row r = _rows[_used++];
            if (!r.rt.gameObject.activeSelf) r.rt.gameObject.SetActive(true);
            return r;
        }

        private Row CreateRow()
        {
            GameObject go = new GameObject("Row", typeof(RectTransform));
            go.transform.SetParent(_content, false);
            Row r = new Row { rt = (RectTransform)go.transform };
            r.rt.anchorMin = r.rt.anchorMax = r.rt.pivot = new Vector2(0f, 1f);

            GameObject barGo = new GameObject("Bar", typeof(RectTransform));
            barGo.transform.SetParent(go.transform, false);
            r.barRt = (RectTransform)barGo.transform;
            r.bar = barGo.AddComponent<Image>();
            r.bar.raycastTarget = false;

            GameObject iconGo = new GameObject("Icon", typeof(RectTransform));
            iconGo.transform.SetParent(go.transform, false);
            r.icon = iconGo.AddComponent<Image>();
            r.icon.raycastTarget = false;
            r.icon.preserveAspect = true;
            RectTransform irt = r.icon.rectTransform;
            irt.anchorMin = irt.anchorMax = irt.pivot = new Vector2(0f, 0.5f);

            GameObject ricon = new GameObject("RightIcon", typeof(RectTransform));
            ricon.transform.SetParent(go.transform, false);
            r.rightIcon = ricon.AddComponent<Image>();
            r.rightIcon.raycastTarget = false;
            r.rightIcon.preserveAspect = true;
            RectTransform rirt = r.rightIcon.rectTransform;
            rirt.anchorMin = rirt.anchorMax = new Vector2(1f, 0.5f);
            rirt.pivot = new Vector2(1f, 0.5f);

            r.left = CreateText(go.transform, TextAlignmentOptions.MidlineLeft);
            r.right = CreateText(go.transform, TextAlignmentOptions.MidlineRight);
            return r;
        }

        private static TextMeshProUGUI CreateText(Transform parent, TextAlignmentOptions align)
        {
            GameObject go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();
            t.richText = true;
            t.raycastTarget = false;
            t.alignment = align;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Overflow;
            t.enableAutoSizing = false;
            t.margin = Vector4.zero;
            return t;
        }

        private void Layout(Row r, RowSpec s, float x, float y, float w)
        {
            r.rt.anchoredPosition = new Vector2(x, -y);
            r.rt.sizeDelta = new Vector2(w, s.height);

            // Bar: either a faint full-row fill, or a thin line under the row.
            float fill = Mathf.Clamp01(s.fill);
            r.bar.enabled = fill > 0f && s.barColor.a > 0f;
            r.bar.color = s.barColor;
            r.barRt.anchorMin = Vector2.zero;
            r.barRt.anchorMax = new Vector2(fill, s.thinBar ? 0f : 1f);
            r.barRt.pivot = Vector2.zero;
            r.barRt.offsetMin = Vector2.zero;
            r.barRt.offsetMax = s.thinBar ? new Vector2(0f, 2f) : Vector2.zero;

            float iconSize = s.icon == null ? 0f : s.iconSize > 0f ? s.iconSize : Mathf.Min(s.height - 4f, 32f);
            r.icon.enabled = s.icon != null;
            if (s.icon != null)
            {
                r.icon.sprite = s.icon;
                r.icon.color = s.iconColor.a > 0f ? s.iconColor : Color.white;
                r.icon.rectTransform.anchoredPosition = new Vector2(2f, 0f);
                r.icon.rectTransform.sizeDelta = new Vector2(iconSize, iconSize);
            }

            TextStyle style = s.header ? _headerStyle : _bodyStyle;
            float leftStart = s.icon != null ? iconSize + 10f : 3f;

            // Right column first: the left text gets whatever width remains and ends with "…" if too long.
            Apply(r.right, style, s.fontSize, s.right);
            RectTransform rrt = r.right.rectTransform;
            rrt.anchorMin = Vector2.zero;
            rrt.anchorMax = Vector2.one;
            rrt.offsetMin = new Vector2(leftStart, 0f);
            rrt.offsetMax = new Vector2(-3f, 0f);
            float rightWidth = string.IsNullOrEmpty(s.right) ? 0f : r.right.GetPreferredValues(s.right).x;
            r.rightIcon.enabled = s.rightIcon != null;
            if (s.rightIcon != null)
            {
                float ris = Mathf.Min(s.height - 6f, 30f);
                r.rightIcon.sprite = s.rightIcon;
                r.rightIcon.rectTransform.sizeDelta = new Vector2(ris, ris);
                r.rightIcon.rectTransform.anchoredPosition = new Vector2(-(3f + rightWidth + 6f), 0f);
                rightWidth += ris + 6f;
            }

            Apply(r.left, style, s.fontSize, s.left);
            r.left.overflowMode = TextOverflowModes.Ellipsis;
            RectTransform lrt = r.left.rectTransform;
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = new Vector2(1f, 1f);
            lrt.offsetMin = new Vector2(leftStart, 0f);
            lrt.offsetMax = new Vector2(-(3f + rightWidth + (rightWidth > 0f ? 14f : 0f)), 0f);

            LayoutSegments(r, s, w, leftStart, rightWidth);
        }

        private static void LayoutSegments(Row r, RowSpec s, float w, float leftStart, float rightWidth)
        {
            int used = 0;
            if (s.segments != null && s.segmentFill > 0f)
            {
                float leftWidth = string.IsNullOrEmpty(s.left) ? 0f : r.left.GetPreferredValues(s.left).x + 10f;
                float x0 = leftStart + leftWidth;
                float x1 = w - 3f - rightWidth - 16f;
                float length = Mathf.Max(0f, x1 - x0) * Mathf.Clamp01(s.segmentFill);
                double total = 0;
                foreach (double v in s.segments) total += v;
                float h = s.segmentHeight > 0f ? s.segmentHeight : 8f;
                float x = x0;
                for (int i = 0; i < s.segments.Length && total > 0; i++)
                {
                    if (s.segments[i] <= 0) continue;
                    float sw = (float)(length * s.segments[i] / total);
                    if (sw < 0.5f) continue;
                    if (used >= r.segs.Count)
                    {
                        GameObject go = new GameObject("Seg", typeof(RectTransform));
                        go.transform.SetParent(r.rt, false);
                        Image img = go.AddComponent<Image>();
                        img.raycastTarget = false;
                        r.segs.Add(img);
                    }
                    Image seg = r.segs[used++];
                    seg.enabled = true;
                    seg.color = s.segmentColors != null && i < s.segmentColors.Length ? s.segmentColors[i] : Color.white;
                    RectTransform srt = seg.rectTransform;
                    srt.anchorMin = srt.anchorMax = new Vector2(0f, 0.5f);
                    srt.pivot = new Vector2(0f, 0.5f);
                    srt.anchoredPosition = new Vector2(x, 0f);
                    srt.sizeDelta = new Vector2(Mathf.Max(1f, sw - 1f), h); // 1px gap between segments
                    x += sw;
                }
            }
            for (int i = used; i < r.segs.Count; i++) r.segs[i].enabled = false;
        }

        private static void Apply(TextMeshProUGUI t, TextStyle style, float size, string text)
        {
            if (style != null)
            {
                if (style.font != null && t.font != style.font) t.font = style.font;
                if (style.material != null && t.fontSharedMaterial != style.material) t.fontSharedMaterial = style.material;
                t.color = style.color;
                t.fontStyle = style.style;
                if (style.spriteAsset != null && t.spriteAsset != style.spriteAsset) t.spriteAsset = style.spriteAsset;
            }
            if (!Mathf.Approximately(t.fontSize, size)) t.fontSize = size;
            text = text ?? "";
            if (t.text != text) t.SetText(text);
        }
    }
}
