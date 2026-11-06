using System;
using System.Collections.Generic;
using UnityEngine;

namespace DreamDpsMeter
{
    public enum SourceCategory
    {
        Memory,
        Essence,
        Passive,
        Attack,
        Element,
        Other
    }

    /// <summary>Damage buckets, same split as the game's damage numbers (element first, then damage type).</summary>
    public static class DamageKinds
    {
        public const int Count = 7;

        public static readonly string[] Names = { "Physique", "Magique", "Pur", "Feu", "Froid", "Lumière", "Ténèbres" };

        public static readonly Color[] Colors =
        {
            new Color32(0xF2, 0xC1, 0x4E, 0xFF), // physical (attack numbers)
            new Color32(0x5F, 0xA8, 0xFF, 0xFF), // magic (skill numbers)
            new Color32(0xEE, 0xEE, 0xEE, 0xFF), // pure
            new Color32(0xFF, 0x7A, 0x45, 0xFF), // fire
            new Color32(0x9F, 0xF0, 0xFF, 0xFF), // cold
            new Color32(0xFF, 0xE9, 0x8A, 0xFF), // light
            new Color32(0xC0, 0x8C, 0xFF, 0xFF) // dark
        };

        public static int Of(in FinalDamageData d)
        {
            if (d.elemental.HasValue)
            {
                switch (d.elemental.Value)
                {
                    case ElementalType.Fire: return 3;
                    case ElementalType.Cold: return 4;
                    case ElementalType.Light: return 5;
                    case ElementalType.Dark: return 6;
                }
            }
            if ((d.type & DamageData.SourceType.Physical) != 0) return 0;
            if ((d.type & DamageData.SourceType.Magic) != 0) return 1;
            return 2;
        }

        public static int Dominant(double[] values)
        {
            int best = 0;
            for (int i = 1; i < values.Length; i++)
            {
                if (values[i] > values[best]) best = i;
            }
            return best;
        }
    }

    public class SourceStats
    {
        public readonly double[] runByKind = new double[DamageKinds.Count];
        public string key;
        public string label;
        public SourceCategory category;
        public HeroSkillLocation? slot;
        public Sprite icon;
        /// <summary>Last actor seen for this source (gem / memory instance), used to hide unequipped ones.</summary>
        public Actor actor;
        public double runHeal;
        public double runShield;
        public double fightDamage;
        public double runDamage;
        public int fightHits;
        public int runHits;
    }

    public class PlayerStats
    {
        public DewPlayer player;
        public string name;
        public int colorIndex;
        public bool isLocal;
        public double fightDamage;
        public double runDamage;
        public readonly double[] runByKind = new double[DamageKinds.Count];
        public double runHeal;
        public double runShield;
        public double runTaken;      // damage taken by this player's hero
        public double bestFightDps;  // best DPS over one fight (fights of 4 s or more)
        public long runGold;
        public long runDreamDust;
        public Action<int> onEarnGold;
        public Action<int> onEarnDreamDust;
        public readonly Dictionary<string, SourceStats> sources = new Dictionary<string, SourceStats>();
    }

    /// <summary>One boss encounter: duration from first to last hit, damage per player.</summary>
    public class BossFight
    {
        public string name;
        public Entity boss;
        public float start;
        public float lastHit;
        public bool dead;
        public double total;
        public readonly Dictionary<DewPlayer, double> damage = new Dictionary<DewPlayer, double>();

        /// <summary>First hit -> last hit; while the boss is alive and being hit, up to now.</summary>
        public float Duration(float now) => Mathf.Max((dead ? lastHit : Mathf.Min(now, lastHit + 3f)) - start, 1f);

        public double Of(DewPlayer p) => p != null && damage.TryGetValue(p, out double d) ? d : 0;
    }

    /// <summary>Damage and combat time spent in one zone of the run.</summary>
    public class ZoneRecord
    {
        public int index;
        public string name;
        public double combatTime;
        public double total;
        public readonly Dictionary<DewPlayer, double> damage = new Dictionary<DewPlayer, double>();

