using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows.Data;

namespace Andl.Workbench {
  public class TitleConverter : IMultiValueConverter {
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) {
      var filename = values[0] as string ?? "";
      var modified = values[1] as bool? ?? false;
      var title = "Andl Workbench";
      if (filename != "") title += " - " + filename + (modified ? " *" : "");
      return title;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) {
      return null;
    }
  }
}
