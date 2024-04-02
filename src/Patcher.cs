using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using AutoHookGenPatcher.Config;
using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using System.Threading.Tasks;

namespace AutoHookGenPatcher
{
    // [PatcherPluginInfo(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    // internal class Patcher : BasePatcher {
    internal static class Patcher {
        internal static string? mmhookPath;
        internal static ManualLogSource Logger = BepInEx.Logging.Logger.CreateLogSource(PluginInfo.PLUGIN_NAME);
        private static readonly PluginConfig BoundConfig = new PluginConfig(new ConfigFile(Path.Combine(Paths.ConfigPath, PluginInfo.PLUGIN_NAME + ".cfg"), false));
        internal static List<Task> hookGenTasks = new();
        internal static List<string> mmhookOverrides = new();
        internal static string? cacheLocation;
        internal static bool isCacheUpdateNeeded = false;
        public static void Initialize() {
            if(BoundConfig.ExtendedLogging.Value)
            {
                Logger.LogInfo($"[{nameof(Initialize)}] Extended Logging is enabled.");
                if(BoundConfig.GenerateForAllPlugins.Value)
                    Logger.LogInfo($"[{nameof(Initialize)}] GenerateForAllPlugins is enabled.");
            }

            mmhookOverrides.Add("Assembly-CSharp");

            mmhookPath = Path.Combine(Paths.PluginPath, "MMHOOK");
            cacheLocation = Path.Combine(Paths.CachePath, "AutoHookGenPatcher_MMHOOK_Cache.xml");

            if(!Directory.Exists(mmhookPath))
                Directory.CreateDirectory(mmhookPath);

            var watch = Stopwatch.StartNew();
            
            Begin();

            watch.Stop();
            
            ExtendedLogging($"[{nameof(Initialize)}] Took {watch.ElapsedMilliseconds}ms");
        }

        private static void ExtendedLogging(string message){
            if(BoundConfig.ExtendedLogging.Value) Logger.LogInfo(message);
        }

        private static void Begin(){
            var cachedPlugins = TryLoadCache();

            var currentPlugins = GetPlugins(cachedPlugins).ToList();

            for(int i = 0; i < currentPlugins.Count; i++)
            {
                var plugin = currentPlugins[i];
                if(!IsPluginInfoUpToDate(plugin))
                {
                    ReadPluginInfoAndMMHOOKReferences(plugin);
                    isCacheUpdateNeeded = true;
                }
            }

            var mmHookPaths = Directory.GetFiles(Paths.PluginPath, "*.dll", SearchOption.AllDirectories).Where(name => Path.GetFileName(name).StartsWith("MMHOOK_")).ToList();
            int alreadyHadMMHOOKCount = 0;

            for(int i = 0; i < currentPlugins.Count; i++)
            {
                var plugin = currentPlugins[i];
                if(plugin.AlreadyHasMMHOOK){
                    alreadyHadMMHOOKCount++;
                    // ExtendedLogging($"[{nameof(Begin)}] Assembly Already Has MMHOOK: " + plugin.GUID);
                    continue;
                }

                if(BoundConfig.GenerateForAllPlugins.Value || mmhookOverrides.Contains(plugin.GUID) || currentPlugins.SelectMany(plugins => plugins.References).Contains(plugin.GUID))
                {
                    var hookGenTask = new Task(() => StartHookGen(plugin, mmHookPaths));
                    hookGenTask.Start();
                    hookGenTasks.Add(hookGenTask);
                }
            }


            if(BoundConfig.GenerateForAllPlugins.Value && BoundConfig.DisableGenerateForAllPlugins.Value)
            {
                Logger.LogInfo($"[{nameof(Begin)}] DisableGenerateForAllPlugins is enabled, disabling GenerateForAllPlugins.");
                BoundConfig.GenerateForAllPlugins.Value = false;
            }
            
            ExtendedLogging($"[{nameof(Begin)}] Already Has MMHOOK: {alreadyHadMMHOOKCount} Out of {currentPlugins.Count} Assemblies");
            ExtendedLogging($"[{nameof(Begin)}] Waiting for {hookGenTasks.Count} HookGen Task{(hookGenTasks.Count == 1 ? "" : "s")} to finish.");

            hookGenTasks.ForEach(x => x.Wait());
            
            if(isCacheUpdateNeeded || cachedPlugins == null)
                WriteCache(currentPlugins);
        }

