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
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using System.IO;
using System.Drawing;
using lattice.DotMatrixGenerator;
using System.Windows.Threading;
using AForge.Video.DirectShow;
using AForge.Video;

namespace lattice
{
    public partial class MainWindow : Window
    {
        private string imagePath;
        private Bitmap originalImage;
        private Bitmap processedImage;
        private DotMatrixGenerator.DotMatrixGenerator generator;
        private DispatcherTimer updateTimer;

        public MainWindow()
        {
            InitializeComponent();

            // 创建一个延迟更新定时器，避免滑块值改变时立即更新
            updateTimer = new DispatcherTimer();
            updateTimer.Interval = TimeSpan.FromMilliseconds(100);
            updateTimer.Tick += UpdateTimer_Tick;
        }

        private void SelectImage_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "图片文件|*.jpg;*.jpeg;*.png;*.bmp|所有文件|*.*"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                imagePath = openFileDialog.FileName;
                originalImage = new Bitmap(imagePath);
                UpdateImage();
            }
        }

        private void SaveImage_Click(object sender, RoutedEventArgs e)
        {
            if (processedImage == null)
            {
                MessageBox.Show("请先选择并处理图片", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SaveFileDialog saveFileDialog = new SaveFileDialog
            {
                Filter = "PNG文件|*.png|JPEG文件|*.jpg|SVG矢量图文件(支持Adobe Illustrator)|*.svg|所有文件|*.*",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                try
                {
                    string ext = Path.GetExtension(saveFileDialog.FileName).ToLower();
                    if (ext == ".svg")
                    {
                        generator.SaveAsSvg(imagePath, saveFileDialog.FileName);
                    }
                    else
                    {
                        processedImage.Save(saveFileDialog.FileName);
                    }
                    MessageBox.Show("图片已成功导出", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"导出图片时出错：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async void ProcessVideo_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "视频文件|*.mp4;*.avi;*.mov|所有文件|*.*",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)
            };

            if (openFileDialog.ShowDialog() == true)
            {
                var folderDialog = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "选择输出目录",
                    FileName = "选择此文件夹", // 这只是一个提示文本
                    InitialDirectory = Path.GetDirectoryName(openFileDialog.FileName),
                    ValidateNames = false,
                    CheckFileExists = false,
                    CheckPathExists = true,
                    OverwritePrompt = false
                };

                if (folderDialog.ShowDialog() == true)
                {
                    string outputDir = Path.GetDirectoryName(folderDialog.FileName);
                    ProgressWindow progressWindow = null;
                    try
                    {
                        UpdateGenerator();
                        progressWindow = new ProgressWindow();
                        progressWindow.Owner = this;
                        progressWindow.Show();

                        // 处理视频时要禁用处理视频的按钮
                        ProcessVideoBtn.IsEnabled = false;
                        ProProcessVideoBtn.IsEnabled = false;
                        SelectVideoBtn.IsEnabled = false;

                        await Task.Run(() => 
                        {
                            generator.ProcessVideo(openFileDialog.FileName, outputDir);
                            // 确保在UI线程上关闭窗口
                            Dispatcher.Invoke(() => 
                            {
                                if (progressWindow != null && progressWindow.IsLoaded)
                                {
                                    progressWindow.AllowClosing = true; // 允许程序关闭
                                    progressWindow.Close();

                                    // 恢复处理视频的按钮
                                    ProcessVideoBtn.IsEnabled = true;
                                    ProProcessVideoBtn.IsEnabled = true;
                                    SelectVideoBtn.IsEnabled = true;
                                }
                            });
                        });
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"处理视频时出错：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                        if (progressWindow != null && progressWindow.IsLoaded)
                        {
                            progressWindow.AllowClosing = true; // 允许程序关闭
                            progressWindow.Close();

                            // 恢复处理视频的按钮
                            ProcessVideoBtn.IsEnabled = true;
                            ProProcessVideoBtn.IsEnabled = true;
                            SelectVideoBtn.IsEnabled = true;
                        }
                    }
                }
            }
        }

        private void Parameter_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (updateTimer == null)
            {
                // 如果定时器未初始化，则直接更新图像
                UpdateImage();
                return;
            }

            // 重置定时器
            updateTimer.Stop();
            updateTimer.Start();
        }

        private void UpdateTimer_Tick(object sender, EventArgs e)
        {
            updateTimer.Stop();
            UpdateImage();
        }

        private void UpdateGenerator()
        {
            int blurRadius = (int)BlurRadiusSlider.Value;
            if (blurRadius % 2 == 0) blurRadius++;

            generator = new DotMatrixGenerator.DotMatrixGenerator(
                dotSize: (int)DotSizeSlider.Value,
                spacing: (int)SpacingSlider.Value,
                brightness: (float)BrightnessSlider.Value,
                contrast: (float)ContrastSlider.Value,
                gamma: (float)GammaSlider.Value,
                blurRadius: blurRadius,
                sizeScale: (float)SizeScaleSlider.Value,
                aaScale: (int)AaScaleSlider.Value,
                noGradientHalftoneStyleThreshold: (float)NoGradientHalftoneSlider.Value,
                isOpposition: (bool)OppositionCheckBox.IsChecked
            );
        }

        private void UpdateImage()
        {
            if (originalImage == null) return;

            UpdateGenerator();

            // 处理图像
            using (var grayImage = new Bitmap(originalImage.Width, originalImage.Height))
            {
                using (var g = Graphics.FromImage(grayImage))
                {
                    var colorMatrix = new System.Drawing.Imaging.ColorMatrix(new float[][]
                    {
                        new float[] {0.299f, 0.299f, 0.299f, 0, 0},
                        new float[] {0.587f, 0.587f, 0.587f, 0, 0},
                        new float[] {0.114f, 0.114f, 0.114f, 0, 0},
                        new float[] {0, 0, 0, 1, 0},
                        new float[] {0, 0, 0, 0, 1}
                    });

                    var attributes = new System.Drawing.Imaging.ImageAttributes();
                    attributes.SetColorMatrix(colorMatrix);

                    g.DrawImage(originalImage,
                        new Rectangle(0, 0, originalImage.Width, originalImage.Height),
                        0, 0, originalImage.Width, originalImage.Height,
                        GraphicsUnit.Pixel,
                        attributes);
                }

                processedImage = generator.ProcessImage(grayImage);
            }

            // 转换为WPF图像并显示
            PreviewImage.Source = BitmapToImageSource(processedImage);
        }

        private BitmapSource BitmapToImageSource(Bitmap bitmap)
        {
            var handle = bitmap.GetHbitmap();
            try
            {
                return System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                    handle,
                    IntPtr.Zero,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
            }
            finally
            {
                DeleteObject(handle);
            }
        }

        [System.Runtime.InteropServices.DllImport("gdi32.dll")]
        public static extern bool DeleteObject(IntPtr hObject);

        //===================================视频时间轴逻辑=============================================
        //删除时间轴点预设
        private void DeletePreset_Click(object sender, RoutedEventArgs e)
        {
            if (TimeLineListBox.SelectedItem != null)
            {
                TimeLineListBox.Items.Remove(TimeLineListBox.SelectedItem);
            }
        }

        //保存时间轴点预设
        private void SavePreset_Click(object sender, RoutedEventArgs e)
        {
            var preset = new TimeLinePreset
            {
                FrameNum = (int)VideoTimelineSlider.Value,
                DotSize = (int)DotSizeSlider.Value,
                Spacing = (int)SpacingSlider.Value,
                Brightness = BrightnessSlider.Value,
                Contrast = ContrastSlider.Value,
                Gamma = GammaSlider.Value,
                BlurRadius = BlurRadiusSlider.Value,
                SizeScale = SizeScaleSlider.Value,
                AaScale = (int)AaScaleSlider.Value,
                NoGradientHalftone = NoGradientHalftoneSlider.Value
            };

            TimeLineListBox.Items.Add(preset);
        }

        //加载时间轴点预设
        private void PresetListBox_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (TimeLineListBox.SelectedItem is TimeLinePreset preset)
            {
                VideoTimelineSlider.Value = preset.FrameNum;
                DotSizeSlider.Value = preset.DotSize;
                SpacingSlider.Value = preset.Spacing;
                BrightnessSlider.Value = preset.Brightness;
                ContrastSlider.Value = preset.Contrast;
                GammaSlider.Value = preset.Gamma;
                BlurRadiusSlider.Value = preset.BlurRadius;
                SizeScaleSlider.Value = preset.SizeScale;
                AaScaleSlider.Value = preset.AaScale;
                NoGradientHalftoneSlider.Value = preset.NoGradientHalftone;
            }
        }

        //时间轴点拖动逻辑
        private void VideoTimeline_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {

        }

        // 高级视频处理中的选择视频
        private void SelectVideo_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "视频文件|*.mp4;*.avi;*.mov|所有文件|*.*",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)
            };

            if (openFileDialog.ShowDialog() == true)
            {
                VideoPathTextBox.Text = openFileDialog.FileName;

                FileVideoSource videoSource = new FileVideoSource(VideoPathTextBox.Text);

                int frameCount = 0;

                videoSource.NewFrame += (s, reason) =>
                {
                    frameCount++;
                };

                videoSource.PlayingFinished += (s, reason) =>
                {
                    // 使用 Dispatcher 在 UI 线程上更新控件
                    Dispatcher.Invoke(() =>
                    {
                        MessageBox.Show($"视频帧数计数处理结束，原因：{reason}");
                        
                        VideoTimelineSlider.Maximum = frameCount > 0 ? frameCount - 1 : 0;
                        VideoTimelineSlider.Value = 0;

                        //videoSource.SignalToStop();
                        //videoSource.WaitForStop();
                    });
                };

                videoSource.VideoSourceError += (s, err) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        MessageBox.Show($"视频帧数计数处理出错: {err.Description}\n发生位置：{err.ToString()}", 
                            "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    });
                };

                videoSource.Start();
            }
        }

        // 高级视频处理中的选择输出文件夹
        private void SelectOutputDir_Click(object sender, RoutedEventArgs e)
        {
            var folderDialog = new SaveFileDialog
            {
                Title = "选择输出目录",
                FileName = "选择此文件夹",
                ValidateNames = false,
                CheckFileExists = false,
                CheckPathExists = true,
                OverwritePrompt = false
            };

            if (folderDialog.ShowDialog() == true)
            {
                OutputPathTextBox.Text = Path.GetDirectoryName(folderDialog.FileName);
            }
        }

        // 带时间轴适配的视频处理方法
        private void ProProcessVideo_Click(object sender, RoutedEventArgs e)
        {
            if (VideoPathTextBox.Text == String.Empty)
            {
                MessageBox.Show("未填写输入视频路径",
                    "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            else if (OutputPathTextBox.Text == String.Empty)
            {
                MessageBox.Show("未填写视频输出路径",
                    "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            else if (TimeLineListBox.Items.Count == 0)
            {
                MessageBox.Show("空时间轴列表",
                    "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            else
            {
                ProProcessVideoBtn.IsEnabled = false;
                bool isOK = ProVideoGen.ProProcessVideo(VideoPathTextBox.Text, OutputPathTextBox.Text, TimeLineListBox.Items.Cast<TimeLinePreset>().ToArray());
                if (isOK)
                {
                    ProProcessVideoBtn.IsEnabled = true;
                }
                else
                {
                    ProProcessVideoBtn.IsEnabled = true;
                }
            }
            
        }
    }

}