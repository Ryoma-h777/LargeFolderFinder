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

        // Methods
        void ApplyLocalization(LocalizationManager lm);
        void UpdateSortVisuals(AppConstants.SortTarget target, AppConstants.SortDirection direction);
    }
}
