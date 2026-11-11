using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CustomStyEditor.Services;

namespace CustomStyEditor
{
    /// <summary>Base for the small dialogs: OK/Cancel row, localized through MainWindow.Str.</summary>
    public class DialogBase : Window
    {
        protected readonly DockPanel Root = new DockPanel { Margin = new Thickness(10) };
        protected Button OkButton;

        public DialogBase(Window owner, string titleKey, double width, double height)
        {
            Owner = owner;
            Title = MainWindow.StrStatic(titleKey);
            Width = width;
            Height = height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.CanResizeWithGrip;

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
            OkButton = new Button { Content = MainWindow.StrStatic("Btn_OK"), MinWidth = 80, IsDefault = true, Margin = new Thickness(0, 0, 6, 0) };
            OkButton.Click += delegate { if (Validate()) DialogResult = true; };
            var cancel = new Button { Content = MainWindow.StrStatic("Btn_Cancel"), MinWidth = 80, IsCancel = true };
            buttons.Children.Add(OkButton);
            buttons.Children.Add(cancel);
            DockPanel.SetDock(buttons, Dock.Bottom);
            Root.Children.Add(buttons);
            Content = Root;
        }

        protected virtual bool Validate() { return true; }

        protected static TextBlock Label(string key)
        {
            return new TextBlock { Text = MainWindow.StrStatic(key), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        }
    }

    /// <summary>Lists the base stylesheet's markers; returns the chosen one.</summary>
    public class MarkerPickerWindow : DialogBase
    {
        private class Item
        {
            public string Marker;
            public string Text;
            public override string ToString() { return Text; }
        }

        private readonly TextBox _filter = new TextBox();
        private readonly ListBox _list = new ListBox();
        private readonly List<Item> _items = new List<Item>();

        public string SelectedMarker { get; private set; }

        public MarkerPickerWindow(Window owner, BaseStylesheet sheet, ICollection<string> inFile)
            : base(owner, "Picker_Title", 560, 620)
        {
            var top = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var label = Label("Picker_Filter");
            DockPanel.SetDock(label, Dock.Left);
            top.Children.Add(label);
            top.Children.Add(_filter);
            DockPanel.SetDock(top, Dock.Top);
            Root.Children.Add(top);

            _list.FontFamily = new FontFamily("Consolas");
            Root.Children.Add(_list);

            string inFileText = MainWindow.StrStatic("Picker_InFile");
            foreach (string m in sheet.Order)
            {
                string type = sheet.StyleType(m);
                _items.Add(new Item
                {
                    Marker = m,
                    Text = ("\\" + m).PadRight(10) + " " + sheet.Name(m)
                        + (type.Length > 0 ? "   [" + type + "]" : "")
                        + (inFile.Contains(m) ? "   " + inFileText : "")
                });
            }
            ApplyFilter();
            _filter.TextChanged += delegate { ApplyFilter(); };
            _list.MouseDoubleClick += delegate { if (Validate()) DialogResult = true; };
            Loaded += delegate { _filter.Focus(); };
        }

        private void ApplyFilter()
        {
            string f = _filter.Text.Trim().TrimStart('\\');
            _list.ItemsSource = f.Length == 0
                ? _items
                : _items.Where(i => i.Text.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0
                                    || i.Marker.StartsWith(f, StringComparison.Ordinal)).ToList();
            if (_list.Items.Count > 0) _list.SelectedIndex = 0;
        }

        protected override bool Validate()
        {
            var item = _list.SelectedItem as Item;
            if (item == null) return false;
            SelectedMarker = item.Marker;
            return true;
        }
    }

