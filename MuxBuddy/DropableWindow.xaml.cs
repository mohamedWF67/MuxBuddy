using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Shell;
using FFMpegCore;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using ComboBox = System.Windows.Controls.ComboBox;
using DataFormats = System.Windows.DataFormats;
using DataObject = System.Windows.DataObject;
using DragDropEffects = System.Windows.DragDropEffects;
using DragEventArgs = System.Windows.DragEventArgs;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;

namespace MuxBuddy;

public partial class DropableWindow
{
    public ObservableCollection<string> droppedFiles { get; } = new ObservableCollection<string>();
    private Point _dragStartPoint;
    private bool _isDragging;
    
    private bool _allowClose = true;
    private float DesiredSize { get; set; } = 10;
    private event Action<double> onprogress;
    private event Action<FfmpegStats> onStatusUpdated;
    
    private bool _isEncodingEnabled = true;
    private int _bitrateMode = 1;
    
    private VideoInfo _videoInfo;
    private VideoInfo _VideoInfoBeforeEncoding;
    
    public DropableWindow()
    {
        InitializeComponent();
        
        onprogress += Ononprogress;
        onStatusUpdated += OnonStatusUpdated;
    }

    private void OnonStatusUpdated(FfmpegStats obj)
    {
        TimeSpan eta = TimeSpan.FromSeconds(obj.Remaining.TotalSeconds / obj.Speed.Value);
        Dispatcher.Invoke(() =>
        {
            VideoStatsFPS.Text = obj.Fps.ToString();
            VideoStatsElapsed.Text = obj.ElapsedRealtime.ToString(@"hh\:mm\:ss\.fff");
            VideoStatsET.Text = eta.ToString(@"hh\:mm\:ss\.fff");
        });
    }

