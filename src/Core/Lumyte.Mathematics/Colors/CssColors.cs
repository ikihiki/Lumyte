using System.Collections.Frozen;

namespace Lumyte.Mathematics.Colors;

/// <summary>Provides CSS named colors as immutable straight-alpha sRGB eight-bit code values, not normalized components.</summary>
public static class CssColors
{
    /// <summary>The CSS aliceblue color, #F0F8FF, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 AliceBlue = new(240, 248, 255, 255);

    /// <summary>The CSS antiquewhite color, #FAEBD7, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 AntiqueWhite = new(250, 235, 215, 255);

    /// <summary>The CSS aqua color, #00FFFF, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 Aqua = new(0, 255, 255, 255);

    /// <summary>The CSS aquamarine color, #7FFFD4, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 Aquamarine = new(127, 255, 212, 255);

    /// <summary>The CSS azure color, #F0FFFF, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 Azure = new(240, 255, 255, 255);

    /// <summary>The CSS beige color, #F5F5DC, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 Beige = new(245, 245, 220, 255);

    /// <summary>The CSS bisque color, #FFE4C4, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 Bisque = new(255, 228, 196, 255);

    /// <summary>The CSS black color, #000000, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 Black = new(0, 0, 0, 255);

    /// <summary>The CSS blanchedalmond color, #FFEBCD, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 BlanchedAlmond = new(255, 235, 205, 255);

    /// <summary>The CSS blue color, #0000FF, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 Blue = new(0, 0, 255, 255);

    /// <summary>The CSS blueviolet color, #8A2BE2, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 BlueViolet = new(138, 43, 226, 255);

    /// <summary>The CSS brown color, #A52A2A, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 Brown = new(165, 42, 42, 255);

    /// <summary>The CSS burlywood color, #DEB887, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 BurlyWood = new(222, 184, 135, 255);

    /// <summary>The CSS cadetblue color, #5F9EA0, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 CadetBlue = new(95, 158, 160, 255);

    /// <summary>The CSS chartreuse color, #7FFF00, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 Chartreuse = new(127, 255, 0, 255);

    /// <summary>The CSS chocolate color, #D2691E, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 Chocolate = new(210, 105, 30, 255);

    /// <summary>The CSS coral color, #FF7F50, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 Coral = new(255, 127, 80, 255);

    /// <summary>The CSS cornflowerblue color, #6495ED, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 CornflowerBlue = new(100, 149, 237, 255);

    /// <summary>The CSS cornsilk color, #FFF8DC, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 Cornsilk = new(255, 248, 220, 255);

    /// <summary>The CSS crimson color, #DC143C, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 Crimson = new(220, 20, 60, 255);

    /// <summary>The CSS cyan color, #00FFFF, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 Cyan = new(0, 255, 255, 255);

    /// <summary>The CSS darkblue color, #00008B, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 DarkBlue = new(0, 0, 139, 255);

    /// <summary>The CSS darkcyan color, #008B8B, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 DarkCyan = new(0, 139, 139, 255);

    /// <summary>The CSS darkgoldenrod color, #B8860B, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 DarkGoldenrod = new(184, 134, 11, 255);

    /// <summary>The CSS darkgray color, #A9A9A9, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 DarkGray = new(169, 169, 169, 255);

    /// <summary>The CSS darkgreen color, #006400, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 DarkGreen = new(0, 100, 0, 255);

    /// <summary>The CSS darkgrey color, #A9A9A9, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 DarkGrey = new(169, 169, 169, 255);

    /// <summary>The CSS darkkhaki color, #BDB76B, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 DarkKhaki = new(189, 183, 107, 255);

    /// <summary>The CSS darkmagenta color, #8B008B, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 DarkMagenta = new(139, 0, 139, 255);

    /// <summary>The CSS darkolivegreen color, #556B2F, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 DarkOliveGreen = new(85, 107, 47, 255);

    /// <summary>The CSS darkorange color, #FF8C00, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 DarkOrange = new(255, 140, 0, 255);

    /// <summary>The CSS darkorchid color, #9932CC, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 DarkOrchid = new(153, 50, 204, 255);

    /// <summary>The CSS darkred color, #8B0000, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 DarkRed = new(139, 0, 0, 255);

    /// <summary>The CSS darksalmon color, #E9967A, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 DarkSalmon = new(233, 150, 122, 255);

