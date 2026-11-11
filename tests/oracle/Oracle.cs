using System;
using System.IO;
using System.Reflection;
using System.Collections;

// Loads Paratext's own ScrStylesheet(main, custom) and prints the merged tags as a test oracle.
// Usage: Oracle.exe main.sty [custom.sty|-] marker1 marker2 ... | Oracle.exe main.sty custom.sty --all
class Oracle
{
    const string PtDir = @"C:\Program Files\Paratext 9";

    static int Main(string[] args)
    {
        AppDomain.CurrentDomain.AssemblyResolve += delegate(object s, ResolveEventArgs e)
        {
            string n = new AssemblyName(e.Name).Name;
            string p = Path.Combine(PtDir, n + ".dll");
            return File.Exists(p) ? Assembly.LoadFrom(p) : null;
        };
        return Run(args);
    }

    static int Run(string[] args)
    {
        Assembly a = Assembly.LoadFrom(Path.Combine(PtDir, "ParatextData.dll"));
        Type t = a.GetType("Paratext.Data.ScrStylesheet");
        BindingFlags any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        object ss = args[1] == "-"
            ? Activator.CreateInstance(t, any, null, new object[] { args[0] }, null)
            : Activator.CreateInstance(t, any, null, new object[] { args[0], args[1] }, null);
        Console.WriteLine("TagCount=" + t.GetProperty("TagCount").GetValue(ss, null));
        PrintList("MainErrors", t.GetProperty("MainStylesheetErrors").GetValue(ss, null));
        PrintList("AltErrors", t.GetProperty("AltStylesheetErrors").GetValue(ss, null));

        if (args.Length > 2 && args[2] == "--dump")
        {
            var lines = new System.Collections.Generic.List<string>();
            foreach (object tag in (IEnumerable)t.GetProperty("Tags").GetValue(ss, null))
                lines.Add(Norm(tag));
            lines.Sort(StringComparer.Ordinal);
            using (var w = new StreamWriter(args[3], false, new System.Text.UTF8Encoding(false)))
                foreach (string l in lines) w.WriteLine(l);
            return 0;
        }
        MethodInfo getTag = t.GetMethod("GetTag", new[] { typeof(string) });
        if (args.Length > 2 && args[2] == "--all")
        {
            foreach (object tag in (IEnumerable)t.GetProperty("Tags").GetValue(ss, null))
                PrintTag(tag);
            return 0;
        }
        for (int i = 2; i < args.Length; i++)
        {
            object tag = getTag.Invoke(ss, new object[] { args[i] });
            if (tag == null) Console.WriteLine(args[i] + " : <null>");
            else PrintTag(tag);
        }
        return 0;
    }

    static void PrintList(string label, object list)
    {
        Console.Write(label + "=");
        if (list != null)
            foreach (object o in (IEnumerable)list) Console.Write("[" + o + "] ");
        Console.WriteLine();
    }

    static readonly string[] Props = {
        "Name", "StyleType", "TextType", "TextProperties", "Endmarker", "Description", "OccursUnder", "Rank",
        "NotRepeatable", "Bold", "Italic", "Underline", "SmallCaps", "Superscript", "Subscript", "Regular",
        "Color", "Fontname", "FontSize", "JustificationType", "LeftMargin", "RightMargin", "FirstLineIndent",
        "SpaceBefore", "SpaceAfter", "LineSpacing", "RawAttributes" };

    static void PrintTag(object tag)
    {
        Type tt = tag.GetType();
        Console.Write("<" + Member(tag, "Marker") + ">");
        foreach (string p in Props)
        {
            PropertyInfo pi = tt.GetProperty(p);
            object v = pi != null ? pi.GetValue(tag, null) : "?";
            Console.Write(" " + p + "=" + Fmt(v));
        }
        Console.WriteLine();
    }

    static string P(object tag, string name)
    {
        object v = tag.GetType().GetProperty(name).GetValue(tag, null);
        return v == null ? "" : v.ToString();
    }

    static string Strip(string enumText)
    {
        var parts = new System.Collections.Generic.List<string>();
        foreach (string raw in enumText.Split(','))
        {
            string x = raw.Trim();
            if (x.StartsWith("sc")) x = x.Substring(2);
            if (x.EndsWith("Style") && x != "Style") x = x.Substring(0, x.Length - 5);
            if (x.Length > 0) parts.Add(x.ToLowerInvariant());
        }
        parts.Sort(StringComparer.Ordinal);
        return string.Join(" ", parts.ToArray());
    }

    static string Norm(object tag)
    {
        object c = tag.GetType().GetProperty("Color").GetValue(tag, null);
        string color = "000000";
        var m = System.Text.RegularExpressions.Regex.Match(c.ToString(), @"ARGB\((\d+),(\d+),(\d+),(\d+)\)");
        if (m.Success) color = int.Parse(m.Groups[2].Value).ToString("X2") + int.Parse(m.Groups[3].Value).ToString("X2") + int.Parse(m.Groups[4].Value).ToString("X2");
        string ou = string.Join(" ", P(tag, "OccursUnder").Split(new[] { ' ', '	' }, StringSplitOptions.RemoveEmptyEntries));
        return string.Join("	", new[] {
            Member(tag, "Marker").ToString(), "Name=" + P(tag, "Name"), "StyleType=" + Strip(P(tag, "StyleType")),
            "TextType=" + Strip(P(tag, "TextType")), "TP=" + Strip(P(tag, "TextProperties")),
            "Endmarker=" + P(tag, "Endmarker"), "OccursUnder=" + ou, "Rank=" + P(tag, "Rank"),
            "NotRepeatable=" + P(tag, "NotRepeatable"),
            "Bold=" + P(tag, "Bold"), "Italic=" + P(tag, "Italic"), "Underline=" + P(tag, "Underline"),
            "SmallCaps=" + P(tag, "SmallCaps"), "Superscript=" + P(tag, "Superscript"),
            "Subscript=" + P(tag, "Subscript"), "Regular=" + P(tag, "Regular"),
            "Color=" + color, "Fontname=" + P(tag, "Fontname"), "FontSize=" + P(tag, "FontSize"),
            "Just=" + Strip(P(tag, "JustificationType")), "LM=" + P(tag, "LeftMargin"), "RM=" + P(tag, "RightMargin"),
            "FLI=" + P(tag, "FirstLineIndent"), "SB=" + P(tag, "SpaceBefore"), "SA=" + P(tag, "SpaceAfter"), "LS=" + P(tag, "LineSpacing") });
    }

    static object Member(object o, string name)
    {
        Type t = o.GetType();
        PropertyInfo pi = t.GetProperty(name);
        if (pi != null) return pi.GetValue(o, null);
        FieldInfo fi = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return fi != null ? fi.GetValue(o) : "?";
    }

    static string Fmt(object v)
    {
        if (v == null) return "null";
        Type t = v.GetType();
        if (t.Name == "RgbColor")
        {
            PropertyInfo r = t.GetProperty("Red"), g = t.GetProperty("Green"), b = t.GetProperty("Blue");
            if (r != null) return "#" + Convert.ToInt32(r.GetValue(v, null)).ToString("X2")
                + Convert.ToInt32(g.GetValue(v, null)).ToString("X2") + Convert.ToInt32(b.GetValue(v, null)).ToString("X2");
        }
        return v.ToString();
    }
}