        private static void StartHookGen(CachedAssemblyInfo plugin, List<string> mmhookPaths){
            if(HookGenPatch.RunHookGen(plugin, mmhookPaths)){
                lock(plugin){
                    plugin.AlreadyHasMMHOOK = true;
                }
                isCacheUpdateNeeded = true;
            }
        }

        private static IEnumerable<CachedAssemblyInfo> GetPlugins(List<CachedAssemblyInfo>? cachedAssemblies){
            List<string> paths = new();
            Directory.GetFiles(Paths.PluginPath, "*.dll", SearchOption.AllDirectories).ToList().ForEach(paths.Add);
            Directory.GetFiles(Paths.ManagedPath, "*.dll", SearchOption.AllDirectories).ToList().ForEach(paths.Add);

            var assemblyPaths = paths.Where(name => !Path.GetFileName(name).StartsWith("MMHOOK_"));
            var mmHookPaths = paths.Where(name => Path.GetFileName(name).StartsWith("MMHOOK_")).Select(name => Path.GetFileName(name));

            foreach (string assemblyPath in assemblyPaths)
            {
                CachedAssemblyInfo? cachedAssembly = cachedAssemblies?.Find(ass => ass.Path.Equals(assemblyPath));

                if(cachedAssembly == null)
                {
                    ExtendedLogging($"[{nameof(GetPlugins)}] Found New Assembly: " + Path.GetFileName(assemblyPath));
                    yield return new CachedAssemblyInfo(null!, assemblyPath, 0, false, null);
                }
                else
                {
                    var hasMMHOOKinReality = mmHookPaths.Contains($"MMHOOK_{cachedAssembly.GUID}.dll");
                    
                    if(cachedAssembly.AlreadyHasMMHOOK != hasMMHOOKinReality)
                        isCacheUpdateNeeded = true;

                    bool needsInspection = cachedAssembly.AlreadyHasMMHOOK && !hasMMHOOKinReality;

                    // ExtendedLogging($"[{nameof(GetPlugins)}] Found Known Assembly: " + Path.GetFileName(assemblyPath));
                    yield return new CachedAssemblyInfo(cachedAssembly.GUID!, assemblyPath, needsInspection ? 0 : cachedAssembly.DateModified, hasMMHOOKinReality, cachedAssembly.References);
                }
            }
        }

        private static bool IsPluginInfoUpToDate(CachedAssemblyInfo cachedAssemblyInfo)
        {
            var currentDateModified = File.GetLastWriteTime(cachedAssemblyInfo.Path).Ticks;

            if(currentDateModified == cachedAssemblyInfo.DateModified)
            {
                // ExtendedLogging($"[{nameof(IsPluginInfoUpToDate)}] Assembly is up-to-date: " + Path.GetFileName(cachedAssemblyInfo.Path));
                return true;
            }

            // ExtendedLogging($"[{nameof(IsPluginInfoUpToDate)}] Cached Assembly Info is Not up-to-date! " + Path.GetFileName(cachedAssemblyInfo.Path));
            cachedAssemblyInfo.DateModified = currentDateModified;
            return false;
        }

        private static void ReadPluginInfoAndMMHOOKReferences(CachedAssemblyInfo cachedAssemblyInfo){
            using (var pluginAssembly = AssemblyDefinition.ReadAssembly(cachedAssemblyInfo.Path))
            {
                var plugin_GUID = GetPluginGUID(cachedAssemblyInfo.Path, pluginAssembly);
                cachedAssemblyInfo.GUID = plugin_GUID;

                ExtendedLogging($"[{nameof(ReadPluginInfoAndMMHOOKReferences)}] Starting Reading: " + plugin_GUID);

                var mmhookReferences = pluginAssembly.MainModule.AssemblyReferences.Where(x => x.Name.StartsWith("MMHOOK_"));
                    // && !x.Name.Equals("MMHOOK_AmazingAssets.TerrainToMesh") // Exclude ones already included in HookGenPatcher
                    // && !x.Name.Equals("MMHOOK_Assembly-CSharp")
                    // && !x.Name.Equals("MMHOOK_ClientNetworkTransform")
                    // && !x.Name.Equals("MMHOOK_DissonanceVoip")
                    // && !x.Name.Equals("MMHOOK_Facepunch.Steamworks.Win64")
                    // && !x.Name.Equals("MMHOOK_Facepunch Transport for Netcode for GameObjects"));
                
                cachedAssemblyInfo.References.Clear();

                foreach(var referencedPlugin in mmhookReferences){
                    ExtendedLogging($"[{nameof(ReadPluginInfoAndMMHOOKReferences)}] Found Reference to {referencedPlugin.Name} in {plugin_GUID}.");
                    // Trim out 'MMHOOK_' to get the GUID (the rest of the name, excluding '.dll')
                    var referencedPlugin_GUID = referencedPlugin.Name.Substring(7, referencedPlugin.Name.Length - 7);

                    cachedAssemblyInfo.References.Add(referencedPlugin_GUID);
                }
            }
        }

