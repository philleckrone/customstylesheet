using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using CustomStyEditor.Models;
using CustomStyEditor.Services;

// Tests for the CustomStyEditor core.
//   Harness.exe roundtrip <file>...           load + write without edits must give the same bytes
//   Harness.exe dump <base.sty> <custom.sty>  merged tags in the Oracle's normalized format
//   Harness.exe edits                          scripted edit checks
class Harness
{
    static int Main(string[] args)
    {
        if (args.Length == 0) { Console.WriteLine("usage"); return 2; }
        if (args[0] == "roundtrip") return RoundTrip(args.Skip(1));
        if (args[0] == "dump") return Dump(args[1], args[2]);
        if (args[0] == "edits") return Edits.Run();
        return 2;
    }

    static int RoundTrip(IEnumerable<string> files)
    {
        int bad = 0, n = 0;
        foreach (string f in files)
        {
            n++;
            byte[] orig = File.ReadAllBytes(f);
            StyDocument doc = StyFileService.Read(f);
            byte[] back = StyFileService.ToBytes(doc);
            if (!orig.SequenceEqual(back))
            {
                bad++;
                Console.WriteLine("DIFF " + f + " (" + orig.Length + " vs " + back.Length + ") enc=" + doc.Encoding.WebName);
            }
        }
        Console.WriteLine("roundtrip: " + n + " files, " + bad + " different");
        return bad == 0 ? 0 : 1;
    }

    // Merge base + custom like Paratext and print normalized fields per marker.
    static int Dump(string basePath, string customPath)
    {
        BaseStylesheet sheet = StylesheetService.Load(basePath);
        StyDocument custom = StyFileService.Read(customPath);
        StyDocument.Analysis a = custom.Analyze();

        var merged = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        foreach (string m in sheet.Order)
            merged[m] = new Dictionary<string, string>(sheet.Values[m]);
        foreach (StyDocument.MarkerInfo info in a.MarkersInFileOrder)
        {
            if (info.IsDisabled) continue;
            if (info.IsRemoved) { merged.Remove(info.Marker); continue; }
            Dictionary<string, string> v;
            merged.TryGetValue(info.Marker, out v);
            merged[info.Marker] = custom.EffectiveValues(info, v);
        }
        var sb = new StringBuilder();
        foreach (var kv in merged.OrderBy(k => k.Key, StringComparer.Ordinal))
            sb.AppendLine(Line(kv.Key, kv.Value));
        Console.Out.Write(sb.ToString());
        return 0;
    }

    static string Get(Dictionary<string, string> v, string k) { string s; return v.TryGetValue(k, out s) ? s : null; }
    static string Flag(Dictionary<string, string> v, string k) { string s = Get(v, k); return s == null || s == "-" ? "False" : "True"; }
    static string Int(Dictionary<string, string> v, string k)
    {
        string s = Get(v, k); int n;
        return s != null && int.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out n) ? n.ToString() : "0";
    }
    static string Thou(Dictionary<string, string> v, string k)
    {
        string s = Get(v, k); double d;
        return s != null && double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out d)
            ? ((int)Math.Round(d * 1000)).ToString() : "0";
    }

    static string Line(string marker, Dictionary<string, string> v)
    {
        string color;
        if (Get(v, "colorname") != null) color = "?";
        else { int rgb = ColorService.ParseColorValue(Get(v, "color")); color = (rgb < 0 ? 0 : rgb).ToString("X6"); }
        string tp = string.Join(" ", (Get(v, "textproperties") ?? "").ToLowerInvariant()
            .Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToArray());
        string just = (Get(v, "justification") ?? "left").Trim().ToLowerInvariant();
        string tt = (Get(v, "texttype") ?? "notspecified").Trim().ToLowerInvariant();
        string st = (Get(v, "styletype") ?? "unknown").Trim().ToLowerInvariant();
        string ou = string.Join(" ", (Get(v, "occursunder") ?? "").Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries));
        return string.Join("\t", new[] {
            marker, "Name=" + (Get(v, "name") ?? ""), "StyleType=" + st, "TextType=" + tt, "TP=" + tp,
            "Endmarker=" + (Get(v, "endmarker") ?? ""), "OccursUnder=" + ou, "Rank=" + Int(v, "rank"),
            "NotRepeatable=" + Flag(v, "notrepeatable"),
            "Bold=" + Flag(v, "bold"), "Italic=" + Flag(v, "italic"), "Underline=" + Flag(v, "underline"),
            "SmallCaps=" + Flag(v, "smallcaps"), "Superscript=" + Flag(v, "superscript"),
            "Subscript=" + Flag(v, "subscript"), "Regular=" + Flag(v, "regular"),
            "Color=" + color, "Fontname=" + (Get(v, "fontname") ?? ""), "FontSize=" + Int(v, "fontsize"),
            "Just=" + just, "LM=" + Thou(v, "leftmargin"), "RM=" + Thou(v, "rightmargin"), "FLI=" + Thou(v, "firstlineindent"),
            "SB=" + Int(v, "spacebefore"), "SA=" + Int(v, "spaceafter"), "LS=" + Int(v, "linespacing") });
    }
}
