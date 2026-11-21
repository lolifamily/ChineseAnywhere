using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using ClientPlugin.Patches;
using HarmonyLib;
using VRage.Plugins;

// Define assembly version when compiled by Pulsar
#if !LOCAL_BUILD
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

#endif
namespace ClientPlugin;

// ReSharper disable once ClassNeverInstantiated.Global
public class Plugin : IPlugin
{
    public const string Name = "ChineseAnywhere";

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void Init(object gameInstance)
    {
        if (GameFormInitPatch.FontPatchError is { } error)
            throw new InvalidOperationException("Patching the fonts at window creation failed", error);

        // The rest went in earlier, see Preloader
        new Harmony(Name).PatchAllUncategorized(Assembly.GetExecutingAssembly());
    }

    public void Dispose()
    {
        // IMPORTANT: Do NOT call harmony.UnpatchAll() here! It may break other plugins.
    }

    public void Update()
    {
    }
}
