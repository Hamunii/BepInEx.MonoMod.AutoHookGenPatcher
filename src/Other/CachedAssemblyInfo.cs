using System;
using System.Collections.Generic;

namespace AutoHookGenPatcher
{
    internal class CachedAssemblyInfo
    {
        internal string GUID { get; set; }
        internal string Path { get; set; }
        internal long DateModified { get; set; }
        internal long MMHOOKDate { get; set; }
        internal List<string> References { get; set; }
        internal bool AlreadyHasMMHOOK { get; set; }
        internal Version PluginVersion { get; set; }
        internal bool IsDuplicate { get; set; }
        internal CachedAssemblyInfo(string guid, string path, long dateModified, long mmhookDate, List<string>? references, string? pluginVersion, bool alreadyHasMMHOOK){
            GUID = guid;
            Path = path;
            DateModified = dateModified;
            MMHOOKDate = mmhookDate;
            References = references ?? new();
            PluginVersion = (pluginVersion == null || pluginVersion == "") ? new Version(0, 0, 0) : new Version(pluginVersion);
            AlreadyHasMMHOOK = alreadyHasMMHOOK;
            
            IsDuplicate = false;
        }

        internal CachedAssemblyInfo(string guid, string path, long dateModified, long mmhookDate, List<string>? references, string pluginVersion)
            : this(guid, path, dateModified, mmhookDate, references, pluginVersion, alreadyHasMMHOOK: false) { }

        internal CachedAssemblyInfo(string path)
            : this(guid: null!, path, dateModified: 0, mmhookDate: 0, references: null, pluginVersion: null, alreadyHasMMHOOK: false) { }
    }
}
