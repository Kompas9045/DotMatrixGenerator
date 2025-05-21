using System.Windows;
using System.ComponentModel;

namespace lattice
{
    public partial class ProgressWindow : Window
    {
        public bool AllowClosing { get; set; } = false; // 默认不允许关闭

        public ProgressWindow()
        {
            InitializeComponent();
            this.Closing += Window_Closing;
        }

        //这是一个进度条窗口，不应该被用户关闭，所以这里阻止用户关闭窗口
        private void Window_Closing(object sender, CancelEventArgs e)
        {
            if (!AllowClosing) // 如果不是程序主动关闭，则阻止用户关闭
            {
                e.Cancel = true;
            }
        }
    }
}