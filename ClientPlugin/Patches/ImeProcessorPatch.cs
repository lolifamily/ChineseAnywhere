using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using HarmonyLib;
using Sandbox.Graphics.GUI;
using VRage;
using VRage.Platform.Windows.IME;

namespace ClientPlugin.Patches;

// The game's IME, built for every UI language (GameFormInitPatch), with typed text also going where
// English mode puts it:
//  * every committed character also enters MyInput.TextInput, the stream mods read in English mode;
//  * the vanilla text box the IME serves still gets everything through the IME's ordered queue
//    (composition preview, commits, typed keys) and skips TextInput meanwhile (TextboxTextInputPatch).
// The IME shows its own candidate window, where that box shows the composition (ImeCaret). The game's own
// candidate list, which showed the candidates a second time wherever the IME also answers ImmGetCandidateList,
// is never filled.
//
// The IME's messages arrive on the window thread, which is the render thread: the game creates its window there.
// The boxes, the screens and the composition state belong to the game thread. Vanilla handles a composition on the
// window thread, so it touched them all from there: it added its candidate list to the screen's controls while the
// game thread went through them, set IsImeActive out of order with the queued edits (the end of one composition,
// queued, could clear it during the next), counted the room left in a box before its queued edits were applied, and
// kept the composition state the game thread writes too. Here the window thread only reads the IME, answers it from
// ImeCaret and posts each message to the game thread (Post); all the rest runs there, in the order of the queue.
[HarmonyPatch(typeof(MyImeProcessor))]
internal static class ImeProcessorPatch
{
    private const int WM_CHAR = 0x0102;
    private const int WM_IME_STARTCOMPOSITION = 0x010D;
    private const int WM_IME_ENDCOMPOSITION = 0x010E;
    private const int WM_IME_COMPOSITION = 0x010F;
    private const int WM_IME_SETCONTEXT = 0x0281;
    private const int WM_IME_SELECT = 0x0285;
    private const int WM_IME_REQUEST = 0x0288;
    private const int GCS_RESULTSTR = 0x0800;
    private const long ISC_SHOWUICOMPOSITIONWINDOW = 0x80000000;
    private const int IMR_QUERYCHARPOSITION = 0x0006;
    private const int CFS_FORCE_POSITION = 0x0020;

    // Measured on the game thread after each run of the IME's queue, read on the window thread
    private static volatile ImeCaret caret;

    // The game window, in whose client area ImeCaret measures; used on its own thread, where the IME's messages arrive
    private static Control GameWindow => (Control)MyVRage.Platform.Windows.Window;

    // The window thread's part of each message. Returning false skips vanilla, and __result, false unless set, then
    // keeps the message from the hidden TextBox and DefWindowProc.
    [HarmonyPrefix]
    [HarmonyPatch(nameof(MyImeProcessor.WndProc))]
    private static bool WndProcPrefix(MyImeProcessor __instance, ref Message m, ref bool __result)
    {
        switch (m.Msg)
        {
            // Vanilla keeps both from DefWindowProc, to hide the IME's windows: the IME then never learns that this
            // window shows IME UI, and WeType, for one, keeps its candidate window hidden. The composition window
            // stays hidden, the game draws the composition in the box itself
            case WM_IME_SETCONTEXT:
                m.LParam = new IntPtr(m.LParam.ToInt64() & ~ISC_SHOWUICOMPOSITIONWINDOW);
                __result = true;
                return false;

            case WM_IME_SELECT:
                __result = true;
                return false;

            case WM_IME_STARTCOMPOSITION:
                PlaceCompositionWindow(__instance, m.HWnd);
                Post(__instance, Start);
                return false;

            // Both strings are read now, the IME moves on before the game thread gets to them. In the order the IME
            // produced them: the commit, then whatever composition continues after it
            case WM_IME_COMPOSITION:
            {
                var committed = (m.LParam.ToInt64() & GCS_RESULTSTR) != 0;
                var result = committed ? ResultString(m.HWnd) : string.Empty;
                var composition = __instance.CurrentCompStr(m.HWnd);
                foreach (var c in result)
                    MyVRage.Platform.Input.AddChar(c);

                Post(__instance, (ime, box) =>
                {
                    Commit(ime, box, result);
                    Preview(ime, box, composition);
                });

                // DefWindowProc would send the result again, as WM_IME_CHAR
                __result = !committed;
                return false;
            }

            case WM_IME_ENDCOMPOSITION:
                Post(__instance, End);
                return false;

            // Kept from the hidden TextBox, which would reset its text
            case WM_CHAR:
            {
                var c = (char)(int)m.WParam;
                MyVRage.Platform.Input.AddChar(c);
                Post(__instance, (ime, box) => TypeChar(ime, box, c));
                return false;
            }

            // The IME asks where a composition character is shown, to put its candidate window by it. Whoever
            // draws the composition answers; the game draws it in the box, yet answers 0, "unknown"
            case WM_IME_REQUEST when (int)m.WParam == IMR_QUERYCHARPOSITION && caret is { } current:
                AnswerCharPosition(current, m.LParam);
                m.Result = (IntPtr)1;
                return false;
        }

        return true;
    }

