using System.Windows;
using System.Windows.Controls;


namespace CPUSetSetter.UI.Tabs.Processes.Benchmark
{
    /// <summary>
    /// The Benchmark tab: applies candidate CPU masks to a target process and compares the results
    /// </summary>
    public partial class BenchmarkTab : Grid
    {
        private readonly BenchmarkViewModel viewModel;

        public BenchmarkTab()
        {
            viewModel = new();
            DataContext = viewModel;
            InitializeComponent();

            Loaded += (_, _) => viewModel.RefreshTargets();
        }

        /// <summary>
        /// While typing in the target search box, keep the dropdown open showing the live-filtered matches
        /// </summary>
        private void TargetSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!targetComboBox.IsDropDownOpen && targetSearchBox.Text.Length > 0)
                targetComboBox.IsDropDownOpen = true;
        }
    }
}