    private void ToggleVideoProgress(bool isEnabled)
    {
        ProgressPanel.Visibility = isEnabled ? Visibility.Visible : Visibility.Collapsed;
        OutputNameSeparator.Visibility = isEnabled ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Ononprogress(double obj)
    {
        Dispatcher.Invoke(() =>
        {
            ProgressBar.Value = obj;
            if (obj >= 100)
            {
                TaskbarInfo.ProgressState = TaskbarItemProgressState.None;
                
                var hwnd = new WindowInteropHelper(this).Handle;
                WindowAttention.FlashTaskbar(hwnd);

                ProgressBar.Value = 0;
                
                MessageBox.Show($"File is Ready at \n {_videoInfo.OutputPath}");
            }
            else
            {
                TaskbarInfo.ProgressState = TaskbarItemProgressState.Normal;
                TaskbarInfo.ProgressValue = obj / 100.0;
            }
        });
    }

    private void MainWindow_OnDrop(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files.Length > 1)
            {
                MessageBox.Show($"More than one file dropped!", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
                
            switch (Path.GetExtension(files[0]))
            {
                case ".mkv":
                case ".mp4":
                case ".ts":
                    break;
                default:
                    MessageBox.Show($"Unsupported file type", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
            }
                
            if (droppedFiles.Count > 0) ClearFiles();
            droppedFiles.Add(files[0]);
                
            _VideoInfoBeforeEncoding  = FFmpegHelper.ExtractInfo(droppedFiles.First());
            _videoInfo = _VideoInfoBeforeEncoding.Clone();
            
            UpdateVideoBeforeUI();
            ShowVideoEndcoderSettings();
            
            Console.WriteLine("------Files------");
            foreach (var file in droppedFiles)
            {
                Console.WriteLine(file);
            }
            TitleBar.Title = $"Mux Buddy - {droppedFiles.Count}";
        }
    }

    private void HideVideoEndcoderSettings()
    {
        VideoEncoderPropertiesPanel.Visibility = Visibility.Collapsed;
        VideoAfterDataPanel.Visibility = Visibility.Collapsed;
        VideoBeforeDataPanel.Visibility = Visibility.Collapsed;
        MainBtn.IsEnabled = false;
        ClearBtn.IsEnabled = false;
    }
    private void ShowVideoEndcoderSettings()
    {
        VideoEncoderPropertiesPanel.Visibility = Visibility.Visible;
        VideoAfterDataPanel.Visibility = Visibility.Visible;
        VideoBeforeDataPanel.Visibility = Visibility.Visible;
        ClearBtn.IsEnabled = true;
    }

    private void MainBtn_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {   //? Start Video Processing
            FFmpegHelper.CutAndEncodeFromPointToEndPlus(_videoInfo,onStatusUpdated,onprogress, _isEncodingEnabled);
            Console.WriteLine("Start Video");
            VideoViewPanel.ScrollToEnd();
            ToggleVideoProgress(true);
            _videoInfo.Display();
        }
        catch (Exception exception)
        {
            MessageBox.Show($"Error: {exception.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Console.WriteLine(exception);
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            this.Hide();
        }
    }

    public void ExitApp()
    {
        _allowClose = true;
        this.Close();
    }

    private void UpdateVideoInfo()
    {
        // Update Video Duration
        _videoInfo.VideoDuration = TimeSpan.FromSeconds(_videoInfo.EndTime - _videoInfo.StartTime);
        _videoInfo.VideoDurationInSeconds = _videoInfo.VideoDuration.TotalSeconds;
        
        //update Video Audio Properties
        if (_videoInfo.AudioMixMode == 0)
        {
            _videoInfo.AudioStreamsCount = _VideoInfoBeforeEncoding.AudioStreamsCount;
        }else if (_videoInfo.AudioMixMode == 1)
        {
            _videoInfo.AudioStreamsCount = 1;
        }else if (_videoInfo.AudioMixMode == 2)
        {
            _videoInfo.AudioStreamsCount = 3;
        }
        
        // Update Video Bitrate and Video Size
        if (_isEncodingEnabled && _bitrateMode == 1)
        {
            _videoInfo.VideoBitrate = FFmpegHelper.GetBitrateForSize(_videoInfo,DesiredSize);
            _videoInfo.VideoSize = (_videoInfo.VideoBitrate + _videoInfo.AudioStreamsCount * _videoInfo.AudioBitrate) * _videoInfo.VideoDurationInSeconds / 8192;
        }else if (_isEncodingEnabled && _bitrateMode == 2)
        {
            _videoInfo.VideoSize = (_videoInfo.VideoBitrate + _videoInfo.AudioStreamsCount * _videoInfo.AudioBitrate) * _videoInfo.VideoDurationInSeconds / 8192;
        }
        else
        {
            _videoInfo.VideoBitrate = _VideoInfoBeforeEncoding.VideoBitrate;
            _videoInfo.VideoSize = (_videoInfo.VideoBitrate + _videoInfo.AudioStreamsCount * _videoInfo.AudioBitrate) * _videoInfo.VideoDurationInSeconds / 8192;
        }
        
        UpdateVideoUI();
    }
    
    private void UpdateVideoBeforeUI()
    {
        VideoPathTrace.Text = _VideoInfoBeforeEncoding.FileInfo.Name;
        
        VideoBeforeBitRate.Text = _VideoInfoBeforeEncoding.VideoBitrate.ToString("N0") + " kbps";
        VideoBeforeAudioBitrate.Text = _VideoInfoBeforeEncoding.AudioBitrate.ToString("N0") + " kbps";
        VideoBeforeTotalBitrate.Text = (_VideoInfoBeforeEncoding.VideoBitrate + (_VideoInfoBeforeEncoding.AudioBitrate * _VideoInfoBeforeEncoding.AudioStreamsCount)).ToString("N0") + " kbps";
        
        VideoBeforeSize.Text = _VideoInfoBeforeEncoding.VideoSize.ToString("0.00") + " Mb";
        VideoBeforeAudioCount.Text = _VideoInfoBeforeEncoding.AudioStreamsCount.ToString();
        VideoBeforeDuration.Text = _VideoInfoBeforeEncoding.VideoDuration.ToString(@"hh\:mm\:ss\.fff");

        VideoAudioBitrateCombobox.SelectedIndex = 0;
        
        UpdateVideoInfo();
    }
    
    private void UpdateVideoUI()
    {
        VideoStartTimeNumberBox.Value = _videoInfo.StartTime;
        VideoStartTimeTxt.Text = TimeSpan.FromSeconds(_videoInfo.StartTime).ToString(@"hh\:mm\:ss\.fff");
        VideoEndTimeNumberBox.Value = _videoInfo.EndTime;
        VideoEndTimeTxt.Text = TimeSpan.FromSeconds(_videoInfo.EndTime).ToString(@"hh\:mm\:ss\.fff");

        VideoStartTimeNumberBox.Minimum = 0;
        VideoStartTimeNumberBox.Maximum = _videoInfo.EndTime;
        
        VideoEndTimeNumberBox.Minimum = _videoInfo.StartTime;
        VideoEndTimeNumberBox.Maximum = _VideoInfoBeforeEncoding.VideoDurationInSeconds;
        
        Console.WriteLine($"Video start Min time: {VideoStartTimeNumberBox.Minimum}");
        Console.WriteLine($"Video start max Time: {VideoStartTimeNumberBox.Maximum}");
        
        Console.WriteLine($"Video Min time: {VideoEndTimeNumberBox.Minimum}");
        Console.WriteLine($"Video max Time: {VideoEndTimeNumberBox.Maximum}");
        
        CustomSizeNumberBox.Value = DesiredSize;
        VideoCustomBitrateNumberBox.Value = _videoInfo.VideoBitrate;
        
        UpdateExportName();
        VideoAfterPathTrace.Text = Path.GetFileName(_videoInfo.OutputPath);
        
        if (_videoInfo.VideoBitrate < 0 || _videoInfo.VideoBitrate > _VideoInfoBeforeEncoding.VideoBitrate)
        {
            VideoAfterBitRate.Text = "-\u221E Kbps";
            MainBtn.IsEnabled = false;
        }
        else
        {
            MainBtn.IsEnabled = true;
            VideoAfterBitRate.Text = _videoInfo.VideoBitrate.ToString("N0") + " kbps";
        }
        VideoAfterAudioBitrate.Text = _videoInfo.AudioBitrate.ToString("N0") + " kbps";
        VideoAfterTotalBitrate.Text = (_videoInfo.VideoBitrate + (_videoInfo.AudioBitrate * _videoInfo.AudioStreamsCount)).ToString("N0") + " kbps";
        
        VideoAfterSize.Text = _videoInfo.VideoSize.ToString("0.00") + " Mb";
        VideoAfterAudioCount.Text = _videoInfo.AudioStreamsCount.ToString();
        VideoAfterDuration.Text = _videoInfo.VideoDuration.ToString(@"hh\:mm\:ss\.fff");
        
        if (EncodeSettingsPanel != null)
            EncodeSettingsPanel.Visibility = _isEncodingEnabled ? Visibility.Visible : Visibility.Collapsed;
        
        EncodeSwitch.IsChecked = _isEncodingEnabled;

        if ((int)_videoInfo.Encoder != VideoEncoderCombobox.SelectedIndex)
        {
            VideoEncoderCombobox.SelectedIndex = (int)_videoInfo.Encoder;
        }
        
        bool MultiAudioStream = _VideoInfoBeforeEncoding.AudioStreamsCount > 1;
        
        if (VideoAudioMixerCombobox.Items.Count > 1)
        {
            ((ComboBoxItem)VideoAudioMixerCombobox.Items[1]).IsEnabled = MultiAudioStream;
            ((ComboBoxItem)VideoAudioMixerCombobox.Items[2]).IsEnabled = MultiAudioStream;
        }

        if (VideoAudioMixerCombobox.SelectedIndex > 0 && !MultiAudioStream)
        {
            VideoAudioMixerCombobox.SelectedIndex = 0;
        }
        
        
        int AudioComboboxItemsCount = VideoAudioBitrateCombobox.Items.Count;
        for (int i = 1; i < AudioComboboxItemsCount; i++)
        {
            try
            {
                ComboBoxItem cbi = (ComboBoxItem)VideoAudioBitrateCombobox.Items[i];
                int.TryParse(cbi.Content.ToString(), out int result);
                cbi.IsEnabled = result <= _VideoInfoBeforeEncoding.AudioBitrate;
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }
        
        VideoAudioCustomBitrateNumberBox.Value = _videoInfo.AudioBitrate;
        VideoAudioCustomBitrateNumberBox.Maximum = _VideoInfoBeforeEncoding.AudioBitrate;
    }

    private void ClearFiles()
    {
        droppedFiles.Clear();
        _videoInfo = null;
        _VideoInfoBeforeEncoding = null;
        VideoPathTrace.Text = "Drop a video to start";
        HideVideoEndcoderSettings();
        ToggleVideoProgress(false);
        onprogress.Invoke(0);
        DesiredSize = 10;
        _isEncodingEnabled = true;
        TitleBar.Title = $"Mux Buddy";
    }

    private void CustomSizeCheckBox_OnChecked(object sender, RoutedEventArgs e)
    {
        VideoFileSizeDefaultBtns.IsEnabled = false;
        CustomSizeNumberBox.IsEnabled = true;
    }

    private void CustomSizeCheckBox_OnUnChecked(object sender, RoutedEventArgs e)
    {
        VideoFileSizeDefaultBtns.IsEnabled = true;
        CustomSizeNumberBox.IsEnabled = false;
    }

    private void VideoBitratePresetbtn_OnClick(object sender, RoutedEventArgs e)
    {
        string size = (sender as Button).Content.ToString();
        if (float.TryParse(size, out float result))
        {
            DesiredSize = result;
            UpdateVideoInfo();
        }
        else
        {
            Console.WriteLine("Invalid Size");
        }
    }

    private void CustomSizeNumberBox_OnValueChanged(object sender, NumberBoxValueChangedEventArgs args)
    {
        if (args.NewValue <= 0 || args.NewValue == DesiredSize) return;
        DesiredSize = (float)args.NewValue;
        UpdateVideoInfo();
    }

    private void VideoStartTime_OnValueChanged(object sender, NumberBoxValueChangedEventArgs args)
    {
        double? newvalue = args.NewValue;
        Console.WriteLine($"Video Start Time: {_videoInfo.StartTime}");
        if (newvalue > _videoInfo.EndTime || newvalue < 0)
        {
            VideoStartTimeNumberBox.Value = _videoInfo.StartTime;
            return;
        }

        if (!args.NewValue.HasValue)
        {
            _videoInfo.StartTime = 0;
        }else
        {
            _videoInfo.StartTime = args.NewValue.Value;
        } 
        
        UpdateVideoInfo();
    }

    private void VideoEndTime_OnValueChanged(object sender, NumberBoxValueChangedEventArgs args)
    {
        double? newvalue = args.NewValue;
        Console.WriteLine($"Video End Time: {_videoInfo.EndTime}");
        if (newvalue < _videoInfo.StartTime || newvalue > _VideoInfoBeforeEncoding.VideoDurationInSeconds)
        {
            VideoEndTimeNumberBox.Value = _videoInfo.EndTime;
            return;
        }

        if (!args.NewValue.HasValue)
        {
            _videoInfo.EndTime = _VideoInfoBeforeEncoding.VideoDurationInSeconds;
        }
        else
        {
            _videoInfo.EndTime = args.NewValue.Value;
        }
        
        UpdateVideoInfo();
    }

    private void EncodeSwitch_OnUnChecked(object sender, RoutedEventArgs e)
    {
        if (!_isEncodingEnabled) return;
        
        Console.WriteLine($"Encoding Disabled {DateTime.Now}");
        _isEncodingEnabled = false;
        
        UpdateVideoInfo();
    }

    private void EncodeSwitch_OnChecked(object sender, RoutedEventArgs e)
    {
        if (_isEncodingEnabled) return;
        
        Console.WriteLine($"Encoding Enabled {DateTime.Now}");
        _isEncodingEnabled = true;
        
        UpdateVideoInfo();
    }
    
    public void UpdateExportName()
    {
        string ModifiedName = "";
        if (_videoInfo.AudioMixMode == 1 || _videoInfo.AudioMixMode == 2) ModifiedName += "-Resampled";
        if (_isEncodingEnabled && DesiredSize > 0) ModifiedName += $"-{DesiredSize}Mb";
        if (_isEncodingEnabled) ModifiedName += $"-{_videoInfo.Encoder}";
        if (ModifiedName.Equals("")) ModifiedName = "-Modified";
        
        _videoInfo.OutputPath = Path.Combine(Path.GetDirectoryName(_videoInfo.FileInfo.FullName), Path.GetFileNameWithoutExtension(_videoInfo.FileInfo.Name) + ModifiedName + Path.GetExtension(_videoInfo.FileInfo.Name));
        Console.WriteLine($"Output Path: {_videoInfo.OutputPath}");
    }
    
    private void Window_DragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            DropOverlay.Visibility = Visibility.Visible;
            e.Effects = DragDropEffects.Copy;
        }
    }

    private void Window_DragLeave(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
    }

    private void OpenVideo_OnClick(object sender, RoutedEventArgs e)
    {
        Process.Start("explorer.exe", _videoInfo.FileInfo.FullName);
    }

    private void OpenInExporer_OnClick(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"/select,\"{_videoInfo.FileInfo.FullName}\"",
            UseShellExecute = true
        });
    }

    private void VideoCustomBitrateNumberBox_OnValueChanged(object sender, NumberBoxValueChangedEventArgs args)
    {
        if (args.NewValue <= 0 || args.NewValue >= _VideoInfoBeforeEncoding.VideoBitrate || args.NewValue == _videoInfo.VideoBitrate) return;
        
        if (!args.NewValue.HasValue)
            _videoInfo.VideoBitrate = _VideoInfoBeforeEncoding.VideoBitrate;
        else
            _videoInfo.VideoBitrate = (int)args.NewValue;

        UpdateVideoInfo();
    }

    private void BitrateBySizeBtn_OnClick(object sender, RoutedEventArgs e)
    {
        _bitrateMode = 1;
        UpdateVideoInfo();
        VideoBySizePanel.Visibility = Visibility.Visible;
        VideoByBitratePanel.Visibility = Visibility.Collapsed;
    }

    private void BitrateByBitrateBtn_OnClick(object sender, RoutedEventArgs e)
    {
        _bitrateMode = 2;
        UpdateVideoInfo();
        VideoBySizePanel.Visibility = Visibility.Collapsed;
        VideoByBitratePanel.Visibility = Visibility.Visible;
    }

    private void VideoAudioMixerCombobox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var cb = sender as ComboBox;
        if (cb == null || _videoInfo == null) return;
        
        _videoInfo.AudioMixMode = cb.SelectedIndex;
        Console.WriteLine($"Audio Mix Mode: {_videoInfo.AudioMixMode}");
        UpdateVideoInfo();
    }

    private void VideoAudioBitrateCombobox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var cb = sender as ComboBox;
        if (cb == null || _videoInfo == null) return;

        var cbi = cb.SelectedItem as ComboBoxItem;
        if (cbi.Content.ToString().Equals("Default", StringComparison.OrdinalIgnoreCase))
        {
            _videoInfo.AudioBitrate = _VideoInfoBeforeEncoding.AudioBitrate;
        }else if (int.TryParse(cbi.Content.ToString(), out int result))
        {
            _videoInfo.AudioBitrate = result;
        }else if (cbi.Content.ToString().Equals("Custom", StringComparison.OrdinalIgnoreCase))
        {
            VideoAudioCustomBitrateNumberBox.Visibility = Visibility.Visible;
            return;
        }
        else
        {
            _videoInfo.AudioBitrate = 96;
        }
        
        VideoAudioCustomBitrateNumberBox.Visibility = Visibility.Collapsed;
        Console.WriteLine($"Audio Bitrate: {_videoInfo.AudioBitrate}");
        UpdateVideoInfo();
    }

    private void VideoAudioCustomBitrateNumberBox_OnValueChanged(object sender, NumberBoxValueChangedEventArgs args)
    {
        if (args.NewValue <= 0 || args.NewValue >= _VideoInfoBeforeEncoding.AudioBitrate || args.NewValue == _videoInfo.AudioBitrate) return;
        
        if (!args.NewValue.HasValue)
            _videoInfo.AudioBitrate = _VideoInfoBeforeEncoding.AudioBitrate;
        else
            _videoInfo.AudioBitrate = (int)args.NewValue;
        
        Console.WriteLine($"Audio Bitrate: {_videoInfo.AudioBitrate}");
        UpdateVideoInfo();
    }

    private void ClearBtn_OnClick(object sender, RoutedEventArgs e)
    {
        ClearFiles();
    }

    private void VideoEncoderCombobox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var cb = sender as ComboBox;
        if (cb == null || _videoInfo == null) return;

        _videoInfo.Encoder = (VideoEncoder)cb.SelectedIndex;
        Console.WriteLine($"Video Encoder: {_videoInfo.Encoder}");
        UpdateVideoInfo();
    }
}