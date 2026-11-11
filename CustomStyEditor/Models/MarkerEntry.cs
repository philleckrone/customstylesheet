using System.ComponentModel;

namespace CustomStyEditor.Models
{
    /// <summary>One row of the marker list: a marker that custom.sty mentions.</summary>
    public class MarkerEntry : INotifyPropertyChanged
    {
        private string _name = "";
        private string _changes = "";
        private string _status = "";
        private string _warning = "";
        private bool _isDisabled;
        private bool _isRemoved;

        public string Marker { get; private set; }

        public MarkerEntry(string marker)
        {
            Marker = marker;
        }

        public string DisplayMarker { get { return "\\" + Marker; } }

        public string Name { get { return _name; } set { Set(ref _name, value, "Name"); } }
        public string Changes { get { return _changes; } set { Set(ref _changes, value, "Changes"); } }
        public string Status { get { return _status; } set { Set(ref _status, value, "Status"); } }

        /// <summary>Warnings for the row's tooltip; "" when there are none.</summary>
        public string Warning
        {
            get { return _warning; }
            set
            {
                if (Set(ref _warning, value, "Warning"))
                    OnPropertyChanged("HasWarning");
            }
        }

        public bool HasWarning { get { return _warning.Length > 0; } }

        public bool IsDisabled
        {
            get { return _isDisabled; }
            set { Set(ref _isDisabled, value, "IsDisabled"); }
        }

        public bool IsRemoved
        {
            get { return _isRemoved; }
            set { Set(ref _isRemoved, value, "IsRemoved"); }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private bool Set<T>(ref T field, T value, string name)
        {
            if (Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(name);
            return true;
        }

        private void OnPropertyChanged(string name)
        {
            PropertyChangedEventHandler h = PropertyChanged;
            if (h != null) h(this, new PropertyChangedEventArgs(name));
        }
    }
}
