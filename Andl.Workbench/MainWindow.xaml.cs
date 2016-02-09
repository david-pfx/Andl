using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
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

namespace Andl.Workbench {
  /// <summary>
  /// Interaction logic for MainWindow.xaml
  /// </summary>
  public partial class MainWindow : Window {
    const string AppNameVersion = "Andl Workbench 1.0b2";
    const string DefaultScriptName = "workbench.andl";

    public TreeDataViewModel DataModel { get; set; }

    readonly DatabaseSelector _dbconnector;

    public MainWindow() {
      _dbconnector = DatabaseSelector.Create(".");
      // data context is this; access data model via simply property
      DataModel = new TreeDataViewModel(_dbconnector);
      DataContext = this;

      InitializeComponent();


      // set initial visual state
      if (DataModel.Databases.Length > 0) {
        var name = DataModel.Databases.Select(d => d.OpenName)
          .FirstOrDefault(s => s.StartsWith("workbench", StringComparison.InvariantCultureIgnoreCase));
        DataModel.DatabaseName = name ?? DataModel.Databases[0].OpenName;
      }

      textEditor.Focus();
      if (File.Exists(DefaultScriptName)) {
        CurrentFileName = DefaultScriptName;
        textEditor.Load(DefaultScriptName);
      }
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

    public string OutputTextColour {
      get { return (string)GetValue(OutputTextColourProperty); }
      set { SetValue(OutputTextColourProperty, value); }
    }
    public static readonly DependencyProperty OutputTextColourProperty =
        DependencyProperty.Register("OutputTextColour", typeof(string), typeof(MainWindow), new PropertyMetadata("black"));

    public string CurrentFileName {
      get { return (string)GetValue(CurrentFileNameProperty); }
      set { SetValue(CurrentFileNameProperty, value); }
    }

    // Using a DependencyProperty as the backing store for CurrentFileName.  This enables animation, styling, binding, etc...
    public static readonly DependencyProperty CurrentFileNameProperty =
        DependencyProperty.Register("CurrentFileName", typeof(string), typeof(MainWindow), new PropertyMetadata(null));

    ///============================================================================================
    ///
    /// Implementation
    /// 
    bool SaveCurrentFile(bool ask = false) {
      if (ask || CurrentFileName == null) {
        SaveFileDialog dlg = new SaveFileDialog() {
          InitialDirectory = Directory.GetCurrentDirectory(),
        };
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
      var result = DataModel.Connector.Execute(text);
      ShowOutput(result.Ok, result.Ok ? result.Value as string + "\nFinished, no errors." : result.Message);
      DataModel.Refresh();
    }

    void LoadDatabase(bool loadcatalog) {
      DataModel.Reload(loadcatalog);
      ShowOutput(true, string.Format(loadcatalog ? "Database '{0}' and catalog reloaded." : "Database '{0}' loaded with new catalog.", 
        DataModel.DatabaseName));
    }

    void SaveDatabase() {
      DataModel.Save();
    }

    void ShowOutput(bool ok, string message) {
      OutputText = message;
      OutputTextColour = (ok) ? "black" : "red";
    }

    ///============================================================================================
    ///
    /// Handlers
    /// 

    private void Open_Executed(object sender, ExecutedRoutedEventArgs e) {
      OpenFileDialog dlg = new OpenFileDialog() {
        InitialDirectory = Directory.GetCurrentDirectory(),
      };
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
    private void SaveAs_Executed(object sender, ExecutedRoutedEventArgs e) {
      SaveCurrentFile(true);
    }

    private void Exit_Executed(object sender, ExecutedRoutedEventArgs e) {
      Close();
    }

    private void NewCatalog_Executed(object sender, ExecutedRoutedEventArgs e) {
      LoadDatabase(false);
    }
    private void ReloadCatalog_Executed(object sender, ExecutedRoutedEventArgs e) {
      LoadDatabase(true);
    }
    private void SaveCatalog_Executed(object sender, ExecutedRoutedEventArgs e) {
      SaveDatabase();
    }
    private void Properties_Executed(object sender, ExecutedRoutedEventArgs e) {
    }

    private void About_Executed(object sender, ExecutedRoutedEventArgs e) {
      var about = new About() { AppNameVersion = AppNameVersion };
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

    private void Execute_ExpandAll(object sender, ExecutedRoutedEventArgs e) {
      databaseTreeControl.ExpandAll();
    }

    private void TextBox_PreviewKeyDown(object sender, KeyEventArgs e) {
      // the IsReadOnly flag on the control doesn't let the navigation keys work! WPF BUG?
      if (!(e.Key == Key.Down || e.Key == Key.Up || e.Key == Key.Left || e.Key == Key.Right 
         || e.Key == Key.Home || e.Key == Key.End || e.Key == Key.PageDown || e.Key == Key.PageUp 
         || e.Key == Key.Tab || e.Key == Key.Escape))
        e.Handled = true;
    }

  }
}
