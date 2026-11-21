using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using VRageRender;

namespace ClientPlugin.Patches;

// MyRenderFont picks straight or premultiplied alpha once per font: premultiplied as soon as one atlas
// file name contains "FontDataPA". The fallback atlases are straight alpha (their transparent texels are
// white), so drawn premultiplied each CJK glyph would be a solid box. The choice is made per glyph instead,
// fallback glyphs being drawn the way the game draws them in its Chinese and Japanese UI. The sprite
// batcher starts a new batch whenever the flag changes, so mixed text costs nothing more.
//
// DrawCharGlyph is AggressiveInlining, and a caller compiled before this patch would keep the original
// inlined. Harmony marks a patched method not inlinable, and the only callers, DrawString and
// DrawStringAligned, are first compiled when the first text is drawn: after the window, at whose creation
// this category is applied.
[HarmonyPatchCategory(Preloader.WindowCreationCategory)]
[HarmonyPatch(typeof(MyRenderFont), nameof(MyRenderFont.DrawCharGlyph))]
internal static class RenderFontPatch
{
    private static readonly FieldInfo PremultipliedAlpha =
        AccessTools.Field(typeof(MyRenderFont), nameof(MyRenderFont.m_premultipliedAlpha));

    // this.m_premultipliedAlpha  ->  IsPremultiplied(this.m_premultipliedAlpha, ginfo)
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        using var code = instructions.GetEnumerator();
        while (code.MoveNext())
        {
            var ci = code.Current;
            yield return ci;
            if (!ci.LoadsField(PremultipliedAlpha))
                continue;

            yield return new CodeInstruction(OpCodes.Ldarg_2);
            yield return CodeInstruction.Call(typeof(RenderFontPatch), nameof(IsPremultiplied));
            while (code.MoveNext())
                yield return code.Current;
            yield break;
        }

        // Drawn the way the font picks, the fallback glyphs would be solid boxes
        throw new InvalidOperationException("No read of MyRenderFont.m_premultipliedAlpha to decide per glyph");
    }

    private static bool IsPremultiplied(bool fontIsPremultiplied, MyGlyphInfo glyph) =>
        fontIsPremultiplied && glyph.nBitmapID < FallbackFont.FirstBitmapId;
}
