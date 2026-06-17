using System.Windows;
using System.IO;

namespace OrisKaraokeEditorWpf;

public partial class App
{
    private void Application_Startup(object sender, StartupEventArgs e)
    {
        var win = new MainWindow();

        if (e.Args.Length > 0 && File.Exists(e.Args[0]))
            win.LoadSongFile(e.Args[0]);

        win.Show();
    }
}
