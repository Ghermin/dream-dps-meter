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

        /// <summary>
        /// Max panel height (0 = the panel grows with its rows). Past it, the rows after the first
        /// <see cref="pinnedRows"/> (the header) scroll, with a scrollbar on the right (mouse wheel or drag).
        /// </summary>
        public float maxHeight;
        public int pinnedRows = 1;
        public Color scrollbarColor = new Color(0.75f, 0.86f, 0.93f, 0.65f);

        private const float ScrollbarWidth = 6f;
        private const float ScrollbarGap = 8f;
        private const float ScrollbarGrab = 14f; // extra clickable width on each side of the thin bar
        private const float MinListHeight = 120f;
        private const float MinHandle = 24f;
        private const float WheelStep = 42f;     // about one row per wheel notch

        private GameObject _ownCanvas;
        private RectTransform _content;
        private RectTransform _viewport;
        private RectTransform _list;
        private RectTransform _track;
        private RectTransform _handle;
        private Image _handleImage;

        // Scrolling is driven by MeterPanel itself (HandlePointer), not by the EventSystem:
        // the game's windows do not forward pointer events to our panel.
        private float _scrollOffset;
        private float _maxOffset;
        private float _viewHeight;
        private float _listHeight;
        private bool _dragging;
        private float _dragStartY;
        private float _dragStartOffset;
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
            int pinned = maxHeight > 0f ? Mathf.Clamp(pinnedRows, 0, specs.Count) : specs.Count;

            // Pinned rows (header): laid out directly in the panel, never scrolled.
            float y = padding.z;
            for (int i = 0; i < pinned; i++)
            {
                RowSpec s = specs[i];
                Row r = NextRow(_content);
                Layout(r, s, padding.x + s.indent, y, inner - s.indent);
                y += s.height + rowGap;
            }

            // The other rows go in a masked list that scrolls once the panel would exceed maxHeight.
            float listHeight = 0f;
            for (int i = pinned; i < specs.Count; i++) listHeight += specs[i].height + rowGap;
            if (listHeight > 0f) listHeight -= rowGap;
            float maxList = maxHeight > 0f ? Mathf.Max(MinListHeight, maxHeight - y - padding.w) : float.MaxValue;
            bool scroll = listHeight > maxList + 0.5f;

            float height;
            if (listHeight > 0f)
            {
                EnsureScrollArea();
                float viewHeight = scroll ? maxList : listHeight;
                float listWidth = inner - (scroll ? ScrollbarWidth + ScrollbarGap : 0f);
                float ly = 0f;
                for (int i = pinned; i < specs.Count; i++)
                {
                    RowSpec s = specs[i];
                    Row r = NextRow(_list);
                    Layout(r, s, s.indent, ly, listWidth - s.indent);
                    ly += s.height + rowGap;
                }
                _viewport.anchoredPosition = new Vector2(padding.x, -y);
                _viewport.sizeDelta = new Vector2(listWidth, viewHeight);
                _list.sizeDelta = new Vector2(listWidth, listHeight);
                _track.anchoredPosition = new Vector2(padding.x + inner - ScrollbarWidth, -y);
                _track.sizeDelta = new Vector2(ScrollbarWidth, viewHeight);
                if (_track.gameObject.activeSelf != scroll) _track.gameObject.SetActive(scroll);
                _viewHeight = viewHeight;
                _listHeight = listHeight;
                _maxOffset = scroll ? listHeight - viewHeight : 0f;
                ApplyScroll();
                height = y + viewHeight + padding.w;
            }
            else
            {
                _maxOffset = 0f;
                _dragging = false;
                height = y - rowGap + padding.w;
            }
            if (_viewport != null && _viewport.gameObject.activeSelf != listHeight > 0f) _viewport.gameObject.SetActive(listHeight > 0f);
            if (_track != null && listHeight <= 0f && _track.gameObject.activeSelf) _track.gameObject.SetActive(false);

            for (int i = _used; i < _rows.Count; i++)
            {
                if (_rows[i].rt.gameObject.activeSelf) _rows[i].rt.gameObject.SetActive(false);
            }
            rect.sizeDelta = new Vector2(width, height);
            if (layout != null && !layout.ignoreLayout)
            {
                layout.preferredWidth = width;
                layout.preferredHeight = height;
                layout.minHeight = height;
            }
        }

        /// <summary>Back to the top of the scrolled list (e.g. when the panel is opened again).</summary>
        public void ScrollToTop()
        {
            _scrollOffset = 0f;
            _dragging = false;
            ApplyScroll();
        }

        /// <summary>
        /// Mouse input for the scrolled list, called every frame while the panel is visible:
        /// wheel over the list or the bar, drag of the bar handle, click on the bar to jump.
        /// </summary>
        public void HandlePointer(Vector2 screenPos, float wheelNotches, bool pressed, bool held)
        {
            if (!alive || _viewport == null || _maxOffset <= 0f || !_viewport.gameObject.activeInHierarchy)
            {
                _dragging = false;
                return;
            }
            Camera cam = EventCamera();
            bool overBar = RectTransformUtility.ScreenPointToLocalPointInRectangle(_track, screenPos, cam, out Vector2 barLocal)
                           && barLocal.x >= -ScrollbarGrab && barLocal.x <= ScrollbarWidth + ScrollbarGrab
                           && barLocal.y <= 0f && barLocal.y >= -_viewHeight;
            bool overList = RectTransformUtility.RectangleContainsScreenPoint(_viewport, screenPos, cam);

            if (!held) _dragging = false;
            if (pressed && overBar)
            {
                float handleTop = -HandleY();
                float handleBottom = handleTop - HandleHeight();
                if (barLocal.y > handleTop || barLocal.y < handleBottom)
                {
                    // Click on the track: centre the handle there, then keep dragging from it.
                    float travel = _viewHeight - HandleHeight();
                    float t = travel > 0f ? Mathf.Clamp01((-barLocal.y - HandleHeight() * 0.5f) / travel) : 0f;
                    _scrollOffset = t * _maxOffset;
                }
                _dragging = true;
                _dragStartY = barLocal.y;
                _dragStartOffset = _scrollOffset;
            }
            if (_dragging && held)
            {
                float travel = _viewHeight - HandleHeight();
                if (travel > 0f) _scrollOffset = _dragStartOffset + (_dragStartY - barLocal.y) * (_maxOffset / travel);
            }
            if (Mathf.Abs(wheelNotches) > 0.001f && (overList || overBar))
            {
                _scrollOffset -= wheelNotches * WheelStep;
            }
            ApplyScroll();
        }

        private Camera EventCamera()
        {
            Canvas c = rect.GetComponentInParent<Canvas>();
            if (c == null) return null;
            c = c.rootCanvas;
            return c.renderMode == RenderMode.ScreenSpaceOverlay ? null : c.worldCamera;
        }

        private float HandleHeight()
        {
            if (_listHeight <= 0f) return _viewHeight;
            return Mathf.Clamp(_viewHeight * _viewHeight / _listHeight, MinHandle, _viewHeight);
        }

        /// <summary>Distance from the top of the bar to the top of the handle.</summary>
        private float HandleY()
        {
            return _maxOffset > 0f ? (_viewHeight - HandleHeight()) * (_scrollOffset / _maxOffset) : 0f;
        }

        private void ApplyScroll()
        {
            _scrollOffset = Mathf.Clamp(_scrollOffset, 0f, Mathf.Max(0f, _maxOffset));
            if (_list != null) _list.anchoredPosition = new Vector2(0f, Mathf.Round(_scrollOffset));
            if (_handle != null)
            {
                _handle.anchoredPosition = new Vector2(0f, -HandleY());
                _handle.sizeDelta = new Vector2(0f, HandleHeight());
                _handleImage.color = _dragging ? new Color(scrollbarColor.r, scrollbarColor.g, scrollbarColor.b, 1f) : scrollbarColor;
            }
        }

        /// <summary>Masked viewport + list, and a thin scrollbar beside it (created on first use).</summary>
        private void EnsureScrollArea()
        {
            if (_viewport != null) return;

            GameObject vp = new GameObject("Viewport", typeof(RectTransform));
            vp.transform.SetParent(rect, false);
            _viewport = (RectTransform)vp.transform;
            _viewport.anchorMin = _viewport.anchorMax = _viewport.pivot = new Vector2(0f, 1f);
            vp.AddComponent<RectMask2D>();

            GameObject list = new GameObject("List", typeof(RectTransform));
            list.transform.SetParent(_viewport, false);
            _list = (RectTransform)list.transform;
            _list.anchorMin = _list.anchorMax = _list.pivot = new Vector2(0f, 1f);

            GameObject sb = new GameObject("Scrollbar", typeof(RectTransform));
            sb.transform.SetParent(rect, false);
            _track = (RectTransform)sb.transform;
            _track.anchorMin = _track.anchorMax = _track.pivot = new Vector2(0f, 1f);
            Image track = sb.AddComponent<Image>();
            track.raycastTarget = false;
            track.color = new Color(1f, 1f, 1f, 0.08f);

            GameObject handle = new GameObject("Handle", typeof(RectTransform));
            handle.transform.SetParent(sb.transform, false);
            _handle = (RectTransform)handle.transform;
            _handle.anchorMin = new Vector2(0f, 1f);
            _handle.anchorMax = new Vector2(1f, 1f);
            _handle.pivot = new Vector2(0.5f, 1f);
            _handleImage = handle.AddComponent<Image>();
            _handleImage.raycastTarget = false;
            _handleImage.color = scrollbarColor;
            sb.SetActive(false);
        }

        private Row NextRow(RectTransform parent)
        {
            if (_used >= _rows.Count) _rows.Add(CreateRow(parent));
            Row r = _rows[_used++];
            if (r.rt.parent != parent) r.rt.SetParent(parent, false);
            if (!r.rt.gameObject.activeSelf) r.rt.gameObject.SetActive(true);
            return r;
        }

        private Row CreateRow(RectTransform parent)
        {
            GameObject go = new GameObject("Row", typeof(RectTransform));
            go.transform.SetParent(parent, false);
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
