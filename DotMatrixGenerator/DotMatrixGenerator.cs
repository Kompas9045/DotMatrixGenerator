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
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using AForge.Video;
using AForge.Video.DirectShow;
using Size = System.Drawing.Size;

namespace lattice.DotMatrixGenerator
{
    public class DotMatrixGenerator
    {
        /// <summary>
        /// 每个点的大小（像素）
        /// </summary>
        private float dotSize;

        /// <summary>
        /// 点之间的间距（像素）
        /// </summary>
        private int spacing;

        /// <summary>
        /// 点大小加间距的总和
        /// </summary>
        private float gridSize;  // 改为 float 类型

        /// <summary>
        /// 亮度调整值，范围[-255, 255]
        /// </summary>
        private float brightness;

        /// <summary>
        /// 对比度调整值，范围[0, 3.0]
        /// </summary>
        private float contrast;

        /// <summary>
        /// gamma值，范围[0.1, 10.0]
        /// </summary>
        private float gamma;

        /// <summary>
        /// 高斯模糊半径，必须是奇数
        /// </summary>
        private int blurRadius;

        /// <summary>
        /// 输出图像相对于输入图像的尺寸倍数
        /// </summary>
        private float sizeScale;

        /// <summary>
        /// 抗锯齿缩放因子，范围[1, 3]
        /// </summary>
        private int aaScale;

        /// <summary>
        /// 非梯度半色调风格阈值，取0时不使用梯度半色调风格，取其它数字时使用梯度半色调风格且值为风格过滤阈值，范围[0, 1]
        /// </summary>
        private float noGradientHalftoneStyleThreshold;

        /// <summary>
        /// 反相，取false时为白底黑点
        /// </summary>
        private bool isOpposition;

        /// <summary>
        /// 初始化点阵图生成器
        /// </summary>
        /// <param name="dotSize">每个点的大小（像素）</param>
        /// <param name="spacing">点之间的间距（像素）</param>
        /// <param name="brightness">亮度调整值，范围[-255, 255]</param>
        /// <param name="contrast">对比度调整值，范围[0, 3.0]</param>
        /// <param name="gamma">gamma值，范围[0.1, 10.0]</param>
        /// <param name="blurRadius">高斯模糊半径，必须是奇数</param>
        /// <param name="sizeScale">输出图像相对于输入图像的尺寸倍数</param>
        /// <param name="aaScale">抗锯齿缩放因子，范围[1, 3]</param>
        /// <param name="noGradientHalftoneStyleThreshold">非梯度半色调风格阈值，取0时不使用梯度半色调风格，取其它数字时使用梯度半色调风格且值为风格过滤阈值</param>
        /// <param name="isOpposition">反相，取false时为白底黑点</param>
        public DotMatrixGenerator(
            float dotSize = 10,
            int spacing = 2,
            float brightness = 0,
            float contrast = 1.0f,
            float gamma = 1.0f,
            int blurRadius = 0,
            float sizeScale = 1.0f,
            int aaScale = 2,
            float noGradientHalftoneStyleThreshold = 0f,
            bool isOpposition = false)
        {
            this.dotSize = dotSize;
            this.spacing = spacing;
            this.gridSize = dotSize + spacing;  // 直接使用浮点运算
            this.brightness = brightness;
            this.contrast = contrast;
            this.gamma = gamma;
            this.blurRadius = blurRadius % 2 == 1 ? blurRadius : blurRadius + 1;
            this.sizeScale = sizeScale;
            this.aaScale = Math.Max(1, Math.Min(3, aaScale));
            this.noGradientHalftoneStyleThreshold = noGradientHalftoneStyleThreshold;
            this.isOpposition = isOpposition;
        }

