using System.Globalization;
using System.Windows.Data;
using GameSaveManager.App.ViewModels;
using GameSaveManager.Application.Api;

namespace GameSaveManager.App.Views;

public sealed class GameLaunchDisabledReasonConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values is [CloudGame game, MainViewModel viewModel, ..]
            ? viewModel.GetLaunchDisabledReason(game) ?? "启动游戏"
            : "启动配置待验证";

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
