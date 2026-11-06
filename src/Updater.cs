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
                ShowToast("<b>Dream DPS Meter</b> mis à jour en v" + remote + "\nRedémarre le jeu pour l'appliquer.");
            }
            catch (Exception e)
            {
                Debug.LogWarning("[DreamDpsMeter] Could not install the update: " + e.Message);
            }
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
