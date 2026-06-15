using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace RevitLoader.App
{
    public partial class PluginSelectionWindow : Window
    {
        public List<PluginItem> Plugins { get; set; }

        public PluginSelectionWindow(List<string> availablePlugins, List<string> selectedPlugins)
        {
            InitializeComponent();
            Plugins = availablePlugins.Select(p => new PluginItem
            {
                Name = p,
                IsSelected = selectedPlugins == null || selectedPlugins.Contains(p)
            }).ToList();
            PluginsListBox.ItemsSource = Plugins;
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        public List<string> GetSelectedPlugins()
        {
            return Plugins.Where(p => p.IsSelected).Select(p => p.Name).ToList();
        }
    }

    public class PluginItem
    {
        public string Name { get; set; } = string.Empty;
        public bool IsSelected { get; set; }
    }
}
