using System;
using System.Reflection;
using System.Runtime.InteropServices;
using Fusion;
using HarmonyLib;
using RR.Game.Damage;
using RR.Game.Input;
using RR.Game.Perk;
using RR.Game.Stats;
using UnityEngine;

namespace BlackveilDpsMeter
{
    /// <summary>
    /// Récupère chaque coup / soin du jeu :
    /// - sur l'hôte (qui calcule tous les dégâts) : au moment où le jeu écrit l'entrée dans Health (AddDamageData),
    ///   avec le contexte de DecreaseCurrentHealth (compétence utilisée, sbire, perk) ;
    /// - sur un client : en relisant le buffer réseau des 64 derniers coups de chaque Health (celui qui sert
    ///   aux chiffres de dégâts du jeu), juste après que le jeu l'a lu dans Render().
    /// </summary>
    internal static class DamageFeed
    {
        // ---- contexte de l'hôte, posé pendant DecreaseCurrentHealth
        [ThreadStatic] private static bool _hasContext;
        [ThreadStatic] private static UserAction _ctxAction;
        [ThreadStatic] private static bool _ctxMinion;

        // ---- lecture du buffer réseau (offsets lus une fois dans les attributs générés par Fusion)
        private static bool _bufferReady;
        private static bool _bufferBroken;
        private static int _arrayWord, _counterWord, _entrySize, _offValue, _offType, _offAttacker, _offControl;
        private static AccessTools.FieldRef<Health, int> _lastVisualized;
        private static AccessTools.FieldRef<Health, StatsManager> _healthStats;

        public static void Init(Harmony harmony)
        {
            _healthStats = AccessTools.FieldRefAccess<Health, StatsManager>("_stats");
            try
            {
                _lastVisualized = AccessTools.FieldRefAccess<Health, int>("_lastVisualizedDamageData");
                _arrayWord = WordOffset("ReceivedDamageDataArray");
                _counterWord = WordOffset("NetworkedReceivedDamageDataCounter");
                Type entry = AccessTools.Inner(typeof(Health), "NetworkedDamageData");
                _entrySize = Marshal.SizeOf(entry);
                _offControl = (int)Marshal.OffsetOf(entry, "Control");
                _offValue = (int)Marshal.OffsetOf(entry, "value");
                _offType = (int)Marshal.OffsetOf(entry, "damageStatusType");
                _offAttacker = (int)Marshal.OffsetOf(entry, "attackerID");
                _bufferReady = _arrayWord >= 0 && _counterWord >= 0 && _entrySize >= 16 && _entrySize % 4 == 0
                    && _offValue % 4 == 0 && _offType % 4 == 0 && _offAttacker % 4 == 0;
                Plugin.Log.LogInfo($"Buffer de dégâts : tableau@{_arrayWord} compteur@{_counterWord} entrée {_entrySize} o");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Lecture du buffer de dégâts indisponible (les dégâts des autres joueurs ne seront vus que sur l'hôte) : " + e.Message);
            }

            harmony.Patch(AccessTools.Method(typeof(Health), "DecreaseCurrentHealth"),
                prefix: new HarmonyMethod(typeof(DamageFeed), nameof(DecreasePrefix)),
                finalizer: new HarmonyMethod(typeof(DamageFeed), nameof(DecreaseFinalizer)));
            harmony.Patch(AccessTools.Method(typeof(Health), nameof(Health.AddDamageData)),
                postfix: new HarmonyMethod(typeof(DamageFeed), nameof(AddDamageDataPostfix)));
            if (_bufferReady)
            {
                harmony.Patch(AccessTools.Method(typeof(Health), nameof(Health.Render)),
                    prefix: new HarmonyMethod(typeof(DamageFeed), nameof(RenderPrefix)),
                    postfix: new HarmonyMethod(typeof(DamageFeed), nameof(RenderPostfix)));
            }
        }

        private static int WordOffset(string property)
        {
            PropertyInfo p = AccessTools.Property(typeof(Health), property);
            NetworkedWeavedAttribute a = p?.GetCustomAttribute<NetworkedWeavedAttribute>();
            return a?.WordOffset ?? -1;
        }

        // ------------------------------------------------------------------ hôte

        private static void DecreasePrefix(StatsManager attacker, DamageDescription damageDescription)
        {
            _hasContext = true;
            _ctxAction = damageDescription != null ? damageDescription.attackActionHint : UserAction.None;
            _ctxMinion = attacker != null && attacker.IsChampionMinion && attacker.OwnerStats != attacker;
        }

        private static Exception DecreaseFinalizer(Exception __exception)
        {
            _hasContext = false;
            return __exception;
        }

        private static void AddDamageDataPostfix(Health __instance, ref float damageValue, ref StatusEffect damageType, ref int attackerID, ref bool critical)
        {
            try
            {
                Source source = Sources.FromHost(damageType, _hasContext, _ctxAction, _ctxMinion);
                Tracker.Ingest(Victim(__instance), damageValue, damageType, attackerID, critical, source);
            }
            catch (Exception e)
            {
                Plugin.LogOnce("feed-host", e);
            }
        }

        // ------------------------------------------------------------------ client

        private static void RenderPrefix(Health __instance, out int __state)
        {
            __state = -1;
            if (_bufferBroken) return;
            try
            {
                NetworkObject obj = __instance.Object;
                if (obj == null || obj.HasStateAuthority) return; // l'hôte passe par AddDamageData
                __state = _lastVisualized(__instance);
            }
            catch (Exception e)
            {
                Plugin.LogOnce("feed-client-pre", e);
            }
        }

        private static void RenderPostfix(Health __instance, int __state)
        {
            if (__state < 0 || _bufferBroken) return;
            try
            {
                NetworkBehaviourBuffer buf = __instance.StateBuffer;
                if (!buf.Valid) return;
                int counter = buf[_counterWord];
                int from = Math.Max(__state, counter - 64);
                if (from >= counter) return;
                StatsManager victim = Victim(__instance);
                int stride = _entrySize / 4;
                for (int i = from; i < counter; i++)
                {
                    int w = _arrayWord + (i % 64) * stride;
                    float value = BitConverter.Int32BitsToSingle(buf[w + _offValue / 4]);
                    var type = (StatusEffect)buf[w + _offType / 4];
                    int attacker = buf[w + _offAttacker / 4];
                    bool crit = ((buf[w + _offControl / 4] >> (8 * (_offControl % 4))) & 1) != 0;
                    Tracker.Ingest(victim, value, type, attacker, crit, Sources.FromClient(type));
                }
            }
            catch (Exception e)
            {
                _bufferBroken = true;
                Plugin.LogOnce("feed-client", e);
            }
        }

        private static StatsManager Victim(Health h)
        {
            StatsManager s = _healthStats(h);
            return s != null ? s : h.GetComponent<StatsManager>();
        }
    }
}
