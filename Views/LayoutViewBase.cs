using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Linq;
using System.Collections.Generic;

namespace LargeFolderFinder
{
    public class LayoutViewBase : UserControl, IMainLayoutView
    {
        protected MainWindow? _mainWindow;

        public void Initialize(MainWindow mainWindow)
        {
            _mainWindow = mainWindow;

            // Common Event Attachments
            var openCmd = new CommandBinding(ApplicationCommands.Open, Open_Executed, Open_CanExecute);
            this.CommandBindings.Add(openCmd);

            var listView = OutputListBox as ListView;
            if (listView != null)
            {
                listView.AddHandler(GridViewColumnHeader.ClickEvent, new RoutedEventHandler(OnGridViewColumnHeaderClicked));
                listView.SizeChanged += OnListViewSizeChanged;
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

        // IMainLayoutView Property Implementations (using FindName for XAML elements)
        public TextBox MinSizeTextBox => (TextBox)FindName("minSizeTextBox");
        public ComboBox UnitComboBox => (ComboBox)FindName("unitComboBox");
        public Button CopyButton => (Button)FindName("copyButton");
        public ListBox OutputListBox => (ListBox)FindName("outputListBox");
        public ProgressBar ScanProgressBar => (ProgressBar)FindName("scanProgressBar");
        public TextBlock StatusTextBlock => (TextBlock)FindName("statusTextBlock");
        public TextBlock NotificationTextBlock => (TextBlock)FindName("notificationTextBlock");
        public CheckBox IncludeFilesCheckBox => (CheckBox)FindName("includeFilesCheckBox");
        public ComboBox SeparatorComboBox => (ComboBox)FindName("separatorComboBox");
        public TextBox TabWidthTextBox => (TextBox)FindName("tabWidthTextBox");
        public FrameworkElement TabWidthArea => (FrameworkElement)FindName("tabWidthArea");
        public TextBox FontSizeTextBox => (TextBox)FindName("fontSizeTextBox");

        public TextBlock MinSizeLabel => (TextBlock)FindName("minSizeLabel");
        public TextBlock ViewHeaderLabel => (TextBlock)FindName("viewHeaderLabel");
        public TextBlock IncludeFilesLabel => (TextBlock)FindName("includeFilesLabel");
        public TextBlock SeparatorLabel => (TextBlock)FindName("separatorLabel");
        public TextBlock TabWidthLabel => (TextBlock)FindName("tabWidthLabel");

        public TextBox? FilterTextBox => (TextBox?)FindName("filterTextBox");
        public ComboBox? FilterModeComboBox => (ComboBox?)FindName("filterModeComboBox");
        public TextBlock? FilterLabel => (TextBlock?)FindName("filterLabel");
        public Grid? LoadingOverlay => (Grid?)FindName("loadingOverlay");

        public GridViewColumn? NameColumnRef => (GridViewColumn?)FindName("NameColumn");
        public GridViewColumn? SizeColumnRef => (GridViewColumn?)FindName("SizeColumn");
        public GridViewColumn? DateColumnRef => (GridViewColumn?)FindName("DateColumn");
        public GridViewColumn? TypeColumnRef => (GridViewColumn?)FindName("TypeColumn");
        public GridViewColumn? OwnerColumnRef => (GridViewColumn?)FindName("OwnerColumn");

        GridViewColumn? IMainLayoutView.NameColumn => NameColumnRef;
        GridViewColumn? IMainLayoutView.SizeColumn => SizeColumnRef;
        GridViewColumn? IMainLayoutView.DateColumn => DateColumnRef;
        GridViewColumn? IMainLayoutView.TypeColumn => TypeColumnRef;
        GridViewColumn? IMainLayoutView.OwnerColumn => OwnerColumnRef;

        public ContextMenu? ItemContextMenu => (ContextMenu?)Resources["ItemContextMenu"];

        public void ApplyLocalization(LocalizationManager lm)
        {
            if (CopyButton != null) CopyButton.ToolTip = lm.GetText(LanguageKey.CopyToolTip);

            if (MinSizeLabel != null)
            {
                MinSizeLabel.Text = lm.GetText(LanguageKey.MinSizeLabel);
                MinSizeLabel.ToolTip = lm.GetText(LanguageKey.MinSizeToolTip);
            }
            if (SeparatorLabel != null) SeparatorLabel.Text = lm.GetText(LanguageKey.SeparatorLabel);
            if (SeparatorComboBox != null) SeparatorComboBox.ToolTip = lm.GetText(LanguageKey.SeparatorToolTip);

            if (TabWidthLabel != null) TabWidthLabel.Text = lm.GetText(LanguageKey.TabWidthLabel);
            if (ViewHeaderLabel != null) ViewHeaderLabel.Text = lm.GetText(LanguageKey.ViewLabel);
            if (IncludeFilesLabel != null) IncludeFilesLabel.Text = lm.GetText(LanguageKey.IncludeFiles);
            if (IncludeFilesCheckBox != null) IncludeFilesCheckBox.Content = null;

            if (FilterLabel != null) FilterLabel.Text = lm.GetText(LanguageKey.FilterLabel);

            if (FilterModeComboBox != null)
            {
                FilterModeComboBox.ToolTip = lm.GetText(LanguageKey.FilterTooltip);
                if (FilterModeComboBox.Items.Count > 0 && FilterModeComboBox.Items[0] is ComboBoxItem item1)
                    item1.Content = lm.GetText(LanguageKey.FilterMode_Normal);
                if (FilterModeComboBox.Items.Count > 1 && FilterModeComboBox.Items[1] is ComboBoxItem item2)
                    item2.Content = lm.GetText(LanguageKey.FilterMode_Regex);
            }

            if (FilterTextBox != null)
            {
                FilterTextBox.ToolTip = lm.GetText(LanguageKey.FilterTooltip);
            }

            if (NameColumnRef != null) NameColumnRef.Header = lm.GetText(LanguageKey.HeaderName);
            if (SizeColumnRef != null) SizeColumnRef.Header = lm.GetText(LanguageKey.HeaderSize);
            if (DateColumnRef != null) DateColumnRef.Header = lm.GetText(LanguageKey.HeaderDate);
            if (TypeColumnRef != null) TypeColumnRef.Header = lm.GetText(LanguageKey.HeaderType);
            if (OwnerColumnRef != null) OwnerColumnRef.Header = lm.GetText(LanguageKey.HeaderOwner);

            if (ItemContextMenu != null && ItemContextMenu.Items.Count > 0)
            {
                if (ItemContextMenu.Items[0] is MenuItem menuItemOpen)
                {
                    menuItemOpen.Header = lm.GetText(LanguageKey.ContextOpen);
                }
                if (ItemContextMenu.Items.Count > 1 && ItemContextMenu.Items[1] is MenuItem menuItemOwner)
                {
                    menuItemOwner.Header = lm.GetText(LanguageKey.ContextShowOwner);
                }
            }

            // Ensure sort visuals are maintained after localization
            if (_mainWindow != null && _mainWindow.SelectedIndex >= 0)
            {
                var session = _mainWindow.Sessions[_mainWindow.SelectedIndex];
                UpdateSortVisuals(session.SortTarget, session.SortDirection);
            }
        }

        public void UpdateSortVisuals(AppConstants.SortTarget target, AppConstants.SortDirection direction)
        {
            var listView = OutputListBox as ListView;
            var gridView = listView?.View as GridView;
            if (gridView == null) return;

            for (int i = 0; i < gridView.Columns.Count; i++)
            {
                var column = gridView.Columns[i];
                var headerContent = column.Header;
                string text = "";

                if (headerContent is string s) text = s;
                else if (headerContent is TextBlock tb) text = tb.Text;

                text = text.Replace(" ▲", "").Replace(" ▼", "");

                bool isTarget = false;
                if (column == NameColumnRef && target == AppConstants.SortTarget.Name) isTarget = true;
                else if (column == SizeColumnRef && target == AppConstants.SortTarget.Size) isTarget = true;
                else if (column == DateColumnRef && target == AppConstants.SortTarget.Date) isTarget = true;
                else if (column == TypeColumnRef && target == AppConstants.SortTarget.Type) isTarget = true;

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
                    column.Header = text;
                }
            }
        }

        protected void OnGridViewColumnHeaderClicked(object sender, RoutedEventArgs e)
        {
            if (e.OriginalSource is GridViewColumnHeader header && header.Column != null)
            {
                var target = AppConstants.SortTarget.Size;

                if (header.Column == NameColumnRef) target = AppConstants.SortTarget.Name;
                else if (header.Column == SizeColumnRef) target = AppConstants.SortTarget.Size;
                else if (header.Column == DateColumnRef) target = AppConstants.SortTarget.Date;
                else if (header.Column == TypeColumnRef) target = AppConstants.SortTarget.Type;
                else return;

                SortBy(target);
            }
        }

        protected void OnListViewSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (NameColumnRef == null || SizeColumnRef == null || DateColumnRef == null || TypeColumnRef == null || OwnerColumnRef == null) return;

            var listView = sender as ListView;
            if (listView == null) return;

            double totalWidth = listView.ActualWidth;
            double otherColumnsWidth = SizeColumnRef.ActualWidth + DateColumnRef.ActualWidth + TypeColumnRef.ActualWidth + OwnerColumnRef.ActualWidth;
            double padding = 35;

            double newWidth = totalWidth - otherColumnsWidth - padding;
            if (newWidth > 100)
            {
                NameColumnRef.Width = newWidth;
            }
        }

