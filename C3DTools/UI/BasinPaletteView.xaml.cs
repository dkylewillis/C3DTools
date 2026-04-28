using C3DTools.ViewModels;
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using WpfUserControl = System.Windows.Controls.UserControl;

namespace C3DTools.UI
{
    /// <summary>
    /// Interaction logic for BasinPaletteView.xaml
    /// </summary>
    public partial class BasinPaletteView : WpfUserControl
    {
        public BasinPaletteView()
        {
            InitializeComponent();
            DataContext = new BasinPaletteViewModel();
        }

        public BasinPaletteViewModel ViewModel => (BasinPaletteViewModel)DataContext;
    }

    /// <summary>
    /// Converts null to Visibility.Collapsed, non-null to Visibility.Visible.
    /// </summary>
    public class NullToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value != null ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converts null to Visibility.Visible, non-null to Visibility.Collapsed.
    /// </summary>
    public class InverseNullToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value == null ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converts false to Visibility.Visible, true to Visibility.Collapsed.
    /// </summary>
    public class InverseBoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
                return boolValue ? Visibility.Collapsed : Visibility.Visible;
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Returns true when the bound string equals ConverterParameter.
    /// Used to drive RadioButton.IsChecked from a string property.
    /// </summary>
    public class StringEqualityConverter : IValueConverter
    {
        public static readonly StringEqualityConverter Instance = new StringEqualityConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value?.ToString() == parameter?.ToString();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b && b)
                return parameter?.ToString() ?? string.Empty;
            return System.Windows.Data.Binding.DoNothing;
        }
    }
}
