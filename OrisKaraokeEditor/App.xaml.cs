using System.Windows;
using System.IO;
using System.Linq;

namespace OrisKaraokeEditorWpf;

public partial class App
{
    private void Application_Startup(object sender, StartupEventArgs e)
    {
        var win = new MainWindow();

        // Podpora otevření přes asociaci souboru i přes argumenty typu:
        //   OrisKaraokeEditorWpf.exe "soubor.ock"
        //   OrisKaraokeEditorWpf.exe /edit "soubor.ock"
        string? fileToOpen = e.Args
            .FirstOrDefault(arg => !string.IsNullOrWhiteSpace(arg) && File.Exists(arg));

        if (fileToOpen != null)
            win.LoadSongFile(Path.GetFullPath(fileToOpen));

        win.Show();
    }
}