        private Bitmap PreprocessImage(Bitmap image)
        {
            // 创建一个新的Bitmap来存储处理后的图像
            Bitmap processedImage = new Bitmap(image.Width, image.Height);

            // 创建颜色矩阵来应用亮度和对比度
            float[][] colorMatrixElements = {
                new float[] {contrast, 0, 0, 0, 0},
                new float[] {0, contrast, 0, 0, 0},
                new float[] {0, 0, contrast, 0, 0},
                new float[] {0, 0, 0, 1, 0},
                new float[] {brightness, brightness, brightness, 0, 1}
            };

            ColorMatrix colorMatrix = new ColorMatrix(colorMatrixElements);

            // 创建图像属性并设置颜色矩阵
            ImageAttributes imageAttributes = new ImageAttributes();
            imageAttributes.SetColorMatrix(colorMatrix);

            // 应用颜色变换
            using (Graphics g = Graphics.FromImage(processedImage))
            {
                g.DrawImage(image,
                    new Rectangle(0, 0, image.Width, image.Height),
                    0, 0, image.Width, image.Height,
                    GraphicsUnit.Pixel,
                    imageAttributes);
            }

            // 应用Gamma校正
            if (Math.Abs(gamma - 1.0f) > float.Epsilon)
            {
                ApplyGammaCorrection(processedImage, gamma);
            }

            // 应用高斯模糊
            if (blurRadius > 0)
            {
                processedImage = ApplyBlur(processedImage, blurRadius);
            }

            return processedImage;
        }

        private void ApplyGammaCorrection(Bitmap image, float gamma)
        {
            byte[] gammaLookup = new byte[256];
            for (int i = 0; i < 256; i++)
            {
                gammaLookup[i] = (byte)(Math.Pow(i / 255.0, 1.0 / gamma) * 255.0);
            }

            BitmapData bmpData = image.LockBits(
                new Rectangle(0, 0, image.Width, image.Height),
                ImageLockMode.ReadWrite,
                PixelFormat.Format32bppArgb);

            int bytes = Math.Abs(bmpData.Stride) * image.Height;
            byte[] rgbValues = new byte[bytes];

            System.Runtime.InteropServices.Marshal.Copy(bmpData.Scan0, rgbValues, 0, bytes);

            for (int i = 0; i < bytes; i += 4)
            {
                rgbValues[i] = gammaLookup[rgbValues[i]];     // Blue
                rgbValues[i + 1] = gammaLookup[rgbValues[i + 1]]; // Green
                rgbValues[i + 2] = gammaLookup[rgbValues[i + 2]]; // Red
            }

            System.Runtime.InteropServices.Marshal.Copy(rgbValues, 0, bmpData.Scan0, bytes);
            image.UnlockBits(bmpData);
        }

        private Bitmap ApplyBlur(Bitmap image, int radius)
        {
            // 根据半径计算权重
            float weight = 1.0f / (radius * 2 + 1);
            
            // 创建模糊矩阵
            float[][] matrixElements = {
                new float[] {weight, weight, weight, 0, 0},
                new float[] {weight, weight, weight, 0, 0},
                new float[] {weight, weight, weight, 0, 0},
                new float[] {0, 0, 0, 1, 0},
                new float[] {0, 0, 0, 0, 1}
            };

            ColorMatrix colorMatrix = new ColorMatrix(matrixElements);
            ImageAttributes imageAttributes = new ImageAttributes();
            imageAttributes.SetColorMatrix(colorMatrix);

            // 创建输出图像
            Bitmap blurredImage = new Bitmap(image.Width, image.Height);
            using (Graphics g = Graphics.FromImage(blurredImage))
            {
                g.DrawImage(image,
                    new Rectangle(0, 0, image.Width, image.Height),
                    0, 0, image.Width, image.Height,
                    GraphicsUnit.Pixel,
                    imageAttributes);
            }

            // 如果需要更强的模糊效果，可以多次应用
            if (radius > 3)
            {
                using (Graphics g = Graphics.FromImage(blurredImage))
                {
                    g.DrawImage(blurredImage,
                        new Rectangle(0, 0, image.Width, image.Height),
                        0, 0, image.Width, image.Height,
                        GraphicsUnit.Pixel,
                        imageAttributes);
                }
            }

            return blurredImage;
        }

