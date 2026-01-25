using System.Windows;
using System.Windows.Controls;

namespace LargeFolderFinder
{
    public class LayoutViewCommonLogic : IMainLayoutView
    {
        protected IMainLayoutView _mainWindow;

        public LayoutViewCommonLogic(IMainLayoutView view)
        {
            _mainWindow = view;
        }

        public TextBox MinSizeTextBox => _mainWindow.MinSizeTextBox;

        public ComboBox UnitComboBox => _mainWindow.UnitComboBox;

        public Button CopyButton => _mainWindow.CopyButton;

        public ListBox OutputListBox => _mainWindow.OutputListBox;

        public ProgressBar ScanProgressBar => _mainWindow.ScanProgressBar;
        public TextBlock StatusTextBlock => _mainWindow.StatusTextBlock;
        public TextBlock NotificationTextBlock => _mainWindow.NotificationTextBlock;

        public CheckBox IncludeFilesCheckBox => _mainWindow.IncludeFilesCheckBox;

        public ComboBox SeparatorComboBox => _mainWindow.SeparatorComboBox;
        public TextBox TabWidthTextBox => _mainWindow.TabWidthTextBox;
        public FrameworkElement TabWidthArea => _mainWindow.TabWidthArea;
        public TextBox FontSizeTextBox => _mainWindow.FontSizeTextBox;

        public TextBlock MinSizeLabel => _mainWindow.MinSizeLabel;
        public TextBlock ViewHeaderLabel => _mainWindow.ViewHeaderLabel;
        public TextBlock IncludeFilesLabel => _mainWindow.IncludeFilesLabel;
        public TextBlock SeparatorLabel => _mainWindow.SeparatorLabel;
        public TextBlock TabWidthLabel => _mainWindow.TabWidthLabel;

        public void ApplyLocalization(LocalizationManager lm)
        {
            if (_mainWindow == null) return;

            CopyButton.ToolTip = lm.GetText(LanguageKey.CopyToolTip);

            MinSizeLabel.Text = lm.GetText(LanguageKey.MinSizeLabel);
            MinSizeLabel.ToolTip = lm.GetText(LanguageKey.MinSizeToolTip);
            MinSizeLabel.Text = lm.GetText(LanguageKey.MinSizeLabel);
            MinSizeLabel.ToolTip = lm.GetText(LanguageKey.MinSizeToolTip);
            SeparatorLabel.Text = lm.GetText(LanguageKey.SeparatorLabel);
            SeparatorComboBox.ToolTip = lm.GetText(LanguageKey.SeparatorToolTip);

            TabWidthLabel.Text = lm.GetText(LanguageKey.TabWidthLabel);
            ViewHeaderLabel.Text = lm.GetText(LanguageKey.ViewLabel);
            IncludeFilesLabel.Text = lm.GetText(LanguageKey.IncludeFiles);
            IncludeFilesCheckBox.Content = null;
        }

        public void UpdateSortVisuals(AppConstants.SortTarget target, AppConstants.SortDirection direction)
        {
            _mainWindow.UpdateSortVisuals(target, direction);
        }
    }
}
