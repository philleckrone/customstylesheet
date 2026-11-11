using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using CustomStyEditor.Models;
using Microsoft.Win32;

namespace CustomStyEditor.Services
{
    /// <summary>The project's base stylesheet (usfm.sty or the one named in Settings.xml), read-only.</summary>
    public class BaseStylesheet
    {
        public string Path;
        public readonly List<string> Order = new List<string>();
        public readonly Dictionary<string, Dictionary<string, string>> Values =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);

        public bool Has(string marker)
        {
            return marker != null && Values.ContainsKey(marker);
        }

        /// <summary>The value for a lowercase keyword, or null when the base sheet does not set it.</summary>
        public string Get(string marker, string key)
        {
            Dictionary<string, string> v;
            string s;
            if (marker != null && Values.TryGetValue(marker, out v) && v.TryGetValue(key, out s)) return s;
            return null;
        }

        public string Name(string marker)
        {
            return Get(marker, "name") ?? "";
        }

        public string StyleType(string marker)
        {
            return Get(marker, "styletype") ?? "";
        }
    }

    /// <summary>Project facts the editor shows: name, base stylesheet, default font.</summary>
    public class ProjectInfo
    {
        public string ProjectDir;
        public string ShortName;
        public string StyleSheetName;   // from Settings.xml, "usfm.sty" when missing
        public string DefaultFont;
        public double DefaultFontSize;  // points; 12 when missing
    }

    public static class StylesheetService
    {
        public const string DefaultStyleSheet = "usfm.sty";

        public static ProjectInfo ReadProject(string customStyPath)
        {
            var info = new ProjectInfo();
            info.ProjectDir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(customStyPath));
            info.ShortName = System.IO.Path.GetFileName(info.ProjectDir);
            info.StyleSheetName = DefaultStyleSheet;
            info.DefaultFontSize = 12;
            string settings = System.IO.Path.Combine(info.ProjectDir, "Settings.xml");
            if (!File.Exists(settings)) return info;
            try
            {
                XDocument x = XDocument.Load(settings);
                XElement root = x.Root;
                if (root == null) return info;
                string sheet = Element(root, "StyleSheet");
                if (IsPlainFileName(sheet)) info.StyleSheetName = sheet;
                info.DefaultFont = Element(root, "DefaultFont");
                double size;
                if (double.TryParse(Element(root, "DefaultFontSize"), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out size) && size > 0)
                    info.DefaultFontSize = size;
                string name = Element(root, "Name");
                if (!string.IsNullOrEmpty(name)) info.ShortName = name;
            }
            catch (Exception) { }
            return info;
        }

        /// <summary>
        /// Settings.xml arrives by Send/Receive, so its StyleSheet value is accepted only as a
        /// bare file name (for example usfm_sb.sty). A full or network path (\\server\share\x.sty)
        /// would make the editor read files elsewhere, or connect to another computer and send
        /// it the user's Windows login; such a value falls back to usfm.sty.
        /// </summary>
        public static bool IsPlainFileName(string name)
        {
            if (string.IsNullOrEmpty(name) || name == "." || name == "..") return false;
            if (name.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0) return false;
            return name == System.IO.Path.GetFileName(name);
        }

        private static string Element(XElement root, string name)
        {
            XElement e = root.Element(name);
            return e != null ? e.Value.Trim() : null;
        }

        /// <summary>
        /// The base stylesheet file: the Settings.xml name in the project folder, then in the
        /// projects folder above it (where Paratext keeps usfm.sty), then usfm.sty there. Null if none exists.
        /// </summary>
        public static string FindBaseStylesheet(ProjectInfo project)
        {
            var candidates = new List<string>();
            string parent = Directory.GetParent(project.ProjectDir) != null
                ? Directory.GetParent(project.ProjectDir).FullName : null;
            candidates.Add(System.IO.Path.Combine(project.ProjectDir, project.StyleSheetName));
            if (parent != null) candidates.Add(System.IO.Path.Combine(parent, project.StyleSheetName));
            string projectsDir = ProjectsDirectory();
            if (projectsDir != null) candidates.Add(System.IO.Path.Combine(projectsDir, project.StyleSheetName));
            if (parent != null) candidates.Add(System.IO.Path.Combine(parent, DefaultStyleSheet));
            if (projectsDir != null) candidates.Add(System.IO.Path.Combine(projectsDir, DefaultStyleSheet));
            return candidates.FirstOrDefault(File.Exists);
        }

        public static BaseStylesheet Load(string path)
        {
            var sheet = new BaseStylesheet();
            sheet.Path = path;
            StyDocument doc = StyFileService.Read(path);
            StyDocument.Analysis a = doc.Analyze();
            foreach (StyDocument.MarkerInfo m in a.MarkersInFileOrder)
            {
                if (m.IsDisabled || m.IsRemoved) continue;
                sheet.Order.Add(m.Marker);
                sheet.Values[m.Marker] = doc.EffectiveValues(m);
            }
            return sheet;
        }

        /// <summary>Paratext's projects folder from the registry (Settings_Directory), or null.</summary>
        public static string ProjectsDirectory()
        {
            return ReadParatextKey("Settings_Directory");
        }

        /// <summary>
        /// The Paratext 9 program folder: two levels above this exe when it runs from
        /// Paratext's plugins folder, else the registry, else the usual install folder.
        /// </summary>
        public static string ParatextDirectory()
        {
            try
            {
                string exeDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
                DirectoryInfo up = Directory.GetParent(exeDir);
                if (up != null) up = up.Parent;
                if (up != null && File.Exists(System.IO.Path.Combine(up.FullName, "Paratext.exe")))
                    return up.FullName;
            }
            catch (Exception) { }
            string reg = ReadParatextKey("Paratext9_Full_Release_AppPath");
            if (reg != null) return reg;
            return @"C:\Program Files\Paratext 9";
        }

        private static string ReadParatextKey(string valueName)
        {
            foreach (RegistryView view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
            {
                try
                {
                    using (RegistryKey root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                    using (RegistryKey key = root.OpenSubKey(@"SOFTWARE\Paratext\8"))
                    {
                        if (key == null) continue;
                        string dir = key.GetValue(valueName) as string;
                        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                            return dir.TrimEnd('\\');
                    }
                }
                catch (Exception) { }
            }
            return null;
        }
    }
}
