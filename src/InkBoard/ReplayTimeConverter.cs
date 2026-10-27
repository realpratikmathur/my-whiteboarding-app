using System;
using Microsoft.UI.Xaml.Data;

namespace InkBoard;

/// <summary>Shows a replay position in milliseconds as m:ss (the replay slider's thumb tooltip).</summary>
public sealed partial class ReplayTimeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is double ms ? TimeSpan.FromMilliseconds(Math.Max(0, ms)).ToString(@"m\:ss") : "";

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
