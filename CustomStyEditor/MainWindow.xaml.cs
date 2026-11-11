using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using CustomStyEditor.Models;
using CustomStyEditor.Services;
using Microsoft.Win32;

namespace CustomStyEditor
{
    public partial class MainWindow : Window
    {
        public static readonly RoutedUICommand SaveAsCommand = new RoutedUICommand(
            "Save As", "SaveAs", typeof(MainWindow),
            new InputGestureCollection { new KeyGesture(Key.S, ModifierKeys.Control | ModifierKeys.Shift) });

        private StyDocument _doc = new StyDocument();
        private StyDocument.Analysis _analysis;
        private readonly ObservableCollection<MarkerEntry> _entries = new ObservableCollection<MarkerEntry>();
        private readonly List<PropertyRow> _rows = new List<PropertyRow>();
        private readonly Dictionary<PropertyGroup, GroupBox> _groups = new Dictionary<PropertyGroup, GroupBox>();
        private TextBlock _otherLinesText;
        private GroupBox _otherLinesGroup;

        private ProjectInfo _project;
        private BaseStylesheet _base;
        private string _baseName = StylesheetService.DefaultStyleSheet;
        private string _baseMissingName;
        private string _currentPath;
        private bool _isDirty;
        private string _selectedMarker;
        private bool _suppressSelection;
        private string _statusMessage;

        public MainWindow()
        {
            InitializeComponent();
            MarkerGrid.ItemsSource = _entries;
            BuildPropertyPanel();
            PopulateLanguageCombo();
            UpdateLocalizedColumnHeaders();
            LoadBase(null);
            RefreshAll();
        }

        public StyDocument Document { get { return _doc; } }

        // \u2500\u2500 Strings \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500

        public static string StrStatic(string key)
        {
            object val = Application.Current.Resources[key];
            return val as string ?? key;
        }

        public string Str(string key)
        {
            return StrStatic(key);
        }

        // \u2500\u2500 Language \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500

        private void PopulateLanguageCombo()
        {
            var languages = new List<LanguageOption>
            {
                new LanguageOption { DisplayName = "English",      Culture = "en" },
                new LanguageOption { DisplayName = "Espa\u00F1ol",      Culture = "es" },
                new LanguageOption { DisplayName = "Fran\u00E7ais",     Culture = "fr" },
                new LanguageOption { DisplayName = "Portugu\u00EAs",    Culture = "pt" },
                new LanguageOption { DisplayName = "\u4E2D\u6587 (\u7B80\u4F53)",  Culture = "zh-Hans" }
            };
            LanguageCombo.ItemsSource = languages;
            LanguageCombo.DisplayMemberPath = "DisplayName";

            LanguageCombo.SelectionChanged -= LanguageCombo_SelectionChanged;
            LanguageCombo.SelectedIndex = 0;
            LanguageCombo.SelectionChanged += LanguageCombo_SelectionChanged;
        }

        /// <summary>Selects the UI language by culture name prefix (es, fr, pt, zh); anything else gives English.</summary>
        public void SetLanguage(string culture)
        {
            if (string.IsNullOrEmpty(culture)) return;
            string c = culture.ToLowerInvariant();
            var options = (List<LanguageOption>)LanguageCombo.ItemsSource;
            LanguageOption match = options.FirstOrDefault(o => c.StartsWith(o.Culture.Substring(0, 2).ToLowerInvariant()))
                ?? options[0];
            LanguageCombo.SelectedItem = match;
        }

        private void LanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            LanguageOption opt = LanguageCombo.SelectedItem as LanguageOption;
            if (opt == null) return;
            App.SwitchLanguage(opt.Culture);
            UpdateLocalizedColumnHeaders();
            RefreshAll();
        }

        private void UpdateLocalizedColumnHeaders()
        {
            if (MarkerGrid.Columns.Count < 5) return;
            MarkerGrid.Columns[0].Header = Str("Column_Marker");
            MarkerGrid.Columns[1].Header = Str("Column_Name");
            MarkerGrid.Columns[2].Header = Str("Column_Changes");
            MarkerGrid.Columns[3].Header = Str("Column_Status");
            MarkerGrid.Columns[4].Header = Str("Column_Disabled");
        }

        // \u2500\u2500 Property panel construction \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500

        private static readonly PropertyGroup[] GroupOrder =
        {
            PropertyGroup.TextFormat, PropertyGroup.Font, PropertyGroup.Paragraph,
            PropertyGroup.Identity, PropertyGroup.Placement, PropertyGroup.Other
        };

