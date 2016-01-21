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
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace Andl.Workshop {
  public interface IDatabaseProvider {
    DatabaseSelector Database { get; }
  }

  /// <summary>
  /// Interaction logic for CatalogTreeControl.xaml
  /// 
  /// Note: relies on data context provided by parent
  /// </summary>
  public partial class CatalogTreeControl : UserControl {

    public CatalogTreeControl() {
      InitializeComponent();
    }

    private void treeViewControl_MouseRightButtonDown(object sender, MouseButtonEventArgs e) {
    }

    private void treeViewControl_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e) {
    }

    private void comboControl_TargetUpdated(object sender, DataTransferEventArgs e) {
    }
  }
}
