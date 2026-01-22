using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

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
        public TextBox PathTextBox => pathTextBox;
        public TextBox SearchSizeTextBox => searchSizeTextBox;
        public ComboBox UnitComboBox => unitComboBox;
        public Button RunButton => runButton;
        public Button CancelButton => cancelButton;
        public Button ConfigButton => configButton;
        public Button CopyButton => copyButton;
        public TextBox OutputTextBox => outputTextBox;
        public ProgressBar ScanProgressBar => scanProgressBar;
        public TextBlock StatusTextBlock => statusTextBlock;
        public TextBlock NotificationTextBlock => notificationTextBlock;
        public ComboBox SortComboBox => sortComboBox;
        public ComboBox SortDirectionComboBox => sortDirectionComboBox;
        public CheckBox IncludeFilesCheckBox => includeFilesCheckBox;
        public ComboBox SeparatorComboBox => separatorComboBox;
        public TextBox TabWidthTextBox => tabWidthTextBox;
        public FrameworkElement TabWidthArea => tabWidthArea;
        public TextBlock SearchSizeLabel => searchSizeLabel;
        public TextBlock ViewHeaderLabel => viewHeaderLabel;
        public TextBlock SortLabel => sortLabel;
        public TextBlock IncludeFilesLabel => includeFilesLabel;
        public TextBlock SeparatorLabel => separatorLabel;
        public TextBlock TabWidthLabel => tabWidthLabel;

        public void ApplyLocalization(LocalizationManager lm)
        {
            _commonLogic?.ApplyLocalization(lm);
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e) => _mainWindow?.BrowseButton_Click(sender, e);
        private void ScanButton_Click(object sender, RoutedEventArgs e) => _mainWindow?.ScanButton_Click(sender, e);
        private void CancelButton_Click(object sender, RoutedEventArgs e) => _mainWindow?.CancelButton_Click(sender, e);
        private void MenuConfig_Click(object sender, RoutedEventArgs e) => _mainWindow?.MenuConfig_Click(sender, e);
        private void CopyButton_Click(object sender, RoutedEventArgs e) => _mainWindow?.CopyButton_Click(sender, e);
        /// <summary>
        /// キー押したときの動作
        /// Xamlにレイアウトが紐づくので共通化できていません。
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>

        private void Input_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                ScanButton_Click(sender, e);
            }
            // Undoに取られて動作しない
            // else if (e.Key == Key.Z && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            // {
            //     CancelButton_Click(sender, e);
            // }
        }
    }
}
