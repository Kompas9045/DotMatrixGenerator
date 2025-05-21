/*
 <DotMatrixGenerator>
Developer:Gu JiaBin
 
This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.
 
This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.
 
You should have received a copy of the GNU General Public License
along with this program.  If not, see <https://www.gnu.org/licenses/>.
 */

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using AForge.Video;
using AForge.Video.DirectShow;

namespace lattice.DotMatrixGenerator
{
    public class TimeLinePreset
    {
        public int FrameNum { get; set; }
        public double DotSize { get; set; }
        public int Spacing { get; set; }
        public double Brightness { get; set; }
        public double Contrast { get; set; }
        public double Gamma { get; set; }
        public double BlurRadius { get; set; }
        public double SizeScale { get; set; }
        public int AaScale { get; set; }
        public double NoGradientHalftone { get; set; }
        private bool isOpposition;
        public override string ToString()
        {
            return $"第{FrameNum}帧 - 点大小:{DotSize} 间距:{Spacing} 亮度:{Brightness:F2} " +
                   $"对比度:{Contrast:F2} Gamma:{Gamma:F2} 模糊:{BlurRadius:F1} " +
                   $"缩放:{SizeScale:F2} 抗锯齿:{AaScale} 非梯度阈值:{NoGradientHalftone:F2}";
        }
    }

    public static class ProVideoGen
    {
        public static bool ProProcessVideo(string videoPath, string outputDir, TimeLinePreset[] timeLineArray)
        {
            if (!File.Exists(videoPath))
            {
                MessageBox.Show("视频文件不存在");
                return false;
            }

            DirectoryInfo di = new DirectoryInfo(outputDir);
            if (!di.Exists)
            {
                di.Create();
            }

            // 实例化插值器
            try
            {
                using var interpolator = new TimeLinePresetInterpolator(timeLineArray);
                FileVideoSource fileVideo = new FileVideoSource(videoPath);
                int frameCount = 0;
                bool isCompleted = false;

                // 注册视频完成事件
                fileVideo.VideoSourceError += (s, e) =>
                {
                    MessageBox.Show($"视频处理出错: {e.Description}\n发生位置：{e.ToString()}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    if (e.GetType() == typeof(VideoSourceErrorEventArgs))
                    {
                        MessageBoxResult result = MessageBox.Show("这是一个视频来源的错误，请在确保视频文件无误后检查下面的编码问题\n软件使用的是Directshow技术来处理视频，未处理的Windows环境下仅能支持.avi格式，如需支持mp4等格式需要额外安装k-lite codec pack编码库(可以在 https://www.codecguide.com/download_kl.htm 找到，如果没有特殊需要，Basic版本即可，按下yes可以打开网址)支持。", "提示", MessageBoxButton.YesNo, MessageBoxImage.Information);
                        if (result == MessageBoxResult.Yes)
                        {
                            System.Diagnostics.Process.Start(new ProcessStartInfo("https://www.codecguide.com/download_kl.htm")
                            {
                                UseShellExecute = true
                            });
                        }
                    }
                    isCompleted = true;
                };

                fileVideo.PlayingFinished += (s, e) =>
                {
                    isCompleted = true;
                };

                fileVideo.NewFrame += (sender, eventArgs) =>
                {
                    try
                    {
                        // 获取当前帧的参数预设
                        var preset = interpolator.GetPresetAtFrame(frameCount);

                        // 创建对应参数的生成器
                        var generator = new DotMatrixGenerator(
                            dotSize: (float)preset.DotSize,
                            spacing: preset.Spacing,
                            brightness: (float)preset.Brightness,
                            contrast: (float)preset.Contrast,
                            gamma: (float)preset.Gamma,
                            blurRadius: (int)preset.BlurRadius,
                            sizeScale: (float)preset.SizeScale,
                            aaScale: preset.AaScale,
                            noGradientHalftoneStyleThreshold: (float)preset.NoGradientHalftone
                        );

                        using (Bitmap frame = (Bitmap)eventArgs.Frame.Clone())
                        {
                            string outputPath = Path.Combine(outputDir, $"frame_{frameCount:D4}.png");
                            using (Bitmap processedFrame = generator.ProcessFrame(frame))
                            {
                                processedFrame.Save(outputPath, ImageFormat.Png);
                            }
                        }
                        frameCount++;
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"处理帧 {frameCount} 时出错: {ex.Message}");
                    }
                };

                try
                {
                    // 确认/取消的消息框，并获取用户的响应
                    MessageBoxResult result = MessageBox.Show("确认以开始处理视频", "确认操作", MessageBoxButton.OKCancel, MessageBoxImage.Question);
                    if (result == MessageBoxResult.OK)
                    {
                        // 用户点击了“确认”，继续执行
                    }
                    else
                    {
                        // 用户点击了“取消”，抛出异常
                        throw new OperationCanceledException("用户取消了操作");
                    }

                    fileVideo.Start();

                    // 等待处理完成
                    while (!isCompleted)
                    {

                        Thread.Sleep(100);
                    }

                    MessageBox.Show($"视频处理完成，共处理 {frameCount} 帧");
                    return true;
                }
                finally
                {
                    if (fileVideo.IsRunning)
                    {
                        fileVideo.SignalToStop();
                        fileVideo.WaitForStop();
                    }

                    // 释放插值器内存
                    interpolator.Dispose();
                }
            }
            catch (Exception e)
            {
                MessageBox.Show($"视频处理出错: {e.Message}\n{e.Data}\n发生位置：{e.Source}",
                    "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                if (e.GetType() == typeof(VideoSourceErrorEventArgs))
                {
                    MessageBoxResult result = MessageBox.Show("这是一个视频来源的错误，请在确保视频文件无误后检查下面的编码问题\n软件使用的是Directshow技术来处理视频，未处理的Windows环境下仅能支持.avi格式，如需支持mp4等格式需要额外安装k-lite codec pack编码库(可以在 https://www.codecguide.com/download_kl.htm 找到，如果没有特殊需要，Basic版本即可，按下yes可以打开网址)支持。", "提示", MessageBoxButton.YesNo, MessageBoxImage.Information);
                    if (result == MessageBoxResult.Yes)
                    {
                        System.Diagnostics.Process.Start(new ProcessStartInfo("https://www.codecguide.com/download_kl.htm")
                        {
                            UseShellExecute = true
                        });
                    }
                }
                return false;
            }
        }
    }
    

