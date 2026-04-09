# Mux Buddy

A Windows WPF application for video encoding and processing with GPU acceleration using FFmpeg.

## Features

- **Drag & Drop Interface**: Simple drag-and-drop functionality for video files
- **GPU-Accelerated Encoding**: NVIDIA CUDA hardware acceleration (h264_nvenc)
- **Smart Video Processing**:
  - Cut and trim videos by start/end time
  - Target specific file sizes with automatic bitrate calculation
  - Custom bitrate control
  - Audio stream mixing and management
  - Real-time encoding progress with FPS, speed, and ETA stats

## Supported Formats

- `.mkv`
- `.mp4`
- `.ts`

## Requirements

- **Windows** 10/11 with .NET 9.0
- **FFmpeg**

## Dependencies

- [FFMpegCore](https://github.com/rosenbjerg/FFMpegCore) v5.4.0
- [WPF-UI](https://github.com/lepoco/wpfui) v4.2.0

## Usage

1. Launch Mux Buddy
2. Drag and drop a video file onto the window
3. Configure encoding settings:
   - **Encoding Mode**: Choose between target size or custom bitrate
   - **Time Range**: Set start and end times to trim the video
   - **Audio Options**: Mix multiple audio streams or keep originals
   - **Audio Bitrate**: Adjust audio quality
4. Click the encode button to process
5. Monitor real-time progress with FPS, elapsed time, and ETA

## Audio Mixing Modes

- **Keep Original**: Preserve all audio streams as-is
- **Mix ALL**: Combine multiple audio streams into one
- **Mix + Keep**: Create merged stream while keeping originals

## Technical Details

- **Video Codec**: H.264/H265 with NVENC/AMD/CPU
- **Audio Codec**: AAC
- **Encoding Preset**: P4 (balanced quality/speed)
- **Rate Control**: CBR HQ (Constant Bitrate High Quality)
- **Bitrate Modifier**: 0.95 safety factor for size calculations

## Version

Current Version: **0.1.1**

## Author

Mohamed Waleed - Wela Corp

## License

See project license file for details.
