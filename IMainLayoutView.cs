using System.Windows.Controls;

namespace LargeFolderFinder
{
    public interface IMainLayoutView
    {
        TextBox SearchSizeTextBox { get; }
        ComboBox UnitComboBox { get; }
        Button CopyButton { get; }
        TextBox OutputTextBox { get; }
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

        // Labels (for localization)
        TextBlock SearchSizeLabel { get; }
        TextBlock ViewHeaderLabel { get; }
        TextBlock SortLabel { get; }
        TextBlock IncludeFilesLabel { get; }
        TextBlock SeparatorLabel { get; }
        TextBlock TabWidthLabel { get; }

        // Methods
        void ApplyLocalization(LocalizationManager lm);
    }
}
