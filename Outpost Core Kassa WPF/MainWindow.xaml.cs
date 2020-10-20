using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Data.SqlClient;
using System.Security;
using System.Diagnostics;

using Microsoft.PointOfService;
using FDM.Client;

namespace Outpost_Core_Kassa_WPF {
	public partial class MainWindow : Window {	
		SqlConnection Connection;
		Scanner Scanner;
		PosPrinter PosPrinter;
		FDMClient FDMClient;
		public List<ReceiptLine> ReceiptLines { get; set; }

		public MainWindow() {
			InitializeComponent();

			DataContext = this;

			ReceiptLines = new List<ReceiptLine>();

			OpenSqlConnection();
			InitializeScannerAndPrinter();

			if(!InitializeFDMClient()) {
				MessageBox.Show("Failed to initialize FDM client.");
			}

			// TODO: DEBUG
			//DebugWriteAllProducts();
		}

		public bool InitializeFDMClient() {
			KeyFile k;
			string path = "C:\\Users\\dewints\\Downloads\\fdm.key";

			// TODO: Use our own passphase.
			if(!KeyFile.TryLoadFromFile(path, "DEMO", out k)) {
				Debug.WriteLine("Unable to load the key file \"" + path + "\".");
				return false;
			}

			FDMClient = FDMClient.CreateClient(k, Languages.English);
			if(FDMClient == null) {
				Debug.WriteLine("Unable to create the FDM client.");
				return false;
			}

			return true;
		}

		public void SendHashAndSignTransaction() {
			// Create the event to sign.

			string pos_serial_nr = "B999000ABC4567"; // BXXXCCCPPPPPPP:
													 // BXXX: Producer ID.
													 // CCC: Certificate number.
													 // PPPPPPP: Last 7 characters of software license key,
													 //          ignoring possible control character.
			string terminal_id   = "10";
			string terminal_name = "FDM DEMO POS";
			string operator_id   = "79100590097"; // INSZ-number or BIS-number (11 characters) or "Guest" (00000000097)
			string operator_name = "Tony";
			int transaction_nr   = 1; // ???

			PosEvent posevent = new PosEvent(pos_serial_nr, terminal_id, terminal_name, operator_id, operator_name,
											 transaction_nr, DateTime.Now);

			Debug.Assert(TrainingButton.IsChecked != null);
			Debug.Assert(RefundButton.IsChecked != null);

			posevent.IsTrainingMode = (bool)TrainingButton.IsChecked;
			posevent.IsRefund = (bool)RefundButton.IsChecked;
			posevent.IsFinalized = true;
			posevent.DrawerOpen = posevent.IsFinalized && (posevent.Payments.Count != 0);

			posevent.SetVatRate(21, 12, 6, 0);

			decimal payment_total = 0.0m; // DEBUG
			foreach(ReceiptLine l in ReceiptLines) {
				// TODO: Determine whether to use 'ID' or 'SKU' for argument 'ProductId'.
				posevent.Products.Add(new ProductLine(l.SKU, l.Description, l.Amount, l.TotalPrice, l.VAT.ToString()));
				payment_total += l.TotalPrice;
			}

			posevent.Payments.Add(new PaymentLine("PAY001", "Euro", PaymentTypes.Cash, 1, payment_total));

			FDMClient.BeginHashAndSign(posevent, HashAndSignCallback, null); // Argument 3 feeds the callback.
		}

