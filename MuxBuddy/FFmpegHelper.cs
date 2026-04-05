using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using FFMpegCore;
using FFMpegCore.Enums;

namespace MuxBuddy;

public static class FFmpegHelperProperties{
    public static string HelperVersion { get; } = "0.5.8.23";
} 

public class VideoInfo
{
    public FileInfo? FileInfo { get; set; }
    public string OutputPath { get; set; }
    public float VideoBitrate { get; set; }
    public float AudioBitrate { get; set; }
    public double VideoSize { get; set; }
    public TimeSpan VideoDuration { get; set; }
    public double VideoDurationInSeconds { get; set; }
    public int AudioStreamsCount { get; set; }
    public int AudioMixMode { get; set; }
    public double StartTime { get; set; }
    public double EndTime { get; set; }

    public VideoInfo Clone()
    {
        return new VideoInfo
        {
            FileInfo = FileInfo,
            OutputPath = OutputPath,
            VideoBitrate = VideoBitrate,
            AudioBitrate = AudioBitrate,
            VideoSize = VideoSize,
            VideoDuration = VideoDuration,
            VideoDurationInSeconds = VideoDurationInSeconds,
            AudioStreamsCount = AudioStreamsCount,
            AudioMixMode = AudioMixMode,
            StartTime = StartTime,
            EndTime = EndTime
        };
    }

    public void Display()
    {
        Console.WriteLine($"Video Bitrate: {VideoBitrate} kbps");
        Console.WriteLine($"Audio Bitrate: {AudioBitrate} kbps");
        Console.WriteLine($"Video Size: {VideoSize} MB");
        Console.WriteLine($"Video Duration: {VideoDuration}");
        Console.WriteLine($"Video Duration in Seconds: {VideoDurationInSeconds}");
        Console.WriteLine($"Audio Channels Count: {AudioStreamsCount}");
        Console.WriteLine($"Audio Mix Mode: {AudioMixMode}");
        Console.WriteLine($"Start Time: {StartTime}");
        Console.WriteLine($"End Time: {EndTime}");
    }
}

public sealed class FfmpegStats
{
    public TimeSpan ElapsedRealtime { get; set; }
    public TimeSpan Elapsed { get; set; }
    public TimeSpan Remaining { get; set; }
    public double? Fps { get; set; }
    public double? Speed { get; set; }
    public string? RawLine { get; set; }
    
    public void Display()
    {
        Console.WriteLine($"Elapsed Realtime: {ElapsedRealtime}");
        Console.WriteLine($"Elapsed: {Elapsed}");
        Console.WriteLine($"Remaining: {Remaining}");
        Console.WriteLine($"FPS: {Fps}");
        Console.WriteLine($"Speed: {Speed}");
        Console.WriteLine($"Raw Line: {RawLine}");
    }
}

public class FFmpegHelper
{
    private static float BitrateModifer = 0.95f;
    
    private static readonly Regex FpsRegex =
        new(@"(?:^|\s)fps=\s*(?<fps>\d+(?:\.\d+)?)", RegexOptions.Compiled);

    private static readonly Regex TimeRegex =
        new(@"time=(?<time>\d{2}:\d{2}:\d{2}(?:\.\d+)?)", RegexOptions.Compiled);

    private static readonly Regex SpeedRegex =
        new(@"speed=\s*(?<speed>\d+(?:\.\d+)?)x", RegexOptions.Compiled);
    
    public static VideoInfo ExtractInfo(string inputPath)
    {
        var analysis = FFProbe.Analyse(inputPath);
        var fileInfo = new FileInfo(inputPath);

        return new VideoInfo
        {
            VideoBitrate = analysis.PrimaryVideoStream.BitRate / 1000,
            AudioBitrate = analysis.PrimaryAudioStream?.BitRate / 1000 ?? 0,
            VideoSize = fileInfo.Length / 1024.0 / 1024.0,
            VideoDuration = analysis.Duration,
            VideoDurationInSeconds = analysis.Duration.TotalSeconds,
            AudioStreamsCount = analysis.AudioStreams.Count,
            AudioMixMode = 0,
            FileInfo = fileInfo,
            OutputPath = Path.Combine(Path.GetDirectoryName(inputPath),Path.GetFileNameWithoutExtension(inputPath) + "-Modified" + Path.GetExtension(inputPath)),
            StartTime = 0,
            EndTime = analysis.Duration.TotalSeconds
        };
    }

