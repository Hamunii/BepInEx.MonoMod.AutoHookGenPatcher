using BepInEx;
using Mono.Cecil;
using MonoMod;
using MonoMod.RuntimeDetour.HookGen;
using System;
using System.IO;
using System.Security.Cryptography;

namespace AutoHookGenPatcher;

public static class HookGenPatch {
    private static bool skipHashing => false;
    private static readonly object hookGenLock = new object();
    internal static bool RunHookGen(CachedAssemblyInfo plugin) {
        var assemblyName = plugin.GUID;
        var location = plugin.Path;
        
        string mmhookFolder = Patcher.mmhookPath;

        if(plugin.Path.Contains(Paths.ManagedPath))
            mmhookFolder = Path.Combine(Patcher.mmhookPath, "Managed");
        else if(plugin.Path.Contains(Paths.PluginPath))
            mmhookFolder = Path.Combine(Patcher.mmhookPath, "plugins");
        else if(plugin.Path.Contains(Paths.BepInExAssemblyDirectory))
            mmhookFolder = Path.Combine(Patcher.mmhookPath, "core");
        else if(plugin.Path.Contains(Paths.PatcherPluginPath))
            mmhookFolder = Path.Combine(Patcher.mmhookPath, "patchers");

        var fileExtension = ".dll";
        var mmhookFileName = "MMHOOK_" + assemblyName + fileExtension;
        string pathIn = location;
        string pathOut = Path.Combine(mmhookFolder, mmhookFileName);
        bool shouldCreateDirectory = true;

        foreach (string mmhookFile in Patcher.mmHookFiles)
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
                            Patcher.ExtendedLogging($"[{nameof(RunHookGen)}] Already ran for latest version of {mmhookFileName}");
                            return true;
                        }
                        hash = fileInfo.makeHash();
                        bool mmContentHash = oldMM.MainModule.GetType("BepHookGen.content" + hash) != null;
                        if (mmContentHash)
                        {
                            Patcher.ExtendedLogging($"[{nameof(RunHookGen)}] Already ran for latest version of {mmhookFileName}");
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

        {
            // Doing this instead of using(){} because MonoModder creation needs to be locked, but 
            // we want to do the rest asynchronously, which I don't know how else I would do it.
            // This is basically what using(){} does, at least according to https://stackoverflow.com/a/75483
            MonoModder mm = null!;
            try{
                lock(hookGenLock){
                    mm = new MonoModder(){
                        InputPath = pathIn,
                        OutputPath = pathOut,
                        ReadingMode = ReadingMode.Deferred
                    };

                    (mm.AssemblyResolver as BaseAssemblyResolver)?.AddSearchDirectory(Paths.BepInExAssemblyDirectory);

                    mm.Read();

                    mm.MapDependencies();
                    
                    if (File.Exists(pathOut))
                    {
                        Patcher.Logger.LogDebug($"Clearing {pathOut}");
                        File.Delete(pathOut);
                    }
                }

                HookGenerator gen;
                Patcher.Logger.LogInfo($"[{nameof(RunHookGen)}] Starting HookGenerator for {mmhookFileName}");
                lock(hookGenLock){
                    gen = new HookGenerator(mm, Path.GetFileName(pathOut));
                }

                using (ModuleDefinition mOut = gen.OutputModule)
                {
                    gen.Generate();
                    mOut.Types.Add(new TypeDefinition("BepHookGen", "size" + size, TypeAttributes.Class | TypeAttributes.Public, mOut.TypeSystem.Object));
                    if (!skipHashing)
                    {
                        mOut.Types.Add(new TypeDefinition("BepHookGen", "content" + (hash == 0 ? fileInfo.makeHash() : hash), TypeAttributes.Class | TypeAttributes.Public, mOut.TypeSystem.Object));
                    }
                    lock(hookGenLock){
                        mOut.Write(pathOut);
                    }
                }

                Patcher.Logger.LogInfo($"[{nameof(RunHookGen)}] HookGen done for {mmhookFileName}");
            }
            catch (Exception e){
                Patcher.Logger.LogInfo($"[{nameof(RunHookGen)}] Error in HookGen for {mmhookFileName}: {e}");
            }
            finally{
                mm?.Dispose();
            }
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