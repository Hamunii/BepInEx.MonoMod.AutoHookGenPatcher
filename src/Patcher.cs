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
        public static string? mmhookPath;
        public static ManualLogSource Logger = BepInEx.Logging.Logger.CreateLogSource(PluginInfo.PLUGIN_NAME);
        private static readonly PluginConfig BoundConfig = new PluginConfig(new ConfigFile(Path.Combine(Paths.ConfigPath, PluginInfo.PLUGIN_NAME + ".cfg"), false));
        internal static List<Task> hookGenTasks = new();
        internal static string? cacheLocation;
        internal static bool isCacheUpdateNeeded = false;
        public static void Finish() {
            mmhookPath = Path.Combine(Paths.PluginPath, "MMHOOK");
            cacheLocation = Path.Combine(Path.Combine(mmhookPath, "Any"), "cache.xml");
            if(!Directory.Exists(mmhookPath)){
                Logger.LogError($"No MMHOOK directory found! Please install HookGenPatcher.");
                mmhookPath = null!;
            }
            var watch = Stopwatch.StartNew();
            Begin();
            watch.Stop();
            Logger.LogInfo($"Took {watch.ElapsedMilliseconds}ms");
        }

        private static void Begin(){
            var cachedPlugins = TryLoadCache();

            var currentPlugins = GetPlugins(cachedPlugins).ToList();

            for(int idx = 0; idx < currentPlugins.Count; idx++)
            {
                var plugin = currentPlugins[idx];
                if(!IsPluginInfoUpToDate(plugin))
                {
                    ReadPluginInfoAndMMHOOKReferences(plugin);
                    isCacheUpdateNeeded = true;
                }
            }

            var mmHookPaths = Directory.GetFiles(Paths.PluginPath, "*.dll", SearchOption.AllDirectories).Where(name => Path.GetFileName(name).StartsWith("MMHOOK_")).ToList();

            for(int idx = 0; idx < currentPlugins.Count; idx++)
            {
                var plugin = currentPlugins[idx];
                if(plugin.AlreadyHasMMHOOK) continue;

                if(BoundConfig.GenerateForAllPlugins.Value || currentPlugins.SelectMany(plugins => plugins.References).Contains(plugin.GUID))
                {
                    var hookGenTask = new Task(() => StartHookGen(plugin, mmHookPaths));
                    hookGenTask.Start();
                    hookGenTasks.Add(hookGenTask);
                }
            }

            if(BoundConfig.GenerateForAllPlugins.Value)
                if(BoundConfig.DisableGenerateForAllPlugins.Value)
                    BoundConfig.GenerateForAllPlugins.Value = false;
            
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
            var paths = Directory.GetFiles(Paths.PluginPath, "*.dll", SearchOption.AllDirectories);
            var assemblyPaths = paths.Where(name => !Path.GetFileName(name).StartsWith("MMHOOK_"));
            var mmHookPaths = paths.Where(name => Path.GetFileName(name).StartsWith("MMHOOK_")).Select(name => Path.GetFileName(name));

            foreach (string assemblyPath in assemblyPaths)
            {
                CachedAssemblyInfo? cachedAssembly = cachedAssemblies?.Find(ass => ass.Path.Equals(assemblyPath));

                if(cachedAssembly == null)
                    yield return new CachedAssemblyInfo(null!, assemblyPath, 0, false, null);
                else{
                    var hasMMHOOKinReality = mmHookPaths.Contains($"MMHOOK_{cachedAssembly.GUID}.dll");
                    
                    if(cachedAssembly.AlreadyHasMMHOOK != hasMMHOOKinReality)
                        isCacheUpdateNeeded = true;

                    bool needsInspection = cachedAssembly.AlreadyHasMMHOOK && !hasMMHOOKinReality;

                    yield return new CachedAssemblyInfo(cachedAssembly.GUID!, assemblyPath, needsInspection ? 0 : cachedAssembly.DateModified, hasMMHOOKinReality, cachedAssembly.References);
                }
            }
        }

        private static bool IsPluginInfoUpToDate(CachedAssemblyInfo cachedAssemblyInfo)
        {
            var currentDateModified = File.GetLastWriteTime(cachedAssemblyInfo.Path).Ticks;

            if(currentDateModified == cachedAssemblyInfo.DateModified)
                return true;

            cachedAssemblyInfo.DateModified = currentDateModified;
            return false;
        }

        private static void ReadPluginInfoAndMMHOOKReferences(CachedAssemblyInfo cachedAssemblyInfo){
            using (var pluginAssembly = AssemblyDefinition.ReadAssembly(cachedAssemblyInfo.Path))
            {
                var plugin_GUID = GetPluginGUID(cachedAssemblyInfo.Path, pluginAssembly);
                cachedAssemblyInfo.GUID = plugin_GUID;

                var mmhookReferences = pluginAssembly.MainModule.AssemblyReferences.Where(x => x.Name.StartsWith("MMHOOK_")
                    && !x.Name.Equals("MMHOOK_AmazingAssets.TerrainToMesh") // Exclude ones already included in HookGenPatcher
                    && !x.Name.Equals("MMHOOK_Assembly-CSharp")
                    && !x.Name.Equals("MMHOOK_ClientNetworkTransform")
                    && !x.Name.Equals("MMHOOK_DissonanceVoip")
                    && !x.Name.Equals("MMHOOK_Facepunch.Steamworks.Win64")
                    && !x.Name.Equals("MMHOOK_Facepunch Transport for Netcode for GameObjects"));
                
                cachedAssemblyInfo.References.Clear();

                foreach(var referencePlugin in mmhookReferences){
                    Logger.LogInfo($"Found reference to '{referencePlugin.Name}' in '{plugin_GUID}'.");
                    // Trim out 'MMHOOK_' to get the GUID (the rest of the name, excluding '.dll')
                    var referencedPlugin_GUID = referencePlugin.Name.Substring(7, referencePlugin.Name.Length - 7);

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
            if(!File.Exists(cacheLocation))
                return null;

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
                return fromCache;
            }
            catch(Exception e){
                Logger.LogError($"Failed to load 'cache.xml'! Rebuilding cache.\n{e}");
                return null;
                // forceCacheRebuild = true;
            }
        }

        private static void WriteCache(IEnumerable<CachedAssemblyInfo> cachedAssemblyInfos){
            try{
                Logger.LogInfo("Updating cache.");
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
                Logger.LogError("Error while writing cache file, clearing cache.\n" + e);
                File.Delete(cacheLocation);
            }
        }

        /*private static void DiscoverPlugins(){
            
            var mmhookFolder = Path.Combine(Path.Combine(Paths.PluginPath, "MMHOOK"), "Any");
            var cacheLocation = Path.Combine(mmhookFolder, "cache.xml");
            bool forceCacheRebuild = false;

            HookGenPatch.pluginSearch = new();
            HookGenPatch.pluginCacheToWrite = new();
            HookGenPatch.loadedPluginCache = new();
            Dictionary<string, string> pathToGUID = new();
            List<string> pluginsToIgnorePatching = new();
            hasPluginMMHOOKFile = new();
            
            if(File.Exists(cacheLocation)){
                XDocument doc = new XDocument();
                try{
                    doc = XDocument.Load(cacheLocation);
                    foreach(var el in doc.Root.Elements())
                    {
                        if(!HookGenPatch.loadedPluginCache.ContainsKey(el.Name.LocalName)){
                            HookGenPatch.loadedPluginCache.Add(el.Attribute("guid").Value, long.Parse(el.Attribute("dateModified").Value));
                            pathToGUID.Add(el.Attribute("path").Value, el.Attribute("guid").Value);
                            hasPluginMMHOOKFile.Add(el.Attribute("guid").Value, bool.Parse(el.Attribute("hasMMHOOK").Value));
                        }
                        else{
                            Logger.LogWarning($"Failed to load 'cache.xml' because of duplicate GUID! Rebuilding cache.");
                            forceCacheRebuild = true;
                        }
                    }
                }
                catch(Exception e){
                    Logger.LogError($"Failed to load 'cache.xml'! Rebuilding cache.\n{e}");
                    forceCacheRebuild = true;
                }
            }

            toPatchFromGUID = new();
            bool shouldPatchAll = BoundConfig.PatchAllPlugins.Value;
            bool isAllUpToDate = true;

            foreach (string assemblyPath in Directory.GetFiles(Paths.PluginPath, "*.dll", SearchOption.AllDirectories))
            {
                if (!Path.GetFileName(assemblyPath).StartsWith("MMHOOK_")){
                    if(pathToGUID.ContainsKey(assemblyPath)){
                        long date = File.GetLastWriteTime(assemblyPath).Ticks;
                        if(!forceCacheRebuild && HookGenPatch.loadedPluginCache[pathToGUID[assemblyPath]] == date){
                            if(hasPluginMMHOOKFile[pathToGUID[assemblyPath]]){
                                Logger.LogInfo($"Up to date: '{pathToGUID[assemblyPath]}'");
                                pluginsToIgnorePatching.Add(pathToGUID[assemblyPath]);
                                HookGenPatch.pluginCacheToWrite.Add($"{pathToGUID[assemblyPath]}?{assemblyPath}", date);
                            }
                            else{
                                Logger.LogInfo($"Up to date, but no MMHOOK: '{pathToGUID[assemblyPath]}'");
                            }
                            if(!HookGenPatch.pluginSearch.ContainsKey(pathToGUID[assemblyPath]))
                                HookGenPatch.pluginSearch.Add(pathToGUID[assemblyPath], assemblyPath);
                            continue;
                        }
                        else isAllUpToDate = false;
                    }
                    else isAllUpToDate = false;
                    try
                    {
                        using (var pluginAssembly = AssemblyDefinition.ReadAssembly(assemblyPath))
                        {
                            DateTime modification = File.GetLastWriteTime(assemblyPath);                            
                            var pluginAttributes = pluginAssembly.MainModule.GetCustomAttributes();
                            var bepInPluginAttr = pluginAttributes.FirstOrDefault(x => x.AttributeType.Name.Equals("BepInPlugin"));
                            string plugin_GUID = null;
                            if(bepInPluginAttr != null){
                                CustomAttributeArgument pluginAttrArg = bepInPluginAttr.ConstructorArguments.FirstOrDefault();
                                plugin_GUID = pluginAttrArg.Value.ToString();
                            }
                            if(bepInPluginAttr == null || plugin_GUID == null || plugin_GUID == "") {
                                // We use the filename as the GUID, because it doesn't have one.
                                var fileName = Path.GetFileName(assemblyPath);
                                // Trim out '.dll'
                                plugin_GUID = fileName.Substring(0, fileName.Length - 4);
                            }
                            if(!HookGenPatch.pluginSearch.ContainsKey(plugin_GUID))
                                HookGenPatch.pluginSearch.Add(plugin_GUID, assemblyPath);
                            else{
                                Logger.LogError($"Another plugin has the same GUID '{plugin_GUID}' and cannot patched!");
                                continue;
                            }
                            var mmhookReferences = pluginAssembly.MainModule.AssemblyReferences.Where(x => x.Name.StartsWith("MMHOOK_")
                                && !x.Name.Equals("MMHOOK_AmazingAssets.TerrainToMesh") // Exclude ones already included in HookGenPatcher
                                && !x.Name.Equals("MMHOOK_Assembly-CSharp")
                                && !x.Name.Equals("MMHOOK_ClientNetworkTransform")
                                && !x.Name.Equals("MMHOOK_DissonanceVoip")
                                && !x.Name.Equals("MMHOOK_Facepunch.Steamworks.Win64")
                                && !x.Name.Equals("MMHOOK_Facepunch Transport for Netcode for GameObjects"));
                            
                            foreach(var reference in mmhookReferences){
                                Logger.LogInfo($"Found reference to '{reference.Name}' in '{plugin_GUID}'.");
                                // Trim out 'MMHOOK_' to get the GUID (the rest of the name, excluding '.dll')
                                var guid = reference.Name.Substring(7, reference.Name.Length - 7);
                                toPatchFromGUID.Add(guid);
                            }
                            if(!hasPluginMMHOOKFile.ContainsKey(plugin_GUID))
                                hasPluginMMHOOKFile.Add(plugin_GUID, false);
                            if(!HookGenPatch.pluginCacheToWrite.ContainsKey($"{plugin_GUID}?{assemblyPath}")){
                                HookGenPatch.pluginCacheToWrite.Add($"{plugin_GUID}?{assemblyPath}", modification.Ticks);
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
            Logger.LogInfo("Patching everything because [HookGenPatch All Plugins] is enabled");
            if(!shouldPatchAll){
                foreach(var plugin_GUID in toPatchFromGUID.Distinct().ToList()){
                    if(HookGenPatch.pluginSearch.ContainsKey(plugin_GUID)){
                        if(!pluginsToIgnorePatching.Contains(plugin_GUID))
                            hasPluginMMHOOKFile[plugin_GUID] = HookGenPatch.RunHookGen(HookGenPatch.pluginSearch[plugin_GUID], plugin_GUID, isPlugin: true);
                    }
                    else
                        Logger.LogInfo($"Plugin with GUID '{plugin_GUID}' was not found, and couldn't be patched.");
                }
            }
            else{
                foreach(var entry in HookGenPatch.pluginSearch){
                    if(!pluginsToIgnorePatching.Contains(entry.Key))
                        HookGenPatch.RunHookGen(entry.Value, entry.Key, isPlugin: true);
                }
            }
            if(isAllUpToDate){
                Logger.LogInfo("All MMHOOK files are up to date");
                return;
            }

            try{
                XElement xmlElements = new XElement("cache", HookGenPatch.pluginCacheToWrite.Select(
                    kv => new XElement("assembly",
                        new XAttribute("guid", kv.Key.Split(new char[] {'?'})[0]),
                        new XAttribute("path", kv.Key.Split(new char[] {'?'})[1]),
                        new XAttribute("dateModified", kv.Value),
                        new XAttribute("hasMMHOOK", hasPluginMMHOOKFile[kv.Key.Split(new char[] {'?'})[0]])
                    )));
                // Logger.LogInfo("\n" + xmlElements);
                // Create the file.
                File.WriteAllText(cacheLocation, xmlElements.ToString());
            }
            catch(Exception e) {
                Logger.LogError("Error while writing cache file, clearing cache.\n" + e);
                File.Delete(cacheLocation);
            }
        }*/

        // Load us https://docs.bepinex.dev/articles/dev_guide/preloader_patchers.html
        public static IEnumerable<string> TargetDLLs { get; } = new string[] { };
        public static void Patch(AssemblyDefinition _) { }
    }
}