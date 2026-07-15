using System.IO;
using System.Windows;

namespace GameSaveManager.App.Views;

public partial class ThirdPartyNoticeWindow : Window
{
    public string NoticeText { get; }
    public ThirdPartyNoticeWindow()
    {
        InitializeComponent();
        string path = Path.Combine(AppContext.BaseDirectory, "Assets", "NOTICE-Ludusavi-manifest.txt");
        NoticeText = File.Exists(path) ? File.ReadAllText(path) : "未找到 Ludusavi Manifest 的许可证文件。请重新安装或修复客户端。";
        DataContext = this;
    }
}