    /// <summary>The CSS darkseagreen color, #8FBC8F, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 DarkSeaGreen = new(143, 188, 143, 255);

    /// <summary>The CSS darkslateblue color, #483D8B, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 DarkSlateBlue = new(72, 61, 139, 255);

    /// <summary>The CSS darkslategray color, #2F4F4F, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 DarkSlateGray = new(47, 79, 79, 255);

    /// <summary>The CSS darkslategrey color, #2F4F4F, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 DarkSlateGrey = new(47, 79, 79, 255);

    /// <summary>The CSS darkturquoise color, #00CED1, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 DarkTurquoise = new(0, 206, 209, 255);

    /// <summary>The CSS darkviolet color, #9400D3, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 DarkViolet = new(148, 0, 211, 255);

    /// <summary>The CSS deeppink color, #FF1493, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 DeepPink = new(255, 20, 147, 255);

    /// <summary>The CSS deepskyblue color, #00BFFF, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 DeepSkyBlue = new(0, 191, 255, 255);

    /// <summary>The CSS dimgray color, #696969, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 DimGray = new(105, 105, 105, 255);

    /// <summary>The CSS dimgrey color, #696969, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 DimGrey = new(105, 105, 105, 255);

    /// <summary>The CSS dodgerblue color, #1E90FF, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 DodgerBlue = new(30, 144, 255, 255);

    /// <summary>The CSS firebrick color, #B22222, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 Firebrick = new(178, 34, 34, 255);

    /// <summary>The CSS floralwhite color, #FFFAF0, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 FloralWhite = new(255, 250, 240, 255);

    /// <summary>The CSS forestgreen color, #228B22, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 ForestGreen = new(34, 139, 34, 255);

    /// <summary>The CSS fuchsia color, #FF00FF, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 Fuchsia = new(255, 0, 255, 255);

    /// <summary>The CSS gainsboro color, #DCDCDC, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 Gainsboro = new(220, 220, 220, 255);

    /// <summary>The CSS ghostwhite color, #F8F8FF, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 GhostWhite = new(248, 248, 255, 255);

    /// <summary>The CSS gold color, #FFD700, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 Gold = new(255, 215, 0, 255);

    /// <summary>The CSS goldenrod color, #DAA520, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 Goldenrod = new(218, 165, 32, 255);

    /// <summary>The CSS gray color, #808080, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 Gray = new(128, 128, 128, 255);

    /// <summary>The CSS green color, #008000, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 Green = new(0, 128, 0, 255);

    /// <summary>The CSS greenyellow color, #ADFF2F, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 GreenYellow = new(173, 255, 47, 255);

    /// <summary>The CSS grey color, #808080, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 Grey = new(128, 128, 128, 255);

    /// <summary>The CSS honeydew color, #F0FFF0, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 Honeydew = new(240, 255, 240, 255);

    /// <summary>The CSS hotpink color, #FF69B4, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 HotPink = new(255, 105, 180, 255);

    /// <summary>The CSS indianred color, #CD5C5C, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 IndianRed = new(205, 92, 92, 255);

    /// <summary>The CSS indigo color, #4B0082, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 Indigo = new(75, 0, 130, 255);

    /// <summary>The CSS ivory color, #FFFFF0, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 Ivory = new(255, 255, 240, 255);

    /// <summary>The CSS khaki color, #F0E68C, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 Khaki = new(240, 230, 140, 255);

    /// <summary>The CSS lavender color, #E6E6FA, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 Lavender = new(230, 230, 250, 255);

    /// <summary>The CSS lavenderblush color, #FFF0F5, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 LavenderBlush = new(255, 240, 245, 255);

    /// <summary>The CSS lawngreen color, #7CFC00, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 LawnGreen = new(124, 252, 0, 255);

    /// <summary>The CSS lemonchiffon color, #FFFACD, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 LemonChiffon = new(255, 250, 205, 255);

    /// <summary>The CSS lightblue color, #ADD8E6, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 LightBlue = new(173, 216, 230, 255);

    /// <summary>The CSS lightcoral color, #F08080, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 LightCoral = new(240, 128, 128, 255);

    /// <summary>The CSS lightcyan color, #E0FFFF, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 LightCyan = new(224, 255, 255, 255);