        private void BuildPropertyPanel()
        {
            foreach (PropertyGroup g in GroupOrder)
            {
                var grid = new Grid { Margin = new Thickness(4) };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                foreach (KeywordDef def in StyleKeywords.All.Where(d => d.Group == g))
                {
                    var row = new PropertyRow(def, this);
                    row.AddTo(grid);
                    _rows.Add(row);
                }
                var box = new GroupBox { Content = grid, Margin = new Thickness(0, 4, 0, 4), Padding = new Thickness(2) };
                box.SetResourceReference(HeaderedContentControl.HeaderProperty, "Group_" + g);
                _groups[g] = box;
                GroupsPanel.Children.Add(box);
            }

            _otherLinesText = new TextBlock { FontFamily = new FontFamily("Consolas"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4) };
            _otherLinesGroup = new GroupBox { Content = _otherLinesText, Margin = new Thickness(0, 4, 0, 4) };
            _otherLinesGroup.SetResourceReference(HeaderedContentControl.HeaderProperty, "Group_OtherLines");
            GroupsPanel.Children.Add(_otherLinesGroup);
        }

        // \u2500\u2500 Base stylesheet \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500

        private void LoadBase(string customPath)
        {
            _project = customPath != null ? StylesheetService.ReadProject(customPath) : null;
            string basePath;
            if (_project != null)
            {
                basePath = StylesheetService.FindBaseStylesheet(_project);
                _baseName = _project.StyleSheetName;
            }
            else
            {
                string dir = StylesheetService.ProjectsDirectory();
                basePath = dir != null ? Path.Combine(dir, StylesheetService.DefaultStyleSheet) : null;
                if (basePath != null && !File.Exists(basePath)) basePath = null;
                _baseName = StylesheetService.DefaultStyleSheet;
            }

            _base = null;
            _baseMissingName = null;
            if (basePath != null)
            {
                try
                {
                    _base = StylesheetService.Load(basePath);
                    _baseName = Path.GetFileName(basePath);
                }
                catch (Exception)
                {
                    _base = null;
                }
            }
            if (_base == null) _baseMissingName = _baseName;
        }

