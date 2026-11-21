using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Sandbox.Graphics.GUI;

namespace ClientPlugin.Patches;

// Up and Down take the caret to m_currentCarriageColumn of the next line, so whatever moves the caret along a
// line updates it. Typing in English mode does (HandleTextInputBuffered), these edits from the IME's queue do
// not: after typing, Up and Down went to the column where the typing started. Nothing else calls them.
// Undo and Redo get theirs in MultilineUndoCaretPatch.
[HarmonyPatch]
internal static class MultilineCaretColumnPatch
{
    private static IEnumerable<MethodBase> TargetMethods() =>
    [
        AccessTools.Method(typeof(MyGuiControlMultilineEditableText), nameof(MyGuiControlMultilineEditableText.InsertChar)),
        AccessTools.Method(typeof(MyGuiControlMultilineEditableText), nameof(MyGuiControlMultilineEditableText.InsertCharMultiple)),
    ];

    private static void Postfix(MyGuiControlMultilineEditableText __instance) =>
        __instance.m_currentCarriageColumn = __instance.GetCarriageColumn(__instance.CarriagePositionIndex);
}
