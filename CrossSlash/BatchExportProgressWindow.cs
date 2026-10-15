using System.Windows;
using System.Windows.Controls;

namespace CrossSlash {
    internal sealed record BatchProgressUpdate(int Completed, int Total, string Item);

    internal sealed class BatchExportProgressWindow : Window {
        private readonly TextBlock _item;
        private readonly TextBlock _count;
        private readonly ProgressBar _progress;
        private readonly Button _cancelButton;
        private bool _finished;
        private bool _cancellationRequested;

        public BatchExportProgressWindow(Window owner, string title, int total, Action requestCancellation) {
            Owner = owner;
            Title = title;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            _item = new TextBlock {
                Text = "Preparing export…",
                Foreground = DesktopUi.TextPrimaryBrush,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 0, 0, 12),
            };
            _progress = new ProgressBar {
                Minimum = 0,
                Maximum = Math.Max(total, 1),
                Value = 0,
                Height = 16,
                Background = DesktopUi.InputBackgroundBrush,
                Foreground = DesktopUi.AccentBrush,
                BorderBrush = DesktopUi.ControlBorderBrush,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 0, 8),
            };
            _count = new TextBlock {
                Text = $"0 of {total:N0} processed",
                Foreground = DesktopUi.TextSecondaryBrush,
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            _cancelButton = DesktopUi.CreateButton("Cancel");
            _cancelButton.Width = 128;
            _cancelButton.Height = 36;
            _cancelButton.Padding = new Thickness(12, 4, 12, 4);
            _cancelButton.HorizontalAlignment = HorizontalAlignment.Right;
            _cancelButton.Margin = new Thickness(0, 12, 0, 0);
            _cancelButton.Click += (_, _) => {
                if (_cancellationRequested)
                    return;
                _cancellationRequested = true;
                _cancelButton.Content = "Cancelling…";
                _cancelButton.IsEnabled = false;
                _item.Text = "Finishing the current item before cancelling…";
                requestCancellation();
            };

            var content = new StackPanel {
                Margin = new Thickness(18),
                Children = { _item, _progress, _count, _cancelButton },
            };
            DesktopUi.ConfigureWindow(this, content, width: 460, height: 200, scrollContent: false);
            MinWidth = MaxWidth = Width;
            MinHeight = MaxHeight = Height;
            Closing += (_, e) => {
                if (!_finished)
                    e.Cancel = true;
            };
        }

        public void UpdateProgress(BatchProgressUpdate update) {
            if (!IsVisible)
                return;
            if (!_cancellationRequested)
                _item.Text = update.Item;
            _progress.Maximum = Math.Max(update.Total, 1);
            _progress.Value = Math.Clamp(update.Completed, 0, _progress.Maximum);
            _count.Text = $"{update.Completed:N0} of {update.Total:N0} processed";
        }

        public void CloseAfterExport() {
            _finished = true;
            Close();
        }
    }

    internal static class BatchExportRunner {
        public static async Task<T> RunAsync<T>(
            Window owner,
            string title,
            int total,
            Func<IProgress<BatchProgressUpdate>, CancellationToken, T> export) {
            // Export views are hosted as content inside SplashWindow and are never shown
            // themselves. Own the progress dialog with the actual, visible app window.
            Window hostWindow = DesktopUi.ResolveOwner(owner);
            using var cancellation = new CancellationTokenSource();
            var window = new BatchExportProgressWindow(hostWindow, title, total, cancellation.Cancel);
            var progress = new Progress<BatchProgressUpdate>(window.UpdateProgress);
            bool wasEnabled = hostWindow.IsEnabled;
            hostWindow.IsEnabled = false;
            try {
                window.Show();
                return await Task.Run(() => export(progress, cancellation.Token));
            } finally {
                if (window.IsVisible)
                    window.CloseAfterExport();
                hostWindow.IsEnabled = wasEnabled;
            }
        }
    }
}
