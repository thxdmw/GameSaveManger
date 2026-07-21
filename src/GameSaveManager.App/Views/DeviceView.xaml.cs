using System.Windows;
using System.Windows.Controls;
using GameSaveManager.App.ViewModels;

namespace GameSaveManager.App.Views;

public partial class DeviceView : UserControl
{
    public DeviceView() => InitializeComponent();

    private void RevokeDeviceButton_OnClick(object sender, RoutedEventArgs e)
    {
        Window? owner = Window.GetWindow(this);
        if (DataContext is not MainViewModel viewModel || viewModel.SelectedDevice is not { } device)
        {
            ThemedDialogWindow.ShowThemed(owner, "GameSave Manager", "请先选择要撤销的设备。", "知道了");
            return;
        }
        ThemedDialogResult confirmation = ThemedDialogWindow.ShowThemed(
            owner,
            "确认撤销设备",
            $"确定撤销设备“{device.DeviceName}”吗？该设备的登录 Token 将立即失效。",
            "确认撤销",
            "取消");
        if (confirmation == ThemedDialogResult.Primary && viewModel.RevokeDeviceCommand.CanExecute(device))
            viewModel.RevokeDeviceCommand.Execute(device);
    }
}
