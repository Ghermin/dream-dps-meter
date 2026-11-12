using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace BlackveilDpsMeter
{
    /// <summary>
    /// Textures du jeu, reprises telles quelles sur ses propres éléments (avec leur découpe 9-slice) :
    /// le panneau utilise ainsi exactement les cadres, icônes et couleurs de l'interface d'origine.
    /// </summary>
    internal static class Skin
    {
        public static readonly Color Title = new Color32(0xE5, 0xC3, 0x73, 0xFF);   // « ÉTAGE 1 »
        public static readonly Color Text = new Color32(0xF2, 0xEE, 0xDA, 0xFF);    // valeurs
        public static readonly Color Muted = new Color32(0xB2, 0xAD, 0xA0, 0xFF);   // libellés
        public static readonly Color Dim = new Color32(0x99, 0x96, 0x91, 0xFF);     // hors combat
        public static readonly Color Gold = new Color32(0xD9, 0xBF, 0x77, 0xFF);
        public static readonly Color Heal = new Color32(0x85, 0xF2, 0x97, 0xFF);
        public static readonly Color Hurt = new Color32(0xE5, 0x5C, 0x5C, 0xFF);
        public static readonly Color PanelFill = new Color32(0x14, 0x0E, 0x12, 0xE6);
        public static readonly Color PanelBorder = new Color32(0x5C, 0x4D, 0x36, 0xFF);
        public static readonly Color BarBack = new Color32(0x00, 0x00, 0x00, 0x80);

        private static readonly Dictionary<string, VisualElement> Sources = new Dictionary<string, VisualElement>();
        private static IPanel _scannedPanel;
        private static float _scannedAt = -100f;

        /// <summary>Repère une fois les éléments du jeu porteurs d'une image (y compris masqués).</summary>
        public static void Scan(VisualElement anyInPanel, bool force = false)
        {
            IPanel panel = anyInPanel?.panel;
            if (panel == null) return;
            if (!force && panel == _scannedPanel && Time.realtimeSinceStartup - _scannedAt < 5f) return;
            _scannedPanel = panel;
            _scannedAt = Time.realtimeSinceStartup;
            Sources.Clear();
            Walk(panel.visualTree, 0);
        }

        private static void Walk(VisualElement e, int depth)
        {
            if (e == null || depth > 80) return;
            if (e.name != null && e.name.StartsWith(HudPanel.RootName, System.StringComparison.Ordinal)) return;
            Background bg = e.resolvedStyle.backgroundImage;
            string name = bg.texture != null ? bg.texture.name : bg.sprite != null ? bg.sprite.name : null;
            if (name != null && !Sources.ContainsKey(name)) Sources[name] = e;
            for (int i = 0; i < e.hierarchy.childCount; i++) Walk(e.hierarchy[i], depth + 1);
        }

        public static bool Has(string texture) => texture != null && Sources.ContainsKey(texture);

        /// <summary>Applique l'image du jeu (et sa découpe) à un élément du mod. Faux si elle n'est pas chargée.</summary>
        public static bool Apply(VisualElement target, string texture)
        {
            if (texture == null || !Sources.TryGetValue(texture, out VisualElement src) || src == null) return false;
            CopyBackground(src, target);
            return true;
        }

        public static void CopyBackground(VisualElement src, VisualElement target)
        {
            IResolvedStyle rs = src.resolvedStyle;
            target.style.backgroundImage = new StyleBackground(rs.backgroundImage);
            target.style.unitySliceLeft = rs.unitySliceLeft;
            target.style.unitySliceTop = rs.unitySliceTop;
            target.style.unitySliceRight = rs.unitySliceRight;
            target.style.unitySliceBottom = rs.unitySliceBottom;
            target.style.unitySliceScale = rs.unitySliceScale;
            target.style.backgroundSize = rs.backgroundSize;
            target.style.backgroundRepeat = rs.backgroundRepeat;
            target.style.backgroundPositionX = rs.backgroundPositionX;
            target.style.backgroundPositionY = rs.backgroundPositionY;
        }

        /// <summary>Icône d'une source : compétence de la barre du HUD, icône d'altération, ou autre image du jeu.</summary>
        public static bool ApplyIcon(VisualElement target, Source source, VisualElement hudRoot)
        {
            string ability = BlackveilDpsMeter.Sources.AbilityIconElement(source);
            if (ability != null && hudRoot != null)
            {
                VisualElement icon = hudRoot.Q(ability)?.Q("IconDefault");
                if (icon != null && icon.resolvedStyle.backgroundImage.texture != null)
                {
                    target.style.backgroundImage = new StyleBackground(icon.resolvedStyle.backgroundImage);
                    target.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
                    return true;
                }
            }
            switch (source)
            {
                case Source.Burn: return Icon(target, "Stats_Icon_Burn");
                case Source.Poison: return Icon(target, "Stats_Icon_Poison");
                case Source.Bleed: return Icon(target, "Stats_Icon_Bleed");
                case Source.Curse: return Icon(target, "Stats_Icon_Curse");
                case Source.Shock: return Icon(target, "Stats_Icon_Shock");
                case Source.Root: return Icon(target, "Stats_Icon_Root");
                case Source.Frost: return Icon(target, "Stats_Icon_Chilled");
                case Source.Dash: return Icon(target, "HPBar_Dash_Icon_Main_Ready");
                case Source.Perks: return Icon(target, "HUD_Shortcuts_PerkWheel2");
                default: return false;
            }
        }

        /// <summary>Le jeu a-t-il une icône pour cette source (sinon : pastille de couleur, inutile de la chercher) ?</summary>
        public static bool MayHaveIcon(Source s) =>
            BlackveilDpsMeter.Sources.AbilityIconElement(s) != null || BlackveilDpsMeter.Sources.EffectIconElement(s) != null
            || s == Source.Dash || s == Source.Perks;

        private static bool Icon(VisualElement target, string texture)
        {
            if (!Sources.TryGetValue(texture, out VisualElement src) || src == null) return false;
            target.style.backgroundImage = new StyleBackground(src.resolvedStyle.backgroundImage);
            target.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
            return true;
        }

        /// <summary>Couleur de barre par source (proche des couleurs d'altération du jeu).</summary>
        public static Color SourceColor(Source s)
        {
            switch (s)
            {
                case Source.Attack: return new Color32(0xE0, 0xD6, 0xBC, 0xFF);
                case Source.Power: return new Color32(0xE5, 0xA8, 0x3B, 0xFF);
                case Source.Defensive: return new Color32(0x8F, 0xB4, 0xD9, 0xFF);
                case Source.Special: return new Color32(0xC9, 0x8B, 0xE0, 0xFF);
                case Source.Ultimate: return new Color32(0xFF, 0xD3, 0x4D, 0xFF);
                case Source.Dash: return new Color32(0xA6, 0xD9, 0xD0, 0xFF);
                case Source.Direct: return new Color32(0xE0, 0xD6, 0xBC, 0xFF);
                case Source.Minions: return new Color32(0xB0, 0x9A, 0x70, 0xFF);
                case Source.Perks: return new Color32(0xD9, 0xBF, 0x77, 0xFF);
                case Source.Burn: return new Color32(0xF2, 0x7A, 0x2E, 0xFF);
                case Source.Poison: return new Color32(0x86, 0xC9, 0x46, 0xFF);
                case Source.Bleed: return new Color32(0xC4, 0x2B, 0x3B, 0xFF);
                case Source.Curse: return new Color32(0x9E, 0x5E, 0xD9, 0xFF);
                case Source.Shock: return new Color32(0x5C, 0xC8, 0xF2, 0xFF);
                case Source.Root: return new Color32(0x9C, 0x7A, 0x48, 0xFF);
                case Source.Bless: return new Color32(0xF5, 0xE2, 0x8A, 0xFF);
                case Source.Frost: return new Color32(0xA3, 0xE0, 0xF5, 0xFF);
                default: return new Color32(0x99, 0x96, 0x91, 0xFF);
            }
        }

        public static string Format(float v)
        {
            try
            {
                var f = RR.UI.UISystem.UIManager.Instance != null ? RR.UI.UISystem.UIManager.Instance.CompactNumberFormatter : null;
                if (f != null) return f.Format(v);
            }
            catch (System.Exception)
            {
                // formateur du jeu indisponible
            }
            if (v >= 1e6f) return (v / 1e6f).ToString("0.##") + "M";
            if (v >= 1e4f) return (v / 1e3f).ToString("0.#") + "k";
            return Mathf.RoundToInt(v).ToString();
        }
    }
}
