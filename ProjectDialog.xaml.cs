using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace MetroPlanner
{
    public partial class ProjectDialog : Window
    {
        List<(string Name, double Lng, double Lat)> _cities;

        public string ProjectName => NameBox.Text.Trim();
        public string CityName { get; private set; } = "";
        public double Lng { get; private set; }
        public double Lat { get; private set; }

        public ProjectDialog(IEnumerable<(string Name, double Lng, double Lat)> cities)
        {
            InitializeComponent();
            _cities = cities.ToList();
            foreach (var c in _cities)
                CityCombo.Items.Add(new ComboBoxItem { Content = c.Name, Tag = $"{c.Lng.ToString(CultureInfo.InvariantCulture)},{c.Lat.ToString(CultureInfo.InvariantCulture)}" });
            NameBox.Focus();
            NameBox.SelectAll();
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (ProjectName.Length == 0) { MessageBox.Show("请输入项目名称", "提示", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            CityName = CityCombo.Text.Trim();
            Lng = 0; Lat = 0;
            if (CityCombo.SelectedItem is ComboBoxItem it && it.Tag is string tag)
            {
                var p = tag.Split(',');
                if (p.Length == 2 && double.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var lng) && double.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var lat))
                { Lng = lng; Lat = lat; }
            }
            else
            {
                var m = _cities.FirstOrDefault(c => c.Name == CityName);
                if (m.Name != null) { Lng = m.Lng; Lat = m.Lat; }
            }
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
