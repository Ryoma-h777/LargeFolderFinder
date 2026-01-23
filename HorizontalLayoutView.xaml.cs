using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Linq;

namespace LargeFolderFinder
{
    /// <summary>
    /// 横並びレイアウトのビュークラス
    /// </summary>
    public partial class HorizontalLayoutView : UserControl, IMainLayoutView
    {
        private readonly MainWindow? _mainWindow;
        private readonly LayoutViewCommonLogic? _commonLogic;


        private HorizontalLayoutView()
        {
            InitializeComponent();
        }

        public HorizontalLayoutView(MainWindow mainWindow) : this()
        {
            _mainWindow = mainWindow;
            _commonLogic = new LayoutViewCommonLogic(this);
        }

        // IMainLayoutView Implementation
        // IMainLayoutView Implementation
        public TextBox MinSizeTextBox => minSizeTextBox;
        public ComboBox UnitComboBox => unitComboBox;
        public Button CopyButton => copyButton;
        public ListBox OutputListBox => (ListBox)FindName("outputListBox");
        public ProgressBar ScanProgressBar => scanProgressBar;
        public TextBlock StatusTextBlock => statusTextBlock;
        public TextBlock NotificationTextBlock => notificationTextBlock;
        public ComboBox SortComboBox => sortComboBox;
        public ComboBox SortDirectionComboBox => sortDirectionComboBox;
        public CheckBox IncludeFilesCheckBox => includeFilesCheckBox;
        public ComboBox SeparatorComboBox => separatorComboBox;
        public TextBox TabWidthTextBox => tabWidthTextBox;
        public FrameworkElement TabWidthArea => tabWidthArea;
        public TextBox FontSizeTextBox => fontSizeTextBox;
        public TextBlock MinSizeLabel => minSizeLabel;
        public TextBlock ViewHeaderLabel => viewHeaderLabel;
        public TextBlock SortLabel => sortLabel;
        public TextBlock IncludeFilesLabel => includeFilesLabel;
        public TextBlock SeparatorLabel => separatorLabel;
        public TextBlock TabWidthLabel => tabWidthLabel;

        public void ApplyLocalization(LocalizationManager lm)
        {
            _commonLogic?.ApplyLocalization(lm);
        }

        private void CopyButton_Click(object sender, RoutedEventArgs e) => _mainWindow?.CopyButton_Click(sender, e);

        private void TriggerScan(object sender, RoutedEventArgs e) => _mainWindow?.ScanButton_Click(sender, e);
        /// <summary>
        /// キー押したときの動作
        /// Xamlにレイアウトが紐づくので共通化できていません。
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>

        private void ListBoxItem_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is ListBoxItem item && item.Content is FolderRowItem rowItem)
            {
                // Toggle single item
                _mainWindow?.ToggleFolderExpansion(new[] { rowItem });
                e.Handled = true;
            }
        }

        private void ListBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (sender is ListBox listBox)
            {
                var selectedItems = listBox.SelectedItems.OfType<FolderRowItem>().ToList();
                if (selectedItems.Count == 0) return;

                if (e.Key == System.Windows.Input.Key.Enter)
                {
                    _mainWindow?.ToggleFolderExpansion(selectedItems);
                    e.Handled = true;
                }
                else if (e.Key == System.Windows.Input.Key.Right)
                {
                    _mainWindow?.SetFolderExpansion(selectedItems, true);
                    e.Handled = true;
                }
                else if (e.Key == System.Windows.Input.Key.Left)
                {
                    // If all selected items are already collapsed, maybe we should select parent? 
                    // Current requirement: Left -> Collapse.
                    _mainWindow?.SetFolderExpansion(selectedItems, false);
                    e.Handled = true;
                }
                else if (e.Key == System.Windows.Input.Key.C && (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) == System.Windows.Input.ModifierKeys.Control)
                {
                    // Custom Copy
                    try
                    {
                        if (selectedItems.Count == 1)
                        {
                            var item = selectedItems[0];
                            string text = $"{item.Node.Name} {item.SizeText.Trim()}";
                            Clipboard.SetText(text);
                            this.NotificationTextBlock.Text = LocalizationManager.Instance.GetText(LanguageKey.CopyNotification); // or simpler message
                        }
                        else
                        {
                            var sb = new System.Text.StringBuilder();
                            foreach (var item in selectedItems)
                            {
                                sb.AppendLine(item.DisplayText);
                            }
                            Clipboard.SetText(sb.ToString());
                            this.NotificationTextBlock.Text = LocalizationManager.Instance.GetText(LanguageKey.CopyNotification); // or simpler message
                        }
                        e.Handled = true;
                    }
                    catch (Exception ex)
                    {
                        Logger.Log("Error in Custom Copy", ex);
                    }
                }
            }
        }

        private void Input_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                TriggerScan(sender, e);
            }
        }
    }
}
