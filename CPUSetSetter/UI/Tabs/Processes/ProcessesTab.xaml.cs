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

            Loaded += (_, _) => logBox.ScrollToEnd();
        }

        private void Log_TextChanged(object sender, TextChangedEventArgs e)
        {
            logBox.ScrollToEnd();
        }

        private void Benchmark_Click(object sender, RoutedEventArgs e)
        {
            if (App.Current.MainWindow is MainWindow mainWindow)
                mainWindow.SelectBenchmarkTab();
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