		void PrintReceipt(PosEventSignResult pesr) {
			string tab = "    "; // Necessary since the receipt printer doesn't seem to support normal tab characters.
			string total_label = "Total: ";

			StringBuilder sb = new StringBuilder();
			try {
				// Initialization.
				PosPrinter.RecLetterQuality = true;		
				PosPrinter.SetBitmap(1, PrinterStation.Receipt, "..\\..\\Resources\\outpost_logo_black.bmp",
				                     PosPrinter.RecLineWidth, PosPrinter.PrinterBitmapCenter);

				// Header logo.
				sb.Append(PrintCommands.bitmap_1);

				// Establishment data.
				Dispatcher.Invoke(() => {
					sb.Append(
						PrintCommands.center +
						CompanyNameLabel.Content   + "\n" +
						CompanyStreetLabel.Content + "\n" +
						CompanyCityLabel.Content   + "\n" +
						CompanyVATLabel.Content    + "\n\n"
					);
				});
				
				// Receipt title.
				sb.Append(PrintCommands.double_size);
				if(pesr.Event.IsTrainingMode) sb.Append("TRAINING RECEIPT\n");
				else sb.Append("VAT RECEIPT\n");
				if(pesr.Event.IsRefund) sb.Append("REFUND\n");
				sb.Append(PrintCommands.reset);

				decimal receipt_total = 0.0m;
				int space_count;

				// Product lines & total.
				sb.Append(PrintCommands.bold + "Products & Services:\n" + PrintCommands.reset);
				foreach(ProductLine l in pesr.Event.Products) {
					space_count =
						PosPrinter.RecLineChars -
						l.Quantity.ToString().Length -
						l.ProductName.ToString().Length -
						l.SellingPrice.ToString().Length -
						l.PrintVatRateId.ToString().Length -
						tab.Length - 3;

					// TODO: Handle (space_count <= 0) case.
					Debug.Assert(0 < space_count);

					sb.Append(
						string.Format(tab + "{0}x {1}", l.Quantity, l.ProductName) +
						string.Concat(Enumerable.Repeat(" ", space_count)) +
						string.Format("{0} {1}\n", l.SellingPrice, l.PrintVatRateId)
					);

					receipt_total += l.SellingPrice;
				}
				sb.Append(tab + string.Concat(Enumerable.Repeat("-", PosPrinter.RecLineChars - tab.Length)) + "\n");
				space_count = PosPrinter.RecLineChars - total_label.Length -
					          receipt_total.ToString().Length - tab.Length - 2;
				sb.Append(tab + total_label + string.Concat(Enumerable.Repeat(" ", space_count)) +
					      receipt_total.ToString() + "\n\n");

				// Payment lines.
				// TODO: Add support for change & refund.
				sb.Append(PrintCommands.bold + "Payment:\n" + PrintCommands.reset);
				foreach(PaymentLine l in pesr.Event.Payments) {
					space_count = PosPrinter.RecLineChars - l.PaymentName.ToString().Length -
						          l.PayAmount.ToString().Length - tab.Length - 2;

					sb.Append(tab + l.PaymentName + string.Concat(Enumerable.Repeat(" ", space_count)) + l.PayAmount + "\n");
				}
				sb.Append("\n");

				// VAT data.
				int len, taxable_max_char_count = 0, rate_max_char_count = 0, amount_max_char_count = 0;
				sb.Append(PrintCommands.bold + "VAT:\n" + PrintCommands.reset); // TODO: Multilingual support.
				foreach(VATSplit v in pesr.VATSplit) {
					// Calculate spacing for alignment.
					len = v.TaxableAmount.ToString().Length;
					if(taxable_max_char_count < len) taxable_max_char_count = len;
					len = v.VATRate.ToString().Length;
					if(rate_max_char_count < len) rate_max_char_count = len;
					len = v.VATAmount.ToString().Length;
					if(amount_max_char_count < len) amount_max_char_count = len;
				}
				foreach(VATSplit v in pesr.VATSplit) {
					sb.Append(
						tab + v.PrintVatRateId + ": " +
						string.Concat(Enumerable.Repeat(" ", taxable_max_char_count - v.TaxableAmount.ToString().Length)) +
						v.TaxableAmount + " @ " +
						string.Concat(Enumerable.Repeat(" ", rate_max_char_count - v.VATRate.ToString().Length)) +
						v.VATRate + "% = " +
						string.Concat(Enumerable.Repeat(" ", amount_max_char_count - v.VATAmount.ToString().Length)) +
						+ v.VATAmount + "\n"
					);
				}
				sb.Append(tab + total_label +
					string.Concat(
						Enumerable.Repeat(" ",
							taxable_max_char_count + rate_max_char_count + amount_max_char_count + 10 -
							total_label.Length - pesr.GetTotalVATAmount().ToString().Length
						)
					) + pesr.GetTotalVATAmount() + "\n\n"
				);

				// Other mandatory data.
				sb.Append(
					PrintCommands.bold + "Cash Register Data:\n" + PrintCommands.reset +
					tab + "PLU hash:    " + pesr.Signature.PrintPluHash + "\n" +
					tab + "POS:         " + pesr.Event.POSSerialNumber + "\n" +
					tab + "Version:     " + "~TODO~" + "\n" + // TODO
					tab + "Terminal:    " + pesr.Event.TerminalId + "\n" +
					tab + "Transaction: " + pesr.Event.TransactionNumber.ToString() + "\n" +
					tab + "Date & time: " + pesr.Event.TransactionDateTime.ToString("dd/MM/yyyy HH:mm:ss") + "\n" +
					tab + "User:        " + pesr.Event.OperatorName + "\n" +
					"\n"
				);

				// FDM control data.
				// Must follow specific naming conventions as dictated by the law!
				// See circular no. E.T. 124.747, chapter 5, points 44 & 45!
				sb.Append(
					PrintCommands.bold + "Control Data:\n" + PrintCommands.reset +
					tab + pesr.Signature.SignDateTime + "\n" +
					tab + string.Format("Receipt counter: {0}/{1} {2}\n",
						pesr.Signature.TicketNumber,
						pesr.Signature.TicketCount,
						pesr.Signature.EventType
					) +
					tab + "Receipt signature:\n" + tab + tab + pesr.Signature.Signature + "\n" +
					tab + "Control module ID: " + pesr.Signature.FDMSerialNumber + "\n" +
					tab + "VAT signing card ID: " + pesr.Signature.VSCIdentificationNumber + "\n\n"
				);

				// Invalidity footer.
				if(pesr.Event.IsTrainingMode)
					sb.Append(PrintCommands.center + PrintCommands.double_size +
						      "THIS IS NOT A\nVALID VAT RECEIPT\n" + PrintCommands.reset
					);

				sb.Append(PrintCommands.feed_cut);

				PosPrinter.PrintNormal(PrinterStation.Receipt, sb.ToString());
			} catch(PosControlException e) {
				Debug.WriteLine("Point of sale control exception: {0}", e);
				if(e.ErrorCode == ErrorCode.Extended) {
					Debug.WriteLine("Extended: ", e.ErrorCodeExtended);
				}
			}
		}