        private Bitmap GenerateDotMatrix(Bitmap grayImage)
        {
            int height = grayImage.Height;
            int width = grayImage.Width;
            Bitmap originalGrayImage = grayImage;
            Bitmap result = null;

            try
            {
                // 如果需要缩放，先对图像进行缩放
                if (Math.Abs(sizeScale - 1.0f) > float.Epsilon)
                {
                    int scaledWidth = (int)(width * sizeScale);
                    int scaledHeight = (int)(height * sizeScale);
                    Bitmap scaledImage = new Bitmap(scaledWidth, scaledHeight);

                    using (Graphics g = Graphics.FromImage(scaledImage))
                    {
                        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        g.DrawImage(grayImage, 0, 0, scaledWidth, scaledHeight);
                    }

                    grayImage = scaledImage;
                    height = scaledHeight;
                    width = scaledWidth;
                }

                // 计算网格数量
                int gridH = (int)(height / gridSize);
                int gridW = (int)(width / gridSize);

                // 创建高分辨率画布
                using (Bitmap largeOutput = new Bitmap(width * aaScale, height * aaScale))
                {
                    using (Graphics g = Graphics.FromImage(largeOutput))
                    {
                        // 背景颜色
                        if (isOpposition){g.Clear(Color.Black);}else{g.Clear(Color.White);}
                       
                        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

                        // 遍历每个网格
                        for (int i = 0; i < gridH; i++)
                        {
                            for (int j = 0; j < gridW; j++)
                            {
                                float y1 = i * gridSize;  // 使用浮点计算
                                float x1 = j * gridSize;  // 使用浮点计算

                                // 计算当前网格的平均灰度值（需要修改 CalculateAverageIntensity 方法）
                                float avgIntensity = CalculateAverageIntensity(grayImage, (int)x1, (int)y1, (int)gridSize);
                                
                                // 计算点的半径
                                float dotRadius = (255 - avgIntensity) * gridSize / (2 * 255);

                                // 无渐变半色调风格
                                if (noGradientHalftoneStyleThreshold != 0)
                                {
                                    if (dotRadius > noGradientHalftoneStyleThreshold) dotRadius = (255 - 0) * gridSize / (2 * 255); else dotRadius = 0f;
                                }

                                if (dotRadius > 0)
                                {
                                    float centerY = (y1 + gridSize / 2) * aaScale;
                                    float centerX = (x1 + gridSize / 2) * aaScale;

                                    using (SolidBrush brush = new SolidBrush(Color.Black))
                                    {
                                        // 根据 isOpposition 来设置画刷颜色
                                        if (isOpposition){brush.Color = Color.White;}else{brush.Color = Color.Black;}

                                        g.FillEllipse(brush,
                                            centerX - dotRadius * aaScale,
                                            centerY - dotRadius * aaScale,
                                            dotRadius * 2 * aaScale,
                                            dotRadius * 2 * aaScale);
                                    }
                                }
                            }
                        }
                    }

                    // 缩小回原始尺寸
                    result = new Bitmap(width, height);
                    using (Graphics g = Graphics.FromImage(result))
                    {
                        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        g.DrawImage(largeOutput, 0, 0, width, height);
                    }
                }

                return result;
            }
            finally
            {
                // 如果进行了缩放，清理缩放后的图像
                if (grayImage != originalGrayImage)
                {
                    grayImage.Dispose();
                }
            }
        }

        private float CalculateAverageIntensity(Bitmap image, int x, int y, int size)
        {
            float sum = 0;
            int count = 0;

            for (int i = y; i < Math.Min(y + size, image.Height); i++)
            {
                for (int j = x; j < Math.Min(x + size, image.Width); j++)
                {
                    Color pixel = image.GetPixel(j, i);
                    sum += (pixel.R * 0.299f + pixel.G * 0.587f + pixel.B * 0.114f);
                    count++;
                }
            }

            return sum / count;
        }