    /// <summary>Asks for a new marker's name, style type and related fields.</summary>
    public class NewMarkerWindow : DialogBase
    {
        private readonly TextBox _marker = new TextBox();
        private readonly TextBox _name = new TextBox();
        private readonly ComboBox _styleType = new ComboBox();
        private readonly TextBox _endmarker = new TextBox();
        private readonly ComboBox _textType = new ComboBox();
        private readonly TextBox _textProperties = new TextBox();
        private readonly TextBlock _error = new TextBlock { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
        private readonly Func<string, bool> _inBase;
        private readonly Func<string, bool> _inFile;
        private bool _endmarkerTouched;

        public string Marker { get { return _marker.Text.Trim().TrimStart('\\'); } }
        public string MarkerName { get { return _name.Text.Trim(); } }
        public string StyleType { get { return (string)_styleType.SelectedItem; } }
        public string Endmarker { get { return _endmarker.Text.Trim(); } }
        public string TextType { get { return (string)_textType.SelectedItem; } }
        public string TextProperties { get { return _textProperties.Text.Trim(); } }

        public NewMarkerWindow(Window owner, Func<string, bool> inBase, Func<string, bool> inFile)
            : base(owner, "New_Title", 480, 330)
        {
            _inBase = inBase;
            _inFile = inFile;
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            AddRow(grid, "New_Marker", _marker);
            AddRow(grid, "New_Name", _name);
            AddRow(grid, "New_StyleType", _styleType);
            AddRow(grid, "New_Endmarker", _endmarker);
            AddRow(grid, "New_TextType", _textType);
            AddRow(grid, "New_TextProperties", _textProperties);
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(_error, grid.RowDefinitions.Count - 1);
            Grid.SetColumnSpan(_error, 2);
            grid.Children.Add(_error);
            Root.Children.Add(grid);

            _marker.FontFamily = new FontFamily("Consolas");
            _endmarker.FontFamily = new FontFamily("Consolas");
            _styleType.ItemsSource = StyleKeywords.StyleTypes;
            _styleType.SelectedItem = "Character";
            _textType.ItemsSource = StyleKeywords.TextTypes;
            _textType.SelectedItem = "VerseText";
            _textProperties.Text = "publishable vernacular";

            _marker.TextChanged += delegate { SuggestEndmarker(); };
            _styleType.SelectionChanged += delegate
            {
                SuggestEndmarker();
                bool para = StyleType == "Paragraph";
                if (para && !_textProperties.Text.Contains("paragraph"))
                    _textProperties.Text = "paragraph " + _textProperties.Text;
                if (!para) _textProperties.Text = _textProperties.Text.Replace("paragraph ", "");
            };
            _endmarker.TextChanged += delegate { if (_endmarker.IsKeyboardFocusWithin) _endmarkerTouched = true; };
            Loaded += delegate { _marker.Focus(); };
        }

        private void SuggestEndmarker()
        {
            if (_endmarkerTouched) return;
            string m = Marker;
            string type = StyleType;
            if (m.Length == 0 || type == "Paragraph") _endmarker.Text = "";
            else if (type == "Milestone") _endmarker.Text = m.EndsWith("-s") ? m.Substring(0, m.Length - 2) + "-e" : "";
            else _endmarker.Text = m + "*";
        }

        private static void AddRow(Grid grid, string labelKey, Control control)
        {
            int row = grid.RowDefinitions.Count;
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var label = Label(labelKey);
            Grid.SetRow(label, row);
            grid.Children.Add(label);
            control.Margin = new Thickness(0, 2, 0, 2);
            Grid.SetRow(control, row);
            Grid.SetColumn(control, 1);
            grid.Children.Add(control);
        }

        protected override bool Validate()
        {
            string m = Marker;
            if (m.Length == 0) return Fail(MainWindow.StrStatic("New_ErrEmpty"), _marker);
            if (m.IndexOfAny(new[] { ' ', '\t', '\\', '*', '#' }) >= 0) return Fail(MainWindow.StrStatic("New_ErrChars"), _marker);
            if (_inBase(m)) return Fail(string.Format(MainWindow.StrStatic("New_ErrStock"), m), _marker);
            if (_inFile(m)) return Fail(string.Format(MainWindow.StrStatic("New_ErrExists"), m), _marker);
            if (MarkerName.Length == 0) return Fail(MainWindow.StrStatic("New_ErrName"), _name);
            if (!m.StartsWith("z"))
            {
                MessageBoxResult r = MessageBox.Show(this, string.Format(MainWindow.StrStatic("New_WarnZ"), m),
                    Title, MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (r != MessageBoxResult.Yes) return false;
            }
            return true;
        }

        private bool Fail(string message, Control focus)
        {
            _error.Text = message;
            focus.Focus();
            return false;
        }
    }

    /// <summary>A filtered checklist (OccursUnder markers or TextProperties words).</summary>
    public class ChecklistWindow : DialogBase
    {
        public class Option
        {
            public string Value;
            public string Label;
            public CheckBox Box;
        }

        private readonly TextBox _filter = new TextBox();
        private readonly StackPanel _items = new StackPanel();
        private readonly List<Option> _options = new List<Option>();
        private readonly List<string> _originalOrder;

        public ChecklistWindow(Window owner, string title, IEnumerable<Option> options, IEnumerable<string> selected,
            IEnumerable<string> stock)
            : base(owner, "Btn_OK", 520, 640)
        {
            Title = title;
            _originalOrder = selected.ToList();
            var selectedSet = new HashSet<string>(_originalOrder);
            var stockList = stock != null ? stock.ToList() : null;

            var top = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var tools = new StackPanel { Orientation = Orientation.Horizontal };
            if (stockList != null)
            {
                var copy = new Button { Content = MainWindow.StrStatic("Check_CopyStock"), Padding = new Thickness(6, 1, 6, 1), Margin = new Thickness(6, 0, 0, 0) };
                copy.Click += delegate
                {
                    // The stock list, in its own order.
                    var set = new HashSet<string>(stockList);
                    foreach (Option o in _options) o.Box.IsChecked = set.Contains(o.Value);
                    _originalOrder.Clear();
                    _originalOrder.AddRange(stockList);
                };
                tools.Children.Add(copy);
            }
            var clear = new Button { Content = MainWindow.StrStatic("Check_Clear"), Padding = new Thickness(6, 1, 6, 1), Margin = new Thickness(6, 0, 0, 0) };
            clear.Click += delegate { foreach (Option o in _options) o.Box.IsChecked = false; };
            tools.Children.Add(clear);
            DockPanel.SetDock(tools, Dock.Right);
            top.Children.Add(tools);
            var label = Label("Picker_Filter");
            DockPanel.SetDock(label, Dock.Left);
            top.Children.Add(label);
            top.Children.Add(_filter);
            DockPanel.SetDock(top, Dock.Top);
            Root.Children.Add(top);

            // Values set now or in the stock list but not offered stay visible at the top.
            var all = new List<Option>();
            var offered = new HashSet<string>(options.Select(o => o.Value));
            var extra = _originalOrder.Concat(stockList ?? new List<string>()).Where(v => !offered.Contains(v)).Distinct();
            foreach (string v in extra)
                all.Add(new Option { Value = v, Label = v + "   " + MainWindow.StrStatic("Check_Unknown") });
            all.AddRange(options);
            foreach (Option o in all)
            {
                o.Box = new CheckBox { Content = o.Label, IsChecked = selectedSet.Contains(o.Value), Margin = new Thickness(2, 1, 2, 1) };
                _options.Add(o);
                _items.Children.Add(o.Box);
            }
            Root.Children.Add(new ScrollViewer { Content = _items, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });

            _filter.TextChanged += delegate
            {
                string f = _filter.Text.Trim().TrimStart('\\');
                foreach (Option o in _options)
                    o.Box.Visibility = f.Length == 0 || o.Label.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0
                        ? Visibility.Visible : Visibility.Collapsed;
            };
            Loaded += delegate { _filter.Focus(); };
        }

        /// <summary>Ticked values: those already set keep their order, newly ticked ones follow in list order.</summary>
        public List<string> Result
        {
            get
            {
                var ticked = new HashSet<string>(_options.Where(o => o.Box.IsChecked == true).Select(o => o.Value));
                var result = _originalOrder.Where(ticked.Contains).ToList();
                foreach (Option o in _options)
                    if (ticked.Contains(o.Value) && !result.Contains(o.Value)) result.Add(o.Value);
                return result;
            }
        }
    }
}