        private static string GetPluginGUID(string assemblyPath, AssemblyDefinition pluginAssembly){
            var pluginAttributes = pluginAssembly.MainModule.GetCustomAttributes();
            var bepInPluginAttribute = pluginAttributes.FirstOrDefault(x => x.AttributeType.Name.Equals("BepInPlugin"));
            string? plugin_GUID = null;

            if(bepInPluginAttribute != null){
                var bepInPluginAttributeArg = bepInPluginAttribute.ConstructorArguments.FirstOrDefault();
                plugin_GUID = bepInPluginAttributeArg.Value.ToString();
            }

            if(bepInPluginAttribute == null || plugin_GUID == null || plugin_GUID == "") {
                // We use the filename as the GUID, because it doesn't have one.
                var fileName = Path.GetFileName(assemblyPath);
                // Trim out '.dll'
                plugin_GUID = fileName.Substring(0, fileName.Length - 4);
            }

            return plugin_GUID;
        }

        private static List<CachedAssemblyInfo>? TryLoadCache(){
            if(!File.Exists(cacheLocation)){
                ExtendedLogging($"[{nameof(TryLoadCache)}] Cache didn't exist.");
                return null;
            }

            XDocument doc = new XDocument();
            List<CachedAssemblyInfo> fromCache = new();

            try{
                doc = XDocument.Load(cacheLocation);
                foreach(var el in doc.Root.Elements())
                {
                    fromCache.Add(new CachedAssemblyInfo(
                        el.Attribute("guid").Value,
                        el.Attribute("path").Value,
                        long.Parse(el.Attribute("dateModified").Value),
                        bool.Parse(el.Attribute("hasMMHOOK").Value),
                        el.Attribute("references").Value.Split(new char[] {'/'}, StringSplitOptions.RemoveEmptyEntries).ToList()
                        ));
                }
                ExtendedLogging($"[{nameof(TryLoadCache)}] Loaded Cache, which contains {fromCache.Count} entries.");

                return fromCache;
            }
            catch(Exception e){
                Logger.LogError($"Failed to load cache! Rebuilding it.\n{e}");
                return null;
                // forceCacheRebuild = true;
            }
        }

        private static void WriteCache(IEnumerable<CachedAssemblyInfo> cachedAssemblyInfos){
            try{
                ExtendedLogging($"[{nameof(WriteCache)}] Updating cache.");
                
                XElement xmlElements = new XElement("cache", cachedAssemblyInfos.Select(
                    assembly => new XElement("assembly",
                    new XAttribute("guid", assembly.GUID),
                    new XAttribute("path", assembly.Path),
                    new XAttribute("dateModified", assembly.DateModified),
                    new XAttribute("hasMMHOOK", assembly.AlreadyHasMMHOOK),
                    new XAttribute("references", string.Join( '/', assembly.References))
                    )));
                // Logger.LogInfo("\n" + xmlElements);
                // Create the file.
                File.WriteAllText(cacheLocation, xmlElements.ToString());
            }
            catch(Exception e) {
                Logger.LogError($"[{nameof(WriteCache)}] Error while writing cache file, clearing it.\n" + e);
                File.Delete(cacheLocation);
            }
        }

        // Load us https://docs.bepinex.dev/articles/dev_guide/preloader_patchers.html
        public static IEnumerable<string> TargetDLLs { get; } = new string[] { };
        public static void Patch(AssemblyDefinition _) { }
    }
}