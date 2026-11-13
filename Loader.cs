using Modding;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using UnityEngine;

namespace AnyRadianceLoader
{
    public enum AnyRadianceVersion
    {
        Off = 0,
        V1 = 1,
        V2 = 2,
        V3 = 3
    }

    public sealed class LoaderGlobalSettings
    {
        public AnyRadianceVersion Selected = AnyRadianceVersion.Off;
        public bool Adopted;
    }

    internal sealed class Variant
    {
        public Variant(AnyRadianceVersion version, string label, string folder, bool needsModCommon, string? requiredMod, params string[] files)
        {
            Version = version;
            Label = label;
            Folder = folder;
            NeedsModCommon = needsModCommon;
            RequiredMod = requiredMod;
            Files = files;
        }

        public AnyRadianceVersion Version { get; }
        public string Label { get; }
        public string Folder { get; }
        public bool NeedsModCommon { get; }
        public string? RequiredMod { get; }
        public string[] Files { get; }

        public string Resource(string file) => "v" + (int)Version + "/" + file;
    }

    internal static class Catalog
    {
        public const string AssemblyName = "AnyRadiance";
        public const string ModCommonFolder = "ModCommon";
        public const string ModCommonFile = "ModCommon_1.5.dll";
        public const string ModCommonResource = "ModCommon/ModCommon_1.5.dll";

        public static readonly Variant[] Variants =
        {
            new Variant(AnyRadianceVersion.V1, "1.0", "AnyRadianceFixedDDark", true, null, "AnyRadiance.dll"),
            new Variant(AnyRadianceVersion.V2, "2.0", "AnyRadiance2-1.5", true, null, "AnyRadiance.dll", "AnyRadiance.pdb"),
            new Variant(AnyRadianceVersion.V3, "3.0", "AnyRadiance 3.0", false, "Vasi", "AnyRadiance.dll", "AnyRadiance.pdb")
        };

        public static Variant? Find(AnyRadianceVersion version) => Variants.FirstOrDefault(v => v.Version == version);

        public static string Label(AnyRadianceVersion version) => Find(version)?.Label ?? "Off";

        public static AnyRadianceVersion? Classify(Assembly assembly)
        {
            string? location = null;
            try
            {
                location = assembly.Location;
            }
            catch
            {
            }

            if (!string.IsNullOrEmpty(location) && File.Exists(location))
            {
                string hash = HashFile(location!);
                foreach (Variant variant in Variants)
                {
                    if (hash == HashResource(variant.Resource(variant.Files[0])))
                    {
                        return variant.Version;
                    }
                }
            }

            return ClassifyByTypes(assembly);
        }

        public static AnyRadianceVersion? ClassifyByTypes(Assembly assembly)
        {
            if (assembly.GetType("AnyRadiance.Radiance.Radiance", false) != null)
            {
                return AnyRadianceVersion.V3;
            }

            if (assembly.GetType("Abs", false) != null)
            {
                return AnyRadianceVersion.V1;
            }

            if (assembly.GetType("Radiance", false) != null)
            {
                return AnyRadianceVersion.V2;
            }

            return null;
        }

        private static readonly Dictionary<string, string> ResourceHashes = new Dictionary<string, string>(StringComparer.Ordinal);

        private static string HashResource(string resource)
        {
            if (ResourceHashes.TryGetValue(resource, out string? known))
            {
                return known;
            }

            using Stream? stream = typeof(Catalog).Assembly.GetManifestResourceStream(resource);
            using SHA256 sha = SHA256.Create();
            string hash = stream == null ? string.Empty : BitConverter.ToString(sha.ComputeHash(stream));
            ResourceHashes[resource] = hash;
            return hash;
        }

