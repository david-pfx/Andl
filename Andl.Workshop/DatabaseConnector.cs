using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using Andl.Gateway;
using Andl.Runtime;
using System.Windows;

namespace Andl.Workshop {

  /// <summary>
  /// Implements a connection to an Andl database
  /// </summary>
  public class DatabaseSelector {
    public const string TestDatabasePath = "$test$";

    string DatabasePath { get; set; }
    public bool IsTest {  get { return DatabasePath == TestDatabasePath; } }

    static readonly Dictionary<string, string> _settings = new Dictionary<string, string> {
      { "Noisy", "0" },
    };

    // Create a selector for a specified folder (use test data if null)
    static public DatabaseSelector Create(string databasepath = null) {
      return new DatabaseSelector() { DatabasePath = databasepath ?? TestDatabasePath };
    }

    // Create list of databases found for path
    public DatabaseInfo[] GetDatabaseList() {
      if (IsTest) return GetTestDatabaseInfo();
      // FIX: use defaults from Catalog
      var dfiles = Directory.EnumerateDirectories(DatabasePath, "*.sandl").Select(f => Path.GetFileNameWithoutExtension(f));
      var sfiles = Directory.EnumerateFiles(DatabasePath, "*.sqandl").Select(f => Path.GetFileNameWithoutExtension(f));
      return dfiles.Select(f => new DatabaseInfo { Name = f })
        .Concat(sfiles.Select(f => new DatabaseInfo { Name = f, IsSql = true }))
        .ToArray();
    }

    // Open a database, or return empty object if null
    public DatabaseConnector OpenDatabase(string name = null) {
      if (DatabasePath == TestDatabasePath)
        return new DatabaseConnector { Selector = this, Name = TestDatabasePath };
      if (name == null)
        return new DatabaseConnector { Selector = this, Name = "" };
      try {
        var gateway = GatewayFactory.Create(name, _settings);
        return new DatabaseConnector { Selector = this, Name = name, Gateway = gateway };
      } catch (ProgramException e) {
        var msg = string.Format("Error: {0}", e.Message);
        MessageBox.Show(msg, "Database error");
        return new DatabaseConnector { Selector = this, Name = name };
      }
    }

    // generate test data 
    DatabaseInfo[] GetTestDatabaseInfo() {
      return new DatabaseInfo[] {
        new DatabaseInfo {
          Name = "data",
        },
        new DatabaseInfo {
          Name = "second",
        },
        new DatabaseInfo {
          Name = "third",
        },
      };
    }
  }

  /// <summary>
  /// Implements a connection to an Andl database
  /// </summary>
  public class DatabaseConnector {
    public DatabaseSelector Selector { get; set; }
    public string Name { get; set; }
    public GatewayBase Gateway;

    public ItemInfo GetEntries(EntryInfoKind kind) {
      if (Selector.IsTest) return new ItemInfo();
      if (Gateway == null) return new ItemInfo();
      return new ItemInfo() {
        Name = Name,
        Items = Gateway.GetEntryInfoDict(kind),
      };
    }

    public ItemInfo GetSubEntries(string name, EntrySubInfoKind kind) {
      if (Selector.IsTest) return new ItemInfo();
      if (Gateway == null) return new ItemInfo();
      return new ItemInfo() {
        Name = Name,
        Items = Gateway.GetSubEntryInfoDict(name, kind),
      };
    }

    //    public ItemInfo GetRelations() {
    //      if (Selector.IsTest) return GetTestRelationInfo(Name);
    //      if (Gateway == null) return new ItemInfo();
    //      return new ItemInfo() {
    //        Name = Name,
    //        Items = Gateway.GetEntryInfoDict(EntryInfoKind.Relation),
    //      };
    //    }

    //    public ItemInfo GetOperators() {
    //      if (Selector.IsTest) return GetTestOperatorInfo(Name);
    //      if (Gateway == null) return new ItemInfo();
    //      return new ItemInfo() {
    //        Name = Name,
    //        Items = Gateway.GetEntryInfoDict(EntryInfoKind.Operator),
    //      };
    //    }

    //    public ItemInfo GetTypes() {
    //      if (Selector.IsTest) return GetTestTypeInfo(Name);
    //      if (Gateway == null) return new ItemInfo();
    //      return new ItemInfo() {
    //        Name = Name,
    //        Items = Gateway.GetEntryInfoDict(EntryInfoKind.Type),
    //      };
    //    }

    //    public ItemInfo GetVariables() {
    //      //if (Selector.IsTest)
    ////        return GetTestTypeInfo(Name);
    //      if (Gateway == null)
    //        return new ItemInfo();
    //      return new ItemInfo() {
    //        Name = Name,
    //        Items = Gateway.GetEntryInfoDict(EntryInfoKind.Variable),
    //      };
    //    }

    public string Execute(string command) {
      return String.Format("Results of {0} executing '{1}' go here.", Name, command);
    }

    ItemInfo GetTestRelationInfo(string name) {
      return new ItemInfo { Name = name,
        Items = new Dictionary<string, string> {
          { "testrel", "relation [5]" },
          { "testrel2", "relation [3]" },
        },
      };
    }

    ItemInfo GetTestOperatorInfo(string name) {
      return new ItemInfo { Name = name,
        Items = new Dictionary<string, string> {
          { "testop", "time [1]" },
          { "testop2", "text [0]" },
        },
      };
    }

    ItemInfo GetTestTypeInfo(string name) {
      return new ItemInfo {
        Name = name,
        Items = new Dictionary<string, string> {
            { "testtype", "[3]" },
          },
      };
    }

    //RelationInfo[] GetTestRelationInfo() {
    //  return new RelationInfo[] {
    //    new RelationInfo {
    //      Name = "testrel",
    //      Count = 99,
    //      Attributes = new AttributeInfo[] {
    //        new AttributeInfo {
    //          Name="testatt1",
    //          Type="text",
    //        },
    //        new AttributeInfo {
    //          Name="testatt2",
    //          Type="number",
    //        },
    //      },
    //    },
    //    new RelationInfo {
    //      Name = "testrel2",
    //      Count = 98,
    //      Attributes = new AttributeInfo[] { },
    //    },
    //  };
    //}

    //private OperatorInfo[] GetTestOperatorInfo() {
    //  return new OperatorInfo[] {
    //    new OperatorInfo {
    //      Name = "testop",
    //      Type = "time",
    //      Arguments = new AttributeInfo[] {
    //        new AttributeInfo {
    //          Name="testarg1",
    //          Type="text",
    //        },
    //        new AttributeInfo {
    //          Name="testarg2",
    //          Type="number",
    //        },
    //      },
    //    },
    //    new OperatorInfo {
    //      Name = "testop2",
    //      Type = "time",
    //      Arguments = new AttributeInfo[] { },
    //    },
    //  };
    //}

  }

  public class DatabaseInfo {
    public string Name;
    public bool IsSql;
  }

  public class ItemInfo {
    public string Name;
    public Dictionary<string, string> Items = new Dictionary<string, string>();
  }

  //public struct RelationInfo {
  //  public string Name;
  //  public int Count;
  //  public AttributeInfo[] Attributes;
  //}

  //public struct AttributeInfo {
  //  public string Name;
  //  public string Type;
  //}

  //public struct OperatorInfo {
  //  public string Name;
  //  public string Type;
  //  public AttributeInfo[] Arguments;
  //}

  //public struct TypeInfo {
  //  public string Name;
  //  public AttributeInfo[] Components;
  //}

}
