using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Andl.Runtime;

namespace Andl.Workbench {
  /// <summary>
  /// Implement data model for tree data view
  /// Also provides access to database connector
  /// </summary>
  public class TreeDataViewModel : INotifyPropertyChanged {

    public event PropertyChangedEventHandler PropertyChanged;

    //-- props for tree control
    public DatabaseItem[] Databases {
      get { return _selector.GetDatabaseList().Select(c => new DatabaseItem { Info = c }).ToArray(); }
    }
    public EntryItem[] Relations {
      get { return Explode(_connector.GetEntries( Runtime.EntryInfoKind.Relation)); }
    }
    public EntryItem[] Operators {
      get { return Explode(_connector.GetEntries(Runtime.EntryInfoKind.Operator)); }
    }
    public EntryItem[] Types {
      get { return Explode(_connector.GetEntries(Runtime.EntryInfoKind.Type)); }
    }
    public EntryItem[] Variables {
      get { return Explode(_connector.GetEntries(Runtime.EntryInfoKind.Variable)); }
    }

    // get or set the database name
    public string DatabaseName {
      get { return _connector.Name; }
      set {
        // avoid recursive call to 'reset' during data binding
        if (value == null) return;
        _connector = _selector.OpenDatabase(value, LoadCatalog);
        Refresh();
      }
    }

    public bool LoadCatalog { get; set; }

    // get the database connector
    public DatabaseConnector Connector { get { return _connector; } }

    //-- privates
    DatabaseSelector _selector;
    DatabaseConnector _connector;

    //-- ctor
    public TreeDataViewModel(DatabaseSelector database) {
      _selector = database;
      _connector = _selector.OpenDatabase();  // default/empty
      LoadCatalog = true;
    }

    public void Load(string database, bool load) {
      LoadCatalog = load;
      DatabaseName = database;
    }

    public void Reload(bool load) {
      LoadCatalog = load;
      DatabaseName = DatabaseName; // weird!
    }

    public void Save() {
      LoadCatalog = true;
      _connector.SaveCatalog();
    }

    public void Refresh() {
      if (PropertyChanged != null)
        PropertyChanged(this, new PropertyChangedEventArgs(null));
    }

    // Get sub-entries for any entry
    internal EntryItem[] GetSubEntries(string name, Runtime.EntrySubInfoKind kind) {
      return Explode(_connector.GetSubEntries(name, kind));
    }

    EntryItem[] Explode(ItemInfo info) {
      return info.Items.Select(x => new EntryItem { Owner = this, Name = x.Key, Value = x.Value }).ToArray();
    }
  }

  //-- simple data transfer objects for each object of interest

  public struct DatabaseItem {
    public DatabaseInfo Info;
    public string Name { get { return Info.Name; } }
    public string Display { get { return Name + (Info.IsSql ? " - sql" : ""); } }
    public string OpenName { get { return Name + (Info.IsSql ? ".sqandl" : ".sandl"); } }
  }

  public class EntryItem {
    public TreeDataViewModel Owner;
    public string Name;
    public string Value;

    public EntryItem[] Attributes {
      get { return Owner.GetSubEntries(Name, EntrySubInfoKind.Attribute); }
    }
    public EntryItem[] Arguments {
      get { return Owner.GetSubEntries(Name, EntrySubInfoKind.Argument); }
    }
    public EntryItem[] Components {
      get { return Owner.GetSubEntries(Name, EntrySubInfoKind.Component); }
    }

    public string Display { get { return String.Format("{0} : {1}", Name, Value); } }

    public bool IsExpanded { get; set; }
    public bool IsSelected { get; set; }
  }


}
