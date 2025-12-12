using Modding;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using System.Linq;

namespace AnyRadianceLoader
{
    internal class LoaderMod : Mod, ILocalSettings<LoaderSettings>, IMenuMod
    {
        private const string TargetModsPath = @"C:\Program Files (x86)\Steam\steamapps\common\Hollow Knight\hollow_knight_Data\Managed\Mods";
        private bool _deployedV1;
        private bool _deployedV2;
        private bool _deployedV3;
        private bool _deployedModCommon;
        private bool _deploymentOccurred;

        public LoaderMod() : base("Any Radiance Loader") { }

        public override string GetVersion() => "1.0.0.0";

        public bool ToggleButtonInsideMenu => true;

        public bool Hidden
        {
            get
            {
                var gm = GameManager.instance;
                return gm != null && gm.gameState != GlobalEnums.GameState.MAIN_MENU;
            }
        }

        private bool IsInMainMenu()
        {
            var gm = GameManager.instance;
            return gm != null && gm.gameState == GlobalEnums.GameState.MAIN_MENU;
        }

        public static LoaderSettings Settings = new();

        public void OnLoadLocal(LoaderSettings s) => Settings = s ?? new LoaderSettings();
        public LoaderSettings OnSaveLocal() => Settings;

        public override void Initialize()
        {
            Application.quitting += CleanupBundles;
            AppDomain.CurrentDomain.ProcessExit += (_, _) => CleanupBundles();

            if (Settings.EnableV1)
            {
                TryDeployBundlesV1();
            }

            if (Settings.EnableV2)
            {
                TryDeployBundlesV2();
            }

            if (Settings.EnableV3)
            {
                TryDeployBundlesV3();
            }
        }

        public List<IMenuMod.MenuEntry> GetMenuData(IMenuMod.MenuEntry? _)
        {
            return new List<IMenuMod.MenuEntry>
            {
                new()
                {
                    Name = "Use Any Radiance 1.0",
                    Description = "Copies AnyRadiance 1.0 + ModCommon into Mods and loads it.",
                    Values = new[] { Language.Language.Get("MOH_ON", "MainMenu"), Language.Language.Get("MOH_OFF", "MainMenu") },
                    Saver = i =>
                    {
                        bool enable = i == 0;
                        if (enable && _deploymentOccurred) return;
                        Settings.EnableV1 = enable;
                        if (enable)
                        {
                            TryDeployBundlesV1();
                        }
                        else
                        {
                            CleanupBundles();
                        }
                    },
                    Loader = () => Settings.EnableV1 ? 0 : 1
                },
                new()
                {
                    Name = "Use Any Radiance 2.0",
                    Description = "Copies AnyRadiance 2.0 + ModCommon into Mods and loads it.",
                    Values = new[] { Language.Language.Get("MOH_ON", "MainMenu"), Language.Language.Get("MOH_OFF", "MainMenu") },
                    Saver = i =>
                    {
                        bool enable = i == 0;
                        if (enable && _deploymentOccurred) return;
                        Settings.EnableV2 = enable;
                        if (enable)
                        {
                            TryDeployBundlesV2();
                        }
                        else
                        {
                            CleanupBundles();
                        }
                    },
                    Loader = () => Settings.EnableV2 ? 0 : 1
                },
                new()
                {
                    Name = "Use Any Radiance 3.0",
                    Description = "Copies Any Radiance 3.0 into Mods and loads it.",
                    Values = new[] { Language.Language.Get("MOH_ON", "MainMenu"), Language.Language.Get("MOH_OFF", "MainMenu") },
                    Saver = i =>
                    {
                        bool enable = i == 0;
                        if (enable && _deploymentOccurred) return;
                        Settings.EnableV3 = enable;
                        if (enable)
                        {
                            TryDeployBundlesV3();
                        }
                        else
                        {
                            CleanupBundles();
                        }
                    },
                    Loader = () => Settings.EnableV3 ? 0 : 1
                }
            };
        }

        private void TryDeployBundlesV1()
        {
            if (!IsInMainMenu()) return;
            if (_deployedV1)
            {
                return;
            }

            DeployVariant("Embedded.AnyRadianceFixedDDark.AnyRadiance.dll", "AnyRadianceFixedDDark", "AnyRadiance.dll", ref _deployedV1, "Any Radiance 1.0");
        }

        private void TryDeployBundlesV2()
        {
            if (!IsInMainMenu()) return;
            if (_deployedV2)
            {
                return;
            }

            // resource name stored with underscores in assembly: AnyRadiance2_1._5.AnyRadiance.dll
            DeployVariant("AnyRadianceLoader.Embedded.AnyRadiance2_1._5.AnyRadiance.dll", "AnyRadiance2-1.5", "AnyRadiance.dll", ref _deployedV2, "Any Radiance 2.0");
        }

        private void TryDeployBundlesV3()
        {
            if (!IsInMainMenu()) return;
            if (_deployedV3)
            {
                return;
            }

            DeployVariant("AnyRadianceLoader.Embedded.Any_Radiance_3_0.AnyRadiance.dll", "AnyRadiance 3.0", "AnyRadiance.dll", ref _deployedV3, "Any Radiance 3.0");
        }

        private void DeployVariant(string resourcePath, string folderName, string dllName, ref bool deployedFlag, string logName)
        {
            try
            {
                // Shared dependency
                if (!_deployedModCommon)
                {
                    ExtractBundle("Embedded.ModCommon.ModCommon_1.5.dll", "ModCommon", "ModCommon_1.5.dll");
                    _deployedModCommon = true;
                }

                ExtractBundle(resourcePath, folderName, dllName);
                deployedFlag = true;
                _deploymentOccurred = true;
                TryLoadDeployedMod(folderName, dllName);
                Log($"{logName} deployed.");
            }
            catch (Exception e)
            {
                LogError($"Failed to deploy {logName}: {e}");
            }
        }