		private void HashAndSignCallback(IAsyncResult ar) {
			PosEventSignResult result = FDMClient.EndHashAndSign(ar);

			if(result != null) {
				if(result.HasErrors) {
					foreach(Error e in result.Errors) {
						MessageBox.Show(e.ToString());
					}
				} else {
					/*
					string result_message = "Hash and sign result:\n";
					result_message = string.Concat(
						result_message, string.Format(
							"\tIsCLocking: {0}\n" +
							"\tClockingType: {1}\n" +
							"\tVATReceiptPrintingMode: {2}\n" + 
							"\tVATSplits:\n",
							result.IsClocking,
							result.ClockingType,
							result.VATReceiptPrintingMode
						)
					);

					if(result.VATSplit != null) {
						foreach(VATSplit s in result.VATSplit) {
							result_message = string.Concat(
								result_message, string.Format(
									"\t\tPrintVatRateId: {0}\n" +
									"\t\tTaxableAmount: {1}\n" +
									"\t\tVATAmount: {2}\n" +
									"\t\tVATRate: {3}\n" +
									"\t\tVatRateId: {4}\n" +
									"\n",
									s.PrintVatRateId,
									s.TaxableAmount,
									s.VATAmount,
									s.VATRate,
									s.VatRateId
								)
							);
						}
					}

					MessageBox.Show(result_message);

					string event_message = "Event:\n";
					event_message = string.Concat(
						event_message, string.Format(
							"\tDrawerId: {0}\n" +
							"\tDrawerName: {1}\n" +
							"\tDrawerOpen: {2}\n" +
							"\tInvoiceNumber: {3}\n" +
							"\tIsFinalized: {4}\n" +
							"\tIsRefund: {5}\n" +
							"\tIsSimplifiedReceipt: {6}\n" +
							"\tIsTrainingMode: {7}\n" +
							"\tOperatorId: {8}\n" +
							"\tOperatorName: {9}\n" +
							"\tPayments:\n",
							result.Event.DrawerId,
							result.Event.DrawerName,
							result.Event.DrawerOpen,
							result.Event.InvoiceNumber,
							result.Event.IsFinalized,
							result.Event.IsRefund,
							result.Event.IsSimplifiedReceipt,
							result.Event.IsTrainingMode,
							result.Event.OperatorId,
							result.Event.OperatorName
						)
					);

					foreach(PaymentLine p in result.Event.Payments) {
						event_message = string.Concat(
							event_message, string.Format(
								"\t\tForeignCurrencyAmount: {0}\n" +
								"\t\tForeignCurrencyISO: {1}\n" +
								"\t\tPayAmount: {2}\n" +
								"\t\tPaymentId: {3}\n" +
								"\t\tPaymentName: {4}\n" +
								"\t\tPaymentType: {5}\n" +
								"\t\tQuantity: {6}\n" +
								"\t\tReference: {7}\n" +
								"\n",
								p.ForeignCurrencyAmount,
								p.ForeignCurrencyISO,
								p.PayAmount,
								p.PaymentId,
								p.PaymentName,
								p.PaymentType,
								p.Quantity,
								p.Reference
							)
						);
					}

					event_message = string.Concat(
						event_message, string.Format(
							"\tPOSSerialNumber: {0}\n" +
							"\tProducts:\n",
							result.Event.POSSerialNumber
						)
					);

					foreach(ProductLine p in result.Event.Products) {
						event_message = string.Concat(
							event_message, string.Format(
								"\t\tDiscounts: {0}\n" +
								"\t\tPrintVatRateId: {1}\n" +
								"\t\tProductGroupId: {2}\n" +
								"\t\tProductGroupName: {3}\n" +
								"\t\tProductId: {4}\n" +
								"\t\tProductName: {5}\n" +
								"\t\tQuantity: {6}\n" +
								"\t\tQuantityUnit: {7}\n" +
								"\t\tSellingPrice: {8}\n" +
								"\t\tVatRateId: {9}\n" +
								"\n",
								p.Discounts,
								p.PrintVatRateId,
								p.ProductGroupId,
								p.ProductGroupName,
								p.ProductId,
								p.ProductName,
								p.Quantity,
								p.QuantityUnit,
								p.SellingPrice,
								p.VatRateId
							)
						);
					}
					
					event_message = string.Concat(
						event_message, string.Format(
							"\tReference: {0}\n" +
							"\tTerminalId: {1}\n" +
							"\tTerminalName: {2}\n" +
							"\tTransactionDateTime: {3}\n" +
							"\tTransactionNumber: {4}\n" +
							"\tVatRateA: {5}\n" +
							"\tVatRateB: {6}\n" +
							"\tVatRateC: {7}\n" +
							"\tVatRateD: {8}\n",
							result.Event.Reference,
							result.Event.TerminalId,
							result.Event.TerminalName,
							result.Event.TransactionDateTime,
							result.Event.TransactionNumber,
							result.Event.VatRateA,
							result.Event.VatRateB,
							result.Event.VatRateC,
							result.Event.VatRateD
						)
					);
					MessageBox.Show(event_message);

					if(result.HasSignature) {
						string signature_message = "Signature:\n";
						signature_message = string.Concat(
							signature_message, string.Format(
								"\tEventType: {0}\n" +
								"\tFDMSerialNumber: {1}\n" +
								"\tFullPluHash: {2}\n" +
								"\tPrintEventType: {3}\n" +
								"\tPrintPluHash: {4}\n" +
								"\tSignature: {5}\n" +
								"\tSignDateTime: {6}\n" +
								"\tTicketCount: {7}\n" +
								"\tTicketNumber: {8}\n" +
								"\tVSCIdentificationNumber: {9}\n",
								result.Signature.EventType,
								result.Signature.FDMSerialNumber,
								result.Signature.FullPluHash,
								result.Signature.PrintEventType,
								result.Signature.PrintPluHash,
								result.Signature.Signature,
								result.Signature.SignDateTime,
								result.Signature.TicketCount,
								result.Signature.TicketNumber,
								result.Signature.VSCIdentificationNumber
							)
						);
						MessageBox.Show(signature_message);
					}
					*/
					PrintReceipt(result);
				}

				// Start the next transaction.
				// We allow clocking in the middle of another transaction in this example, for a real-world POS
				// you need to beware of transaction numbers. Don't create gaps or duplicate numbers if you do this!
				//if(result.IsClocking == false) InitializeTransaction();
			}
		}

