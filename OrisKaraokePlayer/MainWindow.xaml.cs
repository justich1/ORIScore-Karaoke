using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.IO;
using System.Text.Json;
using System.ComponentModel;

namespace OrisKaraokePlayerWpf;

public partial class MainWindow : Window
{
    private KaraokeSong? _song;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(35) };
    private bool _mediaReady;
    private bool _draggingSeek;
    private bool _isFullscreen;
    private bool _playWhenMediaOpens;
    private bool _isPlaying;
    private WindowStyle _oldStyle;
    private WindowState _oldState;
    private ResizeMode _oldResize;
    private int _offsetMs;
    private bool _smoothLyricsScroll;
    private const double SmoothLineFontSize = 60;

    public MainWindow()
    {
        InitializeComponent();

        Loaded += (_, _) => LoadWindowState();

        _timer.Tick += (_, _) => RefreshPlayback();
        _timer.Start();

        RenderEmpty("Karaoke");
    }

    private static readonly string WindowSettingsDir =
    Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ORIScore",
        "KaraokePlayer"
    );

    private static readonly string WindowSettingsFile =
        Path.Combine(WindowSettingsDir, "window.json");

    private sealed class SavedWindowState
    {
        public double Width { get; set; }
        public double Height { get; set; }
        public double Left { get; set; }
        public double Top { get; set; }
        public bool Maximized { get; set; }
        public bool SmoothLyricsScroll { get; set; }
    }

    private void OpenJson_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "ORIScore Karaoke (*.ock)|*.ock|ORIS karaoke JSON (*.karaoke.json;*.json)|*.karaoke.json;*.json|Vše (*.*)|*.*",
            Title = "Otevřít karaoke soubor"
        };

        if (dlg.ShowDialog(this) != true) return;

        LoadSongFile(dlg.FileName, autoPlay: true);
    }

    public void LoadSongFile(string path, bool autoPlay)
    {
        try
        {
            Player.Stop();
            _isPlaying = false;
            Player.Tag = "paused";
            _mediaReady = false;

            _song = KaraokeSong.Load(path);

            TitleText.Text = string.IsNullOrWhiteSpace(_song.Title)
                ? System.IO.Path.GetFileNameWithoutExtension(path)
                : _song.Title;

            ArtistText.Text = _song.Artist;
            _playWhenMediaOpens = autoPlay;

            if (_song.AudioPath == null)
            {
                RenderEmpty("Audio k souboru nenalezeno");
                MessageBox.Show(this, "Audio k souboru nebylo nalezeno. Vyber ho ručně.", "Chybí audio",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            LoadAudio(_song.AudioPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Chyba načtení", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void PickAudio_Click(object sender, RoutedEventArgs e)
    {
        PickAudio();
    }

    private bool PickAudio()
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Audio (*.mp3;*.wav;*.m4a;*.ogg;*.wma)|*.mp3;*.wav;*.m4a;*.ogg;*.wma|Vše (*.*)|*.*",
            Title = "Vybrat audio"
        };

        if (dlg.ShowDialog(this) != true) return false;

        if (_song != null)
        {
            _song.AudioPath = System.IO.Path.GetFullPath(dlg.FileName);
            _song.Audio = System.IO.Path.GetFileName(dlg.FileName);
        }

        LoadAudio(System.IO.Path.GetFullPath(dlg.FileName));
        return true;
    }

    private void LoadAudio(string path)
    {
        _mediaReady = false;
        Player.Stop();

        string fullPath = System.IO.Path.GetFullPath(path);
        Player.Source = new Uri(fullPath, UriKind.Absolute);
        Player.Volume = VolumeSlider.Value;
        Player.Play();
        if (!_playWhenMediaOpens)
            Player.Pause();

        ArtistText.Text = (_song?.Artist ?? "") + "    audio: " + System.IO.Path.GetFileName(path);
    }

    private void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (!_mediaReady)
        {
            if (_song?.AudioPath != null)
                LoadAudio(_song.AudioPath);
            else if (!PickAudio())
                return;
        }

        if (_isPlaying)
        {
            Player.Pause();
            _isPlaying = false;
            Player.Tag = "paused";
        }
        else
        {
            Player.Play();
            _isPlaying = true;
            Player.Tag = "playing";
        }
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        Player.Stop();
        _isPlaying = false;
        if (_playWhenMediaOpens)
        {
            Player.Play();
            _isPlaying = true;
            Player.Tag = "playing";
            _playWhenMediaOpens = false;
        }
        else
        {
            _isPlaying = false;
            Player.Tag = "paused";
        }

        RefreshPlayback();
    }

    private void Fullscreen_Click(object sender, RoutedEventArgs e)
    {
        ToggleFullscreen();
    }

    private void ToggleFullscreen()
    {
        if (!_isFullscreen)
        {
            _oldStyle = WindowStyle;
            _oldState = WindowState;
            _oldResize = ResizeMode;

            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Maximized;
            _isFullscreen = true;
        }
        else
        {
            WindowStyle = _oldStyle;
            ResizeMode = _oldResize;
            WindowState = _oldState;
            _isFullscreen = false;
        }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space)
        {
            PlayPause_Click(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && _isFullscreen)
        {
            ToggleFullscreen();
            e.Handled = true;
        }
        else if (e.Key == Key.F11)
        {
            ToggleFullscreen();
            e.Handled = true;
        }
    }

    private void OffsetBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (int.TryParse(OffsetBox.Text, out int v))
            _offsetMs = v;
    }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (Player != null)
            Player.Volume = VolumeSlider.Value;
    }

    private void SmoothScrollCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        _smoothLyricsScroll = SmoothScrollCheckBox.IsChecked == true;
        UpdateLyricsModeVisibility();
    }

    private void UpdateLyricsModeVisibility()
    {
        if (StaticLyricsHost == null || SmoothLyricsHost == null)
            return;

        StaticLyricsHost.Visibility = _smoothLyricsScroll ? Visibility.Collapsed : Visibility.Visible;
        SmoothLyricsHost.Visibility = _smoothLyricsScroll ? Visibility.Visible : Visibility.Collapsed;

        if (!_smoothLyricsScroll)
            ResetLyricsScroll();
    }

    private void Player_MediaOpened(object sender, RoutedEventArgs e)
    {
        _mediaReady = true;

        if (Player.NaturalDuration.HasTimeSpan)
            SeekSlider.Maximum = Math.Max(1, Player.NaturalDuration.TimeSpan.TotalMilliseconds);

        Player.Tag = "paused";
        RefreshPlayback();
    }

    private void Player_MediaEnded(object sender, RoutedEventArgs e)
    {
        _isPlaying = false;
        Player.Tag = "paused";
        Player.Stop();
        // Důležité: nespouštíme další skladbu automaticky.
        RefreshPlayback();
    }

    private void Player_MediaFailed(object sender, ExceptionRoutedEventArgs e)
    {
        _mediaReady = false;
        RenderEmpty("Audio nejde přehrát");
        MessageBox.Show(this, e.ErrorException.Message, "Chyba audia", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private void SeekSlider_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        _draggingSeek = true;
    }

    private void SeekSlider_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        _draggingSeek = false;
        Player.Position = TimeSpan.FromMilliseconds(SeekSlider.Value);
        RefreshPlayback();
    }

    private void SeekSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_draggingSeek)
            TimeText.Text = $"{FormatTime((int)SeekSlider.Value)} / {FormatTime(DurationMs())}";
    }

    private void RefreshPlayback()
    {
        int pos = PositionMs();
        int dur = DurationMs();

        if (!_draggingSeek)
        {
            SeekSlider.Maximum = Math.Max(1, dur);
            SeekSlider.Value = Math.Clamp(pos, 0, (int)SeekSlider.Maximum);
        }

        TimeText.Text = $"{FormatTime(pos)} / {FormatTime(dur)}";

        if (_song == null)
        {
            RenderEmpty("Karaoke");
            return;
        }

        int karaokePos = pos + _offsetMs;
        int lineIndex = _song.FindLineIndexAt(karaokePos);

        if (lineIndex < 0)
        {
            RenderEmpty("Tahle skladba nemá text");
            return;
        }

        if (_smoothLyricsScroll)
        {
            RenderSmoothLyrics(lineIndex, karaokePos);
            ApplyLyricsScroll(lineIndex, karaokePos);
        }
        else
        {
            PrevLineText.Text = lineIndex > 0 ? _song.Lines[lineIndex - 1].Text : "";
            NextLineText.Text = lineIndex + 1 < _song.Lines.Count ? _song.Lines[lineIndex + 1].Text : "";

            RenderWords(_song.Lines[lineIndex], karaokePos);
            ResetLyricsScroll();
        }
    }

    private void ApplyLyricsScroll(int lineIndex, int posMs)
    {
        if (!_smoothLyricsScroll
            || _song == null
            || lineIndex < 0
            || lineIndex >= _song.Lines.Count
            || LyricsViewport.ActualHeight <= 0)
        {
            ResetLyricsScroll();
            return;
        }

        KaraokeLine line = _song.Lines[lineIndex];

        int start = line.StartMs;
        int nextStart = lineIndex + 1 < _song.Lines.Count
            ? _song.Lines[lineIndex + 1].StartMs
            : Math.Max(line.EndMs, line.StartMs + 4000);

        int span = Math.Max(500, nextStart - start);
        double progress = Math.Clamp((posMs - start) / (double)span, 0.0, 1.0);

        // V plynulém režimu mají všechny tři řádky stejnou výšku i stejný font.
        // Díky tomu odjíždějící řádek při přechodu nezmění velikost.
        double rowCenterDistance = LyricsViewport.ActualHeight / 3.0;
        LyricsMoveTransform.Y = rowCenterDistance * (0.5 - progress);
    }

    private void ResetLyricsScroll()
    {
        if (LyricsMoveTransform != null)
            LyricsMoveTransform.Y = 0;
    }

    private int PositionMs()
    {
        if (!_mediaReady) return 0;
        return (int)Player.Position.TotalMilliseconds;
    }

    private int DurationMs()
    {
        if (!_mediaReady || !Player.NaturalDuration.HasTimeSpan) return 0;
        return (int)Player.NaturalDuration.TimeSpan.TotalMilliseconds;
    }

    private void RenderEmpty(string text)
    {
        ResetLyricsScroll();
        PrevLineText.Text = "";
        NextLineText.Text = "";
        WordsPanel.Children.Clear();
        SmoothPrevPanel.Children.Clear();
        SmoothCurrentPanel.Children.Clear();
        SmoothNextPanel.Children.Clear();

        var block = new TextBlock
        {
            Text = text,
            FontSize = 58,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };

        if (_smoothLyricsScroll)
            SmoothCurrentPanel.Children.Add(block);
        else
            WordsPanel.Children.Add(block);
    }

    private void RenderSmoothLyrics(int lineIndex, int posMs)
    {
        SmoothPrevPanel.Children.Clear();
        SmoothCurrentPanel.Children.Clear();
        SmoothNextPanel.Children.Clear();

        if (_song == null)
            return;

        double maxWidth = Math.Max(240, LyricsViewport.ActualWidth - 140);
        SmoothPrevPanel.MaxWidth = maxWidth;
        SmoothCurrentPanel.MaxWidth = maxWidth;
        SmoothNextPanel.MaxWidth = maxWidth;

        if (lineIndex > 0)
        {
            AddSmoothPlainLine(
                SmoothPrevPanel,
                _song.Lines[lineIndex - 1].Text,
                Brushes.DeepSkyBlue,
                0.70,
                maxWidth);
        }

        RenderSmoothCurrentWords(_song.Lines[lineIndex], posMs, maxWidth);

        if (lineIndex + 1 < _song.Lines.Count)
        {
            AddSmoothPlainLine(
                SmoothNextPanel,
                _song.Lines[lineIndex + 1].Text,
                Brushes.WhiteSmoke,
                0.95,
                maxWidth);
        }
    }

    private static void AddSmoothPlainLine(Panel target, string text, Brush foreground, double opacity, double maxWidth)
    {
        target.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(text) ? " " : text,
            FontSize = SmoothLineFontSize,
            FontWeight = FontWeights.Bold,
            Foreground = foreground,
            Opacity = opacity,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = maxWidth,
            Margin = new Thickness(4)
        });
    }

    private void RenderSmoothCurrentWords(KaraokeLine line, int posMs, double maxWidth)
    {
        string text = line.Text ?? "";
        if (string.IsNullOrWhiteSpace(text) && line.Words.Count > 0)
            text = string.Join(" ", line.Words.Select(w => w.Text));

        if (line.Words.Count == 0)
        {
            AddSmoothPlainLine(SmoothCurrentPanel, text.Length > 0 ? text : " ", Brushes.WhiteSmoke, 1.0, maxWidth);
            return;
        }

        var validWords = line.Words
            .Where(w => w.CharLength > 0
                && w.CharStart >= 0
                && w.CharStart < text.Length)
            .OrderBy(w => w.CharStart)
            .ThenBy(w => w.Index)
            .ToList();

        if (validWords.Count == 0)
        {
            // Starý/legacy JSON necháme bez zásahu do původního legacy vykreslení.
            // V plynulém režimu ho raději ukážeme jako pevný celý řádek, aby neskákal.
            AddSmoothPlainLine(SmoothCurrentPanel, text.Length > 0 ? text : " ", Brushes.WhiteSmoke, 1.0, maxWidth);
            return;
        }

        TextBlock block = MakeLineTextBlock("", SmoothLineFontSize, maxWidth);
        bool hasCurrentPart = false;
        int cursor = 0;

        foreach (var w in validWords)
        {
            int start = Math.Clamp(w.CharStart, 0, text.Length);
            int length = Math.Clamp(w.CharLength, 0, text.Length - start);

            if (length <= 0)
                continue;

            if (start < cursor)
            {
                int overlap = cursor - start;
                if (overlap >= length)
                    continue;

                start = cursor;
                length -= overlap;
            }

            if (start > cursor)
                AddRun(block, text[cursor..start], past: false, now: false, fixedFontSize: true);

            int end = WordEndMs(line, w);
            bool now = posMs >= w.TimeMs && posMs < end;
            bool past = posMs >= end;
            if (now) hasCurrentPart = true;

            AddRun(block, text.Substring(start, length), past, now, fixedFontSize: true);
            cursor = start + length;
        }

        if (cursor < text.Length)
            AddRun(block, text[cursor..], past: false, now: false, fixedFontSize: true);

        if (hasCurrentPart)
        {
            block.Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Gold,
                BlurRadius = 22,
                ShadowDepth = 0,
                Opacity = 0.55
            };
        }

        SmoothCurrentPanel.Children.Add(block);
    }

    private void RenderWords(KaraokeLine line, int posMs)
    {
        WordsPanel.Children.Clear();

        string text = line.Text ?? "";
        if (string.IsNullOrWhiteSpace(text) && line.Words.Count > 0)
            text = string.Join(" ", line.Words.Select(w => w.Text));

        if (line.Words.Count == 0)
        {
            WordsPanel.Children.Add(MakeLineTextBlock(text.Length > 0 ? text : " "));
            return;
        }

        var validWords = line.Words
            .Where(w => w.CharLength > 0
                && w.CharStart >= 0
                && w.CharStart < text.Length)
            .OrderBy(w => w.CharStart)
            .ThenBy(w => w.Index)
            .ToList();

        if (validWords.Count == 0)
        {
            // Nouzový fallback pro staré JSONy bez CharStart/CharLength.
            // Nové JSONy z konvertoru mají pozice a používají větev níže.
            RenderLegacyWords(line, posMs);
            return;
        }

        TextBlock block = MakeLineTextBlock("");
        bool hasCurrentPart = false;
        int cursor = 0;

        foreach (var w in validWords)
        {
            int start = Math.Clamp(w.CharStart, 0, text.Length);
            int length = Math.Clamp(w.CharLength, 0, text.Length - start);

            if (length <= 0)
                continue;

            // Kdyby se části překryly, raději neposuneme text zpět.
            if (start < cursor)
            {
                int overlap = cursor - start;
                if (overlap >= length)
                    continue;

                start = cursor;
                length -= overlap;
            }

            if (start > cursor)
                AddRun(block, text[cursor..start], past: false, now: false, fixedFontSize: _smoothLyricsScroll);

            int end = WordEndMs(line, w);
            bool now = posMs >= w.TimeMs && posMs < end;
            bool past = posMs >= end;
            if (now) hasCurrentPart = true;

            AddRun(block, text.Substring(start, length), past, now, fixedFontSize: _smoothLyricsScroll);
            cursor = start + length;
        }

        if (cursor < text.Length)
            AddRun(block, text[cursor..], past: false, now: false, fixedFontSize: _smoothLyricsScroll);

        if (hasCurrentPart)
        {
            block.Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Gold,
                BlurRadius = 22,
                ShadowDepth = 0,
                Opacity = 0.55
            };
        }

        WordsPanel.Children.Add(block);
    }

    private static int WordEndMs(KaraokeLine line, KaraokeWord word)
    {
        if (word.EndMs > word.TimeMs)
            return word.EndMs;

        int index = line.Words.IndexOf(word);
        if (index >= 0 && index + 1 < line.Words.Count)
            return line.Words[index + 1].TimeMs;

        return Math.Max(line.EndMs, word.TimeMs + 300);
    }

    private static TextBlock MakeLineTextBlock(string text, double fontSize = 60, double maxWidth = 0)
    {
        var block = new TextBlock
        {
            Text = text,
            FontSize = fontSize,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.WhiteSmoke,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(4)
        };

        if (maxWidth > 0)
            block.MaxWidth = maxWidth;

        return block;
    }

    private static void AddRun(TextBlock block, string text, bool past, bool now, bool fixedFontSize = false)
    {
        if (string.IsNullOrEmpty(text))
            return;

        // V plynulém posunu necháváme všechny části řádku stejně velké.
        // Jinak Viewbox při zvýraznění slabiky přepočítá měřítko a celý text cukne.
        // Legacy režim používá vlastní MakeLegacyWord a tímhle se nemění.
        block.Inlines.Add(new Run(text)
        {
            FontSize = fixedFontSize ? 60 : now ? 66 : 60,
            FontWeight = FontWeights.Bold,
            Foreground = now ? Brushes.Gold : past ? Brushes.DeepSkyBlue : Brushes.WhiteSmoke
        });
    }

    private void RenderLegacyWords(KaraokeLine line, int posMs)
    {
        for (int i = 0; i < line.Words.Count; i++)
        {
            var w = line.Words[i];
            int end = WordEndMs(line, w);
            bool now = posMs >= w.TimeMs && posMs < end;
            bool past = posMs >= end;

            WordsPanel.Children.Add(MakeLegacyWord(w.Text, past, now));
        }
    }

    private static TextBlock MakeLegacyWord(string text, bool past, bool now)
    {
        return new TextBlock
        {
            Text = text + " ",
            FontSize = now ? 62 : 56,
            FontWeight = FontWeights.Bold,
            Foreground = now ? Brushes.Gold : past ? Brushes.DeepSkyBlue : Brushes.WhiteSmoke,
            Margin = new Thickness(4),
            Effect = now
                ? new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Colors.Gold,
                    BlurRadius = 22,
                    ShadowDepth = 0,
                    Opacity = 0.65
                }
                : null
        };
    }

    private static string FormatTime(int ms)
    {
        var t = TimeSpan.FromMilliseconds(Math.Max(0, ms));
        return $"{(int)t.TotalMinutes:00}:{t.Seconds:00}";
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        SaveWindowState();
        base.OnClosing(e);
    }

    private void LoadWindowState()
    {
        try
        {
            if (!File.Exists(WindowSettingsFile))
                return;

            string json = File.ReadAllText(WindowSettingsFile);
            var s = JsonSerializer.Deserialize<SavedWindowState>(json);

            if (s == null)
                return;

            if (s.Width >= MinWidth && s.Height >= MinHeight)
            {
                Width = s.Width;
                Height = s.Height;
            }

            if (IsWindowPositionVisible(s.Left, s.Top, s.Width, s.Height))
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = s.Left;
                Top = s.Top;
            }

            _smoothLyricsScroll = s.SmoothLyricsScroll;
            SmoothScrollCheckBox.IsChecked = _smoothLyricsScroll;
            UpdateLyricsModeVisibility();

            if (s.Maximized)
                WindowState = WindowState.Maximized;
        }
        catch
        {
            // Ignorovat poškozené nastavení.
        }
    }

    private void SaveWindowState()
    {
        try
        {
            Directory.CreateDirectory(WindowSettingsDir);

            var bounds = WindowState == WindowState.Normal
                ? new Rect(Left, Top, Width, Height)
                : RestoreBounds;

            var s = new SavedWindowState
            {
                Width = bounds.Width,
                Height = bounds.Height,
                Left = bounds.Left,
                Top = bounds.Top,
                Maximized = WindowState == WindowState.Maximized,
                SmoothLyricsScroll = SmoothScrollCheckBox.IsChecked == true
            };

            string json = JsonSerializer.Serialize(s, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            File.WriteAllText(WindowSettingsFile, json);
        }
        catch
        {
            // Ukládání nesmí shodit přehrávač.
        }
    }

    private static bool IsWindowPositionVisible(double left, double top, double width, double height)
    {
        if (double.IsNaN(left) || double.IsNaN(top) || width <= 0 || height <= 0)
            return false;

        Rect windowRect = new(left, top, width, height);

        Rect screenRect = new(
            SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth,
            SystemParameters.VirtualScreenHeight
        );

        return screenRect.IntersectsWith(windowRect);
    }
}
