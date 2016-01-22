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
    public const string TestDatabaseSearchPath = "$test$";

    string DatabaseSearchPath { get; set; }
    public bool IsTest {  get { return DatabaseSearchPath == TestDatabaseSearchPath; } }

    static readonly Dictionary<string, string> _settings = new Dictionary<string, string> {
      { "Noisy", "3" },
    };

    // Create a selector for a specified folder (use test data if null)
    static public DatabaseSelector Create(string databasepath = null) {
      Logger.OpenTrace(0);
      return new DatabaseSelector() { DatabaseSearchPath = databasepath ?? TestDatabaseSearchPath };
    }

    // Create list of databases found for path
    public DatabaseInfo[] GetDatabaseList() {
      if (IsTest) return GetTestDatabaseInfo();
      // FIX: use defaults from Catalog
      var dfiles = Directory.EnumerateDirectories(DatabaseSearchPath, "*.sandl").Select(f => Path.GetFileNameWithoutExtension(f));
      var sfiles = Directory.EnumerateFiles(DatabaseSearchPath, "*.sqandl").Select(f => Path.GetFileNameWithoutExtension(f));
      return dfiles.Select(f => new DatabaseInfo { Name = f })
        .Concat(sfiles.Select(f => new DatabaseInfo { Name = f, IsSql = true }))
        .ToArray();
    }

    // Open a database, or return empty object if null
    public DatabaseConnector OpenDatabase(string name = null) {
      if (IsTest) return new DatabaseConnector { Selector = this, Name = TestDatabaseSearchPath };
      if (name == null) return new DatabaseConnector { Selector = this, Name = "" };
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

    // Retrieve entries of a type
    public ItemInfo GetEntries(EntryInfoKind kind) {
      if (Selector.IsTest) return new ItemInfo();
      if (Gateway == null) return new ItemInfo();
      return new ItemInfo() {
        Name = Name,
        Items = Gateway.GetEntryInfoDict(kind),
      };
    }

    // Retrieve sub-entries for a name
    public ItemInfo GetSubEntries(string name, EntrySubInfoKind kind) {
      if (Selector.IsTest) return new ItemInfo();
      if (Gateway == null) return new ItemInfo();
      return new ItemInfo() {
        Name = Name,
        Items = Gateway.GetSubEntryInfoDict(name, kind),
      };
    }

    // execute a script
    public Result Execute(string command) {
      if (Selector.IsTest) return Result.Failure("This is a test!!!");
      if (Gateway == null) return Result.Failure(String.Format("Database {0} not connected!", Name));
      return Gateway.Execute(command, ExecModes.Raw);
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

  }

  // Simple objects to hold data
  public class DatabaseInfo {
    public string Name = "?";
    public bool IsSql = false;
  }

  public class ItemInfo {
    public string Name = "?";
    public Dictionary<string, string> Items = new Dictionary<string, string>();
  }
}