    /// <summary>The CSS lightgoldenrodyellow color, #FAFAD2, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 LightGoldenrodYellow = new(250, 250, 210, 255);

    /// <summary>The CSS lightgray color, #D3D3D3, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 LightGray = new(211, 211, 211, 255);

    /// <summary>The CSS lightgreen color, #90EE90, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 LightGreen = new(144, 238, 144, 255);

    /// <summary>The CSS lightgrey color, #D3D3D3, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 LightGrey = new(211, 211, 211, 255);

    /// <summary>The CSS lightpink color, #FFB6C1, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 LightPink = new(255, 182, 193, 255);

    /// <summary>The CSS lightsalmon color, #FFA07A, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 LightSalmon = new(255, 160, 122, 255);

    /// <summary>The CSS lightseagreen color, #20B2AA, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 LightSeaGreen = new(32, 178, 170, 255);

    /// <summary>The CSS lightskyblue color, #87CEFA, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 LightSkyBlue = new(135, 206, 250, 255);

    /// <summary>The CSS lightslategray color, #778899, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 LightSlateGray = new(119, 136, 153, 255);

    /// <summary>The CSS lightslategrey color, #778899, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 LightSlateGrey = new(119, 136, 153, 255);

    /// <summary>The CSS lightsteelblue color, #B0C4DE, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 LightSteelBlue = new(176, 196, 222, 255);

    /// <summary>The CSS lightyellow color, #FFFFE0, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 LightYellow = new(255, 255, 224, 255);

    /// <summary>The CSS lime color, #00FF00, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 Lime = new(0, 255, 0, 255);

    /// <summary>The CSS limegreen color, #32CD32, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 LimeGreen = new(50, 205, 50, 255);

    /// <summary>The CSS linen color, #FAF0E6, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 Linen = new(250, 240, 230, 255);

    /// <summary>The CSS magenta color, #FF00FF, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 Magenta = new(255, 0, 255, 255);

    /// <summary>The CSS maroon color, #800000, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 Maroon = new(128, 0, 0, 255);

    /// <summary>The CSS mediumaquamarine color, #66CDAA, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 MediumAquamarine = new(102, 205, 170, 255);

    /// <summary>The CSS mediumblue color, #0000CD, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 MediumBlue = new(0, 0, 205, 255);

    /// <summary>The CSS mediumorchid color, #BA55D3, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 MediumOrchid = new(186, 85, 211, 255);

    /// <summary>The CSS mediumpurple color, #9370DB, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 MediumPurple = new(147, 112, 219, 255);

    /// <summary>The CSS mediumseagreen color, #3CB371, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 MediumSeaGreen = new(60, 179, 113, 255);

    /// <summary>The CSS mediumslateblue color, #7B68EE, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 MediumSlateBlue = new(123, 104, 238, 255);

    /// <summary>The CSS mediumspringgreen color, #00FA9A, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 MediumSpringGreen = new(0, 250, 154, 255);

    /// <summary>The CSS mediumturquoise color, #48D1CC, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 MediumTurquoise = new(72, 209, 204, 255);

    /// <summary>The CSS mediumvioletred color, #C71585, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 MediumVioletRed = new(199, 21, 133, 255);

    /// <summary>The CSS midnightblue color, #191970, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 MidnightBlue = new(25, 25, 112, 255);

    /// <summary>The CSS mintcream color, #F5FFFA, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 MintCream = new(245, 255, 250, 255);

    /// <summary>The CSS mistyrose color, #FFE4E1, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 MistyRose = new(255, 228, 225, 255);

    /// <summary>The CSS moccasin color, #FFE4B5, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 Moccasin = new(255, 228, 181, 255);

    /// <summary>The CSS navajowhite color, #FFDEAD, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 NavajoWhite = new(255, 222, 173, 255);

    /// <summary>The CSS navy color, #000080, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 Navy = new(0, 0, 128, 255);

    /// <summary>The CSS oldlace color, #FDF5E6, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 OldLace = new(253, 245, 230, 255);

    /// <summary>The CSS olive color, #808000, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 Olive = new(128, 128, 0, 255);

    /// <summary>The CSS olivedrab color, #6B8E23, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 OliveDrab = new(107, 142, 35, 255);

    /// <summary>The CSS orange color, #FFA500, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 Orange = new(255, 165, 0, 255);

