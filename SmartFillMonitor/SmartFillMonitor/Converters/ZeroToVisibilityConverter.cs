using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;

namespace SmartFillMonitor.Converters
{
    // 实现IValueConverter接口，这是WPF转换器的标配
    public class ZeroToVisibilityConverter : IValueConverter
    {
        // Convert：从「数据源（ViewModel）」转到「UI」（核心方法）
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // 1. 把传进来的值（ActiveAlarmCount）转成int类型
            if (value is int count)
            {
                // 2. 核心逻辑：count=0 → 显示（Visible）；count≠0 → 隐藏（Collapsed）
                return count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            // 3. 非int类型（比如null），默认隐藏
            return Visibility.Collapsed;
        }

        // ConvertBack：从「UI」转回「数据源」（这里用不到，所以抛异常）
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
