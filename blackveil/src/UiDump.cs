using System;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace BlackveilDpsMeter
{
    /// <summary>Outil de développement : écrit l'arbre UI Toolkit d'une page dans un fichier texte.</summary>
    internal static class UiDump
    {
        public static string Dump(VisualElement root, string title)
        {
            var sb = new StringBuilder();
            sb.AppendLine("==== " + title + " @ " + DateTime.Now.ToString("HH:mm:ss") + " screen " + Screen.width + "x" + Screen.height);
            if (root?.panel != null)
            {
                VisualElement top = root.panel.visualTree;
                sb.AppendLine("panel root: " + top.layout);
            }
            Walk(root, 0, sb);
            return sb.ToString();
        }

        private static void Walk(VisualElement e, int depth, StringBuilder sb)
        {
            if (e == null || depth > 60) return;
            sb.Append(' ', depth * 2);
            sb.Append(e.GetType().Name);
            if (!string.IsNullOrEmpty(e.name)) sb.Append(" #").Append(e.name);
            foreach (string c in e.GetClasses()) sb.Append(" .").Append(c);
            Rect wb = e.worldBound;
            sb.Append("  [").Append((int)wb.x).Append(',').Append((int)wb.y).Append(' ').Append((int)wb.width).Append('x').Append((int)wb.height).Append(']');
            IResolvedStyle rs = e.resolvedStyle;
            if (rs.display == DisplayStyle.None) sb.Append(" HIDDEN");
            if (rs.visibility == Visibility.Hidden) sb.Append(" INVISIBLE");
            if (rs.opacity < 0.99f) sb.Append(" op=").Append(rs.opacity.ToString("0.00"));
            if (rs.backgroundColor.a > 0.01f) sb.Append(" bg=").Append(ColorUtility.ToHtmlStringRGBA(rs.backgroundColor));
            Background bi = rs.backgroundImage;
            if (bi.texture != null) sb.Append(" img=").Append(bi.texture.name);
            if (bi.sprite != null) sb.Append(" sprite=").Append(bi.sprite.name);
            if (bi.vectorImage != null) sb.Append(" vec=").Append(bi.vectorImage.name);
            if (rs.borderTopWidth > 0 || rs.borderLeftWidth > 0) sb.Append(" border=").Append(rs.borderTopWidth).Append('/').Append(ColorUtility.ToHtmlStringRGBA(rs.borderTopColor));
            if (e is TextElement t)
            {
                sb.Append(" font=").Append(rs.fontSize).Append(' ').Append(ColorUtility.ToHtmlStringRGBA(rs.color));
                var fd = rs.unityFontDefinition;
                if (fd.fontAsset != null) sb.Append(' ').Append(fd.fontAsset.name);
                string text = t.text ?? "";
                if (text.Length > 80) text = text.Substring(0, 80);
                sb.Append(" \"").Append(text.Replace("\n", "\\n")).Append('"');
            }
            if (e.styleSheets.count > 0)
            {
                sb.Append(" sheets:");
                for (int i = 0; i < e.styleSheets.count; i++) sb.Append(' ').Append(e.styleSheets[i]?.name);
            }
            sb.AppendLine();
            for (int i = 0; i < e.hierarchy.childCount; i++) Walk(e.hierarchy[i], depth + 1, sb);
        }

        private static readonly System.Text.RegularExpressions.Regex FrameLike =
            new System.Text.RegularExpressions.Regex("Frame|BG|Panel|Box|Divider|Plate|Banner|Title|Stats_Icon|Dash_Icon|Shortcuts",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        /// <summary>Exporte en PNG les textures « cadre » de l'interface, pour choisir l'habillage du panneau.</summary>
        public static void ExportTextures(VisualElement root)
        {
            string dir = Path.Combine(BepInEx.Paths.BepInExRootPath, "tex");
            Directory.CreateDirectory(dir);
            var seen = new System.Collections.Generic.HashSet<string>();
            var sb = new StringBuilder();
            ExportWalk(root, dir, seen, sb, 0);
            File.AppendAllText(Path.Combine(dir, "_slices.txt"), sb.ToString());
        }

        private static void ExportWalk(VisualElement e, string dir, System.Collections.Generic.HashSet<string> seen, StringBuilder sb, int depth)
        {
            if (e == null || depth > 80) return;
            IResolvedStyle rs = e.resolvedStyle;
            Texture2D tex = rs.backgroundImage.texture;
            if (tex != null && FrameLike.IsMatch(tex.name) && seen.Add(tex.name))
            {
                string path = Path.Combine(dir, tex.name + ".png");
                sb.AppendLine(tex.name + " " + tex.width + "x" + tex.height + " slice=" + rs.unitySliceLeft + "," + rs.unitySliceTop + "," + rs.unitySliceRight + "," + rs.unitySliceBottom + " scale=" + rs.unitySliceScale + " on #" + e.name + " ." + string.Join(".", e.GetClasses()));
                if (!File.Exists(path))
                {
                    try
                    {
                        RenderTexture rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                        Graphics.Blit(tex, rt);
                        RenderTexture prev = RenderTexture.active;
                        RenderTexture.active = rt;
                        var copy = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false);
                        copy.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
                        copy.Apply();
                        RenderTexture.active = prev;
                        RenderTexture.ReleaseTemporary(rt);
                        File.WriteAllBytes(path, copy.EncodeToPNG());
                        UnityEngine.Object.Destroy(copy);
                    }
                    catch (Exception ex)
                    {
                        sb.AppendLine("  export failed: " + ex.Message);
                    }
                }
            }
            for (int i = 0; i < e.hierarchy.childCount; i++) ExportWalk(e.hierarchy[i], dir, seen, sb, depth + 1);
        }

        public static void WriteFile(string text)
        {
            try
            {
                string path = Path.Combine(BepInEx.Paths.BepInExRootPath, "blackveil_ui_dump.txt");
                File.AppendAllText(path, text + "\n");
                Plugin.Log.LogInfo("UI dump -> " + path);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("UI dump failed: " + e.Message);
            }
        }
    }
}