		public void SendNoOperation() {
			NoOperationEvent nop = new NoOperationEvent("B999000ABC4567", "10", "FDM DEMO POS", DateTime.Now);
			MessageBox.Show("Sending NOP event to FDM...");
			FDMClient.BeginNOP(nop, NoOperationCallback, null); // Argument 3 feeds the callback.
		}

		private void NoOperationCallback(IAsyncResult ar) {
			NoOperationResult result = FDMClient.EndNOP(ar);

			if(result != null) {
				if(result.HasErrors) {
					MessageBox.Show("FDM result contains errors!");
				}
				if(result.HasFDM) {
					MessageBox.Show(
						string.Format(
							"FDM result:\n\tFDM serial number: {0}\n\tVSC ID: {1}\n\tMemory +90%?: {2}",
							result.FDMSerialNumber,
							result.VSCIdentificationNumber,
							result.FDMMemory90
						)
					);
				}
			}
		}

		void ClearReceipt() {
			ReceiptLines.Clear();
			ReceiptDataGrid.Items.Refresh();

			// Shrink the 'Description' column width to its minimal size,
			// providing room for the other columns to grow if needed.
			ReceiptDataGrid.Columns[2].Width = DataGridLength.Auto;
			ReceiptDataGrid.UpdateLayout();

			// Expand the 'Description' column width again to fill any empty space that remains.
			ReceiptDataGrid.Columns[2].Width = new DataGridLength(1.0, DataGridLengthUnitType.Star);
			ReceiptDataGrid.UpdateLayout();
		}

