using System;

namespace CustomStyEditor.Models
{
    public enum StyLineKind { Blank, Comment, Keyword, Other }

    /// <summary>
    /// One physical line of a .sty file. Unchanged lines are written back exactly as read
    /// (Text + Eol); a keyword line that is edited is rebuilt from Lead/Keyword/Value/Comment.
    /// </summary>
    public class StyLine
    {
        /// <summary>Line text without its line ending.</summary>
        public string Text;
        /// <summary>"\r\n", "\n", "\r", or "" for a last line without an ending.</summary>
        public string Eol;
        public StyLineKind Kind;

        /// <summary>Everything before the backslash: "", "#!", "# ", "# #!", indentation...</summary>
        public string Lead = "";
        /// <summary>Keyword as written, without the backslash (case kept).</summary>
        public string Keyword = "";
        /// <summary>Whitespace written between the keyword and the value.</summary>
        public string Separator = "";
        /// <summary>Value, trimmed, without the trailing comment.</summary>
        public string Value = "";
        /// <summary>Trailing comment including the whitespace before its '#', or "".</summary>
        public string Comment = "";

        public bool IsKeyword { get { return Kind == StyLineKind.Keyword; } }

        /// <summary>Lowercase keyword ("marker", "color", ...); Paratext ignores keyword case.</summary>
        public string Key { get { return Keyword.ToLowerInvariant(); } }

        public bool IsMarker { get { return IsKeyword && Key == "marker"; } }

        /// <summary>A keyword line commented out with '#'. "#!" lines are read by Paratext (USFM 3 entries).</summary>
        public bool IsDisabled { get { return IsKeyword && LeadIsDisabled(Lead); } }

        public bool IsActive { get { return IsKeyword && !LeadIsDisabled(Lead); } }

        /// <summary>For a \Marker line: the marker name (first word of the value).</summary>
        public string MarkerName
        {
            get
            {
                string[] parts = SplitWords(Value);
                return parts.Length > 0 ? parts[0] : "";
            }
        }

        /// <summary>For a \Marker line: "\Marker pn -" removes the marker from the stylesheet.</summary>
        public bool MarkerRemoved
        {
            get
            {
                string[] parts = SplitWords(Value);
                return parts.Length > 1 && parts[1] == "-";
            }
        }

        public static StyLine Parse(string text, string eol)
        {
            var line = new StyLine();
            line.Text = text;
            line.Eol = eol;

            int i = 0;
            while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
            if (i == text.Length)
            {
                line.Kind = StyLineKind.Blank;
                return line;
            }

            int slash = -1;
            if (text[i] == '#')
            {
                int j = i + 1;
                if (j < text.Length && text[j] == '!') j++;
                while (j < text.Length && char.IsWhiteSpace(text[j])) j++;
                // "# #!\Marker" is a disabled "#!" line.
                if (j + 1 < text.Length && text[j] == '#' && text[j + 1] == '!' && text[i + 1] != '!')
                {
                    j += 2;
                    while (j < text.Length && char.IsWhiteSpace(text[j])) j++;
                }
                if (IsKeywordStart(text, j)) slash = j;
                else
                {
                    line.Kind = StyLineKind.Comment;
                    return line;
                }
            }
            else if (IsKeywordStart(text, i))
            {
                slash = i;
            }
            else
            {
                line.Kind = StyLineKind.Other;
                return line;
            }

            line.Kind = StyLineKind.Keyword;
            line.Lead = text.Substring(0, slash);
            int k = slash + 1;
            while (k < text.Length && (char.IsLetterOrDigit(text[k]) || text[k] == '_')) k++;
            line.Keyword = text.Substring(slash + 1, k - slash - 1);

            int v = k;
            while (v < text.Length && char.IsWhiteSpace(text[v])) v++;
            line.Separator = text.Substring(k, v - k);
            string rest = text.Substring(v);

            int hash = FindTrailingComment(rest, line.Separator.Length > 0);
            if (hash >= 0)
            {
                // The comment keeps the whitespace in front of its '#'.
                int start = hash;
                while (start > 0 && char.IsWhiteSpace(rest[start - 1])) start--;
                if (start == 0)
                {
                    line.Comment = line.Separator + rest;
                    line.Separator = "";
                    line.Value = "";
                }
                else
                {
                    line.Comment = rest.Substring(start);
                    line.Value = rest.Substring(0, start).Trim();
                }
            }
            else
            {
                line.Value = rest.Trim();
            }
            return line;
        }

        /// <summary>A new keyword line (active).</summary>
        public static StyLine Create(string keyword, string value, string comment, string eol)
        {
            var line = new StyLine();
            line.Kind = StyLineKind.Keyword;
            line.Keyword = keyword;
            line.Value = value ?? "";
            line.Comment = comment ?? "";
            line.Eol = eol;
            line.Rebuild();
            return line;
        }

        public static StyLine CreateBlank(string eol)
        {
            var line = new StyLine();
            line.Kind = StyLineKind.Blank;
            line.Text = "";
            line.Eol = eol;
            return line;
        }

        /// <summary>Rebuilds Text after Lead/Keyword/Value/Comment changed.</summary>
        public void Rebuild()
        {
            string sep = Separator.Length > 0 ? Separator : " ";
            Text = Lead + "\\" + Keyword + (Value.Length > 0 ? sep + Value : "") + Comment;
        }

        public void SetValue(string value)
        {
            Value = (value ?? "").Trim();
            Rebuild();
        }

        public void SetDisabled(bool disabled)
        {
            if (!IsKeyword || disabled == IsDisabled) return;
            if (disabled)
            {
                Lead = "# " + Lead;
            }
            else
            {
                int idx = Lead.IndexOf('#');
                string after = Lead.Substring(idx + 1);
                if (after.StartsWith(" ")) after = after.Substring(1);
                Lead = Lead.Substring(0, idx) + after;
            }
            Rebuild();
        }

        public string FullText { get { return Text + Eol; } }

        internal static bool LeadIsDisabled(string lead)
        {
            string t = lead.TrimStart();
            return t.StartsWith("#") && !t.StartsWith("#!");
        }

        private static bool IsKeywordStart(string text, int pos)
        {
            return pos + 1 < text.Length && text[pos] == '\\' && char.IsLetter(text[pos + 1]);
        }

        /// <summary>
        /// Index of a '#' that starts a trailing comment: one preceded by whitespace (or by the
        /// separator when it is the first character), as Paratext reads "\LeftMargin 1 #note".
        /// </summary>
        private static int FindTrailingComment(string rest, bool hasSeparator)
        {
            for (int i = 0; i < rest.Length; i++)
            {
                if (rest[i] != '#') continue;
                bool before = i == 0 ? hasSeparator : char.IsWhiteSpace(rest[i - 1]);
                if (before) return i;
            }
            return -1;
        }

        internal static string[] SplitWords(string s)
        {
            return (s ?? "").Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        }
    }
}
