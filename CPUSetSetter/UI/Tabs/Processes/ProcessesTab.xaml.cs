using CPUSetSetter.Config.Models;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;


namespace CPUSetSetter.UI.Tabs.Processes
{
    public partial class ProcessesTab : Grid
    {
        private readonly ProcessesTabViewModel viewModel;

        private DataGridRow? _clickedRow;
        private bool _rowWasSelectedOnMouseDown;
        private bool _rowWasVisibleOnMouseDown;

        public ProcessesTab()
        {
            viewModel = new(Dispatcher);
            DataContext = viewModel;
            InitializeComponent();

            Loaded += (_, _) =>
            {
                logBox.ScrollToEnd();
                PreviewKeyDown += (_, e) => HandlePreviewKeyDown(e);
            };
        }

        private void HandlePreviewKeyDown(System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.K && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                searchBox.Focus();
                e.Handled = true;
            }
        }

        private void Log_TextChanged(object sender, TextChangedEventArgs e)
        {
            logBox.ScrollToEnd();
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            searchWatermark.Visibility = string.IsNullOrEmpty(searchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Benchmark_Click(object sender, RoutedEventArgs e)
        {
            if (App.Current.MainWindow is MainWindow mainWindow)
                mainWindow.SelectBenchmarkTab();
        }

        /// <summary>
        /// Re-scan the process list: remove exited processes and re-add anything that was missed
        /// </summary>
        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            viewModel.RefreshProcessList();
        }

        /// <summary>
        /// Toggle the manual pause of the live-sorted process list
        /// </summary>
        private void Pause_Click(object sender, RoutedEventArgs e)
        {
            viewModel.ToggleManualPause();
            bool paused = viewModel.IsManuallyPaused;

            pauseLabel.Text = paused ? "Resume" : "Pause";
            pauseIcon.Data = paused
                ? Geometry.Parse("M 2 2 L 11 6.5 L 2 11 Z") // play triangle
                : Geometry.Parse("M 2 2 H 5 V 11 H 2 Z M 8 2 H 11 V 11 H 8 Z"); // pause bars
        }

        /// <summary>
        /// Clicking a process row toggles its per-core details: click to show, click again to hide
        /// </summary>
        private void ProcessesGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (IsInteractive((DependencyObject?)e.OriginalSource))
                return;

            DataGridRow? row = FindAncestor<DataGridRow>((DependencyObject?)e.OriginalSource);
            _clickedRow = row;
            _rowWasSelectedOnMouseDown = row?.IsSelected ?? false;
            _rowWasVisibleOnMouseDown = row?.DetailsVisibility == Visibility.Visible;
        }

        private void ProcessesGrid_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (IsInteractive((DependencyObject?)e.OriginalSource))
                return;
            if (FindAncestor<DataGridRow>((DependencyObject?)e.OriginalSource) is not { } row || row != _clickedRow)
                return;

            // The row was already selected and showing details when the mouse went down, so this click hides them
            if (_rowWasSelectedOnMouseDown && _rowWasVisibleOnMouseDown)
                viewModel.SelectedProcess = null;
        }

        private static bool IsInteractive(DependencyObject? source)
        {
            if (source is null)
                return false;
            return FindAncestor<ComboBox>(source) is not null
                || FindAncestor<Button>(source) is not null
                || FindAncestor<TextBox>(source) is not null
                || FindAncestor<DataGridDetailsPresenter>(source) is not null;
        }

        /// <summary>
        /// Collapse the row details by clearing the selection
        /// </summary>
        private void HideDetails_Click(object sender, RoutedEventArgs e)
        {
            viewModel.SelectedProcess = null;
        }

        private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
        {
            while (current is not null)
            {
                if (current is T match)
                    return match;
                current = VisualTreeHelper.GetParent(current);
            }
            return null;
        }
    }
}
