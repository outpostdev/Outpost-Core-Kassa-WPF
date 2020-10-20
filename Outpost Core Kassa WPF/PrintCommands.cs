using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Outpost_Core_Kassa_WPF {
	class PrintCommands {
		public static string reset       = "\x1b|N";
		public static string feed_cut    = "\x1b|fP";
		public static string bold        = "\x1b|bC";
		public static string center      = "\x1b|cA";
		public static string right       = "\x1b|rA";
		public static string normal_size = "\x1b|1C";
		public static string double_size = "\x1b|4C";
		public static string top_logo    = "\x1b|tL";
		public static string bottom_logo = "\x1b|bL";
		public static string bitmap_1    = "\x1b|1B";
		public static string bitmap_2    = "\x1b|2B";
	}
}
