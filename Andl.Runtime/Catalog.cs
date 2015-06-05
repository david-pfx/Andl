/// Andl is A New Data Language. See andl.org.
///
/// Copyright © David M. Bennett 2015 as an unpublished work. All rights reserved.
///
/// If you have received this file directly from me then you are hereby granted 
/// permission to use it for personal study. For any other use you must ask my 
/// permission. Not to be copied, distributed or used commercially without my 
/// explicit written permission.
///
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Andl.Runtime {
  // scope levels, can be compared in this order
  public enum ScopeLevels { Persistent = 0, Global = 1, Local = 2 };
  
  // entry kinds -- persistence is sorted in this order
  public enum EntryKinds { System = 0, Type = 1, Link = 2, Value = 3 };

  /// <summary>
  /// Implement a single entry in a catalog
  /// </summary>
  public struct CatalogEntry {
    public string Name { get; set; }
    public EntryKinds Kind { get; set; }
    public TypedValue Value { get; set; }
  }

  /// <summary>
  /// Implement the catalog as a whole, including multiple scope levels
  /// </summary>
  public class Catalog {
    static readonly DataHeading CatalogHeading = DataHeading.Create("Name:text", "Kind:text", "Type:text", "Value:binary");
    static readonly DataHeading CatalogKey = DataHeading.Create("Name:text");

    public bool IsCompiling { get; set; }       // invoke builtin functions in preview/compile mode
    public bool InteractiveFlag { get; set; }   // execute during compilation
    public bool ExecuteFlag { get; set; }       // execute after compilation
    public bool PersistFlag { get; set; }       // persist the catalog after compilation
    public bool DatabaseSqlFlag { get; set; }   // use sql as the database
    public bool NewFlag { get; set; }           // create new catalog

    public string CatalogName { get; set; }     // name used to store catalog
    public string ProtectPattern { get; set; }  // variables protected from update
    public string PersistPattern { get; set; }  // variables persisted in the catalog
    public string DatabasePattern { get; set; } // relvars kept in the database
    public string DatabasePath { get; set; }    // path to the database
    public string SourcePath { get; set; }      // path for reading a source
    public DataTableLocal PersistTable { get; set; } // table of persistent items

    VariableScope _variables { get; set; }

    public bool IsGlobalLevel { get { return _variables.Level <= ScopeLevels.Global;  } }

    public bool IsPersist(string name) {
      return Regex.IsMatch(name, PersistPattern);
    }
    public bool IsDatabaseSql(string name) {
      return DatabaseSqlFlag && Regex.IsMatch(name, DatabasePattern);
    }

    public bool IsDatabaseLocal(string name) {
      return !DatabaseSqlFlag && Regex.IsMatch(name, DatabasePattern);
    }

    public IEnumerable<CatalogEntry> GetEntries(ScopeLevels level) {
      return FindLevel(level)._entries.Values; 
    }

    //--- create

    Catalog() { }
    public static Catalog Create() {
      var cat = new Catalog {
        _variables = new VariableScope(ScopeLevels.Global, new VariableScope(ScopeLevels.Persistent, null)),
      };
      return cat;
    }

    // open the catalog for use, after all flags set up (including from lexer)
    // create catalog table here, local until the end
    public void Start() {
      var sqleval = SqlEvaluator.Create();
      if (DatabaseSqlFlag) {
        var database = Sqlite.SqliteDatabase.Create(DatabasePath, sqleval);
        SqlTarget.Configure(database);
      }
      PersistTable = DataTableLocal.Create(CatalogHeading);
      if (NewFlag) {
        SetValueInLevel(CatalogName, TypedValue.Create(PersistTable), EntryKinds.System);
      } else {
        LinkRelvar(CatalogName, "", CatalogHeading);
        LoadFromTable();
      }
    }

    // All done, persist catalog if required
    public void Finish() {
      if (PersistFlag)
        StoreToTable();
    }

    public void PushScope() {
      _variables = new VariableScope(ScopeLevels.Local, _variables);
    }

    public void PopScope() {
      _variables = _variables.Parent;
    }

    // Return raw value from variable
    public TypedValue Get(string name) {
      return _variables.Get(name);
    }

    // Return value from variable with evaluation if needed
    public TypedValue GetValue(string name) {
      return _variables.GetValue(name);
    }

    // Return type from variable as evaluated if needed
    public DataType GetDataType(string name) {
      return _variables.GetDataType(name);
    }

    VariableScope FindLevel(ScopeLevels level) {
      for (var v = _variables; v != null; v = v.Parent)
        if (v.Level == level)
          return v;
      return null;
    }

    ///-----------------------------------------------------------------
    ///
    /// Operations on values
    /// 

    // Add a named entry
    // Equivalant to assignment: value replaces existing, type should be compatible
    public void SetValue(string name, TypedValue value) {
      var finalvalue = value;
      var kind = EntryKinds.Value;
      // first sort out relation storage: sql store, local store or in catalog
      if (value is RelationValue) {
        var table = (value as RelationValue).AsTable();
        if (IsDatabaseSql(name)) {
          if (table.IsLocal)
            finalvalue = RelationValue.Create(DataTableSql.Create(name, table));
          kind = EntryKinds.Link;
        } else if (IsDatabaseLocal(name)) {
          if (!table.IsLocal)
            finalvalue = RelationValue.Create(DataTableLocal.Create(table));
          // note: could defer persistence until shutdown
          Persist.Create(DatabasePath).Store(name, finalvalue);
          kind = EntryKinds.Link;
        } else {
          if (!table.IsLocal)
            finalvalue = RelationValue.Create(DataTableLocal.Create(table));
        }
      }
      SetValueInLevel(name, finalvalue, kind);
    }

    // get the type of a relation from some persistence store
    // Used during compilation; use AddRelvar to import the value
    public DataType GetRelvarType(string name, string source) {
      var v = Get(name);
      if (v != null && v.DataType is DataTypeRelation)
        return v.DataType;
      if (IsDatabaseSql(name) && source == "") {
        var heading = SqlTarget.Create().GetTableHeading(name);
        return (heading == null) ? null : DataTypeRelation.Get(heading);
      }
      if (IsDatabaseLocal(name) && source == "") {
        var type = Persist.Create(DatabasePath).Peek(name);
        return type;
      }
      if (source != "") {
        var table = DataSourceStream.Create(source, SourcePath).Input(name, true);
        if (table != null) return table.DataType;
      }
      return null;
    }

    // get the value of a relation from some persistence store
    public bool LinkRelvar(string name, string source, DataHeading heading) {
      var v = Get(name);
      if (v != null)
        return v.DataType is DataTypeRelation && v.DataType.Heading.Equals(heading);
      var level = (IsGlobalLevel && IsPersist(name))
        ? FindLevel(ScopeLevels.Persistent) : _variables;
      if (IsDatabaseSql(name) && source == "") {
        var sqlheading = SqlTarget.Create().GetTableHeading(name);
        if (sqlheading == null || !heading.Equals(sqlheading))
          RuntimeError.Fatal("Catalog link relvar", "sql table not found: {0}", name);
        var table = DataTableSql.Create(name, heading);
        SetValueInLevel(name, RelationValue.Create(table), EntryKinds.Link);
        return true;
      }
      if (IsDatabaseLocal(name) && source == "") {
        var table = Persist.Create(DatabasePath).Load(name);
        if (table == null || !heading.Equals(table.Heading))
          RuntimeError.Fatal("Catalog link relvar", "local store table not found: {0}", name);
        SetValueInLevel(name, RelationValue.Create(table.AsTable()), EntryKinds.Link);
        return true;
      }
      if (source != "") {
        var table = DataSourceStream.Create(source, SourcePath).Input(name, false);
        if (table == null || !heading.Equals(table.Heading))
          RuntimeError.Fatal("Catalog link relvar", "csv table not found: {0}", name);
        SetValueInLevel(name, RelationValue.Create(table), EntryKinds.Link);
        return true;
      }
      return false;
    }

    // Add a user type, just so it will get persisted
    public void AddUserType(string name, DataTypeUser datatype) {
      //var level = (IsGlobalLevel && IsPersist(name))
      //  ? FindLevel(ScopeLevels.Persistent) : _variables;
      //level.SetValue(name, datatype.GetDefault(), EntryKinds.User);
      SetValueInLevel(name, datatype.GetDefault(), EntryKinds.Type);
    }

    // Set value in current level or persist level
    // If persist and unknown, update persist table (dummy entry for now)
    void SetValueInLevel(string name, TypedValue value, EntryKinds kind) {
      if (IsGlobalLevel && IsPersist(name)) {
        var level = FindLevel(ScopeLevels.Persistent);
        if (level.Get(name) == null) {
          PersistTable.AddRow(TextValue.Create(name), TextValue.Create(kind.ToString()),
              TextValue.Create(value.DataType.BaseType.Name), BinaryValue.Empty);
        }
        level.SetValue(name, value, kind);
      } else _variables.SetValue(name, value, kind);
    }

    //--- persistence Mk II

    // Store the persistent catalog with current values
    public void StoreToTable() {
      PersistTable = DataTableLocal.Create(CatalogHeading);
      foreach (var entry in GetEntries(ScopeLevels.Persistent)) {
        var addrow = DataRow.Create(CatalogHeading, new TypedValue[] 
          { TextValue.Create(entry.Name), 
            TextValue.Create(entry.Kind.ToString()), 
            TextValue.Create(entry.Value.DataType.BaseType.Name), 
            BinaryValue.Create(ToBinary(entry)) });
        PersistTable.AddRow(addrow);
      }
      if (DatabaseSqlFlag)
        DataTableSql.Create(CatalogName, PersistTable);
      else Persist.Create(DatabasePath).Store(CatalogName, RelationValue.Create(PersistTable));
    }

    public void LoadFromTable() {
      DataTable table = Get(CatalogName).AsTable();
      var level = FindLevel(ScopeLevels.Persistent);
      foreach (var row in table.GetRows()) {
        var blob = (row.Values[3] as BinaryValue).Value;
        var entry = FromBinary(blob);
        if (entry.Kind == EntryKinds.Link) {
          if (!LinkRelvar(entry.Name, "", entry.Value.DataType.Heading))
            RuntimeError.Fatal("Load catalog", "adding relvar {0}", entry.Name);
        } else if (entry.Kind != EntryKinds.System) {
          level.SetValue(entry.Name, entry.Value, entry.Kind);
        }
      }
    }

        //  var name = (row.Values[0] as TextValue).Value;
        //  var kind = (EntryKinds)Enum.Parse(typeof(EntryKinds), (row.Values[1] as TextValue).Value);
        //  var type = (row.Values[2] as TextValue).Value;
        //  var content = (entry.Kind == EntryKinds.Value)
        //    ? PersistWriter.ToBinary(entry.Value)
        //    : PersistWriter.ToBinary(datatype.Heading);

        //}

    //--- persistence

    // persist a catalog entry
    public byte[] ToBinary(CatalogEntry entry) {
      using (var writer = PersistWriter.Create()) {
        writer.Write(entry.Name);
        writer.Write((byte)entry.Kind);
        if (entry.Kind == EntryKinds.Link)
          writer.WriteValue(RelationValue.Create(DataTableLocal.Create(entry.Value.Heading)));
        else
          writer.WriteValue(entry.Value);

        return writer.ToArray();
      }
    }

    public CatalogEntry FromBinary(byte[] buffer) {
      using (var reader = PersistReader.Create(buffer)) {
        var name = reader.ReadString();
        var kind = (EntryKinds)reader.ReadByte();
        TypedValue value = reader.ReadValue();
        return new CatalogEntry { Name = name, Kind = kind, Value = value };
      }
    }
  }

  //////////////////////////////////////////////////////////////////////
  /// <summary>
  /// Implement a single catalog scope level
  /// </summary>
  public class VariableScope {
    internal Dictionary<string, CatalogEntry> _entries = new Dictionary<string, CatalogEntry>();
    internal VariableScope Parent { get; private set; }
    internal ScopeLevels Level { get; private set; }

    internal VariableScope(ScopeLevels level, VariableScope parent = null) {
      Level = level;
      Parent = parent;
    }

    // Add a named entry
    internal void SetValue(string name, TypedValue value, EntryKinds kind = EntryKinds.Value) {
      _entries[name] = new CatalogEntry {
        Name = name, Value = value, Kind = kind,
      };
    }

    bool Exists(string key) { 
      return _entries.ContainsKey(key)
        || Parent != null && Parent.Exists(key); 
    }

    // Return raw value from variable
    internal TypedValue Get(string name) {
      return _entries.ContainsKey(name) ? _entries[name].Value
        : Parent != null ? Parent.Get(name)
        : null;
    }

    // Return value from variable with evaluation if needed
    internal TypedValue GetValue(string name) {
      if (!Exists(name)) return null;
      var value = Get(name);
      if (value.DataType == DataTypes.Code)
        return (value as CodeValue).Value.Evaluate();
      else return value;
    }

    // Return type from variable as evaluated if needed
    public DataType GetDataType(string name) {
      if (!Exists(name)) return null;
      var value = Get(name);
      var type = (value.DataType == DataTypes.Code) ? (value as CodeValue).Value.DataType : value.DataType;
      return type;
    }
    
  }
}