    public static float GetBitrateForSize(VideoInfo Video,float DesiredSize)
    {
        double bitrate;
        if (Double.IsNaN(Video.StartTime) && Double.IsNaN(Video.EndTime))
        {
            double DurationInSeconds = TimeSpan.FromSeconds(Video.EndTime - Video.StartTime).TotalSeconds; 
            bitrate = (((DesiredSize * BitrateModifer * 8192) / DurationInSeconds) - Video.AudioBitrate * Video.AudioStreamsCount);
        }
        else
        {
            bitrate = (((DesiredSize * BitrateModifer * 8192) / Video.VideoDurationInSeconds) - Video.AudioBitrate * Video.AudioStreamsCount);
        }
        return (float)bitrate;
    }

    public static async Task<bool> CutAndEncodeFromPointToEnd(
        string inputPath,
        string? outputPath,
        int bitrate,
        double start,
        double end,
        Action<double>? onProgress = null,
        bool hasMultipleAudio = false,
        bool encodeVideo = false,
        bool keepOriginalStreams = false)
    {
        VideoInfo info = ExtractInfo(inputPath);
        
        var durationInSeconds = TimeSpan.FromSeconds(end - start);
        
        return await FFMpegArguments
            .FromFileInput(info.FileInfo,options => options
                .WithCustomArgument("-hwaccel cuda") // Use hardware acceleration for decoding
                .WithCustomArgument("-hwaccel_output_format cuda"))
            .OutputToFile(outputPath, overwrite: true, options =>
                {
                    if (hasMultipleAudio)
                    {
                        if (keepOriginalStreams)
                        {
                            // Keep both original audio streams and add a third merged stream
                            options.WithCustomArgument("-filter_complex \"[0:a:0][0:a:1]amix=inputs=2:duration=longest:normalize=1[a]\"")
                                .WithCustomArgument("-map 0:v")
                                .WithCustomArgument("-map 0:a:0")
                                .WithCustomArgument("-map 0:a:1")
                                .WithCustomArgument("-map \"[a]\"");
                        }
                        else
                        {
                            // Merge audio streams into one (current behavior)
                            options.WithCustomArgument("-filter_complex \"[0:a:0][0:a:1]amix=inputs=2:duration=longest:normalize=1[a]\"")
                                .WithCustomArgument("-map 0:v")
                                .WithCustomArgument("-map \"[a]\"");
                        }
                    }
                    else
                    {
                        options.WithCustomArgument("-map 0:v")
                            .WithCustomArgument("-map 0:a");
                    }

                    if (start != null)
                        options.Seek(TimeSpan.FromSeconds(start)).WithDuration(durationInSeconds);
                    
                    if (encodeVideo)
                    {
                        options.WithCustomArgument($"-c:v h264_nvenc -rc cbr_hq -preset p4 -b:v {bitrate}k");
                    }
                    else
                    {
                        options.WithCustomArgument("-c:v copy");
                    }

                    options.WithAudioCodec(AudioCodec.Aac)
                        .WithCustomArgument("-b:a 96k");
                })
            .NotifyOnProgress(progress =>
            {
                onProgress?.Invoke(progress);
                if (progress >= 100) MessageBox.Show($"File is Ready at \n {outputPath}");
            }, durationInSeconds)
            .ProcessAsynchronously();
    }
    
