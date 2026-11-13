using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace DpsMetersInstaller
{
    /// <summary>Le dépôt GitHub : tout ce que l'installeur télécharge vient de là (ou du chargeur de mods déclaré dans games.json).</summary>
    internal static class Repo
    {
        /// <summary>Dépôt GitHub, ou un clone local pour tester : DPS_METERS_REPO=file:///C:/chemin/du/depot/</summary>
        public static readonly string Raw = Environment.GetEnvironmentVariable("DPS_METERS_REPO") ?? "https://raw.githubusercontent.com/Ghermin/dream-dps-meter/main/";
        public const string Page = "https://ghermin.github.io/dream-dps-meter/";
        public const string Playground = "https://ghermin.github.io/";

        /// <summary>Contourne le cache des fichiers bruts de GitHub.</summary>
        public static string Bust => Raw.StartsWith("file:", StringComparison.OrdinalIgnoreCase) ? "" : "?t=" + DateTime.UtcNow.Ticks;

        public static byte[] Get(string url)
        {
            using (var wc = new WebClient())
            {
                wc.Headers[HttpRequestHeader.UserAgent] = "DpsMetersInstaller";
                return wc.DownloadData(url);
            }
        }

        public static string GetText(string url) => Encoding.UTF8.GetString(Get(url)).Trim('\uFEFF', ' ', '\r', '\n', '\t');
    }

    internal sealed class MeterFile
    {
        public string Src, Dst;
    }

    internal sealed class Loader
    {
        public string Name, Url, Conflict, ConflictMessage;
        public string[] Present = new string[0];
    }

    internal enum MeterState { Idle, Working, Done, Error }

    internal sealed class Meter
    {
        public string Id, Name, Game, Exe, Process, LoaderId, Target, VersionPath, Tagline, After;
        public int SteamAppId;
        public List<MeterFile> Files = new List<MeterFile>();
        public Loader Loader;

        // État dans l'installeur
        public string GamePath;
        public string Latest;
        public bool Selected;
        public MeterState State;
        public string Message;

        public bool Found => GamePath != null;
        public string TargetDir => Path.Combine(GamePath, Target);
        public bool Installed => Found && Files.Count > 0 && File.Exists(Path.Combine(TargetDir, Files[0].Dst));
    }

    /// <summary>Lecture de games.json et recherche des jeux dans les bibliothèques Steam.</summary>
    internal static class Catalog
    {
        public static List<Meter> Load()
        {
            var root = (Dictionary<string, object>)new JavaScriptSerializer().DeserializeObject(Repo.GetText(Repo.Raw + "games.json" + Repo.Bust));
            var loaders = new Dictionary<string, Loader>();
            if (root.TryGetValue("loaders", out object l) && l is Dictionary<string, object> ld)
            {
                foreach (var kv in ld)
                {
                    var o = (Dictionary<string, object>)kv.Value;
                    loaders[kv.Key] = new Loader
                    {
                        Name = Str(o, "name"), Url = Str(o, "url"), Conflict = Str(o, "conflict"), ConflictMessage = Str(o, "conflictMessage"),
                        Present = Arr(o, "present").Select(x => (string)x).ToArray(),
                    };
                }
            }

            var meters = new List<Meter>();
            foreach (Dictionary<string, object> o in Arr(root, "meters"))
            {
                var m = new Meter
                {
                    Id = Str(o, "id"), Name = Str(o, "name"), Game = Str(o, "game"), Exe = Str(o, "exe"), Process = Str(o, "process"),
                    LoaderId = Str(o, "loader"), Target = Str(o, "target"), VersionPath = Str(o, "version"),
                    Tagline = Str(o, "tagline"), After = Str(o, "after"),
                    SteamAppId = o.TryGetValue("steamAppId", out object id) ? Convert.ToInt32(id) : 0,
                };
                foreach (Dictionary<string, object> f in Arr(o, "files")) m.Files.Add(new MeterFile { Src = Str(f, "src"), Dst = Str(f, "dst") });
                if (m.LoaderId != null) loaders.TryGetValue(m.LoaderId, out m.Loader);
                meters.Add(m);
            }

            var libraries = SteamLibraries();
            foreach (var m in meters)
            {
                m.GamePath = FindGame(m, libraries);
                m.Selected = m.Found && !m.Installed;
                try { m.Latest = Repo.GetText(Repo.Raw + m.VersionPath + Repo.Bust); }
                catch (Exception) { m.Latest = null; }
            }
            return meters;
        }

        private static string Str(Dictionary<string, object> o, string key) => o.TryGetValue(key, out object v) ? v as string : null;

        private static IEnumerable<object> Arr(Dictionary<string, object> o, string key) =>
            o.TryGetValue(key, out object v) && v is object[] a ? a : Enumerable.Empty<object>();

        public static List<string> SteamLibraries()
        {
            var roots = new List<string>();
            string steam = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string
                ?? Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string
                ?? Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam", "InstallPath", null) as string;
            if (steam != null)
            {
                steam = steam.Replace('/', '\\');
                roots.Add(steam);
                string vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
                try
                {
                    if (File.Exists(vdf))
                    {
                        foreach (Match match in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                            roots.Add(match.Groups[1].Value.Replace(@"\\", @"\"));
                    }
                }
                catch (Exception)
                {
                    // fichier illisible : on garde les emplacements habituels
                }
            }
            roots.AddRange(new[] { @"C:\Program Files (x86)\Steam", @"C:\Program Files\Steam", @"D:\SteamLibrary", @"E:\SteamLibrary", @"D:\Steam", @"E:\Steam" });
            return roots.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static string FindGame(Meter m, List<string> libraries)
        {
            foreach (string root in libraries)
            {
                try
                {
                    string dir = Path.Combine(root, "steamapps", "common", m.Game);
                    if (File.Exists(Path.Combine(dir, m.Exe))) return dir;
                }
                catch (Exception)
                {
                    // lecteur absent ou chemin invalide
                }
            }
            return null;
        }
    }

    /// <summary>Message à montrer tel quel à l'utilisateur.</summary>
    internal sealed class UserError : Exception
    {
        public UserError(string message) : base(message) { }
    }

    /// <summary>Installation, mise à jour et désinstallation d'un DPS meter (appelé hors du thread de l'interface).</summary>
    internal static class Setup
    {
        public static void Install(Meter m, Action<string> report)
        {
            EnsureGameClosed(m);
            if (m.Loader != null) InstallLoader(m, report);

            string target = m.TargetDir;
            Directory.CreateDirectory(target);
            foreach (var f in m.Files)
            {
                report("Téléchargement de " + Path.GetFileName(f.Dst) + "…");
                byte[] data = Repo.Get(Repo.Raw + f.Src + Repo.Bust);
                bool dll = f.Dst.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
                if (data.Length == 0 || (dll && (data.Length < 4096 || data[0] != 'M' || data[1] != 'Z')))
                    throw new UserError("Fichier reçu invalide (" + Path.GetFileName(f.Dst) + "). Réessaie dans un instant.");
                string dst = Inside(target, f.Dst);
                Directory.CreateDirectory(Path.GetDirectoryName(dst));
                try
                {
                    File.WriteAllBytes(dst, data);
                }
                catch (IOException)
                {
                    throw new UserError("Impossible d'écrire " + Path.GetFileName(f.Dst) + " : ferme le jeu et réessaie.");
                }
            }
        }

        public static void Uninstall(Meter m)
        {
            EnsureGameClosed(m);
            if (Directory.Exists(m.TargetDir)) Directory.Delete(m.TargetDir, true);
        }

        private static void EnsureGameClosed(Meter m)
        {
            if (!string.IsNullOrEmpty(m.Process) && System.Diagnostics.Process.GetProcessesByName(m.Process).Length > 0)
                throw new UserError(m.Game + " est ouvert : ferme le jeu puis réessaie.");
        }

        private static void InstallLoader(Meter m, Action<string> report)
        {
            var loader = m.Loader;
            if (!string.IsNullOrEmpty(loader.Conflict) && File.Exists(Path.Combine(m.GamePath, loader.Conflict)))
                throw new UserError(loader.ConflictMessage ?? "Un autre chargeur de mods est déjà installé dans ce jeu.");
            if (loader.Present.All(p => File.Exists(Path.Combine(m.GamePath, p)))) return;

            report("Installation de " + loader.Name + " (chargeur de mods)…");
            byte[] zip = Repo.Get(loader.Url);
            using (var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read))
            {
                foreach (var entry in archive.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name)) continue;
                    string dst = Inside(m.GamePath, entry.FullName.Replace('/', '\\'));
                    Directory.CreateDirectory(Path.GetDirectoryName(dst));
                    entry.ExtractToFile(dst, true);
                }
            }
        }

        /// <summary>Chemin sous <paramref name="root"/>, jamais en dehors.</summary>
        private static string Inside(string root, string relative)
        {
            string full = Path.GetFullPath(Path.Combine(root, relative));
            string baseDir = Path.GetFullPath(root).TrimEnd('\\') + "\\";
            if (!full.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase)) throw new UserError("Chemin invalide : " + relative);
            return full;
        }

        public static void Open(string url)
        {
            try { System.Diagnostics.Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception) { /* pas de navigateur par défaut */ }
        }
    }
}
