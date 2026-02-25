using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Media;  // 注意：这是 WPF 的颜色命名空间
using SmartFillMonitor.Models;

namespace SmartFillMonitor.Converters
{
    //枚举定 “状态名称”，转换器定 “状态对应颜色”，依赖属性存 “当前状态”，XAML 用 “转换后的颜色” 渲染 —— 这就是完整的逻辑闭环。
    public class LightStateToBrushConverter : IValueConverter
    {
        public static readonly Color OffColor = Colors.DimGray;
        public static readonly Color GreenColor = Colors.Green;
        public static readonly Color YellowColor = Colors.Yellow;
        public static readonly Color RedColor = Colors.Red;

    //object value,         参数1：绑定的源值（你代码里的AsyncState枚举值）
    //Type targetType,      参数2：目标属性的类型（你代码里是Color类型）
    //object parameter,     参数3：XAML里传的自定义参数（你代码里的Green/Yellow/Red）
    //CultureInfo culture   参数4：区域文化信息（一般用不到）
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var state=value is LightState ls? ls: LightState.Off;
            var role = (parameter as string)?.ToLowerInvariant() ?? string.Empty;
            if (state == LightState.Off) return OffColor;
            return role switch
            {
                "green" => state == LightState.Green ? GreenColor : OffColor,
                "yellow" => state == LightState.Yellow ? YellowColor : OffColor,
                "red" => state == LightState.Red ? RedColor : OffColor,
                _ => OffColor
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
