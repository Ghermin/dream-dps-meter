using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DreamDpsMeter
{
    /// <summary>
    /// Borrows the look of existing game windows (objective box, stats window) by cloning
    /// their frame graphics without any of their scripts or texts.
    /// </summary>
    public static class UiSkin
    {
        /// <summary>
        /// Clone <paramref name="source"/> (its own Image included) keeping only the direct children named in
        /// <paramref name="keepChildren"/>, strip every script/text, and stretch it behind <paramref name="parent"/>'s content.
        /// </summary>
        public static RectTransform CloneParts(Transform source, string[] keepChildren, RectTransform parent)
        {
            if (source == null) return null;
            GameObject holder = new GameObject("DreamDpsMeter SkinHolder");
            holder.SetActive(false); // children instantiated here never Awake/OnEnable
            try
            {
                GameObject clone = UnityEngine.Object.Instantiate(source.gameObject, holder.transform, false);
                clone.name = "Skin (" + source.name + ")";
                RectTransform root = (RectTransform)clone.transform;

                for (int i = root.childCount - 1; i >= 0; i--)
                {
                    Transform c = root.GetChild(i);
                    if (Array.IndexOf(keepChildren, c.name) < 0) UnityEngine.Object.DestroyImmediate(c.gameObject);
                }
                StripBehaviours(clone);

                root.SetParent(parent, false);
                root.anchorMin = Vector2.zero;
                root.anchorMax = Vector2.one;
                root.pivot = new Vector2(0.5f, 0.5f);
                root.offsetMin = Vector2.zero;
                root.offsetMax = Vector2.zero;
                root.localScale = Vector3.one;
                root.localRotation = Quaternion.identity;
                root.SetAsFirstSibling();
                clone.SetActive(true);
                return root;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[DreamDpsMeter] Could not clone skin from " + source.name + ": " + e);
                return null;
            }
            finally
            {
                UnityEngine.Object.Destroy(holder);
            }
        }

        private static void StripBehaviours(GameObject clone)
        {
            // Several passes so components that others depend on can be removed afterwards.
            for (int pass = 0; pass < 3; pass++)
            {
                foreach (Component c in clone.GetComponentsInChildren<Component>(true))
                {
                    if (c == null || c is Transform || c is CanvasRenderer || c is Graphic || c is BaseMeshEffect ||
                        c is Mask || c is RectMask2D)
                    {
                        continue;
                    }
                    try
                    {
                        UnityEngine.Object.DestroyImmediate(c);
                    }
                    catch
                    {
                        // dependency, retried next pass
                    }
                }
            }
            foreach (Graphic g in clone.GetComponentsInChildren<Graphic>(true))
            {
                if (g is TMP_Text) UnityEngine.Object.DestroyImmediate(g);
                else g.raycastTarget = false;
            }
        }

        // ------------------------------------------------------------------ game sprites by name

        private static readonly Dictionary<string, Sprite> _sprites = new Dictionary<string, Sprite>();

        /// <summary>A sprite the game has loaded (e.g. "UISprite", "Tooltip_Border"), found by name.</summary>
        public static Sprite GameSprite(string name)
        {
            if (_sprites.TryGetValue(name, out Sprite cached) && cached != null) return cached;
            foreach (Sprite sp in Resources.FindObjectsOfTypeAll<Sprite>())
            {
                if (sp != null && sp.name == name)
                {
                    _sprites[name] = sp;
                    return sp;
                }
            }
            return null;
        }

        private static Image AddLayer(RectTransform parent, string name, Sprite sprite, Color color, float grow, float ppu)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(-grow, -grow);
            rt.offsetMax = new Vector2(grow, grow);
            Image img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.type = sprite != null ? Image.Type.Sliced : Image.Type.Simple;
            img.pixelsPerUnitMultiplier = ppu;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        /// <summary>The stats-window look (shadow, background, borders, top deco), rebuilt from the game's sprites.</summary>
        public static void BuildWindowFrame(RectTransform parent)
        {
            Sprite shadow = GameSprite("SofterBoxyCircle");
            if (shadow != null) AddLayer(parent, "Shadow", shadow, new Color32(0, 0, 0, 0xB7), 40f, 1f);
            AddLayer(parent, "Background", null, new Color32(0x07, 0x13, 0x1D, 0xF8), 0f, 1f);
            Sprite big = GameSprite("General_Container_Border");
            if (big != null) AddLayer(parent, "Big Border", big, new Color32(0x1B, 0x32, 0x43, 0xFF), 0f, 1f);
            Sprite sharp = GameSprite("Tooltip_Border");
            if (sharp != null) AddLayer(parent, "Sharp Border", sharp, new Color32(0x62, 0x6C, 0x87, 0x4F), -1f, 1f);
            Sprite deco = GameSprite("texTopHalf");
            if (deco != null) AddLayer(parent, "Deco", deco, new Color32(0x7B, 0x9C, 0xD7, 0x92), 3f, 3.36f);
        }

        /// <summary>Small dark tag in the style of the game's key hints ("Passer [Space]").</summary>
        public static Image KeyHintBackground(GameObject go)
        {
            Image bg = go.AddComponent<Image>();
            Sprite sp = GameSprite("UISprite");
            bg.sprite = sp;
            bg.type = sp != null ? Image.Type.Sliced : Image.Type.Simple;
            bg.color = new Color32(0x24, 0x2F, 0x3D, 0xF5);
            bg.raycastTarget = false;
            Outline o = go.AddComponent<Outline>();
            o.effectColor = new Color32(0, 0, 0, 0x80);
            o.effectDistance = new Vector2(2f, -2f);
            return bg;
        }

        // ------------------------------------------------------------------ debug dump

        public static string DumpHierarchy(Transform t)
        {
            StringBuilder sb = new StringBuilder();
            if (t == null)
            {
                sb.AppendLine("(null)");
                return sb.ToString();
            }
            Dump(sb, t, 0);
            return sb.ToString();
        }

        private static void Dump(StringBuilder sb, Transform t, int depth)
        {
            CultureInfo ci = CultureInfo.InvariantCulture;
            sb.Append(' ', depth * 2).Append(t.name);
            if (!t.gameObject.activeSelf) sb.Append(" [inactive]");
            if (t is RectTransform rt)
            {
                sb.AppendFormat(ci, "  aMin({0:0.##},{1:0.##}) aMax({2:0.##},{3:0.##}) piv({4:0.##},{5:0.##}) pos({6:0.#},{7:0.#}) size({8:0.#},{9:0.#}) rect({10:0.#}x{11:0.#})",
                    rt.anchorMin.x, rt.anchorMin.y, rt.anchorMax.x, rt.anchorMax.y, rt.pivot.x, rt.pivot.y,
                    rt.anchoredPosition.x, rt.anchoredPosition.y, rt.sizeDelta.x, rt.sizeDelta.y, rt.rect.width, rt.rect.height);
            }
            sb.AppendLine();
            foreach (Component c in t.GetComponents<Component>())
            {
                if (c == null || c is Transform || c is CanvasRenderer) continue;
                sb.Append(' ', depth * 2 + 4).Append("- ").Append(c.GetType().Name);
                switch (c)
                {
                    case Image img:
                        sb.AppendFormat(ci, " sprite={0} type={1} color={2} ppu={3:0.##} mat={4} enabled={5}",
                            img.sprite != null ? img.sprite.name : "null", img.type, ColorUtility.ToHtmlStringRGBA(img.color),
                            img.pixelsPerUnitMultiplier, img.material != null ? img.material.name : "null", img.enabled);
                        break;
                    case TMP_Text txt:
                        string s = txt.text ?? "";
                        if (s.Length > 60) s = s.Substring(0, 60);
                        sb.AppendFormat(ci, " font={0} size={1:0.#} color={2} align={3} style={4} text=\"{5}\"",
                            txt.font != null ? txt.font.name : "null", txt.fontSize, ColorUtility.ToHtmlStringRGBA(txt.color),
                            txt.alignment, txt.fontStyle, s.Replace("\n", "\\n"));
                        break;
                    case HorizontalOrVerticalLayoutGroup lg:
                        sb.AppendFormat(ci, " spacing={0} pad=({1},{2},{3},{4}) ctrlW={5} ctrlH={6} expW={7} expH={8} align={9}",
                            lg.spacing, lg.padding.left, lg.padding.right, lg.padding.top, lg.padding.bottom,
                            lg.childControlWidth, lg.childControlHeight, lg.childForceExpandWidth, lg.childForceExpandHeight, lg.childAlignment);
                        break;
                    case ContentSizeFitter csf:
                        sb.AppendFormat(" h={0} v={1}", csf.horizontalFit, csf.verticalFit);
                        break;
                    case LayoutElement le:
                        sb.AppendFormat(ci, " ignore={0} pref=({1},{2}) min=({3},{4})", le.ignoreLayout, le.preferredWidth, le.preferredHeight, le.minWidth, le.minHeight);
                        break;
                    case CanvasGroup cg:
                        sb.AppendFormat(ci, " alpha={0:0.##}", cg.alpha);
                        break;
                    case Shadow sh:
                        sb.AppendFormat(ci, " color={0} dist={1}", ColorUtility.ToHtmlStringRGBA(sh.effectColor), sh.effectDistance);
                        break;
                }
                sb.AppendLine();
            }
            for (int i = 0; i < t.childCount; i++) Dump(sb, t.GetChild(i), depth + 1);
        }
    }
}
