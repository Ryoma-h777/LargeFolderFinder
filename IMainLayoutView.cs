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
        ComboBox SortComboBox { get; }
        ComboBox SortDirectionComboBox { get; }
        CheckBox IncludeFilesCheckBox { get; }
        ComboBox SeparatorComboBox { get; }
        TextBox TabWidthTextBox { get; }
        System.Windows.FrameworkElement TabWidthArea { get; }
        TextBox FontSizeTextBox { get; }

        // Labels (for localization)
        TextBlock MinSizeLabel { get; }
        TextBlock ViewHeaderLabel { get; }
        TextBlock SortLabel { get; }
        TextBlock IncludeFilesLabel { get; }
        TextBlock SeparatorLabel { get; }
        TextBlock TabWidthLabel { get; }

        // Methods
        void ApplyLocalization(LocalizationManager lm);
    }
}
