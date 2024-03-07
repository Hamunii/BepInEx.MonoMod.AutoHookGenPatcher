using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HookGenPatchAnything.Config;
using Mono.Cecil;
using MonoMod;
using MonoMod.RuntimeDetour.HookGen;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace HookGenPatchAnything
{
    // [PatcherPluginInfo(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    // internal class Patcher : BasePatcher {
    internal static class Patcher {
        public static string mmhookPath;
        public static ManualLogSource Logger = BepInEx.Logging.Logger.CreateLogSource("HookGenPatchAnything");
        private static readonly PluginConfig BoundConfig = new PluginConfig(new ConfigFile(Path.Combine(Paths.ConfigPath, PluginInfo.PLUGIN_NAME + ".cfg"), false));

        internal static List<string> toPatchFromGUID;
        public static void Finish() {            
            mmhookPath = Path.Combine(Paths.PluginPath, "MMHOOK");
            if(!Directory.Exists(mmhookPath)){
                Logger.LogError($"No MMHOOK directory found! Please install HookGenPatcher.");
                mmhookPath = null;
            }
            var watch = Stopwatch.StartNew();
            DiscoverPlugins();
            watch.Stop();
            Logger.LogInfo($"Took {watch.ElapsedMilliseconds}ms");
        }

        private static void DiscoverPlugins(){
            HookGenPatch.pluginSearch = new();
            toPatchFromGUID = new();
            var mmhookFolder = Path.Combine(Path.Combine(Paths.PluginPath, "MMHOOK"), "Any");
            bool shouldPatchAll = BoundConfig.PatchAllPlugins.Value;

            foreach (string assemblyPath in Directory.GetFiles(Paths.PluginPath, "*.dll", SearchOption.AllDirectories))
            {
                if (!Path.GetFileName(assemblyPath).StartsWith("MMHOOK_")){

                    // Logger.LogInfo($"File: {assemblyPath}");
                    var fileInfo = new FileInfo(assemblyPath);
  
                    try
                    {
                        using (var pluginAssembly = AssemblyDefinition.ReadAssembly(assemblyPath))
                        {
                            var pluginAttributes = pluginAssembly.MainModule.GetCustomAttributes();
                            
                            // We look for a class named 'MMHOOK_ALL_PLUGINS' to determine whether or not we should HookGen every plugin, for dev purposes.
                            // var hookGenPatchAllCommand = pluginAssembly.MainModule.Types.FirstOrDefault(type => type.Name.Equals("MMHOOK_ALL_PLUGINS"));
                            // if(hookGenPatchAllCommand != null)
                            //     shouldPatchAll = true;
                            
                            var bepInPluginAttr = pluginAttributes.FirstOrDefault(x => x.AttributeType.Name.Equals("BepInPlugin"));
                            CustomAttributeArgument plugin_GUID;
                            if(bepInPluginAttr != null)
                                plugin_GUID = bepInPluginAttr.ConstructorArguments.FirstOrDefault();
                            if(bepInPluginAttr == null || plugin_GUID.Value == null || plugin_GUID.Value.ToString() == "") {
                                // We use the filename as the GUID, because it doesn't have one.
                                var fileName = Path.GetFileName(assemblyPath);
                                // Trim out '.dll'
                                var guid = fileName.Substring(0, fileName.Length - 4);
                                HookGenPatch.pluginSearch.Add(guid, assemblyPath);
                                continue;
                            }
                            HookGenPatch.pluginSearch.Add(plugin_GUID.Value.ToString(), assemblyPath);

                            //if(hookGenPatchAttr == null) {
                            //    continue;
                            //};

                            // Logger.LogInfo($"Found custom attribute {hookGenPatchAttr.AttributeType.Name} in {Path.GetFileName(assemblyPath)}.");
                            var mmhookReferences = pluginAssembly.MainModule.AssemblyReferences.Where(x => x.Name.StartsWith("MMHOOK_")
                                && !x.Name.Equals("MMHOOK_AmazingAssets.TerrainToMesh") // Exclude ones already included in HookGenPatcher
                                && !x.Name.Equals("MMHOOK_Assembly-CSharp")
                                && !x.Name.Equals("MMHOOK_ClientNetworkTransform")
                                && !x.Name.Equals("MMHOOK_DissonanceVoip")
                                && !x.Name.Equals("MMHOOK_Facepunch.Steamworks.Win64")
                                && !x.Name.Equals("MMHOOK_Facepunch Transport for Netcode for GameObjects"));
                            
                            foreach(var reference in mmhookReferences){
                                Logger.LogInfo($"Found reference to {reference.Name} in {Path.GetFileName(assemblyPath)}.");
                                // Trim out 'MMHOOK_' to get the GUID (the rest of the name, excluding '.dll')
                                var guid = reference.Name.Substring(7, reference.Name.Length - 7);
                                toPatchFromGUID.Add(guid);
                            }
                        }
                    }
                    catch (BadImageFormatException)
                    {
                        Logger.LogWarning($"Failed to read {Path.GetFileName(assemblyPath)}, Bad Image Format.");
                    }
                    catch (Exception e)
                    {
                        Logger.LogWarning($"Failed to read {Path.GetFileName(assemblyPath)}, Generic Exception {e}");
                    }
                }
            }
            if(!shouldPatchAll){
                foreach(var plugin_GUID in toPatchFromGUID.Distinct().ToList()){
                    HookGenPatch.RunHookGen(HookGenPatch.pluginSearch[plugin_GUID], plugin_GUID, isPlugin: true);
                }
            }
            else{
                foreach(var entry in HookGenPatch.pluginSearch){
                    HookGenPatch.RunHookGen(entry.Value, entry.Key, isPlugin: true);
                }
                Logger.LogWarning("Patched everything!");
            }
        }

        // Load us https://docs.bepinex.dev/articles/dev_guide/preloader_patchers.html
        public static IEnumerable<string> TargetDLLs { get; } = new string[] { };
        public static void Patch(AssemblyDefinition _) { }
    }

    public static class HookGenPatch {
        private static bool skipHashing => true;
        internal static Dictionary<string, string> pluginSearch;
    
        internal static bool RunHookGen(string location, string assemblyName, bool isPlugin) {

            var mmhookFolder = Path.Combine(Path.Combine(Paths.PluginPath, "MMHOOK"), "Any");
            string fileExtension;
            if(isPlugin)
                fileExtension = ".dll";
            else
                fileExtension = "";
            var mmhookFileName = "MMHOOK_" + assemblyName + fileExtension;
            string pathIn = location;
            string pathOut = Path.Combine(mmhookFolder, mmhookFileName);
            bool shouldCreateDirectory = true;

            foreach (string mmhookFile in Directory.GetFiles(Paths.PluginPath, mmhookFileName, SearchOption.AllDirectories))
            {
                if (Path.GetFileName(mmhookFile).Equals(mmhookFileName))
                {
                    pathOut = mmhookFile;
                    Patcher.Logger.LogInfo($"Previous location found for '{mmhookFileName}'. Using that location to save instead.");
                    shouldCreateDirectory = false;
                    break;
                }
            }
            if (shouldCreateDirectory)
            {
                Directory.CreateDirectory(mmhookFolder);
            }

            var fileInfo = new FileInfo(pathIn);
            var size = fileInfo.Length;
            long hash = 0;

            if (File.Exists(pathOut))
            {
                try
                {
                    using (var oldMM = AssemblyDefinition.ReadAssembly(pathOut))
                    {
                        bool mmSizeHash = oldMM.MainModule.GetType("BepHookGen.size" + size) != null;
                        if (mmSizeHash)
                        {
                            if (skipHashing)
                            {
                                Patcher.Logger.LogInfo("Already ran for this version, reusing that file.");
                                return true;
                            }
                            hash = fileInfo.makeHash();
                            bool mmContentHash = oldMM.MainModule.GetType("BepHookGen.content" + hash) != null;
                            if (mmContentHash)
                            {
                                Patcher.Logger.LogInfo("Already ran for this version, reusing that file.");
                                return true;
                            }
                        }
                    }
                }
                catch (BadImageFormatException)
                {
                    Patcher.Logger.LogWarning($"Failed to read {Path.GetFileName(pathOut)}, probably corrupted, remaking one.");
                }
            }

            Environment.SetEnvironmentVariable("MONOMOD_HOOKGEN_PRIVATE", "1");
            Environment.SetEnvironmentVariable("MONOMOD_DEPENDENCY_MISSING_THROW", "0");

            using (MonoModder mm = new MonoModder()
            {
                InputPath = pathIn,
                OutputPath = pathOut,
                ReadingMode = ReadingMode.Deferred
            })
            {
                (mm.AssemblyResolver as BaseAssemblyResolver)?.AddSearchDirectory(Paths.BepInExAssemblyDirectory);

                mm.Read();

                mm.MapDependencies();

                if (File.Exists(pathOut))
                {
                    Patcher.Logger.LogDebug($"Clearing {pathOut}");
                    File.Delete(pathOut);
                }

                Patcher.Logger.LogInfo("Starting HookGenerator");
                HookGenerator gen = new HookGenerator(mm, Path.GetFileName(pathOut));

                using (ModuleDefinition mOut = gen.OutputModule)
                {
                    gen.Generate();
                    mOut.Types.Add(new TypeDefinition("BepHookGen", "size" + size, TypeAttributes.Class | TypeAttributes.Public, mOut.TypeSystem.Object));
                    if (!skipHashing)
                    {
                        mOut.Types.Add(new TypeDefinition("BepHookGen", "content" + (hash == 0 ? fileInfo.makeHash() : hash), TypeAttributes.Class | TypeAttributes.Public, mOut.TypeSystem.Object));
                    }
                    mOut.Write(pathOut);
                }

                Patcher.Logger.LogInfo("Done.");
            }

            return true;
        }

        private static long makeHash(this FileInfo fileInfo)
        {
            var fileStream = fileInfo.OpenRead();
            byte[] hashbuffer = null;
            using (MD5 md5 = new MD5CryptoServiceProvider())
            {
                hashbuffer = md5.ComputeHash(fileStream);
            }
            long hash = BitConverter.ToInt64(hashbuffer, 0);
            return hash != 0 ? hash : 1;
        }
    }
}
