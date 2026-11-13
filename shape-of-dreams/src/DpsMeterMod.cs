using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace DreamDpsMeter
{
    public class DpsMeterMod : ModBehaviour
    {
        private const float RefreshInterval = 0.2f;
        private const float AttachInterval = 0.5f;
        private const string PanelName = "DreamDpsMeter Panel";

        // Fixed settings (no options in the mod manager).
        private const float CombatTimeout = 5f;   // a fight ends after 5 s without damage
        private const bool IncludeOverkill = true; // damage past the enemy's remaining health counts too
        private const int DetailMaxRows = 7;       // sources shown in the detail before it scrolls

        private static readonly Color[] PlayerColors =
        {
            new Color32(0xF0, 0xC0, 0x5A, 0xFF), // gold
            new Color32(0x6C, 0xBE, 0xF5, 0xFF), // blue
            new Color32(0x86, 0xDA, 0x80, 0xFF), // green
            new Color32(0xEE, 0x7C, 0x86, 0xFF), // red
            new Color32(0xC2, 0x98, 0xF4, 0xFF), // violet
            new Color32(0x72, 0xDC, 0xD2, 0xFF) // teal
        };

        private static readonly Dictionary<SourceCategory, Color> CategoryColors = new Dictionary<SourceCategory, Color>
        {
            { SourceCategory.Memory, new Color32(0x86, 0xC0, 0xFF, 0xFF) },
            { SourceCategory.Essence, new Color32(0xCC, 0x96, 0xFF, 0xFF) },
            { SourceCategory.Passive, new Color32(0xF2, 0xC8, 0x72, 0xFF) },
            { SourceCategory.Attack, new Color32(0xDA, 0xDA, 0xDA, 0xFF) },
            { SourceCategory.Element, new Color32(0xFF, 0x7A, 0x45, 0xFF) },
            { SourceCategory.Other, new Color32(0xA0, 0xA0, 0xA0, 0xFF) }
        };

        private readonly DamageTracker _tracker = new DamageTracker();
        private readonly List<RowSpec> _specs = new List<RowSpec>();
        private Action<EventInfoDamage> _onDamage;
        private Action<EventInfoHeal> _onHeal;
        private Action<EventInfoShield> _onShield;
        private readonly SkillBarLabels _skillLabels = new SkillBarLabels();

        // One panel next to the stats window: the summary normally, the detail while the key is held.
        private MeterPanel _detail;
        private bool _detailFallback;
        private UI_InGame_HeroDetailWindow _window;
        private Color _titleColor = new Color32(0x7F, 0xD8, 0xF5, 0xFF);
        private float _detailWidth = 340f;
        private float _nextAttach;
        private float _nextRefresh;
        private float _playingSince = -1f;
        private bool _lastDetailShown;
        private bool _dumpRequested;

        private void Awake()
        {
            _onDamage = OnDamage;
            _onHeal = OnHeal;
            _onShield = OnShield;
            _tracker.combatTimeout = CombatTimeout;
            _tracker.includeOverkill = IncludeOverkill;

            // Called every time a ClientEventManager starts (each new game), cleanup when it goes away.
            CallOnNetworkedManager<ClientEventManager>(manager =>
            {
                _tracker.ResetAll();
                manager.OnTakeDamage -= _onDamage; // guard against double subscription
                manager.OnTakeDamage += _onDamage;
                manager.OnTakeHeal -= _onHeal;
                manager.OnTakeHeal += _onHeal;
                manager.OnTakeShield -= _onShield;
                manager.OnTakeShield += _onShield;
                return () =>
                {
                    if (manager == null) return;
                    manager.OnTakeDamage -= _onDamage;
                    manager.OnTakeHeal -= _onHeal;
                    manager.OnTakeShield -= _onShield;
                };
            });

            Debug.Log("[DreamDpsMeter] Loaded " + mod.metadata.id + " v" + ModVersion.Current);
        }

        private void OnDestroy()
        {
            ClientEventManager cem = NetworkedManagerBase<ClientEventManager>.softInstance;
            if (cem != null && _onDamage != null) cem.OnTakeDamage -= _onDamage;
            if (cem != null && _onHeal != null) cem.OnTakeHeal -= _onHeal;
            if (cem != null && _onShield != null) cem.OnTakeShield -= _onShield;
            _tracker.ResetAll(); // also unsubscribes gold / dream dust listeners
            _skillLabels.Clear();
            DestroyPanels();
        }

        private void OnShield(EventInfoShield info)
        {
            try
            {
                _tracker.OnTakeShield(info);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        private void OnHeal(EventInfoHeal info)
        {
            try
            {
                _tracker.OnTakeHeal(info);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        private void OnDamage(EventInfoDamage info)
        {
            try
            {
                _tracker.OnTakeDamage(info);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        // ------------------------------------------------------------------ lifecycle

        private void Update()
        {
            CheckMockTrigger();
            InGameUIManager endUi = InGameUIManager.softInstance;
            if (endUi != null && (endUi.isDoingEnding || endUi.IsState("Result"))) _tracker.Freeze();
            if (IsResultScreen())
            {
                if (_detail != null) _detail.SetActive(false);
                UpdateRecap();
                return;
            }
            bool inGame = IsPlaying();
            if (!inGame)
            {
                _playingSince = -1f;
                if (_detail != null) _detail.SetActive(false);
                return;
            }
            float unscaled = Time.unscaledTime;
            if (_playingSince < 0f) _playingSince = unscaled;

            if (unscaled >= _nextAttach)
            {
                _nextAttach = unscaled + AttachInterval;
                EnsurePanels(unscaled - _playingSince);
                if (_bodyStyle != null) _skillLabels.Ensure(_bodyStyle);
            }

            float now = Time.time;
            _tracker.Tick(now);

            // One panel, two contents: the summary, or the detail while the detail key (Ctrl) is held.
            bool detailShown = IsDetailKeyHeld();
            bool alive = _detail != null && _detail.alive;
            if (alive)
            {
                _detail.SetActive(true);
                FollowStatsWindow();
            }
            if (detailShown && alive && ReadMouse(out Vector2 mouse, out float wheel, out bool pressed, out bool held))
            {
                _detail.HandlePointer(mouse, wheel, pressed, held);
            }

            if (detailShown != _lastDetailShown || unscaled >= _nextRefresh)
            {
                if (detailShown && !_lastDetailShown && alive) _detail.ScrollToTop();
                _lastDetailShown = detailShown;
                _nextRefresh = unscaled + RefreshInterval;
                _tracker.SyncPlayers();
                if (alive)
                {
                    if (detailShown) RenderDetail(now);
                    else RenderSummary(now);
                    FollowStatsWindow(); // the height may have changed
                }
                UpdateSkillLabels();
            }
        }

        private static readonly System.Reflection.FieldInfo WindowOriginalPos =
            typeof(UI_InGame_HeroDetailWindow).GetField("_originalPos", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        /// <summary>
        /// The panel is a sibling of the stats window (not a child: the game hides the window by fading and sliding it
        /// unless "always show" is on). It copies the window's tilt and sits right above it while the window is on
        /// screen, or in its place while it is hidden, so the two never overlap.
        /// </summary>
        private void FollowStatsWindow()
        {
            if (_detailFallback || _window == null || _detail == null || !_detail.alive) return;
            RectTransform w = (RectTransform)_window.transform;
            RectTransform p = _detail.rect;
            if (p.parent != w.parent) p.SetParent(w.parent, false);

            // Where the window stands when shown (it slides by hideOffset when hidden).
            Vector3 local = w.localPosition;
            bool shown = _window.isShown;
            Vector3 origin = local;
            try
            {
                Vector3 hide = _window.hideOffset;
                if (WindowOriginalPos != null)
                {
                    // The game keeps it either as a local or as an anchored position: take the reading that matches
                    // where the window is now (at its place when shown, hideOffset away when hidden).
                    Vector3 o = (Vector3)WindowOriginalPos.GetValue(_window);
                    Vector3 asLocal = o;
                    Vector3 fromAnchored = o + (local - w.anchoredPosition3D);
                    float dLocal = Mathf.Min((asLocal - local).sqrMagnitude, (asLocal + hide - local).sqrMagnitude);
                    float dAnchored = Mathf.Min((fromAnchored - local).sqrMagnitude, (fromAnchored + hide - local).sqrMagnitude);
                    origin = dLocal <= dAnchored ? asLocal : fromAnchored;
                }
                else if (!shown)
                {
                    origin = local - hide;
                }
            }
            catch (Exception)
            {
                origin = local;
            }

            Rect r = w.rect;
            // Above the window (its shadow overflows ~18 units) while shown, in its place while hidden.
            Vector3 corner = new Vector3(r.xMin, shown ? r.yMax + 18f : r.yMin, 0f);
            p.localRotation = w.localRotation;
            p.localScale = w.localScale;
            p.localPosition = origin + w.localRotation * Vector3.Scale(w.localScale, corner);
        }

        private bool _legacyMouseFailed;

        /// <summary>Mouse position (screen pixels), wheel in notches and left button, from the Input System (legacy Input as fallback).</summary>
        private bool ReadMouse(out Vector2 pos, out float wheel, out bool pressed, out bool held)
        {
            pos = Vector2.zero;
            wheel = 0f;
            pressed = held = false;
            try
            {
                UnityEngine.InputSystem.Mouse m = UnityEngine.InputSystem.Mouse.current;
                if (m != null)
                {
                    pos = m.position.ReadValue();
                    float raw = m.scroll.ReadValue().y;
                    wheel = Mathf.Abs(raw) >= 20f ? raw / 120f : raw; // Windows reports 120 per notch on some versions
                    pressed = m.leftButton.wasPressedThisFrame;
                    held = m.leftButton.isPressed;
                    return true;
                }
            }
            catch (Exception)
            {
                // Input System unavailable: try the legacy input below
            }
            if (_legacyMouseFailed) return false;
            try
            {
                pos = Input.mousePosition;
                wheel = Input.mouseScrollDelta.y;
                pressed = Input.GetMouseButtonDown(0);
                held = Input.GetMouseButton(0);
                return true;
            }
            catch (Exception)
            {
                _legacyMouseFailed = true; // legacy input disabled in this build
                return false;
            }
        }

        private bool IsPlaying()
        {
            if (NetworkedManagerBase<ClientEventManager>.softInstance == null) return false;
            InGameUIManager ui = InGameUIManager.softInstance;
            if (ui == null || !ui.IsState("Playing")) return false;
            if (ui.isWorldDisplayed != WorldDisplayStatus.None) return false;
            return !ui.isDoingEnding;
        }

        /// <summary>
        /// Detail key held (Ctrl by default: "show details" / skill editing). Read from the keys, not from the stats
        /// window, which may be set to always show.
        /// </summary>
        private static bool IsDetailKeyHeld()
        {
            try
            {
                bool details = false, editHold = false;
                if (DewSave.profileMain != null)
                {
                    var controls = DewSave.profileMain.controls;
                    details = DewInput.GetButton(controls.showDetails, checkGameAreaForMouse: false);
                    editHold = DewInput.GetButton(controls.editSkillHold, checkGameAreaForMouse: false);
                }
                EditSkillManager esm = ManagerBase<EditSkillManager>.softInstance;
                return details || editHold || (esm != null && esm.mode != EditSkillManager.ModeType.None);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Attach the panel next to the game's stats window; fall back to our own canvas.</summary>
        private void EnsurePanels(float secondsInGame)
        {
            bool needPanel = _detail == null || !_detail.alive || _detailFallback || _window == null;
            if (!needPanel && !_dumpRequested) return;

            UI_InGame_QuestDisplay quests = UnityEngine.Object.FindObjectOfType<UI_InGame_QuestDisplay>();
            UI_InGame_QuestDisplay_Item questItem = null;
            if (quests != null && quests.itemParent != null)
            {
                foreach (UI_InGame_QuestDisplay_Item it in quests.itemParent.GetComponentsInChildren<UI_InGame_QuestDisplay_Item>(false))
                {
                    questItem = it;
                    break;
                }
            }
            if (questItem != null)
            {
                _titleColor = questItem.goalTitleColor; // header color of the objectives
                _titleColor.a = 1f;
                if (_bodyStyle == null) _bodyStyle = TextStyle.From(questItem.descText, null);
            }

            UI_InGame_HeroDetailWindow window = ManagerBase<UI_InGame_HeroDetailWindow>.softInstance;

            if (_dumpRequested && questItem != null && window != null)
            {
                _dumpRequested = false;
                WriteDump(quests, questItem, window);
            }
            if (!needPanel) return;

            if (window != null)
            {
                if (_detail != null) _detail.Destroy();
                TextStyle body = TextStyle.From(window.healthText, null);
                Transform iconText = window.transform.Find("AD/Icon Text");
                TMPro.TMP_Text iconTmp = iconText != null ? iconText.GetComponent<TMPro.TMP_Text>() : null;
                TMPro.TMP_SpriteAsset icons = iconTmp != null && iconTmp.spriteAsset != null ? iconTmp.spriteAsset : TMPro.TMP_Settings.defaultSpriteAsset;
                if (icons != null) body.spriteAsset = icons; // inline heart / shield
                _swordIcon = SpriteFromAsset(icons, 2); // the AD sword of the stats window
                ResultScreen.swordIcon = _swordIcon;
                _statsStyle = body;
                if (_bodyStyle == null) _bodyStyle = body;
                // Sibling of the window, same frame; FollowStatsWindow() places it every frame.
                _detail = MeterPanel.Create(PanelName, window.transform.parent, false, window.transform, WindowSkinParts, body, body);
                _detail.padding = new Vector4(20f, 20f, 14f, 16f);
                _detail.rowGap = 2f;
                _detail.layout.ignoreLayout = true;
                RectTransform rt = _detail.rect;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0f, 0f);
                rt.SetSiblingIndex(window.transform.GetSiblingIndex() + 1); // just above the window, below later UI
                _detailWidth = Mathf.Max(423f, ((RectTransform)window.transform).rect.width);
                _window = window;
                _detailFallback = false;
                _nextRefresh = 0f;
                FollowStatsWindow();
            }
            else if ((_detail == null || !_detail.alive) && secondsInGame > 3f)
            {
                _detail = MeterPanel.Create(PanelName, transform, true, null, null, null, null);
                PlaceFallback(_detail.rect, new Vector2(0f, 0f), new Vector2(40f, 380f));
                _detail.rect.localScale = new Vector3(0.55f, 0.55f, 1f);
                _detailFallback = true;
                _detailWidth = 423f;
                _nextRefresh = 0f;
            }
        }

        private static readonly string[] WindowSkinParts = { "Shadow", "Background", "Window Deco - Top Half" };
        private static readonly Color ValueColor = new Color32(0xD5, 0xF8, 0xFF, 0xFF);   // objective name text
        private static readonly Color StatTextColor = new Color32(0xBF, 0xD3, 0xD9, 0xFF); // stats window values

        private static void PlaceFallback(RectTransform rt, Vector2 anchor, Vector2 pos)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
            rt.anchoredPosition = pos;
        }

        private void DestroyPanels()
        {
            if (_detail != null) _detail.Destroy();
            if (_recap != null) _recap.Destroy();
            _result.Clear();
            _detail = null;
            _recap = null;
        }

        // ------------------------------------------------------------------ rendering

        private static readonly Color HealColor = new Color32(0x86, 0xDA, 0x80, 0xFF);
        private static readonly Color ShieldColor = new Color32(0xC9, 0xD8, 0xE6, 0xFF);
        private Sprite _swordIcon;

        /// <summary>Cut one glyph out of a TMP sprite asset (the icons used with &lt;sprite=N&gt;) as a regular Sprite.</summary>
        public static Sprite SpriteFromAsset(TMPro.TMP_SpriteAsset asset, int index)
        {
            try
            {
                if (asset == null || asset.spriteCharacterTable == null || index >= asset.spriteCharacterTable.Count) return null;
                Texture2D tex = asset.spriteSheet as Texture2D;
                UnityEngine.TextCore.Glyph glyph = asset.spriteCharacterTable[index].glyph;
                if (tex == null || glyph == null) return null;
                UnityEngine.TextCore.GlyphRect r = glyph.glyphRect;
                return Sprite.Create(tex, new Rect(r.x, r.y, r.width, r.height), new Vector2(0.5f, 0.5f));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[DreamDpsMeter] Could not read sprite " + index + ": " + e.Message);
                return null;
            }
        }
        private static readonly Color GoldColor = new Color32(0xF2, 0xC1, 0x4E, 0xFF);
        private static readonly Color DustColor = new Color32(0x7F, 0xD5, 0xF3, 0xFF);
        private Sprite _goldIcon;
        private Sprite _dustIcon;
        private float _nextIconLookup;

        /// <summary>Reuse the coin / dream dust icons shown next to the HUD counters (bottom-left).</summary>
        private void FindCurrencyIcons()
        {
            if ((_goldIcon != null && _dustIcon != null) || Time.unscaledTime < _nextIconLookup) return;
            _nextIconLookup = Time.unscaledTime + 5f;
            if (_goldIcon == null) _goldIcon = IconNextTo(UnityEngine.Object.FindObjectOfType<UI_EntityGold>());
            if (_dustIcon == null) _dustIcon = IconNextTo(UnityEngine.Object.FindObjectOfType<UI_EntityDreamDust>());
            if (_dustIcon != null && _dustIcon == _goldIcon) _dustIcon = null; // never show the coin twice
        }

        /// <summary>The icon right next to a HUD counter: nearest sibling image of the text (or of its parent).</summary>
        private static Sprite IconNextTo(Component text)
        {
            if (text == null) return null;
            Transform t = text.transform;
            for (int depth = 0; depth < 2 && t != null && t.parent != null; depth++, t = t.parent)
            {
                Transform parent = t.parent;
                int idx = t.GetSiblingIndex();
                Sprite best = null;
                int bestDist = int.MaxValue;
                for (int i = 0; i < parent.childCount; i++)
                {
                    if (i == idx) continue;
                    UnityEngine.UI.Image img = parent.GetChild(i).GetComponent<UnityEngine.UI.Image>();
                    if (img == null || img.sprite == null || !img.gameObject.activeInHierarchy) continue;
                    Rect r = ((RectTransform)img.transform).rect;
                    if (r.width < 8f || r.width > 90f || r.height < 8f || r.height > 90f) continue;
                    int dist = Mathf.Abs(i - idx);
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        best = img.sprite;
                    }
                }
                if (best != null) return best;
            }
            return null;
        }

        /// <summary>Summary above the stats window (key not held): damage, DPS, healing, damage taken; gold and dream dust earned.</summary>
        private void RenderSummary(float now)
        {
            _specs.Clear();
            FindCurrencyIcons();
            List<PlayerStats> list = new List<PlayerStats>(_tracker.players);
            list.Sort((a, b) => b.runDamage.CompareTo(a.runDamage));
            bool multi = list.Count > 1;
            double top = list.Count > 0 ? list[0].runDamage : 0;
            double fight = _tracker.FightDuration(now);

            BossFight boss = _tracker.ActiveBoss(now);
            string state = boss != null
                ? "<i>" + Col(BossColor, Escape(boss.name, 18) + " " + FormatTime(boss.Duration(now))) + "</i>"
                : _tracker.inFight ? "<i>" + Dim("combat " + FormatTime(fight), "BB") + "</i>" : "";
            _specs.Add(new RowSpec
            {
                height = 46f, fontSize = 28f, header = true,
                left = "<b>" + Col(_titleColor, "Dégâts") + "</b>",
                right = state.Length > 0 ? "<size=80%>" + state + "</size>" : ""
            });

            if (list.Count == 0)
            {
                _specs.Add(new RowSpec { height = 40f, fontSize = 25f, left = "<i>" + Dim("Aucun dégât pour l'instant", "AA") + "</i>" });
            }
            for (int i = 0; i < list.Count; i++)
            {
                PlayerStats p = list[i];
                Color pc = PlayerColors[p.colorIndex % PlayerColors.Length];
                string name = multi ? Dim((i + 1) + ".", "99") + " " + Col(pc, Escape(p.name, 16)) : Col(pc, "Total"); // solo: the name only takes room
                // During a boss fight the DPS shown is the DPS on the boss.
                string dps = boss != null
                    ? Col(BossColor, Fmt(boss.Of(p.player) / boss.Duration(now)) + " dps")
                    : Dim(Fmt(p.fightDamage / fight) + " dps", "AA");
                string right = (p.runDamage <= 0 ? Dim("—", "77") : "<b>" + Col(ValueColor, Fmt(p.runDamage)) + "</b>")
                               + " <size=78%>" + dps + "</size>";
                _specs.Add(new RowSpec
                {
                    height = 40f, fontSize = 25f, left = name, right = right,
                    fill = top > 0 ? (float)(p.runDamage / top) : 0f, barColor = WithAlpha(pc, 0.7f), thinBar = true
                });

                // Healing received / health lost, on the line below so the name keeps its room.
                string life = Col(HealColor, "+" + Fmt(p.runHeal)) + "  " + Col(TakenColor, "-" + Fmt(p.runTaken));

                if (p.player != DewPlayer.local)
                {
                    // Other players: no currencies, just their health line.
                    _specs.Add(new RowSpec { height = 30f, fontSize = 21f, left = life });
                    continue;
                }

                // Currencies earned this run, with the health line between them.
                string gold = Col(GoldColor, "+" + p.runGold.ToString("#,0", CultureInfo.InvariantCulture).Replace(",", " "))
                              + (_goldIcon == null ? " " + Dim("or", "AA") : "");
                string dust = Col(DustColor, "+" + p.runDreamDust.ToString("#,0", CultureInfo.InvariantCulture).Replace(",", " "))
                              + (_dustIcon == null ? " " + Dim("poussière de rêve", "AA") : "");
                _specs.Add(new RowSpec
                {
                    height = 34f, fontSize = 22f, indent = 0f,
                    icon = _goldIcon, iconSize = 24f, left = gold + "   " + life,
                    rightIcon = _dustIcon, right = dust
                });
                AddElementRows(p.runByKind, 0f);
            }
            _detail.maxHeight = 0f; // never scrolls
            _detail.Render(_specs, _detailWidth);
        }

        /// <summary>
        /// Ctrl detail above the stats window: one line per memory / essence / passive with its icon,
        /// a bar split by damage element (game colors), the run total and its share.
        /// </summary>
        private void RenderDetail(float now)
        {
            _specs.Clear();
            List<PlayerStats> list = new List<PlayerStats>();
            foreach (PlayerStats lp in _tracker.players)
            {
                if (lp.player == DewPlayer.local) list.Add(lp); // the detail is yours only
            }
            list.Sort((a, b) => b.runDamage.CompareTo(a.runDamage));
            bool multi = list.Count > 1;
            double party = 0;
            foreach (PlayerStats p in list) party += p.runDamage;

            _specs.Add(new RowSpec
            {
                height = 46f, fontSize = 28f, header = true,
                left = "<b>" + Col(_titleColor, "Dégâts") + "</b>",
                right = multi ? "" : "<b>" + Col(ValueColor, Fmt(party)) + "</b>"
            });

            bool any = false;
            foreach (PlayerStats p in list)
            {
                double dmg = p.runDamage;
                Color pc = PlayerColors[p.colorIndex % PlayerColors.Length];
                if (multi)
                {
                    int rank = list.IndexOf(p) + 1;
                    _specs.Add(new RowSpec
                    {
                        height = 42f, fontSize = 26f,
                        left = Dim(rank + ".", "99") + "  " + Col(pc, Escape(p.name, 14)),
                        right = "<b>" + Col(ValueColor, Fmt(dmg)) + "</b>  " + Dim("<size=85%>" + Pct(dmg, party) + "</size>", "99"),
                        fill = party > 0 ? (float)(dmg / party) : 0f, barColor = WithAlpha(pc, 0.7f), thinBar = true
                    });
                }

                Hero hero = p.player != null ? p.player.hero : null;
                List<SourceStats> sources = new List<SourceStats>();
                foreach (SourceStats s in p.sources.Values)
                {
                    bool useful = s.runDamage > 0 || s.runHeal > 0 || s.runShield > 0;
                    if (useful && DamageTracker.IsListed(s, hero)) sources.Add(s);
                }
                sources.Sort((a, b) => b.runDamage != a.runDamage
                    ? b.runDamage.CompareTo(a.runDamage)
                    : (b.runHeal + b.runShield).CompareTo(a.runHeal + a.runShield));
                double topSource = sources.Count > 0 ? sources[0].runDamage : 0;
                foreach (SourceStats s in sources)
                {
                    any = true;
                    Sprite icon = s.icon != null ? s.icon : s.category == SourceCategory.Attack ? _swordIcon : null;
                    string left = icon != null ? "" : Col(StatTextColor, Escape(s.label, 12));
                    string right = s.runDamage > 0
                        ? Col(ValueColor, Fmt(s.runDamage)) + "  " + Dim("<size=85%>" + Pct(s.runDamage, dmg) + "</size>", "99")
                        : "";
                    // Healing / shields given by this source (shown only, never counted in the DPS).
                    string support = "";
                    if (s.runHeal > 0) support += "<sprite=3>" + Col(HealColor, Fmt(s.runHeal));
                    if (s.runShield > 0) support += (support.Length > 0 ? " " : "") + "<sprite=7>" + Col(ShieldColor, Fmt(s.runShield));
                    if (support.Length > 0) right = "<size=78%>" + support + "</size>" + (right.Length > 0 ? "   " + right : "");
                    _specs.Add(new RowSpec
                    {
                        height = 40f, indent = multi ? 6f : 0f, fontSize = 25f,
                        icon = icon, iconSize = 34f,
                        left = left,
                        right = right,
                        segments = s.runByKind, segmentColors = DamageKinds.Colors, segmentHeight = 10f,
                        segmentFill = topSource > 0 ? (float)(s.runDamage / topSource) : 0f
                    });
                }
            }
            if (!any)
            {
                _specs.Add(new RowSpec { height = 42f, fontSize = 25f, left = "<i>" + Dim("Aucun dégât pour l'instant", "AA") + "</i>" });
            }
            _detail.maxHeight = DetailMaxHeight();
            _detail.Render(_specs, _detailWidth);
        }

        /// <summary>Tallest the detail may get before its list scrolls: 7 sources, and never past the top of the screen.</summary>
        private float DetailMaxHeight()
        {
            // Header (46 + gap) and N source rows (40 + gap), plus the panel padding.
            float cap = _detail.padding.z + 48f + DetailMaxRows * 42f - 2f + _detail.padding.w;
            if (_detailFallback) return cap;
            RectTransform rt = _detail.rect;
            Camera cam = CanvasCamera(rt);
            // Screen pixels per panel unit, and the screen y of the panel's bottom (it grows upward from there).
            float bottom = RectTransformUtility.WorldToScreenPoint(cam, rt.TransformPoint(Vector3.zero)).y;
            float perUnit = RectTransformUtility.WorldToScreenPoint(cam, rt.TransformPoint(new Vector3(0f, 100f, 0f))).y - bottom;
            if (perUnit <= 0f) return cap;
            perUnit /= 100f;
            float room = (Screen.height - 40f * perUnit - bottom) / perUnit;
            return Mathf.Clamp(room, 200f, cap);
        }

        private static Camera CanvasCamera(Transform t)
        {
            Canvas c = t.GetComponentInParent<Canvas>();
            if (c == null) return null;
            c = c.rootCanvas;
            return c.renderMode == RenderMode.ScreenSpaceOverlay ? null : c.worldCamera;
        }

        private TextStyle _bodyStyle;
        private TextStyle _statsStyle;
        private static readonly Color BossColor = new Color32(0xFF, 0xB8, 0x55, 0xFF);
        private static readonly Color TakenColor = new Color32(0xFF, 0x7B, 0x73, 0xFF);

        // ------------------------------------------------------------------ end-of-run recap

        private const string RecapName = "DreamDpsMeter Recap";
        private MeterPanel _recap;
        private float _nextRecap;

        private bool IsResultScreen()
        {
            InGameUIManager ui = InGameUIManager.softInstance;
            return ui != null && ui.IsState("Result");
        }

        private readonly ResultScreen _result = new ResultScreen();

        /// <summary>Damage recap integrated into the game's own result screen (hero panel + stats list).</summary>
        private void UpdateRecap()
        {
            float unscaled = Time.unscaledTime;
            if (unscaled < _nextRecap) return;
            _nextRecap = unscaled + 0.5f;
            try
            {
                _result.Update(_tracker, Time.time);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                _nextRecap = unscaled + 5f;
            }
        }

        private void RenderRecap(float now)
        {
            _specs.Clear();
            List<PlayerStats> list = new List<PlayerStats>(_tracker.players);
            list.Sort((a, b) => b.runDamage.CompareTo(a.runDamage));
            bool multi = list.Count > 1;
            double combat = _tracker.RunCombatDuration(now);
            double top = list.Count > 0 ? list[0].runDamage : 0;

            _specs.Add(new RowSpec
            {
                height = 48f, fontSize = 28f, header = true,
                left = "<b>" + Col(_titleColor, "Récap des dégâts") + "</b>",
                right = "<i><size=80%>" + Dim("en combat " + FormatTime(combat), "AA") + "</size></i>"
            });
            for (int i = 0; i < list.Count; i++)
            {
                PlayerStats p = list[i];
                Color pc = PlayerColors[p.colorIndex % PlayerColors.Length];
                _specs.Add(new RowSpec
                {
                    height = 42f, fontSize = 27f,
                    left = (multi ? Dim((i + 1) + ".", "99") + "  " : "") + Col(pc, Escape(p.name, 16)),
                    right = "<b>" + Col(ValueColor, Fmt(p.runDamage)) + "</b>  <size=82%>" + Dim(Fmt(p.runDamage / combat) + " dps moy.", "AA") + "</size>",
                    fill = top > 0 ? (float)(p.runDamage / top) : 0f, barColor = WithAlpha(pc, 0.7f), thinBar = true
                });
                _specs.Add(new RowSpec
                {
                    height = 32f, fontSize = 22f, indent = 4f,
                    left = Dim("meilleur combat ", "99") + Col(ValueColor, Fmt(p.bestFightDps) + " dps"),
                    right = Col(HealColor, "+" + Fmt(p.runHeal) + " soin") + "  " + Col(ShieldColor, Fmt(p.runShield) + " bouclier")
                            + "  " + Col(TakenColor, "-" + Fmt(p.runTaken) + " subis")
                });
            }

            PlayerStats me = null;
            foreach (PlayerStats p in list)
            {
                if (p.player == DewPlayer.local) me = p;
            }
            if (me != null)
            {
                _specs.Add(new RowSpec { height = 34f, fontSize = 23f, header = true, left = "<b>" + Col(_titleColor, "Tes sources") + "</b>" });
                Hero hero = me.player != null ? me.player.hero : null;
                List<SourceStats> sources = new List<SourceStats>();
                foreach (SourceStats s in me.sources.Values)
                {
                    if (s.runDamage > 0 || s.runHeal > 0 || s.runShield > 0) sources.Add(s); // the whole run, equipped or not
                }
                sources.Sort((a, b) => b.runDamage.CompareTo(a.runDamage));
                double topSource = sources.Count > 0 ? sources[0].runDamage : 0;
                foreach (SourceStats s in sources)
                {
                    Sprite icon = s.icon != null ? s.icon : s.category == SourceCategory.Attack ? _swordIcon : null;
                    string right = s.runDamage > 0
                        ? Col(ValueColor, Fmt(s.runDamage)) + "  " + Dim("<size=85%>" + Pct(s.runDamage, me.runDamage) + "</size>", "99")
                        : "";
                    if (s.runHeal > 0) right = "<size=78%>" + Col(HealColor, "+" + Fmt(s.runHeal)) + "</size>  " + right;
                    _specs.Add(new RowSpec
                    {
                        height = 38f, fontSize = 24f, icon = icon, iconSize = 32f,
                        left = icon != null ? "" : Col(StatTextColor, Escape(s.label, 12)),
                        right = right,
                        segments = s.runByKind, segmentColors = DamageKinds.Colors, segmentHeight = 10f,
                        segmentFill = topSource > 0 ? (float)(s.runDamage / topSource) : 0f
                    });
                }
            }
            _recap.Render(_specs, 660f);
        }

        /// <summary>Damage split by element / type, colored like the game's damage numbers (3 per row).</summary>
        private void AddElementRows(double[] kinds, float indent)
        {
            double total = 0;
            foreach (double v in kinds) total += v;
            if (total <= 0) return;
            _specs.Add(new RowSpec
            {
                height = 18f, indent = indent, fontSize = 1f,
                segments = kinds, segmentColors = DamageKinds.Colors, segmentFill = 1f, segmentHeight = 8f
            });
        }

        private void UpdateSkillLabels()
        {
            DewPlayer local = DewPlayer.local;
            PlayerStats ps = null;
            if (local != null)
            {
                foreach (PlayerStats p in _tracker.players)
                {
                    if (p.player == local) ps = p;
                }
            }
            _skillLabels.Update(ps, local != null ? local.hero : null);
        }

        private static double Value(PlayerStats p, bool run) => run ? p.runDamage : p.fightDamage;

        private static double Value(SourceStats s, bool run) => run ? s.runDamage : s.fightDamage;

        private static string SourceTag(SourceStats s)
        {
            switch (s.category)
            {
                case SourceCategory.Memory:
                case SourceCategory.Essence:
                    if (!s.slot.HasValue) return s.category == SourceCategory.Memory ? "Mémoire" : "Essence";
                    switch (s.slot.Value)
                    {
                        case HeroSkillLocation.Identity: return "[Trait]";
                        case HeroSkillLocation.Movement: return "[Esp]";
                        default: return "[" + s.slot.Value + "]";
                    }
                case SourceCategory.Passive:
                    return "Passif";
                default:
                    return "";
            }
        }

        // ------------------------------------------------------------------ helpers

        private static Color WithAlpha(Color c, float a)
        {
            c.a = a;
            return c;
        }

        private static string Col(Color c, string s) => "<color=#" + ColorUtility.ToHtmlStringRGB(c) + ">" + s + "</color>";

        /// <summary>White text with the given alpha (hex "00".."FF"). TMP has no closing tag for &lt;alpha&gt;.</summary>
        private static string Dim(string s, string alphaHex) => "<color=#FFFFFF" + alphaHex + ">" + s + "</color>";

        private static string Escape(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("<", "‹").Replace(">", "›").Replace("\n", " ").Trim();
            if (s.Length > max) s = s.Substring(0, max - 1).TrimEnd() + "…";
            return s;
        }

        public static string Fmt(double v)
        {
            CultureInfo ci = CultureInfo.InvariantCulture;
            if (double.IsNaN(v) || v < 0) v = 0;
            if (v < 1000) return Math.Round(v).ToString("0", ci);
            if (v < 10000) return (v / 1000).ToString("0.00", ci) + "k";
            if (v < 1000000) return (v / 1000).ToString("0.0", ci) + "k";
            if (v < 10000000) return (v / 1000000).ToString("0.00", ci) + "M";
            return (v / 1000000).ToString("0.0", ci) + "M";
        }

        private static string Pct(double part, double whole)
        {
            if (whole <= 0) return "0%";
            return Math.Round(part / whole * 100.0).ToString("0", CultureInfo.InvariantCulture) + "%";
        }

        private static string FormatTime(double seconds)
        {
            int s = (int)Math.Max(0, seconds);
            return (s / 60) + ":" + (s % 60).ToString("00", CultureInfo.InvariantCulture);
        }

        // ------------------------------------------------------------------ debug

        private void WriteDump(UI_InGame_QuestDisplay quests, UI_InGame_QuestDisplay_Item item, UI_InGame_HeroDetailWindow window)
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("=== Quest display item parent ===");
                sb.Append(UiSkin.DumpHierarchy(quests != null ? quests.itemParent : null));
                sb.AppendLine("=== Party display ===");
                UI_InGame_PartyDisplay party = UnityEngine.Object.FindObjectOfType<UI_InGame_PartyDisplay>();
                sb.Append(UiSkin.DumpHierarchy(party != null ? party.transform : null));
                sb.AppendLine("=== Hero detail window ===");
                sb.Append(UiSkin.DumpHierarchy(window != null ? window.transform : null));
                // Outside of Mods/: a file written there would trigger the game's mod auto-reload.
                string path = Path.Combine(Application.persistentDataPath, "DreamDpsMeter_ui_dump.txt");
                File.WriteAllText(path, sb.ToString());
                Debug.Log("[DreamDpsMeter] UI dump written to " + path);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[DreamDpsMeter] UI dump failed: " + e.Message);
            }
        }

        private float _nextMockCheck;

        /// <summary>Test data on demand: console "dps_mock", or a file named DreamDpsMeter_mock in the game's data folder.</summary>
        private void CheckMockTrigger()
        {
            if (Time.unscaledTime < _nextMockCheck) return;
            _nextMockCheck = Time.unscaledTime + 1f;
            try
            {
                string path = Path.Combine(Application.persistentDataPath, "DreamDpsMeter_mock");
                if (!File.Exists(path)) return;
                File.Delete(path);
                DpsMock();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[DreamDpsMeter] Mock trigger failed: " + e.Message);
            }
        }

        [ConsoleCommand("Remplit le DPS meter avec des dégâts fictifs (test)", "dps_mock")]
        public void DpsMock()
        {
            DewGameResult.PlayerData pd = null;
            UI_InGame_Result_HeroSkillItem item = UnityEngine.Object.FindObjectOfType<UI_InGame_Result_HeroSkillItem>();
            if (item != null && item.currentData != null && item.currentData.players != null && item.currentPlayerIndex < item.currentData.players.Count)
            {
                pd = item.currentData.players[item.currentPlayerIndex];
            }
            DewPlayer local = DewPlayer.local;
            Hero hero = local != null && pd == null ? local.hero : null;
            _tracker.SyncPlayers();
            _tracker.Mock(local, hero, pd);
            _nextRefresh = 0f;
            _nextRecap = 0f;
            Debug.Log("[DreamDpsMeter] Données de test injectées.");
        }

        [ConsoleCommand("Remet à zéro les compteurs du DPS meter", "dps_reset")]
        public void DpsReset()
        {
            _tracker.ResetAll();
            _nextRefresh = 0f;
            Debug.Log("[DreamDpsMeter] Compteurs remis à zéro.");
        }

        [ConsoleCommand("Reconstruit les panneaux du DPS meter et réécrit ui_dump.txt", "dps_rebuild")]
        public void DpsRebuild()
        {
            DestroyPanels();
            _skillLabels.Clear();
            _dumpRequested = true;
            _nextAttach = 0f;
        }
    }
}
