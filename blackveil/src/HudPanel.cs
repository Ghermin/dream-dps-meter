using System;
using System.Collections.Generic;
using System.Linq;
using RR.Game.Character;
using RR.UI.Extensions;
using RR.UI.Pages;
using RR.UI.Utils;
using UnityEngine;
using UnityEngine.UIElements;

namespace BlackveilDpsMeter
{
    /// <summary>
    /// Panneau du HUD, construit en UI Toolkit dans l'arbre de la page du jeu (polices, couleurs et cadres du jeu) :
    /// - en permanence, sous le bloc « Étage / Alerte » : dégâts de la run, DPS du combat, soins, dégâts subis,
    ///   et en multi le classement des joueurs ;
    /// - pendant que le jeu affiche les détails des compétences (Alt maintenu), le même panneau passe au détail :
    ///   tes dégâts par compétence et par altération, DPS moyen / meilleur / boss ;
    /// - un petit % au-dessus de chaque compétence de la barre (part de tes dégâts).
    /// </summary>
    internal sealed class HudPanel
    {
        public const string RootName = "BlackveilDpsMeter";
        /// <summary>Outils de dév : force la vue détaillée (vérification du rendu sans toucher au clavier).</summary>
        internal static bool DevForceDetail;
        /// <summary>Outils de dév : fait défiler la liste toute seule (vérification de la barre de défilement).</summary>
        internal static bool DevAutoScroll;
        private const float Width = 390f;
        private const float ScrollBarSpace = 18f;

        private BaseHUDPage _page;
        private VisualElement _container;
        private VisualElement _root;
        private Label _title, _total, _dpsLabel, _dpsValue, _notice, _empty;
        private VisualElement _summary, _players, _detail, _detailScroll, _detailRows, _detailStats, _vitals;
        private RR.UI.Components.ScrollableContainer _scroller;
        private Label _heal, _taken;
        private readonly List<PlayerRow> _playerRows = new List<PlayerRow>();
        private readonly List<SourceRow> _sourceRows = new List<SourceRow>();
        private readonly List<(Label label, Label value)> _stats = new List<(Label, Label)>();
        private readonly Dictionary<string, Label> _abilityLabels = new Dictionary<string, Label>();
        private float _nextRefresh;
        private bool _detailShown;
        private bool _skinned;

