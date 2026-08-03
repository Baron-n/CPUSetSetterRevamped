using CPUSetSetter.Platforms;
using CPUSetSetter.UI.Tabs.Processes;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;


namespace CPUSetSetter.UI
{
    public partial class MainWindow : Window
    {
        private bool _listIsPaused = false;

        public string StatusText => $"Ready · {CpuInfo.LogicalProcessorCount} Cores Detected";

        public MainWindow()
        {
            InitializeComponent();

            // Correct the WindowChrome maximized-window overshoot: when maximized, WPF sizes the window
            // larger than the monitor's work area by the resize border, so its content extends past the
            // left/top edge. Measure the actual overshoot after the window is placed and inset the content
            StateChanged += (_, _) => CorrectMaximizedOvershoot();

            // Listen for the Ctrl key, so the processes list's live sorting can be paused
            PreviewKeyDown += (_, e) => KeyPressed(e);
            PreviewKeyUp += (_, e) => KeyReleased(e);

            Deactivated += (_, _) => ResumeListUpdates();
        }

        private void CorrectMaximizedOvershoot()
        {
            if (WindowState != WindowState.Maximized)
            {
                RootLayout.Margin = new Thickness(0);
                return;
            }

            // Measure after the window has been sized/positioned, so this works no matter how the
            // window was maximized (title-bar double click, Win+Up, dragging to the screen edge, our button...)
            Dispatcher.BeginInvoke(() =>
            {
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                if (!GetWindowRect(hwnd, out RECT wndRect))
                    return;

                MONITORINFO monitorInfo = new() { cbSize = Marshal.SizeOf<MONITORINFO>() };
                if (!GetMonitorInfo(MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST), ref monitorInfo))
                    return;

                RECT work = monitorInfo.rcWork;
                int left = Math.Max(0, work.Left - wndRect.Left);
                int top = Math.Max(0, work.Top - wndRect.Top);
                int right = Math.Max(0, wndRect.Right - work.Right);
                int bottom = Math.Max(0, wndRect.Bottom - work.Bottom);

                if (left == 0 && top == 0 && right == 0 && bottom == 0)
                {
                    RootLayout.Margin = new Thickness(0);
                    return;
                }

                double dpiScale = VisualTreeHelper.GetDpi(this).PixelsPerDip;
                RootLayout.Margin = new Thickness(left / dpiScale, top / dpiScale, right / dpiScale, bottom / dpiScale);
            }, DispatcherPriority.Background);
        }

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hwnd, out RECT lpRect);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll")]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

        protected override void OnClosing(CancelEventArgs e)
        {
            e.Cancel = true;
            Hide();
            base.OnClosing(e);
        }

        private void KeyPressed(System.Windows.Input.KeyEventArgs e)
        {
            if ((e.Key == Key.LeftCtrl || e.Key == Key.RightCtrl) && !_listIsPaused)
            {
                _listIsPaused = true;
                ProcessesTabViewModel.Instance?.PauseListUpdates();
            }
        }

        private void KeyReleased(System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.LeftCtrl || e.Key == Key.RightCtrl)
            {
                ResumeListUpdates();
            }
        }

        private void ResumeListUpdates()
        {
            if (_listIsPaused)
            {
                _listIsPaused = false;
                ProcessesTabViewModel.Instance?.ResumeListUpdates();
            }
        }

        /// <summary>
        /// Switch to the Benchmark tab, refreshing its target list so it is never stale
        /// </summary>
        public void SelectBenchmarkTab()
        {
            BenchmarkTabItem.IsSelected = true;
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
