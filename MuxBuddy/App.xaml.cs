using System.Configuration;
using System.Data;
using System.IO;
using System.Windows;
using FFMpegCore;

namespace MuxBuddy;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        GlobalFFOptions.Configure(options =>
        {
            options.BinaryFolder = Path.Combine(
                AppContext.BaseDirectory,
                "ffmpeg"
            );

            options.TemporaryFilesFolder = Path.Combine(
                Path.GetTempPath(),
                "MuxBuddy"
            );
        });
    }
}