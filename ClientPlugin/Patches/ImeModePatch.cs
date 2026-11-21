using System;
using HarmonyLib;
using VRage.Platform.Windows.IME;

namespace ClientPlugin.Patches;

// The game thread turns the IME on and off (MyImeProcessor's Activate, Deactivate and CaretRepositionReaction,
// ImeProcessorPatch.Restart) by Control.Invoke, waiting for the window thread to run it. That is the render thread,
// which pumps its messages once a frame: each focus change held the game for up to a frame or two, and launched with
// -render_sync, where the render thread waits for the game's next frame before pumping again, it could hang. Nothing
// on the game thread waits for the switch any more, what the IME does about it comes back through its queue
// (ImeProcessorPatch); posted, it runs at the same point of the window thread's pump as before.
// In Callees because CaretRepositionReaction, which ImeProcessorPatch compiles, calls both.
[HarmonyPatchCategory(Preloader.CalleesCategory)]
[HarmonyPatch(typeof(MyGuiControlIme))]
internal static class ImeModePatch
{
    [HarmonyPrefix]
    [HarmonyPatch(nameof(MyGuiControlIme.ActivateIme))]
    private static bool ActivateImePrefix(MyGuiControlIme __instance)
    {
        _ = __instance.BeginInvoke((Action)__instance.ActivateDelegate);
        return false;
    }

    [HarmonyPrefix]
    [HarmonyPatch(nameof(MyGuiControlIme.DeactivateIme))]
    private static bool DeactivateImePrefix(MyGuiControlIme __instance)
    {
        _ = __instance.BeginInvoke((Action)__instance.DeactivateDelegate);
        return false;
    }
}
