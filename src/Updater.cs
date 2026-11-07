using System;
using System.Collections;
using System.Globalization;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace DreamDpsMeter
{
    /// <summary>
    /// Self-update: at game start, compares this build with release/version.txt on GitHub and, if newer,
    /// downloads release/DreamDpsMeter.dll over the installed one (applied at the next game start).
    /// Only ever downloads from this mod's own repository, over HTTPS.
    /// </summary>
    public class DpsMeterUpdater : ModBehaviour
    {
        private const string DllName = "DreamDpsMeter.dll";
        private GameObject _toast;

        private void Start()
        {
            AnnounceIfJustUpdated();
            DpsMeterMod meter = GetComponent<DpsMeterMod>();
            if (meter != null && !meter.config.autoUpdate) return;
            StartCoroutine(CheckForUpdate());
        }

        private void OnDestroy()
        {
            if (_toast != null) Destroy(_toast);
        }

        private IEnumerator CheckForUpdate()
        {
            yield return new WaitForSecondsRealtime(3f);
            string bust = "?t=" + DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture); // skip GitHub's raw cache

            string remote;
            using (UnityWebRequest req = UnityWebRequest.Get(ModVersion.RawBase + "release/version.txt" + bust))
            {
                req.timeout = 15;
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success)
                {
                    Debug.Log("[DreamDpsMeter] Update check failed: " + req.error);
                    yield break;
                }
                remote = req.downloadHandler.text.Trim();
            }
            if (!IsNewer(remote, ModVersion.Current))
            {
                Debug.Log("[DreamDpsMeter] Up to date (" + ModVersion.Current + ").");
                yield return SyncAboutFiles(bust);
                yield break;
            }

            byte[] dll;
            using (UnityWebRequest req = UnityWebRequest.Get(ModVersion.RawBase + "release/" + DllName + bust))
            {
                req.timeout = 60;
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning("[DreamDpsMeter] Update download failed: " + req.error);
                    yield break;
                }
                dll = req.downloadHandler.data;
            }
            // Sanity check: a .NET assembly starts with "MZ".
            if (dll == null || dll.Length < 4096 || dll[0] != (byte)'M' || dll[1] != (byte)'Z')
            {
                Debug.LogWarning("[DreamDpsMeter] Update download is not a valid DLL, ignored.");
                yield break;
            }

            string metadata = null;
            using (UnityWebRequest req = UnityWebRequest.Get(ModVersion.RawBase + "about/metadata.json" + bust))
            {
                req.timeout = 15;
                yield return req.SendWebRequest();
                if (req.result == UnityWebRequest.Result.Success) metadata = req.downloadHandler.text;
            }

            try
            {
                string target = Path.Combine(mod.path, DllName);
                string temp = target + ".download";
                File.WriteAllBytes(temp, dll);
                if (File.Exists(target)) File.Delete(target);
                File.Move(temp, target);
                if (!string.IsNullOrEmpty(metadata) && metadata.Contains("\"assemblies\""))
                {
                    File.WriteAllText(Path.Combine(mod.path, "about", "metadata.json"), metadata);
                }
                Debug.Log("[DreamDpsMeter] Updated " + ModVersion.Current + " -> " + remote + ".");
                File.WriteAllText(Path.Combine(mod.path, "about", JustUpdatedFile), remote);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[DreamDpsMeter] Could not install the update: " + e.Message);
                yield break;
            }
            yield return ApplyWhenSafe(remote);
        }

        /// <summary>Written before reloading, read by the new version to say it is running.</summary>
        private const string JustUpdatedFile = ".updated";

        /// <summary>
        /// Load the new DLL right away through the game's own mod reload (the game reads the DLL as bytes, so
        /// the file was free to replace). Only outside a run / lobby, like the game's mod manager.
        /// </summary>
        private IEnumerator ApplyWhenSafe(string remote)
        {
            bool told = false;
            while (true)
            {
                bool autoReload = false;
                try
                {
                    autoReload = DewMod.isAutoReloadEnabled; // developer auto-reload already picks the new file up
                }
                catch (Exception)
                {
                    // older game: no auto-reload flag
                }
                if (autoReload) yield break;
                if (IsSafeToReload())
                {
                    Debug.Log("[DreamDpsMeter] Reloading mods to apply v" + remote + ".");
                    try
                    {
                        DewMod.ReloadFromActiveMods(); // destroys this instance, then loads the new DLL
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning("[DreamDpsMeter] Reload failed, the update applies at the next launch: " + e.Message);
                        ShowToast("<b>Dream DPS Meter</b> mis à jour en v" + remote + "\nRedémarre le jeu pour l'appliquer.");
                    }
                    yield break;
                }
                if (!told)
                {
                    told = true;
                    ShowToast("<b>Dream DPS Meter</b> v" + remote + " téléchargée\nElle s'appliquera au retour au menu principal.");
                }
                yield return new WaitForSecondsRealtime(3f);
            }
        }

        private static bool IsSafeToReload()
        {
            try
            {
                if (DewMod.isLoadingMod) return false;
                if (Mirror.NetworkClient.active || Mirror.NetworkServer.active) return false; // run or lobby
                return NetworkedManagerBase<ClientEventManager>.softInstance == null;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>After an in-game reload: confirm the new version is running.</summary>
        private void AnnounceIfJustUpdated()
        {
            try
            {
                string path = Path.Combine(mod.path, "about", JustUpdatedFile);
                if (!File.Exists(path)) return;
                File.Delete(path);
                ShowToast("<b>Dream DPS Meter</b> mis à jour en v" + ModVersion.Current);
            }
            catch (Exception)
            {
                // nothing to announce
            }
        }

        /// <summary>Files of the Mods menu (icon, preview, description) that only the DLL update would otherwise never refresh.</summary>
        private static readonly string[] AboutFiles = { "icon.png", "preview.png", "description.txt" };

        /// <summary>
        /// Once per installed version: bring about/ in line with the repository (icon, preview, description).
        /// A marker file remembers the version already synced, so this costs nothing on later launches.
        /// </summary>
        private IEnumerator SyncAboutFiles(string bust)
        {
            string dir = Path.Combine(mod.path, "about");
            string marker = Path.Combine(dir, ".synced");
            bool done = false;
            try
            {
                done = File.Exists(marker) && File.ReadAllText(marker).Trim() == ModVersion.Current;
            }
            catch (Exception)
            {
                // unreadable marker: sync again
            }
            if (done) yield break;

            bool ok = true;
            foreach (string name in AboutFiles)
            {
                using (UnityWebRequest req = UnityWebRequest.Get(ModVersion.RawBase + "about/" + name + bust))
                {
                    req.timeout = 20;
                    yield return req.SendWebRequest();
                    byte[] data = req.result == UnityWebRequest.Result.Success ? req.downloadHandler.data : null;
                    bool png = name.EndsWith(".png", StringComparison.OrdinalIgnoreCase);
                    if (data == null || data.Length == 0 || (png && (data.Length < 8 || data[0] != 0x89 || data[1] != (byte)'P')))
                    {
                        ok = false;
                        continue;
                    }
                    try
                    {
                        string path = Path.Combine(dir, name);
                        if (!File.Exists(path) || !SameBytes(File.ReadAllBytes(path), data)) File.WriteAllBytes(path, data);
                    }
                    catch (Exception e)
                    {
                        ok = false;
                        Debug.LogWarning("[DreamDpsMeter] Could not update about/" + name + ": " + e.Message);
                    }
                }
            }
            if (!ok) yield break;
            try
            {
                File.WriteAllText(marker, ModVersion.Current);
            }
            catch (Exception)
            {
                // read-only folder: we will simply check again next launch
            }
        }

        private static bool SameBytes(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i]) return false;
            }
            return true;
        }

        private static bool IsNewer(string remote, string local)
        {
            return Version.TryParse(remote, out Version r) && Version.TryParse(local, out Version l) && r > l;
        }

        /// <summary>Small notice at the bottom of the screen for a few seconds.</summary>
        private void ShowToast(string text)
        {
            _toast = new GameObject("DreamDpsMeter Update", typeof(RectTransform));
            _toast.transform.SetParent(transform, false);
            Canvas canvas = _toast.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 500;
            CanvasScaler scaler = _toast.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            GameObject box = new GameObject("Box", typeof(RectTransform));
            box.transform.SetParent(_toast.transform, false);
            RectTransform brt = (RectTransform)box.transform;
            brt.anchorMin = brt.anchorMax = brt.pivot = new Vector2(0.5f, 0f);
            brt.anchoredPosition = new Vector2(0f, 60f);
            brt.sizeDelta = new Vector2(560f, 80f);
            UiSkin.BuildWindowFrame(brt);

            GameObject label = new GameObject("Text", typeof(RectTransform));
            label.transform.SetParent(box.transform, false);
            RectTransform lrt = (RectTransform)label.transform;
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = new Vector2(16f, 8f);
            lrt.offsetMax = new Vector2(-16f, -8f);
            TextMeshProUGUI t = label.AddComponent<TextMeshProUGUI>();
            t.fontSize = 22f;
            t.alignment = TextAlignmentOptions.Center;
            t.color = new Color32(0xD5, 0xF8, 0xFF, 0xFF);
            t.SetText(text);
            Destroy(_toast, 12f);
        }
    }
}
