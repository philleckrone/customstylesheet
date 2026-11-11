using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CustomStyEditor.Models
{
    /// <summary>
    /// A .sty file as a list of lines. Paratext reads a stylesheet from top to bottom: '#' lines
    /// are ignored, every other keyword line belongs to the nearest active \Marker line above
    /// it, and a later value replaces an earlier one. Analyze() follows the same rules.
    /// Edits change lines in place; a marker that is not in the file yet is added at the end.
    /// </summary>
    public class StyDocument
    {
        public readonly List<StyLine> Lines = new List<StyLine>();
        public Encoding Encoding = new UTF8Encoding(false);
        public bool HasBom;
        public string DefaultEol = "\r\n";

        // \u2500\u2500 Analysis \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500

        /// <summary>A \Marker line (active or commented out) and the lines below it up to the next \Marker line.</summary>
        public class Block
        {
            public int Start;           // index of the \Marker line
            public int End;             // exclusive: next \Marker line or Lines.Count
            public int LastKeyword;     // last keyword line of the block (Start when it has none)
            public string Marker;
            public bool IsDisabled;
            public bool IsRemoved;
        }

        /// <summary>What the file says about one marker.</summary>
        public class MarkerInfo
        {
            public string Marker;
            public readonly List<Block> Blocks = new List<Block>();
            /// <summary>Property lines by lowercase keyword, in file order: lines Paratext applies
            /// to this marker plus commented-out lines inside its blocks.</summary>
            public readonly Dictionary<string, List<int>> KeyLines = new Dictionary<string, List<int>>();
            /// <summary>Active lines that sit under a commented-out \Marker line of this marker, so
            /// Paratext applies them to the marker above instead.</summary>
            public readonly List<int> StrayLines = new List<int>();
            public int FirstLine { get { return Blocks.Count > 0 ? Blocks[0].Start : int.MaxValue; } }
            public int ActiveBlockCount { get { return Blocks.Count(b => !b.IsDisabled); } }
            public bool IsDisabled { get { return ActiveBlockCount == 0; } }
            public Block LastActiveBlock { get { return Blocks.LastOrDefault(b => !b.IsDisabled); } }
            /// <summary>"\Marker x -" on the last active \Marker line.</summary>
            public bool IsRemoved { get { Block b = LastActiveBlock; return b != null && b.IsRemoved; } }
        }

        public class Analysis
        {
            public readonly List<Block> Blocks = new List<Block>();
            public readonly Dictionary<string, MarkerInfo> Markers = new Dictionary<string, MarkerInfo>(StringComparer.Ordinal);
            /// <summary>Active property lines above the first active \Marker line (Paratext ignores them).</summary>
            public readonly List<int> OrphanLines = new List<int>();
            /// <summary>Lines Paratext applies to each marker: (marker, line index).</summary>
            public readonly Dictionary<int, string> AppliedTo = new Dictionary<int, string>();

            public IEnumerable<MarkerInfo> MarkersInFileOrder
            {
                get { return Markers.Values.OrderBy(m => m.FirstLine); }
            }
        }

        public Analysis Analyze()
        {
            var a = new Analysis();
            Block current = null;
            for (int i = 0; i < Lines.Count; i++)
            {
                StyLine line = Lines[i];
                if (line.IsMarker)
                {
                    if (current != null) current.End = i;
                    current = new Block();
                    current.Start = i;
                    current.LastKeyword = i;
                    current.Marker = line.MarkerName;
                    current.IsDisabled = line.IsDisabled;
                    current.IsRemoved = line.MarkerRemoved;
                    a.Blocks.Add(current);
                }
                else if (line.IsKeyword && current != null)
                {
                    current.LastKeyword = i;
                }
            }
            if (current != null) current.End = Lines.Count;

            foreach (Block b in a.Blocks)
            {
                if (b.Marker.Length == 0) continue;
                MarkerInfo info;
                if (!a.Markers.TryGetValue(b.Marker, out info))
                {
                    info = new MarkerInfo();
                    info.Marker = b.Marker;
                    a.Markers[b.Marker] = info;
                }
                info.Blocks.Add(b);
            }

            // Walk the lines the way Paratext does.
            string activeMarker = null;     // marker of the last active \Marker line
            Block physical = null;          // block the line sits in
            int blockIndex = 0;
            for (int i = 0; i < Lines.Count; i++)
            {
                while (blockIndex < a.Blocks.Count && a.Blocks[blockIndex].Start <= i)
                    physical = a.Blocks[blockIndex++];

                StyLine line = Lines[i];
                if (!line.IsKeyword) continue;
                if (line.IsMarker)
                {
                    if (line.IsActive) activeMarker = line.MarkerName.Length > 0 ? line.MarkerName : null;
                    continue;
                }

                if (line.IsActive)
                {
                    if (activeMarker == null)
                    {
                        a.OrphanLines.Add(i);
                        continue;
                    }
                    AddKeyLine(a.Markers[activeMarker], line.Key, i);
                    a.AppliedTo[i] = activeMarker;
                    if (physical != null && physical.IsDisabled && physical.Marker.Length > 0)
                        a.Markers[physical.Marker].StrayLines.Add(i);
                }
                else if (physical != null && physical.Marker.Length > 0)
                {
                    AddKeyLine(a.Markers[physical.Marker], line.Key, i);
                }
            }
            return a;
        }

        private static void AddKeyLine(MarkerInfo info, string key, int index)
        {
            List<int> list;
            if (!info.KeyLines.TryGetValue(key, out list))
            {
                list = new List<int>();
                info.KeyLines[key] = list;
            }
            list.Add(index);
        }

        /// <summary>The line whose value Paratext uses for any of the keys (the last active one), or -1.</summary>
        public int WinningLine(MarkerInfo info, params string[] keys)
        {
            int best = -1;
            foreach (string key in keys)
            {
                List<int> list;
                if (info == null || !info.KeyLines.TryGetValue(key, out list)) continue;
                foreach (int i in list)
                    if (Lines[i].IsActive && i > best) best = i;
            }
            return best;
        }

        /// <summary>The line an editor row shows: the winning line, else the last commented-out one, or -1.</summary>
        public int BoundLine(MarkerInfo info, params string[] keys)
        {
            int win = WinningLine(info, keys);
            if (win >= 0) return win;
            int best = -1;
            foreach (string key in keys)
            {
                List<int> list;
                if (info == null || !info.KeyLines.TryGetValue(key, out list)) continue;
                foreach (int i in list)
                    if (i > best) best = i;
            }
            return best;
        }

        /// <summary>Active lines for the keys that a later line overrides.</summary>
        public List<int> OverriddenLines(MarkerInfo info, params string[] keys)
        {
            var result = new List<int>();
            int win = WinningLine(info, keys);
            foreach (string key in keys)
            {
                List<int> list;
                if (info == null || !info.KeyLines.TryGetValue(key, out list)) continue;
                foreach (int i in list)
                    if (Lines[i].IsActive && i != win) result.Add(i);
            }
            result.Sort();
            return result;
        }

        /// <summary>
        /// Values Paratext uses for a marker, starting from baseValues (the base stylesheet's
        /// values, or null): lowercase keyword to value ("" for a plain flag, "-" for a flag
        /// turned off). See ApplyValue for the rules.
        /// </summary>
        public Dictionary<string, string> EffectiveValues(MarkerInfo info, Dictionary<string, string> baseValues = null)
        {
            var values = baseValues != null
                ? new Dictionary<string, string>(baseValues)
                : new Dictionary<string, string>();
            if (info == null) return values;
            var applied = new List<int>();
            foreach (List<int> list in info.KeyLines.Values)
                applied.AddRange(list.Where(i => Lines[i].IsActive));
            applied.Sort();
            foreach (int i in applied)
                ApplyValue(values, Lines[i].Key, Lines[i].Value);
            return values;
        }

        /// <summary>
        /// One line as Paratext's ScrStylesheet applies it (checked against ParatextData.dll):
        /// a later value replaces an earlier one; \Color and \ColorName set the same color;
        /// \Regular turns Bold, Italic and Superscript off; \TextProperties adds its words to
        /// the ones already set, and nonpublishable removes publishable.
        /// </summary>
        public static void ApplyValue(Dictionary<string, string> values, string key, string value)
        {
            if (key == "color") values.Remove("colorname");
            if (key == "colorname") values.Remove("color");
            if (key == "textproperties")
            {
                string old;
                var words = new List<string>();
                if (values.TryGetValue(key, out old))
                    words.AddRange(StyLine.SplitWords(old.ToLowerInvariant()));
                foreach (string w in StyLine.SplitWords(value.ToLowerInvariant()))
                    if (!words.Contains(w)) words.Add(w);
                if (words.Contains("nonpublishable")) words.Remove("publishable");
                values[key] = string.Join(" ", words.ToArray());
                return;
            }
            values[key] = value;
            if (key == "regular" && value != "-")
            {
                values["bold"] = "-";
                values["italic"] = "-";
                values["superscript"] = "-";
            }
        }

        // \u2500\u2500 Edits \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500

        private void EnsureLastLineEnded()
        {
            if (Lines.Count > 0 && Lines[Lines.Count - 1].Eol.Length == 0)
                Lines[Lines.Count - 1].Eol = DefaultEol;
        }

        /// <summary>Adds "\Marker x" (or "\Marker x -") at the end of the file, after a blank line.</summary>
        public int AppendMarkerBlock(string marker, bool removed)
        {
            EnsureLastLineEnded();
            if (Lines.Count > 0 && Lines[Lines.Count - 1].Kind != StyLineKind.Blank)
                Lines.Add(StyLine.CreateBlank(DefaultEol));
            Lines.Add(StyLine.Create("Marker", marker + (removed ? " -" : ""), "", DefaultEol));
            return Lines.Count - 1;
        }

        /// <summary>
        /// Adds a property line for the marker. It goes at the end of the marker's last active
        /// block (Paratext reports a second \Marker block for the same marker as a duplicate
        /// definition); a marker with no active block gets a new block at the end of the file.
        /// Returns the new line's index.
        /// </summary>
        public int AddProperty(string marker, string keyword, string value, string comment)
        {
            MarkerInfo info;
            Analyze().Markers.TryGetValue(marker, out info);
            Block block = info != null ? info.LastActiveBlock : null;
            int insertAt;
            if (block == null)
            {
                insertAt = AppendMarkerBlock(marker, false) + 1;
            }
            else
            {
                insertAt = block.LastKeyword + 1;
                if (insertAt == Lines.Count) EnsureLastLineEnded();
            }
            Lines.Insert(insertAt, StyLine.Create(keyword, value, comment, DefaultEol));
            return insertAt;
        }

        public void SetLineValue(int index, string keyword, string value, string comment)
        {
            StyLine line = Lines[index];
            if (keyword != null) line.Keyword = keyword;
            line.Value = (value ?? "").Trim();
            if (comment != null) line.Comment = comment;
            line.Rebuild();
        }

        public void SetLineDisabled(int index, bool disabled)
        {
            Lines[index].SetDisabled(disabled);
        }

        public void DeleteLines(IEnumerable<int> indices)
        {
            foreach (int i in indices.Distinct().OrderByDescending(x => x))
                Lines.RemoveAt(i);
        }

        /// <summary>
        /// Disabling comments out every keyword line of the marker's active blocks. Enabling
        /// uncomments the last block only (two active blocks would be a duplicate definition).
        /// </summary>
        public void SetMarkerDisabled(string marker, bool disabled)
        {
            MarkerInfo info;
            if (!Analyze().Markers.TryGetValue(marker, out info)) return;
            if (disabled)
            {
                foreach (Block b in info.Blocks.Where(x => !x.IsDisabled))
                    for (int i = b.Start; i <= b.LastKeyword; i++)
                        if (Lines[i].IsKeyword) Lines[i].SetDisabled(true);
            }
            else if (info.IsDisabled && info.Blocks.Count > 0)
            {
                Block b = info.Blocks[info.Blocks.Count - 1];
                for (int i = b.Start; i <= b.LastKeyword; i++)
                    if (Lines[i].IsKeyword) Lines[i].SetDisabled(false);
            }
        }

        /// <summary>Sets or clears the " -" on the marker's last active \Marker line, or adds "\Marker x -" at the end.</summary>
        public void SetMarkerRemoved(string marker, bool removed)
        {
            MarkerInfo info;
            Analyze().Markers.TryGetValue(marker, out info);
            Block b = info != null ? info.LastActiveBlock : null;
            if (b == null)
            {
                if (removed) AppendMarkerBlock(marker, true);
                return;
            }
            StyLine line = Lines[b.Start];
            string[] words = StyLine.SplitWords(line.Value);
            var rest = words.Skip(1).Where(w => w != "-");
            line.Value = string.Join(" ", new[] { marker }.Concat(removed ? new[] { "-" } : new string[0]).Concat(rest).ToArray());
            line.Rebuild();
        }

        /// <summary>Deletes every block of the marker (the \Marker line through its last keyword line).</summary>
        public void DeleteMarker(string marker)
        {
            MarkerInfo info;
            if (!Analyze().Markers.TryGetValue(marker, out info)) return;
            foreach (Block b in info.Blocks.OrderByDescending(x => x.Start))
            {
                int start = b.Start, end = b.LastKeyword;
                // Keep one blank line between the neighbours, not two.
                if (end + 1 < Lines.Count && Lines[end + 1].Kind == StyLineKind.Blank
                    && (start == 0 || Lines[start - 1].Kind == StyLineKind.Blank))
                    end++;
                Lines.RemoveRange(start, end - start + 1);
            }
            if (Lines.Count > 0 && Lines.All(l => l.Kind == StyLineKind.Blank))
                Lines.Clear();
        }

        /// <summary>The lines of the marker's blocks, numbered, as they will be written.</summary>
        public string BlockText(MarkerInfo info)
        {
            if (info == null) return "";
            var sb = new StringBuilder();
            foreach (Block b in info.Blocks)
            {
                if (sb.Length > 0) sb.AppendLine("...");
                for (int i = b.Start; i <= b.LastKeyword; i++)
                    sb.AppendLine((i + 1).ToString().PadLeft(4) + "  " + Lines[i].Text);
            }
            return sb.ToString();
        }
    }
}
