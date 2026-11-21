using System;
using System.Collections.Generic;
using HarmonyLib;
using Sandbox.Graphics.GUI;

namespace ClientPlugin.Patches;

// Undo and Redo put the caret by a diff of the two texts, shifting the "no difference" -1 like a real one: after an
// edit at the end, where one text starts with the other, the caret went to the start. They also set it before the
// text came back, clamped to the text going away, and Redo moved it back one more. Here the caret goes after the
// part that comes back, set before OnTextChanged scrolls to it and resets the selection there.
//
// In Callees because HandleTextInputBuffered, which TextboxTextInputPatch compiles, calls both.
// Restore updates the caret column too, which KeypressUndo and KeypressRedo, unlike HandleTextInputBuffered, do not.
[HarmonyPatchCategory(Preloader.CalleesCategory)]
[HarmonyPatch(typeof(MyGuiControlMultilineEditableText))]
internal static class MultilineUndoCaretPatch
{
    [HarmonyPrefix]
    [HarmonyPatch(nameof(MyGuiControlMultilineEditableText.Undo))]
    private static bool UndoPrefix(MyGuiControlMultilineEditableText __instance)
    {
        Restore(__instance, __instance.m_undoCache, __instance.AddToRedo);
        return false;
    }

    [HarmonyPrefix]
    [HarmonyPatch(nameof(MyGuiControlMultilineEditableText.Redo))]
    private static bool RedoPrefix(MyGuiControlMultilineEditableText __instance)
    {
        Restore(__instance, __instance.m_redoCache, text => __instance.AddToUndo(text, clearRedo: false));
        return false;
    }

    // InsertChar saves the text for undo first, then its Tab branch saves it again, as the one of
    // HandleTextInputBuffered does: undoing a Tab took two Ctrl+Z. The Tab branch's save is enough.
    [HarmonyPrefix]
    [HarmonyPatch(nameof(MyGuiControlMultilineEditableText.InsertChar))]
    private static void InsertCharPrefix(char character, ref bool compositionEnd) =>
        compositionEnd &= character != '\t';

    // The last text of history comes back, the current one goes to keep. When the two are the same (Backspace at
    // the start and Delete at the end save one too), the caret stays.
    private static void Restore(MyGuiControlMultilineEditableText editor, List<string> history, Action<string> keep)
    {
        if (history.Count == 0)
            return;

        var current = editor.m_text.ToString();
        var restored = history[history.Count - 1];
        keep(current);
        if (restored != current)
            editor.m_carriagePositionIndex = EndOfChange(restored, current);

        editor.UpdateEditorText(history.Count - 1, history);
        editor.m_currentCarriageColumn = editor.GetCarriageColumn(editor.CarriagePositionIndex);
    }

    // Where the part of text that differs from other ends: after their common start, before their common end
    private static int EndOfChange(string text, string other)
    {
        var common = Math.Min(text.Length, other.Length);
        var prefix = 0;
        while (prefix < common && text[prefix] == other[prefix])
            prefix++;

        var suffix = 0;
        while (suffix < common - prefix && text[text.Length - 1 - suffix] == other[other.Length - 1 - suffix])
            suffix++;

        return text.Length - suffix;
    }
}
