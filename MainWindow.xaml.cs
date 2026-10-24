using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;

namespace MultiFrameViewer;

public partial class MainWindow : Window
{
    private SlideshowPanel? _fullscreenPanel;
    private DispatcherTimer? _chromeHideTimer;
    private DispatcherTimer? _cursorHideTimer;
    private bool _autoHideCursor = true;
    private bool _isCursorHidden;

    public MainWindow()
    {
        InitializeComponent();
        Panel1.SetPanelNumber(1);
        Panel2.SetPanelNumber(2);
        Panel3.SetPanelNumber(3);
        Panel1.OpenFolderPicker += (_, _) => PickFolderForPanel(Panel1);
        Panel2.OpenFolderPicker += (_, _) => PickFolderForPanel(Panel2);
        Panel3.OpenFolderPicker += (_, _) => PickFolderForPanel(Panel3);
        HookPanel(Panel1);
        HookPanel(Panel2);
        HookPanel(Panel3);
        ContentGrid.MouseMove += OnContentMouseMove;
        ContentGrid.MouseLeave += OnContentMouseLeave;
        RootGrid.MouseMove += OnRootMouseMove;
        MouseMove += OnWindowMouseMove;
        Deactivated += (_, _) => RestoreCursor();
        ContextMenuOpening += (_, _) => RestoreCursor();
        _chromeHideTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2.5)
        };
        _chromeHideTimer.Tick += (_, _) =>
        {
            _chromeHideTimer.Stop();
            if (WindowState == WindowState.Maximized)
            {
                HideChrome();
            }
        };
        _cursorHideTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _cursorHideTimer.Tick += (_, _) =>
        {
            _cursorHideTimer.Stop();
            if (_autoHideCursor &&
                IsActive &&
                !IsChromeVisible &&
                IsMouseOver &&
                ContextMenu?.IsOpen != true)
            {
                Mouse.OverrideCursor = Cursors.None;
                _isCursorHidden = true;
            }
        };
        Loaded += OnLoaded;
        StateChanged += (_, _) =>
        {
            if (WindowState == WindowState.Normal)
            {
                UpdateLayoutButtons();
            }
        };
    }

    private void HookPanel(SlideshowPanel panel)
    {
        panel.RequestFullscreen += EnterPanelFullscreen;
        panel.RequestRestoreLayout += RestoreLayout;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        RestoreLayout();
        WindowState = WindowState.Normal;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        HideChrome();
        WindowState = WindowState.Maximized;
        var args = Environment.GetCommandLineArgs();
        var intervalArg = args.FirstOrDefault(arg => arg.StartsWith("--interval=", StringComparison.OrdinalIgnoreCase));
        if (intervalArg is not null &&
            int.TryParse(intervalArg["--interval=".Length..], out var intervalSeconds))
        {
            foreach (var panel in new[] { Panel1, Panel2, Panel3 })
            {
                panel.SetIntervalSeconds(intervalSeconds);
            }
        }
        var folders = args.Skip(1)
            .Where(arg => !arg.StartsWith("--", StringComparison.Ordinal))
            .Where(Directory.Exists)
            .Take(3)
            .ToArray();

        if (folders.Length == 1)
        {
            Dispatcher.BeginInvoke(() =>
            {
                Panel1.OpenFolder(folders[0]);
                Panel2.OpenFolder(folders[0]);
                Panel3.OpenFolder(folders[0]);
            }, DispatcherPriority.Background);
        }
        else if (folders.Length > 1)
        {
            var panels = new[] { Panel1, Panel2, Panel3 };
            for (var i = 0; i < folders.Length; i++)
            {
                panels[i].OpenFolder(folders[i]);
            }
        }
        else
        {
            Dispatcher.BeginInvoke(() =>
            {
                Panel1.Focus();
                MessageBox.Show(
                    "点击任意一块空白画面，选择要播放的图片文件夹。\n\n" +
                    "三块画面可以分别选择不同文件夹，各自独立播放。",
                    "欢迎使用三境",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }, DispatcherPriority.Background);
        }
    }

    private void PickFolderForPanel(SlideshowPanel panel)
    {
        var folder = FolderPicker.Pick(new WindowInteropHelper(this).Handle);
        if (!string.IsNullOrWhiteSpace(folder))
        {
            panel.OpenFolder(folder);
        }
    }

    private async void OnChooseAllClick(object sender, RoutedEventArgs e)
    {
        var panels = new[] { Panel1, Panel2, Panel3 };
        for (var i = 0; i < panels.Length; i++)
        {
            if (i < 3)
            {
                panels[i].OpenFolder();
            }
            await Task.Delay(60);
        }
    }

    private void OnChooseSharedFolderClick(object sender, RoutedEventArgs e)
    {
        var folder = FolderPicker.Pick(new WindowInteropHelper(this).Handle);
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }
        Panel1.OpenFolder(folder);
        Panel2.OpenFolder(folder);
        Panel3.OpenFolder(folder);
    }

    private void OnIntervalChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }
        var seconds = ReadInterval();
        foreach (var panel in new[] { Panel1, Panel2, Panel3 })
        {
            panel.SetIntervalSeconds(seconds);
        }
    }

    private int ReadInterval()
    {
        if (int.TryParse(IntervalBox.Text, out var seconds))
        {
            return Math.Clamp(seconds, 1, 86400);
        }
        IntervalBox.Text = "5";
        return 5;
    }

    private void OnFullscreenClick(object sender, RoutedEventArgs e)
    {
        if (_fullscreenPanel is not null)
        {
            RestoreLayout();
            return;
        }
        if (WindowState == WindowState.Maximized)
        {
            WindowState = WindowState.Normal;
        }
        else
        {
            WindowState = WindowState.Maximized;
        }
    }

    private void OnPresentationModeClick(object sender, RoutedEventArgs e) =>
        EnterPresentation();

    private void EnterPresentation()
    {
        WindowState = WindowState.Normal;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        HideChrome();
        WindowState = WindowState.Maximized;
    }

    private void EnterPanelFullscreen(SlideshowPanel panel)
    {
        if (_fullscreenPanel is not null)
        {
            RestoreLayout();
        }

        _fullscreenPanel = panel;
        LeftColumn.Width = panel == Panel1 ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        RightColumn.Width = panel == Panel3 ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        MiddleColumn.Width = panel == Panel2 ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        Splitter1Column.Width = new GridLength(0);
        Splitter2Column.Width = new GridLength(0);
        Splitter1.Visibility = Visibility.Collapsed;
        Splitter2.Visibility = Visibility.Collapsed;
        Panel1.Visibility = panel == Panel1 ? Visibility.Visible : Visibility.Collapsed;
        Panel2.Visibility = panel == Panel2 ? Visibility.Visible : Visibility.Collapsed;
        Panel3.Visibility = panel == Panel3 ? Visibility.Visible : Visibility.Collapsed;
        WindowState = WindowState.Maximized;
    }

    private void RestoreLayout()
    {
        LeftColumn.Width = new GridLength(1, GridUnitType.Star);
        MiddleColumn.Width = new GridLength(1, GridUnitType.Star);
        RightColumn.Width = new GridLength(1, GridUnitType.Star);
        Splitter1Column.Width = new GridLength(7);
        Splitter2Column.Width = new GridLength(7);
        Splitter1.Visibility = Visibility.Visible;
        Splitter2.Visibility = Visibility.Visible;
        Splitter1.Visibility = Visibility.Visible;
        Splitter2.Visibility = Visibility.Visible;
        Panel1.Visibility = Visibility.Visible;
        Panel2.Visibility = Visibility.Visible;
        Panel3.Visibility = Visibility.Visible;
        _fullscreenPanel = null;
        UpdateLayoutButtons();
    }

    private void OnTitleBarMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }
        if (e.ClickCount == 2)
        {
            OnFullscreenClick(this, new RoutedEventArgs());
            return;
        }
        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.OriginalSource is TextBox)
        {
            return;
        }
        if (e.Key == Key.F11)
        {
            OnFullscreenClick(this, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            if (_fullscreenPanel is not null)
            {
                RestoreLayout();
            }
            else if (WindowState == WindowState.Maximized)
            {
                WindowState = WindowState.Normal;
            }
            ShowChrome();
            ResizeMode = ResizeMode.CanResize;
            if (_fullscreenPanel is not null)
            {
                RestoreLayout();
            }
        }
        else if (e.Key == Key.F10)
        {
            EnterPresentation();
            e.Handled = true;
        }
    }

    private void ShowChrome()
    {
        TitleBar.Visibility = Visibility.Visible;
        FooterBar.Visibility = Visibility.Visible;
        _chromeHideTimer?.Stop();
    }

    private void HideChrome()
    {
        TitleBar.Visibility = Visibility.Collapsed;
        FooterBar.Visibility = Visibility.Collapsed;
        _chromeHideTimer?.Stop();
    }

    private void OnWindowMouseMove(object sender, MouseEventArgs e)
    {
        RestoreCursor();
        if (_autoHideCursor && IsActive && !IsChromeVisible)
        {
            _cursorHideTimer?.Stop();
            _cursorHideTimer?.Start();
        }
    }

    private void RestoreCursor()
    {
        if (_isCursorHidden)
        {
            Mouse.OverrideCursor = null;
            _isCursorHidden = false;
        }
        _cursorHideTimer?.Stop();
    }

    private void OnToggleHideCursorClick(object sender, RoutedEventArgs e)
    {
        _autoHideCursor = HideCursorMenuItem.IsChecked;
        if (!_autoHideCursor)
        {
            RestoreCursor();
        }
        else
        {
            _cursorHideTimer?.Stop();
            _cursorHideTimer?.Start();
        }
    }

    private void OnRootMouseMove(object sender, MouseEventArgs e)
    {
        if (IsPointerOverChrome())
        {
            ShowChrome();
            _chromeHideTimer?.Stop();
            _chromeHideTimer?.Start();
        }
        else if (IsChromeVisible)
        {
            HideChrome();
        }
    }

    private void OnToggleChromeClick(object sender, RoutedEventArgs e)
    {
        if (IsChromeVisible)
        {
            HideChrome();
        }
        else
        {
            ShowChrome();
        }
    }

    private void OnSetIntervalMenuClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string value } && int.TryParse(value, out var seconds))
        {
            foreach (var panel in new[] { Panel1, Panel2, Panel3 })
            {
                panel.SetIntervalSeconds(seconds);
            }
        }
    }

    private void OnContentMouseMove(object sender, MouseEventArgs e)
    {
        if (!IsPointerOverChrome())
        {
            HideChrome();
            return;
        }
        ShowChrome();
        _chromeHideTimer?.Stop();
        _chromeHideTimer?.Start();
    }

    private bool IsChromeVisible => TitleBar.Visibility == Visibility.Visible;

    private bool IsPointerOverChrome()
    {
        var position = Mouse.GetPosition(RootGrid);
        return position.Y <= 44 || position.Y >= RootGrid.ActualHeight - 36;
    }

    private void OnContentMouseLeave(object sender, MouseEventArgs e)
    {
        if (WindowState == WindowState.Maximized)
        {
            HideChrome();
        }
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void OnCloseClick(object sender, RoutedEventArgs e) =>
        Close();

    private void UpdateLayoutButtons()
    {
    }
}