    /// <summary>The CSS orangered color, #FF4500, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 OrangeRed = new(255, 69, 0, 255);

    /// <summary>The CSS orchid color, #DA70D6, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 Orchid = new(218, 112, 214, 255);

    /// <summary>The CSS palegoldenrod color, #EEE8AA, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 PaleGoldenrod = new(238, 232, 170, 255);

    /// <summary>The CSS palegreen color, #98FB98, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 PaleGreen = new(152, 251, 152, 255);

    /// <summary>The CSS paleturquoise color, #AFEEEE, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 PaleTurquoise = new(175, 238, 238, 255);

    /// <summary>The CSS palevioletred color, #DB7093, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 PaleVioletRed = new(219, 112, 147, 255);

    /// <summary>The CSS papayawhip color, #FFEFD5, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 PapayaWhip = new(255, 239, 213, 255);

    /// <summary>The CSS peachpuff color, #FFDAB9, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 PeachPuff = new(255, 218, 185, 255);

    /// <summary>The CSS peru color, #CD853F, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 Peru = new(205, 133, 63, 255);

    /// <summary>The CSS pink color, #FFC0CB, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 Pink = new(255, 192, 203, 255);

    /// <summary>The CSS plum color, #DDA0DD, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 Plum = new(221, 160, 221, 255);

    /// <summary>The CSS powderblue color, #B0E0E6, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 PowderBlue = new(176, 224, 230, 255);

    /// <summary>The CSS purple color, #800080, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 Purple = new(128, 0, 128, 255);

    /// <summary>The CSS rebeccapurple color, #663399, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 RebeccaPurple = new(102, 51, 153, 255);

    /// <summary>The CSS red color, #FF0000, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 Red = new(255, 0, 0, 255);

    /// <summary>The CSS rosybrown color, #BC8F8F, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 RosyBrown = new(188, 143, 143, 255);

    /// <summary>The CSS royalblue color, #4169E1, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 RoyalBlue = new(65, 105, 225, 255);

    /// <summary>The CSS saddlebrown color, #8B4513, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 SaddleBrown = new(139, 69, 19, 255);

    /// <summary>The CSS salmon color, #FA8072, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 Salmon = new(250, 128, 114, 255);

    /// <summary>The CSS sandybrown color, #F4A460, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 SandyBrown = new(244, 164, 96, 255);

    /// <summary>The CSS seagreen color, #2E8B57, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 SeaGreen = new(46, 139, 87, 255);

    /// <summary>The CSS seashell color, #FFF5EE, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 Seashell = new(255, 245, 238, 255);

    /// <summary>The CSS sienna color, #A0522D, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 Sienna = new(160, 82, 45, 255);

    /// <summary>The CSS silver color, #C0C0C0, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 Silver = new(192, 192, 192, 255);

    /// <summary>The CSS skyblue color, #87CEEB, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 SkyBlue = new(135, 206, 235, 255);

    /// <summary>The CSS slateblue color, #6A5ACD, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 SlateBlue = new(106, 90, 205, 255);

    /// <summary>The CSS slategray color, #708090, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 SlateGray = new(112, 128, 144, 255);

    /// <summary>The CSS slategrey color, #708090, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 SlateGrey = new(112, 128, 144, 255);

    /// <summary>The CSS snow color, #FFFAFA, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 Snow = new(255, 250, 250, 255);

    /// <summary>The CSS springgreen color, #00FF7F, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 SpringGreen = new(0, 255, 127, 255);

    /// <summary>The CSS steelblue color, #4682B4, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 SteelBlue = new(70, 130, 180, 255);

    /// <summary>The CSS tan color, #D2B48C, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 Tan = new(210, 180, 140, 255);

    /// <summary>The CSS teal color, #008080, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 Teal = new(0, 128, 128, 255);

    /// <summary>The CSS thistle color, #D8BFD8, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 Thistle = new(216, 191, 216, 255);

    /// <summary>The CSS tomato color, #FF6347, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 Tomato = new(255, 99, 71, 255);

    /// <summary>The CSS turquoise color, #40E0D0, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 Turquoise = new(64, 224, 208, 255);

    /// <summary>The CSS violet color, #EE82EE, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 Violet = new(238, 130, 238, 255);

    /// <summary>The CSS wheat color, #F5DEB3, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 Wheat = new(245, 222, 179, 255);

