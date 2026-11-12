using System;
using RR.Game.Input;
using RR.Game.Perk;
using RR.UI.Components;

namespace BlackveilDpsMeter
{
    /// <summary>D'où vient un coup. L'hôte connaît la compétence ; un client ne voit que le type de dégâts.</summary>
    internal enum Source
    {
        Attack,
        Power,
        Defensive,
        Special,
        Ultimate,
        Dash,
        Direct,     // client : coup direct dont la compétence est inconnue
        Minions,
        Perks,
        Burn,
        Poison,
        Bleed,
        Curse,
        Shock,
        Root,
        Bless,
        Frost,
        Other,
        Count
    }

    internal static class Sources
    {
        public static Source FromHost(StatusEffect type, bool hasContext, UserAction action, bool minion)
        {
            Source effect = FromEffect(type);
            if (effect != Source.Direct) return effect;
            if (!hasContext) return Source.Other;
            if (minion) return Source.Minions;
            switch (action)
            {
                case UserAction.Attack: return Source.Attack;
                case UserAction.Offensive: return Source.Power;
                case UserAction.Defensive: return Source.Defensive;
                case UserAction.Special: return Source.Special;
                case UserAction.Ultimate: return Source.Ultimate;
                case UserAction.Dash: return Source.Dash;
                default: return Source.Perks; // dégâts d'un champion sans action : effets de perks
            }
        }

        public static Source FromClient(StatusEffect type) => FromEffect(type);

        /// <summary>Type d'altération ; Direct pour un coup « normal ».</summary>
        private static Source FromEffect(StatusEffect type)
        {
            switch (type)
            {
                case StatusEffect.Burn:
                case StatusEffect.BurnDoubleExplosion:
                case StatusEffect.ChillOrBurn:
                case StatusEffect.BurnOrHealFlat:
                    return Source.Burn;
                case StatusEffect.Poison:
                case StatusEffect.ChillOrPoison:
                    return Source.Poison;
                case StatusEffect.Bleed:
                case StatusEffect.DamageWithBleed:
                    return Source.Bleed;
                case StatusEffect.Curse:
                    return Source.Curse;
                case StatusEffect.Shock:
                case StatusEffect.ChainShock:
                    return Source.Shock;
                case StatusEffect.Root:
                    return Source.Root;
                case StatusEffect.Bless:
                    return Source.Bless;
                case StatusEffect.Chill:
                case StatusEffect.Freeze:
                    return Source.Frost;
                case StatusEffect.None:
                case StatusEffect.BasicDamage:
                case StatusEffect.PunchWithDelay:
                case StatusEffect.CriticalHitWithDelay:
                case StatusEffect.BlessedAttackDelay:
                case StatusEffect.FuryAttackDelay:
                case StatusEffect.AbilityHitWithDelay:
                case StatusEffect.Fury:
                    return Source.Direct;
                default:
                    return Source.Other;
            }
        }

        public static bool IsHealing(StatusEffect t) => t.IsHealingEffect();

        public static bool IsAbility(Source s) => s <= Source.Dash;

        /// <summary>Nom affiché : clés de traduction du jeu quand elles existent (l'écran de fin les utilise).</summary>
        public static string Name(Source s)
        {
            switch (s)
            {
                case Source.Attack: return Loc("Champions.ability-attack-name", "Attaque");
                case Source.Power: return Loc("Champions.ability-offense-name", "Puissance");
                case Source.Defensive: return Loc("Champions.ability-defense-name", "Défense");
                case Source.Special: return Loc("Champions.ability-special-name", "Spéciale");
                case Source.Ultimate: return Loc("Champions.ability-ultimate-name", "Ultime");
                case Source.Dash: return "Dash";
                case Source.Direct: return "Coups directs";
                case Source.Minions: return Loc("GameEnd.combat-dmg-minions", "Sbires");
                case Source.Perks: return Loc("GameEnd.combat-dmg-perks", "Perks");
                case Source.Burn: return Loc("GameEnd.combat-dmg-burn", "Brûlure");
                case Source.Poison: return Loc("GameEnd.combat-dmg-poison", "Poison");
                case Source.Bleed: return Loc("GameEnd.combat-dmg-bleed", "Saignement");
                case Source.Curse: return Loc("GameEnd.combat-dmg-curse", "Malédiction");
                case Source.Shock: return Loc("GameEnd.combat-dmg-shock", "Électrocution");
                case Source.Root: return Loc("GameEnd.combat-dmg-root", "Racines");
                case Source.Bless: return "Bénédiction";
                case Source.Frost: return "Givre";
                default: return "Autres";
            }
        }

        /// <summary>Nom de l'icône d'altération du jeu (affichée sur les barres de vie).</summary>
        public static string EffectIconElement(Source s)
        {
            switch (s)
            {
                case Source.Burn: return "BurnIcon";
                case Source.Poison: return "PoisonIcon";
                case Source.Bleed: return "BleedIcon";
                case Source.Curse: return "CurseIcon";
                case Source.Shock: return "ShockIcon";
                case Source.Root: return "RootIcon";
                case Source.Frost: return "ChilledIcon";
                default: return null;
            }
        }

        /// <summary>Élément de la barre de compétences du HUD.</summary>
        public static string AbilityIconElement(Source s)
        {
            switch (s)
            {
                case Source.Attack: return "AbilityPrimary";
                case Source.Power: return "AbilityPower";
                case Source.Defensive: return "AbilityDefensive";
                case Source.Special: return "AbilitySpecial";
                case Source.Ultimate: return "AbilityUltimate";
                default: return null;
            }
        }

        private static string Loc(string key, string fallback)
        {
            try
            {
                string s = LocString.Get(key);
                if (!string.IsNullOrEmpty(s) && s != key && !s.StartsWith("[", StringComparison.Ordinal)) return s;
            }
            catch (Exception)
            {
                // pas de traduction : texte par défaut
            }
            return fallback;
        }
    }
}
