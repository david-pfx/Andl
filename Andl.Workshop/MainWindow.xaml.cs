using Microsoft.Win32;
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
  /// <summary>
  /// Interaction logic for MainWindow.xaml
  /// </summary>
  public partial class MainWindow : Window {
    public TreeDataViewModel DataModel { get; set; }
    string CurrentFileName { get; set; }

    readonly DatabaseConnector _dbconnector;

    public MainWindow() {
      InitializeComponent();

      _dbconnector = DatabaseConnector.Create();
      _dbconnector.OpenCatalog("test");

      // data context is this; access data model via simply property
      DataModel = new TreeDataViewModel(_dbconnector);
      DataContext = this;
      
      textEditor.Focus();
      textEditor.IsModified = false;
    }


    ///============================================================================================
    /// Dependency properties
    /// 

    // Text bound by output window
    public string OutputText {
      get { return (string)GetValue(OutputTextProperty); }
      set { SetValue(OutputTextProperty, value); }
    }
    public static readonly DependencyProperty OutputTextProperty =
        DependencyProperty.Register("OutputText", typeof(string), typeof(MainWindow), new PropertyMetadata("test"));

    ///============================================================================================
    ///
    /// Implementation
    /// 
    bool SaveCurrentFile() {
      if (CurrentFileName == null) {
        SaveFileDialog dlg = new SaveFileDialog();
        dlg.DefaultExt = ".txt";
        if (dlg.ShowDialog() ?? false)
          CurrentFileName = dlg.FileName;
        else return false;
      }
      textEditor.Save(CurrentFileName);
      return true;
    }

    private bool CloseCurrentFile() {
      if (textEditor.IsModified) {
        MessageBoxResult result;
        do {
          var msg = String.Format("Save file '{0}'?", CurrentFileName ?? "<unnamed>");
          result = MessageBox.Show(msg, "Save?", MessageBoxButton.YesNoCancel);
          if (result == MessageBoxResult.Cancel) return false;
        } while (result == MessageBoxResult.Yes && !SaveCurrentFile());
      }
      return true;
    }

    private void ExecuteQuery() {
      var text = textEditor.SelectedText;
      if (text.Length == 0) text = textEditor.Text;
      OutputText = DataModel.Catalog.Execute(text);
    }

    ///============================================================================================
    ///
    /// Handlers
    /// 

    private void Open_Executed(object sender, ExecutedRoutedEventArgs e) {
      OpenFileDialog dlg = new OpenFileDialog();
      dlg.CheckFileExists = true;
      if (dlg.ShowDialog() ?? false) {
        CurrentFileName = dlg.FileName;
        textEditor.Load(CurrentFileName);
      }
    }

    private void New_Executed(object sender, ExecutedRoutedEventArgs e) {
      if (CloseCurrentFile()) {
        textEditor.Clear();
        CurrentFileName = null;
      }
    }

    private void Save_Executed(object sender, ExecutedRoutedEventArgs e) {
      SaveCurrentFile();
    }

    private void Exit_Executed(object sender, ExecutedRoutedEventArgs e) {
      Close();
    }

    private void Refresh_Executed(object sender, ExecutedRoutedEventArgs e) {

    }

    private void Properties_Executed(object sender, ExecutedRoutedEventArgs e) {

    }

    private void About_Executed(object sender, ExecutedRoutedEventArgs e) {
      var about = new About();
      about.ShowDialog();
    }

    private void Testing_Executed(object sender, ExecutedRoutedEventArgs e) {

    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e) {
      if (!CloseCurrentFile()) e.Cancel = true;
    }

    private void Execute_Executed(object sender, ExecutedRoutedEventArgs e) {
      ExecuteQuery();
    }

    private void Web_Executed(object sender, ExecutedRoutedEventArgs e) {

    }
  }
}
