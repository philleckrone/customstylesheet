using System.Collections.Generic;
using System.Linq;

namespace CustomStyEditor.Services
{
    public enum PropertyKind { Flag, Text, Integer, Number, Choice, Font, Color, MarkerList, Words }

    public enum PropertyGroup { Identity, TextFormat, Font, Paragraph, Placement, Other }

    /// <summary>A stylesheet keyword the editor offers (spelled as in usfm.sty and the Paratext help).</summary>
    public class KeywordDef
    {
        public string Keyword;      // canonical spelling written for new lines
        public PropertyKind Kind;
        public PropertyGroup Group;
        public string[] Choices;    // Choice and Words kinds
        public string UnitKey;      // string resource for the unit, or null

        public string Key { get { return Keyword.ToLowerInvariant(); } }
    }

    public static class StyleKeywords
    {
        public static readonly string[] StyleTypes = { "Paragraph", "Character", "Note", "Milestone" };
        public static readonly string[] TextTypes =
            { "VerseText", "Title", "Section", "NoteText", "Other", "BackTranslation", "TranslationNote", "ChapterNumber", "VerseNumber" };
        public static readonly string[] Justifications = { "Left", "Center", "Right", "Both" };
        public static readonly string[] TextPropertyWords =
        {
            "paragraph", "publishable", "nonpublishable", "vernacular", "nonvernacular", "poetic",
            "level_1", "level_2", "level_3", "level_4", "level_5", "crossreference", "note", "book", "chapter", "verse"
        };

        public static readonly List<KeywordDef> All = new List<KeywordDef>
        {
            Def("Name", PropertyKind.Text, PropertyGroup.Identity),
            Def("Description", PropertyKind.Text, PropertyGroup.Identity),
            Def("StyleType", PropertyKind.Choice, PropertyGroup.Identity, StyleTypes),
            Def("Endmarker", PropertyKind.Text, PropertyGroup.Identity),
            Def("TextType", PropertyKind.Choice, PropertyGroup.Identity, TextTypes),
            Def("TextProperties", PropertyKind.Words, PropertyGroup.Identity, TextPropertyWords),

            Def("Bold", PropertyKind.Flag, PropertyGroup.TextFormat),
            Def("Italic", PropertyKind.Flag, PropertyGroup.TextFormat),
            Def("Underline", PropertyKind.Flag, PropertyGroup.TextFormat),
            Def("Smallcaps", PropertyKind.Flag, PropertyGroup.TextFormat),
            Def("Superscript", PropertyKind.Flag, PropertyGroup.TextFormat),
            Def("Subscript", PropertyKind.Flag, PropertyGroup.TextFormat),
            Def("Regular", PropertyKind.Flag, PropertyGroup.TextFormat),

            Def("Fontname", PropertyKind.Font, PropertyGroup.Font),
            Def("FontSize", PropertyKind.Integer, PropertyGroup.Font, null, "Unit_Points"),
            Def("Color", PropertyKind.Color, PropertyGroup.Font),

            Def("Justification", PropertyKind.Choice, PropertyGroup.Paragraph, Justifications),
            Def("LeftMargin", PropertyKind.Number, PropertyGroup.Paragraph, null, "Unit_Inches"),
            Def("RightMargin", PropertyKind.Number, PropertyGroup.Paragraph, null, "Unit_Inches"),
            Def("FirstLineIndent", PropertyKind.Number, PropertyGroup.Paragraph, null, "Unit_Inches"),
            Def("SpaceBefore", PropertyKind.Integer, PropertyGroup.Paragraph, null, "Unit_Points"),
            Def("SpaceAfter", PropertyKind.Integer, PropertyGroup.Paragraph, null, "Unit_Points"),
            Def("LineSpacing", PropertyKind.Integer, PropertyGroup.Paragraph, null, "Unit_LineSpacing"),

            Def("OccursUnder", PropertyKind.MarkerList, PropertyGroup.Placement),
            Def("Rank", PropertyKind.Integer, PropertyGroup.Placement),
            Def("NotRepeatable", PropertyKind.Flag, PropertyGroup.Placement),

            Def("Attributes", PropertyKind.Text, PropertyGroup.Other),
        };

        /// <summary>Lowercase keywords the editor has a row for ("colorname" is part of the Color row).</summary>
        public static readonly HashSet<string> KnownKeys =
            new HashSet<string>(All.Select(d => d.Key).Concat(new[] { "marker", "colorname" }));

        public static KeywordDef Find(string key)
        {
            return All.FirstOrDefault(d => d.Key == key);
        }

        private static KeywordDef Def(string keyword, PropertyKind kind, PropertyGroup group,
            string[] choices = null, string unitKey = null)
        {
            var d = new KeywordDef();
            d.Keyword = keyword;
            d.Kind = kind;
            d.Group = group;
            d.Choices = choices;
            d.UnitKey = unitKey;
            return d;
        }
    }
}