		void FinalizeTransactionClickCallback(object sender, EventArgs e) {
			SendHashAndSignTransaction();
			ClearReceipt();
		}

		void WindowMouseDownCallback(object sender, EventArgs e) {
			ReceiptDataGrid.UnselectAll();
		}

		void ClearReceiptClickCallback(object sender, EventArgs e) {
			ClearReceipt();
			TrainingButton.IsChecked = false;
			RefundButton.IsChecked = false;
		}

		private void OpenSqlConnection() {
			string pass = "rNevMhkDY8Z8c54u";
			SecureString ss = new SecureString();

			// TODO: Fixme.
			foreach(char c in pass) ss.AppendChar(c);

			Connection = new SqlConnection("Data Source=BURO_EXTRA2\\SQLEXPRESS;Initial Catalog=outpost_core_kassa;" +
			                               "User id=sa;Password=rNevMhkDY8Z8c54u;"/*, new SqlCredential("sa", ss)*/);
			Connection.Open();
		}

		private void DebugWriteAllProducts() {
			Debug.Assert(Connection != null);
			
			Debug.WriteLine("SQL connection state: {0}", Connection.State);

			SqlCommand com = new SqlCommand("select * from products;", Connection);

			using(SqlDataReader r = com.ExecuteReader()) {
				while(r.Read()) Debug.WriteLine(string.Format("{0}, {1}, {2}, {3}", r[0], r[1], r[2], r[3]));
			}
		}

		private void InitializeScannerAndPrinter() {
			PosExplorer exp = new PosExplorer();
			DeviceInfo scanner_info = null, printer_info = null;
			string scanner_so_name = "SYMBOL_SCANNER";
			string printer_so_name = "Star TSP100 Cutter (TSP143)_1";

			DeviceCollection devices = exp.GetDevices(DeviceCompatibilities.OposAndCompatibilityLevel1);
			foreach(DeviceInfo info in devices) {
				Debug.WriteLine(info.Type + " \"" + info.ServiceObjectName + "\"");


				if(info.ServiceObjectName == scanner_so_name) {
					scanner_info = info;
				} else if(info.ServiceObjectName == printer_so_name) {
					printer_info = info;
				}
			}

			if(scanner_info == null) {
				Debug.WriteLine("Scanner \"" + scanner_so_name + "\" NOT found.");
			} else {
				Debug.WriteLine("Scanner \"" + scanner_so_name + "\" found.");
				Scanner = (Scanner)exp.CreateInstance(scanner_info);
				StartScanner();
			}

			if(printer_info == null) {
				Debug.WriteLine("Printer \"" + printer_so_name + "\" NOT found.");
			} else {
				Debug.WriteLine("Printer \"" + printer_so_name + "\" found.");
				PosPrinter = (PosPrinter)exp.CreateInstance(printer_info);
				OpenClaimEnablePosPrinter();
			}
		}

		private void StartScanner() {
			try {
				Scanner.Open();
				Scanner.Claim(0);
				Scanner.DeviceEnabled    = true;
				Scanner.DecodeData       = true;
				Scanner.DataEvent       += ScanCallback;
				Scanner.DataEventEnabled = true;
			} catch(PosControlException e) {
				Debug.WriteLine("Point of sale control exception: {0}", e);
			}
		}

