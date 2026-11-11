using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CustomStyEditor.Models;
using CustomStyEditor.Services;

// Scripted document edits: each check states which lines may change. Output files go to
// out\edits\ so the Oracle can confirm Paratext reports no new errors.
static class Edits
{
    static int _fail;
    static string OutDir;

    static void Check(bool ok, string what)
    {
        Console.WriteLine((ok ? "ok   " : "FAIL ") + what);
        if (!ok) _fail++;
    }

    static string Text(StyDocument d)
    {
        return Encoding.UTF8.GetString(StyFileService.ToBytes(d));
    }

    static StyDocument Doc(string text) { return StyFileService.Parse(text); }

    static void Save(StyDocument d, string name)
    {
        File.WriteAllBytes(Path.Combine(OutDir, name), StyFileService.ToBytes(d));
    }

    public static int Run()
    {
        OutDir = Path.Combine(Path.GetDirectoryName(typeof(Edits).Assembly.Location), "..", "out", "edits");
        Directory.CreateDirectory(OutDir);
        const string N = "\r\n";
        string bs = "\\";
        string src =
            "# header comment" + N +
            bs + "Marker xt" + N +
            bs + "Color 1219071" + N +
            bs + "OccursUnder p q1" + N +
            N +
            "# Commented out" + N +
            "# " + bs + "Marker fe" + N +
            "# " + bs + "Color 16711680" + N +
            N +
            bs + "marker add" + N +
            bs + "color 65280 # Green" + N +
            "#!" + bs + "Superscript" + N +
            N +
            bs + "Marker qt" + N +
            bs + "Italic" + N;

        // 1. Edit an existing value in place.
        StyDocument d = Doc(src);
        var a = d.Analyze();
        int line = d.WinningLine(a.Markers["xt"], "color");
        d.SetLineValue(line, null, "255", null);
        Check(Text(d) == src.Replace(bs + "Color 1219071", bs + "Color 255"), "edit value in place");

        // 2. New property for a marker in the middle goes into its block (no second \Marker xt).
        d = Doc(src);
        d.AddProperty("xt", "Bold", "", "");
        Check(Text(d) == src.Replace(bs + "OccursUnder p q1" + N, bs + "OccursUnder p q1" + N + bs + "Bold" + N), "add property inside existing block");
        Check(d.Analyze().Markers["xt"].Blocks.Count == 1, "still one xt block");
        Save(d, "add_in_block.sty");

        // 3. New marker goes at the end, after a blank line; everything above is unchanged.
        d = Doc(src);
        d.AddProperty("bd", "Italic", "", "");
        Check(Text(d) == src + N + bs + "Marker bd" + N + bs + "Italic" + N, "new marker appended at the end");
        Save(d, "new_marker.sty");

        // 4. Property for a stock marker whose only block is commented out: new block at the end.
        d = Doc(src);
        d.AddProperty("fe", "Bold", "", "");
        Check(Text(d) == src + N + bs + "Marker fe" + N + bs + "Bold" + N, "property for commented-out marker appended at end");

        // 5. Disable and enable a block restores the original bytes.
        d = Doc(src);
        d.SetMarkerDisabled("xt", true);
        Check(Text(d) == src.Replace(bs + "Marker xt", "# " + bs + "Marker xt").Replace(bs + "Color 1219071", "# " + bs + "Color 1219071").Replace(bs + "OccursUnder p", "# " + bs + "OccursUnder p"), "disable block comments out its keyword lines");
        Check(d.Analyze().Markers["xt"].IsDisabled, "xt now disabled");
        d.SetMarkerDisabled("xt", false);
        Check(Text(d) == src, "enable block restores the file");

        // 6. Enable the commented-out fe block.
        d = Doc(src);
        d.SetMarkerDisabled("fe", false);
        Check(Text(d) == src.Replace("# " + bs + "Marker fe", bs + "Marker fe").Replace("# " + bs + "Color 16711680", bs + "Color 16711680"), "enable commented-out block");
        Check(d.Analyze().Markers.ContainsKey("fe") && !d.Analyze().Markers["fe"].IsDisabled, "fe active");

        // 7. "#!" lines: disable/enable round trip.
        d = Doc(src);
        a = d.Analyze();
        int sup = d.WinningLine(a.Markers["add"], "superscript");
        Check(sup >= 0, "#! line is active");
        d.SetLineDisabled(sup, true);
        Check(d.Lines[sup].Text == "# #!" + bs + "Superscript" && d.Lines[sup].IsDisabled, "#! line disabled as '# #!'");
        Check(StyLine.Parse(d.Lines[sup].Text, "").IsDisabled, "'# #!' re-parses as disabled keyword");
        d.SetLineDisabled(sup, false);
        Check(Text(d) == src, "#! line enabled again");

        // 8. Remove / restore a marker.
        d = Doc(src);
        d.SetMarkerRemoved("qt", true);
        Check(Text(d) == src.Replace(bs + "Marker qt", bs + "Marker qt -"), "remove marker in place");
        Check(d.Analyze().Markers["qt"].IsRemoved, "qt removed");
        d.SetMarkerRemoved("qt", false);
        Check(Text(d) == src, "restore marker");
        d.SetMarkerRemoved("pn", true);
        Check(Text(d) == src + N + bs + "Marker pn -" + N, "remove marker not in file appends \\Marker pn -");

        // 9. Delete a marker block.
        d = Doc(src);
        d.DeleteMarker("add");
        Check(Text(d) == src.Replace(bs + "marker add" + N + bs + "color 65280 # Green" + N + "#!" + bs + "Superscript" + N + N, ""), "delete marker block");

        // 10. Keyword case is kept when a value changes; trailing comment replaced.
        d = Doc(src);
        a = d.Analyze();
        line = d.WinningLine(a.Markers["add"], "color");
        d.SetLineValue(line, d.Lines[line].Keyword, "255", " # Red");
        Check(d.Lines[line].Text == bs + "color 255 # Red", "keyword case kept, comment replaced");

        // 11. File without a final line ending.
        d = Doc(bs + "Marker q1" + N + bs + "Bold");
        d.AddProperty("q1", "Italic", "", "");
        Check(Text(d) == bs + "Marker q1" + N + bs + "Bold" + N + bs + "Italic" + N, "add after last line without EOL");
        d = Doc(bs + "Marker q1" + N + bs + "Bold");
        d.AddProperty("q2", "Italic", "", "");
        Check(Text(d) == bs + "Marker q1" + N + bs + "Bold" + N + N + bs + "Marker q2" + N + bs + "Italic" + N, "append marker after last line without EOL");

        // 12. LF-only file keeps LF for new lines.
        d = Doc(bs + "Marker q1\n" + bs + "Bold\n");
        d.AddProperty("q1", "Italic", "", "");
        Check(Text(d) == bs + "Marker q1\n" + bs + "Bold\n" + bs + "Italic\n", "LF file gets LF lines");

        // 13. Comments: '#' after whitespace starts a comment, as Paratext reads it.
        StyLine c = StyLine.Parse(bs + "LeftMargin 1 #increase by .25", "");
        Check(c.Value == "1" && c.Comment == " #increase by .25", "comment without space after #");
        c = StyLine.Parse(bs + "Bold # make bold", "");
        Check(c.Value == "" && c.Comment == " # make bold", "flag with comment");
        c.SetValue("-");
        Check(c.Text == bs + "Bold - # make bold", "flag value set keeps comment");

        // 14. Analysis: an active line under a commented-out \Marker applies to the marker above.
        d = Doc(bs + "Marker q1" + N + bs + "Bold" + N + "# " + bs + "Marker xt" + N + bs + "Italic" + N);
        a = d.Analyze();
        Check(a.Markers["q1"].KeyLines.ContainsKey("italic") && a.Markers["xt"].StrayLines.Count == 1, "stray line applies to previous marker");

        // 15. Effective values: Regular and TextProperties rules.
        d = Doc(bs + "Marker q1" + N + bs + "Bold" + N + bs + "Regular" + N + bs + "TextProperties nonpublishable" + N);
        var v = d.EffectiveValues(d.Analyze().Markers["q1"], new Dictionary<string, string> { { "textproperties", "paragraph publishable vernacular" }, { "italic", "" } });
        Check(v["bold"] == "-" && v["italic"] == "-" && v["textproperties"] == "paragraph vernacular nonpublishable", "Regular and TextProperties rules");

        // 16. Real files: add a property to the first marker and a new marker, save for the Oracle.
        int n = 0;
        string root = @"C:\My Paratext 8 Projects";
        foreach (string dir in Directory.GetDirectories(root))
        {
            string f = Path.Combine(dir, "custom.sty");
            if (!File.Exists(f)) continue;
            StyDocument doc = StyFileService.Read(f);
            var an = doc.Analyze();
            var first = an.MarkersInFileOrder.FirstOrDefault(m => !m.IsDisabled && !m.IsRemoved && m.ActiveBlockCount == 1);
            if (first == null) continue;
            string before = doc.Encoding.GetString(StyFileService.ToBytes(doc));
            int insertAfter = 0;
            for (int i = 0; i <= first.Blocks[0].LastKeyword; i++) insertAfter += doc.Lines[i].Text.Length + doc.Lines[i].Eol.Length;

            doc.AddProperty(first.Marker, "Underline", "", "");
            doc.AddProperty("zzEditorTest", "Name", "zzEditorTest - test", "");
            doc.AddProperty("zzEditorTest", "StyleType", "Character", "");
            doc.AddProperty("zzEditorTest", "Endmarker", "zzEditorTest*", "");
            string after = doc.Encoding.GetString(StyFileService.ToBytes(doc));
            string tag = Path.GetFileName(dir);
            string expectedStart = before.Substring(0, insertAfter);
            bool ok = after.StartsWith(expectedStart) && after.Contains("zzEditorTest*")
                && doc.Analyze().Markers[first.Marker].Blocks.Count == 1;
            if (!ok) Check(false, "real file edit " + tag);
            File.WriteAllBytes(Path.Combine(OutDir, tag + ".custom.sty"), StyFileService.ToBytes(doc));
            File.WriteAllText(Path.Combine(OutDir, tag + ".marker.txt"), first.Marker);
            n++;
        }
        Console.WriteLine("real files edited: " + n);
        Console.WriteLine(_fail == 0 ? "ALL EDIT CHECKS PASSED" : _fail + " EDIT CHECKS FAILED");
        return _fail == 0 ? 0 : 1;
    }
}
