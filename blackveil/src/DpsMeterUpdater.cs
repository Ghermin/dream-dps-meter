using System;
using System.Collections;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace BlackveilDpsMeter
{
    /// <summary>
    /// Mise à jour automatique : au lancement, compare cette version avec blackveil/release/version.txt sur GitHub
    /// et, si elle est plus récente, remplace la DLL (appliquée au prochain lancement du jeu).
    /// Ne télécharge que depuis le dépôt du mod, en HTTPS.
    /// </summary>
    internal class DpsMeterUpdater : MonoBehaviour
    {
        /// <summary>Message affiché quelques secondes dans le panneau.</summary>
        public static string Notice { get; private set; }
        public static float NoticeUntil { get; private set; }

        private string _dllPath;

        public void Init(string dllPath)
        {
            _dllPath = dllPath;
        }

        private void Start()
        {
            CleanupOld();
            AnnounceIfJustUpdated();
            StartCoroutine(Check());
        }

        private void CleanupOld()
        {
            try
            {
                string old = _dllPath + ".old";
                if (File.Exists(old)) File.Delete(old);
            }
            catch (Exception)
            {
                // encore verrouillé : on réessaiera au prochain lancement
            }
        }

        private IEnumerator Check()
        {
            if (string.IsNullOrEmpty(_dllPath)) yield break;
            yield return new WaitForSecondsRealtime(5f);
            string bust = "?t=" + DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture);
            string remote;
            using (UnityWebRequest req = UnityWebRequest.Get(ModVersion.RawBase + "release/version.txt" + bust))
            {
                req.timeout = 15;
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success)
                {
                    Plugin.Log.LogInfo("Vérification de mise à jour impossible : " + req.error);
                    yield break;
                }
                remote = req.downloadHandler.text.Trim();
            }
            if (!IsNewer(remote, ModVersion.Current))
            {
                Plugin.Log.LogInfo("À jour (" + ModVersion.Current + ").");
                yield break;
            }

            byte[] dll;
            using (UnityWebRequest req = UnityWebRequest.Get(ModVersion.RawBase + "release/BlackveilDpsMeter.dll" + bust))
            {
                req.timeout = 60;
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success)
                {
                    Plugin.Log.LogWarning("Téléchargement de la mise à jour impossible : " + req.error);
                    yield break;
                }
                dll = req.downloadHandler.data;
            }
            if (dll == null || dll.Length < 4096 || dll[0] != (byte)'M' || dll[1] != (byte)'Z')
            {
                Plugin.Log.LogWarning("La mise à jour téléchargée n'est pas une DLL valide, ignorée.");
                yield break;
            }
            try
            {
                string old = _dllPath + ".old";
                string temp = _dllPath + ".download";
                File.WriteAllBytes(temp, dll);
                if (File.Exists(old)) File.Delete(old);
                File.Move(_dllPath, old); // Windows permet de renommer la DLL chargée
                File.Move(temp, _dllPath);
                File.WriteAllText(Path.Combine(Path.GetDirectoryName(_dllPath), ".updated"), remote);
                Plugin.Log.LogInfo("Mis à jour " + ModVersion.Current + " -> " + remote + " (au prochain lancement).");
                Show("Blackveil DPS Meter v" + remote + " téléchargée : elle s'appliquera au prochain lancement du jeu.", 15f);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Installation de la mise à jour impossible : " + e.Message);
            }
        }

        private void AnnounceIfJustUpdated()
        {
            try
            {
                string marker = Path.Combine(Path.GetDirectoryName(_dllPath), ".updated");
                if (!File.Exists(marker)) return;
                File.Delete(marker);
                Show("Blackveil DPS Meter mis à jour en v" + ModVersion.Current, 12f);
            }
            catch (Exception)
            {
                // rien à annoncer
            }
        }

        private static void Show(string text, float seconds)
        {
            Notice = text;
            NoticeUntil = Time.realtimeSinceStartup + seconds;
        }

        private static bool IsNewer(string remote, string local)
        {
            return Version.TryParse(remote, out Version r) && Version.TryParse(local, out Version l) && r > l;
        }
    }
}
