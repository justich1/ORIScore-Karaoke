using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

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

    public MainWindow()
    {
        InitializeComponent();

        _timer.Tick += (_, _) => RefreshPlayback();
        _timer.Start();

        RenderEmpty("Karaoke");
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

        PrevLineText.Text = lineIndex > 0 ? _song.Lines[lineIndex - 1].Text : "";
        NextLineText.Text = lineIndex + 1 < _song.Lines.Count ? _song.Lines[lineIndex + 1].Text : "";

        RenderWords(_song.Lines[lineIndex], karaokePos);
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
        PrevLineText.Text = "";
        NextLineText.Text = "";
        WordsPanel.Children.Clear();

        WordsPanel.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 58,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            TextAlignment = TextAlignment.Center
        });
    }

    private void RenderWords(KaraokeLine line, int posMs)
    {
        WordsPanel.Children.Clear();

        if (line.Words.Count == 0)
        {
            WordsPanel.Children.Add(MakeWord(line.Text, false, true));
            return;
        }

        for (int i = 0; i < line.Words.Count; i++)
        {
            var w = line.Words[i];
            int start = w.TimeMs;
            int end = w.EndMs > w.TimeMs
                ? w.EndMs
                : i + 1 < line.Words.Count ? line.Words[i + 1].TimeMs : Math.Max(line.EndMs, w.TimeMs + 300);

            bool now = posMs >= start && posMs < end;
            bool past = posMs >= end;

            WordsPanel.Children.Add(MakeWord(w.Text, past, now));
        }
    }

    private static TextBlock MakeWord(string text, bool past, bool now)
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
}
