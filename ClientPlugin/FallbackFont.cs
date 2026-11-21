using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using VRage.FileSystem;
using VRageRender;

namespace ClientPlugin;

// The characters a game font lacks, taken from the game's own Chinese font (GB2312 and CJK punctuation),
// then from its Japanese font (kana, Japanese kanji). Glyphs a font has are never replaced, so the English
// UI keeps its exact look, kerning and icons included.
internal sealed class FallbackFont
{
    // Fallback atlases are numbered from here: far above any font's own pages (numbered from 0),
    // and every id must fit MyGlyphInfo.nBitmapID, a ushort
    public const int FirstBitmapId = 65000;

    // The game's CJK glyphs come in two styles, each in the folder of its name: white_shadow with a dark shadow,
    // white without
    private static readonly Lazy<FallbackFont> White = new(() => new FallbackFont("white"));
    private static readonly Lazy<FallbackFont> WhiteShadow = new(() => new FallbackFont("white_shadow"));

    // The glyphs font definitions pair with the files they load (FontDefinitionInitPatch), by the path as the game
    // hands it to MyFont: taken unchanged from the definition on both sides, and what the renderer caches fonts by
    private static readonly ConcurrentDictionary<string, Lazy<FallbackFont>> Declared = new(StringComparer.Ordinal);

    private readonly List<MyFontBitmapInfo> atlases = [];
    private readonly List<KeyValuePair<char, MyGlyphInfo>> glyphs = [];

    // A font definition pairs the font file with the style of its Chinese variant
    public static void Declare(string fontFilePath, string chineseFilePath)
    {
        if (!string.IsNullOrEmpty(fontFilePath))
            Declared[fontFilePath] = OfStyle(Path.GetFileName(Path.GetDirectoryName(chineseFilePath)));
    }

    // As the font's definitions pair it, else as the game pairs its own fonts: those in white_shadow with
    // white_shadow glyphs, everything else with white
    public static FallbackFont For(string fontFilePath, string fontDirectory) =>
        (Declared.TryGetValue(fontFilePath, out var declared) ? declared : OfStyle(Path.GetFileName(fontDirectory))).Value;

    private static Lazy<FallbackFont> OfStyle(string folder) =>
        string.Equals(folder, "white_shadow", StringComparison.OrdinalIgnoreCase) ? WhiteShadow : White;

    private FallbackFont(string style)
    {
        var folder = Path.Combine(MyFileSystem.ContentPath, "Fonts", style);
        var chinese = Parse(Path.Combine(folder, "FontDataCH.xml"));
        var japanese = Parse(Path.Combine(folder, "FontDataJP.xml"));

        var chineseAtlases = AddAtlases(chinese, folder);
        foreach (var glyph in chinese.m_glyphInfoByChar)
            Add(glyph.Key, glyph.Value, chineseAtlases);

        // Japanese cells are 50 px tall with the baseline at 31, the Chinese and English ones 45 and 30.
        // Cropping 1 row at the top and 4 at the bottom gives kana and kanji the same box, so a line holding
        // them keeps the height of its font. Those rows hold no ink in the game's atlases (in white_shadow
        // only BC7 bleed from the neighbouring cells).
        var top = japanese.Baseline - chinese.Baseline;
        int height = chinese.m_glyphInfoByChar['a'].pxHeight;
        int japaneseHeight = japanese.m_glyphInfoByChar['a'].pxHeight;
        var japaneseAtlases = AddAtlases(japanese, folder);
        foreach (var glyph in japanese.m_glyphInfoByChar)
        {
            if (chinese.m_glyphInfoByChar.ContainsKey(glyph.Key))
                continue;

            var copy = Add(glyph.Key, glyph.Value, japaneseAtlases);
            if (copy.pxHeight != japaneseHeight)
                continue;

            copy.pxLocY = (ushort)(copy.pxLocY + top);
            copy.pxHeight = (byte)height;
        }
    }

    // Adds the characters the font lacks and the atlases they are drawn from
    public void MergeInto(MyFont font)
    {
        var used = new bool[atlases.Count];
        foreach (var glyph in glyphs)
        {
            if (font.m_glyphInfoByChar.ContainsKey(glyph.Key))
                continue;

            font.m_glyphInfoByChar.Add(glyph.Key, glyph.Value);
            used[glyph.Value.nBitmapID - FirstBitmapId] = true;
        }

        // Each atlas listed is a texture the renderer loads for this font
        for (var i = 0; i < used.Length; i++)
            if (used[i])
                font.BitmapInfoById.Add(FirstBitmapId + i, atlases[i]);
    }

    // An empty font filled by the game's own parser; FontConstructorPatch skips dummy fonts
    private static MyFont Parse(string path)
    {
        var font = new MyFont(path, dummyFont: true);
        font.LoadFontXML(path);
        return font;
    }

    // One slot per bitmap id of the source font (small ids from 0), returns the slot of id 0
    private int AddAtlases(MyFont source, string folder)
    {
        var first = atlases.Count;
        foreach (var bitmap in source.BitmapInfoById)
        {
            while (atlases.Count <= first + bitmap.Key)
                atlases.Add(default);

            // Absolute: the renderer combines it with the folder of the font it ends up in
            atlases[first + bitmap.Key] = new MyFontBitmapInfo
            {
                strFilename = Path.Combine(folder, bitmap.Value.strFilename),
                nX = bitmap.Value.nX,
                nY = bitmap.Value.nY,
            };
        }

        return first;
    }

    private MyGlyphInfo Add(char character, MyGlyphInfo glyph, int firstAtlas)
    {
        var copy = new MyGlyphInfo
        {
            nBitmapID = (ushort)(FirstBitmapId + firstAtlas + glyph.nBitmapID),
            pxLocX = glyph.pxLocX,
            pxLocY = glyph.pxLocY,
            pxWidth = glyph.pxWidth,
            pxHeight = glyph.pxHeight,
            pxAdvanceWidth = glyph.pxAdvanceWidth,
            pxLeftSideBearing = glyph.pxLeftSideBearing,
            pxHeightOffset = glyph.pxHeightOffset,
        };
        glyphs.Add(new KeyValuePair<char, MyGlyphInfo>(character, copy));
        return copy;
    }
}
