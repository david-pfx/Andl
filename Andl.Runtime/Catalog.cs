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
  public enum EntryKinds { None = 0, Type = 1, Value = 2, Code = 3 };

  // entry flags -- including visibility
  [Flags]
  public enum EntryFlags {
    None = 0,
    System = 1,     // system special, protected
    Public = 2,     // externally visible through gateway
    Persistent = 4, // entry persists in catalog (and maybe data too)
    Database = 8,   // link to relvar data stored in external database (local or SQL)
  };

  ///===========================================================================
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

    static readonly string _localdatabasepath = "andl.sandl";
    static readonly string _sqldatabasepath = "andl.sqlite";
    static readonly string _catalogname = "default";
    static readonly string _sysprefix = "andl_";

    // internally visible tables
    static readonly string _catalogtablename = "andl_catalog";
    static readonly string _variabletablename = "andl_variable";
    static readonly string _operatortablename = "andl_operator";
    static readonly string _membertablename = "andl_member";

    static readonly DataHeading CatalogHeading = DataHeading.Create("Name:text", "Kind:text", "Type:text", "Value:binary");
    static readonly DataHeading VariableHeading = DataHeading.Create("Name:text", "Type:text", "Members:text");
    static readonly DataHeading OperatorHeading = DataHeading.Create("Name:text", "Members:text", "Arguments:text");
    static readonly DataHeading MemberHeading = DataHeading.Create("MemberOf:text", "Index:number", "Name:text", "Type:text", "Members:text");
    static readonly DataHeading CatalogKey = DataHeading.Create("Name:text");

    //static Dictionary<string, DataHeading> _protectedheadings;

    static Dictionary<string, DataHeading> _protectedheadings = new Dictionary<string, DataHeading> {
      { _catalogtablename, CatalogHeading },
      { _variabletablename, VariableHeading },
      { _operatortablename, OperatorHeading },
      { _membertablename, MemberHeading },
    };

    static Dictionary<string, Func<CatalogTableMaker, IEnumerable<CatalogEntry>, CatalogTableMaker>> _protectedtablemaker = 
      new Dictionary<string,Func<CatalogTableMaker, IEnumerable<CatalogEntry>,CatalogTableMaker>> {
      { _catalogtablename, (c, e) => c.AddEntries(e) },
      { _variabletablename, (c, e) => c.AddVariables(e) },
      { _operatortablename, (c, e) => c.AddOperators(e) },
      { _membertablename, (c, e) => c.AddMembers(e) },
    };

    static Dictionary<string, Action<Catalog, string>> _settings = new Dictionary<string, Action<Catalog, string>> {
      { "DatabasePath", (c,s) => c.DatabasePath = s },
      { "DatabaseSqlFlag", (c,s) => c.DatabaseSqlFlag = (s.ToLower() == "true") },
      { "CatalogName", (c,s) => c.CatalogName = s },
      { "Noisy", (c,s) => Logger.Level = Int32.Parse(s) },
    };

    // configuration settings
    //public bool IsCompiling { get; set; }       // invoke builtin functions in preview/compile mode
    public bool InteractiveFlag { get; set; }   // execute during compilation
    public bool ExecuteFlag { get; set; }       // execute after compilation
    public bool LoadFlag { get; set; }          // load an existing catalog (else create new)
    public bool SaveFlag { get; set; }          // save the updated the catalog after compilation
    public bool DatabaseSqlFlag { get; set; }   // use sql as the database

    public string SystemPattern { get; set; }   // variables protected from update
    public string PersistPattern { get; set; }  // variables that are persisted
    public string DatabasePattern { get; set; } // relvars kept in the (SQL) database

    public string BaseName { get; set; }        // base name for application
    public string CatalogName { get; set; }     // public name for the catalog
    public string CatalogFullName { get { return _sysprefix + CatalogName; } } // storage name
    public string DatabasePath { get; set; }    // path to the database (either kind)
    public string SourcePath { get; set; }      // base path for reading a source

    // predefined scopes, accessed globally
    public CatalogScope PersistentVars { get; private set; }
    public CatalogScope GlobalVars { get; private set; } 

    public bool IsSystem(string name) {
      return Regex.IsMatch(name, SystemPattern);
    }

    public bool IsPersist(string name) {
      return Regex.IsMatch(name, PersistPattern) && !IsSystem(name);
    }

    public bool IsDatabase(string name) {
      return IsPersist(name) && Regex.IsMatch(name, DatabasePattern);
    }

    //--- create

    Catalog() { }
    public static Catalog Create() {
      var cat = new Catalog {
        CatalogName = _catalogname,
        SystemPattern = _systempattern,
        PersistPattern = _persistpattern,
        DatabasePattern = _databasepattern,
      };
      cat.PersistentVars = CatalogScope.Create(cat, ScopeLevels.Persistent, null);
      cat.GlobalVars = CatalogScope.Create(cat, ScopeLevels.Global, cat.PersistentVars);
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

      // Add alternate catalog name as alias
      _protectedheadings.Add(CatalogFullName, _protectedheadings[_catalogtablename]);
      _protectedtablemaker.Add(CatalogFullName, _protectedtablemaker[_catalogtablename]);
      //if (CatalogName != _catalogtablename) {
      //  _protectedheadings.Add(CatalogName, _protectedheadings[_catalogtablename]);
      //  _protectedtablemaker.Add(CatalogName, _protectedtablemaker[_catalogtablename]);
      //}

      foreach (var name in _protectedheadings.Keys) {
        var table = DataTableLocal.Create(_protectedheadings[name]);
        GlobalVars.Add(name, table.DataType, EntryKinds.Value, EntryFlags.Public | EntryFlags.System, TypedValue.Create(table));
      }
      GlobalVars.FindEntry(CatalogFullName).Flags |= EntryFlags.Database;
      if (LoadFlag) {
        if (!LinkRelvar(CatalogFullName))
          RuntimeError.Fatal("Catalog", "cannot load catalog");
        LoadFromTable();
        Logger.WriteLine(1, "Loaded catalog '{0}'", CatalogName);
      }
    }

    // All done, persist catalog if required
    public void Finish() {
      if (SaveFlag) {
        StoreToTable();
        Logger.WriteLine(1, "Updated catalog '{0}'", CatalogName);
      }
    }

    // handle special protected pseudo-tables
    internal DataHeading GetProtectedHeading(string name) {
      DataHeading heading;
      if (_protectedheadings.TryGetValue(name, out heading)) return heading;
      else return null;
      //if (!_protectedheadings.ContainsKey(name)) return null;
      //return _protectedheadings[name];
    }

    internal TypedValue GetProtectedValue(string name) {
      var heading = GetProtectedHeading(name);
      if (heading == null) return null;
      var tablemaker = CatalogTableMaker.Create(heading);
      _protectedtablemaker[name](tablemaker, PersistentVars.GetEntries());
      return RelationValue.Create(tablemaker.Table);
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
          var type = Persist.Create(DatabasePath, false).Peek(name);
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
      var entry = GlobalVars.FindEntry(name);
      Logger.Assert(entry != null && entry.IsDatabase);

      var heading = entry.DataType.Heading;
      if (DatabaseSqlFlag) {
        var sqlheading = SqlTarget.Create().GetTableHeading(name);
        if (sqlheading == null || !heading.Equals(sqlheading))
          RuntimeError.Fatal("Catalog link relvar", "sql table not found: {0}", name);
        var table = DataTableSql.Create(name, heading);
        entry.Value = RelationValue.Create(table);
      } else {
        var tablev = Persist.Create(DatabasePath, false).Load(name);
        if (tablev == null || !heading.Equals(tablev.Heading))
          RuntimeError.Fatal("Catalog link relvar", "local table not found: {0}", name);
        entry.Value = RelationValue.Create(tablev.AsTable());
      }
      return true;
    }

    // Get the value of a relation by importing some other format
    // Entry previously created by peeking
    public bool ImportRelvar(string name, string source) {
      var entry = GlobalVars.FindEntry(name);
      Logger.Assert(entry != null);
      var heading = entry.DataType.Heading;
      var table = DataSourceStream.Create(source, SourcePath).Input(name, false);
      if (table == null || !heading.Equals(table.Heading))
        RuntimeError.Fatal("Catalog link relvar", "{0} table not found: {1}", source, name);
      GlobalVars.SetValue(name, RelationValue.Create(table));
      return true;
    }

    // Add a user type, just so it will get persisted
    public void AddUserType(string name, DataTypeUser datatype, EntryFlags flags) {
      GlobalVars.Add(name, datatype, EntryKinds.Type, flags);
    }

    //--- persistence Mk II

    // Store the persistent catalog with current values
    public void StoreToTable() {
      var table = DataTableLocal.Create(CatalogHeading);
      foreach (var entry in PersistentVars.GetEntries()) {
        var addrow = DataRow.Create(CatalogHeading, new TypedValue[] 
          { TextValue.Create(entry.Name), 
            TextValue.Create(entry.Kind.ToString()), 
            TextValue.Create(entry.DataType.BaseType.Name), 
            BinaryValue.Create(entry.ToBinary()) });
        table.AddRow(addrow);
      }
      if (DatabaseSqlFlag)
        DataTableSql.Create(CatalogFullName, table);
      else Persist.Create(DatabasePath, true).Store(CatalogFullName, RelationValue.Create(table));
    }

    public void LoadFromTable() {
      var table = GlobalVars.GetValue(CatalogFullName).AsTable();
      //var level = FindLevel(ScopeLevels.Persistent);
      foreach (var row in table.GetRows()) {
        var blob = (row.Values[3] as BinaryValue).Value;
        var entry = CatalogEntry.FromBinary(blob);
        PersistentVars.Add(entry);
        if (entry.IsDatabase) {
          if (!LinkRelvar(entry.Name))
            RuntimeError.Fatal("Load catalog", "adding relvar {0}", entry.Name);
        }
      }
    }
  }

  ///===========================================================================
  /// <summary>
  /// Implement a single catalog scope level containing variables
  /// </summary>
  public class CatalogScope {
    internal Catalog Catalog { get; private set; }
    internal CatalogScope Parent { get; private set; }
    internal ScopeLevels Level { get; private set; }

    internal Dictionary<string, CatalogEntry> _entries = new Dictionary<string, CatalogEntry>();

    public static CatalogScope Create(Catalog catalog, ScopeLevels level, CatalogScope parent = null) {
      return new CatalogScope {
        Catalog = catalog,
        Level = level,
        Parent = parent,
      };
    }

    public IEnumerable<CatalogEntry> GetEntries() {
      return _entries.Values;
    }

    // Add a named entry
    internal void Add(CatalogEntry entry) {
      _entries[entry.Name] = entry;
    }

    // Add new catalog entry to proper scope
    // Risky: caller may not know where it went; maybe merge Global & Persistent?
    public void Add(string name, DataType datatype, EntryKinds kind, EntryFlags flags = EntryFlags.None, TypedValue value = null) {
      var scope = (flags.HasFlag(EntryFlags.Persistent)) ? Catalog.PersistentVars : this;
      scope.Add(new CatalogEntry {
        Name = name,
        DataType = datatype,
        Kind = kind,
        Flags = flags,
        Scope = scope,
        Value = value,
      });
    }

    // Add a new entry to the catalog.
    // Do kind, flags and other stuff here
    public void AddNew(string name, DataType datatype, EntryKinds kind, EntryFlags flags) {
      if (Catalog.IsPersist(name)) flags |= EntryFlags.Persistent;
      if (kind == EntryKinds.Value && datatype is DataTypeRelation && Catalog.IsDatabase(name))
        flags |= EntryFlags.Database;
      Add(name, datatype, kind, flags, datatype.DefaultValue());   // make sure it has something, to avoid later errrors
      if (datatype is DataTypeRelation)
        (datatype as DataTypeRelation).ProposeCleanName(name);
      if (datatype is DataTypeTuple)
        (datatype as DataTypeTuple).ProposeCleanName(name);
    }


    // set a variable to a new value of the same type
    // NOTE: if level is global, needs concurrency control
    internal void Set(string name, TypedValue value) {
      _entries[name].Set(value);
      //Logger.Assert(value.DataType == _entries[name].DataType);
      //_entries[name].Value = value;
      //_entries[name].NativeValue = TypeMaker.ToNativeValue(value);
    }

    // Return raw value from variable
    internal CatalogEntry FindEntry(string name) {
      CatalogEntry entry;
      return (_entries.TryGetValue(name, out entry)) ? entry
        : Parent != null ? Parent.FindEntry(name)
        : null;
    }

    // Return raw value from variable
    internal TypedValue GetValue(string name) {
      var entry = FindEntry(name);
      return entry == null ? null : entry.Value;
    }

    // Return type from variable as evaluated if needed
    public DataType GetDataType(string name) {
      var entry = FindEntry(name);
      return entry == null ? null : entry.DataType;
    }

    // Value replaces existing, type should be compatible
    // Supports assignment. Handles linked tables.
    public void SetValue(string name, TypedValue value) {
      if (Catalog.IsSystem(name)) RuntimeError.Fatal("Catalog Set", "protected name");
      var entry = FindEntry(name);
      if (entry == null && Level == ScopeLevels.Local) {
        Add(name, value.DataType, EntryKinds.Value);
        return;
      }
      Logger.Assert(entry != null);
      entry.Set(value);

      // if relation value, convert to/from Sql
      // database flag means linked entry, value in database
      if (entry.IsDatabase) {
        var table = value.AsTable();
        if (Catalog.DatabaseSqlFlag)
          RelationValue.Create(DataTableSql.Create(name, table));
        else {
          var finalvalue = RelationValue.Create(DataTableLocal.Convert(table));
          // note: could defer persistence until shutdown
          Persist.Create(Catalog.DatabasePath, true).Store(name, finalvalue);
        }
      }
    }

    // Return type for an entry that is settable
    public DataType GetSetterType(string name) {
      var entry = FindEntry(name);
      if (entry == null) return null;
      if (entry.Kind == EntryKinds.Value) return entry.DataType;
      if (entry.Kind == EntryKinds.Code) {
        var expr = entry.Value as CodeValue;
        if (expr.Value.NumArgs == 1) return expr.Value.Lookup.Columns[0].DataType;
      }
      return null;
    }

    // Return types for arguments
    public DataType[] GetArgumentTypes(string name) {
      var entry = FindEntry(name);
      if (entry == null) return null;
      var expr = entry.Value as CodeValue;
      if (expr == null) return null;
      return expr.Value.Lookup.Columns.Select(c => c.DataType).ToArray();
    }

  }


  ///===========================================================================
  /// <summary>
  /// Implement a private catalog to support nested scopes
  /// 
  /// Note: initially created at global scope. Must Push to get new scope for local variables.
  /// </summary>
  public class CatalogPrivate {
    internal Catalog Catalog { get; private set; }
    internal CatalogScope Current { get; private set; }

    public static CatalogPrivate Create(Catalog catalog, bool global = false) {
      return new CatalogPrivate {
        Catalog = catalog,
        Current = (global) ? catalog.GlobalVars
                           : CatalogScope.Create(catalog, ScopeLevels.Local, catalog.GlobalVars),
      };
    }

    public void PopScope() {
      Current = Current.Parent;
    }

    public void PushScope() {
      Current = CatalogScope.Create(Catalog, ScopeLevels.Local, Current);
    }

    // Set a value for a variable in the current scope ie assignment
    public void SetValue(string name, TypedValue value) {
      Current.SetValue(name, value);
    }

    // Return type of entry
    public EntryKinds GetKind(string name) {
      var entry = Current.FindEntry(name);
      return (entry == null) ? EntryKinds.None : entry.Kind;
    }

    // Return raw value of variable 
    public TypedValue GetValue(string name) {
      var value = Catalog.GetProtectedValue(name);
      if (value != null) return value;
      return Current.GetValue(name);
    }

    // Return raw type of variable 
    public DataType GetDataType(string name) {
      return Current.GetDataType(name);
    }

  }

  ///===========================================================================
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
    public CatalogScope Scope { get; set; }

    public bool IsCode { get { return Kind == EntryKinds.Code; } }
    public bool IsDatabase { get { return Flags.HasFlag(EntryFlags.Database); } }
    public bool IsPublic { get { return Flags.HasFlag(EntryFlags.Public); } }
    public bool IsPersistent { get { return Flags.HasFlag(EntryFlags.Persistent); } }
    public bool IsSystem { get { return Flags.HasFlag(EntryFlags.System); } }
    public CodeValue CodeValue { get { return Value as CodeValue; } }

    public static readonly CatalogEntry Empty = new CatalogEntry();

    // Common code for setting a value
    public void Set(TypedValue value) {
      Value = value;
      if (value.DataType != DataTypes.Code) {
        Logger.Assert(value.DataType == DataType);
        // TEMP: following code is just so it gets exercised
        NativeValue = TypeMaker.ToNativeValue(value);
        if (NativeValue != null)  // TODO: CodeValue
          Value = TypeMaker.FromNativeValue(NativeValue, DataType);
        TypedValueBuilderTest.Test(value);
      }
    }

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

  ///===========================================================================
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

  ///===========================================================================
  /// <summary>
  /// Implement writing out varioues interface definitions
  /// </summary>
  public class CatalogInterfaceWriter {
    List<DataType> _datatypes = new List<DataType>();

    public void WriteThrift(TextWriter tw, string basename, IEnumerable<CatalogEntry> entries) {
      var operators = entries.Where(e => e.IsCode)
        .Select(e => e.CodeValue.Value).ToArray();
      AddTypes(operators.Select(e => e.DataType));
      AddTypes(operators.SelectMany(e => e.Lookup.Columns.Select(c => c.DataType)));
      ThriftGen.Process(tw, basename, _datatypes.ToArray(), operators);
    }

    //public void WriteThrift(TextWriter tw, string basename, IEnumerable<CatalogEntry> entries) {
    //  var operators = entries.Where(e => e.IsCode)
    //    .Select(e => e.CodeValue.Value).ToArray();
    //  var rtypes = operators.Select(e => e.DataType);
    //  var atypes = operators.SelectMany(e => e.Lookup.Columns.Select(c => c.DataType));
    //  var types = rtypes.Union(atypes)
    //    .Where(t => t.HasHeading)
    //    .OrderBy(t => t.GenCleanName).ToArray();
    //  ThriftGen.Process(tw, basename, types, operators);
    //}

    // Recursively add types that have headings to list
    void AddTypes(IEnumerable<DataType> types) {
      var htypes = types.Where(t => t.HasHeading);
      _datatypes.AddRange(htypes);
      foreach (var type in htypes) {
        AddTypes(type.Heading.Columns.Select(c => c.DataType));
      }
    }
  }
}
