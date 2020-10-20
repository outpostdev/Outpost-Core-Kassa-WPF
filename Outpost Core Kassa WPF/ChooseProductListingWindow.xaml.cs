using System;
using System.Windows;

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
