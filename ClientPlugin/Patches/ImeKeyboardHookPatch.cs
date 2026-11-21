using HarmonyLib;
using VRage.Platform.Windows.IME;

namespace ClientPlugin.Patches;

// The IME hooks the keyboard of the whole desktop (WH_KEYBOARD_LL) on the render thread, only to keep the Esc that
// cancels a composition from also closing the open screen. Windows then holds every keystroke, in every program,
// until that thread pumps its messages, once a frame, and silently removes the hook when it is too late.
// CompositionKeysPatch does the job instead. Applied by Preloader.Finish: the window creation installs the hook.
[HarmonyPatchCategory(Preloader.CalleesCategory)]
[HarmonyPatch(typeof(MyImeProcessor), nameof(MyImeProcessor.SetHook))]
internal static class ImeKeyboardHookPatch
{
    // SetHook returns IntPtr.Zero, no hook
    private static bool Prefix() => false;
}