    /// <summary>The CSS white color, #FFFFFF, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 White = new(255, 255, 255, 255);

    /// <summary>The CSS whitesmoke color, #F5F5F5, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 WhiteSmoke = new(245, 245, 245, 255);

    /// <summary>The CSS yellow color, #FFFF00, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 Yellow = new(255, 255, 0, 255);

    /// <summary>The CSS yellowgreen color, #9ACD32, with opaque alpha code 255.</summary>
    public static readonly SrgbRgba8 YellowGreen = new(154, 205, 50, 255);

    /// <summary>The separate CSS transparent keyword: transparent black, RGBA codes (0,0,0,0).</summary>
    public static readonly SrgbRgba8 Transparent = new(0, 0, 0, 0);

    /// <summary>Gets the immutable 148 named colors plus transparent, keyed by lowercase CSS keyword with ordinal case-insensitive lookup.</summary>
    public static IReadOnlyDictionary<string, SrgbRgba8> All { get; } = CreateAll();

    /// <summary>Looks up an ASCII named keyword without parsing CSS, trimming whitespace, or resolving contextual colors.</summary>
    /// <param name="name">The ASCII keyword, compared using ordinal case-insensitive equality.</param>
    /// <param name="color">The eight-bit code value, or default for null or an unknown keyword.</param>
    /// <returns>Whether the keyword has a fixed palette value.</returns>
    public static bool TryGet(string? name, out SrgbRgba8 color)
    {
        color = default;
        if (name is null)
        {
            return false;
        }

        foreach (char character in name)
        {
            if (character > 127)
            {
                return false;
            }
        }

        return All.TryGetValue(name, out color);
    }