        /// <summary>Markers of the base stylesheet plus those defined in custom.sty.</summary>
        private HashSet<string> KnownMarkers()
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            if (_base != null) foreach (string m in _base.Order) set.Add(m);
            if (_analysis != null)
                foreach (StyDocument.MarkerInfo info in _analysis.Markers.Values)
                    if (!info.IsDisabled && !info.IsRemoved) set.Add(info.Marker);
            return set;
        }

        private bool InBase(string marker)
        {
            return _base != null && _base.Has(marker);
        }

        private Dictionary<string, string> BaseValues(string marker)
        {
            Dictionary<string, string> v;
            return _base != null && _base.Values.TryGetValue(marker, out v) ? v : null;
        }

        // \u2500\u2500 Refresh \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500

        private void RefreshAll()
        {
            _analysis = _doc.Analyze();
            UpdateEntries();
            RefreshPanel();
            UpdateStatus();
        }

        private void UpdateEntries()
        {
            _suppressSelection = true;
            try
            {
                var infos = _analysis.MarkersInFileOrder.ToList();
                var byMarker = _entries.ToDictionary(e => e.Marker, StringComparer.Ordinal);
                for (int i = 0; i < infos.Count; i++)
                {
                    StyDocument.MarkerInfo info = infos[i];
                    MarkerEntry entry;
                    if (!byMarker.TryGetValue(info.Marker, out entry))
                    {
                        entry = new MarkerEntry(info.Marker);
                        _entries.Insert(Math.Min(i, _entries.Count), entry);
                    }
                    int at = _entries.IndexOf(entry);
                    if (at != i) _entries.Move(at, i);
                    FillEntry(entry, info);
                }
                while (_entries.Count > infos.Count)
                    _entries.RemoveAt(_entries.Count - 1);

                MarkerEntry selected = _entries.FirstOrDefault(e => e.Marker == _selectedMarker);
                MarkerGrid.SelectedItem = selected;
                if (selected == null) _selectedMarker = null;
            }
            finally
            {
                _suppressSelection = false;
            }
        }

        private void FillEntry(MarkerEntry entry, StyDocument.MarkerInfo info)
        {
            Dictionary<string, string> custom = _doc.EffectiveValues(info);
            string name;
            if (!custom.TryGetValue("name", out name)) name = _base != null ? _base.Name(info.Marker) : "";
            entry.Name = name;

            var changed = info.KeyLines
                .Where(kv => kv.Value.Any(i => _doc.Lines[i].IsActive))
                .OrderBy(kv => kv.Value.Min())
                .Select(kv => { KeywordDef d = StyleKeywords.Find(kv.Key); return d != null ? d.Keyword : (kv.Key == "colorname" ? "ColorName" : kv.Key); })
                .Distinct()
                .ToArray();
            entry.Changes = info.IsRemoved ? "" : string.Join(", ", changed);
            entry.IsDisabled = info.IsDisabled;
            entry.IsRemoved = !info.IsDisabled && info.IsRemoved;

            List<string> warnings = Warnings(info);
            entry.Warning = string.Join("\n", warnings.ToArray());
            var status = new List<string>();
            if (info.IsDisabled) status.Add(Str("Status_Disabled"));
            else if (info.IsRemoved) status.Add(Str("Status_Removed"));
            else if (_base != null && !_base.Has(info.Marker)) status.Add(Str("Status_New"));
            if (warnings.Count > 0) status.Add(Str("Status_Warning"));
            entry.Status = string.Join(" ", status.ToArray());
        }

        private List<string> Warnings(StyDocument.MarkerInfo info)
        {
            var w = new List<string>();
            if (info.ActiveBlockCount > 1)
                w.Add(string.Format(Str("Warn_Duplicate"), info.Marker, info.ActiveBlockCount));
            foreach (var kv in info.KeyLines.OrderBy(k => k.Value.Min()))
            {
                var active = kv.Value.Where(i => _doc.Lines[i].IsActive).ToList();
                if (active.Count > 1)
                    w.Add(string.Format(Str("Warn_DuplicateAttr"), _doc.Lines[active[0]].Keyword,
                        string.Join(", ", active.Select(i => (i + 1).ToString()).ToArray())));
            }
            if (info.Marker.Length > 1 && info.Marker.EndsWith("-") && InBase(info.Marker.TrimEnd('-')))
                w.Add(string.Format(Str("Warn_DashName"), info.Marker, info.Marker.TrimEnd('-')));
            foreach (int i in info.StrayLines)
                w.Add(string.Format(Str("Warn_Stray"), i + 1));
            if (!info.IsDisabled && !info.IsRemoved && _base != null && !_base.Has(info.Marker))
            {
                Dictionary<string, string> v = _doc.EffectiveValues(info);
                if (!v.ContainsKey("name") || !v.ContainsKey("styletype"))
                    w.Add(Str("Warn_NeedsName"));
            }
            if (!info.IsDisabled && !info.IsRemoved)
            {
                Dictionary<string, string> v = _doc.EffectiveValues(info);
                string ou;
                HashSet<string> known = KnownMarkers();
                if (_base != null && v.TryGetValue("occursunder", out ou))
                {
                    var unknown = StyLine.SplitWords(ou).Where(m => m != "NEST" && !known.Contains(m)).ToArray();
                    if (unknown.Length > 0) w.Add("\\OccursUnder: " + string.Format(Str("Warn_UnknownMarkers"), string.Join(" ", unknown)));
                }
                foreach (int i in info.KeyLines.Where(k => k.Key == "textproperties").SelectMany(k => k.Value))
                {
                    if (!_doc.Lines[i].IsActive) continue;
                    var bad = StyLine.SplitWords(_doc.Lines[i].Value)
                        .Where(x => !StyleKeywords.TextPropertyWords.Contains(x.ToLowerInvariant())).ToArray();
                    if (bad.Length > 0) w.Add("\\TextProperties: " + string.Format(Str("Warn_UnknownWords"), string.Join(" ", bad)));
                }
            }
            return w;
        }

        private StyDocument.MarkerInfo SelectedInfo
        {
            get
            {
                StyDocument.MarkerInfo info;
                return _selectedMarker != null && _analysis.Markers.TryGetValue(_selectedMarker, out info) ? info : null;
            }
        }

        private void RefreshPanel()
        {
            StyDocument.MarkerInfo info = SelectedInfo;
            bool has = info != null;
            GroupsPanel.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
            RemoveButton.IsEnabled = has && (info.IsRemoved || InBase(info.Marker)) && !info.IsDisabled;
            RemoveButton.SetResourceReference(ContentControl.ContentProperty, has && info.IsRemoved ? "Btn_Restore" : "Btn_Remove");
            DeleteButton.IsEnabled = has;
            AddStockButton.IsEnabled = true;

            if (!has)
            {
                PanelHeader.Text = Str("Panel_Empty");
                PanelNote.Text = "";
                PanelWarnings.Text = "";
                PreviewViewer.Document = new FlowDocument();
                FileTextBox.Text = "";
                return;
            }

            string marker = info.Marker;
            Dictionary<string, string> effective = _doc.EffectiveValues(info, BaseValues(marker));
            string name;
            effective.TryGetValue("name", out name);
            PanelHeader.Text = "\\" + marker + (string.IsNullOrEmpty(name) ? "" : "  \u2014  " + name);

            var notes = new List<string>();
            if (info.IsDisabled) notes.Add(Str("Panel_DisabledNote"));
            else if (info.IsRemoved) notes.Add(string.Format(Str("Panel_RemovedNote"), marker));
            if (_base != null && !_base.Has(marker)) notes.Add(string.Format(Str("Panel_NewNote"), marker));
            PanelNote.Text = string.Join("\n", notes.ToArray());
            PanelNote.Visibility = notes.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            List<string> warnings = Warnings(info);
            PanelWarnings.Text = string.Join("\n", warnings.ToArray());
            PanelWarnings.Visibility = warnings.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            bool editable = !info.IsDisabled && !info.IsRemoved;
            HashSet<string> known = KnownMarkers();
            foreach (PropertyRow row in _rows)
                row.Refresh(_doc, info, _base, marker, _baseName, editable, known);

            // Paragraph settings only matter for paragraph styles (shown anyway when the file sets one).
            string styleType;
            effective.TryGetValue("styletype", out styleType);
            bool isParagraph = string.Equals(styleType, "paragraph", StringComparison.OrdinalIgnoreCase);
            bool fileHasParagraphKeys = _rows.Any(r => r.Def.Group == PropertyGroup.Paragraph && info.KeyLines.ContainsKey(r.Def.Key));
            _groups[PropertyGroup.Paragraph].Visibility = isParagraph || fileHasParagraphKeys ? Visibility.Visible : Visibility.Collapsed;

            // Lines with keywords the editor has no row for are kept as they are.
            var other = new List<string>();
            foreach (StyDocument.Block b in info.Blocks)
                for (int i = b.Start + 1; i <= b.LastKeyword; i++)
                    if (_doc.Lines[i].IsKeyword && !StyleKeywords.KnownKeys.Contains(_doc.Lines[i].Key))
                        other.Add((i + 1).ToString().PadLeft(4) + "  " + _doc.Lines[i].Text);
            _otherLinesText.Text = string.Join("\n", other.ToArray());
            _otherLinesGroup.Visibility = other.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            FileTextBox.Text = _doc.BlockText(info);
            UpdatePreview(info, effective);
        }

        // \u2500\u2500 Preview \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500

        private void UpdatePreview(StyDocument.MarkerInfo info, Dictionary<string, string> v)
        {
            var doc = new FlowDocument { PagePadding = new Thickness(8), Background = Brushes.White, TextAlignment = TextAlignment.Left };
            string defaultFont = _project != null && !string.IsNullOrEmpty(_project.DefaultFont) ? _project.DefaultFont : "Segoe UI";
            doc.FontFamily = new FontFamily(defaultFont);
            double baseSize = _project != null ? _project.DefaultFontSize : 12;
            doc.FontSize = Px(baseSize);

            string styleType = Get(v, "styletype").ToLowerInvariant();
            if (info.IsRemoved || info.IsDisabled)
            {
                doc.Blocks.Add(new Paragraph(new Run(info.IsRemoved ? Str("Preview_Removed") : Str("Panel_DisabledNote"))) { Foreground = Brushes.Gray });
            }
            else if (styleType == "milestone")
            {
                doc.Blocks.Add(new Paragraph(new Run(Str("Preview_Milestone"))) { Foreground = Brushes.Gray });
            }
            else if (styleType == "paragraph")
            {
                var p = new Paragraph();
                var run = new Run(Str("Preview_Sample") + " " + Str("Preview_Sample"));
                ApplyCharacter(run, v, baseSize, defaultFont);
                p.Inlines.Add(run);
                p.Margin = new Thickness(Inches(v, "leftmargin"), Pt(v, "spacebefore"), Inches(v, "rightmargin"), Pt(v, "spaceafter"));
                p.TextIndent = Inches(v, "firstlineindent");
                string just = Get(v, "justification").ToLowerInvariant();
                p.TextAlignment = just == "center" ? TextAlignment.Center
                    : just == "right" ? TextAlignment.Right
                    : just == "both" ? TextAlignment.Justify : TextAlignment.Left;
                double ls;
                if (double.TryParse(Get(v, "linespacing"), NumberStyles.Float, CultureInfo.InvariantCulture, out ls) && ls > 0)
                    p.LineHeight = run.FontSize * 1.2 * (1 + ls / 2);
                doc.Blocks.Add(p);
            }
            else
            {
                var p = new Paragraph();
                p.Inlines.Add(new Run(Str("Preview_Before").Trim() + " "));
                var run = new Run(Str("Preview_Styled"));
                ApplyCharacter(run, v, baseSize, defaultFont);
                p.Inlines.Add(run);
                p.Inlines.Add(new Run(" " + Str("Preview_After").Trim()));
                doc.Blocks.Add(p);
            }
            PreviewViewer.Document = doc;
        }

        private void ApplyCharacter(Run run, Dictionary<string, string> v, double baseSize, string defaultFont)
        {
            string font = Get(v, "fontname");
            if (font.Length > 0) run.FontFamily = new FontFamily(font + ", " + defaultFont);
            double size;
            if (double.TryParse(Get(v, "fontsize"), NumberStyles.Float, CultureInfo.InvariantCulture, out size) && size > 0)
                run.FontSize = Px(Math.Max(4, Math.Min(96, size - 12 + baseSize)));
            if (IsOn(v, "bold")) run.FontWeight = FontWeights.Bold;
            if (IsOn(v, "italic")) run.FontStyle = FontStyles.Italic;
            if (IsOn(v, "underline")) run.TextDecorations = TextDecorations.Underline;
            if (IsOn(v, "smallcaps")) run.Typography.Capitals = FontCapitals.SmallCaps;
            if (IsOn(v, "superscript")) { run.BaselineAlignment = BaselineAlignment.Superscript; run.FontSize *= 0.7; }
            else if (IsOn(v, "subscript")) { run.BaselineAlignment = BaselineAlignment.Subscript; run.FontSize *= 0.7; }
            int rgb = v.ContainsKey("colorname") ? ColorService.ParseColorName(Get(v, "colorname")) : ColorService.ParseColorValue(Get(v, "color"));
            if (rgb >= 0) run.Foreground = new SolidColorBrush(PropertyRow.ToColor(rgb));
        }

        private static string Get(Dictionary<string, string> v, string key)
        {
            string s;
            return v.TryGetValue(key, out s) ? s : "";
        }

        private static bool IsOn(Dictionary<string, string> v, string key)
        {
            string s;
            return v.TryGetValue(key, out s) && s != "-";
        }

        private static double Px(double points) { return points * 96.0 / 72.0; }

        private static double Inches(Dictionary<string, string> v, string key)
        {
            double d;
            if (!double.TryParse(Get(v, key), NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return 0;
            return Math.Max(-300, Math.Min(300, d * 96));
        }

        private static double Pt(Dictionary<string, string> v, string key)
        {
            double d;
            if (!double.TryParse(Get(v, key), NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return 0;
            return Math.Max(0, Math.Min(200, Px(d)));
        }

        // \u2500\u2500 Edits from the property rows \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500

        /// <summary>Sets a property of the selected marker (value null deletes its lines).</summary>
        public void CommitValue(PropertyRow row, string keyword, string value)
        {
            StyDocument.MarkerInfo info = SelectedInfo;
            if (info == null) return;
            string[] keys = row.Keys;

            if (value == null)
            {
                var lines = new List<int>();
                foreach (string key in keys)
                {
                    List<int> list;
                    if (info.KeyLines.TryGetValue(key, out list)) lines.AddRange(list);
                }
                if (lines.Count == 0) return;
                _doc.DeleteLines(lines);
                AfterEdit();
                return;
            }

            int bound = _doc.BoundLine(info, keys);
            StyLine boundLine = bound >= 0 ? _doc.Lines[bound] : null;
            if (boundLine != null && boundLine.Value == value
                && string.Equals(boundLine.Keyword, keyword, StringComparison.OrdinalIgnoreCase))
                return;

            // A decimal \Color gets a comment naming the color (an existing comment of the user's is kept).
            string comment = null;
            bool isColor = keyword.ToLowerInvariant() == "color";
            bool isColorName = keyword.ToLowerInvariant() == "colorname";
            if (isColor && !ColorService.IsHexValue(value))
            {
                if (boundLine == null || ColorService.IsColorComment(boundLine.Comment))
                    comment = ColorService.CommentFor(ColorService.ParseColorValue(value));
            }
            else if ((isColor || isColorName) && boundLine != null && ColorService.IsColorComment(boundLine.Comment))
            {
                comment = "";
            }

            if (boundLine != null)
            {
                // Keep the spelling the file uses unless the keyword itself changes (\Color <-> \ColorName).
                string spelling = string.Equals(boundLine.Keyword, keyword, StringComparison.OrdinalIgnoreCase)
                    ? boundLine.Keyword : keyword;
                _doc.SetLineValue(bound, spelling, value, comment);
            }
            else
            {
                _doc.AddProperty(info.Marker, keyword, value, comment ?? "");
            }
            AfterEdit();
        }

        public void SetLineDisabled(int index, bool disabled)
        {
            _doc.SetLineDisabled(index, disabled);
            AfterEdit();
        }

        public void ChooseList(PropertyRow row, string current)
        {
            StyDocument.MarkerInfo info = SelectedInfo;
            if (info == null) return;
            var options = new List<ChecklistWindow.Option>();
            IEnumerable<string> stock = null;
            string title;
            Dictionary<string, string> baseValues = BaseValues(info.Marker);
            if (row.Def.Kind == PropertyKind.MarkerList)
            {
                title = string.Format(Str("Check_TitleOccursUnder"), info.Marker);
                options.Add(new ChecklistWindow.Option { Value = "NEST", Label = Str("Check_Nest") });
                foreach (string m in KnownMarkers().OrderBy(m => _base != null && _base.Has(m) ? _base.Order.IndexOf(m) : int.MaxValue))
                {
                    string type = _base != null ? _base.StyleType(m) : "";
                    string name = _base != null ? _base.Name(m) : "";
                    options.Add(new ChecklistWindow.Option
                    {
                        Value = m,
                        Label = "\\" + m + "   " + name + (type.Length > 0 ? "   [" + type + "]" : "")
                    });
                }
                string s;
                if (baseValues != null && baseValues.TryGetValue("occursunder", out s)) stock = StyLine.SplitWords(s);
            }
            else
            {
                title = string.Format(Str("Check_TitleTextProperties"), info.Marker);
                foreach (string w in StyleKeywords.TextPropertyWords)
                    options.Add(new ChecklistWindow.Option { Value = w, Label = w });
                string s;
                if (baseValues != null && baseValues.TryGetValue("textproperties", out s)) stock = StyLine.SplitWords(s.ToLowerInvariant());
            }

            var selected = row.Def.Kind == PropertyKind.Words
                ? StyLine.SplitWords(current.ToLowerInvariant())
                : StyLine.SplitWords(current);
            var dlg = new ChecklistWindow(this, title, options, selected, stock);
            if (dlg.ShowDialog() != true) return;
            string value = string.Join(" ", dlg.Result.ToArray());
            row.Commit(null, value.Length == 0 ? null : value);
        }

        public void ChooseColor(PropertyRow row, int rgb)
        {
            using (var dlg = new System.Windows.Forms.ColorDialog())
            {
                dlg.FullOpen = true;
                dlg.AnyColor = true;
                if (rgb >= 0) dlg.Color = System.Drawing.Color.FromArgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
                dlg.CustomColors = ColorService.CommonColors.Select(p => p.Key).ToArray();   // OLE order, as the dialog expects
                if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                int chosen = (dlg.Color.R << 16) | (dlg.Color.G << 8) | dlg.Color.B;
                StyLine existing = row.BoundColorLine;
                string value = existing != null && ColorService.IsHexValue(existing.Value)
                    ? ColorService.FormatHex(chosen)
                    : ColorService.FormatOle(chosen);
                row.Commit("Color", value);
            }
        }

        private void AfterEdit()
        {
            _isDirty = true;
            _statusMessage = null;
            RefreshAll();
        }

        // \u2500\u2500 Marker list \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500

        private void MarkerGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressSelection) return;
            MarkerEntry entry = MarkerGrid.SelectedItem as MarkerEntry;
            _selectedMarker = entry != null ? entry.Marker : null;
            RefreshPanel();
        }

        private void Select(string marker)
        {
            _selectedMarker = marker;
            RefreshAll();
            MarkerEntry entry = _entries.FirstOrDefault(e => e.Marker == marker);
            if (entry != null) MarkerGrid.ScrollIntoView(entry);
        }

        private void MarkerGrid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Delete && SelectedInfo != null)
            {
                DeleteSelected();
                e.Handled = true;
            }
        }

        private void DisabledCheck_Click(object sender, RoutedEventArgs e)
        {
            var box = sender as CheckBox;
            MarkerEntry entry = box != null ? box.DataContext as MarkerEntry : null;
            if (entry == null) return;
            _doc.SetMarkerDisabled(entry.Marker, !entry.IsDisabled);
            _isDirty = true;
            Select(entry.Marker);
        }

        private void AddStock_Click(object sender, RoutedEventArgs e)
        {
            if (_base == null)
            {
                MessageBox.Show(this, Str("Picker_NoBase"), Str("Picker_Title"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var dlg = new MarkerPickerWindow(this, _base, _analysis.Markers.Keys.ToList());
            if (dlg.ShowDialog() != true || dlg.SelectedMarker == null) return;
            string marker = dlg.SelectedMarker;
            if (!_analysis.Markers.ContainsKey(marker))
            {
                _doc.AppendMarkerBlock(marker, false);
                _isDirty = true;
            }
            Select(marker);
        }

        private void NewMarker_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new NewMarkerWindow(this, InBase, m => _analysis.Markers.ContainsKey(m));
            if (dlg.ShowDialog() != true) return;
            string m2 = dlg.Marker;
            _doc.AppendMarkerBlock(m2, false);
            _doc.AddProperty(m2, "Name", dlg.MarkerName, "");
            if (!string.IsNullOrEmpty(dlg.StyleType)) _doc.AddProperty(m2, "StyleType", dlg.StyleType, "");
            if (dlg.Endmarker.Length > 0) _doc.AddProperty(m2, "Endmarker", dlg.Endmarker, "");
            if (!string.IsNullOrEmpty(dlg.TextType)) _doc.AddProperty(m2, "TextType", dlg.TextType, "");
            if (dlg.TextProperties.Length > 0) _doc.AddProperty(m2, "TextProperties", dlg.TextProperties, "");
            _isDirty = true;
            Select(m2);
        }

        private void Remove_Click(object sender, RoutedEventArgs e)
        {
            StyDocument.MarkerInfo info = SelectedInfo;
            if (info == null) return;
            _doc.SetMarkerRemoved(info.Marker, !info.IsRemoved);
            _isDirty = true;
            Select(info.Marker);
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            DeleteSelected();
        }

        private void DeleteSelected()
        {
            StyDocument.MarkerInfo info = SelectedInfo;
            if (info == null) return;
            if (MessageBox.Show(this, string.Format(Str("Dlg_DeleteBody"), info.Marker), Str("Dlg_DeleteTitle"),
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;
            int index = _entries.IndexOf(_entries.FirstOrDefault(x => x.Marker == info.Marker));
            _doc.DeleteMarker(info.Marker);
            _isDirty = true;
            _analysis = _doc.Analyze();
            var remaining = _analysis.MarkersInFileOrder.ToList();
            string next = remaining.Count == 0 ? null : remaining[Math.Max(0, Math.Min(index, remaining.Count - 1))].Marker;
            Select(next);
        }

        // \u2500\u2500 Status \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500

        private void UpdateStatus()
        {
            string appTitle = Str("App_Title");
            string dirty = _isDirty ? "* " : "";
            string file = _currentPath ?? Str("Status_Unsaved");
            int count = _analysis.Markers.Count;
            int warnings = _entries.Count(e => e.HasWarning);
            string summary = string.Format(Str("Status_SummaryFormat"), count);
            if (warnings > 0) summary += "  \u00B7  " + string.Format(Str("Status_WarningsFormat"), warnings);
            if (_analysis.OrphanLines.Count > 0) summary += "  \u00B7  " + string.Format(Str("Warn_Orphans"), _analysis.OrphanLines.Count);
            if (_statusMessage != null) summary += "  \u00B7  " + _statusMessage;
            StatusText.Text = dirty + file + "  \u2014  " + summary;
            Title = _currentPath != null
                ? dirty + appTitle + " \u2014 " + Path.GetFileName(_currentPath)
                : dirty + appTitle;

            ProjectHeaderText.Text = _project != null
                ? string.Format(Str("ProjectBar_Format"), _project.ShortName)
                : (_currentPath ?? Str("Status_Unsaved"));
            if (_base != null)
            {
                BaseSheetText.Text = string.Format(Str("ProjectBar_Base"), _base.Path);
                BaseSheetText.Foreground = Brushes.DimGray;
            }
            else
            {
                BaseSheetText.Text = string.Format(Str("ProjectBar_BaseMissing"), _baseMissingName ?? _baseName);
                BaseSheetText.Foreground = Brushes.Firebrick;
            }
        }

        // \u2500\u2500 File operations \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500

        /// <summary>Opens the file, or starts an empty document that Save writes to that path.</summary>
        public void OpenOrCreateFile(string path)
        {
            string full = Path.GetFullPath(path);
            if (File.Exists(full))
            {
                LoadFile(full);
                return;
            }
            _doc = new StyDocument();
            _currentPath = full;
            _isDirty = false;
            _selectedMarker = null;
            LoadBase(full);
            RefreshAll();
        }

        private void LoadFile(string path)
        {
            StyDocument doc;
            try
            {
                doc = StyFileService.Read(path);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, Str("Dlg_OpenErrorBody") + "\n" + ex.Message, Str("Dlg_OpenErrorTitle"),
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            _doc = doc;
            _currentPath = path;
            _isDirty = false;
            _statusMessage = null;
            LoadBase(path);
            _analysis = _doc.Analyze();
            StyDocument.MarkerInfo first = _analysis.MarkersInFileOrder.FirstOrDefault();
            _selectedMarker = first != null ? first.Marker : null;
            RefreshAll();
        }

        private bool SaveToPath(string path)
        {
            try
            {
                StyFileService.Write(_doc, path);
                bool newPath = !string.Equals(path, _currentPath, StringComparison.OrdinalIgnoreCase);
                _currentPath = path;
                _isDirty = false;
                _statusMessage = Str("Status_Saved");
                if (newPath) LoadBase(path);
                RefreshAll();
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, Str("Dlg_SaveErrorBody") + "\n" + ex.Message,
                    Str("Dlg_SaveErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private bool PromptSaveIfDirty()
        {
            if (!_isDirty) return true;
            MessageBoxResult result = MessageBox.Show(this, Str("Dlg_UnsavedBody"), Str("Dlg_UnsavedTitle"),
                MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes) return DoSave();
            if (result == MessageBoxResult.No) return true;
            return false;
        }

        private bool DoSave()
        {
            CommitFocusedEditor();
            if (_currentPath != null) return SaveToPath(_currentPath);
            return DoSaveAs();
        }

        private bool DoSaveAs()
        {
            CommitFocusedEditor();
            var dlg = new SaveFileDialog
            {
                Title = Str("Dlg_SaveTitle"),
                Filter = Str("Dlg_FileFilter"),
                DefaultExt = "sty",
                FileName = _currentPath != null ? Path.GetFileName(_currentPath) : "custom.sty"
            };
            if (_currentPath != null)
                dlg.InitialDirectory = Path.GetDirectoryName(_currentPath);
            if (dlg.ShowDialog(this) == true)
                return SaveToPath(dlg.FileName);
            return false;
        }

        /// <summary>A text box being edited commits when it loses focus; move focus so Ctrl+S saves its text too.</summary>
        private void CommitFocusedEditor()
        {
            if (Keyboard.FocusedElement is TextBox || Keyboard.FocusedElement is ComboBox)
                MarkerGrid.Focus();
        }

        private void Always_CanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = true;
        }

        private void New_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            if (!PromptSaveIfDirty()) return;
            _doc = new StyDocument();
            _currentPath = null;
            _isDirty = false;
            _selectedMarker = null;
            _statusMessage = null;
            RefreshAll();
        }

        private void Open_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            if (!PromptSaveIfDirty()) return;
            var dlg = new OpenFileDialog
            {
                Title = Str("Dlg_OpenTitle"),
                Filter = Str("Dlg_FileFilter")
            };
            if (_currentPath != null) dlg.InitialDirectory = Path.GetDirectoryName(_currentPath);
            if (dlg.ShowDialog(this) == true)
                LoadFile(dlg.FileName);
        }

        private void Save_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            DoSave();
        }

        private void SaveAs_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            DoSaveAs();
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            CommitFocusedEditor();
            if (!PromptSaveIfDirty()) e.Cancel = true;
        }
    }

    public class LanguageOption
    {
        public string DisplayName { get; set; }
        public string Culture { get; set; }
    }
}
