using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CustomStyEditor.Models;
using CustomStyEditor.Services;

namespace CustomStyEditor
{
    /// <summary>
    /// One keyword in the property panel: [used?] [keyword] [editor] [stock value] [x], plus a
    /// warning line. The row shows the line Paratext uses for the selected marker (or the last
    /// commented-out one) and hands every change to the MainWindow.
    /// </summary>
    public class PropertyRow
    {
        public readonly KeywordDef Def;
        private readonly MainWindow _owner;

        private CheckBox _enabled;
        private TextBlock _label;
        private TextBlock _stock;
        private Button _clear;
        private TextBlock _warning;
        private FrameworkElement _editor;

        private ComboBox _combo;        // Flag, Choice, Font
        private TextBox _box;           // Text, numbers, lists
        private Border _swatch;         // Color
        private TextBlock _colorText;   // Color
        private ComboBox _colorNames;   // Color

        private int _boundLine = -1;
        private string _shownValue = "";
        private bool _updating;

        public PropertyRow(KeywordDef def, MainWindow owner)
        {
            Def = def;
            _owner = owner;
        }

        /// <summary>Lowercase keywords the row covers.</summary>
        public string[] Keys
        {
            get { return Def.Kind == PropertyKind.Color ? new[] { "color", "colorname" } : new[] { Def.Key }; }
        }

        public void AddTo(Grid grid)
        {
            int row = grid.RowDefinitions.Count;
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            _enabled = new CheckBox { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
            _enabled.Click += Enabled_Click;
            Place(grid, _enabled, row, 0);

            _label = new TextBlock
            {
                Text = "\\" + Def.Keyword,
                FontFamily = new FontFamily("Consolas"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };
            Place(grid, _label, row, 1);

            _editor = BuildEditor();
            _editor.Margin = new Thickness(0, 2, 8, 2);
            Place(grid, _editor, row, 2);

            _stock = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromRgb(0x70, 0x70, 0x70)),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 230,
                Margin = new Thickness(0, 0, 6, 0)
            };
            Place(grid, _stock, row, 3);

            _clear = new Button
            {
                Content = "\u2715",
                Padding = new Thickness(5, 0, 5, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Focusable = false
            };
            _clear.Click += delegate { Commit(null, null); };
            Place(grid, _clear, row, 4);

            System.Windows.Automation.AutomationProperties.SetAutomationId(_enabled, "Row_" + Def.Keyword + "_Enabled");
            System.Windows.Automation.AutomationProperties.SetAutomationId(_clear, "Row_" + Def.Keyword + "_Clear");
            if (_combo != null) System.Windows.Automation.AutomationProperties.SetAutomationId(_combo, "Row_" + Def.Keyword + "_Combo");
            if (_box != null) System.Windows.Automation.AutomationProperties.SetAutomationId(_box, "Row_" + Def.Keyword + "_Box");
            if (_colorNames != null) System.Windows.Automation.AutomationProperties.SetAutomationId(_colorNames, "Row_ColorNames");

            _warning = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromRgb(0xB0, 0x00, 0x20)),
                TextWrapping = TextWrapping.Wrap,
                Visibility = Visibility.Collapsed,
                Margin = new Thickness(0, 0, 0, 4)
            };
            Place(grid, _warning, row + 1, 2);
            Grid.SetColumnSpan(_warning, 3);
        }

        private static void Place(Grid grid, UIElement e, int row, int col)
        {
            Grid.SetRow(e, row);
            Grid.SetColumn(e, col);
            grid.Children.Add(e);
        }

