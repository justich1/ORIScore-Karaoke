using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using WpfMessageBox = System.Windows.MessageBox;
using WpfMessageBoxButton = System.Windows.MessageBoxButton;
using WpfMessageBoxImage = System.Windows.MessageBoxImage;
using WpfMessageBoxResult = System.Windows.MessageBoxResult;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfCursors = System.Windows.Input.Cursors;
using WpfBrush = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using WinForms = System.Windows.Forms;
using System.Windows.Input;

namespace OrisKaraokeConverter;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<KfnRow> _rows = new();
    private readonly List<string> _kfnFiles = new();
    private readonly Dictionary<string, string> _manualAudioCache = new(StringComparer.OrdinalIgnoreCase);

    public MainWindow()
    {
        InitializeComponent();
        KfnGrid.ItemsSource = _rows;
    }

    private void BrowseInputButton_Click(object sender, RoutedEventArgs e)
    {
        BrowseFolder(InputDirBox, "Vyber vstupní složku s KFN soubory");
    }

    private void BrowseOutputButton_Click(object sender, RoutedEventArgs e)
    {
        BrowseFolder(OutputDirBox, "Vyber výstupní složku pro .ock a zaklad audio");
    }

    private static void BrowseFolder(WpfTextBox target, string description)
    {
        using var dlg = new WinForms.FolderBrowserDialog
        {
            Description = description,
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true
        };

        if (Directory.Exists(target.Text))
            dlg.SelectedPath = target.Text;

        if (dlg.ShowDialog() == WinForms.DialogResult.OK)
            target.Text = dlg.SelectedPath;
    }

    private void ScanButton_Click(object sender, RoutedEventArgs e)
    {
        Scan();
    }

    private async void ConvertButton_Click(object sender, RoutedEventArgs e)
    {
        await ConvertAsync();
    }

    private void HelpButton_Click(object sender, RoutedEventArgs e)
    {
        WpfMessageBox.Show(
            "Hledat KFN i v podsložkách\n" +
            "Najde karaoke soubory i ve složkách uvnitř vstupu.\n\n" +
            "Zkopírovat MP3/WAV do zaklad\n" +
            "Najde audio z položky Source v KFN a uloží ho do podsložky zaklad vedle vytvořeného .ock.\n\n" +
            "Přepsat existující výstupy\n" +
            "Když už ve výstupu JSON nebo audio existuje, dovolí ho přepsat.\n\n" +
            "Zachovat strom složek\n" +
            "Použij jen tehdy, když chceš ve výstupu stejnou strukturu složek jako ve vstupu. Jinak nech vypnuté.\n\n" +
            "Vytvořit index pro rádio\n" +
            "Vytvoří karaoke.index.json se seznamem skladeb pro ESP rádio/web.\n\n" +
            "Při chybějícím audiu se zeptat\n" +
            "Když původní Source cesta v KFN neexistuje, otevře okno pro ruční výběr správného MP3/WAV. Konvertor nehádá cizí skladby.",
            "Vysvětlení voleb",
            WpfMessageBoxButton.OK,
            WpfMessageBoxImage.Information);
    }

    private void Scan()
    {
        _rows.Clear();
        _kfnFiles.Clear();
        LogBox.Clear();

        if (!Directory.Exists(InputDirBox.Text))
        {
            Log("Vstupní složka neexistuje.");
            return;
        }

        _kfnFiles.AddRange(KaraokeBatchConverter.FindKfnFiles(InputDirBox.Text, RecursiveBox.IsChecked == true));

        foreach (string path in _kfnFiles)
        {
            _rows.Add(new KfnRow
            {
                KfnPath = path,
                AudioPath = "",
                Status = "čeká",
                OutputPath = "",
                RowBrush = WpfBrushes.LightSteelBlue
            });
        }

        Progress.Value = 0;
        Progress.Maximum = Math.Max(1, _kfnFiles.Count);
        Log($"Nalezeno {_kfnFiles.Count} KFN souborů.");
    }

    private async Task ConvertAsync()
    {
        if (_kfnFiles.Count == 0)
            Scan();

        if (_kfnFiles.Count == 0)
            return;

        string inputDir = InputDirBox.Text;
        string outputDir = OutputDirBox.Text;

        if (string.IsNullOrWhiteSpace(outputDir))
        {
            Log("Vyber výstupní složku.");
            return;
        }

        Directory.CreateDirectory(outputDir);

        var options = new ConvertOptions
        {
            Recursive = RecursiveBox.IsChecked == true,
            CopyAudio = CopyAudioBox.IsChecked == true,
            Overwrite = OverwriteBox.IsChecked == true,
            PreserveFolders = PreserveFoldersBox.IsChecked == true,
            CreateIndexJson = CreateIndexBox.IsChecked == true,
            PromptForMissingAudio = PromptMissingAudioBox.IsChecked == true,
            MissingAudioResolver = ResolveMissingAudioOnUiThread
        };

        SetBusy(true);
        Progress.Value = 0;
        Progress.Maximum = Math.Max(1, _kfnFiles.Count);

        await Task.Run(() =>
        {
            for (int i = 0; i < _kfnFiles.Count; i++)
            {
                int rowIndex = i;
                string file = _kfnFiles[rowIndex];
                ConvertResult result = KaraokeBatchConverter.ConvertOne(file, inputDir, outputDir, options);
                Dispatcher.Invoke(() => UpdateRow(rowIndex, result));
            }

            if (options.CreateIndexJson)
            {
                try
                {
                    KaraokeBatchConverter.CreateIndexJson(outputDir);
                    Dispatcher.Invoke(() => Log("Vytvořen karaoke.index.json"));
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() => Log("Index se nepovedl: " + ex.Message));
                }
            }
        });

        SetBusy(false);
        Log("Hotovo.");
    }

    private string? ResolveMissingAudioOnUiThread(string kfnPath, string originalAudioPath)
    {
        string expectedName = KfnParser.FileNameOnly(originalAudioPath);
        string cacheKey = !string.IsNullOrWhiteSpace(expectedName)
            ? expectedName
            : Path.GetFileNameWithoutExtension(kfnPath);

        if (_manualAudioCache.TryGetValue(cacheKey, out string cached) && File.Exists(cached))
            return cached;

        return Dispatcher.Invoke(() =>
        {
            string sourceText = string.IsNullOrWhiteSpace(originalAudioPath) ? "(v KFN není Source cesta)" : originalAudioPath;
            Log($"Audio nenalezeno pro {Path.GetFileName(kfnPath)}. Původní Source: {sourceText}");

            var msg =
                "KFN ukazuje na audio, které neexistuje:\n\n" +
                sourceText + "\n\n" +
                "Vyber správný MP3/WAV soubor pro:\n" +
                Path.GetFileName(kfnPath);

            WpfMessageBoxResult ask = WpfMessageBox.Show(
                this,
                msg,
                "Chybí audio",
                WpfMessageBoxButton.OKCancel,
                WpfMessageBoxImage.Warning);

            if (ask != WpfMessageBoxResult.OK)
                return null;

            var dlg = new OpenFileDialog
            {
                Title = "Dohledat audio pro " + Path.GetFileName(kfnPath),
                Filter = "Audio soubory (*.mp3;*.wav)|*.mp3;*.wav|Všechny soubory (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = false
            };

            string sourceDir = "";
            try
            {
                if (!string.IsNullOrWhiteSpace(originalAudioPath))
                    sourceDir = Path.GetDirectoryName(originalAudioPath) ?? "";
            }
            catch
            {
                sourceDir = "";
            }

            if (!string.IsNullOrWhiteSpace(sourceDir) && Directory.Exists(sourceDir))
                dlg.InitialDirectory = sourceDir;
            else
            {
                string kfnDir = Path.GetDirectoryName(kfnPath) ?? "";
                if (Directory.Exists(kfnDir))
                    dlg.InitialDirectory = kfnDir;
            }

            if (!string.IsNullOrWhiteSpace(expectedName))
                dlg.FileName = expectedName;

            if (dlg.ShowDialog(this) == true && File.Exists(dlg.FileName))
            {
                _manualAudioCache[cacheKey] = dlg.FileName;
                return dlg.FileName;
            }

            return null;
        });
    }

    private void UpdateRow(int index, ConvertResult result)
    {
        if (index < _rows.Count)
        {
            KfnRow row = _rows[index];
            row.AudioPath = result.AudioPath;
            row.Status = result.Message;
            row.OutputPath = result.OutputJson;
            row.RowBrush = result.Success
                ? (result.AudioFound ? WpfBrushes.LightGreen : WpfBrushes.Orange)
                : WpfBrushes.IndianRed;
        }

        Progress.Value = Math.Min(Progress.Maximum, Progress.Value + 1);
        Log($"{Path.GetFileName(result.KfnPath)}: {result.Message}");
    }

    private void KfnGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (KfnGrid.SelectedItem is not KfnRow row)
            return;

        string path = File.Exists(row.OutputPath) ? row.OutputPath : row.KfnPath;
        if (!File.Exists(path))
            return;

        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    private void SetBusy(bool busy)
    {
        ScanButton.IsEnabled = !busy;
        ConvertButton.IsEnabled = !busy;
        BrowseInputButton.IsEnabled = !busy;
        BrowseOutputButton.IsEnabled = !busy;
        Cursor = busy ? WpfCursors.Wait : WpfCursors.Arrow;
    }

    private void Log(string message)
    {
        LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        LogBox.ScrollToEnd();
    }

    private void CreateIndexBox_Checked()
    {

    }
}

public sealed class KfnRow : INotifyPropertyChanged
{
    private string _kfnPath = "";
    private string _audioPath = "";
    private string _status = "";
    private string _outputPath = "";
    private WpfBrush _rowBrush = WpfBrushes.LightSteelBlue;

    public string KfnPath
    {
        get => _kfnPath;
        set => SetField(ref _kfnPath, value);
    }

    public string AudioPath
    {
        get => _audioPath;
        set => SetField(ref _audioPath, value);
    }

    public string Status
    {
        get => _status;
        set => SetField(ref _status, value);
    }

    public string OutputPath
    {
        get => _outputPath;
        set => SetField(ref _outputPath, value);
    }

    public WpfBrush RowBrush
    {
        get => _rowBrush;
        set => SetField(ref _rowBrush, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
