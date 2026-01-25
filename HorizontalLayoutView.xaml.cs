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
            var openCmd = new CommandBinding(ApplicationCommands.Open, Open_Executed, Open_CanExecute);
            this.CommandBindings.Add(openCmd);

            var listView = (ListView)FindName("outputListBox");
            if (listView != null)
            {
                listView.AddHandler(GridViewColumnHeader.ClickEvent, new RoutedEventHandler(GridViewColumnHeaderClicked));
            }
        }

        private void Open_CanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = e.Parameter is FolderRowItem;
            e.Handled = true;
        }

        private void Open_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            if (e.Parameter is FolderRowItem item)
            {
                Logger.Log($"Open command executed for: {item.Node.Name}");
                _mainWindow?.OpenItem(item.Node);
            }
        }

        private void GridViewColumnHeaderClicked(object sender, RoutedEventArgs e)
        {
            if (e.OriginalSource is GridViewColumnHeader header && header.Column != null)
            {
                var gridView = (sender as ListView)?.View as GridView;
                if (gridView == null) return;

                // Compare references instead of indices
                AppConstants.SortTarget target = AppConstants.SortTarget.Size;

                if (header.Column == NameColumn) target = AppConstants.SortTarget.Name;
                else if (header.Column == SizeColumn) target = AppConstants.SortTarget.Size;
                else if (header.Column == DateColumn) target = AppConstants.SortTarget.Date;
                else if (header.Column == TypeColumn) target = AppConstants.SortTarget.Type;
                else return;

                _mainWindow?.SortBy(target);
            }
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
        public CheckBox IncludeFilesCheckBox => includeFilesCheckBox;
        public ComboBox SeparatorComboBox => separatorComboBox;
        public TextBox TabWidthTextBox => tabWidthTextBox;
        public FrameworkElement TabWidthArea => tabWidthArea;
        public TextBox FontSizeTextBox => fontSizeTextBox;
        public TextBlock MinSizeLabel => minSizeLabel;
        public TextBlock ViewHeaderLabel => viewHeaderLabel;
        public TextBlock IncludeFilesLabel => includeFilesLabel;
        public TextBlock SeparatorLabel => separatorLabel;
        public TextBlock TabWidthLabel => tabWidthLabel;

        public void ApplyLocalization(LocalizationManager lm)
        {
            _commonLogic?.ApplyLocalization(lm);

            var listView = (ListView)FindName("outputListBox");
            if (listView != null && listView.View is GridView gridView)
            {
                // Note: UpdateSortVisuals in MainWindow/RenderResult will re-apply arrows later.
                // Just set base strings here.
                if (NameColumn != null) NameColumn.Header = lm.GetText(LanguageKey.HeaderName);
                if (SizeColumn != null) SizeColumn.Header = lm.GetText(LanguageKey.HeaderSize);
                if (DateColumn != null) DateColumn.Header = lm.GetText(LanguageKey.HeaderDate);
                if (TypeColumn != null) TypeColumn.Header = lm.GetText(LanguageKey.HeaderType);
                if (OwnerColumn != null) OwnerColumn.Header = lm.GetText(LanguageKey.HeaderOwner);
            }

            if (this.Resources["ItemContextMenu"] is ContextMenu ctxMenu && ctxMenu.Items.Count > 0)
            {
                if (ctxMenu.Items[0] is MenuItem menuItemOpen)
                {
                    menuItemOpen.Header = lm.GetText(LanguageKey.ContextOpen);
                }
                if (ctxMenu.Items.Count > 1 && ctxMenu.Items[1] is MenuItem menuItemOwner)
                {
                    menuItemOwner.Header = lm.GetText(LanguageKey.ContextShowOwner);
                }
            }
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
        public void UpdateSortVisuals(AppConstants.SortTarget target, AppConstants.SortDirection direction)
        {
            var listView = (ListView)FindName("outputListBox");
            var gridView = listView?.View as GridView;
            if (gridView == null) return;

            // Mapping: 0=Name, 1=Size, 2=Date, 3=Type
            for (int i = 0; i < gridView.Columns.Count; i++)
            {
                var column = gridView.Columns[i];
                var headerContent = column.Header;
                string text = "";

                if (headerContent is string s) text = s;
                else if (headerContent is TextBlock tb) text = tb.Text;

                // Strip arrows
                text = text.Replace(" ▲", "").Replace(" ▼", "");

                bool isTarget = false;
                if (column == NameColumn && target == AppConstants.SortTarget.Name) isTarget = true;
                else if (column == SizeColumn && target == AppConstants.SortTarget.Size) isTarget = true;
                else if (column == DateColumn && target == AppConstants.SortTarget.Date) isTarget = true;
                else if (column == TypeColumn && target == AppConstants.SortTarget.Type) isTarget = true;

                if (isTarget)
                {
                    string arrow = direction == AppConstants.SortDirection.Ascending ? " ▲" : " ▼";
                    column.Header = new TextBlock
                    {
                        Text = text + arrow,
                        Foreground = System.Windows.Media.Brushes.Blue
                    };
                }
                else
                {
                    column.Header = text; // Reset to plain string
                }
            }
        }

        private void ListView_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (NameColumn == null || SizeColumn == null || DateColumn == null || TypeColumn == null || OwnerColumn == null) return;

            var listView = sender as ListView;
            if (listView == null) return;

            // Calculate available width
            double totalWidth = listView.ActualWidth;
            double otherColumnsWidth = SizeColumn.ActualWidth + DateColumn.ActualWidth + TypeColumn.ActualWidth + OwnerColumn.ActualWidth;
            double padding = 35; // ScrollBar + Margin safety

            double newWidth = totalWidth - otherColumnsWidth - padding;
            if (newWidth > 100)
            {
                NameColumn.Width = newWidth;
            }
        }
    }
}
