namespace AutoHookGenPatcher;

static class PathsExtra
{
    static PathsExtra() =>
        Interop_il2cpp = Path.Combine(Paths.BepInExRootPath, "interop");

    internal static string Interop_il2cpp { get; private set; }
}