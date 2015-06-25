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
  /// Implement the catalog as a whole, including multiple scope levels
  /// </summary>
  public class Catalog {
    static readonly string CatalogName = "andl_catalog";
    static readonly string VariableName = "andl_variable";
    static readonly string OperatorName = "andl_operator";
    static readonly string MemberName = "andl_member";
    static readonly DataHeading CatalogHeading = DataHeading.Create("Name:text", "Kind:text", "Type:text", "Value:binary");
    static readonly DataHeading VariableHeading = DataHeading.Create("Name:text", "Type:text", "Members:text");
    static readonly DataHeading OperatorHeading = DataHeading.Create("Name:text", "Type:text", "Members:text", "Arguments:text");
    static readonly DataHeading MemberHeading = DataHeading.Create("MemberOf:text", "Index:number", "Name:text", "Type:text", "Members:text");
    static readonly DataHeading CatalogKey = DataHeading.Create("Name:text");

    static Dictionary<string, DataHeading> _protectedheadings = new Dictionary<string, DataHeading> {
      { CatalogName, CatalogHeading },
      { VariableName, VariableHeading },
      { OperatorName, OperatorHeading },
      { MemberName, MemberHeading },
    };

    public bool IsCompiling { get; set; }       // invoke builtin functions in preview/compile mode
    public bool InteractiveFlag { get; set; }   // execute during compilation
    public bool ExecuteFlag { get; set; }       // execute after compilation
    public bool PersistFlag { get; set; }       // persist the catalog after compilation
    public bool DatabaseSqlFlag { get; set; }   // use sql as the database
    public bool NewFlag { get; set; }           // create new catalog

    //public string CatalogName { get; set; }     // name used to store catalog
    public string SystemPattern { get; set; }   // variables protected from update
    public string PersistPattern { get; set; }  // variables persisted in the catalog
    public string DatabasePattern { get; set; } // relvars kept in the database
    public string DatabasePath { get; set; }    // path to the database
    public string SourcePath { get; set; }      // path for reading a source
    DataTableLocal PersistTable { get; set; }   // table of persistent items

    VariableScope _variables { get; set; }

    public bool IsGlobalLevel { get { return _variables.Level <= ScopeLevels.Global;  } }

    public bool IsPersist(string name) {
      return Regex.IsMatch(name, PersistPattern) && !IsSystem(name);
    }
    public bool IsDatabaseSql(string name) {
      return DatabaseSqlFlag && Regex.IsMatch(name, DatabasePattern);
    }

    public bool IsSystem(string name) {
      return Regex.IsMatch(name, SystemPattern);
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
      foreach (var name in _protectedheadings.Keys) {
        SetValueInLevel(name, TypedValue.Create(DataTableLocal.Create(_protectedheadings[name])), EntryKinds.System);
      }
      //PersistTable = DataTableLocal.Create(CatalogHeading);
      if (NewFlag) {
        //SetValueInLevel(CatalogName, TypedValue.Create(PersistTable), EntryKinds.System);
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
      if (IsSystem(name)) return GetProtectedValue(name);
      return _variables.Get(name);
    }

    // Return value from variable with evaluation if needed
    public TypedValue GetValue(string name) {
      if (IsSystem(name)) return GetProtectedValue(name);
      return _variables.GetValue(name);
    }

    // Return type from variable as evaluated if needed
    public DataType GetDataType(string name) {
      if (IsSystem(name)) return DataTypeRelation.Get(GetProtectedHeading(name));
      return _variables.GetDataType(name);
    }

    VariableScope FindLevel(ScopeLevels level) {
      for (var v = _variables; v != null; v = v.Parent)
        if (v.Level == level)
          return v;
      return null;
    }

    private DataHeading GetProtectedHeading(string name) {
      if (!_protectedheadings.ContainsKey(name)) RuntimeError.Fatal("Catalog table", "invalid table name: " + name);
      return _protectedheadings[name];
    }

    private TypedValue GetProtectedValue(string name) {
      var tablemaker = CatalogTableMaker.Create(GetProtectedHeading(name));
      switch (name) {
      case "andl_catalog": 
        tablemaker.AddEntries(GetEntries(ScopeLevels.Persistent));
        break;
      case "andl_variable":
        tablemaker.AddVariables(GetEntries(ScopeLevels.Persistent));
        break;
      case "andl_operator":
        tablemaker.AddOperators(GetEntries(ScopeLevels.Persistent));
        break;
      case "andl_member":
        tablemaker.AddMembers(GetEntries(ScopeLevels.Persistent));
        break;
      default:
        RuntimeError.Fatal("Catalog table", "invalid table name: " + name);
        break;
      }
      return RelationValue.Create(tablemaker.Table);
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
      SetValueInLevel(name, datatype.Default(), EntryKinds.Type);
    }

    // Set value in current level or persist level
    // If persist and unknown, update persist table (dummy entry for now)
    void SetValueInLevel(string name, TypedValue value, EntryKinds kind) {
      if (IsSystem(name) && kind != EntryKinds.System) RuntimeError.Fatal("Catalog Set", "protected name");
      if (IsGlobalLevel && IsPersist(name)) {
        var level = FindLevel(ScopeLevels.Persistent);
        //if (level.Get(name) == null) {
        //  PersistTable.AddRow(TextValue.Create(name), TextValue.Create(kind.ToString()),
        //      TextValue.Create(value.DataType.BaseType.Name), BinaryValue.Empty);
        //}
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
            TextValue.Create(entry.DataType.BaseType.Name), 
            BinaryValue.Create(entry.ToBinary()) });
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
        var entry = CatalogEntry.FromBinary(blob);
        if (entry.Kind == EntryKinds.Link) {
          if (!LinkRelvar(entry.Name, "", entry.DataType.Heading))
            RuntimeError.Fatal("Load catalog", "adding relvar {0}", entry.Name);
        } else if (entry.Kind != EntryKinds.System) {
          level.SetValue(entry.Name, entry.Value, entry.Kind);
        }
      }
    }

  }

  //////////////////////////////////////////////////////////////////////
  /// <summary>
  /// Implement a single catalog scope level containing variables
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
        Name = name, 
        Value = value, 
        Kind = kind,
        NativeValue = TypeMaker.GetNativeValue(value),
      };
    }

    bool Exists(string key) { 
      return _entries.ContainsKey(key)
        || Parent != null && Parent.Exists(key); 
    }

    // Return raw value from variable
    internal CatalogEntry GetEntry(string name) {
      return _entries.ContainsKey(name) ? _entries[name]
        : Parent != null ? Parent.GetEntry(name)
        : CatalogEntry.Empty;
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
      var entry = GetEntry(name);
      if (entry.IsOperator)
        return entry.CodeValue.Value.Evaluate();
        //return (entry.Value as CodeValue).Value.Evaluate();
      else return entry.Value;
    }

    // Return type from variable as evaluated if needed
    public DataType GetDataType(string name) {
      if (!Exists(name)) return null;
      var entry = GetEntry(name);
      if (entry.IsOperator)
        return entry.CodeValue.Value.DataType;
        //return (entry.Value as CodeValue).Value.DataType;
      return entry.DataType;
      //var value = Get(name);
      //var type = (IsOperator) ? (value as CodeValue).Value.DataType : value.DataType;
      //return type;
    }
    
  }

  /// <summary>
  /// Implement a single entry in a catalog
  /// </summary>
  public struct CatalogEntry {
    public string Name { get; set; }
    public EntryKinds Kind { get; set; }
    public TypedValue Value { get; set; }
    public object NativeValue { get; set; }

    public DataType DataType { get { return Value.DataType; } }
    public bool IsOperator { get { return DataType == DataTypes.Code; } }
    public CodeValue CodeValue { get { return Value as CodeValue; } }

    public static readonly CatalogEntry Empty = new CatalogEntry();

    public override string ToString() {
      return String.Format("{0} {1} {2} {3}", Name, Kind, Value, DataType);
    }

    // persist a catalog entry
    public byte[] ToBinary() {
      using (var writer = PersistWriter.Create()) {
        writer.Write(Name);
        writer.Write((byte)Kind);
        if (Kind == EntryKinds.Link)
          writer.WriteValue(RelationValue.Create(DataTableLocal.Create(Value.Heading)));
        else
          writer.WriteValue(Value);
        return writer.ToArray();
      }
    }

    public static CatalogEntry FromBinary(byte[] buffer) {
      using (var reader = PersistReader.Create(buffer)) {
        var name = reader.ReadString();
        var kind = (EntryKinds)reader.ReadByte();
        TypedValue value = reader.ReadValue();
        return new CatalogEntry { Name = name, Kind = kind, Value = value };
      }
    }
  }

  /// <summary>
  /// Implement the construction of the various catalog tables
  /// </summary>
  public class CatalogTableMaker {
    public DataTableLocal Table { get; private set; }

    public static CatalogTableMaker Create(DataHeading heading) {
      return new CatalogTableMaker {
        Table = DataTableLocal.Create(heading)
      };
    }

    public CatalogTableMaker AddEntries(IEnumerable<CatalogEntry> entries) {
      foreach (var entry in entries) {
        AddEntry(entry);
      }
      return this;
    }

    public void AddEntry(CatalogEntry entry) {
      var addrow = DataRow.Create(Table.Heading, new TypedValue[] 
          { TextValue.Create(entry.Name), 
            TextValue.Create(entry.Kind.ToString()), 
            TextValue.Create(entry.DataType.BaseType.Name), 
            BinaryValue.Create(entry.ToBinary()) });
      Table.AddRow(addrow);
    }

    // Create a table of variables
    public CatalogTableMaker AddVariables(IEnumerable<CatalogEntry> entries) {
      foreach (var entry in entries.Where(e => !e.IsOperator)) {
        AddVariable(entry.Name, entry.DataType);
      }
      return this;
    }

    void AddVariable(string name, DataType datatype) {
      var addrow = DataRow.Create(Table.Heading, new TypedValue[] 
          { TextValue.Create(name), 
            TextValue.Create(datatype.BaseType.Name), 
            TextValue.Create(datatype.SubtypeName ?? "") });
      Table.AddRow(addrow);
    }

    // Create a table of operators
    public CatalogTableMaker AddOperators(IEnumerable<CatalogEntry> entries) {
      foreach (var entry in entries.Where(e => e.IsOperator)) {
        AddOperator(entry.Name, entry.CodeValue.DataType, entry.CodeValue.Value);
      }
      return this;
    }

    void AddOperator(string name, DataType datatype, ExpressionBlock value) {
      var addrow = DataRow.Create(Table.Heading, new TypedValue[] 
          { TextValue.Create(name), 
            TextValue.Create(datatype.BaseType.Name), 
            TextValue.Create(value.DataType.SubtypeName ?? ""),
            TextValue.Create(value.SubtypeName) });
      Table.AddRow(addrow);
    }

    // Create a table of variables
    public CatalogTableMaker AddMembers(IEnumerable<CatalogEntry> entries) {
      foreach (var entry in entries.Where(e => e.DataType.SubtypeName != null)) {
        AddMember(entry.DataType.SubtypeName, entry.DataType.Heading);
        // TODO: recursive call
      }
      foreach (var entry in entries.Where(e => e.IsOperator)) {
        AddMember(entry.CodeValue.Value.SubtypeName, entry.CodeValue.Value.Lookup);
      }
      return this;
    }

    void AddMember(string parent, DataHeading heading) {
      int index = 0;
      foreach (var column in heading.Columns) {
        var addrow = DataRow.Create(Table.Heading, new TypedValue[] 
          { TextValue.Create(parent), 
            NumberValue.Create(++index), 
            TextValue.Create(column.Name), 
            TextValue.Create(column.DataType.BaseType.Name),
            TextValue.Create(column.DataType.SubtypeName ?? "") });
        Table.AddRow(addrow);
        // Recursive call. note: may be duplicate, but no matter.
        if (column.DataType.SubtypeName != null)
          AddMember(column.DataType.SubtypeName, column.DataType.Heading);
      }
    }

  }
}