    // Window thread, at each composition start. Vanilla forces the composition window to (15000, 0), off screen, to
    // hide it, which puts candidate windows placed by it off screen too. The IME draws no composition window anyway:
    // its context is activated without ISC_SHOWUICOMPOSITIONWINDOW (WndProcPrefix).
    private static void PlaceCompositionWindow(MyImeProcessor ime, IntPtr hwnd)
    {
        if (caret is not { } current)
        {
            _ = MyImeProcessor.SetCompositionWindow(hwnd);
            return;
        }

        Control box = ime.m_guiControlElement;
        var start = box.PointToClient(GameWindow.PointToScreen(current.Characters[0]));
        var form = new CompositionForm { dwStyle = CFS_FORCE_POSITION, ptCurrentPos = { x = start.X, y = start.Y } };
        var context = MyImeProcessor.ImmGetContext(hwnd);
        _ = MyImeProcessor.ImmSetCompositionWindow(context, ref form);
        _ = MyImeProcessor.ImmReleaseContext(hwnd, context);
    }

    // As MyImeProcessor.CurrentCompStr, for the result: UTF-16, whatever code page the game's message loop uses
    private static string ResultString(IntPtr handle)
    {
        var context = MyImeProcessor.ImmGetContext(handle);
        try
        {
            var size = MyImeProcessor.ImmGetCompositionStringW(context, GCS_RESULTSTR, null, 0);
            if (size <= 0)
                return string.Empty;

            var buffer = new byte[size];
            var copied = MyImeProcessor.ImmGetCompositionStringW(context, GCS_RESULTSTR, buffer, size);
            return Encoding.Unicode.GetString(buffer, 0, copied);
        }
        finally
        {
            _ = MyImeProcessor.ImmReleaseContext(handle, context);
        }
    }

    // In screen coordinates, as IMECHARPOSITION has them
    private static void AnswerCharPosition(ImeCaret current, IntPtr request)
    {
        var position = Marshal.PtrToStructure<IMECHARPOSITION>(request);
        var character = GameWindow.PointToScreen(current.Characters[Math.Min(position.dwCharPos, (uint)current.Characters.Length - 1)]);
        var box = GameWindow.RectangleToScreen(current.Box);
        position.pt = new POINT_IME { x = character.X, y = character.Y };
        position.cLineHeight = (uint)current.LineHeight;
        position.rcDocument = new RECT_IME { left = box.Left, top = box.Top, right = box.Right, bottom = box.Bottom };
        Marshal.StructureToPtr(position, request, false);
    }

#pragma warning disable CS0649 // dwSize and dwCharPos come from the IME, through Marshal.PtrToStructure
    // ReSharper disable NotAccessedField.Local
    private struct IMECHARPOSITION
    {
        public uint dwSize;
        public uint dwCharPos;
        public POINT_IME pt;
        public uint cLineHeight;
        public RECT_IME rcDocument;
    }
    // ReSharper restore NotAccessedField.Local
#pragma warning restore CS0649

