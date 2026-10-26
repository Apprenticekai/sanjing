using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
namespace MultiFrameViewer;

public sealed class SlideshowPanel : Grid
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".bmp", ".gif", ".tif", ".tiff",
        ".avif", ".jxl", ".heic", ".heif"
    };
    private readonly Image _image;
    private readonly Border _placeholder;
    private readonly TextBlock _placeholderText;
    private readonly TextBlock _folderLabel;
    private readonly TextBlock _fileLabel;
    private readonly TextBlock _counterLabel;
    private readonly Button _playButton;
    private readonly Button _folderButton;
    private readonly Button _closeImageButton;
    private readonly Border _hoverPanel;
    private readonly DispatcherTimer _playbackTimer;
    private readonly DispatcherTimer _hideHoverTimer;
    private readonly List<ImageItem> _images = [];
    private readonly Dictionary<int, BitmapSource> _decodedImages = [];
    private readonly RandomFolderCycle _randomFolderCycle = new();
    private CancellationTokenSource? _decodeCts;
    private string? _activePath;
    private int _switchVersion;
    private int _index;
    private bool _isPlaying;
    private bool _isOpened;
    private bool _isAdvancing;
    private DateTime _nextAdvanceAt;
    private string _rootFolder = "";
    private string _activeFolder = "";
    private TextBlock? _placeholderTextRef;
    private GridLength[] _savedColumnWidths = [];
    private int _panelNumber;

    public SlideshowPanel()
        : this(1)
    {
    }

    public SlideshowPanel(int panelNumber)
    {
        ClipToBounds = true;
        Background = new SolidColorBrush(Color.FromRgb(11, 13, 17));
        Margin = new Thickness(1);
        Focusable = true;

        _image = new Image
        {
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            SnapsToDevicePixels = true
        };
        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.HighQuality);

        _placeholderText = new TextBlock
        {
            Text = "",
            Foreground = new SolidColorBrush(Color.FromRgb(145, 155, 168)),
            FontSize = 16,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        _placeholder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(16, 19, 24)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(42, 48, 57)),
            BorderThickness = new Thickness(1),
            Child = _placeholderText
        };
        _placeholderTextRef = _placeholderText;
        SetPanelNumber(panelNumber);
        _placeholder.MouseLeftButtonDown += (_, _) => OpenFolder();
        _placeholder.Visibility = Visibility.Visible;

        _folderLabel = CreateOverlayText(13, FontWeights.SemiBold);
        _fileLabel = CreateOverlayText(12, FontWeights.Normal);
        _fileLabel.Opacity = 0.82;
        _counterLabel = CreateOverlayText(11, FontWeights.Normal);
        _counterLabel.Opacity = 0.72;

        _playButton = CreateToolButton("暂停", 62);
        _playButton.Click += (_, _) => TogglePlayback();

        _folderButton = CreateToolButton("换文件夹", 74);
        _folderButton.Click += (_, _) => OpenFolder();
        _closeImageButton = CreateToolButton("关闭画面", 74);
        _closeImageButton.Click += (_, _) => CloseImage();

        var tools = new StackPanel
        {
            Orientation = Orientation.Horizontal
        };
        tools.Children.Add(_folderButton);
        tools.Children.Add(_closeImageButton);
        tools.Children.Add(_playButton);

        _hoverPanel = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 8, 8),
            Padding = new Thickness(8, 6, 2, 6),
            Background = new SolidColorBrush(Color.FromArgb(190, 17, 21, 27)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Child = tools,
            Visibility = Visibility.Collapsed,
            Opacity = 0.92
        };
        _hoverPanel.MouseLeftButtonUp += OnHoverPanelMouseLeftButtonUp;

        var labels = new StackPanel
        {
            Margin = new Thickness(14, 10, 14, 42),
            VerticalAlignment = VerticalAlignment.Bottom,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed
        };
        labels.Children.Add(_folderLabel);
        labels.Children.Add(_fileLabel);
        labels.Children.Add(_counterLabel);

        Children.Add(_image);
        Children.Add(_placeholder);
        Children.Add(labels);
        Children.Add(_hoverPanel);

        _hideHoverTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1.8)
        };
        _playbackTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(200)
        };
        _playbackTimer.Tick += OnPlaybackTimerTick;
        _hideHoverTimer.Tick += (_, _) =>
        {
            _hideHoverTimer.Stop();
            HideHoverPanel();
        };
        _hoverPanel.MouseEnter += (_, _) =>
        {
            _hideHoverTimer.Stop();
            _hoverPanel.Visibility = Visibility.Visible;
        };

        MouseDown += OnMouseDown;
        KeyDown += OnKeyDown;
        PreviewMouseWheel += OnPreviewMouseWheel;
        MouseEnter += (_, _) =>
        {
            _hideHoverTimer.Stop();
            if (_isOpened)
            {
                _hoverPanel.Visibility = Visibility.Visible;
            }
            _hideHoverTimer.Start();
        };
        MouseLeave += (_, _) => HideHoverPanel();
        MouseMove += (_, _) =>
        {
            if (_isOpened && _hoverPanel.Visibility != Visibility.Visible)
            {
                _hoverPanel.Visibility = Visibility.Visible;
            }
            if (_hoverPanel.Visibility == Visibility.Visible)
            {
                _hideHoverTimer.Stop();
                _hideHoverTimer.Start();
            }
        };
    }

    public event Action<SlideshowPanel>? RequestFullscreen;
    public event Action? RequestRestoreLayout;

    public bool IsOpened => _isOpened;
    public bool IsPlaying => _isPlaying;
    public string RootFolder => _rootFolder;
    public int IntervalSeconds { get; set; } = 5;
    public bool IsTransient { get; set; }
    public GridLength[] SavedColumnWidths
    {
        get => _savedColumnWidths;
        set => _savedColumnWidths = value;
    }

    public void SetPanelNumber(int number)
    {
        _panelNumber = number;
        if (_placeholderTextRef is not null && !_isOpened)
        {
            _placeholderTextRef.Text = $"画面 {number}\n选择文件夹开始放映";
        }
    }

    public void OpenFolder(string? folder = null)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            OpenFolderPicker?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (!Directory.Exists(folder))
        {
            MessageBox.Show("文件夹不存在或无法访问。", "三境", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        LoadFolder(folder);
    }


    private void LoadFolder(string folder)
    {
        var active = folder;
        if (EnumerateDirectImages(folder).Count == 0)
        {
            var childFolders = Directory.GetDirectories(folder)
                .Where(directory => EnumerateDirectImages(directory).Count > 0)
                .OrderBy(directory => directory, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
            if (childFolders.Length > 0)
            {
                active = childFolders[0];
            }
        }
        LoadFolderCore(folder, active, active);
    }

    public void OpenRootFolder(string? rootFolder = null, string? startSubfolder = null)
    {
        if (string.IsNullOrWhiteSpace(rootFolder) || !Directory.Exists(rootFolder))
        {
            OpenFolderPicker?.Invoke(this, EventArgs.Empty);
            return;
        }
        if (string.IsNullOrWhiteSpace(startSubfolder) || !Directory.Exists(startSubfolder))
        {
            var children = Directory.GetDirectories(rootFolder)
                .Where(directory => EnumerateDirectImages(directory).Count > 0)
                .OrderBy(directory => directory, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
            startSubfolder = children.Length > 0 ? children[0] : rootFolder;
        }
        LoadFolderCore(rootFolder, startSubfolder, startSubfolder);
    }

    private void LoadFolderCore(string rootFolder, string activeFolder, string startFolder)
    {
        StopPlaybackTimer();
        CancelDecode();
        _images.Clear();
        _decodedImages.Clear();
        _index = 0;
        _rootFolder = rootFolder;
        _activeFolder = activeFolder;

        try
        {
            var files = EnumerateDirectImages(activeFolder);
            _images.AddRange(files);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"读取文件夹失败：\n{ex.Message}", "三境", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (_images.Count == 0)
        {
            _isOpened = false;
            _placeholder.Visibility = Visibility.Visible;
            _placeholderText.Text = "没有找到支持的图片\n点击重新选择文件夹";
            return;
        }

        _isOpened = true;
        _placeholder.Visibility = Visibility.Collapsed;
        _folderLabel.Text = Path.GetFileName(activeFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(_folderLabel.Text))
        {
            _folderLabel.Text = activeFolder;
        }
        _folderLabel.ToolTip = activeFolder;
        _index = 0;
        RecordDisplayedFolder(activeFolder);
        ShowImageAsync(_index, true);
        StartPlayback();
        PreloadNext(0);
    }

    public void StartPlayback()
    {
        if (!_isOpened)
        {
            return;
        }
        _isPlaying = true;
        _playButton.Content = "暂停";
        StartPlaybackTimer();
    }

    public void SetIntervalSeconds(int seconds)
    {
        IntervalSeconds = Math.Max(1, seconds);
        if (_isPlaying)
        {
            _nextAdvanceAt = DateTime.UtcNow.AddSeconds(IntervalSeconds);
        }
    }

    public void StopPlayback()
    {
        _isPlaying = false;
        _playButton.Content = "播放";
        StopPlaybackTimer();
    }

    public void TogglePlayback()
    {
        if (!_isOpened)
        {
            OpenFolder();
            return;
        }

        if (_isPlaying)
        {
            StopPlayback();
        }
        else
        {
            StartPlayback();
        }
    }

    public void NextImage()
    {
        if (_images.Count == 0)
        {
            return;
        }
        _index = (_index + 1) % _images.Count;
        _switchVersion++;
        ShowImageAsync(_index);
        PreloadNext(_index);
    }

    public void PreviousImage()
    {
        if (_images.Count == 0)
        {
            return;
        }
        _index = (_index - 1 + _images.Count) % _images.Count;
        _switchVersion++;
        ShowImageAsync(_index);
        PreloadNext(_index);
    }

    public void SetImages(IEnumerable<string> paths)
    {
        var items = paths
            .Where(path => File.Exists(path) && ImageExtensions.Contains(Path.GetExtension(path)))
            .Select(path => new ImageItem(path, Path.GetFileName(path)))
            .ToList();
        if (items.Count == 0)
        {
            return;
        }

        StopPlaybackTimer();
        CancelDecode();
        _images.Clear();
        _decodedImages.Clear();
        _images.AddRange(items);
        _index = 0;
        _isOpened = true;
        _rootFolder = "";
        _placeholder.Visibility = Visibility.Collapsed;
        _folderLabel.Text = "临时图片列表";
        _folderLabel.ToolTip = null;
        ShowImageAsync(0, true);
        StartPlayback();
    }

    private async void ShowImageAsync(int index, bool immediate = false)
    {
        if (_images.Count == 0 || index < 0 || index >= _images.Count)
        {
            return;
        }

        var item = _images[index];
        var version = ++_switchVersion;
        if (_decodedImages.TryGetValue(index, out var cached) && cached.IsFrozen)
        {
            SwapImage(cached, item, immediate);
            return;
        }

        var requestIndex = index;
        try
        {
            var bitmap = await DecodeBitmapAsync(item.Path);
            if (bitmap is null || requestIndex != _index || version < _switchVersion)
            {
                return;
            }
            _decodedImages[requestIndex] = bitmap;
            SwapImage(bitmap, item, immediate);
        }
        catch
        {
        }
    }

    private void SwapImage(BitmapSource bitmap, ImageItem item, bool immediate)
    {
        var changed = !string.Equals(_activePath, item.Path, StringComparison.OrdinalIgnoreCase);
        _activePath = item.Path;
        if (immediate)
        {
            _image.Source = bitmap;
            _fileLabel.Text = item.DisplayPath;
            _counterLabel.Text = $"{_index + 1} / {_images.Count}";
            if (changed && !string.IsNullOrEmpty(_rootFolder))
            {
                LogAdvance(item);
            }
        PreloadNext(_index);
            return;
        }
        _image.Source = bitmap;
        _fileLabel.Text = item.DisplayPath;
        _fileLabel.ToolTip = item.Path;
        _counterLabel.Text = $"{_index + 1} / {_images.Count}";
        _closeImageButton.Visibility = _images.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (changed && !string.IsNullOrEmpty(_rootFolder))
        {
            LogAdvance(item);
        }
        PreloadNext(_index);
    }

    private void LogAdvance(ImageItem item)
    {
        try
        {
            var logDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MultiFrameViewer");
            Directory.CreateDirectory(logDirectory);
            File.AppendAllText(
                Path.Combine(logDirectory, "slideshow.log"),
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\t{_panelNumber}\t{_folderLabel.Text}\t{item.DisplayPath}{Environment.NewLine}");
        }
        catch
        {
        }
    }

    private static async Task<BitmapSource?> DecodeBitmapAsync(string path)
    {
        try
        {
            return await Task.Run(() =>
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bitmap.UriSource = new Uri(path, UriKind.Absolute);
                bitmap.EndInit();
                bitmap.Freeze();
                return (BitmapSource)bitmap;
            });
        }
        catch
        {
            return null;
        }
    }

    private void PreloadNext(int index)
    {
        if (_images.Count == 0)
        {
            return;
        }
        var next = (index + 1) % _images.Count;
        if (next == _index || _decodedImages.ContainsKey(next))
        {
            return;
        }
        _ = Task.Run(async () =>
        {
            var bitmap = await DecodeBitmapAsync(_images[next].Path);
            if (bitmap is not null)
            {
                await Dispatcher.InvokeAsync(() => _decodedImages.TryAdd(next, bitmap));
            }
        });
    }

    private void CancelDecode()
    {
        _decodeCts?.Cancel();
        _decodeCts?.Dispose();
        _decodeCts = new CancellationTokenSource();
    }

    private void CloseImage()
    {
        StopPlayback();
        _images.Clear();
        _isOpened = false;
        _rootFolder = "";
        _image.Source = null;
        _decodedImages.Clear();
        _image.Visibility = Visibility.Visible;
        _placeholder.Visibility = Visibility.Visible;
        _placeholderText.Text = "画面已关闭\n点击选择文件夹";
        HideHoverPanel();
    }

    private static TextBlock CreateOverlayText(double fontSize, FontWeight weight) => new()
    {
        Foreground = Brushes.White,
        FontSize = fontSize,
        FontWeight = weight,
        TextTrimming = TextTrimming.CharacterEllipsis,
        TextWrapping = TextWrapping.NoWrap
    };

    private Button CreateToolButton(string text, double width) => new()
    {
        Content = text,
        Width = width,
        Height = 28,
        Margin = new Thickness(0, 0, 6, 0),
        Background = new SolidColorBrush(Color.FromArgb(195, 22, 27, 34)),
        Foreground = Brushes.White,
        BorderBrush = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)),
        BorderThickness = new Thickness(1),
        Padding = new Thickness(0),
        Cursor = Cursors.Hand,
        FontSize = 12
    };

    private void ShowHoverPanel()
    {
        _hoverPanel.Visibility = _isOpened ? Visibility.Visible : Visibility.Collapsed;
        _hideHoverTimer.Stop();
    }

    private void HideHoverPanel()
    {
        _hoverPanel.Visibility = Visibility.Collapsed;
    }

    private void OnHoverPanelMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && FindAncestor<Button>(source) is not null)
        {
            return;
        }
        Focus();
    }

    private static T? FindAncestor<T>(DependencyObject source) where T : DependencyObject
    {
        for (var current = source; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is T match)
            {
                return match;
            }
        }
        return null;
    }

    private void StartPlaybackTimer()
    {
        _nextAdvanceAt = DateTime.UtcNow.AddSeconds(Math.Max(1, IntervalSeconds));
        if (!_playbackTimer.IsEnabled)
        {
            _playbackTimer.Start();
        }
    }

    private void StopPlaybackTimer()
    {
        _playbackTimer.Stop();
        _isAdvancing = false;
    }

    private void OnPlaybackTimerTick(object? sender, EventArgs e)
    {
        if (!_isPlaying || !_isOpened || _isAdvancing || DateTime.UtcNow < _nextAdvanceAt)
        {
            return;
        }

        _isAdvancing = true;
        try
        {
            AdvanceForSlideshow();
            _nextAdvanceAt = DateTime.UtcNow.AddSeconds(Math.Max(1, IntervalSeconds));
        }
        finally
        {
            _isAdvancing = false;
        }
    }

    private void AdvanceForSlideshow()
    {
        if (_images.Count == 0)
        {
            return;
        }

        if (_index < _images.Count - 1)
        {
            _index++;
            _switchVersion++;
            ShowImageAsync(_index);
            PreloadNext(_index);
            return;
        }

        if (TryLoadRandomSibling())
        {
            return;
        }

        if (!string.Equals(_activeFolder, _rootFolder, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _index = 0;
        _switchVersion++;
        ShowImageAsync(_index);
        PreloadNext(_index);
    }

    private RandomFolderPool? FindRandomFolderPool()
    {
        var active = new DirectoryInfo(_activeFolder);
        var parent = active.Parent;
        if (!active.Exists || parent is null || !parent.Exists)
        {
            return null;
        }

        try
        {
            var folders = parent.GetDirectories()
                .Where(directory => IsInsideRoot(directory.FullName))
                .Select(directory => new FolderQueue(
                    directory.FullName,
                    EnumerateDirectImages(directory.FullName)))
                .Where(folder => folder.Images.Count > 0)
                .OrderBy(folder => folder.FullPath, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var currentFolder = folders.FirstOrDefault(folder => string.Equals(
                folder.FullPath,
                _activeFolder,
                StringComparison.OrdinalIgnoreCase));
            if (currentFolder is null)
            {
                return null;
            }

            return new RandomFolderPool(parent.FullName, folders);
        }
        catch
        {
            return null;
        }
    }

    private FolderQueue? PickRandomSibling()
    {
        var pool = FindRandomFolderPool();
        if (pool is null)
        {
            return null;
        }

        _randomFolderCycle.RecordDisplayed(pool.Key, _activeFolder);
        var selectedPath = _randomFolderCycle.Pick(
            pool.Key,
            pool.Folders.Select(folder => folder.FullPath).ToList(),
            _activeFolder);
        return pool.Folders.FirstOrDefault(folder => string.Equals(
            folder.FullPath,
            selectedPath,
            StringComparison.OrdinalIgnoreCase));
    }

    private void LoadRandomFolder(FolderQueue folder)
    {
        _images.Clear();
        _images.AddRange(folder.Images);
        _decodedImages.Clear();
        _activeFolder = folder.FullPath;
        _folderLabel.Text = Path.GetFileName(folder.FullPath.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar));
        _folderLabel.ToolTip = folder.FullPath;
        _index = 0;
        _switchVersion++;
        RecordDisplayedFolder(folder.FullPath);
        ShowImageAsync(_index);
        PreloadNext(_index);
    }

    private bool TryLoadRandomSibling()
    {
        var folder = PickRandomSibling();
        if (folder is null)
        {
            return false;
        }
        LoadRandomFolder(folder);
        return true;
    }

    private void RecordDisplayedFolder(string folder)
    {
        var parent = new DirectoryInfo(folder).Parent;
        if (parent is not null && parent.Exists)
        {
            _randomFolderCycle.RecordDisplayed(parent.FullName, folder);
        }
    }

    private bool IsInsideRoot(string path)
    {
        var root = Path.GetFullPath(_rootFolder)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    public event EventHandler? OpenFolderPicker;

    private static List<ImageItem> EnumerateDirectImages(string folder)
    {
        try
        {
            return Directory.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly)
                .Where(path => ImageExtensions.Contains(Path.GetExtension(path)))
                .Select(path => new FileInfo(path))
                .Where(file => file.Exists)
                .OrderBy(file => file.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(file => new ImageItem(file.FullName, file.Name))
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!_isOpened || e.Delta == 0)
        {
            return;
        }

        if (_images.Count == 0)
        {
            return;
        }

        if (e.Delta < 0)
        {
            _index = (_index + 1) % _images.Count;
        }
        else
        {
            _index = (_index - 1 + _images.Count) % _images.Count;
        }
        _switchVersion++;
        ShowImageAsync(_index);
        PreloadNext(_index);
        e.Handled = true;
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        Focus();
        ShowHoverPanel();
        if (e.ChangedButton == MouseButton.Right)
        {
            ShowContextMenu(e.GetPosition(this));
            e.Handled = true;
        }
        else if (e.ChangedButton == MouseButton.Left && e.ClickCount == 2)
        {
            RequestFullscreen?.Invoke(this);
            e.Handled = true;
        }
    }

    private void ShowContextMenu(Point point)
    {
        var menu = new ContextMenu();
        var openItem = new MenuItem { Header = _isOpened ? "选择文件夹…" : "选择文件夹…" };
        openItem.Click += (_, _) => OpenFolder();
        menu.Items.Add(openItem);
        if (_isOpened)
        {
            var locateItem = new MenuItem { Header = "定位到当前图片" };
            locateItem.IsEnabled = !string.IsNullOrWhiteSpace(_activePath) && File.Exists(_activePath);
            locateItem.Click += (_, _) => LocateCurrentImage();
            menu.Items.Add(locateItem);

            var randomFolderItem = new MenuItem { Header = "随机切换同级文件夹" };
            randomFolderItem.Click += (_, _) => SwitchToRandomSibling();
            menu.Items.Add(randomFolderItem);

            var deleteFolderItem = new MenuItem { Header = "将当前文件夹移到回收站…" };
            deleteFolderItem.IsEnabled = !string.IsNullOrWhiteSpace(_activeFolder) &&
                Directory.Exists(_activeFolder) &&
                !string.Equals(_activeFolder, _rootFolder, StringComparison.OrdinalIgnoreCase);
            deleteFolderItem.Click += (_, _) => MoveCurrentFolderToRecycleBin();
            menu.Items.Add(deleteFolderItem);

            menu.Items.Add(new Separator());
            var playItem = new MenuItem { Header = _isPlaying ? "暂停" : "播放" };
            playItem.Click += (_, _) => TogglePlayback();
            menu.Items.Add(playItem);
            var closeItem = new MenuItem { Header = "关闭画面" };
            closeItem.Click += (_, _) => CloseImage();
            menu.Items.Add(closeItem);
            menu.Items.Add(new Separator());
            var nextItem = new MenuItem { Header = "下一张" };
            nextItem.Click += (_, _) => NextImage();
            menu.Items.Add(nextItem);
            var previousItem = new MenuItem { Header = "上一张" };
            previousItem.Click += (_, _) => PreviousImage();
            menu.Items.Add(previousItem);
        }
        menu.PlacementTarget = this;
        menu.IsOpen = true;
    }

    private void LocateCurrentImage()
    {
        if (string.IsNullOrWhiteSpace(_activePath) || !File.Exists(_activePath))
        {
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{_activePath}\"",
                UseShellExecute = true
            });
        }
        catch
        {
        }
    }

    private void SwitchToRandomSibling()
    {
        if (!_isOpened)
        {
            return;
        }
        if (!TryLoadRandomSibling())
        {
            MessageBox.Show(
                "当前文件夹所在目录中没有找到其他含图片的同级文件夹。",
                "三境",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }
        if (_isPlaying)
        {
            StartPlayback();
        }
    }

    private void MoveCurrentFolderToRecycleBin()
    {
        if (string.IsNullOrWhiteSpace(_activeFolder) || !Directory.Exists(_activeFolder))
        {
            return;
        }
        if (string.Equals(_activeFolder, _rootFolder, StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(
                "为避免误删整个根目录，不能删除正在播放的根文件夹。请先切换到它的子文件夹。",
                "三境",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var folderName = Path.GetFileName(_activeFolder.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar));
        int imageCount;
        try
        {
            imageCount = Directory.EnumerateFiles(
                    _activeFolder,
                    "*",
                    SearchOption.AllDirectories)
                .Count(path => ImageExtensions.Contains(Path.GetExtension(path)));
        }
        catch
        {
            imageCount = _images.Count;
        }

        var answer = MessageBox.Show(
            $"确定将整个文件夹移到回收站吗？\n\n文件夹：{folderName}\n路径：{_activeFolder}\n图片：约 {imageCount} 张\n\n它会连同文件夹内其他文件一起移动，可从回收站恢复。",
            "确认移入回收站",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        var wasPlaying = _isPlaying;
        var deletedFolder = _activeFolder;
        var nextFolder = PickRandomSibling();
        StopPlayback();
        try
        {
            RecycleBin.MoveDirectory(deletedFolder);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"移入回收站失败：\n{ex.Message}",
                "三境",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        _randomFolderCycle.Forget(deletedFolder);
        if (nextFolder is not null &&
            Directory.Exists(nextFolder.FullPath) &&
            nextFolder.Images.Count > 0)
        {
            LoadRandomFolder(nextFolder);
            if (wasPlaying)
            {
                StartPlayback();
            }
        }
        else
        {
            CloseImage();
        }
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Space:
                TogglePlayback();
                e.Handled = true;
                break;
            case Key.Right:
                NextImage();
                e.Handled = true;
                break;
            case Key.Left:
                PreviousImage();
                e.Handled = true;
                break;
            case Key.O:
                OpenFolder();
                e.Handled = true;
                break;
            case Key.Escape:
                RequestRestoreLayout?.Invoke();
                e.Handled = true;
                break;
        }
    }

    private sealed record ImageItem(string Path, string DisplayPath);
    private sealed record FolderQueue(string FullPath, List<ImageItem> Images);
    private sealed record RandomFolderPool(string Key, List<FolderQueue> Folders);
}
