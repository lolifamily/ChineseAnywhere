using HarmonyLib;
using VRage.Platform.Windows.IME;

namespace ClientPlugin.Patches;

// WM_INPUTLANGCHANGE: the game window and the IME's WndProc both call LanguageChanged, on the window thread, and
// vanilla ends the composition right there. It is posted to the game thread instead, which owns it
// (ImeProcessorPatch). Applied by Preloader.Finish, before the window exists: its message handler is compiled from
// the window creation on, and a method patched after its callers were compiled may stay inlined into them.
[HarmonyPatchCategory(Preloader.CalleesCategory)]
[HarmonyPatch(typeof(MyImeProcessor), nameof(MyImeProcessor.LanguageChanged))]
internal static class LanguageChangedPatch
{
    private static bool Prefix(MyImeProcessor __instance)
    {
        ImeProcessorPatch.Post(__instance, ImeProcessorPatch.Abort);
        return false;
    }
}