        private FrameworkElement BuildEditor()
        {
            switch (Def.Kind)
            {
                case PropertyKind.Flag:
                case PropertyKind.Choice:
                    _combo = new ComboBox { MinWidth = 150, HorizontalAlignment = HorizontalAlignment.Left };
                    _combo.SelectionChanged += Combo_SelectionChanged;
                    return _combo;

                case PropertyKind.Font:
                    _combo = new ComboBox { IsEditable = true, MinWidth = 200, HorizontalAlignment = HorizontalAlignment.Left };
                    _combo.ItemsSource = Fonts.SystemFontFamilies.Select(f => f.Source).OrderBy(s => s, StringComparer.CurrentCultureIgnoreCase).ToList();
                    _combo.LostKeyboardFocus += delegate { CommitText(_combo.Text); };
                    _combo.DropDownClosed += delegate { CommitText(_combo.Text); };
                    _combo.KeyDown += delegate(object s, KeyEventArgs e) { if (e.Key == Key.Enter) CommitText(_combo.Text); };
                    return _combo;

                case PropertyKind.Color:
                    return BuildColorEditor();

                case PropertyKind.MarkerList:
                case PropertyKind.Words:
                {
                    var dock = new DockPanel();
                    var choose = new Button { Padding = new Thickness(6, 0, 6, 0), Margin = new Thickness(4, 0, 0, 0) };
                    choose.SetResourceReference(ContentControl.ContentProperty, "Btn_Choose");
                    choose.Click += delegate { _owner.ChooseList(this, _shownValue); };
                    DockPanel.SetDock(choose, Dock.Right);
                    dock.Children.Add(choose);
                    _box = NewTextBox();
                    _box.TextWrapping = TextWrapping.Wrap;
                    dock.Children.Add(_box);
                    return dock;
                }

                default:
                {
                    _box = NewTextBox();
                    if (Def.UnitKey == null) return _box;
                    _box.Width = 80;
                    _box.HorizontalAlignment = HorizontalAlignment.Left;
                    var panel = new StackPanel { Orientation = Orientation.Horizontal };
                    panel.Children.Add(_box);
                    var unit = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0), Foreground = Brushes.DimGray };
                    unit.SetResourceReference(TextBlock.TextProperty, Def.UnitKey);
                    panel.Children.Add(unit);
                    return panel;
                }
            }
        }

        private TextBox NewTextBox()
        {
            var box = new TextBox { VerticalContentAlignment = VerticalAlignment.Center };
            box.LostKeyboardFocus += delegate { CommitText(box.Text); };
            box.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.Key == Key.Enter) CommitText(box.Text);
                else if (e.Key == Key.Escape) { _updating = true; box.Text = _shownValue; _updating = false; }
            };
            return box;
        }

        private FrameworkElement BuildColorEditor()
        {
            // Line 1: exact color; line 2: Paratext color name.
            var outer = new StackPanel();
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            var names = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 0) };
            outer.Children.Add(panel);
            outer.Children.Add(names);
            _swatch = new Border
            {
                Width = 22, Height = 16,
                BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1),
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0)
            };
            panel.Children.Add(_swatch);
            _colorText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, MinWidth = 60, Margin = new Thickness(0, 0, 6, 0) };
            panel.Children.Add(_colorText);
            var pick = new Button { Padding = new Thickness(6, 0, 6, 0), Margin = new Thickness(0, 0, 8, 0) };
            pick.SetResourceReference(ContentControl.ContentProperty, "Color_Pick");
            pick.Click += delegate { _owner.ChooseColor(this, CurrentRgb()); };
            panel.Children.Add(pick);
            var label = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) };
            label.SetResourceReference(TextBlock.TextProperty, "Color_NamesLabel");
            names.Children.Add(label);
            _colorNames = new ComboBox { MinWidth = 170 };
            _colorNames.ItemTemplate = ColorNameTemplate();
            _colorNames.ItemsSource = ColorService.ColorNames.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
            _colorNames.SelectionChanged += delegate
            {
                if (_updating || _colorNames.SelectedItem == null) return;
                Commit("ColorName", (string)_colorNames.SelectedItem);
            };
            names.Children.Add(_colorNames);
            return outer;
        }

        /// <summary>A swatch plus the name for each Paratext color name.</summary>
        private static DataTemplate ColorNameTemplate()
        {
            var sp = new FrameworkElementFactory(typeof(StackPanel));
            sp.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            var sw = new FrameworkElementFactory(typeof(Border));
            sw.SetValue(FrameworkElement.WidthProperty, 14.0);
            sw.SetValue(FrameworkElement.HeightProperty, 12.0);
            sw.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 6, 0));
            sw.SetValue(Border.BorderBrushProperty, Brushes.Gray);
            sw.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            sw.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding { Converter = new ColorNameBrushConverter() });
            sp.AppendChild(sw);
            var tb = new FrameworkElementFactory(typeof(TextBlock));
            tb.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding());
            sp.AppendChild(tb);
            return new DataTemplate { VisualTree = sp };
        }

        // \u2500\u2500 Refresh from the document \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500

        /// <summary>Shows the selected marker's value. editable is false for commented-out or removed entries.</summary>
        public void Refresh(StyDocument doc, StyDocument.MarkerInfo info, BaseStylesheet sheet, string marker,
            string baseName, bool editable, ICollection<string> knownMarkers)
        {
            _updating = true;
            try
            {
                _boundLine = doc.BoundLine(info, Keys);
                StyLine line = _boundLine >= 0 ? doc.Lines[_boundLine] : null;
                _shownValue = line != null ? line.Value : "";

                _enabled.IsEnabled = editable && line != null;
                _enabled.IsChecked = line != null && line.IsActive;
                _enabled.SetResourceReference(FrameworkElement.ToolTipProperty, "Tip_LineEnabled");
                _clear.IsEnabled = editable && line != null;
                _clear.Visibility = line != null ? Visibility.Visible : Visibility.Hidden;
                _clear.SetResourceReference(FrameworkElement.ToolTipProperty, "Tip_Clear");
                _editor.IsEnabled = editable;
                _editor.Opacity = line != null && line.IsDisabled ? 0.55 : 1.0;
                _label.FontWeight = line != null && line.IsActive ? FontWeights.Bold : FontWeights.Normal;
                _label.SetResourceReference(FrameworkElement.ToolTipProperty, "KwTip_" + Def.Keyword);

                ShowValue(line);
                ShowStock(sheet, marker, baseName);
                ShowWarnings(doc, info, line, knownMarkers);
            }
            finally
            {
                _updating = false;
            }
        }

        private void ShowValue(StyLine line)
        {
            switch (Def.Kind)
            {
                case PropertyKind.Flag:
                {
                    var items = new List<string> { _owner.Str("Flag_NotSet"), _owner.Str("Flag_On"), string.Format(_owner.Str("Flag_Off"), Def.Keyword) };
                    _combo.ItemsSource = items;
                    _combo.SelectedIndex = line == null ? 0 : (line.Value == "-" ? 2 : 1);
                    break;
                }
                case PropertyKind.Choice:
                {
                    var items = new List<string> { _owner.Str("Row_NotSet") };
                    items.AddRange(Def.Choices);
                    int index = 0;
                    if (line != null)
                    {
                        index = items.FindIndex(1, c => string.Equals(c, line.Value, StringComparison.OrdinalIgnoreCase));
                        if (index < 0)
                        {
                            items.Add(line.Value);
                            index = items.Count - 1;
                        }
                    }
                    _combo.ItemsSource = items;
                    _combo.SelectedIndex = index;
                    break;
                }
                case PropertyKind.Font:
                    _combo.Text = _shownValue;
                    break;
                case PropertyKind.Color:
                {
                    int rgb = CurrentRgb();
                    _swatch.Background = rgb >= 0 ? new SolidColorBrush(ToColor(rgb)) : Brushes.Transparent;
                    if (line == null) _colorText.Text = _owner.Str("Row_NotSet");
                    else if (line.Key == "colorname")
                        _colorText.Text = rgb >= 0 ? "\\ColorName" : _owner.Str("Color_UnknownName");
                    else
                        _colorText.Text = line.Value + (rgb >= 0 && !ColorService.IsHexValue(line.Value)
                            ? "  (#" + rgb.ToString("X6") + ")" : "");
                    _colorNames.SelectedItem = line != null && line.Key == "colorname"
                        ? ColorService.ColorNames.Keys.FirstOrDefault(k => string.Equals(k, line.Value, StringComparison.OrdinalIgnoreCase))
                        : null;
                    break;
                }
                default:
                    _box.Text = _shownValue;
                    _box.ClearValue(Control.BorderBrushProperty);
                    break;
            }
        }

        private void ShowStock(BaseStylesheet sheet, string marker, string baseName)
        {
            if (sheet == null || !sheet.Has(marker))
            {
                _stock.Text = "";
                _stock.ToolTip = null;
                return;
            }
            string value = null;
            foreach (string key in Keys)
            {
                string v = sheet.Get(marker, key);
                if (v != null) value = (key == "colorname" ? "ColorName " : "") + v;
            }
            string shown;
            if (Def.Kind == PropertyKind.Flag)
                shown = value != null && value != "-" ? _owner.Str("Flag_StockOn") : _owner.Str("Flag_StockOff");
            else
                shown = value ?? _owner.Str("Row_NotSet");
            _stock.Text = string.Format(_owner.Str("Row_Stock"), baseName, shown);
            _stock.ToolTip = Def.Kind == PropertyKind.Words
                ? _stock.Text + "\n" + _owner.Str("Note_TextProperties")
                : _stock.Text;
        }

        private void ShowWarnings(StyDocument doc, StyDocument.MarkerInfo info, StyLine line, ICollection<string> knownMarkers)
        {
            var msgs = new List<string>();
            List<int> overridden = doc.OverriddenLines(info, Keys);
            if (overridden.Count > 0)
                msgs.Add(string.Format(_owner.Str("Row_Overridden"), string.Join(", ", overridden.Select(i => (i + 1).ToString()).ToArray())));
            if (line != null && line.Value.Length > 0)
            {
                if (Def.Kind == PropertyKind.MarkerList && knownMarkers != null && knownMarkers.Count > 0)
                {
                    var unknown = StyLine.SplitWords(line.Value).Where(w => w != "NEST" && !knownMarkers.Contains(w)).ToArray();
                    if (unknown.Length > 0) msgs.Add(string.Format(_owner.Str("Warn_UnknownMarkers"), string.Join(" ", unknown)));
                }
                if (Def.Kind == PropertyKind.Words)
                {
                    var unknown = StyLine.SplitWords(line.Value).Where(w => !Def.Choices.Contains(w.ToLowerInvariant())).ToArray();
                    if (unknown.Length > 0) msgs.Add(string.Format(_owner.Str("Warn_UnknownWords"), string.Join(" ", unknown)));
                }
                if ((Def.Kind == PropertyKind.Integer || Def.Kind == PropertyKind.Number) && !IsValidNumber(line.Value))
                    msgs.Add(string.Format(_owner.Str("Warn_Invalid"), line.Value, Def.Keyword));
            }
            SetWarning(msgs);
        }

        private void SetWarning(List<string> msgs)
        {
            _warning.Text = string.Join("\n", msgs.ToArray());
            _warning.Visibility = msgs.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        public void SetVisible(bool visible)
        {
            Visibility v = visible ? Visibility.Visible : Visibility.Collapsed;
            _enabled.Visibility = v;
            _label.Visibility = v;
            _editor.Visibility = v;
            _stock.Visibility = v;
            _clear.Visibility = visible && _boundLine >= 0 ? Visibility.Visible : (visible ? Visibility.Hidden : Visibility.Collapsed);
            if (!visible) _warning.Visibility = Visibility.Collapsed;
        }

        // \u2500\u2500 Edits \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500

        private void Enabled_Click(object sender, RoutedEventArgs e)
        {
            if (_boundLine < 0) return;
            _owner.SetLineDisabled(_boundLine, _enabled.IsChecked != true);
        }

        private void Combo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_updating || _combo.SelectedIndex < 0) return;
            if (Def.Kind == PropertyKind.Flag)
            {
                int i = _combo.SelectedIndex;
                Commit(null, i == 0 ? null : (i == 1 ? "" : "-"));
            }
            else
            {
                Commit(null, _combo.SelectedIndex == 0 ? null : (string)_combo.SelectedItem);
            }
        }

        private void CommitText(string text)
        {
            if (_updating) return;
            string value = (text ?? "").Trim();
            if (value == _shownValue) return;
            if ((Def.Kind == PropertyKind.Integer || Def.Kind == PropertyKind.Number) && value.Length > 0 && !IsValidNumber(value))
            {
                if (_box != null) _box.BorderBrush = Brushes.Red;
                SetWarning(new List<string> { string.Format(_owner.Str("Warn_Invalid"), value, Def.Keyword) });
                return;
            }
            Commit(null, value.Length == 0 ? null : value);
        }

        /// <summary>Sets the value (null removes the lines). keyword: the spelling to write, null for the row's keyword.</summary>
        public void Commit(string keyword, string value)
        {
            if (_updating) return;
            _owner.CommitValue(this, keyword ?? Def.Keyword, value);
        }

        private bool IsValidNumber(string value)
        {
            if (Def.Kind == PropertyKind.Integer)
            {
                int n;
                return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out n);
            }
            double d;
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out d);
        }

        private int CurrentRgb()
        {
            if (_boundLine < 0) return -1;
            StyLine line = _owner.Document.Lines[_boundLine];
            return line.Key == "colorname" ? ColorService.ParseColorName(line.Value) : ColorService.ParseColorValue(line.Value);
        }

        /// <summary>The \Color line the row shows, when it is one (to keep its hex/decimal form).</summary>
        public StyLine BoundColorLine
        {
            get
            {
                if (_boundLine < 0) return null;
                StyLine line = _owner.Document.Lines[_boundLine];
                return line.Key == "color" ? line : null;
            }
        }

        public static Color ToColor(int rgb)
        {
            return Color.FromRgb((byte)((rgb >> 16) & 0xFF), (byte)((rgb >> 8) & 0xFF), (byte)(rgb & 0xFF));
        }
    }

    /// <summary>Paratext color name to a brush, for the color name drop-down.</summary>
    public class ColorNameBrushConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            int rgb = ColorService.ParseColorName(value as string);
            return rgb >= 0 ? (object)new SolidColorBrush(PropertyRow.ToColor(rgb)) : Brushes.Transparent;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