        protected void OnListBoxItemMouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is ListBoxItem item && item.Content is FolderRowItem rowItem)
            {
                ToggleFolderExpansion(new[] { rowItem });
                e.Handled = true;
            }
        }

        protected void OnListBoxPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (sender is ListBox listBox)
            {
                var selectedItems = listBox.SelectedItems.OfType<FolderRowItem>().ToList();
                if (selectedItems.Count == 0) return;

                if (e.Key == System.Windows.Input.Key.Enter)
                {
                    ToggleFolderExpansion(selectedItems);
                    e.Handled = true;
                }
                else if (e.Key == System.Windows.Input.Key.Right)
                {
                    SetFolderExpansion(selectedItems, true);
                    e.Handled = true;
                }
                else if (e.Key == Key.Left)
                {
                    SetFolderExpansion(selectedItems, false);
                    e.Handled = true;
                }
                else if (e.Key == Key.C && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
                {
                    try
                    {
                        if (selectedItems.Count == 1)
                        {
                            var item = selectedItems[0];
                            string text = $"{item.Node.Name} {item.SizeText.Trim()}";
                            Clipboard.SetText(text);
                            NotificationTextBlock.Text = LocalizationManager.Instance.GetText(LanguageKey.CopyNotification);
                        }
                        else
                        {
                            var sb = new System.Text.StringBuilder();
                            foreach (var item in selectedItems)
                            {
                                sb.AppendLine(item.DisplayText);
                            }
                            Clipboard.SetText(sb.ToString());
                            NotificationTextBlock.Text = LocalizationManager.Instance.GetText(LanguageKey.CopyNotification);
                        }
                        e.Handled = true;
                    }
                    catch (System.Exception ex)
                    {
                        Logger.Log("Error in Custom Copy", ex);
                    }
                }
            }
        }

        protected void OnInputKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                TriggerScan();
            }
        }

        protected void OwnerMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var selectedItems = OutputListBox.SelectedItems.OfType<FolderRowItem>().ToList();

            // If no items are selected in ListView, fall back to CommandParameter (shouldn't happen normally)
            if (!selectedItems.Any() && sender is MenuItem menuItem && menuItem.CommandParameter is FolderRowItem item)
            {
                selectedItems.Add(item);
            }

            foreach (var selectedItem in selectedItems)
            {
                ShowOwner(selectedItem.Node);
            }
        }

        // Action Implementations (delegated to MainWindow via the reference stored in Initialize)
        public void SortBy(AppConstants.SortTarget target) => _mainWindow?.SortBy(target);
        public void OpenItem(FolderInfo node) => _mainWindow?.OpenItem(node);
        public void ToggleFolderExpansion(System.Collections.Generic.IEnumerable<FolderRowItem> items) => _mainWindow?.ToggleFolderExpansion(items);
        public void SetFolderExpansion(System.Collections.Generic.IEnumerable<FolderRowItem> items, bool isExpanded) => _mainWindow?.SetFolderExpansion(items, isExpanded);
        public void TriggerScan() => _mainWindow?.ScanButton_Click(null, new RoutedEventArgs());
        public void ShowOwner(FolderInfo node) => _mainWindow?.ShowOwner(node);
    }
}
