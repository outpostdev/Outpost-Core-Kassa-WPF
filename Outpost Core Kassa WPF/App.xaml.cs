using System;
using System.Windows;
using System.Windows.Data;

namespace Outpost_Core_Kassa_WPF {
	public partial class App : Application {
	}

	public class FalseToCollapsedConverter : IValueConverter {
		public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) {
			if(value is bool) {
				if((bool)value) return Visibility.Visible;
				else return Visibility.Collapsed;
			} else return null;
		}

		public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) {
			if(value is Visibility) {
				if((Visibility)value == Visibility.Visible) return 1;
				else return 0;
			} else return null;
		}
	}

	public class IsIntZeroConverter : IValueConverter {
		public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) {	
			if(value is int) {
				if((int)value == 0) return true;
				else return false;
			} else return null;
		}

		public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) {
			if(value is bool) {
				if((bool)value) return 1;
				else return 0;
			} else return null;
		}
	}

	public class IsIntNotZeroConverter : IValueConverter {
		public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) {
			if(value is int) {
				if((int)value == 0) return false;
				else return true;
			} else return null;
		}

		public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) {
			if(value is bool) {
				if((bool)value) return 0;
				else return 1;
			} else return null;
		}
	}
}
