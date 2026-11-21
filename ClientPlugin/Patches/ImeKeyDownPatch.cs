using HarmonyLib;
using VRage.Platform.Windows.IME;

namespace ClientPlugin.Patches;

// The IME's hidden TextBox drops the WM_CHAR of Enter, Tab, Esc and Ctrl shortcuts so that its EDIT control
// does not beep. WM_CHAR no longer reaches the EDIT (ImeProcessorPatch), and the multi-line editors need
// those characters for new lines, indents, undo and redo, as in English mode.
[HarmonyPatch(typeof(MyGuiControlIme), nameof(MyGuiControlIme.MyGuiControlIme_KeyDown))]
internal static class ImeKeyDownPatch
{
    private static bool Prefix() => false;
}
