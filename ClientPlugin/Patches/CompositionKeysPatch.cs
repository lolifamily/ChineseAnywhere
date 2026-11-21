using HarmonyLib;
using VRage.Input;
using VRage.Platform.Windows.IME;

namespace ClientPlugin.Patches;

// While the IME composes, it uses the keys below to edit the composition and pick a candidate. The game reads the
// keyboard state itself and would act on them as well, as if no composition were going:
//  * Enter would send the chat or confirm the dialog (MyGuiScreenDialogText confirms on any new Enter), Esc would
//    close the screen;
//  * in the chat, Up, Down, Tab, Page Up and Page Down would put an older message or a player name in place of the
//    line being typed, or scroll the log;
//  * Home and End would take the caret of a multi-line box away from the composition, and Delete, in any box, would
//    remove the text after it.
// Left, Right and Backspace are IME keys too, but the boxes already leave them alone during a composition
// (IsImeActive). For every other key, Space included, the game does the same with or without a composition, so those
// stay.
//
// A key pressed during a composition stays up for the game until it is released: some repeat for as long as they are
// held (MyKeyThrottler), Delete for one, and a key still held when the composition ends would come in as a new press.
// This runs right after the game polls the keyboard, once a frame, on the game thread. The IME's queue, which the game
// thread runs just before polling, may have ended the composition by then, the key that ended it still down; so a
// composition still going at the previous poll counts too. The IME's keyboard hook used to do this for Esc alone
// (ImeKeyboardHookPatch drops that hook).
[HarmonyPatch(typeof(MyVRageInput), nameof(MyVRageInput.Update))]
internal static class CompositionKeysPatch
{
    private static readonly MyKeys[] Keys =
    [
        MyKeys.Enter, MyKeys.Escape, MyKeys.Up, MyKeys.Down, MyKeys.Tab,
        MyKeys.PageUp, MyKeys.PageDown, MyKeys.Home, MyKeys.End, MyKeys.Delete,
    ];

    // Which of Keys went down during a composition and are not released yet
    private static readonly bool[] Taken = new bool[Keys.Length];

    private static bool wasComposing;

    private static void Postfix(MyVRageInput __instance)
    {
        var composing = MyImeProcessor.Instance?.IsComposing == true;
        var keyboard = __instance.m_keyboardState;
        for (var i = 0; i < Keys.Length; i++)
        {
            var key = Keys[i];
            if (!keyboard.m_actualKeyboardState.IsKeyDown(key))
                Taken[i] = false;
            else if (composing || wasComposing)
                Taken[i] = true;

            if (!Taken[i])
                continue;

            keyboard.m_actualKeyboardState.SetKey(key, false);
            keyboard.m_previousKeyboardState.SetKey(key, false);
        }

        wasComposing = composing;
    }
}
