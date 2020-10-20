namespace Outpost_Core_Kassa_WPF {
	public class ReceiptLine {
		public string  SKU         { get; set; }
		public string  Barcode     { get; set; }
		public string  Description { get; set; }
		public int     Amount      { get; set; }
		public decimal UnitPrice   { get; set; }
		public decimal TotalPrice  { get; set; }
		public char    VAT         { get; set; }
	}
}
