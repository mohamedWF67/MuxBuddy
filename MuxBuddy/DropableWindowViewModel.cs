using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Shell;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace MuxBuddy;

public partial class DropableWindowViewModel : ObservableObject
{
    private const float DefaultDesiredSize = 10;
    private const string EmptyVideoText = "Drop a video to start";

    private static readonly string[] SourceProperties =
    [
        nameof(VideoPathText), nameof(VideoBeforeBitrateText), nameof(VideoBeforeAudioBitrateText),
        nameof(VideoBeforeTotalBitrateText), nameof(VideoBeforeSizeText), nameof(VideoBeforeAudioCountText),
        nameof(VideoBeforeDurationText), nameof(CanMixAudio), nameof(CanUseAudio96), nameof(CanUseAudio128),
        nameof(CanUseAudio192), nameof(CanUseAudio320), nameof(EndTimeMaximum)
    ];

    private static readonly string[] OutputProperties =
    [
        nameof(OutputPathText), nameof(StartTimeText), nameof(EndTimeText), nameof(VideoAfterBitrateText),
        nameof(VideoAfterAudioBitrateText), nameof(VideoAfterTotalBitrateText), nameof(VideoAfterSizeText),
        nameof(VideoAfterAudioCountText), nameof(VideoAfterDurationText)
    ];

    private bool _syncing;
    private int _bitrateMode = 1;
    private VideoInfo? _source;
    private VideoInfo? _video;


    public event Action<string, string, MessageBoxImage>? MessageRequested;
    public event Action? ScrollToOutputRequested;
    public event Action? EncodingCompleted;

    [ObservableProperty] private bool fFmpegWarningVisible;
    [ObservableProperty] private bool hasVideo;
    [ObservableProperty] private bool isMainEnabled;
    [ObservableProperty] private bool isProgressVisible;
    [ObservableProperty] private double progressValue;
    [ObservableProperty] private TaskbarItemProgressState taskbarProgressState = TaskbarItemProgressState.None;
    [ObservableProperty] private double taskbarProgressValue;

    [ObservableProperty] private double startTime;
    [ObservableProperty] private double endTime;