        public double Of(DewPlayer p) => p != null && damage.TryGetValue(p, out double d) ? d : 0;
    }

    /// <summary>
    /// Collects damage dealt by every human player's hero (and everything parented to it:
    /// memories, essences, passives, summons) to enemies, using ClientEventManager.OnTakeDamage,
    /// which the host relays to every client. Purely client-side.
    /// </summary>
    public class DamageTracker
    {
        public readonly List<PlayerStats> players = new List<PlayerStats>();
        private readonly Dictionary<DewPlayer, PlayerStats> _byPlayer = new Dictionary<DewPlayer, PlayerStats>();

        public float combatTimeout = 5f;
        public bool includeOverkill;

        public bool inFight { get; private set; }
        private float _fightStart = -1f;
        private float _fightLastHit = -1f;
        private double _completedFightsDuration;

        public bool hasAnyFight => _fightStart >= 0f;

        public readonly List<BossFight> bosses = new List<BossFight>();
        public readonly List<ZoneRecord> zones = new List<ZoneRecord>();
        private ZoneRecord _fightZone;

        public bool frozen { get; private set; }

        /// <summary>
        /// End of the run (final boss down / result screen): close the fight in progress at its last hit and
        /// stop every timer, so the DPS no longer drops while the end screens are shown.
        /// </summary>
        public void Freeze()
        {
            if (frozen) return;
            frozen = true;
            if (inFight)
            {
                inFight = false;
                float duration = FightDuration(_fightLastHit);
                _completedFightsDuration += duration;
                if (_fightZone != null) _fightZone.combatTime += duration;
                if (duration >= 4f)
                {
                    foreach (PlayerStats p in players)
                    {
                        double dps = p.fightDamage / duration;
                        if (dps > p.bestFightDps) p.bestFightDps = dps;
                    }
                }
            }
            foreach (BossFight b in bosses) b.dead = true;
        }

        /// <summary>The boss being fought right now (hit recently and still alive), if any.</summary>
        public BossFight ActiveBoss(float now)
        {
            for (int i = bosses.Count - 1; i >= 0; i--)
            {
                BossFight b = bosses[i];
                if (!b.dead && now - b.lastHit < 15f) return b;
            }
            return null;
        }

        /// <summary>Combat time of a zone, including the fight in progress.</summary>
        public double ZoneCombatTime(ZoneRecord z, float now)
        {
            double t = z.combatTime;
            if (inFight && _fightZone == z) t += FightDuration(now);
            return Math.Max(t, 1.0);
        }

