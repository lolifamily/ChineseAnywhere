using HarmonyLib;
using VRageRender;

namespace ClientPlugin.Patches;

// Both sides build their fonts with this constructor: MyFontDefinition for measuring and wrapping,
// MyRender11 for drawing. Merging the fallback glyphs right here keeps the two in agreement.
[HarmonyPatchCategory(Preloader.WindowCreationCategory)]
[HarmonyPatch(typeof(MyFont), MethodType.Constructor, typeof(string), typeof(int), typeof(bool))]
internal static class FontConstructorPatch
{
    private static void Postfix(MyFont __instance, string fontFilePath, bool dummyFont)
    {
        // Dummy fonts are empty shells: the renderer's loading placeholder and FallbackFont's sources
        if (!dummyFont)
            FallbackFont.For(fontFilePath, __instance.FontDirectory).MergeInto(__instance);
    }
}
