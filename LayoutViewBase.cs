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

        public TextBox PathTextBox => _mainWindow.PathTextBox;

        public TextBox SearchSizeTextBox => _mainWindow.SearchSizeTextBox;

        public ComboBox UnitComboBox => _mainWindow.UnitComboBox;

        public Button RunButton => _mainWindow.RunButton;

        public Button CancelButton => _mainWindow.CancelButton;

        public Button ConfigButton => _mainWindow.ConfigButton;
        public Button CopyButton => _mainWindow.CopyButton;

        public TextBox OutputTextBox => _mainWindow.OutputTextBox;

        public ProgressBar ScanProgressBar => _mainWindow.ScanProgressBar;
        public TextBlock StatusTextBlock => _mainWindow.StatusTextBlock;
        public TextBlock NotificationTextBlock => _mainWindow.NotificationTextBlock;

        public ComboBox SortComboBox => _mainWindow.SortComboBox;
        public ComboBox SortDirectionComboBox => _mainWindow.SortDirectionComboBox;

        public CheckBox IncludeFilesCheckBox => _mainWindow.IncludeFilesCheckBox;

        public ComboBox SeparatorComboBox => _mainWindow.SeparatorComboBox;
        public TextBox TabWidthTextBox => _mainWindow.TabWidthTextBox;
        public FrameworkElement TabWidthArea => _mainWindow.TabWidthArea;

        public TextBlock SearchSizeLabel => _mainWindow.SearchSizeLabel;
        public TextBlock ViewHeaderLabel => _mainWindow.ViewHeaderLabel;
        public TextBlock SortLabel => _mainWindow.SortLabel;
        public TextBlock IncludeFilesLabel => _mainWindow.IncludeFilesLabel;
        public TextBlock SeparatorLabel => _mainWindow.SeparatorLabel;
        public TextBlock TabWidthLabel => _mainWindow.TabWidthLabel;

        public void ApplyLocalization(LocalizationManager lm)
        {
            if (_mainWindow == null) return;

            CancelButton.ToolTip = lm.GetText(LanguageKey.CancelToolTip);
            ConfigButton.ToolTip = lm.GetText(LanguageKey.OpenConfigToolTip);
            CopyButton.ToolTip = lm.GetText(LanguageKey.CopyToolTip);

            SearchSizeLabel.Text = lm.GetText(LanguageKey.SearchSizeLabel).Replace(" ({0})", "");
            SortLabel.Text = lm.GetText(LanguageKey.SortLabel);
            SeparatorLabel.Text = lm.GetText(LanguageKey.SeparatorLabel);
            SeparatorComboBox.ToolTip = lm.GetText(LanguageKey.SeparatorToolTip);

            TabWidthLabel.Text = lm.GetText(LanguageKey.TabWidthLabel);
            ViewHeaderLabel.Text = lm.GetText(LanguageKey.ViewLabel);
            IncludeFilesLabel.Text = lm.GetText(LanguageKey.IncludeFiles); // Checkbox の文字列 (Checkboxの左に文字を出したいときは機能がないみたいで分かれる)
            IncludeFilesCheckBox.Content = null;

            // Sort ComboBox Items
            int sortIdx = SortComboBox.SelectedIndex;
            SortComboBox.Items.Clear();
            SortComboBox.Items.Add(lm.GetText(LanguageKey.TargetSize));
            SortComboBox.Items.Add(lm.GetText(LanguageKey.TargetName));
            SortComboBox.Items.Add(lm.GetText(LanguageKey.TargetDate));
            if (sortIdx >= 0 && sortIdx < SortComboBox.Items.Count) SortComboBox.SelectedIndex = sortIdx;

            // Sort Direction ComboBox Items
            int dirIdx = SortDirectionComboBox.SelectedIndex;
            SortDirectionComboBox.Items.Clear();
            SortDirectionComboBox.Items.Add(lm.GetText(LanguageKey.DirectionAsc));
            SortDirectionComboBox.Items.Add(lm.GetText(LanguageKey.DirectionDesc));
            if (dirIdx >= 0 && dirIdx < SortDirectionComboBox.Items.Count) SortDirectionComboBox.SelectedIndex = dirIdx; // Checkbox自体なので文字は不要
        }
    }
}
