using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace CustomStyEditor.Services
{
    /// <summary>
    /// Stylesheet colors. \Color takes either an "OLE" decimal value (R + G*256 + B*65536, so
    /// the bytes are in B-G-R order) or "x" + RRGGBB hex. \ColorName takes a name from the
    /// ItemColorMappings section of Paratext's Themes\Default\ColorMapping.xml.
    /// Colors here are 0xRRGGBB integers; -1 means unknown.
    /// </summary>
    public static class ColorService
    {
        /// <summary>The "Commonly Used Color Values" table of the Paratext help (OLE value, name).</summary>
        public static readonly KeyValuePair<int, string>[] CommonColors =
        {
            Pair(0, "Black"), Pair(255, "Red"), Pair(65280, "Green"), Pair(16711680, "Blue"),
            Pair(16711935, "Magenta"), Pair(16776960, "Cyan"), Pair(16777215, "White"),
            Pair(12632256, "Light Gray"), Pair(4210752, "Dark Gray"), Pair(8421504, "Gray"),
            Pair(8454143, "Light Yellow"), Pair(65535, "Yellow"), Pair(12639424, "Lime Green"),
            Pair(8421440, "Teal"), Pair(16384, "Dark Green"), Pair(128, "Maroon"), Pair(8388736, "Purple"),
            Pair(33023, "Orange"), Pair(7051175, "Khaki"), Pair(32896, "Olive"), Pair(4210816, "Brown"),
            Pair(8404992, "Navy"),
        };

        private static Dictionary<string, int> _names;   // ColorMapping item name -> RGB

        private static KeyValuePair<int, string> Pair(int ole, string name)
        {
            return new KeyValuePair<int, string>(ole, name);
        }

        public static int OleToRgb(int ole)
        {
            int r = ole & 0xFF, g = (ole >> 8) & 0xFF, b = (ole >> 16) & 0xFF;
            return (r << 16) | (g << 8) | b;
        }

        public static int RgbToOle(int rgb)
        {
            int r = (rgb >> 16) & 0xFF, g = (rgb >> 8) & 0xFF, b = rgb & 0xFF;
            return r + g * 256 + b * 65536;
        }

        /// <summary>The RGB a \Color value stands for, or -1.</summary>
        public static int ParseColorValue(string value)
        {
            if (string.IsNullOrEmpty(value)) return -1;
            string v = value.Trim();
            int n;
            if (v.Length == 7 && (v[0] == 'x' || v[0] == 'X')
                && int.TryParse(v.Substring(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out n))
                return n;
            if (int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out n) && n >= 0 && n <= 0xFFFFFF)
                return OleToRgb(n);
            return -1;
        }

        public static bool IsHexValue(string value)
        {
            return !string.IsNullOrEmpty(value) && (value.Trim().StartsWith("x") || value.Trim().StartsWith("X"));
        }

        public static string FormatOle(int rgb)
        {
            return RgbToOle(rgb).ToString(CultureInfo.InvariantCulture);
        }

        public static string FormatHex(int rgb)
        {
            return "x" + rgb.ToString("X6", CultureInfo.InvariantCulture);
        }

        /// <summary>A comment for a decimal value: the help's color name, else the RGB hex.</summary>
        public static string CommentFor(int rgb)
        {
            int ole = RgbToOle(rgb);
            foreach (var p in CommonColors)
                if (p.Key == ole) return " # " + p.Value;
            return " # RGB " + rgb.ToString("X6", CultureInfo.InvariantCulture);
        }

        /// <summary>True when a trailing comment looks like one CommentFor writes (so it can be replaced).</summary>
        public static bool IsColorComment(string comment)
        {
            if (string.IsNullOrEmpty(comment)) return true;
            string c = comment.Trim().TrimStart('#').Trim();
            if (c.StartsWith("RGB ")) return true;
            return CommonColors.Any(p => string.Equals(p.Value, c, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Paratext color names (ItemColorMappings) with their RGB in the Default theme.</summary>
        public static Dictionary<string, int> ColorNames
        {
            get
            {
                if (_names == null) _names = LoadColorMapping(Path.Combine(
                    Path.Combine(Path.Combine(StylesheetService.ParatextDirectory(), "Themes"), "Default"), "ColorMapping.xml"));
                return _names;
            }
        }

        public static Dictionary<string, int> LoadColorMapping(string path)
        {
            var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!File.Exists(path)) return result;
                XDocument x = XDocument.Load(path);
                var palette = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (XElement c in x.Descendants("Color"))
                {
                    string name = (string)c.Attribute("Name");
                    string hex = (string)c.Attribute("ColorHexCode");
                    int rgb;
                    if (name != null && hex != null
                        && int.TryParse(hex.Trim().TrimStart('#'), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out rgb))
                        palette[name] = rgb;
                }
                XElement items = x.Descendants("ItemColorMappings").FirstOrDefault();
                if (items == null) return result;
                foreach (XElement item in items.Elements("Item"))
                {
                    string name = (string)item.Attribute("Name");
                    string color = (string)item.Attribute("Color");
                    int rgb;
                    if (name != null && color != null && palette.TryGetValue(color, out rgb))
                        result[name] = rgb;
                }
            }
            catch (Exception) { }
            return result;
        }

        /// <summary>The RGB of a \ColorName value, or -1.</summary>
        public static int ParseColorName(string name)
        {
            int rgb;
            if (!string.IsNullOrEmpty(name) && ColorNames.TryGetValue(name.Trim(), out rgb)) return rgb;
            return -1;
        }
    }
}
