using System;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace LargeFolderFinder
{
    public partial class TextViewer : Window
    {
        private string _filePath;
        public string FilePath => _filePath;

        private DateTime _lastModified;
        private long _lastSize;

        public TextViewer(string filePath)
        {
            // InitializeComponent is auto-generated from TextViewer.xaml
            InitializeComponent();
            _filePath = filePath;
            LoadFile();
            ApplyLocalization();

            // Check if this is the current active log file
            if (string.Equals(_filePath, Logger.CurrentLogFilePath, StringComparison.OrdinalIgnoreCase))
            {
                Logger.LogWritten += OnLogWritten;
                InitializeTracking();
            }
        }

        private void InitializeTracking()
        {
            if (File.Exists(_filePath))
            {
                var fi = new FileInfo(_filePath);
                _lastModified = fi.LastWriteTime;
                _lastSize = fi.Length;
            }
        }

        private void OnLogWritten()
        {
            Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    if (!File.Exists(_filePath)) return;

                    var fi = new FileInfo(_filePath);
                    if (fi.LastWriteTime != _lastModified || fi.Length != _lastSize)
                    {
                        _lastModified = fi.LastWriteTime;
                        _lastSize = fi.Length;
                        ReloadFile();
                    }
                }
                catch
                {
                    // Ignore transient errors
                }
            });
        }



        private void ReloadFile()
        {
            try
            {
                // Check if scrolled to bottom
                bool isAtBottom = ContentTextBox.VerticalOffset + ContentTextBox.ViewportHeight >= ContentTextBox.ExtentHeight - 10; // Tolerance

                ContentTextBox.Text = File.ReadAllText(_filePath);

                if (isAtBottom)
                {
                    ContentTextBox.ScrollToEnd();
                }
            }
            catch (Exception ex)
            {
                // Optionally update status or ignore
                System.Diagnostics.Debug.WriteLine($"Error reloading file: {ex.Message}");
            }
        }

        private void LoadFile()
        {
            try
            {
                if (File.Exists(_filePath))
                {
                    ContentTextBox.Text = File.ReadAllText(_filePath);
                    Title = Path.GetFileName(_filePath);
                    ContentTextBox.ScrollToEnd(); // Initial scroll to end for logs is usually preferred
                }
                else
                {
                    ContentTextBox.Text = LocalizationManager.Instance.GetText(LanguageKey.NotFoundMessage);
                }
            }
            catch (Exception ex)
            {
                ContentTextBox.Text = $"Error loading file: {ex.Message}";
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            Logger.LogWritten -= OnLogWritten;
            base.OnClosed(e);
        }

        private void ApplyLocalization()
        {
            var lm = LocalizationManager.Instance;
            CopyButton.ToolTip = lm.GetText(LanguageKey.ViewerCopy);
            OpenFolderButton.Content = lm.GetText(LanguageKey.ViewerOpenFolder);
            CloseButton.Content = lm.GetText(LanguageKey.ViewerClose);
            // Title is set to filename, or could be localized generic title
        }

        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(ContentTextBox.Text);
                Logger.Log("Copied to clipboard");
                MessageBox.Show(LocalizationManager.Instance.GetText(LanguageKey.CopyNotification), LocalizationManager.Instance.GetText(LanguageKey.DialogInfo), MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to Copy from TextViewer", ex);
                MessageBox.Show($"{LocalizationManager.Instance.GetText(LanguageKey.ClipboardError)}{ex.Message}", LocalizationManager.Instance.GetText(LanguageKey.LabelError), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (File.Exists(_filePath))
                {
                    string folderPath = Path.GetDirectoryName(_filePath) ?? "";
                    if (Directory.Exists(folderPath))
                    {
                        Process.Start(new ProcessStartInfo(folderPath) { UseShellExecute = true });
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log("Failed to open folder", ex);
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
