using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RR;
using RR.Game;
using RR.UI.Controls;
using RR.UI.Pages;
using RR.UI.Utils;
using UnityEngine;
using UnityEngine.UIElements;

namespace BlackveilDpsMeter
{
    /// <summary>
    /// Écran de fin : ajoute à la section « Combat » du récapitulatif du jeu, avec ses propres styles de lignes,
    /// « DPS moyen en combat », « Meilleur DPS sur un combat », « DPS moyen sur les boss » et « Temps en combat »,
    /// pour le joueur dont l'onglet est affiché.
    /// </summary>
    [HarmonyPatch(typeof(GameEndSummaryPage), "SetForPlayer")]
    internal static class EndScreen
    {
        private const string RowName = HudPanel.RootName + "-EndRow";
        private static readonly AccessTools.FieldRef<GameEndSummaryPage, VisualElement> ScrollContent =
            AccessTools.FieldRefAccess<GameEndSummaryPage, VisualElement>("_scrollContent");

        private static void Postfix(GameEndSummaryPage __instance, int index)
        {
            try
            {
                Tracker.CloseCombat();
                VisualElement content = ScrollContent(__instance);
                if (content == null) return;
                Player player = OrderedPlayers().ElementAtOrDefault(Mathf.Max(0, index));
                if (player == null || player.PlayableChampion == null) return;
                if (!Tracker.Players.TryGetValue(player.PlayableChampion.ActorID, out PlayerTally t) || t.Damage <= 0f) return;

                var rows = new List<(string, string)>
                {
                    ("DPS moyen en combat", Value(Tracker.AverageCombatDps(t))),
                };
                if (t.IsLocal && Tracker.BestDps(t) > 0f) rows.Add(("Meilleur DPS sur un combat", Value(Tracker.BestDps(t))));
                if (t.BossDamage > 0f) rows.Add(("DPS moyen sur les boss", Value(Tracker.BossDps(t))));
                rows.Add(("Temps en combat", UIFormat.TimeHMS(Mathf.RoundToInt(Tracker.CombatTimeSum))));

                int insertAt = InsertionIndex(content);
                foreach ((string label, string value) in rows)
                {
                    content.Insert(insertAt++, MakeRow(label, value));
                }
            }
            catch (Exception e)
            {
                Plugin.LogOnce("end-screen", e);
            }
        }

        /// <summary>Juste avant la section suivant « Combat » (Économie), sinon à la fin.</summary>
        private static int InsertionIndex(VisualElement content)
        {
            bool inCombat = false;
            for (int i = 0; i < content.childCount; i++)
            {
                if (content[i] is LocLabel cat && cat.ClassListContains("rr-player-summary-panel--stat-category"))
                {
                    if (inCombat) return i;
                    inCombat = cat.Key == "GameEnd.combat-label";
                }
            }
            return content.childCount;
        }

        private static VisualElement MakeRow(string label, string value)
        {
            var row = new VisualElement { name = RowName };
            row.AddToClassList("rr-player-summary-panel--stat-row");
            var l = new Label(label);
            l.AddToClassList("rr-player-summary-panel--stat-label");
            row.Add(l);
            var v = new Label(value);
            v.AddToClassList("rr-player-summary-panel--stat-value");
            row.Add(v);
            return row;
        }

        private static string Value(float v) => UIFormat.LocFormatThousandSeparate(Mathf.Round(v));

        /// <summary>Même ordre que les onglets du jeu : le joueur local d'abord.</summary>
        private static IEnumerable<Player> OrderedPlayers()
        {
            Player local = PlayerManager.Instance.LocalPlayer;
            return PlayerManager.Instance.GetPlayers().OrderByDescending(p => p == local);
        }
    }
}
