using System.Linq;
using HarmonyLib;
using VRage;
using VRage.Game;
using VRage.Game.Definitions;

namespace ClientPlugin.Patches;

// A font definition says, in its Chinese variant, which of the game's CJK glyphs go with its font: white_shadow ones
// with the shadowed fonts, white ones with the rest. FallbackFont goes by that for every file the definition may
// load. The variant of a font loaded from a mod (<Path>) is never applied, yet it still names the glyphs that suit
// the font: BuildInfo's outlined fonts name white_shadow, from folders of their own. Definitions are read before the
// game builds any font they name, on either side.
//
// nameof compiles thanks to the public Init(builder, modContext) of MyDefinitionBase; the argument type picks this
// protected override, virtual and so left alone by Pulsar's publicizer.
[HarmonyPatchCategory(Preloader.WindowCreationCategory)]
[HarmonyPatch(typeof(MyFontDefinition), nameof(MyFontDefinition.Init), typeof(MyObjectBuilder_DefinitionBase))]
internal static class FontDefinitionInitPatch
{
    private static void Prefix(MyObjectBuilder_DefinitionBase builder)
    {
        if (builder is not MyObjectBuilder_FontDefinition font)
            return;

        var chinese = font.LanguageSpecificDefinitions?
            .FirstOrDefault(variant => variant.Language == nameof(MyLanguagesEnum.ChineseChina))?
            .Resources?.FirstOrDefault()?.Path;
        if (chinese == null)
            return;

        FallbackFont.Declare(font.Path, chinese);
        foreach (var resource in font.Resources ?? [])
        {
            FallbackFont.Declare(resource.Path, chinese);
            FallbackFont.Declare(resource.SwappedPath, chinese);
        }
    }
}
