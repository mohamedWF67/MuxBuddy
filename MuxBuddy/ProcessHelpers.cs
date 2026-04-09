using System.Diagnostics;

namespace MuxBuddy;

public static class ProcessHelpers
{
    public static async Task<bool> IsCommandAvailableAsync(string fileName, string arguments)
    {
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            process.Start();
            await process.WaitForExitAsync();

            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public static async Task<int> RunProcessAsync(string fileName, string arguments)
    {
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = false,
                Verb = "runas" // may trigger admin prompt depending on context
            };

            process.OutputDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                    Console.WriteLine(e.Data);
            };

            process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                    Console.Error.WriteLine(e.Data);
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync();
            return process.ExitCode;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to run {fileName}: {ex.Message}");
            return -1;
        }
    }
    
    public static async Task<bool> WingetInstallFFmpeg()
    {
        if (await EnsureFfmpegInstalledAsync())
        {
            return true;
        }
        
        if (await ProcessHelpers.IsCommandAvailableAsync("winget", "--version"))
        {
            Console.WriteLine("Using winget to install FFmpeg...");

            int exitCode = await ProcessHelpers.RunProcessAsync(
                "winget",
                "install -e --id Gyan.FFmpeg --accept-package-agreements --accept-source-agreements");

            if (exitCode == 0 && await ProcessHelpers.IsCommandAvailableAsync("ffmpeg", "-version"))
            {
                return true;
            }
        }
        
        return false;
    }
    
    public static async Task<bool> ChocoInstallFFmpeg()
    {
        if (await EnsureFfmpegInstalledAsync())
        {
            return true;
        }
        
        if (await IsCommandAvailableAsync("choco", "--version"))
        {
            Console.WriteLine("Using Chocolatey to install FFmpeg...");

            int exitCode = await RunProcessAsync(
                "choco",
                "install ffmpeg -y");

            if (exitCode == 0 && await IsCommandAvailableAsync("ffmpeg", "-version"))
            {
                return true;
            }
        }
        
        return false;
    }
    
    public static async Task<bool> ScoopInstallFFmpeg()
    {
        if (await EnsureFfmpegInstalledAsync())
        {
            return true;
        }
        
        if (await IsCommandAvailableAsync("scoop", "--version"))
        {
            Console.WriteLine("Using Scoop to install FFmpeg...");

            int exitCode = await RunProcessAsync(
                "scoop",
                "install ffmpeg-essentials");

            if (exitCode == 0 && await IsCommandAvailableAsync("ffmpeg", "-version"))
            {
                return true;
            }
        }
        
        return false;
    }

    public static async Task<bool> WebInstallFFmpeg()
    {
        if (await EnsureFfmpegInstalledAsync())
        {
            return true;
        }
        
        Process.Start(new ProcessStartInfo
        {
            FileName = "cmd",
            Arguments = $"/c start https://www.gyan.dev/ffmpeg/builds",
            CreateNoWindow = true
        });
        
        return false;
    }

    public static async Task<bool> EnsureFfmpegInstalledAsync()
    {
        if (await IsCommandAvailableAsync("ffmpeg", "-version"))
        {
            Console.WriteLine("FFmpeg is already installed.");
            return true;
        }

        Console.WriteLine("FFmpeg not found. Attempting installation...");
        return false;
    }
}