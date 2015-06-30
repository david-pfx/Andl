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

  // entry kinds -- persistence is sorted in this order, to ensure it restores correctly
  public enum EntryKinds { Type = 0, Value = 1, Code = 2 };
  //  public enum EntryKinds { System = 0, Type = 1, Link = 2, Value = 3 };

  // entry flags -- including visibility
  [Flags]
  public enum EntryFlags {
    None = 0,
    System = 1,     // system special, protected
    Public = 2,     // externally visible through gateway
    Persistent = 4, // entry persists in catalog (and maybe data too)
    Database = 8,   // link to relvar data stored in external database (local or SQL)
  };

  /// <summary>
  /// Implement the catalog as a whole, including multiple scope levels
  /// 
  /// Dependencies:
  ///   Evaluator, to retrieve the value of a code variable.
  ///   Configure flags, paths etc before startup.
  /// </summary>
  public class Catalog {
    static string _systempattern = "^andl.*$";
    static string _persistpattern = @"^[@A-Za-z].*$";
    static string _databasepattern = @"^[A-Za-z].*$";

    static string _localdatabasepath = "andltest.sandl";
    static string _sqldatabasepath = "andltest.sqlite";

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

    static Dictionary<string, Action<Catalog, string>> _settings = new Dictionary<string, Action<Catalog, string>> {
      { "DatabasePath", (c,s) => c.DatabasePath = s },
      { "DatabaseSqlFlag", (c,s) => c.DatabaseSqlFlag = (s.ToLower() == "true") },
    };

    // configuration settings
    public bool IsCompiling { get; set; }       // invoke builtin functions in preview/compile mode
    public bool InteractiveFlag { get; set; }   // execute during compilation
    public bool ExecuteFlag { get; set; }       // execute after compilation
    public bool PersistFlag { get; set; }       // persist the catalog after compilation
    public bool DatabaseSqlFlag { get; set; }   // use sql as the database
    public bool NewFlag { get; set; }           // create new catalog

    public string SystemPattern { get; set; }   // variables protected from update
    public string PersistPattern { get; set; }  // variables that are persisted
    public string DatabasePattern { get; set; } // relvars kept in the (SQL) database

    public string DatabasePath { get; set; }    // path to the database (either kind)
    public string SourcePath { get; set; }      // base path for reading a source
    //DataTableLocal PersistTable { get; set; }   // table of persistent items

    VariableScope _variables { get; set; }

    public bool IsGlobalLevel { get { return _variables.Level <= ScopeLevels.Global; } }

    public bool IsSystem(string name) {
      return Regex.IsMatch(name, SystemPattern);
    }

    public bool IsPersist(string name) {
      return Regex.IsMatch(name, PersistPattern) && !IsSystem(name);
    }

    public bool IsDatabase(string name) {
      return IsPersist(name) && Regex.IsMatch(name, DatabasePattern);
    }

    public IEnumerable<CatalogEntry> GetEntries(ScopeLevels level) {
      return FindLevel(level)._entries.Values;
    }

    //--- create

    Catalog() { }
    public static Catalog Create() {
      var cat = new Catalog {
        _variables = new VariableScope(ScopeLevels.Global, new VariableScope(ScopeLevels.Persistent, null)),
        SystemPattern = _systempattern,
        PersistPattern = _persistpattern,
        DatabasePattern = _databasepattern,
      };
      return cat;
    }

    // Configure settings. Most need to be done before Start
    public bool SetConfig(string key, string value) {
      if (!_settings.ContainsKey(key)) return false;
      _settings[key](this, value);
      return true;
    }

    // open the catalog for use, after all flags set up (including from lexer)
    // create catalog table here, local until the end
    public void Start() {
      if (DatabasePath == null)
        DatabasePath = (DatabaseSqlFlag) ? _sqldatabasepath : _localdatabasepath;
      if (DatabaseSqlFlag) {
        var sqleval = SqlEvaluator.Create();
        var database = Sqlite.SqliteDatabase.Create(DatabasePath, sqleval);
        SqlTarget.Configure(database);
      }
      foreach (var name in _protectedheadings.Keys) {
        var table = DataTableLocal.Create(_protectedheadings[name]);
        _variables.Add(name, table.DataType, EntryKinds.Value, EntryFlags.Public | EntryFlags.System);
        _variables.SetValue(name, TypedValue.Create(table));
      }
      GetEntry(CatalogName).Flags |= EntryFlags.Database;
      if (!NewFlag) {
        if (!LinkRelvar(CatalogName))
          RuntimeError.Fatal("Catalog", "cannot load catalog");
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
    public TypedValue GetRaw(string name) {
      if (IsSystem(name)) return GetProtectedValue(name);
      return _variables.Get(name);
    }

    // Return type from variable as evaluated if needed
    public DataType GetDataType(string name) {
      if (IsSystem(name)) return DataTypeRelation.Get(GetProtectedHeading(name));
      return _variables.GetDataType(name);
    }

    // -- internal

    // Internal get
    TypedValue Get(string name) {
      return _variables.Get(name);
    }

    CatalogEntry GetEntry(string name) {
      return _variables.GetEntry(name);
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
    // all flags now set by caller
    public void Add(string name, DataType datatype, EntryKinds kind, EntryFlags flags) {
      //if (IsPersist(name)) flags |= EntryFlags.Persistent;
      //if (datatype is DataTypeRelation && IsDatabase(name)) flags |= EntryFlags.Database;
      var level = (flags.HasFlag(EntryFlags.Persistent)) ? FindLevel(ScopeLevels.Persistent) : _variables;
      level.Add(name, datatype, kind, flags);
    }

    public void Update(string name, DataType datatype) {
      var entry = GetEntry(name);
      entry.DataType = datatype;
    }

    // Value replaces existing, type should be compatible
    public void SetValue(string name, TypedValue value) {
      var entry = GetEntry(name);
      if (entry.Flags.HasFlag(EntryFlags.System)) RuntimeError.Fatal("Catalog Set", "protected name");

      // if relation value, convert to/from Sql
      var finalvalue = value;
      // database flag means linked entry, value in database
      if (entry.IsDatabase) {
        var table = finalvalue.AsTable();
        if (DatabaseSqlFlag)
          finalvalue = RelationValue.Create(DataTableSql.Create(name, table));
        else {
          finalvalue = RelationValue.Create(DataTableLocal.Convert(table));
          // note: could defer persistence until shutdown
          Persist.Create(DatabasePath).Store(name, finalvalue);
        }
      }
      entry.Value = value;
    }

    // get the type of a relation from some persistence store
    // Used during compilation or startup -- if successful, variable will be created with flags
    // Then use AddRelvar to import the value
    public DataType GetRelvarType(string name, string source) {
      var islinked = (source == "");
      var issql = islinked && IsDatabase(name);
      if (islinked) {
        if (issql) {
          var heading = SqlTarget.Create().GetTableHeading(name);
          return (heading == null) ? null : DataTypeRelation.Get(heading);
        } else {
          var type = Persist.Create(DatabasePath).Peek(name);
          return type;
        }
      } else {
        var table = DataSourceStream.Create(source, SourcePath).Input(name, true);
        if (table != null) return table.DataType;
      }
      return null;
    }

    // Get the value of a relation from a database
    // Entry previously created by peeking
    public bool LinkRelvar(string name) {
      var entry = GetEntry(name);
      Logger.Assert(entry != null && entry.IsDatabase);

      var heading = entry.DataType.Heading;
      if (DatabaseSqlFlag) {
        var sqlheading = SqlTarget.Create().GetTableHeading(name);
        if (sqlheading == null || !heading.Equals(sqlheading))
          RuntimeError.Fatal("Catalog link relvar", "sql table not found: {0}", name);
        var table = DataTableSql.Create(name, heading);
        entry.Value = RelationValue.Create(table);
      } else {
        var tablev = Persist.Create(DatabasePath).Load(name);
        if (tablev == null || !heading.Equals(tablev.Heading))
          RuntimeError.Fatal("Catalog link relvar", "local table not found: {0}", name);
        entry.Value = RelationValue.Create(tablev.AsTable());
      }
      return true;
    }

    // Get the value of a relation by importing some other format
    // Entry previously created by peeking
    public bool ImportRelvar(string name, string source) {
      var entry = GetEntry(name);
      Logger.Assert(entry != null);
      var heading = entry.DataType.Heading;
      var table = DataSourceStream.Create(source, SourcePath).Input(name, false);
      if (table == null || !heading.Equals(table.Heading))
        RuntimeError.Fatal("Catalog link relvar", "{0} table not found: {1}", source, name);
      SetValue(name, RelationValue.Create(table));
      return true;
    }

    // Add a user type, just so it will get persisted
    public void AddUserType(string name, DataTypeUser datatype, EntryFlags flags) {
      Add(name, datatype, EntryKinds.Type, flags);
    }

    //--- persistence Mk II

    // Store the persistent catalog with current values
    public void StoreToTable() {
      var table = DataTableLocal.Create(CatalogHeading);
      foreach (var entry in GetEntries(ScopeLevels.Persistent)) {
        var addrow = DataRow.Create(CatalogHeading, new TypedValue[] 
          { TextValue.Create(entry.Name), 
            TextValue.Create(entry.Kind.ToString()), 
            TextValue.Create(entry.DataType.BaseType.Name), 
            BinaryValue.Create(entry.ToBinary()) });
        table.AddRow(addrow);
      }
      if (DatabaseSqlFlag)
        DataTableSql.Create(CatalogName, table);
      else Persist.Create(DatabasePath).Store(CatalogName, RelationValue.Create(table));
    }

    public void LoadFromTable() {
      var table = Get(CatalogName).AsTable();
      var level = FindLevel(ScopeLevels.Persistent);
      foreach (var row in table.GetRows()) {
        var blob = (row.Values[3] as BinaryValue).Value;
        var entry = CatalogEntry.FromBinary(blob);
        level.Add(entry);
        if (entry.IsDatabase) {
          if (!LinkRelvar(entry.Name))
            RuntimeError.Fatal("Load catalog", "adding relvar {0}", entry.Name);
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
    internal void Add(CatalogEntry entry) {
      _entries[entry.Name] = entry;
    }

    internal void Add(string name, DataType datatype, EntryKinds kind, EntryFlags flags) {
      Add(new CatalogEntry {
        Name = name,
        DataType = datatype,
        Kind = kind,
        Flags = flags,
      });
    }

    internal void SetValue(string name, TypedValue value) {
      Logger.Assert(value.DataType == _entries[name].DataType);
      _entries[name].Value = value;
      _entries[name].NativeValue = TypeMaker.GetNativeValue(value);
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

    // Return type from variable as evaluated if needed
    public DataType GetDataType(string name) {
      if (!Exists(name)) return null;
      return GetEntry(name).DataType;
    }

  }

  /// <summary>
  /// Implement a single entry in a catalog
  /// </summary>
  public class CatalogEntry {
    public string Name { get; set; }
    public EntryKinds Kind { get; set; }
    public EntryFlags Flags { get; set; }
    public DataType DataType { get; set; }
    public TypedValue Value { get; set; }
    public object NativeValue { get; set; }

    //public DataType DataType { get { return Value.DataType; } }
    public bool IsCode { get { return Kind == EntryKinds.Code; } }
    //public bool IsOperator { get { return DataType == DataTypes.Code; } }
    public bool IsDatabase { get { return Flags.HasFlag(EntryFlags.Database); } }
    public bool IsPublic { get { return Flags.HasFlag(EntryFlags.Public); } }
    public bool IsPersistent { get { return Flags.HasFlag(EntryFlags.Persistent); } }
    //    public bool IsSql { get { return Flags.HasFlag(EntryFlags.Sql); } }
    public bool IsSystem { get { return Flags.HasFlag(EntryFlags.System); } }
    public CodeValue CodeValue { get { return Value as CodeValue; } }

    public static readonly CatalogEntry Empty = new CatalogEntry();

    public override string ToString() {
      return String.Format("{0} {1} {2} {3} {4}", Name, Kind, Value, Flags, DataType);
    }

    // persist a catalog entry
    public byte[] ToBinary() {
      using (var writer = PersistWriter.Create()) {
        writer.Write(Name);
        writer.Write((byte)Kind);
        writer.Write((byte)Flags);
        writer.Write(DataType);
        if (IsDatabase)
          writer.WriteValue(RelationValue.Create(DataTableLocal.Create(Value.Heading)));
        else if (Kind != EntryKinds.Type)
          writer.WriteValue(Value);
        return writer.ToArray();
      }
    }

    public static CatalogEntry FromBinary(byte[] buffer) {
      using (var reader = PersistReader.Create(buffer)) {
        var name = reader.ReadString();
        var kind = (EntryKinds)reader.ReadByte();
        var flags = (EntryFlags)reader.ReadByte();
        var datatype = reader.ReadDataType();
        TypedValue value = (kind == EntryKinds.Type) ? null : reader.ReadValue();
        return new CatalogEntry { Name = name, Kind = kind, Flags = flags, DataType = datatype, Value = value };
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
      foreach (var entry in entries.Where(e => !e.IsCode)) {
        AddVariable(entry.Name, entry.DataType);
      }
      return this;
    }

    void AddVariable(string name, DataType datatype) {
      var addrow = DataRow.Create(Table.Heading, new TypedValue[] 
          { TextValue.Create(name), 
            TextValue.Create(datatype.BaseType.Name), 
            TextValue.Create(datatype.GenUniqueName ?? "") });
      Table.AddRow(addrow);
    }

    // Create a table of operators
    public CatalogTableMaker AddOperators(IEnumerable<CatalogEntry> entries) {
      foreach (var entry in entries.Where(e => e.IsCode)) {
        AddOperator(entry.Name, entry.CodeValue.DataType, entry.CodeValue.Value);
      }
      return this;
    }

    void AddOperator(string name, DataType datatype, ExpressionBlock value) {
      var addrow = DataRow.Create(Table.Heading, new TypedValue[] 
          { TextValue.Create(name), 
            TextValue.Create(datatype.BaseType.Name), 
            TextValue.Create(value.DataType.GenUniqueName ?? ""),
            TextValue.Create(value.SubtypeName) });
      Table.AddRow(addrow);
    }

    // Create a table of variables
    public CatalogTableMaker AddMembers(IEnumerable<CatalogEntry> entries) {
      foreach (var entry in entries.Where(e => e.DataType.GenUniqueName != null)) {
        AddMember(entry.DataType.GenUniqueName, entry.DataType.Heading);
        // TODO: recursive call
      }
      foreach (var entry in entries.Where(e => e.IsCode)) {
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
            TextValue.Create(column.DataType.GenUniqueName ?? "") });
        Table.AddRow(addrow);
        // Recursive call. note: may be duplicate, but no matter.
        if (column.DataType.GenUniqueName != null)
          AddMember(column.DataType.GenUniqueName, column.DataType.Heading);
      }
    }
  }
}
