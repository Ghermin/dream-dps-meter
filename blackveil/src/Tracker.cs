using System;
using System.Collections.Generic;
using RR;
using RR.Game;
using RR.Game.Character;
using RR.Game.Enemies;
using RR.Game.Perk;
using RR.Game.Stats;
using UnityEngine;

namespace BlackveilDpsMeter
{
    internal sealed class PlayerTally
    {
        public int ActorId;
        public string Name = "?";
        public bool IsLocal;
        public float Damage;          // toute la run (ou la session de lobby)
        public float CombatDamage;    // combat en cours / dernier combat
        public float BossDamage;
        public float Heal;
        public float Taken;
        public float CombatDamageSum; // somme des dégâts faits pendant des combats terminés
        public readonly float[] BySource = new float[(int)Source.Count];
        public readonly float[] BossBySource = new float[(int)Source.Count];
        public bool SawAbilities;     // l'hôte a vu des compétences : la répartition est précise
    }

    /// <summary>
    /// Compte les dégâts de la run. Un combat commence au premier coup d'un joueur sur un ennemi et finit
    /// après <see cref="CombatGap"/> secondes sans dégâts. Le DPS d'un combat = dégâts / durée du combat.
    /// </summary>
    internal static class Tracker
    {
        public const float CombatGap = 5f;
        private const float MinCombatForBest = 4f;

        public static readonly Dictionary<int, PlayerTally> Players = new Dictionary<int, PlayerTally>();

        // combat en cours
        public static bool InCombat { get; private set; }
        public static bool BossCombat { get; private set; }
        private static float _combatStart, _lastHit;
        public static float LastCombatDuration { get; private set; }
        public static bool LastCombatWasBoss { get; private set; }

        // run (joueur local)
        public static float CombatTimeSum { get; private set; }
        public static float BossTimeSum { get; private set; }
        public static float BestCombatDps { get; private set; }
        public static int Combats { get; private set; }
        public static float RunStart { get; private set; }
        public static bool InRun { get; set; }

        /// <summary>Incrémenté à chaque changement, pour que l'interface ne se redessine que si besoin.</summary>
        public static int Version { get; private set; }

        public static void Reset(bool inRun)
        {
            Players.Clear();
            InCombat = BossCombat = false;
            LastCombatDuration = 0f;
            LastCombatWasBoss = false;
            CombatTimeSum = BossTimeSum = BestCombatDps = 0f;
            Combats = 0;
            RunStart = Time.time;
            InRun = inRun;
            Version++;
        }

        public static void Ingest(StatsManager victim, float value, StatusEffect type, int attackerId, bool critical, Source source)
        {
            if (value <= 0f || victim == null) return;
            if (Sources.IsHealing(type))
            {
                StatsManager healer = StatsManager.GetStatsByActorID(attackerId);
                if (healer != null && healer.OwnerStats != null && healer.OwnerStats.IsChampion)
                {
                    Get(healer.OwnerStats).Heal += value;
                    Version++;
                }
                return;
            }
            if (victim.IsChampion)
            {
                if (attackerId != victim.ActorID)
                {
                    Get(victim).Taken += value;
                    Version++;
                }
                return;
            }
            if (!victim.IsEnemy) return; // caisses, pièges, objets
            StatsManager attacker = StatsManager.GetStatsByActorID(attackerId);
            if (attacker == null) return;
            StatsManager owner = attacker.OwnerStats != null ? attacker.OwnerStats : attacker;
            if (!owner.IsChampion) return; // ennemi contre ennemi, pièges
            if (owner != attacker && source == Source.Direct) source = Source.Minions;

            float now = Time.time;
            bool boss = victim.Enemy is NetworkEnemyBoss;
            if (!InCombat)
            {
                InCombat = true;
                BossCombat = false;
                _combatStart = now;
                foreach (PlayerTally p in Players.Values) p.CombatDamage = 0f;
            }
            if (boss) BossCombat = true;
            _lastHit = now;

            PlayerTally t = Get(owner);
            t.Damage += value;
            t.CombatDamage += value;
            t.BySource[(int)source] += value;
            if (Sources.IsAbility(source)) t.SawAbilities = true;
            if (boss)
            {
                t.BossDamage += value;
                t.BossBySource[(int)source] += value;
            }
            Version++;
        }

        /// <summary>Appelé à chaque frame : ferme le combat après quelques secondes sans dégâts.</summary>
        public static void Tick()
        {
            if (InCombat && Time.time - _lastHit >= CombatGap) CloseCombat();
        }

        /// <summary>
        /// Termine le combat en cours (durée = du premier au dernier coup). Aussi appelé par l'écran de fin :
        /// le jeu y arrête le temps, le combat de la mort ne se fermerait jamais tout seul.
        /// </summary>
        public static void CloseCombat()
        {
            if (!InCombat) return;
            InCombat = false;
            float duration = Mathf.Max(1f, _lastHit - _combatStart);
            LastCombatDuration = duration;
            LastCombatWasBoss = BossCombat;
            CombatTimeSum += duration;
            if (BossCombat) BossTimeSum += duration;
            Combats++;
            foreach (PlayerTally p in Players.Values)
            {
                p.CombatDamageSum += p.CombatDamage;
                if (p.IsLocal && duration >= MinCombatForBest) BestCombatDps = Mathf.Max(BestCombatDps, p.CombatDamage / duration);
            }
            Version++;
        }

