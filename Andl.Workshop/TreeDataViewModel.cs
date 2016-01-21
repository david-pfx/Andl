using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Andl.Workshop {
  public class TreeDataViewModel : INotifyPropertyChanged {

    public event PropertyChangedEventHandler PropertyChanged;

    //-- props
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
    //public OperatorItem[] Operators {
    //  get { return _connector.GetOperators().Items
    //      .Select(r => new OperatorItem { Info = r }).ToArray(); }
    //}
    //public TypeItem[] Types {
    //  get { return _connector.GetTypes().Items.Select(r => new TypeItem { Info = r }).ToArray(); }
    //}

    public string DatabaseName {
      get { return _connector.Name; }
      set {
        _connector = _selector.OpenDatabase(value);
        if (PropertyChanged != null)
          PropertyChanged(this, new PropertyChangedEventArgs(null));
      }
    }
    public DatabaseConnector Connector { get { return _connector; } }

    //-- privates
    DatabaseSelector _selector;
    DatabaseConnector _connector;

    //-- ctor
    public TreeDataViewModel(DatabaseSelector database) {
      _selector = database;
      _connector = _selector.OpenDatabase();  // default/empty
    }

    public EntryItem[] GetSubEntries(string name, Runtime.EntrySubInfoKind kind) {
      return Explode(_connector.GetSubEntries(name, kind));
    }

    public EntryItem[] Explode(ItemInfo info) {
      return info.Items.Select(x => new EntryItem { Owner = this, Name = x.Key, Value = x.Value }).ToArray();
    }
  }

  //-- simple data transfer objects for each object of interest

  public struct DatabaseItem {
    public DatabaseInfo Info;
    public string Name { get { return Info.Name; } }
  }

  public class EntryItem {
    public TreeDataViewModel Owner;
    public string Name;
    public string Value;
    public EntryItem[] Attributes {
      get { return Owner.GetSubEntries(Name, Runtime.EntrySubInfoKind.Attribute); }
    }
    public EntryItem[] Arguments {
      get { return Owner.GetSubEntries(Name, Runtime.EntrySubInfoKind.Argument); }
    }
    public EntryItem[] Components {
      get { return Owner.GetSubEntries(Name, Runtime.EntrySubInfoKind.Component); }
    }

    //public AttributeItem[] Attributes {
    //  get { return Info.Attributes.Select(a => new AttributeItem { Info = a }).ToArray(); }
    //}
    public string Display { get { return String.Format("{0} : {1}", Name, Value); } }

    public bool IsExpanded { get; set; }
    public bool IsSelected { get; set; }
  }

  //public struct RelationItem {
  //  public ItemInfo Info;
  //  public string Name { get { return Info.Name; } }
  //  public int Value { get { return Info.v; } }
  //  //public AttributeItem[] Attributes {
  //  //  get { return Info.Attributes.Select(a => new AttributeItem { Info = a }).ToArray(); }
  //  //}
  //  public string Display { get { return String.Format("{0} : ({1})", Name, Count); } }

  //  public bool IsExpanded { get; set; }
  //  public bool IsSelected { get; set; }
  //}

  //public struct OperatorItem {
  //  public ItemInfo Info;
  //  public string Name { get { return Info.Name; } }
  //  public string Type { get { return Info.Type; } }
  //  //public AttributeItem[] Arguments {
  //  //  get { return Info.Arguments.Select(a => new AttributeItem { Info = a }).ToArray(); }
  //  //}
  //  public string Display { get { return String.Format("{0} : {1}", Name, Type); } }

  //  public bool IsExpanded { get; set; }
  //  public bool IsSelected { get; set; }
  //}

  //public struct TypeItem {
  //  public ItemInfo Info;
  //  public string Name { get { return Info.Name; } }
  //  public AttributeItem[] Components {
  //    get { return Info.Components.Select(a => new AttributeItem { Info = a }).ToArray(); }
  //  }
  //  public string Display { get { return String.Format("{0}", Name); } }

  //  public bool IsExpanded { get; set; }
  //  public bool IsSelected { get; set; }
  //}

  //public struct AttributeItem {
  //  public AttributeInfo Info;
  //  public string Name { get { return Info.Name; } }
  //  public string Type { get { return Info.Type; } }
  //  public string Display { get { return String.Format("{0} : {1}", Name, Type); } }

  //  public bool IsExpanded { get; set; }
  //  public bool IsSelected { get; set; }
  //}

}
