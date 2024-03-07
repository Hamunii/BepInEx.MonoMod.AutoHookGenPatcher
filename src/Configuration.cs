using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;

namespace HookGenPatchAnything.Config {
    public class PluginConfig
    {
        public ConfigEntry<bool> PatchAllPlugins;
        public PluginConfig(ConfigFile cfg)
        {
            PatchAllPlugins = cfg.Bind("HookGenPatch All Plugins", "Enabled", false,
                "If this option is enabled, HookGenAutoPatcher will generate MMHOOK files\n" +
                "for all plugins, whether or not they were referenced by other plugins.");
            
            // ClearUnusedEntries(cfg);
        }

        // private void ClearUnusedEntries(ConfigFile cfg) {
        //     // Normally, old unused config entries don't get removed, so we do it with this piece of code. Credit to Kittenji.
        //     PropertyInfo orphanedEntriesProp = cfg.GetType().GetProperty("OrphanedEntries", BindingFlags.NonPublic | BindingFlags.Instance);
        //     var orphanedEntries = (Dictionary<ConfigDefinition, string>)orphanedEntriesProp.GetValue(cfg, null);
        //     orphanedEntries.Clear(); // Clear orphaned entries (Unbinded/Abandoned entries)
        //     cfg.Save(); // Save the config file to save these changes
        // }
    }
}