    // The IME's own queue, which the game thread runs every frame (MySandboxGame.ProcessInvoke) and vanilla's game
    // thread code queues its edits into too (Deactivate): the IME's edits and the game's own run in one order. The box
    // is the one the IME serves as the message arrives, read as vanilla's InvokeXxx read it: as an address only, the
    // game thread tells whether it still is (Live).
    internal static void Post(MyImeProcessor ime, Action<MyImeProcessor, IMyImeActiveControl> handler) =>
        ime.QueueInvoke(new MyImeProcessor.MyDel(ime.m_activeTextElement, box => handler(ime, box)));

    // From here on the game thread, running the queue, which edits the boxes itself: queued, its edits would come
    // after the messages already waiting.

    [HarmonyPostfix]
    [HarmonyPatch(nameof(MyImeProcessor.ProcessInvoke))]
    private static void ProcessInvokePostfix(MyImeProcessor __instance) =>
        caret = ImeCaret.Measure(__instance.m_activeTextElement, __instance.m_compositionString.Length);

    // The IME goes on sending a composition the game has ended itself, losing the box (Deactivate), refusing it
    // (Start) or keeping it as typed (CaretRepositionReactionPostfix); the queue brings what remains of it before
    // anything of the next one
    private static bool Live(MyImeProcessor ime, IMyImeActiveControl box) =>
        box != null && box == ime.m_activeTextElement && ime.m_isComposing;

    // As vanilla, a composition with no box to show it, or no room in it, is refused by restarting the IME. One begun
    // for a box that has lost the IME since was dropped by that already (Deactivate).
    private static void Start(MyImeProcessor ime, IMyImeActiveControl box)
    {
        if (box != null && box != ime.m_activeTextElement)
            return;

        if (box == null || Room(box) == 0)
        {
            Restart(ime);
            return;
        }

        ime.m_isComposing = true;
        box.IsImeActive = true;
    }

    // In place of the last preview, at the caret, as much as there is room for: the box shows exactly
    // m_compositionString, which is also what vanilla's Deactivate takes back
    private static void Preview(MyImeProcessor ime, IMyImeActiveControl box, string composition)
    {
        if (!Live(ime, box))
            return;

        Erase(box, ime.m_compositionString.Length);
        var shown = composition.Substring(0, Math.Min(composition.Length, Room(box)));
        if (shown.Length > 0)
            box.InsertCharMultiple(false, shown);
        ime.m_compositionString = shown;
    }

    // The preview goes, the result comes. One commit, one edit where it matters: the multi-line editor saves its undo
    // history and lays out the whole text on every insert. Single-line boxes keep it character by character: they
    // have no undo, and take as much as fits, as when typing or pasting; their InsertCharMultiple takes all or nothing.
    private static void Commit(MyImeProcessor ime, IMyImeActiveControl box, string result)
    {
        if (result.Length == 0 || !Live(ime, box))
            return;

        Erase(box, ime.m_compositionString.Length);
        ime.m_compositionString = string.Empty;
        if (box is MyGuiControlMultilineEditableText)
            box.InsertCharMultiple(true, result);
        else
            foreach (var c in result)
                box.InsertChar(true, c);
    }

    private static void End(MyImeProcessor ime, IMyImeActiveControl box)
    {
        if (!Live(ime, box))
            return;

        Erase(box, ime.m_compositionString.Length);
        ime.m_compositionString = string.Empty;
        ime.m_isComposing = false;
        box.DeactivateIme();
    }