		private void ScanCallback(object sender, DataEventArgs e) {
			Scanner s = (Scanner) sender;
			BarCodeSymbology t = s.ScanDataType;
			string l = Encoding.Default.GetString(s.ScanDataLabel);

			Debug.WriteLine("Scan: {0}: " + l, t);

			SqlCommand com = new SqlCommand(string.Format("select * from products inner join sale_listings on products.id =" +
														  "sale_listings.product_id where (barcode = '{0}');", l), Connection);

			SqlDataReader r = com.ExecuteReader();
			List<ProductListingLookUp> plus = new List<ProductListingLookUp>();
			while(r.Read()) {
				plus.Add(new ProductListingLookUp() {
					Id          = (int)r[0],
					SKU         = (string)r[1],
					Barcode     = (string)r[2],
					Description = (string)r[3],
					UnitPrice   = (decimal)r[6],
					VAT         = DecodeVAT((byte)r[7])});
			}
			r.Close();

			Debug.WriteLine("plus.Count: {0}", plus.Count);

			if(plus.Count == 0) {
				MessageBox.Show(string.Format("Barcode \"{0}\" does not have an associated product listing in the database.", l));
			} else if(plus.Count == 1) {

					Debug.WriteLine("Adding new ReceiptLine.");

					AddReceiptLine(
						new ReceiptLine(){
							SKU         = plus[0].SKU,
							Barcode     = plus[0].Barcode,
							Description = plus[0].Description,
							UnitPrice   = plus[0].UnitPrice,
							VAT         = plus[0].VAT
						}
					);		
			} else {
				ChooseProductListingWindow w = new ChooseProductListingWindow();
				w.ProductListingDataGrid.ItemsSource = plus;
				w.ShowDialog();

				Debug.WriteLine("ReturnIndex: {0}", w.ReturnIndex);

				if(w.ReturnIndex != -1) {
					AddReceiptLine(
						new ReceiptLine() {
							SKU         = plus[w.ReturnIndex].SKU,
							Barcode     = plus[w.ReturnIndex].Barcode,
							Description = plus[w.ReturnIndex].Description,
							UnitPrice   = plus[w.ReturnIndex].UnitPrice,
							VAT         = plus[w.ReturnIndex].VAT
						}
					);
				}
			}

			s.DataEventEnabled = true;
		}

		private void AddReceiptLine(ReceiptLine l) {
			bool AddedYet = false;

			Debug.Assert(RefundButton.IsChecked != null);

			// If the 'ReceiptLine' already exists on the receipt, increment its 'amount' value.
			foreach(ReceiptLine lcur in ReceiptLines) {
				if(	lcur.SKU         == l.SKU &&
					lcur.Barcode     == l.Barcode &&
					lcur.Description == l.Description &&
					lcur.UnitPrice   == l.UnitPrice &&
					lcur.VAT         == l.VAT) {
					if((bool)RefundButton.IsChecked) lcur.Amount--;
					else lcur.Amount++;
					lcur.TotalPrice = lcur.UnitPrice * lcur.Amount;
					AddedYet = true;
				}
			}

			// Otherwise, add a new line to the receipt.
			if(!AddedYet) {
				if((bool)RefundButton.IsChecked) l.Amount = -1;
				else l.Amount = 1;
				l.TotalPrice = l.UnitPrice * l.Amount;
				ReceiptLines.Add(l);
			}
			ReceiptDataGrid.Items.Refresh();

			// Shrink the 'Description' column width to its minimal size,
			// providing room for the other columns to grow if needed.
			ReceiptDataGrid.Columns[2].Width = DataGridLength.Auto;
			ReceiptDataGrid.UpdateLayout();

			// Expand the 'Description' column width again to fill any empty space that remains.
			ReceiptDataGrid.Columns[2].Width = new DataGridLength(1.0, DataGridLengthUnitType.Star);
			ReceiptDataGrid.UpdateLayout();
		}

		private char DecodeVAT(byte b) {
			char[] chars = {'A', 'B', 'C', 'D'};
			Debug.Assert(0 <= b && b < 4);
			return chars[b];
		}

		private void OpenClaimEnablePosPrinter() {
			try {
				PosPrinter.Open();
				PosPrinter.Claim(0);
				PosPrinter.DeviceEnabled = true;
			} catch(PosControlException e) {
				Debug.WriteLine("OpenClaimEnablePosPrinter(): {0}", e);
				if(e.ErrorCode == ErrorCode.Extended) {
					Debug.WriteLine("Extended: ", e.ErrorCodeExtended);
				}
			}
		}
	}
}
