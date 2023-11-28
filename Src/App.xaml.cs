using System.Windows;
using RT.Serialization.Settings;
using RT.Util.Forms;
using Windows.Win32;

namespace DcsAutopilot;

public partial class App : Application
{
    internal static SettingsFileXml<Settings> SettingsFile;
    internal static Settings Settings;

    [STAThread]
    static void Main(string[] args)
    {
        PInvoke.SetProcessDpiAwareness(global::Windows.Win32.UI.HiDpi.PROCESS_DPI_AWARENESS.PROCESS_PER_MONITOR_DPI_AWARE);

        SettingsFile = new("DcsAutopilot");
        Settings = SettingsFile.Settings;

        var app = new App();
        app.InitializeComponent();
        app.Run();

        SettingsFile.Save();
    }
}

class Settings
{
    public ManagedWindow.Settings MainWindow = new();
    public ManagedWindow.Settings BobbleheadWindow = new();
}