    // A change of input language (LanguageChangedPatch): as vanilla, the composition ends and the IME restarts
    internal static void Abort(MyImeProcessor ime, IMyImeActiveControl box)
    {
        if (!Live(ime, box))
            return;

        End(ime, box);
        Restart(ime);
    }

    // A click in the box during a composition, before the caret moves there: vanilla keeps the preview as typed text
    // (ConfirmComposition) and restarts the IME, which commits the composition again as it is closed
    // (ImeContext.Disable); that result went in a second time, at the click. The composition is over for the box:
    // what the IME still sends of it is dropped (Live), and the box takes its arrow keys again.
    [HarmonyPostfix]
    [HarmonyPatch(nameof(MyImeProcessor.CaretRepositionReaction))]
    private static void CaretRepositionReactionPostfix(MyImeProcessor __instance)
    {
        if (!__instance.m_isComposing)
            return;

        __instance.m_isComposing = false;
        __instance.m_activeTextElement?.DeactivateIme();
    }

    // Registering a screen hands the IME to the box focused in it. Vanilla only unregisters the screen before, which
    // deactivates the IME whatever box it serves: a box focused before its screen was registered (the terminal's search
    // box, the code editor's) kept the focus without the IME. Registered first and focused then, as the toolbar config
    // does, a box was fine.
    [HarmonyPostfix]
    [HarmonyPatch(nameof(MyImeProcessor.RegisterActiveScreen))]
    private static void RegisterActiveScreenPostfix(MyImeProcessor __instance, IVRageGuiScreen screen)
    {
        if (__instance.m_activeTextElement == null && screen.FocusedControl is IMyImeActiveControl box)
            __instance.Activate(box);
    }

    // Text or action is told by the character itself, as English mode does (HandleTextInputBuffered). The IME's own
    // handler asks whether Ctrl is down instead: that drops what AltGr types ({ [ ] } \ @ ~, Windows adds a Ctrl to
    // AltGr), and the Ctrl it sees is the game thread's last poll, out of step with the message, so a quick Ctrl+V
    // could insert 0x16.
    private static void TypeChar(MyImeProcessor ime, IMyImeActiveControl box, char c)
    {
        if (box == null || box != ime.m_activeTextElement)
            return;

        // During a composition, control characters are dropped, as Firefox and Chrome drop undo and redo. The preview
        // is in the box's text but not in its undo history: after an undo, a redo or an indent, the IME's next update
        // would remove characters that are no longer the preview. The IME takes Backspace and Enter itself; Ctrl+Z,
        // Ctrl+Y and Tab get here when it leaves them to the window.
        if (ime.m_isComposing && char.IsControl(c))
            return;

        // Single-line boxes take no Enter or Tab characters in English mode: EnterPressed would be raised a second
        // time. The other control characters are Ctrl shortcuts and Esc, which the boxes take from the keyboard state.
        var multiline = box is MyGuiControlMultilineEditableText;
        if (!char.IsControl(c) || (c == '\t' && multiline))
            box.InsertChar(true, c);
        else switch (c)
        {
            case '\b':
                box.KeypressBackspace(true);
                break;
            case '\u001a':
                box.KeypressUndo();
                break;
            case '\u0019':
                box.KeypressRedo();
                break;
            case '\r' when multiline:
                box.KeypressEnter(true);
                break;
        }
    }

    // The room vanilla counts at the composition start (m_textLimit), counted when it is used
    private static int Room(IMyImeActiveControl box) =>
        Math.Max(0, box.GetMaxLength() - box.GetTextLength() + box.GetSelectionLength());

    private static void Erase(IMyImeActiveControl box, int count)
    {
        if (count > 0)
            box.KeypressBackspaceMultiple(false, count);
    }

    // As vanilla makes the IME drop a composition: off and on again, on the window thread (ImeModePatch)
    private static void Restart(MyImeProcessor ime)
    {
        ime.m_guiControlElement.DeactivateIme();
        ime.m_guiControlElement.ActivateIme();
    }
}
