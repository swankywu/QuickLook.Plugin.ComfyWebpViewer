using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using QuickLook.Common.Helpers;

namespace QuickLook.Plugin.ComfyWebpViewer
{
    public partial class ImagePanel : UserControl, IDisposable
    {
        private const string Domain = "QuickLook.Plugin.ComfyWebpViewer";
        private const double MinScale = 0.02;
        private const double MaxScale = 32.0;

        private readonly DispatcherTimer _timer;
        private readonly List<BitmapSource> _frames = new List<BitmapSource>();
        private readonly List<int> _delays = new List<int>();

        private double _scale = 1.0;
        private int _frameIndex;
        private bool _playing = true;
        private bool _fitOnResize = true;
        private bool _disposed;

        private Point _panStart;
        private double _panStartX;
        private double _panStartY;
        private bool _panning;

        public ImagePanel()
        {
            InitializeComponent();

            _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(80) };
            _timer.Tick += OnTimerTick;

            Scroller.PreviewMouseWheel += OnPreviewMouseWheel;
            Scroller.PreviewMouseLeftButtonDown += OnPanStart;
            Scroller.PreviewMouseMove += OnPanMove;
            Scroller.PreviewMouseLeftButtonUp += OnPanEnd;
            Scroller.MouseLeave += OnPanEnd;
            Img.MouseLeftButtonDown += OnImageMouseDown;
            SizeChanged += OnSizeChanged;
            Unloaded += (s, e) => Dispose();

            Loaded += (s, e) => Focus();
        }

        // ------------------------------------------------------------------ loading

        public void ShowError(string title, string detail)
        {
            Img.Source = null;
            AnimBar.Visibility = Visibility.Collapsed;
            Placeholder.Visibility = Visibility.Visible;
            Placeholder.Text = string.IsNullOrEmpty(detail) ? title : title + "\n\n" + detail;
            InfoText.Text = title;
            MetaButton.Visibility = Visibility.Collapsed;
        }

        internal void Load(string path, WebpFile info)
        {
            InfoText.Text = ComfyMetadata.DescribeFile(info);

            var result = WebpDecoder.Decode(path, info);
            if (result.Frames.Count == 0)
            {
                ShowError(
                    T("Error_Title", "无法解码此 WebP 文件", "Cannot decode this WebP file"),
                    result.Error);
                return;
            }

            _frames.Clear();
            _frames.AddRange(result.Frames);
            _delays.Clear();
            _delays.AddRange(result.Delays);

            CheckerLayer.Visibility = info.HasAlpha ? Visibility.Visible : Visibility.Collapsed;
            Img.Source = _frames[0];
            _frameIndex = 0;
            _fitOnResize = true;

            if (_frames.Count > 1)
            {
                AnimBar.Visibility = Visibility.Visible;
                FrameSlider.Visibility = _frames.Count <= 600 ? Visibility.Visible : Visibility.Collapsed;
                FrameSlider.Maximum = _frames.Count - 1;
                FrameSlider.Value = 0;
                UpdateFrameText();

                var note = result.Truncated
                    ? T("Warn_Truncated", "（帧数过多，仅载入部分）", "(too many frames, partially loaded)")
                    : string.Empty;
                InfoText.Text = ComfyMetadata.DescribeFile(info) +
                                " · " + _frames.Count + " " + T("Label_Frames", "帧", "frames") +
                                " · " + AverageFps().ToString("0.#", CultureInfo.InvariantCulture) + " fps" + note;

                _timer.Start();
                SyncPlayButton();
            }
            else
            {
                AnimBar.Visibility = Visibility.Collapsed;
            }

            BuildMetadata(info);
            MetaButton.Visibility = _frames.Count > 0 && MetaStack.Children.Count > 0
                ? Visibility.Visible
                : Visibility.Collapsed;

            Loaded += (s, e) => Fit();
            if (IsLoaded)
                Fit();
        }

        private double AverageFps()
        {
            double total = 0;
            foreach (var d in _delays)
                total += d;

            return total <= 0 ? 12.0 : 1000.0 * _delays.Count / total;
        }

        // ---------------------------------------------------------------- metadata

