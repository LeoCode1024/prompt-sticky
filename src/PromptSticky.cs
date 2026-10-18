using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Media3D;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Media.Animation;
using System.Windows.Interop;
using System.Windows.Threading;
using System.Xml.Serialization;
using IOPath = System.IO.Path;
using ShapePath = System.Windows.Shapes.Path;

[assembly: AssemblyTitle("提示词便签")]
[assembly: AssemblyProduct("提示词便签")]
[assembly: AssemblyDescription("本地提示词桌面便签小工具")]
[assembly: AssemblyCompany("")]
[assembly: AssemblyCopyright("")]
[assembly: AssemblyVersion("1.1.1.0")]
[assembly: AssemblyFileVersion("1.1.1.0")]

namespace PromptStickyTool
{
    [Serializable]
    public class PromptItem : INotifyPropertyChanged
    {
        private string _category;
        private string _prompt;

        public PromptItem()
        {
            _category = "";
            _prompt = "";
        }

        public string Category
        {
            get { return _category ?? ""; }
            set
            {
                if (_category == value)
                {
                    return;
                }

                _category = value ?? "";
                OnPropertyChanged("Category");
            }
        }

        public string Prompt
        {
            get { return _prompt ?? ""; }
            set
            {
                if (_prompt == value)
                {
                    return;
                }

                _prompt = value ?? "";
                OnPropertyChanged("Prompt");
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null)
            {
                handler(this, new PropertyChangedEventArgs(propertyName));
            }
        }
    }

    [Serializable]
    public class AppState
    {
        public AppState()
        {
            ExpandedWidth = 520;
            ExpandedHeight = 360;
            IsLocked = false;
            AutoStart = false;
            IsTopmost = false;
            Items = new List<PromptItem>();
        }

        public double Left { get; set; }
        public double Top { get; set; }
        public double ExpandedWidth { get; set; }
        public double ExpandedHeight { get; set; }
        public bool IsLocked { get; set; }
        public bool AutoStart { get; set; }
        public bool IsTopmost { get; set; }
        public List<PromptItem> Items { get; set; }
    }

    public static class EntryPoint
    {
        private static Mutex _singleInstanceMutex;

        [STAThread]
        public static void Main()
        {
            bool createdNew;
            _singleInstanceMutex = new Mutex(true, @"Local\PromptStickyTool.SingleInstance", out createdNew);
            if (!createdNew)
            {
                return;
            }

            AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
            {
                Diagnostics.LogException("UnhandledException", e.ExceptionObject as Exception);
            };

            StartupTaskCompatibility.RepairBatterySettings(Assembly.GetEntryAssembly().Location);

            Application app = new Application();
            app.ShutdownMode = ShutdownMode.OnMainWindowClose;
            app.DispatcherUnhandledException += delegate(object sender, DispatcherUnhandledExceptionEventArgs e)
            {
                Diagnostics.LogException("DispatcherUnhandledException", e.Exception);
                e.Handled = true;
            };
            app.Run(new MainWindow());
            GC.KeepAlive(_singleInstanceMutex);
        }
    }

    public static class Diagnostics
    {
        private static readonly string LogPath = IOPath.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PromptStickyTool",
            "error.log");

        public static void LogException(string source, Exception exception)
        {
            try
            {
                Directory.CreateDirectory(IOPath.GetDirectoryName(LogPath));
                string message = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " [" + source + "]" + Environment.NewLine +
                    (exception == null ? "Unknown exception" : exception.ToString()) +
                    Environment.NewLine + "----" + Environment.NewLine;
                File.AppendAllText(LogPath, message, Encoding.UTF8);
            }
            catch
            {
            }
        }