    // 插值器类
    public class TimeLinePresetInterpolator : IDisposable
    {
        private List<TimeLinePreset> _sortedPresets;
        private bool _disposed;

        // 构造函数：接受 TimeLinePreset 数组并按 FrameNum 排序
        public TimeLinePresetInterpolator(TimeLinePreset[] presets)
        {
            if (presets == null || presets.Length == 0)
                throw new ArgumentException("Presets array cannot be null or empty.");

            // 复制并按 FrameNum 排序
            _sortedPresets = presets.OrderBy(p => p.FrameNum).ToList();

            // 检查是否有重复的 FrameNum
            for (int i = 1; i < _sortedPresets.Count; i++)
            {
                if (_sortedPresets[i].FrameNum == _sortedPresets[i - 1].FrameNum)
                    throw new ArgumentException($"Duplicate FrameNum found: {_sortedPresets[i].FrameNum}");
            }
        }

        // 获取插值后的 TimeLinePreset
        public TimeLinePreset GetPresetAtFrame(int frameNum)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(TimeLinePresetInterpolator));

            if (_sortedPresets.Count == 0)
                throw new InvalidOperationException("No presets available.");

            // 获取最小和最大 FrameNum
            int minFrame = _sortedPresets[0].FrameNum;
            int maxFrame = _sortedPresets[^1].FrameNum;

            // 1. 小于最小 FrameNum：返回第一个 preset 的值
            if (frameNum < minFrame)
                return ClonePreset(_sortedPresets[0], frameNum);

            // 2. 大于最大 FrameNum：返回最后一个 preset 的值
            if (frameNum > maxFrame)
                return ClonePreset(_sortedPresets[^1], frameNum);

            // 3. 在范围内：查找插值区间
            for (int i = 0; i < _sortedPresets.Count - 1; i++)
            {
                var prev = _sortedPresets[i];
                var next = _sortedPresets[i + 1];

                if (frameNum == prev.FrameNum)
                    return ClonePreset(prev, frameNum);

                if (frameNum > prev.FrameNum && frameNum < next.FrameNum)
                {
                    // 线性插值
                    double t = (double)(frameNum - prev.FrameNum) / (next.FrameNum - prev.FrameNum);
                    return InterpolatePresets(prev, next, t, frameNum);
                }
            }

            // 精确匹配最后一个 FrameNum
            if (frameNum == maxFrame)
                return ClonePreset(_sortedPresets[^1], frameNum);

            throw new InvalidOperationException("Unexpected error in frame interpolation.");
        }

        // 线性插值两个 preset
        private TimeLinePreset InterpolatePresets(TimeLinePreset prev, TimeLinePreset next, double t, int frameNum)
        {
            return new TimeLinePreset
            {
                FrameNum = frameNum,
                DotSize = prev.DotSize + (next.DotSize - prev.DotSize) * t, 
                Spacing = (int)Math.Round(prev.Spacing + (next.Spacing - prev.Spacing) * t),
                Brightness = prev.Brightness + (next.Brightness - prev.Brightness) * t,
                Contrast = prev.Contrast + (next.Contrast - prev.Contrast) * t,
                Gamma = prev.Gamma + (next.Gamma - prev.Gamma) * t,
                BlurRadius = prev.BlurRadius + (next.BlurRadius - prev.BlurRadius) * t,
                SizeScale = prev.SizeScale + (next.SizeScale - prev.SizeScale) * t,
                AaScale = (int)Math.Round(prev.AaScale + (next.AaScale - prev.AaScale) * t),
                NoGradientHalftone = prev.NoGradientHalftone + (next.NoGradientHalftone - prev.NoGradientHalftone) * t
            };
        }

        // 克隆 preset 并设置新的 FrameNum
        private TimeLinePreset ClonePreset(TimeLinePreset source, int frameNum)
        {
            return new TimeLinePreset
            {
                FrameNum = frameNum,
                DotSize = source.DotSize,
                Spacing = source.Spacing,
                Brightness = source.Brightness,
                Contrast = source.Contrast,
                Gamma = source.Gamma,
                BlurRadius = source.BlurRadius,
                SizeScale = source.SizeScale,
                AaScale = source.AaScale,
                NoGradientHalftone = source.NoGradientHalftone
            };
        }

        // 内存释放
        public void Dispose()
        {
            if (!_disposed)
            {
                _sortedPresets?.Clear();
                _sortedPresets = null;
                _disposed = true;
            }
        }

        // 显式实现 IDisposable
        ~TimeLinePresetInterpolator()
        {
            Dispose();
        }
    }
}