        private void BuildMetadata(WebpFile info)
        {
            MetaStack.Children.Clear();

            var summary = ComfyMetadata.Build(info);

            var fileGroup = AddGroup(T("Meta_File", "文件", "File"));
            AddRow(fileGroup, T("Meta_Size", "尺寸", "Size"), info.Width + " × " + info.Height);
            AddRow(fileGroup, T("Meta_Codec", "编码", "Codec"), info.Codec);
            AddRow(fileGroup, T("Meta_FileSize", "大小", "Bytes"), ComfyMetadata.FormatSize(info.FileSize));
            if (_frames.Count > 1)
            {
                AddRow(fileGroup, T("Meta_Frames", "帧数", "Frames"), _frames.Count.ToString(CultureInfo.InvariantCulture));
                AddRow(fileGroup, "FPS", AverageFps().ToString("0.##", CultureInfo.InvariantCulture));
            }

            if (summary.Fields.Count > 0)
            {
                var group = AddGroup(T("Meta_Graph", "生成参数", "Generation"));
                foreach (var field in summary.Fields)
                    AddRow(group, field.Key, field.Value);
            }

            if (!string.IsNullOrWhiteSpace(summary.Positive))
                AddTextBlock(T("Meta_Positive", "正向提示词", "Positive prompt"), summary.Positive);

            if (!string.IsNullOrWhiteSpace(summary.Negative))
                AddTextBlock(T("Meta_Negative", "负向提示词", "Negative prompt"), summary.Negative);

            if (!string.IsNullOrWhiteSpace(summary.PromptJson))
                AddTextBlock("prompt (API)", summary.PromptJson, mono: true);

            if (!string.IsNullOrWhiteSpace(summary.WorkflowJson))
                AddTextBlock("workflow (UI)", summary.WorkflowJson, mono: true);
        }