    public static async Task<bool> CutAndEncodeFromPointToEndPlus(
        VideoInfo video,
        Action<FfmpegStats> onStats,
        Action<double>? onProgress = null,
        bool encodeVideo = false)
    {
        var stopwatch = Stopwatch.StartNew();
        TimeSpan lastProgress = TimeSpan.Zero;
        var stats = new FfmpegStats();
        
        return await FFMpegArguments
            .FromFileInput(video.FileInfo,options => options
                .WithCustomArgument("-hwaccel cuda") // Use hardware acceleration for decoding
                .WithCustomArgument("-hwaccel_output_format cuda"))
            .OutputToFile(video.OutputPath, overwrite: true, options =>
                {
                    switch (video.AudioMixMode)
                    {
                        case 0:
                            options.WithCustomArgument("-map 0:v")
                                .WithCustomArgument("-map 0:a");
                            break;
                        case 1:
                            // Merge audio streams into one (current behavior)
                            options.WithCustomArgument("-filter_complex \"[0:a:0][0:a:1]amix=inputs=2:duration=longest:normalize=1[a]\"")
                                .WithCustomArgument("-map 0:v")
                                .WithCustomArgument("-map \"[a]\"");
                            break;
                        case 2:
                            // Keep both original audio streams and add a third merged stream
                            options.WithCustomArgument("-filter_complex \"[0:a:0][0:a:1]amix=inputs=2:duration=longest:normalize=1[a]\"")
                                .WithCustomArgument("-map 0:v")
                                .WithCustomArgument("-map \"[a]\"")
                                .WithCustomArgument("-map 0:a:0")
                                .WithCustomArgument("-map 0:a:1");
                            break;
                                
                    }

                    if (video.StartTime != null)
                        options.Seek(TimeSpan.FromSeconds(video.StartTime)).WithDuration(video.VideoDuration);
                    
                    if (encodeVideo)
                    {
                        options.WithCustomArgument($"-c:v h264_nvenc -rc cbr_hq -preset p4 -b:v {(int)video.VideoBitrate}k");
                    }
                    else
                    {
                        options.WithCustomArgument("-c:v copy");
                    }

                    options.WithAudioCodec(AudioCodec.Aac)
                        .WithCustomArgument($"-b:a {video.AudioBitrate}k");
                    
                    //options.WithCustomArgument("-progress pipe:1 -nostats");
                })
            .NotifyOnProgress(progress =>
            {
                onProgress?.Invoke(progress);
                
                double elapsed = stopwatch.Elapsed.TotalSeconds;
                stats.ElapsedRealtime = TimeSpan.FromSeconds(elapsed);

                // estimate processed seconds from percentage
                double processedSeconds = video.VideoDuration.TotalSeconds * (progress / 100.0);

                double speed = processedSeconds / elapsed;
            }, video.VideoDuration)
            .NotifyOnError(line =>
            {
                if (string.IsNullOrWhiteSpace(line))
                    return;

                stats.RawLine = line;

                var fpsMatch = FpsRegex.Match(line);
                if (fpsMatch.Success &&
                    double.TryParse(
                        fpsMatch.Groups["fps"].Value,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out var fps))
                {
                    stats.Fps = fps;
                }

                var timeMatch = TimeRegex.Match(line);
                if (timeMatch.Success &&
                    TimeSpan.TryParse(timeMatch.Groups["time"].Value, out var elapsed))
                {
                    stats.Elapsed = elapsed;
                }

                var speedMatch = SpeedRegex.Match(line);
                if (speedMatch.Success &&
                    double.TryParse(
                        speedMatch.Groups["speed"].Value,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out var speed))
                {
                    stats.Speed = speed;
                }
                
                stats.Remaining = video.VideoDuration - stats.Elapsed;
                

                if (fpsMatch.Success || timeMatch.Success || speedMatch.Success)
                {
                    onStats(new FfmpegStats
                    {
                        ElapsedRealtime = stats.ElapsedRealtime,
                        Elapsed = stats.Elapsed,
                        Remaining = stats.Remaining,
                        Fps = stats.Fps,
                        Speed = stats.Speed,
                        RawLine = stats.RawLine
                    });
                }
            })
                .ProcessAsynchronously();
    }
}