    private static FrozenDictionary<string, SrgbRgba8> CreateAll() => new Dictionary<string, SrgbRgba8>(StringComparer.OrdinalIgnoreCase)
    {
        ["aliceblue"] = AliceBlue,
        ["antiquewhite"] = AntiqueWhite,
        ["aqua"] = Aqua,
        ["aquamarine"] = Aquamarine,
        ["azure"] = Azure,
        ["beige"] = Beige,
        ["bisque"] = Bisque,
        ["black"] = Black,
        ["blanchedalmond"] = BlanchedAlmond,
        ["blue"] = Blue,
        ["blueviolet"] = BlueViolet,
        ["brown"] = Brown,
        ["burlywood"] = BurlyWood,
        ["cadetblue"] = CadetBlue,
        ["chartreuse"] = Chartreuse,
        ["chocolate"] = Chocolate,
        ["coral"] = Coral,
        ["cornflowerblue"] = CornflowerBlue,
        ["cornsilk"] = Cornsilk,
        ["crimson"] = Crimson,
        ["cyan"] = Cyan,
        ["darkblue"] = DarkBlue,
        ["darkcyan"] = DarkCyan,
        ["darkgoldenrod"] = DarkGoldenrod,
        ["darkgray"] = DarkGray,
        ["darkgreen"] = DarkGreen,
        ["darkgrey"] = DarkGrey,
        ["darkkhaki"] = DarkKhaki,
        ["darkmagenta"] = DarkMagenta,
        ["darkolivegreen"] = DarkOliveGreen,
        ["darkorange"] = DarkOrange,
        ["darkorchid"] = DarkOrchid,
        ["darkred"] = DarkRed,
        ["darksalmon"] = DarkSalmon,
        ["darkseagreen"] = DarkSeaGreen,
        ["darkslateblue"] = DarkSlateBlue,
        ["darkslategray"] = DarkSlateGray,
        ["darkslategrey"] = DarkSlateGrey,
        ["darkturquoise"] = DarkTurquoise,
        ["darkviolet"] = DarkViolet,
        ["deeppink"] = DeepPink,
        ["deepskyblue"] = DeepSkyBlue,
        ["dimgray"] = DimGray,
        ["dimgrey"] = DimGrey,
        ["dodgerblue"] = DodgerBlue,
        ["firebrick"] = Firebrick,
        ["floralwhite"] = FloralWhite,
        ["forestgreen"] = ForestGreen,
        ["fuchsia"] = Fuchsia,
        ["gainsboro"] = Gainsboro,
        ["ghostwhite"] = GhostWhite,
        ["gold"] = Gold,
        ["goldenrod"] = Goldenrod,
        ["gray"] = Gray,
        ["green"] = Green,
        ["greenyellow"] = GreenYellow,
        ["grey"] = Grey,
        ["honeydew"] = Honeydew,
        ["hotpink"] = HotPink,
        ["indianred"] = IndianRed,
        ["indigo"] = Indigo,
        ["ivory"] = Ivory,
        ["khaki"] = Khaki,
        ["lavender"] = Lavender,
        ["lavenderblush"] = LavenderBlush,
        ["lawngreen"] = LawnGreen,
        ["lemonchiffon"] = LemonChiffon,
        ["lightblue"] = LightBlue,
        ["lightcoral"] = LightCoral,
        ["lightcyan"] = LightCyan,
        ["lightgoldenrodyellow"] = LightGoldenrodYellow,
        ["lightgray"] = LightGray,
        ["lightgreen"] = LightGreen,
        ["lightgrey"] = LightGrey,
        ["lightpink"] = LightPink,
        ["lightsalmon"] = LightSalmon,
        ["lightseagreen"] = LightSeaGreen,
        ["lightskyblue"] = LightSkyBlue,
        ["lightslategray"] = LightSlateGray,
        ["lightslategrey"] = LightSlateGrey,
        ["lightsteelblue"] = LightSteelBlue,
        ["lightyellow"] = LightYellow,
        ["lime"] = Lime,
        ["limegreen"] = LimeGreen,
        ["linen"] = Linen,
        ["magenta"] = Magenta,
        ["maroon"] = Maroon,
        ["mediumaquamarine"] = MediumAquamarine,
        ["mediumblue"] = MediumBlue,
        ["mediumorchid"] = MediumOrchid,
        ["mediumpurple"] = MediumPurple,
        ["mediumseagreen"] = MediumSeaGreen,
        ["mediumslateblue"] = MediumSlateBlue,
        ["mediumspringgreen"] = MediumSpringGreen,
        ["mediumturquoise"] = MediumTurquoise,
        ["mediumvioletred"] = MediumVioletRed,
        ["midnightblue"] = MidnightBlue,
        ["mintcream"] = MintCream,
        ["mistyrose"] = MistyRose,
        ["moccasin"] = Moccasin,
        ["navajowhite"] = NavajoWhite,
        ["navy"] = Navy,
        ["oldlace"] = OldLace,
        ["olive"] = Olive,
        ["olivedrab"] = OliveDrab,
        ["orange"] = Orange,
        ["orangered"] = OrangeRed,
        ["orchid"] = Orchid,
        ["palegoldenrod"] = PaleGoldenrod,
        ["palegreen"] = PaleGreen,
        ["paleturquoise"] = PaleTurquoise,
        ["palevioletred"] = PaleVioletRed,
        ["papayawhip"] = PapayaWhip,
        ["peachpuff"] = PeachPuff,
        ["peru"] = Peru,
        ["pink"] = Pink,
        ["plum"] = Plum,
        ["powderblue"] = PowderBlue,
        ["purple"] = Purple,
        ["rebeccapurple"] = RebeccaPurple,
        ["red"] = Red,
        ["rosybrown"] = RosyBrown,
        ["royalblue"] = RoyalBlue,
        ["saddlebrown"] = SaddleBrown,
        ["salmon"] = Salmon,
        ["sandybrown"] = SandyBrown,
        ["seagreen"] = SeaGreen,
        ["seashell"] = Seashell,
        ["sienna"] = Sienna,
        ["silver"] = Silver,
        ["skyblue"] = SkyBlue,
        ["slateblue"] = SlateBlue,
        ["slategray"] = SlateGray,
        ["slategrey"] = SlateGrey,
        ["snow"] = Snow,
        ["springgreen"] = SpringGreen,
        ["steelblue"] = SteelBlue,
        ["tan"] = Tan,
        ["teal"] = Teal,
        ["thistle"] = Thistle,
        ["tomato"] = Tomato,
        ["turquoise"] = Turquoise,
        ["violet"] = Violet,
        ["wheat"] = Wheat,
        ["white"] = White,
        ["whitesmoke"] = WhiteSmoke,
        ["yellow"] = Yellow,
        ["yellowgreen"] = YellowGreen,
        ["transparent"] = Transparent,
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
}
