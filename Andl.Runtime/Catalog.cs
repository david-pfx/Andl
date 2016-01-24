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

  // entry flags -- including visibility
  [Flags]
  public enum EntryFlags {
    None = 0,
    System = 1,     // system special, protected
    Public = 2,     // externally visible through gateway
    Persistent = 4, // entry persists in catalog (and maybe data too)
    Database = 8,   // link to relvar data stored in external database (local or SQL)
  };

  // Requests for entry info use these codes
  public enum EntryInfoKind {
    None, Relation, Variable, Operator, Type
  }

  // Requests for entry component info use these codes
  public enum EntrySubInfoKind {
    None, Attribute, Argument, Component
  }

  // entry kinds -- persistence is sorted in this order, to ensure it restores correctly
  public enum EntryKinds { None = 0, Type = 1, Value = 2, Code = 3 };

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
    static string _databasepattern = @"^[A-Za-z].*$";     // All with alpha first char
    static string _persistpattern = @"^[$^A-Za-z].*$";    // First char alpha, dollar, caret

    public static readonly string DefaultDatabaseExtension = ".sandl";
    public static readonly string DefaultSqlDatabaseExtension = ".sqandl";
    public static readonly string DefaultDatabaseName = "data";
    public static readonly string CatalogTableName = "andl_catalog";

    public enum CatalogTables {
      Catalog, Variable, Operator, Member
    };

    static readonly Dictionary<CatalogTables, DataHeading> _catalogtableheadings = new Dictionary<CatalogTables,DataHeading> {
      { CatalogTables.Catalog,  DataHeading.Create("Name:text", "Kind:text", "Type:text", "Value:binary") },
      { CatalogTables.Variable, DataHeading.Create("Name:text", "Type:text", "Members:text") },
      { CatalogTables.Operator, DataHeading.Create("Name:text", "Type:text", "Members:text", "Arguments:text") },
      { CatalogTables.Member,   DataHeading.Create("MemberOf:text", "Index:number", "Name:text", "Type:text", "Members:text") },
    };

    public static DataHeading CatalogTableHeading(CatalogTables table) {
      return _catalogtableheadings[table];
    }

    static readonly DataHeading _catalogkey = DataHeading.Create("Name:text");
    static readonly DataHeading _catalogtableheading = _catalogtableheadings[CatalogTables.Catalog];

    static Dictionary<CatalogTables, Func<CatalogTableMaker, IEnumerable<CatalogEntry>, CatalogTableMaker>> _catalogtablemaker =
      new Dictionary<CatalogTables, Func<CatalogTableMaker, IEnumerable<CatalogEntry>, CatalogTableMaker>> {
      { CatalogTables.Catalog, (c, e) => c.AddEntries(e) },
      { CatalogTables.Variable, (c, e) => c.AddVariables(e) },
      { CatalogTables.Operator, (c, e) => c.AddOperators(e) },
      { CatalogTables.Member, (c, e) => c.AddMembers(e) },
    };

    internal static readonly Dictionary<string, CatalogTables> _catalogtables = new Dictionary<string, CatalogTables> {
      { CatalogTableName, CatalogTables.Catalog },
      { "andl_variables", CatalogTables.Variable }, 
      { "andl_operators", CatalogTables.Operator }, 
      { "andl_members", CatalogTables.Member },  
    };
    
    static Dictionary<string, Action<Catalog, string>> _settings = new Dictionary<string, Action<Catalog, string>> {
      { "DatabaseName", (c,s) => c.DatabaseName = s },
      { "DatabasePath", (c,s) => c.DatabasePath = s },
      { "DatabaseSqlFlag", (c,s) => c.DatabaseSqlFlag = (s != null && s.ToLower() == "true") },
      { "Load", (c,s) => c.LoadFlag = Boolean.Parse(s) },
      { "Save", (c,s) => c.SaveFlag = Boolean.Parse(s) },
      { "Execute", (c,s) => c.ExecuteFlag = Boolean.Parse(s) },
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
    public string DatabaseName { get; private set; } // name of the database (defaults to same as filename)
    public string DatabasePath { get; set; }    // path to the database (either kind)
    public string SourcePath { get; set; }      // base path for reading a source

    bool _started = false;

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

    public override string ToString() {
      return String.Format("Catalog '{0}' load:{1} save:{2} pers:{3} glob:{4}",
        DatabasePath, LoadFlag, SaveFlag, PersistentVars, GlobalVars);
    }

    //--- create

    Catalog() { }
    public static Catalog Create() {
      var cat = new Catalog {
        SystemPattern = _systempattern,
        PersistPattern = _persistpattern,
        DatabasePattern = _databasepattern,
        SourcePath = "",
      };
      cat.PersistentVars = CatalogScope.Create(cat, ScopeLevels.Persistent, null);
      cat.GlobalVars = CatalogScope.Create(cat, ScopeLevels.Global, cat.PersistentVars);
      return cat;
    }

    // Configure settings. Most need to be done before Start
    public bool SetConfig(Dictionary<string,string> settings) {
      foreach (var key in settings.Keys)
        SetConfig(key, settings[key]);
      return true;
    }

    // Configure settings. Most need to be done before Start
    public bool SetConfig(string key, string value) {
      if (!_settings.ContainsKey(key)) return false;
      _settings[key](this, value);
      return true;
    }

    // open the catalog for use, after all flags set up
    // create catalog table here, local until the end
    // only do it once
    public bool Start(string databasepath = null) {
      if (_started) return false;
      Logger.WriteLine(3, "Catalog Start {0} path:'{1}'", this, databasepath);
      DatabasePath = databasepath ?? DatabasePath ?? DefaultDatabaseName;
      var ext = Path.GetExtension(DatabasePath);
      if (ext == "")
        DatabasePath = Path.ChangeExtension(DatabasePath, (DatabaseSqlFlag) ? DefaultSqlDatabaseExtension : DefaultDatabaseExtension);
      else if (ext == DefaultSqlDatabaseExtension)
        DatabaseSqlFlag = true;
      DatabaseName = Path.GetFileNameWithoutExtension(DatabasePath);
      if (DatabaseSqlFlag) {
        var sqleval = SqlEvaluator.Create();
        var database = Sqlite.SqliteDatabase.Create(DatabasePath, sqleval);
        SqlTarget.Configure(database);
      }

      var table = DataTableLocal.Create(_catalogtableheading);
      GlobalVars.Add(CatalogTableName, table.DataType, EntryKinds.Value, EntryFlags.Public | EntryFlags.System, TypedValue.Create(table));
      GlobalVars.FindEntry(CatalogTableName).Flags |= EntryFlags.Database;
      if (LoadFlag) {
        if (!LinkRelvar(CatalogTableName))
          ProgramError.Fatal("Catalog", "cannot load catalog for '{0}'", DatabaseName);
        LoadFromTable();
      }
      _started = true;
      Logger.WriteLine(3, "[CS {0}]", this);
      Logger.WriteLine(4, "[Relations: {0} Variables: {1} Operators: {2} Types: {3}]",
          PersistentVars.GetEntryInfoDict(EntryInfoKind.Relation).Count,
          PersistentVars.GetEntryInfoDict(EntryInfoKind.Variable).Count,
          PersistentVars.GetEntryInfoDict(EntryInfoKind.Operator).Count,
          PersistentVars.GetEntryInfoDict(EntryInfoKind.Type).Count);
      return true;
    }

    // All done, persist catalog if required
    public bool Finish() {
      if (!_started) return false;
      Logger.WriteLine(2, "Catalog Finish '{0}'", this);
      if (SaveFlag) {
        StoreToTable();
        Logger.WriteLine(2, "Saved catalog for '{0}'", DatabaseName);
      }
      _started = false;
      return true;
    }

    // return value for system tables
    public RelationValue GetCatalogTableValue(CatalogTables table) {
      var tablemaker = CatalogTableMaker.Create(_catalogtableheadings[table]);
      _catalogtablemaker[table](tablemaker, PersistentVars.GetEntries());
      return RelationValue.Create(tablemaker.Table);
    }

    // get the type of a relation from some persistence store
    // Used during compilation or startup -- if successful, variable will be created with flags
    // Then use AddRelvar to import the value
    public DataType GetRelvarType(string name, string source) {
      var islinked = (source == "");
      if (islinked) {
        if (DatabaseSqlFlag) {
          var heading = SqlTarget.Create().GetTableHeading(name);
          return (heading == null) ? null : DataTypeRelation.Get(heading);
        } else {
          var type = Persist.Create(DatabasePath, false).Peek(name);
          return type;
        }
      }
      var table = DataSourceStream.Create(source, SourcePath).Input(name, InputMode.Preview);
      if (table != null) return table.DataType;
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
          ProgramError.Fatal("Catalog", "sql table not found: '{0}'", name);
        var table = DataTableSql.Create(name, heading);
        entry.Value = RelationValue.Create(table);
      } else {
        var tablev = Persist.Create(DatabasePath, false).Load(name);
        if (tablev == null || !heading.Equals(tablev.Heading))
          ProgramError.Fatal("Catalog", "local table not found: '{0}'", name);
        entry.Value = RelationValue.Create(tablev.AsTable());
      }
      return true;
    }

    // Get the value of a relation by importing some other format
    // Entry previously created by peeking
    public bool ImportRelvar(string source, string name, string locator) {
      var entry = GlobalVars.FindEntry(name);
      Logger.Assert(entry != null, name);
      var heading = entry.DataType.Heading;
      var table = DataSourceStream.Create(source, locator).Input(name, InputMode.Import);
      if (table == null || !heading.Equals(table.Heading))
        ProgramError.Fatal("Catalog", "{0} table not found: '{1}'", source, name);
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
      var table = DataTableLocal.Create(_catalogtableheading);
      foreach (var entry in PersistentVars.GetEntries()) {
        var addrow = DataRow.Create(_catalogtableheading, new TypedValue[] 
          { TextValue.Create(entry.Name), 
            TextValue.Create(entry.Kind.ToString()), 
            TextValue.Create(entry.DataType.BaseType.Name), 
            BinaryValue.Create(entry.ToBinary()) });
        table.AddRow(addrow);
      }
      if (DatabaseSqlFlag)
        DataTableSql.Create(CatalogTableName, table);
      else Persist.Create(DatabasePath, true).Store(CatalogTableName, RelationValue.Create(table));
    }

    public void LoadFromTable() {
      var table = GlobalVars.FindEntry(CatalogTableName).Value.AsTable();
      foreach (var row in table.GetRows()) {
        var blob = (row.Values[3] as BinaryValue).Value;
        var entry = CatalogEntry.FromBinary(blob);
        PersistentVars.Add(entry);
        if (entry.IsDatabase) {
          if (!LinkRelvar(entry.Name))
            ProgramError.Fatal("Catalog", "cannot add '{0}'", entry.Name);
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

    public override string ToString() {
      return String.Format("CatScope {0} entries:{1}", Level, _entries.Count);
    }

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
      if (kind == EntryKinds.Value) {
        if (datatype is DataTypeRelation)
          (datatype as DataTypeRelation).ProposeCleanName(name);
        else if (datatype is DataTypeTuple)
          (datatype as DataTypeTuple).ProposeCleanName(name);
      }
    }


    // set a variable to a new value of the same type
    // NOTE: if level is global, needs concurrency control
    internal void Set(string name, TypedValue value) {
      _entries[name].Set(value);
    }

    // Return raw value from variable
    internal CatalogEntry FindEntry(string name) {
      CatalogEntry entry;
      return (_entries.TryGetValue(name, out entry)) ? entry
        : Parent != null ? Parent.FindEntry(name)
        : null;
    }

    // Return raw value from variable
    // special handling of system catalog names
    internal TypedValue GetValue(string name) {
      if (Catalog.IsSystem(name)) {
        if (Catalog._catalogtables.ContainsKey(name))
          return Catalog.GetCatalogTableValue(Catalog._catalogtables[name]);
        return null;
      }
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
      if (Catalog.IsSystem(name)) {
        ProgramError.Error("Catalog", "cannot set '{0}'", name);
        return;
      }
      var entry = FindEntry(name);
      if (entry == null && Level == ScopeLevels.Local) {
        Add(name, value.DataType, EntryKinds.Value);
        entry = FindEntry(name);
      }
      Logger.Assert(entry != null);
      entry.Set(value);

      // if relation value, convert to/from Sql
      // database flag means linked entry, value in database
      if (entry.IsDatabase && Catalog.SaveFlag) {
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

    // Provide formatted dictionary of strings for external tools
    // Searches current scope, not parent
    public Dictionary<string, string> GetEntryInfoDict(EntryInfoKind kind) {
      switch (kind) {
      case EntryInfoKind.Relation:
        return GetEntryValuesDict(ce => ce.IsDatabase, ce => ce.RelationToStringValue());
      case EntryInfoKind.Variable:
        return GetEntryValuesDict(ce => ce.IsVariable, ce => ce.ValueToStringValue());
      case EntryInfoKind.Operator:
        return GetEntryValuesDict(ce => ce.IsCode, ce => ce.CodeToStringValue());
      case EntryInfoKind.Type:
        return GetEntryValuesDict(ce => ce.IsType, ce => ce.TypeToStringValue());
      default:
        break;
      }
      return new Dictionary<string, string>();
    }

    Dictionary<string, string> GetEntryValuesDict(Func<CatalogEntry, bool> selector, Func<CatalogEntry, string> formatter) {
      return GetEntries().Where(e => selector(e)).ToDictionary(e => e.Name, e => formatter(e));
    }

    public Dictionary<string, string> GetSubEntryInfoDict(EntrySubInfoKind kind, string name) {
      if (_entries.ContainsKey(name)) {
        var entry = _entries[name];
        switch (kind) {
        case EntrySubInfoKind.Attribute:
          if (!entry.IsDatabase) break;
          return GetSubEntryValuesDict(entry.DataType.Heading);
        case EntrySubInfoKind.Argument:
          if (!entry.IsCode) break;
          return GetSubEntryValuesDict(entry.CodeValue.Value.Lookup);
        case EntrySubInfoKind.Component:
          if (!entry.IsType) break;
          return GetSubEntryValuesDict(entry.DataType.Heading);
        default:
          break;
        }
      }
      return new Dictionary<string, string>();
    }

    Dictionary<string, string> GetSubEntryValuesDict(DataHeading heading) {
      return heading.Columns.ToDictionary(c => c.Name, c => c.DataType.BaseName);
    }

    //public Dictionary<string, string> GetRelationsDict() {
    //  return GetEntryValuesDict(ce => ce.IsDatabase, ce => ce.RelationToStringValue());
    //}
    //public Dictionary<string, string> GetOperatorsDict() {
    //  return GetEntryValuesDict(ce => ce.IsCode, ce => ce.CodeToStringValue());
    //}
    //public Dictionary<string, string> GetVariablesDict() {
    //  return GetEntryValuesDict(ce => ce.IsVariable, ce => ce.ValueToStringValue());
    //}
    //public Dictionary<string, string> GetTypesDict() {
    //  return GetEntryValuesDict(ce => ce.IsType, ce => ce.TypeToStringValue());
    //}
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
    public bool IsType { get { return Kind == EntryKinds.Type; } }
    public bool IsValue { get { return Kind == EntryKinds.Value; } }
    public bool IsDatabase { get { return Flags.HasFlag(EntryFlags.Database); } }
    public bool IsVariable { get { return IsValue && !IsDatabase; } }

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

    // return entry value formatted to suit display
    public string RelationToStringValue() {
      Logger.Assert(Kind == EntryKinds.Value && DataType.HasHeading, Kind);
      return String.Format("{0} [{1}]", DataType.BaseName, DataType.Heading.Degree);
    }
    public string CodeToStringValue() {
      Logger.Assert(Kind == EntryKinds.Code, Kind);
      return String.Format("{0} [{1}]", CodeValue.Value.DataType.BaseName, CodeValue.Value.NumArgs);
    }
    public string TypeToStringValue() {
      Logger.Assert(Kind == EntryKinds.Type, Kind);
      return String.Format("[{0}]", DataType.Heading.Degree);
    }
    public string ValueToStringValue() {
      Logger.Assert(Kind == EntryKinds.Value, Kind);
      return String.Format("{0} = {1}", DataType.BaseName, Value);
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

    // Fill a table of variables
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
            TextValue.Create(datatype.GetUniqueName ?? "") });
      Table.AddRow(addrow);
    }

    // Fill a table of operators
    public CatalogTableMaker AddOperators(IEnumerable<CatalogEntry> entries) {
      foreach (var entry in entries.Where(e => e.IsCode)) {
        AddOperator(entry.Name, entry.CodeValue.DataType, entry.CodeValue.Value);
      }
      return this;
    }

    void AddOperator(string name, DataType datatype, ExpressionBlock value) {
      var addrow = DataRow.Create(Table.Heading, new TypedValue[]
          { TextValue.Create(name),
            TextValue.Create(value.DataType.BaseType.Name),
            TextValue.Create(value.DataType.GetUniqueName ?? ""),
            TextValue.Create(value.NumArgs == 0 ? "" : value.SubtypeName) }); // suppress empty arg list
      Table.AddRow(addrow);
    }

    // Fill a table of members
    public CatalogTableMaker AddMembers(IEnumerable<CatalogEntry> entries) {
      foreach (var entry in entries.Where(e => e.DataType.GetUniqueName != null)) {
        AddMember(entry.DataType.GetUniqueName, entry.DataType.Heading);
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
            TextValue.Create(column.DataType.GetUniqueName ?? "") });
        Table.AddRow(addrow);
        // Recursive call. note: may be duplicate, but no matter.
        if (column.DataType.GetUniqueName != null)
          AddMember(column.DataType.GetUniqueName, column.DataType.Heading);
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
