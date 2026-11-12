using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using RR.UI.Pages;
using RR.UI.UISystem;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BlackveilDpsMeter
{
    [BepInPlugin(Guid, "Blackveil DPS Meter", ModVersion.Current)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "ghermin.blackveildpsmeter";
        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> DevTools;
        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;
            Log.LogInfo("Blackveil DPS Meter v" + ModVersion.Current);
            DevTools = Config.Bind("Développement", "Outils", false,
                "Dump de l'interface (F8) et captures dans le dossier BepInEx. Inutile pour jouer.");
            _harmony = new Harmony(Guid);
            _harmony.PatchAll(typeof(Plugin).Assembly);
            DamageFeed.Init(_harmony);

            // Les plugins sont chargés très tôt (constructeur statique d'Application) : un composant ajouté à ce moment
            // ne doit pas avoir de Start() (plantage de Unity 6). Les autres composants arrivent à sa première frame.
            var host = new GameObject("BlackveilDpsMeter");
            host.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(host);
            host.AddComponent<Driver>().DllPath = Info.Location;
        }

        private static readonly HashSet<string> Logged = new HashSet<string>();

        /// <summary>Une erreur répétée à chaque frame ne s'écrit qu'une fois dans le journal.</summary>
        internal static void LogOnce(string key, Exception e)
        {
            if (Logged.Add(key)) Log.LogWarning(key + ": " + e);
        }
    }

    /// <summary>Boucle du mod : fin des combats, début / fin de run, panneau du HUD.</summary>
    internal class Driver : MonoBehaviour
    {
        private readonly HudPanel _panel = new HudPanel();
        private string _lastKind;
        internal string DllPath;
        private bool _started;

        /// <summary>
        /// Pas de Start() ici : ce composant est ajouté pendant le chargement des plugins, et Unity 6 plante
        /// si un composant ajouté à ce moment-là a une méthode Start. Les autres composants arrivent à la 1re frame.
        /// </summary>
        private void FirstFrame()
        {
            _started = true;
            try
            {
                gameObject.AddComponent<DpsMeterUpdater>().Init(DllPath);
                if (Plugin.DevTools.Value) gameObject.AddComponent<DevProbe>();
            }
            catch (Exception e)
            {
                Plugin.LogOnce("start", e);
            }
        }

        private void Update()
        {
            if (!_started) FirstFrame();
            try
            {
                Tracker.Tick();
                UIManager ui = UIManager.Instance;
                BaseHUDPage hud = ui != null ? ui.GetActiveHUDPage() : null;
                TrackRun(hud);
                _panel.Update(hud);
            }
            catch (Exception e)
            {
                Plugin.LogOnce("driver", e);
            }
        }

        /// <summary>Remise à zéro en entrant dans une run (depuis le vaisseau) et en revenant au vaisseau.</summary>
        private void TrackRun(BaseHUDPage hud)
        {
            string kind = hud is GameHUDPage ? "run" : hud is LobbyHUDPage ? "lobby" : null;
            if (kind == null || kind == _lastKind) return;
            Tracker.Reset(kind == "run");
            _lastKind = kind;
        }

        private void OnDestroy()
        {
            _panel.Detach();
        }
    }

    /// <summary>Outil de développement : dump de l'arbre UI et capture à chaque nouvelle page, et sur F8.</summary>
    internal class DevProbe : MonoBehaviour
    {
        private readonly HashSet<string> _dumped = new HashSet<string>();
        private string _pending;
        private float _pendingAt;
        private float _nextAuto;
        private float _mockAt, _altNext, _altDumpAt;
        private bool _altWas;
        private int _shots;

        private void Update()
        {
            try
            {
                UIManager ui = UIManager.Instance;
                if (ui == null) return;
                PageBase hud = ui.GetActiveHUDPage();
                string key = "hud=" + (hud?.Name ?? "none") + " main=" + (ui.IsMainLayerPageOpen ? ui.MainLayerPageName : "none");
                if (Keyboard.current != null && Keyboard.current.f8Key.wasPressedThisFrame)
                {
                    DumpNow(ui, hud, key + " (F8)");
                    return;
                }
                if (!_dumped.Contains(key) && _pending != key)
                {
                    _pending = key;
                    _pendingAt = Time.unscaledTime + 2.5f;
                }
                // au vaisseau : données fictives, pour voir le panneau rempli
                if (hud is LobbyHUDPage && Tracker.Players.Count == 0 && Time.unscaledTime >= _mockAt)
                {
                    _mockAt = Time.unscaledTime + 5f;
                    var champ = RR.PlayerManager.Instance != null ? RR.PlayerManager.Instance.LocalChampion : null;
                    if (champ != null) Tracker.Mock(champ.Stats);
                }
                // au vaisseau : alterne vue normale / détaillée toutes les 5 s, pour vérifier les deux rendus
                HudPanel.DevForceDetail = hud is LobbyHUDPage && ((int)(Time.unscaledTime / 5f)) % 2 == 1;
                HudPanel.DevAutoScroll = HudPanel.DevForceDetail;
                // vue détaillée (Alt) : un dump à chaque ouverture (au plus toutes les 10 s)
                bool alt = RR.Input.InputManager.Instance?.InventoryActionMap?.Ability?.Action?.IsPressed() == true;
                if (alt && !_altWas && Time.unscaledTime >= _altNext)
                {
                    _altNext = Time.unscaledTime + 10f;
                    _altDumpAt = Time.unscaledTime + 0.5f;
                }
                _altWas = alt;
                if (_altDumpAt > 0f && Time.unscaledTime >= _altDumpAt)
                {
                    _altDumpAt = 0f;
                    DumpNow(ui, hud, key + " (Alt)");
                }
                // en run : une capture toutes les 20 s, pour voir le panneau sans toucher au clavier
                if (hud is GameHUDPage && Time.unscaledTime >= _nextAuto)
                {
                    _nextAuto = Time.unscaledTime + 20f;
                    if (_dumped.Contains(key)) DumpNow(ui, hud, key + " (auto)");
                }
                if (_pending != null && Time.unscaledTime >= _pendingAt)
                {
                    if (_pending == key)
                    {
                        _dumped.Add(key);
                        DumpNow(ui, hud, key);
                    }
                    _pending = null;
                }
            }
            catch (Exception e)
            {
                Plugin.LogOnce("probe", e);
                enabled = false;
            }
        }

        private void DumpNow(UIManager ui, PageBase hud, string title)
        {
            UnityEngine.UIElements.VisualElement root = null;
            UIScreen screen = ui.GetComponentInChildren<UIScreen>();
            var doc = screen != null ? screen.GetComponent<UnityEngine.UIElements.UIDocument>() : null;
            if (doc != null) root = doc.rootVisualElement?.panel?.visualTree ?? doc.rootVisualElement;
            if (root == null && hud?.RootElement?.panel != null) root = hud.RootElement.panel.visualTree;
            if (root != null)
            {
                UiDump.WriteFile(UiDump.Dump(root, title));
                UiDump.ExportTextures(root);
            }
            string shot = Path.Combine(Paths.BepInExRootPath, "shots");
            Directory.CreateDirectory(shot);
            ScreenCapture.CaptureScreenshot(Path.Combine(shot, DateTime.Now.ToString("HHmmss") + "_" + (++_shots).ToString("00") + ".png"));
            Plugin.Log.LogInfo("dump #" + _shots + " " + title);
        }
    }
}