        public Bitmap ProcessImage(string imagePath, Size? outputSize = null)
        {
            using (Bitmap originalImage = new Bitmap(imagePath))
            {
                // 转换为灰度图
                Bitmap grayImage = new Bitmap(originalImage.Width, originalImage.Height);
                using (Graphics g = Graphics.FromImage(grayImage))
                {
                    ColorMatrix colorMatrix = new ColorMatrix(new float[][]
                    {
                        new float[] {0.299f, 0.299f, 0.299f, 0, 0},
                        new float[] {0.587f, 0.587f, 0.587f, 0, 0},
                        new float[] {0.114f, 0.114f, 0.114f, 0, 0},
                        new float[] {0, 0, 0, 1, 0},
                        new float[] {0, 0, 0, 0, 1}
                    });

                    ImageAttributes attributes = new ImageAttributes();
                    attributes.SetColorMatrix(colorMatrix);

                    g.DrawImage(originalImage,
                        new Rectangle(0, 0, originalImage.Width, originalImage.Height),
                        0, 0, originalImage.Width, originalImage.Height,
                        GraphicsUnit.Pixel,
                        attributes);
                }

                // 如果指定了输出尺寸，调整图片大小
                if (outputSize.HasValue)
                {
                    Bitmap resizedImage = new Bitmap(grayImage, outputSize.Value);
                    grayImage.Dispose();
                    grayImage = resizedImage;
                }

                // 预处理图像
                using (Bitmap processedImage = PreprocessImage(grayImage))
                {
                    return GenerateDotMatrix(processedImage);
                }
            }
        }

        public Bitmap ProcessImage(Bitmap inputImage, Size? outputSize = null)
        {
            // 转换为灰度图
            Bitmap grayImage = new Bitmap(inputImage.Width, inputImage.Height);
            using (Graphics g = Graphics.FromImage(grayImage))
            {
                ColorMatrix colorMatrix = new ColorMatrix(new float[][]
                {
                    new float[] {0.299f, 0.299f, 0.299f, 0, 0},
                    new float[] {0.587f, 0.587f, 0.587f, 0, 0},
                    new float[] {0.114f, 0.114f, 0.114f, 0, 0},
                    new float[] {0, 0, 0, 1, 0},
                    new float[] {0, 0, 0, 0, 1}
                });

                ImageAttributes attributes = new ImageAttributes();
                attributes.SetColorMatrix(colorMatrix);

                g.DrawImage(inputImage,
                    new Rectangle(0, 0, inputImage.Width, inputImage.Height),
                    0, 0, inputImage.Width, inputImage.Height,
                    GraphicsUnit.Pixel,
                    attributes);
            }

            // 如果指定了输出尺寸，调整图片大小
            if (outputSize.HasValue)
            {
                Bitmap resizedImage = new Bitmap(grayImage, outputSize.Value);
                grayImage.Dispose();
                grayImage = resizedImage;
            }

            // 预处理图像
            using (Bitmap processedImage = PreprocessImage(grayImage))
            {
                return GenerateDotMatrix(processedImage);
            }
        }

        // 视频帧计数器
        public void ProcessVideo(string videoPath, string outputDir)
        {
            if (!File.Exists(videoPath))
            {
                MessageBox.Show("视频文件不存在");
                return;
            }

            DirectoryInfo di = new DirectoryInfo(outputDir);
            if (!di.Exists)
            {
                di.Create();
            }

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
                    using (Bitmap frame = (Bitmap)eventArgs.Frame.Clone())
                    {
                        string outputPath = Path.Combine(outputDir, $"frame_{frameCount:D4}.png");
                        using (Bitmap processedFrame = ProcessFrame(frame))
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
                // 显示确认/取消的消息框，并获取用户的响应
                MessageBoxResult result = MessageBox.Show("确认以开始处理视频", "确认", MessageBoxButton.OKCancel, MessageBoxImage.Question);

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
                    // 这里原先是保持ui响应，但是出现问题，所以暂时注释掉
                    // Application.DoEvents();
                    Thread.Sleep(100);
                }

                MessageBox.Show($"视频预处理完成，共处理 {frameCount} 帧");
            }
            finally
            {
                if (fileVideo.IsRunning)
                {
                    fileVideo.SignalToStop();
                    fileVideo.WaitForStop();
                }
                fileVideo.SignalToStop();
                fileVideo.WaitForStop();
            }
        }

