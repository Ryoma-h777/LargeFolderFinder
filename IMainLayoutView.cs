using System.Windows.Controls;

namespace LargeFolderFinder
{
    public interface IMainLayoutView
    {
        TextBox MinSizeTextBox { get; }
        ComboBox UnitComboBox { get; }
        Button CopyButton { get; }
        ListBox OutputListBox { get; }
        ProgressBar ScanProgressBar { get; }
        TextBlock StatusTextBlock { get; }
        TextBlock NotificationTextBlock { get; }

        // Display Settings
        CheckBox IncludeFilesCheckBox { get; }
        ComboBox SeparatorComboBox { get; }
        TextBox TabWidthTextBox { get; }
        System.Windows.FrameworkElement TabWidthArea { get; }
        TextBox FontSizeTextBox { get; }

        // Labels (for localization)
        TextBlock MinSizeLabel { get; }
        TextBlock ViewHeaderLabel { get; }
        TextBlock IncludeFilesLabel { get; }
        TextBlock SeparatorLabel { get; }
        TextBlock TabWidthLabel { get; }

        TextBox? FilterTextBox { get; }
        ComboBox? FilterModeComboBox { get; }
        TextBlock? FilterLabel { get; }
        Grid? LoadingOverlay { get; }

        // Columns for localization
        GridViewColumn? NameColumn { get; }
        GridViewColumn? SizeColumn { get; }
        GridViewColumn? DateColumn { get; }
        GridViewColumn? TypeColumn { get; }
        GridViewColumn? OwnerColumn { get; }
        ContextMenu? ItemContextMenu { get; }

        // Methods
        void ApplyLocalization(LocalizationManager lm);
        void UpdateSortVisuals(AppConstants.SortTarget target, AppConstants.SortDirection direction);

        // Actions (forwarded to MainWindow)
        void SortBy(AppConstants.SortTarget target);
        void OpenItem(FolderInfo node);
        void ToggleFolderExpansion(System.Collections.Generic.IEnumerable<FolderRowItem> items);
        void SetFolderExpansion(System.Collections.Generic.IEnumerable<FolderRowItem> items, bool isExpanded);
        void TriggerScan();
        void ShowOwner(FolderInfo node);
    }
}
