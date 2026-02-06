using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Linq;

namespace LargeFolderFinder
{
    public partial class VerticalLayoutView : LayoutViewBase
    {
        private VerticalLayoutView()
        {
            InitializeComponent();
        }

        public VerticalLayoutView(MainWindow mainWindow) : this()
        {
            Initialize(mainWindow);
        }

        private void CopyButton_Click(object sender, RoutedEventArgs e) => _mainWindow?.CopyButton_Click(sender, e);
        private void ListBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => _mainWindow?.OutputListBox_SelectionChanged(sender, e);
        private void SettingChanged(object sender, SelectionChangedEventArgs e) => _mainWindow?.OnSettingChanged(sender, e);
        private void SettingChanged(object sender, TextChangedEventArgs e) => _mainWindow?.OnSettingChanged(sender, e);
        private void SettingChanged(object sender, RoutedEventArgs e) => _mainWindow?.OnSettingChanged(sender, e);
        private void Filter_TextChanged(object sender, TextChangedEventArgs e) => _mainWindow?.OnFilterTextChanged(sender, e);
        private void FilterMode_SelectionChanged(object sender, SelectionChangedEventArgs e) => _mainWindow?.OnFilterModeChanged(sender, e);

        // Events that delegate to base protected handlers
        private void ListBoxItem_MouseDoubleClick(object sender, MouseButtonEventArgs e) => OnListBoxItemMouseDoubleClick(sender, e);
        private void ListBox_PreviewKeyDown(object sender, KeyEventArgs e) => OnListBoxPreviewKeyDown(sender, e);
        private void Input_KeyDown(object sender, KeyEventArgs e) => OnInputKeyDown(sender, e);
    }
}
