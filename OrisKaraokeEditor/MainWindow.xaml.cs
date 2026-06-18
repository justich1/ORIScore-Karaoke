using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace OrisKaraokeEditorWpf;

public partial class MainWindow : Window
{
    private KaraokeSong _song = new();
    private string _savePath = "";
    private int _tapIndex;
    private int _offsetMs;
    private bool _mediaReady;
    private bool _draggingSeek;
    private bool _isDirty;
    private bool _suppressDirtyTracking;

    private static readonly string WindowSettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ORIScore",
        "KaraokeEditor"
    );

    private static readonly string WindowSettingsFile = Path.Combine(WindowSettingsDir, "window.json");

    private sealed class SavedWindowState
    {
        public double Width { get; set; }
        public double Height { get; set; }
        public double Left { get; set; }
        public double Top { get; set; }
        public bool Maximized { get; set; }
    }

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(35) };
    private readonly ObservableCollection<WordRow> _wordRows = new();

    public MainWindow()
    {
        InitializeComponent();

        Loaded += (_, _) => LoadWindowState();

        TitleBox.TextChanged += (_, _) => MarkDirty();
        LyricsBox.TextChanged += (_, _) => MarkDirty();

        WordsGrid.ItemsSource = _wordRows;

        _timer.Tick += (_, _) => RefreshUi();
        _timer.Start();

        RebuildPreview();
        UpdateWindowTitle();
    }

    private void NewFromText_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDiscardUnsavedChanges())
            return;

        BuildFromText();
        _savePath = "";
        MarkDirty();
    }

    private void BuildFromText_Click(object sender, RoutedEventArgs e)
    {
        BuildFromText();
    }

    private void BuildFromText()
    {
        string title = string.IsNullOrWhiteSpace(TitleBox.Text) ? "Nová karaoke skladba" : TitleBox.Text;
        string audioName = _song.AudioPath != null ? Path.GetFileName(_song.AudioPath) : _song.Audio;

        _song = KaraokeSong.FromPlainText(title, "", audioName, LyricsBox.Text);
        _tapIndex = 0;

        if (!string.IsNullOrWhiteSpace(audioName))
        {
            _song.Audio = audioName;
        }

        TitleText.Text = _song.Title;
        RebuildPreview();
        MarkDirty();
    }

    private void InsertSample_Click(object sender, RoutedEventArgs e)
    {
        TitleBox.Text = "Ukázková skladba";
        LyricsBox.Text =
            "První řádek karaoke textu\n" +
            "Druhý řádek bude následovat\n" +
            "Třetí řádek je jen na test";
    }

    private void OpenJson_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDiscardUnsavedChanges())
            return;

        var dlg = new OpenFileDialog
        {
            Filter = "ORIScore Karaoke (*.ock)|*.ock|ORIS karaoke JSON (*.karaoke.json;*.json)|*.karaoke.json;*.json|Vše (*.*)|*.*",
            Title = "Otevřít karaoke soubor"
        };

        if (dlg.ShowDialog(this) != true) return;

        LoadSongFile(dlg.FileName);
    }

    public void LoadSongFile(string path)
    {
        try
        {
            _suppressDirtyTracking = true;

            _song = KaraokeSong.Load(path);
            _savePath = path;
            _tapIndex = FirstUntimedWordIndex();

            TitleBox.Text = _song.Title;
            TitleText.Text = _song.Title;
            LyricsBox.Text = string.Join(Environment.NewLine, _song.Lines.Select(l => l.Text));

            if (_song.AudioPath != null)
                LoadAudio(_song.AudioPath);

            RebuildPreview();
            SetDirty(false);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Chyba načtení", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _suppressDirtyTracking = false;
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

        _song.AudioPath = dlg.FileName;
        _song.Audio = Path.GetFileName(dlg.FileName);
        LoadAudio(dlg.FileName);
        MarkDirty();
        return true;
    }

    private void LoadAudio(string path)
    {
        _mediaReady = false;
        Player.Stop();
        Player.Source = new Uri(path, UriKind.Absolute);
        Player.Play();
        Player.Pause();

        AudioText.Text = "Audio: " + Path.GetFileName(path);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        Save(false);
    }

    private void SaveAs_Click(object sender, RoutedEventArgs e)
    {
        Save(true);
    }

    private bool Save(bool saveAs)
    {
        if (!ConfirmLyricsTextForSave())
            return false;

        string title = string.IsNullOrWhiteSpace(TitleBox.Text) ? "karaoke" : TitleBox.Text.Trim();
        _song.Title = title;
        TitleText.Text = title;

        string path = _savePath;

        if (saveAs || string.IsNullOrWhiteSpace(path))
        {
            var dlg = new SaveFileDialog
            {
                Filter = "ORIScore Karaoke (*.ock)|*.ock|ORIS karaoke JSON (*.karaoke.json)|*.karaoke.json|JSON (*.json)|*.json",
                DefaultExt = ".ock",
                AddExtension = true,
                FileName = MakeSafeFileName(_song.Title) + ".ock"
            };

            if (dlg.ShowDialog(this) != true) return false;
            path = dlg.FileName;
        }

        try
        {
            FinalizeEndTimes();
            _song.Save(path);
            _savePath = path;
            SetDirty(false);
            MessageBox.Show(this, "Uloženo.", "OK", MessageBoxButton.OK, MessageBoxImage.Information);
            RebuildPreview();
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Chyba uložení", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private void ResetTimes_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "Smazat všechny časy?", "Reset", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        foreach (var w in _song.AllWords())
        {
            w.Word.TimeMs = 0;
            w.Word.EndMs = 0;
        }

        foreach (var l in _song.Lines)
        {
            l.StartMs = 0;
            l.EndMs = 0;
        }

        _tapIndex = 0;
        RebuildPreview();
        MarkDirty();
    }

    private void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        TogglePlay();
    }

    private void TogglePlay()
    {
        if (!_mediaReady)
        {
            if (_song.AudioPath != null)
                LoadAudio(_song.AudioPath);
            else if (!PickAudio())
                return;
        }

        bool playing = Equals(Player.Tag, "playing");
        if (playing)
        {
            Player.Pause();
            Player.Tag = "paused";
        }
        else
        {
            Player.Play();
            Player.Tag = "playing";
        }
    }

    private void Stamp_Click(object sender, RoutedEventArgs e)
    {
        StampNextWord();
    }

    private void StepBack_Click(object sender, RoutedEventArgs e)
    {
        StepBack();
    }

    private void SkipWord_Click(object sender, RoutedEventArgs e)
    {
        if (_tapIndex < _song.WordCount)
            _tapIndex++;
        RebuildPreview();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Když je kurzor v textovém poli, necháme psát normálně.
        if (Keyboard.FocusedElement is TextBox)
            return;

        if (e.Key == Key.Space)
        {
            StampNextWord();
            e.Handled = true;
        }
        else if (e.Key == Key.Back)
        {
            StepBack();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            TogglePlay();
            e.Handled = true;
        }
    }

    private void StampNextWord()
    {
        if (_song.WordCount == 0)
        {
            BuildFromText();
            if (_song.WordCount == 0) return;
        }

        if (_tapIndex >= _song.WordCount)
            return;

        int now = PositionMs() + _offsetMs;
        var current = _song.WordAtFlatIndex(_tapIndex);
        if (current == null) return;

        current.Word.TimeMs = now;
        current.Word.EndMs = now + 300;

        if (_tapIndex > 0)
        {
            var previous = _song.WordAtFlatIndex(_tapIndex - 1);
            if (previous != null)
                previous.Word.EndMs = now;
        }

        _tapIndex++;
        _song.RecalculateLineTimes();
        RebuildPreview();
        MarkDirty();
    }

    private void StepBack()
    {
        if (_tapIndex <= 0) return;

        _tapIndex--;
        var w = _song.WordAtFlatIndex(_tapIndex);
        if (w != null)
        {
            w.Word.TimeMs = 0;
            w.Word.EndMs = 0;
        }

        var prev = _song.WordAtFlatIndex(_tapIndex - 1);
        if (prev != null)
            prev.Word.EndMs = prev.Word.TimeMs + 300;

        _song.RecalculateLineTimes();
        RebuildPreview();
        MarkDirty();
    }

    private bool ConfirmLyricsTextForSave()
    {
        if (!LyricsTextDiffersFromSong())
            return true;

        var result = MessageBox.Show(
            this,
            "Text skladby vlevo byl změněn, ale slova/časy ještě nejsou přepočítané.\n\n" +
            "Vytvořit slova z aktuálního textu před uložením?\n\n" +
            "Ano = použít aktuální text a začít časování od něj.\n" +
            "Ne = uložit aktuální časovanou skladbu bez změn v levém textu.\n" +
            "Storno = neukládat.",
            "Text skladby se změnil",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning);

        if (result == MessageBoxResult.Cancel)
            return false;

        if (result == MessageBoxResult.Yes)
            BuildFromText();

        return true;
    }

    private bool LyricsTextDiffersFromSong()
    {
        string editorText = NormalizeLyricsText(LyricsBox.Text);
        string songText = NormalizeLyricsText(string.Join(Environment.NewLine, _song.Lines.Select(l => l.Text)));
        return !string.Equals(editorText, songText, StringComparison.Ordinal);
    }

    private static string NormalizeLyricsText(string value)
    {
        return (value ?? "")
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Trim();
    }

    private void FinalizeEndTimes()
    {
        var all = _song.AllWords().ToList();

        for (int i = 0; i < all.Count; i++)
        {
            var w = all[i].Word;

            if (w.TimeMs <= 0)
                continue;

            if (i + 1 < all.Count && all[i + 1].Word.TimeMs > 0)
                w.EndMs = all[i + 1].Word.TimeMs;
            else if (w.EndMs <= w.TimeMs)
                w.EndMs = w.TimeMs + 400;
        }

        _song.RecalculateLineTimes();
    }

    private int FirstUntimedWordIndex()
    {
        int i = 0;
        foreach (var w in _song.AllWords())
        {
            if (w.Word.TimeMs <= 0) return i;
            i++;
        }
        return i;
    }

    private void OffsetBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (int.TryParse(OffsetBox.Text, out int v))
            _offsetMs = v;
    }

    private void Player_MediaOpened(object sender, RoutedEventArgs e)
    {
        _mediaReady = true;

        if (Player.NaturalDuration.HasTimeSpan)
            SeekSlider.Maximum = Math.Max(1, Player.NaturalDuration.TimeSpan.TotalMilliseconds);

        Player.Tag = "paused";
        RefreshUi();
    }

    private void Player_MediaEnded(object sender, RoutedEventArgs e)
    {
        Player.Tag = "paused";
        Player.Stop();
        RefreshUi();
    }

    private void Player_MediaFailed(object sender, ExceptionRoutedEventArgs e)
    {
        _mediaReady = false;
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
        RefreshUi();
    }

    private void SeekSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_draggingSeek)
            TimeText.Text = $"{FormatTime((int)SeekSlider.Value)} / {FormatTime(DurationMs())}";
    }

    private void RefreshUi()
    {
        int pos = PositionMs();
        int dur = DurationMs();

        if (!_draggingSeek)
        {
            SeekSlider.Maximum = Math.Max(1, dur);
            SeekSlider.Value = Math.Clamp(pos, 0, (int)SeekSlider.Maximum);
        }

        TimeText.Text = $"{FormatTime(pos)} / {FormatTime(dur)}";
    }

    private void RebuildPreview()
    {
        _wordRows.Clear();

        int flat = 0;
        foreach (var wr in _song.AllWords())
        {
            _wordRows.Add(new WordRow
            {
                FlatIndex = flat,
                LineIndex = wr.LineIndex,
                Text = wr.Word.Text,
                TimeText = wr.Word.TimeMs > 0 ? FormatTime(wr.Word.TimeMs) : "-"
            });
            flat++;
        }

        var next = _song.WordAtFlatIndex(_tapIndex);
        NextWordText.Text = next == null
            ? "Hotovo, všechna slova mají čas"
            : "Další slovo: " + next.Word.Text;

        ProgressText.Text = $"{Math.Min(_tapIndex, _song.WordCount)} / {_song.WordCount} slov";

        int lineIndex = next?.LineIndex ?? Math.Max(0, _song.Lines.Count - 1);
        RenderLinePreview(lineIndex);

        if (_tapIndex >= 0 && _tapIndex < _wordRows.Count)
        {
            WordsGrid.SelectedIndex = _tapIndex;
            WordsGrid.ScrollIntoView(_wordRows[_tapIndex]);
        }
    }

    private void RenderLinePreview(int lineIndex)
    {
        CurrentWordsPanel.Children.Clear();

        if (_song.Lines.Count == 0 || lineIndex < 0 || lineIndex >= _song.Lines.Count)
        {
            CurrentWordsPanel.Children.Add(MakeWord("Zatím není text", false, true));
            PrevLineText.Text = "";
            NextLineText.Text = "";
            return;
        }

        var line = _song.Lines[lineIndex];

        PrevLineText.Text = lineIndex > 0 ? _song.Lines[lineIndex - 1].Text : "";
        NextLineText.Text = lineIndex + 1 < _song.Lines.Count ? _song.Lines[lineIndex + 1].Text : "";

        int baseFlat = _song.Lines.Take(lineIndex).Sum(l => l.Words.Count);

        for (int i = 0; i < line.Words.Count; i++)
        {
            int flatIndex = baseFlat + i;
            bool done = flatIndex < _tapIndex;
            bool now = flatIndex == _tapIndex;
            CurrentWordsPanel.Children.Add(MakeWord(line.Words[i].Text, done, now));
        }
    }

    private static TextBlock MakeWord(string text, bool done, bool now)
    {
        return new TextBlock
        {
            Text = text + " ",
            FontSize = now ? 52 : 46,
            FontWeight = FontWeights.Bold,
            Foreground = now ? Brushes.Gold : done ? Brushes.DeepSkyBlue : Brushes.WhiteSmoke,
            Margin = new Thickness(4),
            Effect = now
                ? new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Colors.Gold,
                    BlurRadius = 20,
                    ShadowDepth = 0,
                    Opacity = 0.65
                }
                : null
        };
    }

    private void Help_Click(object sender, RoutedEventArgs e)
    {
        var help = new Window
        {
            Title = "Nápověda - ORIScore Karaoke Editor",
            Owner = this,
            Width = 760,
            Height = 640,
            MinWidth = 560,
            MinHeight = 420,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.FromRgb(6, 9, 16)),
            Foreground = Brushes.WhiteSmoke
        };

        var text = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 15,
            LineHeight = 22,
            Foreground = Brushes.WhiteSmoke,
            Text =
                "ORIScore Karaoke Editor\n\n" +
                "Základní postup:\n" +
                "1. Vlož text skladby vlevo do pole Text skladby.\n" +
                "2. Vyplň název skladby.\n" +
                "3. Klikni na Vytvořit slova z textu.\n" +
                "4. Vyber audio soubor.\n" +
                "5. Pusť přehrávání klávesou Enter.\n" +
                "6. Mezerníkem odklepávej čas dalšího slova podle zpěvu.\n" +
                "7. Když se spleteš, Backspace vrátí poslední odklepnuté slovo.\n" +
                "8. Nakonec ulož jako .ock.\n\n" +
                "Klávesy:\n" +
                "Enter = Play / Pause\n" +
                "Mezerník = zapsat čas dalšího slova\n" +
                "Backspace = krok zpět\n\n" +
                "Poznámky:\n" +
                "- Když je kurzor v textovém poli, Enter a mezerník píšou normálně.\n" +
                "- Tlačítka už si neberou fokus, takže mezerník a Enter nebudou náhodně mačkat poslední tlačítko.\n" +
                "- Pokud zavřeš editor s neuloženými změnami, editor se nejdřív zeptá.\n" +
                "- Velikost a pozice okna se ukládá do profilu uživatele.\n\n" +
                "Tip:\n" +
                "Offset použij, když chceš všechny zapisované časy posunout dopředu nebo dozadu."
        };

        help.Content = new ScrollViewer
        {
            Padding = new Thickness(24),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = text
        };

        help.Show();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!ConfirmDiscardUnsavedChanges())
        {
            e.Cancel = true;
            return;
        }

        SaveWindowState();
        base.OnClosing(e);
    }

    private bool ConfirmDiscardUnsavedChanges()
    {
        if (!_isDirty)
            return true;

        var result = MessageBox.Show(
            this,
            "Soubor není uložený.\n\nChceš změny před pokračováním uložit?",
            "Neuložené změny",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning);

        if (result == MessageBoxResult.Cancel)
            return false;

        if (result == MessageBoxResult.Yes)
            return Save(false);

        return true;
    }

    private void MarkDirty()
    {
        if (_suppressDirtyTracking)
            return;

        SetDirty(true);
    }

    private void SetDirty(bool value)
    {
        _isDirty = value;
        UpdateWindowTitle();
    }

    private void UpdateWindowTitle()
    {
        string file = string.IsNullOrWhiteSpace(_savePath)
            ? "bez souboru"
            : Path.GetFileName(_savePath);

        Title = (_isDirty ? "* " : "") + "ORIScore Karaoke Editor - " + file;
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

            if (s.Maximized)
                WindowState = WindowState.Maximized;
        }
        catch
        {
            // Poškozené nastavení okna ignorujeme.
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
                Maximized = WindowState == WindowState.Maximized
            };

            string json = JsonSerializer.Serialize(s, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            File.WriteAllText(WindowSettingsFile, json);
        }
        catch
        {
            // Ukládání nastavení nesmí shodit editor.
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

    private static string FormatTime(int ms)
    {
        var t = TimeSpan.FromMilliseconds(Math.Max(0, ms));
        return $"{(int)t.TotalMinutes:00}:{t.Seconds:00}.{t.Milliseconds / 100}";
    }

    private static string MakeSafeFileName(string s)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
            s = s.Replace(c, '_');
        return string.IsNullOrWhiteSpace(s) ? "karaoke" : s.Trim();
    }
}

public sealed class WordRow : INotifyPropertyChanged
{
    public int FlatIndex { get; set; }
    public int LineIndex { get; set; }
    public string Text { get; set; } = "";
    public string TimeText { get; set; } = "";

    public event PropertyChangedEventHandler? PropertyChanged;
}
