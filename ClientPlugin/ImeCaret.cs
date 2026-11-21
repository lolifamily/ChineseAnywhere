using System;
using Sandbox.Graphics;
using Sandbox.Graphics.GUI;
using VRage;
using VRage.Input;
using VRageMath;
using Point = System.Drawing.Point;
using Rectangle = System.Drawing.Rectangle;

namespace ClientPlugin;

// Where the vanilla text box the IME serves shows the composition, in the game window's client pixels, for the
// IME to put its candidate window by (ImeProcessorPatch). Measured on the game thread, which owns the boxes,
// right after the IME's queued edits are applied to them; read on the window thread.
internal sealed class ImeCaret
{
    // Top left of each character from the composition start to the caret: Characters[i] is composition
    // character i, the last one the caret behind the composition
    public readonly Point[] Characters;
    public readonly int LineHeight;
    public readonly Rectangle Box;

    // The IME inserts its preview at the caret, so the composition is the compositionLength characters before it
    private ImeCaret(MyGuiControlBase box, int caret, int compositionLength, Func<int, Vector2> topLeftOf, float lineHeight)
    {
        var start = Math.Max(0, caret - compositionLength);
        Characters = new Point[caret - start + 1];
        for (var i = 0; i < Characters.Length; i++)
            Characters[i] = ToClient(topLeftOf(start + i));

        var topLeft = box.GetPositionAbsoluteTopLeft();
        Point boxTopLeft = ToClient(topLeft), boxBottomRight = ToClient(topLeft + box.Size);
        Box = Rectangle.FromLTRB(boxTopLeft.X, boxTopLeft.Y, boxBottomRight.X, boxBottomRight.Y);
        LineHeight = ToClient(topLeft + new Vector2(0f, lineHeight)).Y - boxTopLeft.Y;
    }

    public static ImeCaret Measure(IMyImeActiveControl box, int compositionLength) => box switch
    {
        MyGuiControlTextbox textbox => Measure(textbox, compositionLength),
        MyGuiControlMultilineEditableText editor => Measure(editor, compositionLength),
        _ => null,
    };

    // As Draw draws the caret: at the text area's left plus GetCarriageOffset, as high as the box
    private static ImeCaret Measure(MyGuiControlTextbox box, int compositionLength)
    {
        var topLeft = box.GetPositionAbsoluteTopLeft();
        var textLeft = topLeft.X + box.m_textAreaRelative.LeftTop.X;
        return new ImeCaret(box, box.CarriagePositionIndex, compositionLength,
            i => new Vector2(textLeft + box.GetCarriageOffset(i), topLeft.Y), box.Size.Y);
    }

    // As Draw draws the caret: at the text area's top left plus GetCarriageOffset, CarriageOffset and the border,
    // one line high. GetCarriageOffset is protected virtual, which Pulsar's publicizer leaves alone; the IME's
    // GetCarriagePosition(shiftX) is GetCarriageOffset(CarriagePositionIndex - shiftX) moved 0.025 down.
    private static ImeCaret Measure(MyGuiControlMultilineEditableText box, int compositionLength)
    {
        var caret = box.CarriagePositionIndex;
        var textTopLeft = box.GetPositionAbsoluteTopLeft() + box.TextPadding.TopLeftOffset + box.CarriageOffset
                              + box.BorderSizeNormalized - new Vector2(0f, 0.025f);
        return new ImeCaret(box, caret, compositionLength,
            i => textTopLeft + box.GetCarriagePosition(caret - i), box.GetCarriageHeight());
    }

    // The inverse of how the game turns the mouse position into GUI coordinates:
    // MyGuiManager.GetNormalizedMousePosition of MyVRageInput.GetMousePosition, which undoes the UI scale
    private static Point ToClient(Vector2 normalized)
    {
        var screen = MyGuiManager.GetScreenCoordinateFromNormalizedCoordinate(normalized);
        var fullscreen = MyGuiManager.GetFullscreenRectangle();
        var area = MyInput.Static.GetMouseAreaSize();
        var scale = MyGuiManager.UIScale;
        var client = screen * area / new Vector2(fullscreen.Width, fullscreen.Height) * scale + area / 2f * (1f - scale);
        return new Point((int)Math.Round(client.X), (int)Math.Round(client.Y));
    }
}
