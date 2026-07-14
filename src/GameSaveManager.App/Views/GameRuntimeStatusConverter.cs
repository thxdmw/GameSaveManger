using System;
using System.Globalization;
using System.Windows.Data;
using GameSaveManager.App.ViewModels;
using GameSaveManager.Application.Api;

namespace GameSaveManager.App.Views;

public sealed class GameRuntimeStatusConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        return values.Length >= 2 && values[0] is CloudGame game && values[1] is MainViewModel viewModel
            ? viewModel.GetGameRuntimeStatusText(game)
            : "未配置 EXE";
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}