using System;
using HarmonyLib;
using Sandbox.Graphics.GUI;
using VRage.Platform.Windows.Forms;

namespace ClientPlugin.Patches;

// The game creates its window on the render thread, inside the MySandboxGame constructor: after
// MyVRage.Init, before any font exists, long before Plugin.Init. Applied by Preloader.Finish.
[HarmonyPatchCategory(Preloader.FinishCategory)]
[HarmonyPatch(typeof(MyGameForm), nameof(MyGameForm.Init))]
internal static class GameFormInitPatch
{
    private static bool fontsPatched;

    // Thrown out of the window creation, a failed font patch would crash the game without naming the plugin;
    // Plugin.Init rethrows it, where Pulsar does. Read after the game thread has waited for the window.
    internal static Exception FontPatchError { get; private set; }

    private static void Prefix(ref Type imeCandidateType)
    {
        if (!fontsPatched)
        {
            fontsPatched = true;
            try
            {
                new Harmony(Plugin.Name).PatchCategory(typeof(GameFormInitPatch).Assembly, Preloader.WindowCreationCategory);
            }
            catch (Exception e)
            {
                FontPatchError = e;
            }
        }

        // The game builds its IME only when it passes the candidate list type, i.e. for the Chinese UI language
        imeCandidateType ??= typeof(MyGuiControlContextMenu);
    }
}