        private static string HashFile(string path)
        {
            using FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using SHA256 sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(stream));
        }
    }

    internal sealed class Switcher
    {
        public const string Marker = ".anyradianceloader";
        public const string LegacyMarker = ".loader_marker";
        public const string RemoveRequest = ".anyradianceloader-remove";
        private const string RemovedSuffix = ".removed";

        private readonly string modsDir;
        private readonly Func<string, Stream?> openResource;
        private readonly Action<string> log;

        public Switcher(string modsDir, Func<string, Stream?> openResource, Action<string> log)
        {
            this.modsDir = modsDir;
            this.openResource = openResource;
            this.log = log;
        }

        public List<string> Warnings { get; } = new List<string>();

        public List<string> PendingRemovals { get; } = new List<string>();

        public static bool IsOwned(string dir) =>
            File.Exists(Path.Combine(dir, Marker)) || File.Exists(Path.Combine(dir, LegacyMarker));

        public static string RemovalArguments(string dir)
        {
            string folder = Quote(dir);
            string request = Quote(Path.Combine(dir, RemoveRequest));
            string script = $"while (Test-Path -LiteralPath {request}) {{ Get-ChildItem -LiteralPath {folder} -Filter *.dll | Remove-Item -Force -ErrorAction SilentlyContinue; if (-not (Get-ChildItem -LiteralPath {folder} -Filter *.dll)) {{ Remove-Item -LiteralPath {folder} -Recurse -Force -ErrorAction SilentlyContinue; break }}; Start-Sleep -Seconds 3 }}";
            return $"-NoProfile -NonInteractive -WindowStyle Hidden -Command \"{script}\"";
        }

        private static string Quote(string path) => "'" + path.Replace("'", "''") + "'";

        public void Apply(AnyRadianceVersion selected)
        {
            Warnings.Clear();
            PendingRemovals.Clear();
            Variant? chosen = Catalog.Find(selected);

            foreach (Variant variant in Catalog.Variants)
            {
                string dir = Path.Combine(modsDir, variant.Folder);
                if (variant.Version == selected)
                {
                    Install(variant, dir);
                }
                else if (Directory.Exists(dir) && !IsOwned(dir) && HasContent(dir))
                {
                    Warnings.Add($"{variant.Folder} was not installed by the loader, so it stays and loads together with the chosen version. Remove that folder by hand.");
                }
                else
                {
                    Remove(dir);
                }
            }

            if (chosen == null)
            {
                return;
            }

            if (chosen.NeedsModCommon)
            {
                InstallModCommon();
            }

            if (chosen.RequiredMod != null && !Directory.Exists(Path.Combine(modsDir, chosen.RequiredMod)))
            {
                Warnings.Add($"AnyRadiance {chosen.Label} needs the {chosen.RequiredMod} mod. Install it with Lumafly.");
            }
        }

        private void Install(Variant variant, string dir)
        {
            if (Directory.Exists(dir) && !IsOwned(dir) && HasContent(dir))
            {
                string dll = Path.Combine(dir, variant.Files[0]);
                if (File.Exists(dll) && SameAsResource(dll, variant.Resource(variant.Files[0])))
                {
                    log($"AnyRadiance {variant.Label} is already in {variant.Folder} (installed by hand), the loader leaves it as it is.");
                    return;
                }

                Warnings.Add($"{variant.Folder} holds files that the loader did not install. Remove that folder to let the loader install AnyRadiance {variant.Label}.");
                return;
            }

            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, Marker), "AnyRadianceLoader");
            DeleteQuietly(Path.Combine(dir, RemoveRequest));
            foreach (string removed in Directory.GetFiles(dir, "*" + RemovedSuffix))
            {
                DeleteQuietly(removed);
            }

            foreach (string file in variant.Files)
            {
                WriteFile(Path.Combine(dir, file), variant.Resource(file));
            }

            log($"AnyRadiance {variant.Label} is in place in {variant.Folder}.");
        }

        private void InstallModCommon()
        {
            string dir = Path.Combine(modsDir, Catalog.ModCommonFolder);
            string target = Path.Combine(dir, Catalog.ModCommonFile);
            if (Directory.Exists(dir) && !IsOwned(dir) && HasContent(dir))
            {
                if (!Directory.GetFiles(dir, "*.dll").Any())
                {
                    Warnings.Add($"{Catalog.ModCommonFolder} has no dll in it. Reinstall ModCommon with Lumafly.");
                }

                return;
            }

            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, Marker), "AnyRadianceLoader");
            WriteFile(target, Catalog.ModCommonResource);
        }

        private void Remove(string dir)
        {
            if (!Directory.Exists(dir) || !IsOwned(dir))
            {
                return;
            }

            string name = Path.GetFileName(dir);
            bool blocked = false;
            foreach (string path in Directory.GetFiles(dir))
            {
                if (IsControlFile(path) || TryDelete(path))
                {
                    continue;
                }

                if (!path.EndsWith(RemovedSuffix, StringComparison.OrdinalIgnoreCase)
                    && path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                    && TryMove(path, path + RemovedSuffix))
                {
                    continue;
                }

                if (path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                {
                    blocked = true;
                }
            }

            bool leftovers = Directory.GetFileSystemEntries(dir).Any(entry => !IsControlFile(entry));
            if (!leftovers)
            {
                try
                {
                    Directory.Delete(dir, true);
                    log($"Removed {name}.");
                    return;
                }
                catch (Exception e)
                {
                    log($"Could not remove the empty folder {name}: {e.Message}");
                    return;
                }
            }

            File.WriteAllText(Path.Combine(dir, RemoveRequest), "AnyRadianceLoader");
            if (blocked)
            {
                PendingRemovals.Add(dir);
                log($"{name} is in use by the game and will be removed after the game closes.");
            }
            else
            {
                log($"{name} is switched off; its leftover files are removed at the next start.");
            }
        }

        private void WriteFile(string target, string resource)
        {
            byte[]? data = ReadResource(resource);
            if (data == null)
            {
                Warnings.Add($"The loader is missing its copy of {resource}.");
                return;
            }

            if (File.Exists(target) && Hash(File.ReadAllBytes(target)) == Hash(data))
            {
                return;
            }

            string temp = target + ".new";
            File.WriteAllBytes(temp, data);
            if (File.Exists(target) && !TryDelete(target) && !TryMove(target, target + RemovedSuffix))
            {
                DeleteQuietly(temp);
                Warnings.Add($"{target} is in use by the game. Restart the game and choose the version again.");
                return;
            }

            File.Move(temp, target);
        }

        private bool SameAsResource(string path, string resource)
        {
            byte[]? data = ReadResource(resource);
            return data != null && Hash(File.ReadAllBytes(path)) == Hash(data);
        }

        private byte[]? ReadResource(string resource)
        {
            using Stream? stream = openResource(resource);
            if (stream == null)
            {
                return null;
            }

            using MemoryStream copy = new MemoryStream();
            stream.CopyTo(copy);
            return copy.ToArray();
        }

        private static string Hash(byte[] data)
        {
            using SHA256 sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(data));
        }

        private static bool HasContent(string dir) =>
            Directory.GetFileSystemEntries(dir).Any(entry => !IsControlFile(entry));

        private static bool IsControlFile(string path)
        {
            string name = Path.GetFileName(path);
            return name == Marker || name == LegacyMarker || name == RemoveRequest;
        }

        private static bool TryDelete(string path)
        {
            try
            {
                File.Delete(path);
                return !File.Exists(path);
            }
            catch
            {
                return false;
            }
        }

        private static bool TryMove(string from, string to)
        {
            try
            {
                if (File.Exists(to))
                {
                    File.Delete(to);
                }

                File.Move(from, to);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void DeleteQuietly(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }
    }

    internal sealed class LoaderMod : Mod, IGlobalSettings<LoaderGlobalSettings>, IMenuMod
    {
        private static readonly string[] SelectorValues = { "Off", "1.0", "2.0", "3.0" };

        private readonly HashSet<string> scheduledRemovals = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private LoaderGlobalSettings settings = new LoaderGlobalSettings();
        private Switcher? switcher;
        private string loadedNow = "Off";
        private string status = string.Empty;

        public LoaderMod() : base("Any Radiance Loader")
        {
        }

        public bool ToggleButtonInsideMenu => false;

        public override string GetVersion() => typeof(LoaderMod).Assembly.GetName().Version.ToString();

        public void OnLoadGlobal(LoaderGlobalSettings loaded) => settings = loaded ?? new LoaderGlobalSettings();

        public LoaderGlobalSettings OnSaveGlobal() => settings;

        public override void Initialize()
        {
            List<(AnyRadianceVersion? Version, string Folder)> loaded = FindLoadedAnyRadiance();
            loadedNow = Describe(loaded);
            if (loaded.Count > 1)
            {
                LogWarn($"Several AnyRadiance versions are loaded at once: {loadedNow}.");
            }

            if (!settings.Adopted)
            {
                settings.Adopted = true;
                if (loaded.Count == 1 && loaded[0].Version is AnyRadianceVersion current)
                {
                    settings.Selected = current;
                }
            }

            string? modsDir = FindModsDirectory();
            if (modsDir == null)
            {
                LogError("Could not find the Mods folder.");
                return;
            }

            switcher = new Switcher(modsDir, name => typeof(LoaderMod).Assembly.GetManifestResourceStream(name), Log);
            Apply();
            Log($"Loaded now: {loadedNow}. Selected: {Catalog.Label(settings.Selected)}.");
        }

        public List<IMenuMod.MenuEntry> GetMenuData(IMenuMod.MenuEntry? toggleButtonEntry)
        {
            return new List<IMenuMod.MenuEntry>
            {
                new IMenuMod.MenuEntry
                {
                    Name = "AnyRadiance version",
                    Description = "Takes effect after you restart the game.",
                    Values = SelectorValues,
                    Saver = index => Select((AnyRadianceVersion)index),
                    Loader = () => (int)settings.Selected
                },
                new IMenuMod.MenuEntry
                {
                    Name = "Loaded now",
                    Description = status.Length > 0 ? status : "The AnyRadiance version the game loaded at startup.",
                    Values = new[] { loadedNow },
                    Saver = _ => { },
                    Loader = () => 0
                }
            };
        }

        private void Select(AnyRadianceVersion version)
        {
            if (version == settings.Selected)
            {
                return;
            }

            settings.Selected = version;
            Apply();
            SaveGlobalSettings();
            Log($"Selected AnyRadiance {Catalog.Label(version)}. Restart the game to load it.");
        }

        private void Apply()
        {
            if (switcher == null)
            {
                return;
            }

            try
            {
                switcher.Apply(settings.Selected);
            }
            catch (Exception e)
            {
                LogError($"Switching AnyRadiance failed: {e}");
                return;
            }

            foreach (string warning in switcher.Warnings)
            {
                LogWarn(warning);
            }

            scheduledRemovals.IntersectWith(switcher.PendingRemovals);
            foreach (string dir in switcher.PendingRemovals)
            {
                ScheduleRemoval(dir);
            }

            status = string.Join(" ", switcher.Warnings.ToArray());
        }

        private void ScheduleRemoval(string dir)
        {
            if (Application.platform != RuntimePlatform.WindowsPlayer || !scheduledRemovals.Add(dir))
            {
                return;
            }

            try
            {
                ProcessStartInfo start = new ProcessStartInfo("powershell.exe", Switcher.RemovalArguments(dir))
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                Process.Start(start)?.Dispose();
                Log($"{Path.GetFileName(dir)} will be removed after the game closes.");
            }
            catch (Exception e)
            {
                LogWarn($"Could not schedule the removal of {dir}: {e.Message}. Delete that folder by hand after closing the game.");
            }
        }

        private static List<(AnyRadianceVersion? Version, string Folder)> FindLoadedAnyRadiance()
        {
            List<(AnyRadianceVersion? Version, string Folder)> result = new List<(AnyRadianceVersion? Version, string Folder)>();
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                string? name;
                try
                {
                    name = assembly.GetName().Name;
                }
                catch
                {
                    continue;
                }

                if (name != Catalog.AssemblyName)
                {
                    continue;
                }

                string folder = "?";
                try
                {
                    folder = Path.GetFileName(Path.GetDirectoryName(assembly.Location)) ?? "?";
                }
                catch
                {
                }

                AnyRadianceVersion? version = null;
                try
                {
                    version = Catalog.Classify(assembly);
                }
                catch
                {
                }

                result.Add((version, folder));
            }

            return result;
        }

        private static string Describe(List<(AnyRadianceVersion? Version, string Folder)> loaded)
        {
            if (loaded.Count == 0)
            {
                return "Off";
            }

            string text = string.Join(" + ", loaded.Select(item => item.Version is AnyRadianceVersion version ? Catalog.Label(version) : "unknown (" + item.Folder + ")").ToArray());
            return loaded.Count > 1 ? text + " (conflict)" : text;
        }

        private static string? FindModsDirectory()
        {
            try
            {
                string? dir = Path.GetDirectoryName(typeof(LoaderMod).Assembly.Location);
                for (int depth = 0; dir != null && depth < 3; depth++)
                {
                    if (string.Equals(Path.GetFileName(dir), "Mods", StringComparison.OrdinalIgnoreCase))
                    {
                        return dir;
                    }

                    dir = Path.GetDirectoryName(dir);
                }
            }
            catch
            {
            }

            string fallback = Path.Combine(Path.Combine(Application.dataPath, "Managed"), "Mods");
            return Directory.Exists(fallback) ? fallback : null;
        }
    }
}