        private StackPanel AddGroup(string header)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 4, 0, 10) };

            var label = new TextBlock
            {
                Text = header,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x7E, 0xC4, 0xFF)),
                Margin = new Thickness(0, 0, 0, 6)
            };

            panel.Children.Add(label);
            MetaStack.Children.Add(panel);
            return panel;
        }

        private void AddRow(Panel parent, string label, string value)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };

            var l = new TextBlock { Style = (Style)FindResource("MetaLabel"), Text = label };
            var v = new TextBox
            {
                Style = (Style)FindResource("MetaValue"),
                Text = value ?? string.Empty,
                MaxWidth = 250
            };

            row.Children.Add(l);
            row.Children.Add(v);
            parent.Children.Add(row);
        }

        private void AddTextBlock(string header, string text, bool mono = false)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 6, 0, 12) };

            panel.Children.Add(new TextBlock
            {
                Text = header,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x7E, 0xC4, 0xFF)),
                Margin = new Thickness(0, 0, 0, 6)
            });

            var box = new TextBox
            {
                Text = text,
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                AcceptsReturn = true,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MaxHeight = 320,
                Foreground = new SolidColorBrush(Color.FromRgb(0xDC, 0xDC, 0xDC)),
                Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1C)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x38, 0x38, 0x3C)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(6),
                FontFamily = mono ? new FontFamily("Consolas, Courier New") : FontFamily,
                FontSize = mono ? 11 : 12
            };

            panel.Children.Add(box);
            MetaStack.Children.Add(panel);
        }

        // --------------------------------------------------------------- animation

        private void OnTimerTick(object sender, EventArgs e)
        {
            if (!_playing || _frames.Count < 2 || _disposed)
                return;

            _frameIndex = (_frameIndex + 1) % _frames.Count;
            Img.Source = _frames[_frameIndex];

            if (FrameSlider.Visibility == Visibility.Visible)
                FrameSlider.Value = _frameIndex;
            else
                UpdateFrameText();
        }

        private void UpdateFrameText()
        {
            FrameText.Text = (_frameIndex + 1) + " / " + _frames.Count;
        }

        private void OnTogglePlay(object sender, RoutedEventArgs e)
        {
            if (_frames.Count < 2)
                return;

            _playing = !_playing;

            if (_playing)
            {
                _timer.Start();
            }
            else
            {
                _timer.Stop();
                Img.Source = _frames[_frameIndex];
            }

            SyncPlayButton();
        }

        private void SyncPlayButton()
        {
            PlayButton.Content = _playing ? "❚❚" : "▶";
        }

        private void OnFrameSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_frames.Count < 2 || _disposed)
                return;

            var index = (int)Math.Round(FrameSlider.Value);
            if (index < 0)
                index = 0;
            if (index >= _frames.Count)
                index = _frames.Count - 1;

            if (index == _frameIndex)
            {
                UpdateFrameText();
                return;
            }

            _frameIndex = index;
            Img.Source = _frames[index];
            UpdateFrameText();
        }

        // -------------------------------------------------------------------- zoom

        private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control || _frames.Count == 0)
                return;

            e.Handled = true;
            ZoomAt(e.GetPosition(Scroller), e.Delta > 0 ? 1.15 : 1 / 1.15);
        }

        private void OnImageMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
                ToggleFit();
        }

        private void OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_fitOnResize && _frames.Count > 0)
                Fit();
        }

        private void OnZoomIn(object sender, RoutedEventArgs e)
        {
            var center = new Point(Scroller.ViewportWidth / 2, Scroller.ViewportHeight / 2);
            ZoomAt(center, 1.25);
        }

        private void OnZoomOut(object sender, RoutedEventArgs e)
        {
            var center = new Point(Scroller.ViewportWidth / 2, Scroller.ViewportHeight / 2);
            ZoomAt(center, 1 / 1.25);
        }

        private void OnFit(object sender, RoutedEventArgs e)
        {
            _fitOnResize = true;
            Fit();
        }

        private void ToggleFit()
        {
            var source = Img.Source as BitmapSource;
            if (source == null)
                return;

            if (Math.Abs(_scale - 1.0) < 0.001)
            {
                _fitOnResize = true;
                Fit();
            }
            else
            {
                _fitOnResize = false;
                ApplyScale(1.0);
            }
        }

        public void Fit()
        {
            var source = Img.Source as BitmapSource;
            if (source == null)
                return;

            var vw = Scroller.ViewportWidth;
            var vh = Scroller.ViewportHeight;
            if (vw <= 0 || vh <= 0)
                return;

            var scale = Math.Min(vw / source.PixelWidth, vh / source.PixelHeight);
            if (scale > 4.0)
                scale = 4.0;

            ApplyScale(scale);
        }

        private void ZoomAt(Point anchor, double factor)
        {
            var source = Img.Source as BitmapSource;
            if (source == null)
                return;

            var oldScale = _scale;
            var newScale = Clamp(oldScale * factor);
            if (Math.Abs(newScale - oldScale) < 1e-6)
                return;

            var contentX = (anchor.X + Scroller.HorizontalOffset - OffsetOf(Scroller.ViewportWidth, source.PixelWidth * oldScale)) / oldScale;
            var contentY = (anchor.Y + Scroller.VerticalOffset - OffsetOf(Scroller.ViewportHeight, source.PixelHeight * oldScale)) / oldScale;

            ApplyScale(newScale);

            var newExtentW = source.PixelWidth * newScale;
            var newExtentH = source.PixelHeight * newScale;
            Scroller.ScrollToHorizontalOffset(contentX * newScale - anchor.X + OffsetOf(Scroller.ViewportWidth, newExtentW));
            Scroller.ScrollToVerticalOffset(contentY * newScale - anchor.Y + OffsetOf(Scroller.ViewportHeight, newExtentH));
            Scroller.UpdateLayout();

            _fitOnResize = false;
        }

        /// <summary>The centring margin WPF applies when the content is smaller than the viewport.</summary>
        private static double OffsetOf(double viewport, double extent)
        {
            return extent < viewport ? (viewport - extent) / 2 : 0;
        }

        private void ApplyScale(double scale)
        {
            _scale = Clamp(scale);
            Img.LayoutTransform = new ScaleTransform(_scale, _scale);
            RenderOptions.SetBitmapScalingMode(Img,
                _scale >= 1.0 ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality);
            ZoomText.Text = (_scale * 100).ToString("0", CultureInfo.InvariantCulture) + "%";
        }

        private static double Clamp(double value)
        {
            if (value < MinScale)
                return MinScale;
            if (value > MaxScale)
                return MaxScale;
            return value;
        }

        // --------------------------------------------------------------------- pan

        private void OnPanStart(object sender, MouseButtonEventArgs e)
        {
            if (_frames.Count == 0)
                return;

            _panning = true;
            _panStart = e.GetPosition(Scroller);
            _panStartX = Scroller.HorizontalOffset;
            _panStartY = Scroller.VerticalOffset;
            Scroller.CaptureMouse();
            Scroller.Cursor = Cursors.Hand;
        }

        private void OnPanMove(object sender, MouseEventArgs e)
        {
            if (!_panning)
                return;

            var p = e.GetPosition(Scroller);
            Scroller.ScrollToHorizontalOffset(_panStartX - (p.X - _panStart.X));
            Scroller.ScrollToVerticalOffset(_panStartY - (p.Y - _panStart.Y));
        }

        private void OnPanEnd(object sender, MouseEventArgs e)
        {
            if (!_panning)
                return;

            _panning = false;
            Scroller.ReleaseMouseCapture();
            Scroller.Cursor = Cursors.Arrow;
        }

        // ------------------------------------------------------------------ drawer

        private void OnToggleDrawer(object sender, RoutedEventArgs e)
        {
            Drawer.Visibility = Drawer.Visibility == Visibility.Visible
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        private void OnCloseDrawer(object sender, RoutedEventArgs e)
        {
            Drawer.Visibility = Visibility.Collapsed;
        }

        // ------------------------------------------------------------------ helpers

        private static string T(string id, string zh, string en)
        {
            return TranslationHelper.Get(id, null, null, en, Domain);
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _timer.Stop();
            _frames.Clear();
            Img.Source = null;
        }
    }
}
