using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using Sandbox.Graphics.GUI;
using VRage;
using VRage.Collections;
using VRage.Input;
using VRage.Platform.Windows.IME;

namespace ClientPlugin.Patches;

// While the IME serves a vanilla text box, the box gets everything through the IME's queue (ImeProcessorPatch).
// The same characters are in TextInput too now, for mods, so the box skips TextInput meanwhile.
[HarmonyPatch]
internal static class TextboxTextInputPatch
{
    [HarmonyTranspiler]
    [HarmonyPatch(typeof(MyGuiControlTextbox), nameof(MyGuiControlTextbox.HandleTextInputBuffered))]
    private static IEnumerable<CodeInstruction> TextboxTranspiler(IEnumerable<CodeInstruction> instructions) =>
        ReadThroughGate(instructions);

    [HarmonyTranspiler]
    [HarmonyPatch(typeof(MyGuiControlMultilineEditableText), nameof(MyGuiControlMultilineEditableText.HandleTextInputBuffered))]
    private static IEnumerable<CodeInstruction> MultilineTranspiler(IEnumerable<CodeInstruction> instructions) =>
        ReadThroughGate(instructions);

    // MyInput.Static.TextInput  ->  TextInputFor(MyInput.Static, this)
    private static IEnumerable<CodeInstruction> ReadThroughGate(IEnumerable<CodeInstruction> instructions)
    {
        var textInput = AccessTools.PropertyGetter(typeof(IMyInput), nameof(IMyInput.TextInput));
        using var code = instructions.GetEnumerator();
        while (code.MoveNext())
        {
            var ci = code.Current;
            if (!ci.Calls(textInput))
            {
                yield return ci;
                continue;
            }

            yield return new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(ci);
            yield return CodeInstruction.Call(typeof(TextboxTextInputPatch), nameof(TextInputFor));
            while (code.MoveNext())
                yield return code.Current;
            yield break;
        }

        // Ungated, the box would get each character twice, from the IME's queue and from TextInput
        throw new InvalidOperationException("No read of IMyInput.TextInput to gate");
    }

    // ListReader<char>.Empty, not []: [] makes a default ListReader, whose null list throws when the box enumerates it
    private static ListReader<char> TextInputFor(IMyInput input, IMyImeActiveControl box) =>
        (MyVRage.Platform.ImeProcessor as MyImeProcessor)?.m_activeTextElement == box ? ListReader<char>.Empty : input.TextInput;
}
