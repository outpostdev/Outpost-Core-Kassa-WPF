using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Outpost_Core_Kassa_WPF {
	public partial class ChooseProductListingWindow : Window {
		public int ReturnIndex = -1;
		
		public ChooseProductListingWindow() {
			InitializeComponent();
		}

		void MouseDoubleClickCallback(object sender, EventArgs e) {
			if(ProductListingDataGrid.SelectedIndex != -1) {
				ReturnIndex = ProductListingDataGrid.SelectedIndex;
				Close();
			}
		}
	}
}