        /// <summary>Durée du combat en cours (ou du dernier).</summary>
        public static float CombatDuration => InCombat ? Mathf.Max(1f, Time.time - _combatStart) : LastCombatDuration;

        public static float CombatDps(PlayerTally p)
        {
            float d = CombatDuration;
            return d > 0f ? p.CombatDamage / d : 0f;
        }

        /// <summary>DPS moyen en combat sur la run (combats terminés + combat en cours).</summary>
        public static float AverageCombatDps(PlayerTally p)
        {
            float time = CombatTimeSum + (InCombat ? CombatDuration : 0f);
            float dmg = p.CombatDamageSum + (InCombat ? p.CombatDamage : 0f);
            return time > 0f ? dmg / time : 0f;
        }

        public static float BossDps(PlayerTally p)
        {
            float time = BossTimeSum + (InCombat && BossCombat ? CombatDuration : 0f);
            return time > 0f ? p.BossDamage / time : 0f;
        }

        public static float BestDps(PlayerTally p)
        {
            float best = BestCombatDps;
            if (InCombat && CombatDuration >= MinCombatForBest) best = Mathf.Max(best, CombatDps(p));
            return best;
        }

        public static PlayerTally Local
        {
            get
            {
                foreach (PlayerTally p in Players.Values)
                {
                    if (p.IsLocal) return p;
                }
                return null;
            }
        }

        public static float TotalDamage
        {
            get
            {
                float sum = 0f;
                foreach (PlayerTally p in Players.Values) sum += p.Damage;
                return sum;
            }
        }

        private static PlayerTally Get(StatsManager champion)
        {
            if (!Players.TryGetValue(champion.ActorID, out PlayerTally t))
            {
                t = new PlayerTally { ActorId = champion.ActorID };
                Players[champion.ActorID] = t;
                Identify(t, champion);
            }
            return t;
        }

        private static void Identify(PlayerTally t, StatsManager champion)
        {
            try
            {
                Player player = champion.Player;
                if (player != null)
                {
                    t.Name = string.IsNullOrEmpty(player.UserName) ? champion.name.Replace("(Clone)", "") : player.UserName;
                    t.IsLocal = player.IsLocal;
                    return;
                }
                t.Name = champion.name.Replace("(Clone)", "");
                NetworkChampionBase local = PlayerManager.Instance != null ? PlayerManager.Instance.LocalChampion : null;
                t.IsLocal = local != null && local.ActorID == champion.ActorID;
            }
            catch (Exception)
            {
                // identité inconnue : on garde le nom par défaut
            }
        }

        /// <summary>Outils de dév : remplit le compteur avec des dégâts fictifs pour le champion local.</summary>
        public static void Mock(StatsManager local)
        {
            if (local == null) return;
            PlayerTally t = Get(local);
            float[] values = { 5200f, 3100f, 900f, 2400f, 6100f, 300f };
            Source[] sources = { Source.Attack, Source.Power, Source.Defensive, Source.Special, Source.Ultimate, Source.Dash };
            for (int i = 0; i < values.Length; i++) t.BySource[(int)sources[i]] += values[i];
            t.BySource[(int)Source.Burn] += 1800f;
            t.BySource[(int)Source.Poison] += 750f;
            t.BySource[(int)Source.Perks] += 1200f;
            // toutes les autres sources aussi : la liste dépasse la place disponible et doit défiler
            t.BySource[(int)Source.Minions] += 1100f;
            t.BySource[(int)Source.Bleed] += 650f;
            t.BySource[(int)Source.Curse] += 600f;
            t.BySource[(int)Source.Shock] += 520f;
            t.BySource[(int)Source.Frost] += 410f;
            t.BySource[(int)Source.Root] += 280f;
            t.BySource[(int)Source.Bless] += 190f;
            t.BySource[(int)Source.Other] += 90f;
            t.SawAbilities = true;
            float sum = 0f;
            foreach (float v in t.BySource) sum += v;
            t.Damage += sum;
            t.CombatDamage = sum * 0.1f;
            t.CombatDamageSum += sum;
            t.BossDamage = 4000f;
            t.Heal += 640f;
            t.Taken += 1320f;
            CombatTimeSum += 60f;
            BossTimeSum += 8f;
            BestCombatDps = Mathf.Max(BestCombatDps, 820f);
            LastCombatDuration = 6f;
            Version++;
        }

        /// <summary>Les noms arrivent parfois après les premiers coups (données de lobby) : on les rafraîchit.</summary>
        public static void RefreshIdentities()
        {
            foreach (PlayerTally t in Players.Values)
            {
                StatsManager s = StatsManager.GetStatsByActorID(t.ActorId);
                if (s != null) Identify(t, s);
            }
        }
    }
}
