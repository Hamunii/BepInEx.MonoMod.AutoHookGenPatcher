using System.Collections.Generic;

namespace AutoHookGenPatcher
{
    internal class CachedAssemblyInfo
    {
        internal string GUID { get; set; }
        internal string Path { get; set; }
        internal long DateModified { get; set; }
        internal bool AlreadyHasMMHOOK { get; set; }
        internal List<string> References { get; set; }
        internal CachedAssemblyInfo(string guid, string path, long dateModified, bool alreadyHasMMHOOK, List<string>? references){
            GUID = guid;
            Path = path;
            DateModified = dateModified;
            AlreadyHasMMHOOK = alreadyHasMMHOOK;
            References = references ?? new();
        }
    }
}