        private ZoneRecord CurrentZone()
        {
            ZoneManager zm = NetworkedManagerBase<ZoneManager>.softInstance;
            if (zm == null) return zones.Count > 0 ? zones[zones.Count - 1] : null;
            int idx = zm.currentZoneIndex;
            if (zones.Count > 0 && zones[zones.Count - 1].index == idx) return zones[zones.Count - 1];
            string name = (idx + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            try
            {
                if (zm.currentZone != null) name += ". " + DewLocalization.GetUIValue(zm.currentZone.name + "_Name");
            }
            catch
            {
                // keep the index only
            }
            ZoneRecord z = new ZoneRecord { index = idx, name = name };
            zones.Add(z);
            return z;
        }

        private BossFight GetBossFight(Monster m, float now)
        {
            for (int i = bosses.Count - 1; i >= 0; i--)
            {
                if (bosses[i].boss == m) return bosses[i];
            }
            string name = "Boss";
            try
            {
                name = DewLocalization.GetUIValue(m.GetType().Name + "_Name");
            }
            catch
            {
                // generic name
            }
            BossFight b = new BossFight { name = name, boss = m, start = now, lastHit = now };
            bosses.Add(b);
            return b;
        }

        private static void Add(Dictionary<DewPlayer, double> d, DewPlayer p, double v)
        {
            d.TryGetValue(p, out double cur);
            d[p] = cur + v;
        }

        public void ResetAll()
        {
            foreach (PlayerStats p in players) Unsubscribe(p);
            players.Clear();
            _byPlayer.Clear();
            bosses.Clear();
            zones.Clear();
            _fightZone = null;
            frozen = false;
            inFight = false;
            _fightStart = -1f;
            _fightLastHit = -1f;
            _completedFightsDuration = 0;
        }

        /// <summary>Duration of the current (or last) fight, never under 1s to avoid spikes.</summary>
        public float FightDuration(float now)
        {
            if (_fightStart < 0f) return 1f;
            float end = inFight ? now : _fightLastHit;
            return Mathf.Max(end - _fightStart, 1f);
        }

        /// <summary>Total time spent in combat during the run (sum of all fights).</summary>
        public double RunCombatDuration(float now)
        {
            double d = _completedFightsDuration;
            if (inFight) d += FightDuration(now);
            return Math.Max(d, 1.0);
        }

        public void Tick(float now)
        {
            if (inFight && now - _fightLastHit > combatTimeout)
            {
                inFight = false;
                float duration = FightDuration(now); // first hit -> last hit (not the idle timeout)
                _completedFightsDuration += duration;
                if (_fightZone != null) _fightZone.combatTime += duration;
                if (duration >= 4f)
                {
                    foreach (PlayerStats p in players)
                    {
                        double dps = p.fightDamage / duration;
                        if (dps > p.bestFightDps) p.bestFightDps = dps;
                    }
                }
            }
            foreach (BossFight b in bosses)
            {
                if (!b.dead && (b.boss == null || b.boss.IsNullInactiveDeadOrKnockedOut())) b.dead = true;
            }
        }

        /// <summary>Keep the list in sync with the players currently in the game.</summary>
        public void SyncPlayers()
        {
            List<DewPlayer> humans = DewPlayer.allHumanPlayers;
            if (humans == null) return;
            for (int i = 0; i < humans.Count; i++)
            {
                DewPlayer p = humans[i];
                if (p == null) continue;
                GetOrCreate(p);
            }
            for (int i = players.Count - 1; i >= 0; i--)
            {
                if (players[i].player == null)
                {
                    players.RemoveAt(i);
                }
            }
        }

        private PlayerStats GetOrCreate(DewPlayer p)
        {
            if (_byPlayer.TryGetValue(p, out PlayerStats ps))
            {
                ps.name = SafeName(p);
                return ps;
            }
            ps = new PlayerStats
            {
                player = p,
                name = SafeName(p),
                colorIndex = _byPlayer.Count,
                isLocal = p == DewPlayer.local
            };
            // Gold / dream dust earned: ClientRpc events, received by every client.
            PlayerStats captured = ps;
            ps.onEarnGold = amount => { if (amount > 0) captured.runGold += amount; };
            ps.onEarnDreamDust = amount => { if (amount > 0) captured.runDreamDust += amount; };
            p.ClientEvent_OnEarnGold += ps.onEarnGold;
            p.ClientEvent_OnEarnDreamDust += ps.onEarnDreamDust;
            _byPlayer[p] = ps;
            players.Add(ps);
            return ps;
        }

        private static void Unsubscribe(PlayerStats ps)
        {
            try
            {
                if (ps.player == null) return;
                if (ps.onEarnGold != null) ps.player.ClientEvent_OnEarnGold -= ps.onEarnGold;
                if (ps.onEarnDreamDust != null) ps.player.ClientEvent_OnEarnDreamDust -= ps.onEarnDreamDust;
            }
            catch
            {
                // player object already gone
            }
        }

        /// <summary>Healing done by a player's hero (and its memories / essences / summons) to itself or allies.</summary>
        public void OnTakeHeal(EventInfoHeal info)
        {
            if (info.actor == null || info.target == null) return;
            if (!TryResolve(info.actor, out Hero hero, out DewPlayer owner, out Actor source)) return;
            if (owner == null || !owner.isHumanPlayer) return;
            if (hero != null && info.target != hero && (hero.GetRelation(info.target) & EntityRelation.Enemy) != 0) return;
            float amount = info.amount;
            if (amount <= 0f || float.IsNaN(amount) || float.IsInfinity(amount)) return;
            PlayerStats ps = GetOrCreate(owner);
            ps.runHeal += amount;
            GetSource(ps, source).runHeal += amount; // shown in the detail, never part of the DPS
        }

        /// <summary>Shields given by a player's memories / essences (to itself or allies). Not part of the DPS.</summary>
        public void OnTakeShield(EventInfoShield info)
        {
            if (info.statusEffect == null || info.target == null) return;
            if (!TryResolve(info.statusEffect, out Hero hero, out DewPlayer owner, out Actor source)) return;
            if (owner == null || !owner.isHumanPlayer) return;
            if (hero != null && info.target != hero && (hero.GetRelation(info.target) & EntityRelation.Enemy) != 0) return;
            float amount = info.finalAmount;
            if (amount <= 0f || float.IsNaN(amount) || float.IsInfinity(amount)) return;
            PlayerStats ps = GetOrCreate(owner);
            ps.runShield += amount;
            GetSource(ps, source).runShield += amount;
        }

        /// <summary>
        /// Test helper: fills the local player's stats with random values on its real memories / essences
        /// (live hero if any, otherwise the result screen data), plus a few bosses and zones.
        /// </summary>
        public void Mock(DewPlayer local, Hero hero, DewGameResult.PlayerData pd)
        {
            if (local == null) return;
            System.Random rnd = new System.Random();
            PlayerStats ps = GetOrCreate(local);
            ps.sources.Clear();
            ps.runDamage = 0;
            for (int k = 0; k < ps.runByKind.Length; k++) ps.runByKind[k] = 0;

            void AddSource(string key, string label, SourceCategory cat, Sprite icon, Actor actor, HeroSkillLocation? slot, double dmg)
            {
                SourceStats src = new SourceStats { key = key, label = label, category = cat, icon = icon, actor = actor, slot = slot };
                src.runDamage = dmg;
                src.fightDamage = dmg * 0.08;
                int k1 = rnd.Next(DamageKinds.Count), k2 = rnd.Next(DamageKinds.Count);
                src.runByKind[k1] += dmg * 0.7;
                src.runByKind[k2] += dmg * 0.3;
                if (rnd.NextDouble() < 0.3) src.runHeal = dmg * 0.05;
                ps.sources[key] = src;
                ps.runDamage += dmg;
                ps.fightDamage += src.fightDamage;
                for (int k = 0; k < DamageKinds.Count; k++) ps.runByKind[k] += src.runByKind[k];
            }

            double Rand(double min, double max) => min + rnd.NextDouble() * (max - min);

            if (hero != null && hero.Skill != null)
            {
                foreach (HeroSkillLocation loc in new[] { HeroSkillLocation.Q, HeroSkillLocation.W, HeroSkillLocation.E, HeroSkillLocation.R, HeroSkillLocation.Identity, HeroSkillLocation.Movement })
                {
                    SkillTrigger st = hero.Skill.GetSkill(loc);
                    if (st == null) continue;
                    AddSource(st.GetType().Name + "#" + st.netId, GetLabel(st), GetCategory(st), GetIcon(st), st, loc, Rand(2000, 150000));
                }
                foreach (Gem g in hero.Skill.gems.Values)
                {
                    if (g == null) continue;
                    AddSource(g.GetType().Name + "#" + g.netId, GetLabel(g), SourceCategory.Essence, GetIcon(g), g, g.location.skill, Rand(500, 40000));
                }
            }
            else if (pd != null)
            {
                foreach (DewGameResult.SkillData sd in pd.skills)
                {
                    Sprite icon = null;
                    try
                    {
                        icon = sd.GetSkillTrigger().configs[0].triggerIcon;
                    }
                    catch
                    {
                        // no icon
                    }
                    AddSource(sd.name + "#" + sd.netId, sd.name, SourceCategory.Memory, icon, null, sd.loc, Rand(2000, 150000));
                }
                foreach (DewGameResult.GemData gd in pd.gems)
                {
                    Sprite icon = null;
                    try
                    {
                        icon = DewResources.GetByShortTypeName<Gem>(gd.name).icon;
                    }
                    catch
                    {
                        // no icon
                    }
                    AddSource(gd.name + "#" + gd.netId, gd.name, SourceCategory.Essence, icon, null, gd.location.skill, Rand(500, 40000));
                }
            }
            AddSource("AttackTrigger", "Attaque", SourceCategory.Attack, null, null, null, Rand(3000, 20000));

            // Lots of essences thrown away during the run (not equipped any more), to test the end screen layout.
            List<Sprite> icons = new List<Sprite>();
            foreach (SourceStats src in ps.sources.Values)
            {
                if (src.icon != null) icons.Add(src.icon);
            }
            for (int i = 0; i < 18 && icons.Count > 0; i++)
            {
                AddSource("MockGem#" + (900000 + i), "Essence " + (i + 1), SourceCategory.Essence, icons[i % icons.Count], null, null, Rand(100, 12000));
            }

            float now = Time.time;
            _fightStart = now - 30f;
            _fightLastHit = now - 20f;
            inFight = false;
            _completedFightsDuration = Rand(400, 900);
            ps.runHeal = ps.runDamage * 0.06;
            ps.runShield = ps.runDamage * 0.02;
            ps.runTaken = ps.runDamage * 0.09;
            ps.runGold += 3000;
            ps.runDreamDust += 1200;
            ps.bestFightDps = ps.runDamage / _completedFightsDuration * 2.3;

            bosses.Clear();
            foreach (string bossName in new[] { "Boss de test I", "Boss de test II", "Boss de test III" })
            {
                float dur = (float)Rand(40, 120);
                BossFight b = new BossFight { name = bossName, start = now - dur, lastHit = now, dead = true };
                double d = ps.runDamage / _completedFightsDuration * Rand(1.2, 2.0) * dur;
                b.damage[local] = d;
                b.total = d;
                bosses.Add(b);
            }
            zones.Clear();
            for (int i = 0; i < 5; i++)
            {
                ZoneRecord z = new ZoneRecord { index = i, name = (i + 1) + ". Zone de test", combatTime = _completedFightsDuration / 5 };
                double d = ps.runDamage / 15.0 * (i + 1);
                z.damage[local] = d;
                z.total = d;
                zones.Add(z);
            }
        }

        private static string SafeName(DewPlayer p)
        {
            try
            {
                string n = p.playerName;
                return string.IsNullOrEmpty(n) ? "Joueur" : n;
            }
            catch
            {
                return "Joueur";
            }
        }

        public void OnTakeDamage(EventInfoDamage info)
        {
            if (info.actor == null || info.victim == null || frozen) return;

            // Damage taken by a player's hero (whoever dealt it).
            if (info.victim is Hero victimHero && victimHero.owner != null && victimHero.owner.isHumanPlayer)
            {
                float taken = info.damage.amount;
                if (taken > 0f && !float.IsNaN(taken) && !float.IsInfinity(taken)) GetOrCreate(victimHero.owner).runTaken += taken;
            }

            if (!TryResolve(info.actor, out Hero hero, out DewPlayer owner, out Actor source)) return;
            if (owner == null || !owner.isHumanPlayer) return;

            // Only count damage against enemies of the attacking hero.
            if (hero != null)
            {
                if (info.victim == hero) return;
                if ((hero.GetRelation(info.victim) & EntityRelation.Enemy) == 0) return;
            }
            else if (!(info.victim is Monster))
            {
                return;
            }

            float amount = info.damage.amount;
            if (includeOverkill) amount += info.damage.discardedAmount;
            if (amount <= 0f || float.IsNaN(amount) || float.IsInfinity(amount)) return;

            float now = Time.time;
            Tick(now);
            if (!inFight)
            {
                // New fight: previous fight values are discarded.
                inFight = true;
                _fightStart = now;
                _fightZone = CurrentZone();
                for (int i = 0; i < players.Count; i++)
                {
                    PlayerStats p = players[i];
                    p.fightDamage = 0;
                    foreach (SourceStats s in p.sources.Values)
                    {
                        s.fightDamage = 0;
                        s.fightHits = 0;
                    }
                }
            }
            _fightLastHit = now;

            PlayerStats ps = GetOrCreate(owner);
            SourceStats ss = GetSource(ps, source);
            ps.fightDamage += amount;
            ps.runDamage += amount;
            ss.fightDamage += amount;
            ss.runDamage += amount;
            ss.fightHits++;
            ss.runHits++;
            int kind = source is ElementalStatusEffect ? ElementKind(source) : DamageKinds.Of(info.damage);
            ss.runByKind[kind] += amount;
            ps.runByKind[kind] += amount;

            ZoneRecord zone = _fightZone ?? CurrentZone();
            if (zone != null)
            {
                zone.total += amount;
                Add(zone.damage, owner, amount);
            }
            if (info.victim is Monster m && m.type == Monster.MonsterType.Boss)
            {
                BossFight b = GetBossFight(m, now);
                b.lastHit = now;
                b.total += amount;
                Add(b.damage, owner, amount);
            }
        }

        /// <summary>
        /// Walk up the actor hierarchy: the nearest Gem / SkillTrigger / AttackTrigger / StarEffect
        /// is the damage source, the first Hero is the attacker.
        /// </summary>
        private static bool TryResolve(Actor actor, out Hero hero, out DewPlayer owner, out Actor source)
        {
            hero = null;
            owner = null;
            source = null;
            Actor a = actor;
            int depth = 0;
            while (a != null && depth++ < 64)
            {
                if (a is Hero h)
                {
                    hero = h;
                    break;
                }
                if (source == null)
                {
                    // Essences spawn their effects through the memory (CreateAbilityInstanceWithSource),
                    // and tag the instance with the gem: that tag is the real source.
                    if (a is ElementalStatusEffect) source = a; // elemental DoT ticks (applied in the hero's name)
                    else if (a is AbilityInstance ai && ai.Network_gem != null) source = ai.Network_gem;
                    else if (a is Gem || a is SkillTrigger || a is AttackTrigger || a is StarEffect) source = a;
                }
                a = a.parentActor;
            }

            if (hero != null)
            {
                owner = hero.owner;
                return owner != null;
            }

            // Summons / minions that are not parented to the hero: fall back on entity ownership.
            Entity ent = actor.firstEntity;
            if (ent != null && ent.owner != null && ent.owner.isHumanPlayer)
            {
                owner = ent.owner;
                hero = owner.hero;
                return true;
            }
            return false;
        }

        private static SourceStats GetSource(PlayerStats ps, Actor source)
        {
            string key = source == null ? "other" : source.GetType().Name;
            if (source is Gem || source is SkillTrigger) key += "#" + source.netId; // two copies = two lines
            if (source is ElementalStatusEffect) key = "elm:" + source.GetType().Name; // one line per element
            if (ps.sources.TryGetValue(key, out SourceStats s))
            {
                // Slot can change when the player moves a memory / essence around.
                HeroSkillLocation? slot = GetSlot(source);
                if (slot.HasValue) s.slot = slot;
                if (s.icon == null) s.icon = GetIcon(source);
                if (source != null) s.actor = source;
                return s;
            }
            s = new SourceStats
            {
                key = key,
                category = GetCategory(source),
                label = GetLabel(source),
                slot = GetSlot(source),
                icon = GetIcon(source),
                actor = source
            };
            ps.sources[key] = s;
            return s;
        }

        /// <summary>
        /// Essences and memories are only listed while still equipped by the hero
        /// (destroyed, sold or unequipped ones disappear). Other sources are always listed.
        /// </summary>
        public static bool IsListed(SourceStats s, Hero hero)
        {
            if (s.category != SourceCategory.Essence && s.category != SourceCategory.Memory) return true;
            if (s.actor == null) return false; // destroyed
            if (hero == null) return true;
            try
            {
                if (s.actor is Gem g)
                {
                    foreach (Gem equipped in hero.Skill.gems.Values)
                    {
                        if (equipped == g) return true;
                    }
                    return false;
                }
                if (s.actor is SkillTrigger st)
                {
                    return hero.Skill.GetSkill(st.skillType) == st;
                }
            }
            catch
            {
                // ignored
            }
            return true;
        }

        private static Sprite GetIcon(Actor source)
        {
            try
            {
                switch (source)
                {
                    case ElementalStatusEffect e:
                        return e.icon;
                    case Gem g:
                        return g.icon;
                    case StarEffect se:
                        return se.starIcon;
                    case AbilityTrigger at:
                        if (at.configs == null || at.configs.Length == 0) return null;
                        int i = Mathf.Clamp(at.currentConfigIndex, 0, at.configs.Length - 1);
                        return at.configs[i] != null ? at.configs[i].triggerIcon : null;
                }
            }
            catch
            {
                // ignored
            }
            return null;
        }

        private static SourceCategory GetCategory(Actor source)
        {
            switch (source)
            {
                case ElementalStatusEffect _:
                    return SourceCategory.Element;
                case Gem _:
                    return SourceCategory.Essence;
                case SkillTrigger st:
                    return st.skillType == HeroSkillLocation.Identity ? SourceCategory.Passive : SourceCategory.Memory;
                case AttackTrigger _:
                    return SourceCategory.Attack;
                case StarEffect _:
                    return SourceCategory.Passive;
                default:
                    return SourceCategory.Other;
            }
        }

        private static HeroSkillLocation? GetSlot(Actor source)
        {
            try
            {
                switch (source)
                {
                    case Gem g:
                        return g.owner != null ? g.location.skill : (HeroSkillLocation?)null;
                    case SkillTrigger st:
                        return st.skillType;
                }
            }
            catch
            {
                // ignored
            }
            return null;
        }

        private static string GetLabel(Actor source)
        {
            try
            {
                switch (source)
                {
                    case ElementalStatusEffect _:
                        return DamageKinds.Names[ElementKind(source)];
                    case Gem g:
                        return DewLocalization.GetGemName(g);
                    case SkillTrigger st:
                        return DewLocalization.GetSkillName(st, st.currentConfigIndex);
                    case AttackTrigger _:
                        return "Attaque";
                    case StarEffect se:
                        return DewLocalization.GetStarName(se.GetType());
                    case null:
                        return "Autres";
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[DreamDpsMeter] Name lookup failed for " + source.GetType().Name + ": " + e.Message);
            }
            return Prettify(source.GetType().Name);
        }

        /// <summary>Se_Elm_Fire / Cold / Light / Dark -> damage kind index.</summary>
        private static int ElementKind(Actor elementalEffect)
        {
            string n = elementalEffect.GetType().Name;
            if (n.Contains("Fire")) return 3;
            if (n.Contains("Cold")) return 4;
            if (n.Contains("Light")) return 5;
            if (n.Contains("Dark")) return 6;
            return 2;
        }

        private static string Prettify(string typeName)
        {
            int idx = typeName.IndexOf('_');
            return idx >= 0 && idx < typeName.Length - 1 ? typeName.Substring(idx + 1) : typeName;
        }
    }
}
