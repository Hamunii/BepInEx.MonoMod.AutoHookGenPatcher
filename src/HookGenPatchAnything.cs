using BepInEx;
using BepInEx.Logging;
using Mono.Cecil;
using MonoMod;
using MonoMod.RuntimeDetour.HookGen;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

namespace HookGenPatchAnything
{
    // [PatcherPluginInfo(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    // internal class Patcher : BasePatcher {
    internal static class Patcher {
        public static string mmhookPath;
        public static ManualLogSource Logger = BepInEx.Logging.Logger.CreateLogSource("HookGenPatchAnything");
        public static void Finish() {
            mmhookPath = Path.Combine(Paths.PluginPath, "MMHOOK");
            if(!Directory.Exists(mmhookPath)){
                Logger.LogError($"No MMHOOK directory found! Please install HookGenPatcher.");
                mmhookPath = null;
            }
        }

        // Load us https://docs.bepinex.dev/articles/dev_guide/preloader_patchers.html
        public static IEnumerable<string> TargetDLLs { get; } = new string[] { };
        public static void Patch(AssemblyDefinition _){ }
    }

    public static class HookGenPatch {
        private static bool skipHashing => true;
        public static bool TargetPlugin(string plugin_GUID) {
            var callingAssembly = System.Reflection.Assembly.GetCallingAssembly();
            if(Patcher.mmhookPath == null){
                Patcher.Logger.LogError($"{callingAssembly.FullName} wants to target Plugin with GUID: '{plugin_GUID}', but no MMHOOK directory found! Please install HookGenPatcher.");
                return false;
            }

            if(!BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey(plugin_GUID)){
                Patcher.Logger.LogWarning($"Plugin with GUID: '{plugin_GUID}' wasn't found!");
                return false;
            }
            var plugin = BepInEx.Bootstrap.Chainloader.PluginInfos[plugin_GUID];

            return RunHookGen(plugin.Location, plugin_GUID, isPlugin: true);
        }

        public static bool TargetGameAssembly(string assemblyName){
            var callingAssembly = System.Reflection.Assembly.GetCallingAssembly();
            if(Patcher.mmhookPath == null){
                Patcher.Logger.LogError($"{callingAssembly.FullName} wants to target '{assemblyName}', but no MMHOOK directory found! Please install HookGenPatcher.");
                return false;
            }
            var assemblyPath = Path.Combine(Paths.ManagedPath, assemblyName);

            if (!File.Exists(assemblyPath))
            {
                Patcher.Logger.LogWarning($"Assembly '{assemblyPath}' wasn't found!");
            }

            return RunHookGen(assemblyPath, assemblyName, isPlugin: false);
        }

        private static bool RunHookGen(string location, string assemblyName, bool isPlugin) {

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