    [ObservableProperty] private float desiredSize = DefaultDesiredSize;
    [ObservableProperty] private bool customSizeEnabled;
    [ObservableProperty] private bool presetSizesEnabled = true;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(EncodeSettingsVisible))] private bool isEncodingEnabled = true;
    [ObservableProperty] private double customVideoBitrate;
    [ObservableProperty] private int selectedEncoderIndex = (int)VideoEncoder.CpuH264;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(AudioCustomBitrateVisible))] private int selectedAudioBitrateIndex;
    [ObservableProperty] private double customAudioBitrate;
    [ObservableProperty] private int selectedAudioMixModeIndex;

    [ObservableProperty] private string statsFpsText = "0";
    [ObservableProperty] private string statsElapsedText = "0";
    [ObservableProperty] private string statsRemainingText = "0";

    public bool EncodeSettingsVisible => IsEncodingEnabled;
    public bool BitrateBySizeVisible => _bitrateMode == 1;
    public bool BitrateByBitrateVisible => _bitrateMode == 2;
    public bool AudioCustomBitrateVisible => SelectedAudioBitrateIndex == 5;
    public bool CanMixAudio => _source?.AudioStreamsCount > 1;
    public bool CanUseAudio96 => CanUseAudio(96);
    public bool CanUseAudio128 => CanUseAudio(128);
    public bool CanUseAudio192 => CanUseAudio(192);
    public bool CanUseAudio320 => CanUseAudio(320);

    public string VideoPathText => _source?.FileInfo?.Name ?? EmptyVideoText;
    public string OutputPathText => _video is null ? EmptyVideoText : Path.GetFileName(_video.OutputPath) ?? EmptyVideoText;
    public string VideoBeforeBitrateText => FormatKbps(_source?.VideoBitrate ?? 0);
    public string VideoBeforeAudioBitrateText => FormatKbps(_source?.AudioBitrate ?? 0);
    public string VideoBeforeTotalBitrateText => FormatKbps(_source is null ? 0 : _source.VideoBitrate + _source.AudioBitrate * _source.AudioStreamsCount);
    public string VideoBeforeSizeText => FormatMb(_source?.VideoSize ?? 0);
    public string VideoBeforeAudioCountText => (_source?.AudioStreamsCount ?? 0).ToString();
    public string VideoBeforeDurationText => FormatTime(_source?.VideoDuration ?? TimeSpan.Zero);

    public string StartTimeText => FormatTime(TimeSpan.FromSeconds(StartTime));
    public string EndTimeText => FormatTime(TimeSpan.FromSeconds(EndTime));
    public double EndTimeMaximum => _source?.VideoDurationInSeconds ?? 0;

    public string VideoAfterBitrateText => _video is null ? "0 kbps" : IsMainEnabled ? FormatKbps(_video.VideoBitrate) : "-∞ Kbps";
    public string VideoAfterAudioBitrateText => FormatKbps(_video?.AudioBitrate ?? 0);
    public string VideoAfterTotalBitrateText => FormatKbps(_video is null ? 0 : _video.VideoBitrate + _video.AudioBitrate * _video.AudioStreamsCount);
    public string VideoAfterSizeText => FormatMb(_video?.VideoSize ?? 0);
    public string VideoAfterAudioCountText => (_video?.AudioStreamsCount ?? 0).ToString();
    public string VideoAfterDurationText => FormatTime(_video?.VideoDuration ?? TimeSpan.Zero);

    public void LoadVideo(string[] files)
    {
        if (files is not [var path])
        {
            if (files.Length > 1)
            {
                MessageRequested?.Invoke("More than one file dropped!", "Error", MessageBoxImage.Error);
            }
            return;
        }

        if (!IsSupportedVideo(path))
        {
            MessageRequested?.Invoke("Unsupported file type", "Error", MessageBoxImage.Error);
            return;
        }

        if (_video is not null)
        {
            ClearFiles();
        }

        _source = FFmpegHelper.ExtractInfo(path);
        _video = _source.Clone();
        HasVideo = true;
        SelectedAudioBitrateIndex = 0;

        UpdateBeforeDisplay();
        UpdateVideoInfo();
    }

    [RelayCommand]
    private void ClearFiles()
    {
        _source = _video = null;
        HasVideo = IsMainEnabled = IsProgressVisible = false;
        ProgressValue = CustomVideoBitrate = CustomAudioBitrate = 0;
        SetTaskbarProgress(null);
        DesiredSize = DefaultDesiredSize;
        IsEncodingEnabled = PresetSizesEnabled = true;
        CustomSizeEnabled = false;
        SelectedAudioBitrateIndex = SelectedAudioMixModeIndex = 0;
        SetBitrateMode(1, false);
        Notify(SourceProperties);
        Notify(OutputProperties);
        EncodeCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void OpenVideo()
    {
        if (_video?.FileInfo is not null)
        {
            Process.Start("explorer.exe", _video.FileInfo.FullName);
        }
    }

    [RelayCommand]
    private void OpenInExplorer()
    {
        if (_video?.FileInfo is null)
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"/select,\"{_video.FileInfo.FullName}\"",
            UseShellExecute = true
        });
    }

    [RelayCommand] private void SetBitrateBySize() => SetBitrateMode(1);
    [RelayCommand] private void SetBitrateByBitrate() => SetBitrateMode(2);

    [RelayCommand]
    private void SetPresetSize(string? size)
    {
        if (float.TryParse(size, out var result))
        {
            DesiredSize = result;
        }
    }

    [RelayCommand(CanExecute = nameof(CanEncode))]
    private async Task EncodeAsync()
    {
        if (_video is null)
        {
            return;
        }

        try
        {
            IsProgressVisible = true;
            ScrollToOutputRequested?.Invoke();
            _video.Display();
            await FFmpegHelper.CutAndEncodeFromPointToEndPlus(_video, OnStatusUpdated, OnProgress, IsEncodingEnabled);
        }
        catch (Exception exception)
        {
            MessageRequested?.Invoke($"Error: {exception.Message}", "Error", MessageBoxImage.Error);
            Console.WriteLine(exception);
        }
        finally
        {
            EncodeCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanEncode() => _video is not null && IsMainEnabled && !EncodeCommand.IsRunning;

    private async Task InstallFfmpegAsync(Func<Task<bool>> installer)
    {
        var installed = await installer();
        MessageRequested?.Invoke(
            installed ? "FFmpeg installed successfully" : "Failed to install FFmpeg",
            installed ? "Success" : "Error",
            installed ? MessageBoxImage.Information : MessageBoxImage.Error);
    }

    [RelayCommand] private Task WingetInstall() => InstallFfmpegAsync(ProcessHelpers.WingetInstallFFmpeg);
    [RelayCommand] private Task ChocoInstall() => InstallFfmpegAsync(ProcessHelpers.ChocoInstallFFmpeg);
    [RelayCommand] private Task ScoopInstall() => InstallFfmpegAsync(ProcessHelpers.ScoopInstallFFmpeg);
    [RelayCommand] private Task WebInstall() => ProcessHelpers.WebInstallFFmpeg();
    private void OnProgress(double value) => RunOnUi(() =>
    {
        if (value >= 100)
        {
            ProgressValue = 0;
            SetTaskbarProgress(null);
            EncodingCompleted?.Invoke();
            MessageRequested?.Invoke($"File is Ready at \n {_video?.OutputPath}", "Mux Buddy", MessageBoxImage.Information);
            return;
        }

        ProgressValue = value;
        SetTaskbarProgress(value);
    });

    private void OnStatusUpdated(FfmpegStats stats) => RunOnUi(() =>
    {
        var speed = stats.Speed.GetValueOrDefault();
        var eta = speed > 0 ? TimeSpan.FromSeconds(stats.Remaining.TotalSeconds / speed) : TimeSpan.Zero;
        StatsFpsText = stats.Fps?.ToString() ?? "0";
        StatsElapsedText = FormatTime(stats.ElapsedRealtime);
        StatsRemainingText = FormatTime(eta);
    });

    partial void OnDesiredSizeChanged(float value)
    {
        if (!_syncing && value > 0)
        {
            UpdateVideoInfo();
        }
    }

    partial void OnCustomSizeEnabledChanged(bool value) => PresetSizesEnabled = !value;

    partial void OnIsEncodingEnabledChanged(bool value)
    {
        if (!_syncing)
        {
            UpdateVideoInfo();
        }
    }

    partial void OnStartTimeChanged(double value)
    {
        if (_syncing || _video is null)
        {
            return;
        }

        if (value < 0 || value > _video.EndTime)
        {
            StartTime = _video.StartTime;
            return;
        }

        _video.StartTime = value;
        UpdateVideoInfo();
    }

    partial void OnEndTimeChanged(double value)
    {
        if (_syncing || _video is null || _source is null)
        {
            return;
        }

        if (value < _video.StartTime || value > _source.VideoDurationInSeconds)
        {
            EndTime = _video.EndTime;
            return;
        }

        _video.EndTime = value;
        UpdateVideoInfo();
    }

    partial void OnCustomVideoBitrateChanged(double value)
    {
        if (!_syncing && _video is not null && _source is not null && value > 0 && value < _source.VideoBitrate)
        {
            _video.VideoBitrate = (float)value;
            UpdateVideoInfo();
        }
    }

    partial void OnSelectedEncoderIndexChanged(int value)
    {
        if (!_syncing && _video is not null && value >= 0)
        {
            _video.Encoder = (VideoEncoder)value;
            UpdateVideoInfo();
        }
    }

    partial void OnSelectedAudioMixModeIndexChanged(int value)
    {
        if (_syncing || _video is null)
        {
            return;
        }

        if (value > 0 && !CanMixAudio)
        {
            SelectedAudioMixModeIndex = 0;
            return;
        }

        _video.AudioMixMode = value;
        UpdateVideoInfo();
    }

    partial void OnSelectedAudioBitrateIndexChanged(int value)
    {
        if (_syncing || _video is null || _source is null || AudioCustomBitrateVisible)
        {
            return;
        }

        _video.AudioBitrate = value switch
        {
            1 => 96,
            2 => 128,
            3 => 192,
            4 => 320,
            _ => _source.AudioBitrate
        };
        UpdateVideoInfo();
    }

    partial void OnCustomAudioBitrateChanged(double value)
    {
        if (!_syncing && _video is not null && _source is not null && value > 0 && value < _source.AudioBitrate)
        {
            _video.AudioBitrate = (float)value;
            UpdateVideoInfo();
        }
    }

    private void UpdateVideoInfo()
    {
        if (_video is null || _source is null)
        {
            return;
        }

        _video.VideoDuration = TimeSpan.FromSeconds(_video.EndTime - _video.StartTime);
        _video.VideoDurationInSeconds = _video.VideoDuration.TotalSeconds;
        _video.AudioStreamsCount = _video.AudioMixMode switch { 1 => 1, 2 => 3, _ => _source.AudioStreamsCount };

        if (IsEncodingEnabled && _bitrateMode == 1)
        {
            _video.VideoBitrate = FFmpegHelper.GetBitrateForSize(_video, DesiredSize);
        }
        else if (!IsEncodingEnabled)
        {
            _video.VideoBitrate = _source.VideoBitrate;
        }

        _video.VideoSize = (_video.VideoBitrate + _video.AudioStreamsCount * _video.AudioBitrate) * _video.VideoDurationInSeconds / 8192;
        UpdateExportName();
        UpdateAfterDisplay();
    }

    private void UpdateBeforeDisplay() => Notify(SourceProperties);

    private void UpdateAfterDisplay()
    {
        if (_video is null || _source is null)
        {
            return;
        }

        _syncing = true;
        StartTime = _video.StartTime;
        EndTime = _video.EndTime;
        CustomVideoBitrate = _video.VideoBitrate;
        CustomAudioBitrate = _video.AudioBitrate;
        SelectedEncoderIndex = (int)_video.Encoder;
        _syncing = false;

        IsMainEnabled = _video.VideoBitrate >= 0 && _video.VideoBitrate <= _source.VideoBitrate;
        Notify(OutputProperties);
        EncodeCommand.NotifyCanExecuteChanged();
    }

    private void UpdateExportName()
    {
        if (_video?.FileInfo is null)
        {
            return;
        }

        var suffix = string.Empty;
        if (_video.AudioMixMode is 1 or 2)
        {
            suffix += "-Resampled";
        }
        if (IsEncodingEnabled && DesiredSize > 0)
        {
            suffix += $"-{DesiredSize}Mb";
        }
        if (IsEncodingEnabled)
        {
            suffix += $"-{_video.Encoder}";
        }

        _video.OutputPath = Path.Combine(
            _video.FileInfo.DirectoryName ?? string.Empty,
            Path.GetFileNameWithoutExtension(_video.FileInfo.Name) + (suffix.Length == 0 ? "-Modified" : suffix) + _video.FileInfo.Extension);
    }

    private void SetBitrateMode(int mode, bool update = true)
    {
        _bitrateMode = mode;
        OnPropertyChanged(nameof(BitrateBySizeVisible));
        OnPropertyChanged(nameof(BitrateByBitrateVisible));
        if (update)
        {
            UpdateVideoInfo();
        }
    }

    private void SetTaskbarProgress(double? percent)
    {
        TaskbarProgressState = percent is null ? TaskbarItemProgressState.None : TaskbarItemProgressState.Normal;
        TaskbarProgressValue = percent.GetValueOrDefault() / 100.0;
    }

    private bool CanUseAudio(int bitrate) => _source?.AudioBitrate >= bitrate;
    private void Notify(IEnumerable<string> propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            OnPropertyChanged(propertyName);
        }
    }

    private static bool IsSupportedVideo(string path) => Path.GetExtension(path).ToLowerInvariant() is ".mkv" or ".mp4" or ".ts";
    private static string FormatKbps(double value) => $"{value:N0} kbps";
    private static string FormatMb(double value) => $"{value:0.00} Mb";
    private static string FormatTime(TimeSpan value) => value.ToString(@"hh\:mm\:ss\.fff");

    private static void RunOnUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.Invoke(action);
        }
    }
}