        public Bitmap ProcessFrame(Bitmap frame)
        {
            // 转换为灰度图
            Bitmap grayFrame = new Bitmap(frame.Width, frame.Height);
            using (Graphics g = Graphics.FromImage(grayFrame))
            {
                ColorMatrix colorMatrix = new ColorMatrix(new float[][]
                {
                    new float[] {0.299f, 0.299f, 0.299f, 0, 0},
                    new float[] {0.587f, 0.587f, 0.587f, 0, 0},
                    new float[] {0.114f, 0.114f, 0.114f, 0, 0},
                    new float[] {0, 0, 0, 1, 0},
                    new float[] {0, 0, 0, 0, 1}
                });

                ImageAttributes attributes = new ImageAttributes();
                attributes.SetColorMatrix(colorMatrix);

                g.DrawImage(frame,
                    new Rectangle(0, 0, frame.Width, frame.Height),
                    0, 0, frame.Width, frame.Height,
                    GraphicsUnit.Pixel,
                    attributes);
            }

            // 预处理并生成点阵图
            using (Bitmap processedFrame = PreprocessImage(grayFrame))
            {
                return GenerateDotMatrix(processedFrame);
            }
        }

        public void SaveAsSvg(string imagePath, string outputPath, Size? outputSize = null)
        {
            using (Bitmap dotMatrix = ProcessImage(imagePath, outputSize))
            {
                string svg = GenerateSvgContent(dotMatrix);
                File.WriteAllText(outputPath, svg, Encoding.UTF8);
            }
        }

        private string GenerateSvgContent(Bitmap image)
        {
            StringBuilder svg = new StringBuilder();
            svg.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            svg.AppendLine($"<svg width=\"{image.Width}\" height=\"{image.Height}\" xmlns=\"http://www.w3.org/2000/svg\">");
            svg.AppendLine("<g>");

            // 设置背景
            if (isOpposition)
            {
                // 黑背景
                svg.AppendLine($"<rect width=\"{image.Width}\" height=\"{image.Height}\" fill=\"black\"/>");
            }
            else
            {
                // 白背景
                svg.AppendLine($"<rect width=\"{image.Width}\" height=\"{image.Height}\" fill=\"white\"/>");
            }
            


            int gridH = (int)(image.Height / gridSize);
            int gridW = (int)(image.Width / gridSize);

            for (int i = 0; i < gridH; i++)
            {
                for (int j = 0; j < gridW; j++)
                {
                    float y1 = i * gridSize;
                    float x1 = j * gridSize;

                    float avgIntensity = CalculateAverageIntensity(image, (int)x1, (int)y1, (int)gridSize);
                    float dotRadius = (255 - avgIntensity) * gridSize / (2 * 255);

                    if (dotRadius > 0)
                    {
                        float centerY = y1 + gridSize / 2;
                        float centerX = x1 + gridSize / 2;
                        if (isOpposition) 
                        { svg.AppendLine($"<circle cx=\"{centerX:F2}\" cy=\"{centerY:F2}\" r=\"{dotRadius:F2}\" fill=\"white\"/>"); } 
                        else
                        { svg.AppendLine($"<circle cx=\"{centerX:F2}\" cy=\"{centerY:F2}\" r=\"{dotRadius:F2}\" fill=\"black\"/>"); }
                            
                    }
                }
            }

            svg.AppendLine("</g>");
            svg.AppendLine("</svg>");

            return svg.ToString();
        }
    }
}