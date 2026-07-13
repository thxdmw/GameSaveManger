using System.Windows;
using System.Windows.Controls;
using GameSaveManager.App.ViewModels;

namespace GameSaveManager.App.Views;

public partial class DeviceView : UserControl
{
    public DeviceView() => InitializeComponent();

    private void RevokeDeviceButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel || viewModel.SelectedDevice is null)
        {
            MessageBox.Show("请先选择要撤销的设备。", "GameSave Manager", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (MessageBox.Show($"确定撤销设备“{viewModel.SelectedDevice.DeviceName}”吗？该设备的登录 Token 将立即失效。", "确认撤销设备", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes && viewModel.RevokeDeviceCommand.CanExecute(null)) viewModel.RevokeDeviceCommand.Execute(null);
    }
}