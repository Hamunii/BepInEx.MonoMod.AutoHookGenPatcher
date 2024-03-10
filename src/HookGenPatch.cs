using BepInEx;
using Mono.Cecil;
using MonoMod;
using MonoMod.RuntimeDetour.HookGen;
using System;
using System.IO;
using System.Security.Cryptography;

namespace AutoHookGenPatcher
{
    public static class HookGenPatch {
        private static bool skipHashing => true;
        internal static bool RunHookGen(CachedAssemblyInfo plugin) {
            var assemblyName = plugin.GUID;
            var location = plugin.Path;
            var mmhookFolder = Path.Combine(Path.Combine(Paths.PluginPath, "MMHOOK"), "Any");
            var fileExtension = ".dll";
            var mmhookFileName = "MMHOOK_" + assemblyName + fileExtension;
            string pathIn = location;
            string pathOut = Path.Combine(mmhookFolder, mmhookFileName);
            bool shouldCreateDirectory = true;

            foreach (string mmhookFile in Directory.GetFiles(Paths.PluginPath, mmhookFileName, SearchOption.AllDirectories))
            {
                if (Path.GetFileName(mmhookFile).Equals(mmhookFileName))
                {
                    pathOut = mmhookFile;
                    // Patcher.Logger.LogInfo($"Previous location found for '{mmhookFileName}'. Using that location to save instead.");
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
                                Patcher.Logger.LogInfo($"In HookGen, Already ran for latest version of '{mmhookFileName}'.");
                                return true;
                            }
                            hash = fileInfo.makeHash();
                            bool mmContentHash = oldMM.MainModule.GetType("BepHookGen.content" + hash) != null;
                            if (mmContentHash)
                            {
                                Patcher.Logger.LogInfo($"In HookGen, Already ran for latest version of '{mmhookFileName}'.");
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
                Patcher.Logger.LogInfo($"Starting HookGenerator for '{mmhookFileName}'");
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
            byte[] hashbuffer = null!;
            using (MD5 md5 = new MD5CryptoServiceProvider())
            {
                hashbuffer = md5.ComputeHash(fileStream);
            }
            long hash = BitConverter.ToInt64(hashbuffer, 0);
            return hash != 0 ? hash : 1;
        }
    }
}