        public static void LogMessage(string source, string detail)
        {
            try
            {
                Directory.CreateDirectory(IOPath.GetDirectoryName(LogPath));
                string message = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " [" + source + "]" + Environment.NewLine +
                    detail + Environment.NewLine + "----" + Environment.NewLine;
                File.AppendAllText(LogPath, message, Encoding.UTF8);
            }
            catch
            {
            }
        }
    }

    public sealed class MainWindow : Window
    {
        private const double IconSize = 48;
        private const double DefaultExpandedWidth = 520;
        private const double DefaultExpandedHeight = 360;
        private const double MinExpandedWidth = 420;
        private const double MinExpandedHeight = 280;
        private const string AppName = "提示词便签";
        private const string RunValueName = "PromptStickyTool";
        private const int GwlExStyle = -20;
        private const int WsExToolWindow = 0x00000080;
        private const int WsExAppWindow = 0x00040000;
        private const int WmSysCommand = 0x0112;
        private const int ScMinimize = 0xF020;
        private const int WmSize = 0x0005;
        private const int SizeMinimized = 1;
        private const uint CfUnicodeText = 13;
        private const uint GmemMoveable = 0x0002;
        private const uint GmemZeroInit = 0x0040;

        private readonly string _dataDir;
        private readonly string _statePath;
        private readonly ObservableCollection<PromptItem> _items;
        private readonly DispatcherTimer _collapseTimer;
        private readonly DispatcherTimer _copyTimer;
        private readonly DispatcherTimer _saveTimer;
        private readonly DispatcherTimer _toastTimer;

        private AppState _state;
        private Border _shell;
        private Border _iconView;
        private Grid _expandedView;
        private DataGrid _grid;
        private DataGridTextColumn _categoryColumn;
        private DataGridTextColumn _promptColumn;
        private Button _lockButton;
        private Button _autoStartButton;
        private Button _topmostButton;
        private Border _toast;
        private TextBlock _toastText;
        private Thumb _resizeThumb;
        private TextBlock _resizeGlyph;
        private DataGridCell _pendingCopyCell;
        private HwndSource _hwndSource;
        private ImageSource _iconImageSource;
        private PromptItem _lastTouchedItem;
        private bool _isExpanded;
        private bool _isLocked;
        private bool _isAutoStart;
        private bool _isTopmost;
        private bool _isLoading;
        private bool _isEditing;
        private bool _isContextMenuOpen;

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hwnd, int index);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hwnd, int index, int value);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool OpenClipboard(IntPtr hWndNewOwner);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool CloseClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool EmptyClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetClipboardData(uint uFormat, IntPtr data);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalLock(IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalUnlock(IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalFree(IntPtr hMem);

        public MainWindow()
        {
            _dataDir = IOPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PromptStickyTool");
            _statePath = IOPath.Combine(_dataDir, "settings.xml");
            _items = new ObservableCollection<PromptItem>();

            _collapseTimer = new DispatcherTimer();
            _collapseTimer.Interval = TimeSpan.FromMilliseconds(500);
            _collapseTimer.Tick += CollapseTimerTick;

            _copyTimer = new DispatcherTimer();
            _copyTimer.Interval = TimeSpan.FromMilliseconds(260);
            _copyTimer.Tick += CopyTimerTick;

            _saveTimer = new DispatcherTimer();
            _saveTimer.Interval = TimeSpan.FromMilliseconds(350);
            _saveTimer.Tick += SaveTimerTick;

            _toastTimer = new DispatcherTimer();
            _toastTimer.Interval = TimeSpan.FromMilliseconds(650);
            _toastTimer.Tick += ToastTimerTick;

            _items.CollectionChanged += ItemsCollectionChanged;

            Title = AppName;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;
            MinWidth = IconSize;
            MinHeight = IconSize;
            Width = IconSize;
            Height = IconSize;
            SnapsToDevicePixels = true;
            UseLayoutRounding = true;

            MouseEnter += WindowMouseEnter;
            MouseLeave += WindowMouseLeave;
            LocationChanged += WindowLocationChanged;
            SizeChanged += WindowSizeChanged;
            StateChanged += WindowStateChanged;
            Closing += WindowClosing;

            LoadState();
            BuildInterface();
            ApplyLoadedState();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            HideFromSystemSwitcher();
            try
            {
                _hwndSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
                if (_hwndSource != null)
                {
                    _hwndSource.AddHook(WindowMessageHook);
                }
            }
            catch (Exception ex)
            {
                Diagnostics.LogException("WindowHook", ex);
            }
        }

        private IntPtr WindowMessageHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WmSysCommand && ((wParam.ToInt64() & 0xFFF0) == ScMinimize))
            {
                handled = true;
                BeginRestoreFromDesktopCommand();
            }
            else if (msg == WmSize && wParam.ToInt32() == SizeMinimized)
            {
                BeginRestoreFromDesktopCommand();
            }

            return IntPtr.Zero;
        }

        private void WindowStateChanged(object sender, EventArgs e)
        {
            if (WindowState == WindowState.Minimized)
            {
                BeginRestoreFromDesktopCommand();
            }
        }

        private void BeginRestoreFromDesktopCommand()
        {
            Dispatcher.BeginInvoke(new Action(delegate
            {
                try
                {
                    WindowState = WindowState.Normal;
                    Show();
                }
                catch (Exception ex)
                {
                    Diagnostics.LogException("RestoreFromDesktopCommand", ex);
                }
            }), DispatcherPriority.Background);
        }

        private void HideFromSystemSwitcher()
        {
            try
            {
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                int style = GetWindowLong(hwnd, GwlExStyle);
                style = (style | WsExToolWindow) & ~WsExAppWindow;
                SetWindowLong(hwnd, GwlExStyle, style);
            }
            catch (Exception ex)
            {
                Diagnostics.LogException("HideFromSystemSwitcher", ex);
            }
        }

        private void LoadState()
        {
            bool firstRun = !File.Exists(_statePath);
            _state = null;

            if (!firstRun)
            {
                try
                {
                    XmlSerializer serializer = new XmlSerializer(typeof(AppState));
                    using (FileStream stream = File.OpenRead(_statePath))
                    {
                        _state = (AppState)serializer.Deserialize(stream);
                    }
                }
                catch
                {
                    _state = null;
                }
            }

            if (_state == null)
            {
                _state = CreateDefaultState(firstRun);
            }

            if (_state.Items == null)
            {
                _state.Items = new List<PromptItem>();
            }

            foreach (PromptItem item in _state.Items)
            {
                AddItemWithEvents(item);
            }

            _isLocked = _state.IsLocked;
            _isAutoStart = _state.AutoStart;
            _isTopmost = _state.IsTopmost;
            Topmost = _isTopmost;
        }

        private AppState CreateDefaultState(bool includeSamples)
        {
            AppState state = new AppState();
            Rect workArea = SystemParameters.WorkArea;
            state.Left = Math.Max(workArea.Left + 16, workArea.Right - DefaultExpandedWidth - 48);
            state.Top = workArea.Top + 120;
            state.ExpandedWidth = DefaultExpandedWidth;
            state.ExpandedHeight = DefaultExpandedHeight;

            if (includeSamples)
            {
                state.Items.Add(new PromptItem
                {
                    Category = "润色",
                    Prompt = "请帮我润色下面这段文字，保持原意，语气自然。"
                });
                state.Items.Add(new PromptItem
                {
                    Category = "总结",
                    Prompt = "请用要点总结以下内容，并给出下一步建议。"
                });
                state.Items.Add(new PromptItem
                {
                    Category = "代码解释",
                    Prompt = "请解释这段代码的作用、关键逻辑和潜在风险。"
                });
            }

            return state;
        }

        private void BuildInterface()
        {
            _shell = new Border();
            Content = _shell;

            _iconView = BuildIconView();
            _expandedView = BuildExpandedView();
            _shell.Child = _iconView;
        }

        private Border BuildIconView()
        {
            Border border = new Border();
            border.Width = IconSize;
            border.Height = IconSize;
            border.CornerRadius = new CornerRadius(8);
            border.Background = new SolidColorBrush(Color.FromRgb(248, 250, 252));
            border.BorderBrush = new SolidColorBrush(Color.FromRgb(203, 213, 225));
            border.BorderThickness = new Thickness(1);
            border.Effect = CreateShadow(0.18, 16, 2);
            border.Opacity = 0.42;
            border.Cursor = Cursors.Hand;
            border.ToolTip = "移入展开；未锁定时可拖动";
            border.MouseLeftButtonDown += DragSurfaceMouseLeftButtonDown;

            Grid grid = new Grid();
            border.Child = grid;

            ImageSource iconSource = LoadIconImageSource();
            if (iconSource != null)
            {
                Image image = new Image();
                image.Source = iconSource;
                image.Width = 43;
                image.Height = 43;
                image.Stretch = Stretch.Uniform;
                image.HorizontalAlignment = HorizontalAlignment.Center;
                image.VerticalAlignment = VerticalAlignment.Center;
                image.SnapsToDevicePixels = true;
                grid.Children.Add(image);
                return border;
            }

            Canvas icon = new Canvas();
            icon.Width = 32;
            icon.Height = 32;
            icon.HorizontalAlignment = HorizontalAlignment.Center;
            icon.VerticalAlignment = VerticalAlignment.Center;

            ShapePath page = new ShapePath();
            page.Data = Geometry.Parse("M6,3 L22,3 L27,8 L27,28 L6,28 Z");
            page.Fill = Brushes.White;
            page.Stroke = new SolidColorBrush(Color.FromRgb(51, 65, 85));
            page.StrokeThickness = 1.4;
            icon.Children.Add(page);

            ShapePath fold = new ShapePath();
            fold.Data = Geometry.Parse("M22,3 L22,8 L27,8");
            fold.Fill = Brushes.Transparent;
            fold.Stroke = new SolidColorBrush(Color.FromRgb(51, 65, 85));
            fold.StrokeThickness = 1.2;
            icon.Children.Add(fold);

            ShapePath lines = new ShapePath();
            lines.Data = Geometry.Parse("M10,13 L23,13 M10,18 L23,18 M10,23 L19,23");
            lines.Stroke = new SolidColorBrush(Color.FromRgb(20, 184, 166));
            lines.StrokeThickness = 1.6;
            lines.StrokeStartLineCap = PenLineCap.Round;
            lines.StrokeEndLineCap = PenLineCap.Round;
            icon.Children.Add(lines);

            Ellipse dot = new Ellipse();
            dot.Width = 7;
            dot.Height = 7;
            dot.Fill = new SolidColorBrush(Color.FromRgb(244, 114, 91));
            Canvas.SetLeft(dot, 4);
            Canvas.SetTop(dot, 4);
            icon.Children.Add(dot);

            grid.Children.Add(icon);
            return border;
        }

        private ImageSource LoadIconImageSource()
        {
            if (_iconImageSource != null)
            {
                return _iconImageSource;
            }

            try
            {
                Assembly assembly = Assembly.GetExecutingAssembly();
                using (Stream stream = assembly.GetManifestResourceStream("PromptStickyTool.Assets.StickyIcon.png"))
                {
                    if (stream == null)
                    {
                        return null;
                    }

                    MemoryStream copy = new MemoryStream();
                    stream.CopyTo(copy);
                    copy.Position = 0;

                    BitmapImage image = new BitmapImage();
                    image.BeginInit();
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.StreamSource = copy;
                    image.EndInit();
                    image.Freeze();
                    _iconImageSource = image;
                    return _iconImageSource;
                }
            }
            catch (Exception ex)
            {
                Diagnostics.LogException("LoadIconImageSource", ex);
                return null;
            }
        }

        private Grid BuildExpandedView()
        {
            Grid outer = new Grid();

            Border panel = new Border();
            panel.CornerRadius = new CornerRadius(8);
            panel.Background = CreatePaperBrush();
            panel.BorderBrush = new SolidColorBrush(Color.FromRgb(231, 214, 158));
            panel.BorderThickness = new Thickness(1);
            panel.Effect = CreateShadow(0.18, 24, 3);
            outer.Children.Add(panel);

            Grid layout = new Grid();
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            panel.Child = layout;

            Border header = new Border();
            header.CornerRadius = new CornerRadius(8, 8, 0, 0);
            header.Background = new SolidColorBrush(Color.FromArgb(220, 255, 253, 235));
            header.BorderBrush = new SolidColorBrush(Color.FromRgb(235, 222, 176));
            header.BorderThickness = new Thickness(0, 0, 0, 1);
            header.MouseLeftButtonDown += HeaderMouseLeftButtonDown;
            Grid.SetRow(header, 0);
            layout.Children.Add(header);

            Grid headerGrid = new Grid();
            headerGrid.Margin = new Thickness(12, 0, 8, 0);
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.Child = headerGrid;

            TextBlock title = new TextBlock();
            title.Text = "提示词便签";
            title.VerticalAlignment = VerticalAlignment.Center;
            title.FontSize = 15;
            title.FontWeight = FontWeights.SemiBold;
            title.Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42));
            Grid.SetColumn(title, 0);
            headerGrid.Children.Add(title);

            StackPanel buttons = new StackPanel();
            buttons.Orientation = Orientation.Horizontal;
            buttons.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(buttons, 1);
            headerGrid.Children.Add(buttons);

            _lockButton = MakeHeaderButton("\uE785", "当前未锁定，可拖动和缩放", LockButtonClick);
            buttons.Children.Add(_lockButton);
            buttons.Children.Add(MakeHeaderButton("\uE710", "添加一行", AddButtonClick));
            buttons.Children.Add(MakeHeaderButton("\uE74D", "删除当前行", DeleteButtonClick));
            _autoStartButton = MakeHeaderButton("\uE7E8", "开机自启动", AutoStartButtonClick);
            buttons.Children.Add(_autoStartButton);
            _topmostButton = MakeHeaderButton("\uE718", "置于所有窗口上方", TopmostButtonClick);
            buttons.Children.Add(_topmostButton);
            buttons.Children.Add(MakeHeaderButton("\uE8B5", "导入/导出 CSV", ImportExportButtonClick));
            buttons.Children.Add(MakeHeaderButton("\uE711", "关闭工具", CloseButtonClick));

            Grid body = new Grid();
            body.Margin = new Thickness(12, 10, 12, 12);
            Grid.SetRow(body, 1);
            layout.Children.Add(body);

            _grid = BuildDataGrid();
            body.Children.Add(_grid);

            _toastText = new TextBlock();
            _toastText.Text = "√";
            _toastText.Foreground = Brushes.White;
            _toastText.FontSize = 12;
            _toastText.FontWeight = FontWeights.SemiBold;
            _toastText.Margin = new Thickness(12, 6, 12, 6);

            _toast = new Border();
            _toast.CornerRadius = new CornerRadius(8);
            _toast.Background = new SolidColorBrush(Color.FromRgb(20, 184, 166));
            _toast.Child = _toastText;
            _toast.HorizontalAlignment = HorizontalAlignment.Right;
            _toast.VerticalAlignment = VerticalAlignment.Top;
            _toast.Margin = new Thickness(0, 8, 8, 0);
            _toast.Visibility = Visibility.Collapsed;
            body.Children.Add(_toast);

            _resizeGlyph = new TextBlock();
            _resizeGlyph.Text = "◢";
            _resizeGlyph.FontSize = 15;
            _resizeGlyph.Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184));
            _resizeGlyph.HorizontalAlignment = HorizontalAlignment.Right;
            _resizeGlyph.VerticalAlignment = VerticalAlignment.Bottom;
            _resizeGlyph.Margin = new Thickness(0, 0, 1, 1);
            body.Children.Add(_resizeGlyph);

            _resizeThumb = new Thumb();
            _resizeThumb.Width = 24;
            _resizeThumb.Height = 24;
            _resizeThumb.HorizontalAlignment = HorizontalAlignment.Right;
            _resizeThumb.VerticalAlignment = VerticalAlignment.Bottom;
            _resizeThumb.Cursor = Cursors.SizeNWSE;
            _resizeThumb.Background = Brushes.Transparent;
            _resizeThumb.BorderThickness = new Thickness(0);
            _resizeThumb.DragDelta += ResizeThumbDragDelta;
            body.Children.Add(_resizeThumb);

            return outer;
        }

        private DataGrid BuildDataGrid()
        {
            DataGrid dataGrid = new DataGrid();
            dataGrid.AutoGenerateColumns = false;
            dataGrid.CanUserAddRows = false;
            dataGrid.CanUserDeleteRows = false;
            dataGrid.CanUserReorderColumns = false;
            dataGrid.CanUserSortColumns = false;
            dataGrid.CanUserResizeColumns = true;
            dataGrid.EnableRowVirtualization = false;
            dataGrid.EnableColumnVirtualization = false;
            dataGrid.SetValue(VirtualizingPanel.IsVirtualizingProperty, false);
            dataGrid.SetValue(ScrollViewer.CanContentScrollProperty, false);
            dataGrid.HeadersVisibility = DataGridHeadersVisibility.Column;
            dataGrid.SelectionUnit = DataGridSelectionUnit.Cell;
            dataGrid.SelectionMode = DataGridSelectionMode.Single;
            dataGrid.RowHeaderWidth = 0;
            dataGrid.GridLinesVisibility = DataGridGridLinesVisibility.Horizontal;
            dataGrid.HorizontalGridLinesBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240));
            dataGrid.VerticalGridLinesBrush = Brushes.Transparent;
            dataGrid.BorderThickness = new Thickness(1);
            dataGrid.BorderBrush = new SolidColorBrush(Color.FromRgb(235, 222, 176));
            dataGrid.Background = new SolidColorBrush(Color.FromArgb(145, 255, 253, 232));
            dataGrid.RowBackground = new SolidColorBrush(Color.FromArgb(215, 255, 254, 237));
            dataGrid.AlternatingRowBackground = new SolidColorBrush(Color.FromArgb(205, 255, 251, 224));
            dataGrid.FontSize = 13;
            dataGrid.RowHeight = 46;
            dataGrid.MinRowHeight = 42;
            dataGrid.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            dataGrid.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            dataGrid.ItemsSource = _items;
            dataGrid.CellStyle = CreateCellStyle();
            dataGrid.ColumnHeaderStyle = CreateHeaderStyle();
            dataGrid.PreviewMouseLeftButtonDown += GridPreviewMouseLeftButtonDown;
            dataGrid.PreviewMouseLeftButtonUp += GridPreviewMouseLeftButtonUp;
            dataGrid.MouseDoubleClick += GridMouseDoubleClick;
            dataGrid.BeginningEdit += GridBeginningEdit;
            dataGrid.CellEditEnding += GridCellEditEnding;
            dataGrid.RowEditEnding += GridRowEditEnding;
            dataGrid.CurrentCellChanged += GridCurrentCellChanged;

            Style displayStyle = CreateDisplayTextStyle();
            Style editingStyle = CreateEditingTextStyle();

            _categoryColumn = new DataGridTextColumn();
            _categoryColumn.Header = "类型";
            _categoryColumn.Binding = new Binding("Category")
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            };
            _categoryColumn.Width = new DataGridLength(145);
            _categoryColumn.MinWidth = 90;
            _categoryColumn.ElementStyle = displayStyle;
            _categoryColumn.EditingElementStyle = editingStyle;
            dataGrid.Columns.Add(_categoryColumn);

            _promptColumn = new DataGridTextColumn();
            _promptColumn.Header = "提示词";
            _promptColumn.Binding = new Binding("Prompt")
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            };
            _promptColumn.Width = new DataGridLength(1, DataGridLengthUnitType.Star);
            _promptColumn.MinWidth = 180;
            _promptColumn.ElementStyle = displayStyle;
            _promptColumn.EditingElementStyle = editingStyle;
            dataGrid.Columns.Add(_promptColumn);

            return dataGrid;
        }

        private Style CreateCellStyle()
        {
            Style style = new Style(typeof(DataGridCell));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 7, 10, 7)));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
            style.Setters.Add(new Setter(Control.BorderBrushProperty, Brushes.Transparent));
            style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
            style.Setters.Add(new Setter(UIElement.RenderTransformOriginProperty, new Point(0.5, 0.5)));
            style.Setters.Add(new Setter(UIElement.SnapsToDevicePixelsProperty, true));

            Trigger hover = new Trigger();
            hover.Property = UIElement.IsMouseOverProperty;
            hover.Value = true;
            hover.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromArgb(190, 255, 255, 245))));
            hover.Setters.Add(new Setter(Control.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(45, 212, 191))));
            hover.Setters.Add(new Setter(UIElement.RenderTransformProperty, new ScaleTransform(1.01, 1.03)));
            hover.Setters.Add(new Setter(UIElement.EffectProperty, CreateShadow(0.14, 10, 1)));
            style.Triggers.Add(hover);

            Trigger selected = new Trigger();
            selected.Property = DataGridCell.IsSelectedProperty;
            selected.Value = true;
            selected.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromArgb(150, 255, 255, 245))));
            selected.Setters.Add(new Setter(Control.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(20, 184, 166))));
            style.Triggers.Add(selected);

            return style;
        }

        private Style CreateHeaderStyle()
        {
            Style style = new Style(typeof(DataGridColumnHeader));
            style.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromArgb(210, 255, 244, 196))));
            style.Setters.Add(new Setter(Control.ForegroundProperty, new SolidColorBrush(Color.FromRgb(51, 65, 85))));
            style.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 8, 10, 8)));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0, 0, 0, 1)));
            style.Setters.Add(new Setter(Control.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(230, 211, 151))));
            return style;
        }

        private Style CreateDisplayTextStyle()
        {
            Style style = new Style(typeof(TextBlock));
            style.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap));
            style.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
            style.Setters.Add(new Setter(TextBlock.ForegroundProperty, new SolidColorBrush(Color.FromRgb(15, 23, 42))));
            style.Setters.Add(new Setter(FrameworkElement.MaxHeightProperty, 72.0));
            style.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center));
            return style;
        }

        private Style CreateEditingTextStyle()
        {
            Style style = new Style(typeof(TextBox));
            style.Setters.Add(new Setter(TextBox.TextWrappingProperty, TextWrapping.Wrap));
            style.Setters.Add(new Setter(TextBox.AcceptsReturnProperty, true));
            style.Setters.Add(new Setter(TextBox.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
            style.Setters.Add(new Setter(Control.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(20, 184, 166))));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6)));
            style.Setters.Add(new Setter(Control.ForegroundProperty, new SolidColorBrush(Color.FromRgb(15, 23, 42))));
            style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.White));
            style.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 34.0));
            style.Setters.Add(new Setter(FrameworkElement.MaxHeightProperty, 180.0));
            style.Setters.Add(new EventSetter(UIElement.PreviewKeyDownEvent, new KeyEventHandler(EditTextBoxPreviewKeyDown)));
            return style;
        }

        private Button MakeHeaderButton(string text, string tooltip, RoutedEventHandler handler)
        {
            Button button = new Button();
            button.MinWidth = 32;
            button.Height = 28;
            button.Margin = new Thickness(5, 0, 0, 0);
            button.Padding = new Thickness(8, 0, 8, 0);
            button.FontSize = 12;
            button.FontWeight = FontWeights.SemiBold;
            button.Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42));
            button.Background = new SolidColorBrush(Color.FromArgb(170, 255, 255, 248));
            button.BorderBrush = new SolidColorBrush(Color.FromRgb(232, 213, 154));
            button.BorderThickness = new Thickness(1);
            button.Cursor = Cursors.Hand;
            button.ToolTip = tooltip;
            button.Template = CreateHeaderButtonTemplate();
            TextBlock icon = new TextBlock();
            icon.Text = text;
            icon.FontFamily = new FontFamily("Segoe MDL2 Assets");
            icon.FontSize = 13;
            icon.HorizontalAlignment = HorizontalAlignment.Center;
            icon.VerticalAlignment = VerticalAlignment.Center;
            icon.Foreground = new SolidColorBrush(Color.FromRgb(51, 65, 85));
            button.Content = icon;
            button.Click += handler;
            return button;
        }

        private ControlTemplate CreateHeaderButtonTemplate()
        {
            ControlTemplate template = new ControlTemplate(typeof(Button));

            FrameworkElementFactory border = new FrameworkElementFactory(typeof(Border));
            border.Name = "Chrome";
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(7));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));

            FrameworkElementFactory content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            content.SetValue(ContentPresenter.MarginProperty, new TemplateBindingExtension(Control.PaddingProperty));
            border.AppendChild(content);
            template.VisualTree = border;

            Trigger hover = new Trigger();
            hover.Property = UIElement.IsMouseOverProperty;
            hover.Value = true;
            hover.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(240, 253, 250)), "Chrome"));
            hover.Setters.Add(new Setter(Border.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(153, 220, 213)), "Chrome"));
            template.Triggers.Add(hover);

            Trigger pressed = new Trigger();
            pressed.Property = ButtonBase.IsPressedProperty;
            pressed.Value = true;
            pressed.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(204, 251, 241)), "Chrome"));
            pressed.Setters.Add(new Setter(Border.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(20, 184, 166)), "Chrome"));
            template.Triggers.Add(pressed);

            return template;
        }

        private DropShadowEffect CreateShadow(double opacity, double blurRadius, double depth)
        {
            DropShadowEffect effect = new DropShadowEffect();
            effect.Color = Color.FromRgb(35, 38, 41);
            effect.Opacity = opacity;
            effect.BlurRadius = blurRadius;
            effect.ShadowDepth = depth;
            effect.Direction = 270;
            return effect;
        }

        private Brush CreatePaperBrush()
        {
            DrawingGroup group = new DrawingGroup();
            group.Children.Add(new GeometryDrawing(
                new SolidColorBrush(Color.FromRgb(255, 253, 232)),
                null,
                new RectangleGeometry(new Rect(0, 0, 48, 48))));

            Pen linePen = new Pen(new SolidColorBrush(Color.FromArgb(34, 217, 190, 125)), 1);
            group.Children.Add(new GeometryDrawing(
                null,
                linePen,
                new LineGeometry(new Point(0, 22), new Point(48, 22))));

            Pen marginPen = new Pen(new SolidColorBrush(Color.FromArgb(24, 20, 184, 166)), 1);
            group.Children.Add(new GeometryDrawing(
                null,
                marginPen,
                new LineGeometry(new Point(34, 0), new Point(34, 48))));

            DrawingBrush brush = new DrawingBrush(group);
            brush.TileMode = TileMode.Tile;
            brush.Viewport = new Rect(0, 0, 48, 48);
            brush.ViewportUnits = BrushMappingMode.Absolute;
            brush.Stretch = Stretch.None;
            return brush;
        }

        private void ApplyLoadedState()
        {
            _isLoading = true;

            double left = IsUsable(_state.Left) ? _state.Left : SystemParameters.WorkArea.Left + 40;
            double top = IsUsable(_state.Top) ? _state.Top : SystemParameters.WorkArea.Top + 120;
            Left = Clamp(left, SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - IconSize);
            Top = Clamp(top, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - IconSize);

            Width = IconSize;
            Height = IconSize;
            _isExpanded = false;
            _shell.Child = _iconView;
            Topmost = _isTopmost;

            UpdateButtons();
            _isLoading = false;
        }

        private void WindowMouseEnter(object sender, MouseEventArgs e)
        {
            _collapseTimer.Stop();
            Expand();
        }

        private void WindowMouseLeave(object sender, MouseEventArgs e)
        {
            BeginCollapseCountdown();
        }

        private void BeginCollapseCountdown()
        {
            if (!_isExpanded || _isEditing || _isContextMenuOpen)
            {
                return;
            }

            _collapseTimer.Stop();
            _collapseTimer.Start();
        }

        private void CollapseTimerTick(object sender, EventArgs e)
        {
            _collapseTimer.Stop();
            if (!IsMouseOver && !_isEditing && !_isContextMenuOpen)
            {
                Collapse();
            }
        }

        private void Expand()
        {
            if (_isExpanded)
            {
                return;
            }

            _isExpanded = true;
            _shell.Child = _expandedView;
            MinWidth = MinExpandedWidth;
            MinHeight = MinExpandedHeight;
            double targetWidth = Math.Max(MinExpandedWidth, IsUsable(_state.ExpandedWidth) ? _state.ExpandedWidth : DefaultExpandedWidth);
            double targetHeight = Math.Max(MinExpandedHeight, IsUsable(_state.ExpandedHeight) ? _state.ExpandedHeight : DefaultExpandedHeight);
            Left = Clamp(Left, SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - targetWidth);
            Top = Clamp(Top, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - targetHeight);
            Width = targetWidth;
            Height = targetHeight;
            ResizeMode = _isLocked ? ResizeMode.NoResize : ResizeMode.CanResize;
            UpdateButtons();
            PlayUnrollAnimation();
        }

        private void PlayUnrollAnimation()
        {
            if (_expandedView == null)
            {
                return;
            }

            ScaleTransform scale = new ScaleTransform(1, 0.08);
            _expandedView.RenderTransformOrigin = new Point(0.5, 0);
            _expandedView.RenderTransform = scale;
            _expandedView.Opacity = 0.0;

            CubicEase ease = new CubicEase();
            ease.EasingMode = EasingMode.EaseOut;

            DoubleAnimation scaleAnimation = new DoubleAnimation();
            scaleAnimation.From = 0.08;
            scaleAnimation.To = 1.0;
            scaleAnimation.Duration = TimeSpan.FromMilliseconds(190);
            scaleAnimation.EasingFunction = ease;

            DoubleAnimation opacityAnimation = new DoubleAnimation();
            opacityAnimation.From = 0.0;
            opacityAnimation.To = 1.0;
            opacityAnimation.Duration = TimeSpan.FromMilliseconds(130);
            opacityAnimation.EasingFunction = ease;

            scale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnimation);
            _expandedView.BeginAnimation(UIElement.OpacityProperty, opacityAnimation);
        }

        private void Collapse()
        {
            if (!_isExpanded)
            {
                return;
            }

            CommitGridEdit();
            _state.ExpandedWidth = Math.Max(MinExpandedWidth, ActualWidth);
            _state.ExpandedHeight = Math.Max(MinExpandedHeight, ActualHeight);
            _state.Left = Left;
            _state.Top = Top;

            _isExpanded = false;
            MinWidth = IconSize;
            MinHeight = IconSize;
            Width = IconSize;
            Height = IconSize;
            ResizeMode = ResizeMode.NoResize;
            _shell.Child = _iconView;
            ScheduleSave();
        }

        private void HeaderMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_isLocked || e.LeftButton != MouseButtonState.Pressed || IsInteractiveElement(e.OriginalSource as DependencyObject))
            {
                return;
            }

            DragWindow();
        }

        private void DragSurfaceMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_isLocked || e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            DragWindow();
        }

        private void DragWindow()
        {
            try
            {
                DragMove();
                _state.Left = Left;
                _state.Top = Top;
                ScheduleSave();
            }
            catch
            {
            }
        }

        private void ResizeThumbDragDelta(object sender, DragDeltaEventArgs e)
        {
            if (_isLocked || !_isExpanded)
            {
                return;
            }

            Width = Math.Max(MinExpandedWidth, Width + e.HorizontalChange);
            Height = Math.Max(MinExpandedHeight, Height + e.VerticalChange);
            _state.ExpandedWidth = Width;
            _state.ExpandedHeight = Height;
            ScheduleSave();
        }

        private void LockButtonClick(object sender, RoutedEventArgs e)
        {
            _isLocked = !_isLocked;
            _state.IsLocked = _isLocked;
            UpdateButtons();
            ScheduleSave();
        }

        private void AddButtonClick(object sender, RoutedEventArgs e)
        {
            CommitGridEdit();

            PromptItem item = new PromptItem();
            item.Category = "新类型";
            item.Prompt = "在这里输入提示词";
            AddItemWithEvents(item);
            _lastTouchedItem = item;
            Dispatcher.BeginInvoke(new Action(delegate
            {
                try
                {
                    if (_grid != null && _items.Contains(item))
                    {
                        _grid.SelectedItem = item;
                        _grid.CurrentCell = new DataGridCellInfo(item, _promptColumn);
                    }
                }
                catch (Exception ex)
                {
                    Diagnostics.LogException("SelectNewRow", ex);
                }
            }), DispatcherPriority.ApplicationIdle);
            ScheduleSave();
        }

        private void DeleteButtonClick(object sender, RoutedEventArgs e)
        {
            CommitGridEdit();
            PromptItem item = GetCurrentItem();
            if (item == null)
            {
                ShowToast("请先点击要删除的行");
                return;
            }

            _collapseTimer.Stop();
            MessageBoxResult result = MessageBox.Show(this, "确定删除当前行吗？", "删除确认", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                int index = _items.IndexOf(item);
                _items.Remove(item);
                if (_items.Count > 0)
                {
                    int nextIndex = Math.Max(0, Math.Min(index, _items.Count - 1));
                    _lastTouchedItem = _items[nextIndex];
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        try
                        {
                            _grid.SelectedItem = _lastTouchedItem;
                            _grid.CurrentCell = new DataGridCellInfo(_lastTouchedItem, _promptColumn);
                        }
                        catch (Exception ex)
                        {
                            Diagnostics.LogException("SelectAfterDelete", ex);
                        }
                    }), DispatcherPriority.ApplicationIdle);
                }
                else
                {
                    _lastTouchedItem = null;
                }
                ScheduleSave();
            }

            if (!IsMouseOver)
            {
                BeginCollapseCountdown();
            }
        }

        private void AutoStartButtonClick(object sender, RoutedEventArgs e)
        {
            bool next = !_isAutoStart;
            if (!ApplyAutoStart(next))
            {
                ShowToast("自启动设置失败");
                return;
            }

            _isAutoStart = next;
            _state.AutoStart = next;
            UpdateButtons();
            ScheduleSave();
            ShowToast(next ? "已开启自启动" : "已关闭自启动");
        }

        private void TopmostButtonClick(object sender, RoutedEventArgs e)
        {
            _isTopmost = !_isTopmost;
            Topmost = _isTopmost;
            _state.IsTopmost = _isTopmost;
            UpdateButtons();
            ScheduleSave();
            ShowToast(_isTopmost ? "已置顶" : "已取消置顶");
        }

        private void ImportExportButtonClick(object sender, RoutedEventArgs e)
        {
            Button button = sender as Button;
            if (button == null)
            {
                return;
            }

            _collapseTimer.Stop();

            bool menuActionStarted = false;
            ContextMenu menu = new ContextMenu();
            menu.PlacementTarget = button;
            menu.Placement = PlacementMode.Bottom;
            _isContextMenuOpen = true;

            MenuItem importItem = new MenuItem();
            importItem.Header = "导入 CSV";
            importItem.Click += delegate
            {
                menuActionStarted = true;
                ImportCsvButtonClick(button, new RoutedEventArgs());
            };
            menu.Items.Add(importItem);

            MenuItem exportItem = new MenuItem();
            exportItem.Header = "导出 CSV";
            exportItem.Click += delegate
            {
                menuActionStarted = true;
                ExportCsvButtonClick(button, new RoutedEventArgs());
            };
            menu.Items.Add(exportItem);

            menu.Closed += delegate
            {
                _isContextMenuOpen = false;
                if (!menuActionStarted && !IsMouseOver)
                {
                    BeginCollapseCountdown();
                }
            };

            button.ContextMenu = menu;
            menu.IsOpen = true;
            e.Handled = true;
        }

        private void ImportCsvButtonClick(object sender, RoutedEventArgs e)
        {
            CommitGridEdit();
            SaveNow();
            _collapseTimer.Stop();

            try
            {
                OpenFileDialog dialog = new OpenFileDialog();
                dialog.Title = "导入提示词";
                dialog.Filter = "CSV 文件 (*.csv)|*.csv|所有文件 (*.*)|*.*";
                dialog.CheckFileExists = true;
                dialog.Multiselect = false;

                bool? result = dialog.ShowDialog(this);
                if (result == true)
                {
                    ImportCsv(dialog.FileName);
                }
            }
            catch (Exception ex)
            {
                Diagnostics.LogException("ImportCsv", ex);
                ShowToast("导入失败");
            }

            if (!IsMouseOver)
            {
                BeginCollapseCountdown();
            }
        }

        private void ExportCsvButtonClick(object sender, RoutedEventArgs e)
        {
            CommitGridEdit();
            SaveNow();
            _collapseTimer.Stop();

            try
            {
                SaveFileDialog dialog = new SaveFileDialog();
                dialog.Title = "导出提示词";
                dialog.Filter = "CSV 文件 (*.csv)|*.csv";
                dialog.FileName = "提示词便签-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".csv";
                dialog.AddExtension = true;
                dialog.DefaultExt = ".csv";
                dialog.OverwritePrompt = true;

                bool? result = dialog.ShowDialog(this);
                if (result == true)
                {
                    ExportCsv(dialog.FileName);
                    ShowToast("√");
                }
            }
            catch (Exception ex)
            {
                Diagnostics.LogException("ExportCsv", ex);
                ShowToast("导出失败");
            }

            if (!IsMouseOver)
            {
                BeginCollapseCountdown();
            }
        }

        private void ImportCsv(string path)
        {
            List<PromptItem> importedItems = LoadCsvItems(path);
            if (importedItems.Count == 0)
            {
                ShowToast("没有可导入内容");
                return;
            }

            MessageBoxResult mode = MessageBox.Show(
                this,
                "是否将导入内容追加到现有表格？\n\n是：追加\n否：替换当前表格\n取消：不导入",
                "导入 CSV",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            if (mode == MessageBoxResult.Cancel)
            {
                return;
            }

            if (mode == MessageBoxResult.No)
            {
                ClearItemsWithEvents();
                _lastTouchedItem = null;
            }

            PromptItem firstImportedItem = null;
            foreach (PromptItem item in importedItems)
            {
                AddItemWithEvents(item);
                if (firstImportedItem == null)
                {
                    firstImportedItem = item;
                }
            }

            _lastTouchedItem = firstImportedItem;
            SelectPromptItem(firstImportedItem);
            SaveNow();
            ShowToast("√");
        }

        private List<PromptItem> LoadCsvItems(string path)
        {
            List<string[]> rows = ParseCsv(ReadCsvText(path));
            List<PromptItem> items = new List<PromptItem>();
            int startIndex = rows.Count > 0 && LooksLikeCsvHeader(rows[0]) ? 1 : 0;

            for (int i = startIndex; i < rows.Count; i++)
            {
                string[] row = rows[i];
                if (row == null || row.Length == 0)
                {
                    continue;
                }

                string category = "";
                string prompt = "";
                if (row.Length == 1)
                {
                    prompt = row[0] ?? "";
                }
                else
                {
                    category = row[0] ?? "";
                    prompt = row[1] ?? "";
                }

                if (string.IsNullOrWhiteSpace(category) && string.IsNullOrWhiteSpace(prompt))
                {
                    continue;
                }

                items.Add(new PromptItem
                {
                    Category = category,
                    Prompt = prompt
                });
            }

            return items;
        }

        private string ReadCsvText(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                return StripTextBom(Encoding.UTF8.GetString(bytes));
            }

            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            {
                return StripTextBom(Encoding.Unicode.GetString(bytes));
            }

            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            {
                return StripTextBom(Encoding.BigEndianUnicode.GetString(bytes));
            }

            try
            {
                return StripTextBom(new UTF8Encoding(false, true).GetString(bytes));
            }
            catch (DecoderFallbackException)
            {
                return StripTextBom(Encoding.Default.GetString(bytes));
            }
        }

        private string StripTextBom(string text)
        {
            if (!string.IsNullOrEmpty(text) && text[0] == '\ufeff')
            {
                return text.Substring(1);
            }

            return text ?? "";
        }

        private List<string[]> ParseCsv(string text)
        {
            List<string[]> rows = new List<string[]>();
            if (string.IsNullOrEmpty(text))
            {
                return rows;
            }

            List<string> fields = new List<string>();
            StringBuilder field = new StringBuilder();
            bool inQuotes = false;
            bool sawFieldContent = false;
            bool rowHasContent = false;

            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];

                if (inQuotes)
                {
                    if (ch == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"')
                        {
                            field.Append('"');
                            sawFieldContent = true;
                            rowHasContent = true;
                            i++;
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        field.Append(ch);
                        sawFieldContent = true;
                        rowHasContent = true;
                    }

                    continue;
                }

                if (ch == '"')
                {
                    inQuotes = true;
                    sawFieldContent = true;
                    rowHasContent = true;
                    continue;
                }

                if (ch == ',')
                {
                    fields.Add(field.ToString());
                    field.Length = 0;
                    sawFieldContent = false;
                    rowHasContent = true;
                    continue;
                }

                if (ch == '\r' || ch == '\n')
                {
                    fields.Add(field.ToString());
                    if (rowHasContent || fields.Count > 1 || sawFieldContent)
                    {
                        rows.Add(fields.ToArray());
                    }

                    fields.Clear();
                    field.Length = 0;
                    sawFieldContent = false;
                    rowHasContent = false;

                    if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                    {
                        i++;
                    }

                    continue;
                }

                field.Append(ch);
                sawFieldContent = true;
                rowHasContent = true;
            }

            if (rowHasContent || fields.Count > 0 || sawFieldContent)
            {
                fields.Add(field.ToString());
                rows.Add(fields.ToArray());
            }

            return rows;
        }

        private bool LooksLikeCsvHeader(string[] row)
        {
            if (row == null || row.Length < 2)
            {
                return false;
            }

            string first = NormalizeCsvHeader(row[0]);
            string second = NormalizeCsvHeader(row[1]);
            bool firstMatches = first == "类型" || first == "类别" || first == "type" || first == "category";
            bool secondMatches = second == "提示词" || second == "内容" || second == "prompt" || second == "text";
            return firstMatches && secondMatches;
        }

        private string NormalizeCsvHeader(string value)
        {
            return (value ?? "").Trim().Trim('\ufeff').ToLowerInvariant();
        }

        private void ExportCsv(string path)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine(CsvField("类型") + "," + CsvField("提示词"));
            foreach (PromptItem item in _items)
            {
                builder.AppendLine(CsvField(item.Category) + "," + CsvField(item.Prompt));
            }

            File.WriteAllText(path, builder.ToString(), new UTF8Encoding(true));
        }

        private string CsvField(string value)
        {
            string text = value ?? "";
            if (text.IndexOfAny(new char[] { '"', ',', '\r', '\n' }) >= 0)
            {
                text = "\"" + text.Replace("\"", "\"\"") + "\"";
            }

            return text;
        }

        private void CloseButtonClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private bool ApplyAutoStart(bool enabled)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (key == null)
                    {
                        return false;
                    }

                    if (enabled)
                    {
                        string exePath = System.Reflection.Assembly.GetEntryAssembly().Location;
                        key.SetValue(RunValueName, "\"" + exePath + "\"");
                    }
                    else if (key.GetValue(RunValueName) != null)
                    {
                        key.DeleteValue(RunValueName);
                    }
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private void UpdateButtons()
        {
            if (_lockButton != null)
            {
                SetButtonIcon(_lockButton, _isLocked ? "\uE72E" : "\uE785");
                _lockButton.ToolTip = _isLocked ? "当前已锁定，点击解锁" : "当前未锁定，可拖动和缩放";
            }

            if (_autoStartButton != null)
            {
                SetButtonIcon(_autoStartButton, "\uE7E8");
                _autoStartButton.ToolTip = _isAutoStart ? "关闭开机自启动" : "开启开机自启动";
                SetButtonActive(_autoStartButton, _isAutoStart);
            }

            if (_topmostButton != null)
            {
                SetButtonIcon(_topmostButton, "\uE718");
                _topmostButton.ToolTip = _isTopmost ? "取消置顶" : "置于所有窗口上方";
                SetButtonActive(_topmostButton, _isTopmost);
            }

            if (_resizeThumb != null)
            {
                _resizeThumb.Visibility = _isLocked ? Visibility.Collapsed : Visibility.Visible;
            }

            if (_resizeGlyph != null)
            {
                _resizeGlyph.Visibility = _isLocked ? Visibility.Collapsed : Visibility.Visible;
            }

            ResizeMode = (!_isExpanded || _isLocked) ? ResizeMode.NoResize : ResizeMode.CanResize;
            Topmost = _isTopmost;
        }

        private void SetButtonIcon(Button button, string glyph)
        {
            TextBlock icon = button.Content as TextBlock;
            if (icon != null)
            {
                icon.Text = glyph;
            }
        }

        private void SetButtonActive(Button button, bool active)
        {
            if (button == null)
            {
                return;
            }

            if (active)
            {
                button.Background = new SolidColorBrush(Color.FromRgb(204, 251, 241));
                button.BorderBrush = new SolidColorBrush(Color.FromRgb(20, 184, 166));
            }
            else
            {
                button.Background = new SolidColorBrush(Color.FromArgb(170, 255, 255, 248));
                button.BorderBrush = new SolidColorBrush(Color.FromRgb(232, 213, 154));
            }
        }

        private void GridPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount > 1)
            {
                _copyTimer.Stop();
                _pendingCopyCell = null;
            }
        }

        private void GridPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isEditing || e.ClickCount != 1)
            {
                return;
            }

            DataGridCell cell = FindVisualParent<DataGridCell>(e.OriginalSource as DependencyObject);
            if (cell == null)
            {
                return;
            }

            CopyCell(cell);
        }

        private void GridMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            DataGridCell cell = FindVisualParent<DataGridCell>(e.OriginalSource as DependencyObject);
            DataGridRow row = FindVisualParent<DataGridRow>(e.OriginalSource as DependencyObject);
            if (cell == null || row == null)
            {
                return;
            }

            _copyTimer.Stop();
            _pendingCopyCell = null;
            _lastTouchedItem = row.Item as PromptItem;
            _grid.CurrentCell = new DataGridCellInfo(row.Item, cell.Column);
            _grid.SelectedItem = row.Item;
            _grid.BeginEdit(e);
        }

        private void GridBeginningEdit(object sender, DataGridBeginningEditEventArgs e)
        {
            _isEditing = true;
            _collapseTimer.Stop();
            _copyTimer.Stop();
        }

        private void GridCellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(delegate
            {
                _isEditing = false;
                CommitGridEdit();
                ScheduleSave();
                if (!IsMouseOver)
                {
                    BeginCollapseCountdown();
                }
            }), DispatcherPriority.Background);
        }

        private void GridRowEditEnding(object sender, DataGridRowEditEndingEventArgs e)
        {
            ScheduleSave();
        }

        private void GridCurrentCellChanged(object sender, EventArgs e)
        {
            if (_grid != null && _grid.CurrentCell.Item is PromptItem)
            {
                _lastTouchedItem = (PromptItem)_grid.CurrentCell.Item;
            }
        }

        private void EditTextBoxPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                TextBox textBox = sender as TextBox;
                if (textBox == null)
                {
                    return;
                }

                if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
                {
                    int caret = textBox.CaretIndex;
                    string newline = Environment.NewLine;
                    textBox.Text = textBox.Text.Insert(caret, newline);
                    textBox.CaretIndex = caret + newline.Length;
                    BindingExpression shiftBinding = textBox.GetBindingExpression(TextBox.TextProperty);
                    if (shiftBinding != null)
                    {
                        shiftBinding.UpdateSource();
                    }

                    e.Handled = true;
                    ScheduleSave();
                    return;
                }

                BindingExpression binding = textBox.GetBindingExpression(TextBox.TextProperty);
                if (binding != null)
                {
                    binding.UpdateSource();
                }

                e.Handled = true;
                CommitGridEdit();
                Keyboard.ClearFocus();
                _isEditing = false;
                ScheduleSave();
                return;
            }

            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                try
                {
                    _grid.CancelEdit(DataGridEditingUnit.Cell);
                    _grid.CancelEdit(DataGridEditingUnit.Row);
                }
                catch
                {
                }

                Keyboard.ClearFocus();
                _isEditing = false;
            }
        }

        private void CopyTimerTick(object sender, EventArgs e)
        {
            _copyTimer.Stop();

            DataGridCell cell = _pendingCopyCell;
            _pendingCopyCell = null;
            if (cell == null)
            {
                return;
            }

            CopyCell(cell);
        }

        private void CopyCell(DataGridCell cell)
        {
            DataGridRow row = FindVisualParent<DataGridRow>(cell);
            if (row == null)
            {
                return;
            }

            PromptItem item = row.Item as PromptItem;
            if (item == null)
            {
                return;
            }

            _lastTouchedItem = item;
            try
            {
                _grid.CurrentCell = new DataGridCellInfo(item, cell.Column);
                _grid.SelectedItem = item;
            }
            catch (Exception ex)
            {
                Diagnostics.LogException("MarkTouchedCell", ex);
            }

            string text = GetCellText(item, cell.Column);
            PressCell(cell);
            bool copied = TrySetClipboardText(text);
            if (copied)
            {
                ShowToast("√");
            }
            else
            {
                HideToast();
            }
        }

        private bool TrySetClipboardText(string text)
        {
            string value = text ?? "";
            int lastError = 0;
            IntPtr hwnd = new WindowInteropHelper(this).Handle;

            for (int attempt = 0; attempt < 24; attempt++)
            {
                if (TrySetClipboardTextNative(value, hwnd, out lastError))
                {
                    return true;
                }

                Thread.Sleep(attempt < 4 ? 8 : 16);
            }

            Diagnostics.LogMessage("ClipboardCopy", "OpenClipboard/SetClipboardData failed after retries. LastWin32Error=" + lastError);
            return false;
        }

        private bool TrySetClipboardTextNative(string text, IntPtr owner, out int lastError)
        {
            lastError = 0;
            IntPtr memory = IntPtr.Zero;
            bool opened = false;

            try
            {
                opened = OpenClipboard(owner);
                if (!opened)
                {
                    opened = OpenClipboard(IntPtr.Zero);
                }

                if (!opened)
                {
                    lastError = Marshal.GetLastWin32Error();
                    return false;
                }

                if (!EmptyClipboard())
                {
                    lastError = Marshal.GetLastWin32Error();
                    return false;
                }

                byte[] bytes = Encoding.Unicode.GetBytes((text ?? "") + "\0");
                memory = GlobalAlloc(GmemMoveable | GmemZeroInit, new UIntPtr((uint)bytes.Length));
                if (memory == IntPtr.Zero)
                {
                    lastError = Marshal.GetLastWin32Error();
                    return false;
                }

                IntPtr target = GlobalLock(memory);
                if (target == IntPtr.Zero)
                {
                    lastError = Marshal.GetLastWin32Error();
                    return false;
                }

                Marshal.Copy(bytes, 0, target, bytes.Length);
                GlobalUnlock(memory);

                if (SetClipboardData(CfUnicodeText, memory) == IntPtr.Zero)
                {
                    lastError = Marshal.GetLastWin32Error();
                    return false;
                }

                memory = IntPtr.Zero;
                return true;
            }
            catch (Exception ex)
            {
                Diagnostics.LogException("ClipboardCopy", ex);
                return false;
            }
            finally
            {
                if (memory != IntPtr.Zero)
                {
                    GlobalFree(memory);
                }

                if (opened)
                {
                    CloseClipboard();
                }
            }
        }

        private void PressCell(DataGridCell cell)
        {
            if (cell == null)
            {
                return;
            }

            try
            {
                ScaleTransform pressed = new ScaleTransform(0.985, 0.965);
                cell.RenderTransformOrigin = new Point(0.5, 0.5);
                cell.RenderTransform = pressed;

                DispatcherTimer timer = new DispatcherTimer();
                timer.Interval = TimeSpan.FromMilliseconds(85);
                timer.Tick += delegate
                {
                    timer.Stop();
                    cell.ClearValue(UIElement.RenderTransformProperty);
                };
                timer.Start();
            }
            catch (Exception ex)
            {
                Diagnostics.LogException("PressCell", ex);
            }
        }

        private void HideToast()
        {
            try
            {
                _toastTimer.Stop();
                if (_toast != null)
                {
                    _toast.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                Diagnostics.LogException("HideToast", ex);
            }
        }

        private string GetCellText(PromptItem item, DataGridColumn column)
        {
            if (column == _categoryColumn)
            {
                return item.Category;
            }

            return item.Prompt;
        }

        private void ShowToast(string message)
        {
            if (_toast == null || _toastText == null)
            {
                return;
            }

            _toastText.Text = message;
            if (message == "√")
            {
                _toastText.FontSize = 20;
                _toastText.FontWeight = FontWeights.Bold;
                _toastText.Margin = new Thickness(12, 1, 12, 3);
            }
            else
            {
                _toastText.FontSize = 12;
                _toastText.FontWeight = FontWeights.SemiBold;
                _toastText.Margin = new Thickness(12, 6, 12, 6);
            }

            _toast.Visibility = Visibility.Visible;
            _toastTimer.Stop();
            _toastTimer.Start();
        }

        private void ToastTimerTick(object sender, EventArgs e)
        {
            _toastTimer.Stop();
            if (_toast != null)
            {
                _toast.Visibility = Visibility.Collapsed;
            }
        }

        private PromptItem GetCurrentItem()
        {
            if (_grid == null)
            {
                return _lastTouchedItem != null && _items.Contains(_lastTouchedItem) ? _lastTouchedItem : null;
            }

            PromptItem item = _grid.CurrentCell.Item as PromptItem;
            if (item != null)
            {
                return item;
            }

            item = _grid.CurrentItem as PromptItem;
            if (item != null)
            {
                return item;
            }

            item = _grid.SelectedItem as PromptItem;
            if (item != null)
            {
                return item;
            }

            if (_lastTouchedItem != null && _items.Contains(_lastTouchedItem))
            {
                return _lastTouchedItem;
            }

            if (_grid.CurrentItem != null)
            {
                return _grid.CurrentItem as PromptItem;
            }

            return null;
        }

        private void SelectPromptItem(PromptItem item)
        {
            if (item == null || _grid == null)
            {
                return;
            }

            Dispatcher.BeginInvoke(new Action(delegate
            {
                try
                {
                    if (_items.Contains(item))
                    {
                        _grid.SelectedItem = item;
                        _grid.CurrentCell = new DataGridCellInfo(item, _promptColumn);
                    }
                }
                catch (Exception ex)
                {
                    Diagnostics.LogException("SelectPromptItem", ex);
                }
            }), DispatcherPriority.ApplicationIdle);
        }

        private void AddItemWithEvents(PromptItem item)
        {
            if (item == null)
            {
                item = new PromptItem();
            }

            item.PropertyChanged += ItemPropertyChanged;
            _items.Add(item);
        }

        private void ClearItemsWithEvents()
        {
            foreach (PromptItem item in _items.ToList())
            {
                item.PropertyChanged -= ItemPropertyChanged;
            }

            _items.Clear();
        }

        private void ItemsCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
            {
                foreach (object oldItem in e.OldItems)
                {
                    PromptItem promptItem = oldItem as PromptItem;
                    if (promptItem != null)
                    {
                        promptItem.PropertyChanged -= ItemPropertyChanged;
                    }
                }
            }

            if (e.NewItems != null)
            {
                foreach (object newItem in e.NewItems)
                {
                    PromptItem promptItem = newItem as PromptItem;
                    if (promptItem != null)
                    {
                        promptItem.PropertyChanged -= ItemPropertyChanged;
                        promptItem.PropertyChanged += ItemPropertyChanged;
                    }
                }
            }

            ScheduleSave();
        }

        private void ItemPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            ScheduleSave();
        }

        private void WindowLocationChanged(object sender, EventArgs e)
        {
            if (_isLoading || _state == null)
            {
                return;
            }

            _state.Left = Left;
            _state.Top = Top;
            ScheduleSave();
        }

        private void WindowSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_isLoading || _state == null || !_isExpanded)
            {
                return;
            }

            _state.ExpandedWidth = Math.Max(MinExpandedWidth, ActualWidth);
            _state.ExpandedHeight = Math.Max(MinExpandedHeight, ActualHeight);
            ScheduleSave();
        }

        private void WindowClosing(object sender, CancelEventArgs e)
        {
            if (_hwndSource != null)
            {
                _hwndSource.RemoveHook(WindowMessageHook);
                _hwndSource = null;
            }

            CommitGridEdit();
            SaveNow();
        }

        private void CommitGridEdit()
        {
            if (_grid == null)
            {
                return;
            }

            try
            {
                _grid.CommitEdit(DataGridEditingUnit.Cell, true);
                _grid.CommitEdit(DataGridEditingUnit.Row, true);
            }
            catch
            {
            }
        }

        private void ScheduleSave()
        {
            if (_isLoading || _state == null)
            {
                return;
            }

            _saveTimer.Stop();
            _saveTimer.Start();
        }

        private void SaveTimerTick(object sender, EventArgs e)
        {
            _saveTimer.Stop();
            SaveNow();
        }

        private void SaveNow()
        {
            if (_state == null)
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(_dataDir);

                _state.Left = Left;
                _state.Top = Top;
                _state.IsLocked = _isLocked;
                _state.AutoStart = _isAutoStart;
                _state.IsTopmost = _isTopmost;
                if (_isExpanded)
                {
                    _state.ExpandedWidth = Math.Max(MinExpandedWidth, ActualWidth);
                    _state.ExpandedHeight = Math.Max(MinExpandedHeight, ActualHeight);
                }

                _state.Items = _items.ToList();

                XmlSerializer serializer = new XmlSerializer(typeof(AppState));
                using (FileStream stream = File.Create(_statePath))
                {
                    serializer.Serialize(stream, _state);
                }
            }
            catch
            {
            }
        }

        private bool IsInteractiveElement(DependencyObject source)
        {
            DependencyObject current = source;
            while (current != null)
            {
                if (current is ButtonBase || current is TextBox || current is DataGrid)
                {
                    return true;
                }

                current = GetDependencyParent(current);
            }

            return false;
        }

        private static T FindVisualParent<T>(DependencyObject source) where T : DependencyObject
        {
            DependencyObject current = source;
            while (current != null)
            {
                T typed = current as T;
                if (typed != null)
                {
                    return typed;
                }

                current = GetDependencyParent(current);
            }

            return null;
        }

        private static DependencyObject GetDependencyParent(DependencyObject source)
        {
            if (source == null)
            {
                return null;
            }

            try
            {
                if (source is Visual || source is Visual3D)
                {
                    return VisualTreeHelper.GetParent(source);
                }

                FrameworkElement element = source as FrameworkElement;
                if (element != null)
                {
                    return element.Parent;
                }

                FrameworkContentElement contentElement = source as FrameworkContentElement;
                if (contentElement != null)
                {
                    return contentElement.Parent;
                }
            }
            catch (Exception ex)
            {
                Diagnostics.LogException("VisualParentLookup", ex);
            }

            return null;
        }

        private static bool IsUsable(double value)
        {
            return !Double.IsNaN(value) && !Double.IsInfinity(value) && value > 0;
        }

        private static double Clamp(double value, double min, double max)
        {
            if (max < min)
            {
                return min;
            }

            if (value < min)
            {
                return min;
            }

            if (value > max)
            {
                return max;
            }

            return value;
        }
    }
}