        private void CleanupBundles()
        {
            RemoveIfMarked("AnyRadianceFixedDDark");
            RemoveIfMarked("AnyRadiance2-1.5");
            RemoveIfMarked("AnyRadiance 3.0");
            RemoveIfMarked("ModCommon");

            _deployedV1 = false;
            _deployedV2 = false;
            _deployedV3 = false;
            _deployedModCommon = false;
            Log("Any Radiance mods removed.");
        }

        private void ExtractBundle(string resourceName, string folderName, string fileName)
        {
            Assembly asm = Assembly.GetExecutingAssembly();
            string Canon(string s) => System.Text.RegularExpressions.Regex.Replace(s, "[^A-Za-z0-9]", "");
            string targetCanon = Canon(resourceName);
            string folderCanon = Canon(folderName);
            string fullResourceName = asm.GetManifestResourceNames()
                .FirstOrDefault(rn =>
                {
                    string rnCanon = Canon(rn);
                    return rn.EndsWith(resourceName, StringComparison.OrdinalIgnoreCase)
                        || rnCanon.IndexOf(targetCanon, StringComparison.OrdinalIgnoreCase) >= 0
                        || (rnCanon.IndexOf(folderCanon, StringComparison.OrdinalIgnoreCase) >= 0 && rn.EndsWith(fileName, StringComparison.OrdinalIgnoreCase));
                });

            if (fullResourceName == null)
            {
                throw new FileNotFoundException($"Embedded resource not found: {resourceName}");
            }

            string targetDir = Path.Combine(TargetModsPath, folderName);
            Directory.CreateDirectory(targetDir);
            using Stream stream = asm.GetManifestResourceStream(fullResourceName)
                ?? throw new FileNotFoundException($"Resource stream not found: {fullResourceName}");
            string targetFile = Path.Combine(targetDir, fileName);
            using FileStream fs = new FileStream(targetFile, FileMode.Create, FileAccess.Write);
            stream.CopyTo(fs);
            File.WriteAllText(Path.Combine(targetDir, ".loader_marker"), "AnyRadianceLoader");

            // optional extras: copy pdb/readme if present in same folder
            TryExtractOptional(asm, folderCanon, targetDir, "AnyRadiance.pdb");
            TryExtractOptional(asm, folderCanon, targetDir, "README.md");
        }

        private void TryExtractOptional(Assembly asm, string folderCanon, string targetDir, string fileName)
        {
            string Canon(string s) => System.Text.RegularExpressions.Regex.Replace(s, "[^A-Za-z0-9]", "");
            string fileCanon = Canon(fileName);
            string res = asm.GetManifestResourceNames()
                .FirstOrDefault(rn =>
                {
                    string rnCanon = Canon(rn);
                    return rnCanon.IndexOf(folderCanon, StringComparison.OrdinalIgnoreCase) >= 0
                           && rnCanon.EndsWith(fileCanon, StringComparison.OrdinalIgnoreCase);
                });

            if (res == null) return;

            using Stream stream = asm.GetManifestResourceStream(res);
            if (stream == null) return;

            string targetFile = Path.Combine(targetDir, fileName);
            using FileStream fs = new FileStream(targetFile, FileMode.Create, FileAccess.Write);
            stream.CopyTo(fs);
        }

        private void RemoveIfMarked(string folderName)
        {
            string target = Path.Combine(TargetModsPath, folderName);
            string marker = Path.Combine(target, ".loader_marker");
            try
            {
                if (Directory.Exists(target) && File.Exists(marker))
                {
                    Directory.Delete(target, true);
                }
            }
            catch (Exception e)
            {
                LogError($"Failed to clean {target}: {e}");
            }
        }

        private void TryLoadDeployedMod(string folder, string dllName)
        {
            // Load ModCommon first if present
            string modCommonPath = Path.Combine(TargetModsPath, "ModCommon", "ModCommon_1.5.dll");
            if (File.Exists(modCommonPath))
            {
                try { Assembly.LoadFrom(modCommonPath); }
                catch (Exception e) { LogError($"Failed to load ModCommon: {e}"); }
            }

            string anyRadPath = Path.Combine(TargetModsPath, folder, dllName);
            if (!File.Exists(anyRadPath))
            {
                LogError($"AnyRadiance.dll not found at {anyRadPath}");
                return;
            }

            try
            {
                Assembly asm = Assembly.LoadFrom(anyRadPath);
                Type modType = asm.GetTypes().FirstOrDefault(t => typeof(Mod).IsAssignableFrom(t) && !t.IsAbstract);
                if (modType == null)
                {
                    LogError("Mod type not found in AnyRadiance.dll");
                    return;
                }

                var modInstance = (Mod)Activator.CreateInstance(modType);

                // Try to find Initialize(Dictionary<string, Dictionary<string, GameObject>>)
                var initWithDict = modType.GetMethod("Initialize", new Type[] { typeof(Dictionary<string, Dictionary<string, GameObject>>) });
                if (initWithDict != null)
                {
                    initWithDict.Invoke(modInstance, new object[] { new Dictionary<string, Dictionary<string, GameObject>>() });
                }
                else
                {
                    modInstance.Initialize();
                }

                Log($"{folder} initialized via loader.");
            }
            catch (Exception e)
            {
                LogError($"Failed to initialize AnyRadiance: {e}");
            }
        }
    }

    [Serializable]
    internal class LoaderSettings
    {
        public bool EnableV1 = false;
        public bool EnableV2 = false;
        public bool EnableV3 = false;
    }
}
