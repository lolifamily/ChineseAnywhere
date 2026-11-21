using HarmonyLib;

// DO NOT USE A NAMESPACE HERE!
// CRITICAL: Using a namespace here will prevent Pulsar from finding the Preloader class.

// The patches go in at four points of the startup, each before the game first runs what they change:
//   Callees         Preloader.Finish     first: methods that other code calls and may inline
//   Finish          Preloader.Finish     what the window creation runs
//   WindowCreation  GameFormInitPatch    the fonts: MyVRage.Platform is set by now and no font exists yet
//   uncategorized   Plugin.Init          the IME routing: Pulsar shows the game window only after plugin init
// Harmony compiles a patched method as soon as it patches it, and a callee patched after a caller was compiled may
// stay inlined into it. Callees go in before any caller is compiled; the order within a category is unspecified.
// The fonts cannot go in Finish: there MyVRage.Platform is still null, and on .NET Framework compiling
// MyFont's constructor runs MyRenderProxy's static initializer, which needs it.
// ReSharper disable once CheckNamespace
#pragma warning disable CA1050
public static class Preloader
{
    public const string CalleesCategory = "Callees";
    public const string FinishCategory = "Finish";
    public const string WindowCreationCategory = "WindowCreation";

    // Runs right before Space Engineers starts
    // ReSharper disable once UnusedMember.Global
    public static void Finish()
    {
        // ClientPlugin.Plugin.Name is a const, compiled in as a literal: Plugin itself never loads here
        var assembly = typeof(Preloader).Assembly;
        var harmony = new Harmony(ClientPlugin.Plugin.Name);
        harmony.PatchCategory(assembly, CalleesCategory);
        harmony.PatchCategory(assembly, FinishCategory);
    }
}
#pragma warning restore CA1050