        public void Update(BaseHUDPage page)
        {
            if (page == null || page.RootElement == null)
            {
                Detach();
                return;
            }
            if (page != _page || _root == null || _root.panel == null || _root.parent == null)
            {
                Detach();
                if (!Attach(page)) return;
            }

            bool detail = DetailKeyHeld() || DevForceDetail;
            bool forceRefresh = detail != _detailShown;
            if (detail && _scroller != null)
            {
                try
                {
                    if (!_detailShown) _scroller.ResetScroll();
                    if (DevAutoScroll && _scroller.NeedsScrolling) _scroller.ScrollStick(-0.25f);
                    _scroller.Update(); // défilement fluide et barre de défilement du jeu, à chaque frame
                }
                catch (Exception e)
                {
                    Plugin.LogOnce("hud-scroll", e);
                }
            }
            if (!forceRefresh && Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + 0.25f;
            _detailShown = detail;
            try
            {
                Place();
                if (!_skinned) Reskin();
                Refresh(detail);
            }
            catch (Exception e)
            {
                Plugin.LogOnce("hud-refresh", e);
            }
        }

        /// <summary>
        /// Touche « détails des compétences » du jeu (Alt par défaut, réassignable, manette comprise).
        /// On lit l'action elle-même : l'indicateur d'amélioration de la barre reste allumé en permanence au vaisseau.
        /// </summary>
        private static bool DetailKeyHeld()
        {
            try
            {
                var input = RR.Input.InputManager.Instance;
                var action = input != null && input.InventoryActionMap != null ? input.InventoryActionMap.Ability : null;
                return action != null && action.Action != null && action.Action.IsPressed();
            }
            catch (Exception)
            {
                return false;
            }
        }

        public void Detach()
        {
            _root?.RemoveFromHierarchy();
            foreach (Label l in _abilityLabels.Values) l.RemoveFromHierarchy();
            _abilityLabels.Clear();
            _root = null;
            _page = null;
            _container = null;
        }

        // ------------------------------------------------------------------ construction

        private bool Attach(BaseHUDPage page)
        {
            VisualElement pageRoot = page.RootElement;
            // En run : dans l'en-tête du HUD, qui glisse / s'efface avec le bloc « Étage » quand une page s'ouvre.
            // Au vaisseau il n'y a pas d'en-tête : directement dans le HUD fixe.
            VisualElement staticHud = pageRoot.Q("StaticHUD");
            if (staticHud == null) return false;
            VisualElement container = staticHud;
            foreach (VisualElement child in staticHud.Children())
            {
                if (child.name == "Header") container = child;
            }
            _page = page;
            _container = container;
            Skin.Scan(pageRoot, force: true);
            Build();
            container.Add(_root);
            _skinned = false;
            Place();
            return true;
        }

        private void Build()
        {
            // Nouveau panneau (changement de page : vaisseau -> run…) : les lignes de l'ancien ne servent plus.
            _playerRows.Clear();
            _sourceRows.Clear();
            _stats.Clear();
            _root = new VisualElement { name = RootName, pickingMode = PickingMode.Ignore };
            _root.style.position = Position.Absolute;
            _root.style.left = 30f;
            _root.style.top = 150f;
            _root.style.width = Width;
            // Marges intérieures plus larges que la bordure décorée du cadre du jeu : aucun texte ne doit la chevaucher.
            _root.style.paddingLeft = 48f;
            _root.style.paddingRight = 50f;
            _root.style.paddingTop = 36f;
            _root.style.paddingBottom = 38f;

            // en-tête : « DÉGÂTS »  ......  total
            var header = Row();
            _title = MakeLabel("DÉGÂTS", 20f, Skin.Title, font2: true);
            _total = MakeLabel("0", 24f, Skin.Text);
            header.Add(_title);
            header.Add(_total);
            _root.Add(header);
            _root.Add(Divider());

            // résumé (vue permanente)
            _summary = new VisualElement { pickingMode = PickingMode.Ignore };
            var dps = Row();
            dps.style.height = 24f;
            _dpsLabel = MakeLabel("DPS", 18f, Skin.Muted);
            _dpsValue = MakeLabel("–", 22f, Skin.Text);
            dps.Add(_dpsLabel);
            dps.Add(_dpsValue);
            _summary.Add(dps);
            _vitals = Row();
            _vitals.style.marginTop = 4f;
            _heal = MakeLabel("", 16f, Skin.Heal);
            _taken = MakeLabel("", 16f, Skin.Hurt);
            _vitals.Add(_heal);
            _vitals.Add(_taken);
            _summary.Add(_vitals);
            _players = new VisualElement { pickingMode = PickingMode.Ignore };
            _players.style.marginTop = 6f;
            _summary.Add(_players);
            _root.Add(_summary);

            // détail (Alt maintenu)
            _detail = new VisualElement { pickingMode = PickingMode.Ignore };
            // Liste des sources dans le conteneur défilant du jeu (molette sur le panneau, ou glisser la barre) :
            // sa hauteur est limitée à la place libre au-dessus du portrait, la barre n'apparaît que si besoin.
            _detailScroll = new VisualElement { name = RootName + "-Scroll" }; // capte la molette (pas Ignore)
            _detailRows = new VisualElement { pickingMode = PickingMode.Ignore };
            _detailScroll.Add(_detailRows);
            _detail.Add(_detailScroll);
            _scroller = null;
            try
            {
                _scroller = new RR.UI.Components.ScrollableContainer(_detailScroll, ScrollBarSpace);
                _detailScroll.style.flexGrow = 0f;
            }
            catch (Exception e)
            {
                Plugin.LogOnce("hud-scroll-init", e); // sans le composant du jeu : liste simple
            }
            _detail.Add(Divider());
            _detailStats = new VisualElement { pickingMode = PickingMode.Ignore };
            _detail.Add(_detailStats);
            _root.Add(_detail);

            _empty = MakeLabel("Aucun dégât pour l'instant", 16f, Skin.Dim);
            _empty.style.marginTop = 4f;
            _root.Add(_empty);
            _notice = MakeLabel("", 15f, Skin.Gold);
            _notice.style.whiteSpace = WhiteSpace.Normal;
            _notice.style.marginTop = 6f;
            _root.Add(_notice);
        }

        /// <summary>Sous le bloc « Étage / Alerte » s'il est là, sinon en haut à gauche.</summary>
        private void Place()
        {
            VisualElement diff = _page.RootElement.Q("DifficultyFeedbackPanel");
            float top = 30f;
            float left = 30f;
            if (diff != null && diff.resolvedStyle.display != DisplayStyle.None && diff.worldBound.height > 1f)
            {
                Vector2 bottomLeft = _container.WorldToLocal(new Vector2(diff.worldBound.xMin, diff.worldBound.yMax));
                top = bottomLeft.y + 14f;
                left = Mathf.Max(20f, bottomLeft.x);
            }
            if (Mathf.Abs(_root.resolvedStyle.top - top) > 0.5f) _root.style.top = top;
            if (Mathf.Abs(_root.resolvedStyle.left - left) > 0.5f) _root.style.left = left;
        }

        /// <summary>Cadre : celui des panneaux d'info du jeu si sa texture est chargée, sinon une plaque sombre bordée.</summary>
        private void Reskin()
        {
            Skin.Scan(_page.RootElement);
            _skinned = true;
            // 1) le fond des infobulles de compétences du jeu (celles d'Alt) : un sprite qui porte sa propre découpe 9-slice ;
            // 2) à défaut, le cadre des boîtes de dialogue, découpé par nos soins.
            if (Skin.Apply(_root, "XP_PanelInfo_Stat_BG_Medium") || Skin.Apply(_root, "XP_PanelInfo_Stat_BG_Small"))
            {
                _root.style.unitySliceLeft = _root.style.unitySliceRight = StyleKeyword.Null;
                _root.style.unitySliceTop = _root.style.unitySliceBottom = StyleKeyword.Null;
                _root.style.unitySliceScale = StyleKeyword.Null;
                StretchBackground(_root);
                return;
            }
            if (Skin.Apply(_root, "Dialogue_Box_BG"))
            {
                _root.style.unitySliceLeft = _root.style.unitySliceRight = 64;
                _root.style.unitySliceTop = _root.style.unitySliceBottom = 64;
                _root.style.unitySliceScale = 0.35f;
                StretchBackground(_root);
                return;
            }
            _root.style.backgroundColor = Skin.PanelFill;
            SetBorder(_root, 2f, Skin.PanelBorder);
            _root.style.borderTopLeftRadius = _root.style.borderTopRightRadius = 4f;
            _root.style.borderBottomLeftRadius = _root.style.borderBottomRightRadius = 4f;
        }

        private static void StretchBackground(VisualElement e)
        {
            e.style.backgroundSize = new BackgroundSize(Length.Percent(100f), Length.Percent(100f));
            e.style.backgroundRepeat = new BackgroundRepeat(Repeat.NoRepeat, Repeat.NoRepeat);
            e.style.backgroundPositionX = new BackgroundPosition(BackgroundPositionKeyword.Center);
            e.style.backgroundPositionY = new BackgroundPosition(BackgroundPositionKeyword.Center);
            e.style.backgroundColor = Color.clear;
            SetBorder(e, 0f, Color.clear);
        }

        // ------------------------------------------------------------------ contenu

        private void Refresh(bool detail)
        {
            PlayerTally me = Tracker.Local;
            bool any = Tracker.Players.Count > 0;
            _total.text = Skin.Format(me != null ? me.Damage : 0f);
            _empty.style.display = any ? DisplayStyle.None : DisplayStyle.Flex;

            bool notice = DpsMeterUpdater.Notice != null && Time.realtimeSinceStartup < DpsMeterUpdater.NoticeUntil;
            _notice.style.display = notice ? DisplayStyle.Flex : DisplayStyle.None;
            if (notice) _notice.text = DpsMeterUpdater.Notice;

            _summary.style.display = !detail && any ? DisplayStyle.Flex : DisplayStyle.None;
            _detail.style.display = detail && me != null ? DisplayStyle.Flex : DisplayStyle.None;
            _title.text = detail ? "TES DÉGÂTS" : "DÉGÂTS";

            if (me != null)
            {
                bool boss = Tracker.InCombat ? Tracker.BossCombat : Tracker.LastCombatWasBoss;
                _dpsLabel.text = boss ? "DPS boss" : Tracker.InCombat ? "DPS" : "DPS (dernier combat)";
                _dpsValue.text = Tracker.CombatDuration > 0f ? Skin.Format(Tracker.CombatDps(me)) : "–";
                _dpsValue.style.color = Tracker.InCombat ? Skin.Text : Skin.Dim;
                _heal.text = me.Heal > 0f ? "+" + Skin.Format(me.Heal) + " PV soignés" : "";
                _taken.text = me.Taken > 0f ? "-" + Skin.Format(me.Taken) + " PV perdus" : "";
                _vitals.style.display = me.Heal > 0f || me.Taken > 0f ? DisplayStyle.Flex : DisplayStyle.None;
            }
            RefreshPlayers();
            if (detail && me != null) RefreshDetail(me);
            RefreshAbilityBar(me);
        }

        private void RefreshPlayers()
        {
            List<PlayerTally> list = Tracker.Players.Values.Where(p => p.Damage > 0f).OrderByDescending(p => p.Damage).ToList();
            bool show = list.Count > 1;
            _players.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (!show) return;
            if (Time.frameCount % 120 == 0) Tracker.RefreshIdentities();
            float max = list[0].Damage;
            float total = Tracker.TotalDamage;
            while (_playerRows.Count < list.Count)
            {
                var row = new PlayerRow(this);
                _playerRows.Add(row);
                _players.Add(row.Root);
            }
            for (int i = 0; i < _playerRows.Count; i++)
            {
                PlayerRow row = _playerRows[i];
                if (i >= list.Count)
                {
                    row.Root.style.display = DisplayStyle.None;
                    continue;
                }
                PlayerTally p = list[i];
                row.Root.style.display = DisplayStyle.Flex;
                row.Name.text = p.Name;
                row.Name.style.color = p.IsLocal ? Skin.Title : Skin.Text;
                row.Value.text = Skin.Format(p.Damage) + "  <color=#999691>" + Pct(p.Damage, total) + "</color>";
                row.SetBar(max > 0f ? p.Damage / max : 0f, p.IsLocal ? Skin.Gold : Skin.Muted);
            }
        }

        private void RefreshDetail(PlayerTally me)
        {
            var sources = new List<(Source s, float v)>();
            for (int i = 0; i < (int)Source.Count; i++)
            {
                if (me.BySource[i] > 0f) sources.Add(((Source)i, me.BySource[i]));
            }
            sources.Sort((a, b) => b.v.CompareTo(a.v));
            float max = sources.Count > 0 ? sources[0].v : 0f;
            while (_sourceRows.Count < sources.Count)
            {
                var row = new SourceRow(this);
                _sourceRows.Add(row);
                _detailRows.Add(row.Root);
            }
            for (int i = 0; i < _sourceRows.Count; i++)
            {
                SourceRow row = _sourceRows[i];
                if (i >= sources.Count)
                {
                    row.Root.style.display = DisplayStyle.None;
                    continue;
                }
                (Source s, float v) = sources[i];
                row.Root.style.display = DisplayStyle.Flex;
                row.Set(s, SourceName(s), Skin.Format(v), Pct(v, me.Damage), max > 0f ? v / max : 0f, _page.RootElement);
            }
            SizeScrollArea();

            var stats = new List<(string, string)>
            {
                ("DPS moyen en combat", Skin.Format(Tracker.AverageCombatDps(me))),
                ("Meilleur DPS sur un combat", Tracker.BestDps(me) > 0f ? Skin.Format(Tracker.BestDps(me)) : "–"),
            };
            if (me.BossDamage > 0f) stats.Add(("DPS moyen sur les boss", Skin.Format(Tracker.BossDps(me))));
            stats.Add(("Soins", Skin.Format(me.Heal)));
            stats.Add(("Dégâts subis", Skin.Format(me.Taken)));
            while (_stats.Count < stats.Count)
            {
                VisualElement r = Row();
                r.style.height = 22f;
                Label l = MakeLabel("", 16f, Skin.Muted);
                Label v = MakeLabel("", 17f, Skin.Text);
                r.Add(l);
                r.Add(v);
                _detailStats.Add(r);
                _stats.Add((l, v));
            }
            for (int i = 0; i < _stats.Count; i++)
            {
                bool on = i < stats.Count;
                _stats[i].label.parent.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
                if (!on) continue;
                _stats[i].label.text = stats[i].Item1;
                _stats[i].value.text = stats[i].Item2;
            }
        }

        /// <summary>
        /// Hauteur de la liste des sources : tout son contenu s'il tient entre le haut du panneau et la barre de vie /
        /// le portrait du joueur (en bas à gauche), sinon la place disponible, et la liste défile.
        /// </summary>
        private void SizeScrollArea()
        {
            if (_scroller == null) return;
            float content = _detailRows.layout.height;
            if (content <= 1f)
            {
                // pas encore mis en page (Alt vient d'être enfoncé) : estimation, corrigée au rafraîchissement suivant
                content = _sourceRows.Count(r => r.Root.style.display == DisplayStyle.Flex) * 44f;
                if (content <= 1f) return;
            }
            float bottom = BottomLimit();
            float scrollHeight = _detailScroll.layout.height;
            float others = _root.layout.height - scrollHeight; // marges, en-tête, statistiques du bas
            float available = bottom - 16f - _root.resolvedStyle.top - others;
            float height = Mathf.Max(120f, Mathf.Min(content, available));
            if (Mathf.Abs(scrollHeight - height) > 0.5f) _detailScroll.style.height = height;
            bool scrolls = content > height + 0.5f;
            _detailRows.style.right = scrolls ? ScrollBarSpace : 0f; // place pour la barre seulement si elle est là
        }

        /// <summary>
        /// Haut du bloc portrait + PV du HUD (composant HealthBar du jeu ; pas les barres de vie au-dessus des
        /// personnages, qui portent aussi ce nom), sinon haut du pied de page, sinon 900.
        /// </summary>
        private float BottomLimit()
        {
            VisualElement limit = _page.RootElement.Q<RR.UI.Controls.HUD.HealthBar>();
            if (limit == null || limit.worldBound.height <= 1f)
            {
                VisualElement staticHud = _page.RootElement.Q("StaticHUD");
                limit = staticHud?.Q("Footer");
            }
            if (limit == null || limit.worldBound.height <= 1f) return 900f;
            return _container.WorldToLocal(new Vector2(0f, limit.worldBound.yMin)).y;
        }

        /// <summary>Nom de la compétence du champion (comme dans le panneau d'amélioration du jeu).</summary>
        private string SourceName(Source s)
        {
            string ability = Sources.AbilityIconElement(s);
            if (ability != null)
            {
                try
                {
                    NetworkChampionBase champ = RR.PlayerManager.Instance != null ? RR.PlayerManager.Instance.LocalChampion : null;
                    if (champ != null && champ.ChampionType.HasValue)
                    {
                        string key = "Champion_" + EnumStr.GetChampionType(champ.ChampionType.Value).Capitalize()
                            + ".ability-" + EnumStr.GetUIAbility(UiAbility(s)) + "-name";
                        string name = RR.UI.Components.LocString.Get(key);
                        if (!string.IsNullOrEmpty(name) && name != key && !name.Contains("ability-")) return name;
                    }
                }
                catch (Exception)
                {
                    // nom générique
                }
            }
            return Sources.Name(s);
        }

        private static RR.UI.Controls.HUD.UIAbility UiAbility(Source s)
        {
            switch (s)
            {
                case Source.Power: return RR.UI.Controls.HUD.UIAbility.Offense;
                case Source.Defensive: return RR.UI.Controls.HUD.UIAbility.Defense;
                case Source.Special: return RR.UI.Controls.HUD.UIAbility.Special;
                case Source.Ultimate: return RR.UI.Controls.HUD.UIAbility.Ultimate;
                default: return RR.UI.Controls.HUD.UIAbility.Attack;
            }
        }

        /// <summary>Part de chaque compétence, au-dessus de son icône (seulement si l'on connaît les compétences : hôte / solo).</summary>
        private void RefreshAbilityBar(PlayerTally me)
        {
            VisualElement bar = _page.RootElement.Q("AbilityBar");
            if (bar == null) return;
            foreach (Source s in new[] { Source.Attack, Source.Power, Source.Ultimate, Source.Special, Source.Defensive })
            {
                string name = Sources.AbilityIconElement(s);
                VisualElement icon = bar.Q(name);
                if (icon == null) continue;
                if (!_abilityLabels.TryGetValue(name, out Label label) || label.parent != icon)
                {
                    label?.RemoveFromHierarchy();
                    label = MakeLabel("", 17f, Skin.Text);
                    label.name = RootName + "-" + name;
                    label.style.position = Position.Absolute;
                    label.style.left = 0f;
                    label.style.right = 0f;
                    label.style.top = -24f;
                    label.style.unityTextAlign = TextAnchor.MiddleCenter;
                    label.style.unityTextOutlineWidth = 0.15f;
                    label.style.unityTextOutlineColor = Color.black;
                    icon.Add(label);
                    _abilityLabels[name] = label;
                }
                float v = me != null && me.SawAbilities ? me.BySource[(int)s] : 0f;
                bool show = v > 0f && me.Damage > 0f;
                label.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
                if (show) label.text = Pct(v, me.Damage);
            }
        }

        // ------------------------------------------------------------------ petits éléments

        private static string Pct(float v, float total)
        {
            if (total <= 0f) return "";
            float p = v * 100f / total;
            return p >= 9.95f ? Mathf.RoundToInt(p) + "%" : p.ToString("0.0") + "%";
        }

        private static VisualElement Row()
        {
            var r = new VisualElement { pickingMode = PickingMode.Ignore };
            r.style.flexDirection = FlexDirection.Row;
            r.style.justifyContent = Justify.SpaceBetween;
            r.style.alignItems = Align.Center;
            return r;
        }

        private static VisualElement Divider()
        {
            var d = new VisualElement { pickingMode = PickingMode.Ignore };
            d.style.height = 1f;
            d.style.marginTop = 5f;
            d.style.marginBottom = 6f;
            d.style.backgroundColor = new Color(Skin.Title.r, Skin.Title.g, Skin.Title.b, 0.35f);
            return d;
        }

        internal static Label MakeLabel(string text, float size, Color color, bool font2 = false)
        {
            var l = new Label(text) { pickingMode = PickingMode.Ignore, enableRichText = true };
            if (font2) l.AddToClassList("font2"); // GermaniaOne, la police des titres du jeu
            l.style.fontSize = size;
            l.style.color = color;
            l.style.marginLeft = l.style.marginRight = 0f;
            l.style.marginTop = l.style.marginBottom = 0f;
            l.style.paddingLeft = l.style.paddingRight = 0f;
            // La police du jeu déborde de sa boîte : accents des majuscules (É, Â) au-dessus, jambages (p, y, g) en dessous.
            l.style.paddingTop = 2f;
            l.style.paddingBottom = 3f;
            l.style.whiteSpace = WhiteSpace.NoWrap;
            return l;
        }

        /// <summary>Nom qui peut être long (joueur, compétence) : coupé avec « … » plutôt que de déborder du cadre.</summary>
        internal static void Ellipsis(Label l)
        {
            l.style.flexShrink = 1f;
            l.style.overflow = Overflow.Hidden;
            l.style.textOverflow = TextOverflow.Ellipsis;
        }

        private static void SetBorder(VisualElement e, float w, Color c)
        {
            e.style.borderLeftWidth = e.style.borderRightWidth = w;
            e.style.borderTopWidth = e.style.borderBottomWidth = w;
            e.style.borderLeftColor = e.style.borderRightColor = c;
            e.style.borderTopColor = e.style.borderBottomColor = c;
        }

        private static VisualElement Bar(out VisualElement fill)
        {
            var back = new VisualElement { pickingMode = PickingMode.Ignore };
            back.style.height = 4f;
            back.style.marginTop = 6f;
            back.style.backgroundColor = Skin.BarBack;
            fill = new VisualElement { pickingMode = PickingMode.Ignore };
            fill.style.height = Length.Percent(100f);
            back.Add(fill);
            return back;
        }

        private sealed class PlayerRow
        {
            public readonly VisualElement Root;
            public readonly Label Name, Value;
            private readonly VisualElement _fill;

            public PlayerRow(HudPanel owner)
            {
                Root = new VisualElement { pickingMode = PickingMode.Ignore };
                Root.style.marginBottom = 4f;
                VisualElement line = Row();
                Name = MakeLabel("", 17f, Skin.Text);
                Ellipsis(Name);
                Value = MakeLabel("", 17f, Skin.Text);
                line.Add(Name);
                line.Add(Value);
                Root.Add(line);
                Root.Add(Bar(out _fill));
            }

            public void SetBar(float ratio, Color color)
            {
                _fill.style.width = Length.Percent(Mathf.Clamp01(ratio) * 100f);
                _fill.style.backgroundColor = color;
            }
        }

        private sealed class SourceRow
        {
            public readonly VisualElement Root;
            private readonly VisualElement _icon, _fill;
            private readonly Label _name, _value, _pct;
            private readonly VisualElement _dot;
            private Source _iconFor = Source.Count;
            private bool _hasIcon;
            private float _retryAt;

            public SourceRow(HudPanel owner)
            {
                Root = new VisualElement { pickingMode = PickingMode.Ignore };
                Root.style.flexDirection = FlexDirection.Row;
                Root.style.alignItems = Align.Center;
                Root.style.marginBottom = 7f;
                _icon = new VisualElement { pickingMode = PickingMode.Ignore };
                _icon.style.width = _icon.style.height = 26f;
                _icon.style.marginRight = 8f;
                _icon.style.flexShrink = 0f;
                _icon.style.alignItems = Align.Center;
                _icon.style.justifyContent = Justify.Center;
                // pastille de couleur quand le jeu n'a pas d'icône pour la source (même place qu'une icône)
                _dot = new VisualElement { pickingMode = PickingMode.Ignore };
                _dot.style.width = _dot.style.height = 10f;
                _dot.style.borderTopLeftRadius = _dot.style.borderTopRightRadius = 5f;
                _dot.style.borderBottomLeftRadius = _dot.style.borderBottomRightRadius = 5f;
                _icon.Add(_dot);
                Root.Add(_icon);
                var body = new VisualElement { pickingMode = PickingMode.Ignore };
                body.style.flexGrow = 1f;
                VisualElement line = Row();
                _name = MakeLabel("", 16f, Skin.Text);
                Ellipsis(_name);
                var right = new VisualElement { pickingMode = PickingMode.Ignore };
                right.style.flexDirection = FlexDirection.Row;
                _value = MakeLabel("", 16f, Skin.Text);
                _pct = MakeLabel("", 14f, Skin.Dim);
                _pct.style.minWidth = 40f;
                _pct.style.unityTextAlign = TextAnchor.MiddleRight;
                right.Add(_value);
                right.Add(_pct);
                line.Add(_name);
                line.Add(right);
                body.Add(line);
                body.Add(Bar(out _fill));
                Root.Add(body);
            }

            public void Set(Source s, string name, string value, string pct, float ratio, VisualElement hudRoot)
            {
                // Les icônes d'altération viennent des barres de vie, créées après le HUD : on réessaie tant qu'elles manquent.
                if (_iconFor != s || (!_hasIcon && Skin.MayHaveIcon(s) && Time.unscaledTime >= _retryAt))
                {
                    _iconFor = s;
                    _retryAt = Time.unscaledTime + 3f;
                    _icon.style.backgroundImage = StyleKeyword.None;
                    if (!_hasIcon) Skin.Scan(hudRoot);
                    _hasIcon = Skin.ApplyIcon(_icon, s, hudRoot);
                    _dot.style.display = _hasIcon ? DisplayStyle.None : DisplayStyle.Flex;
                    _dot.style.backgroundColor = Skin.SourceColor(s);
                }
                _name.text = name.Trim();
                _value.text = value;
                _pct.text = pct;
                _fill.style.width = Length.Percent(Mathf.Clamp01(ratio) * 100f);
                _fill.style.backgroundColor = Skin.SourceColor(s);
            }
        }
    }
}
