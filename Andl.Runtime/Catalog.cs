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
using Andl.Sql;
using Andl.Common;

namespace Andl.Runtime {
  // scope levels, can be compared in this order
  public enum ScopeLevels { Persistent = 0, Global = 1, Local = 2 };
  // session state
  public enum SessionState {
    None, Connect, Full, Ended
  }
  // session result
  public enum SessionResults {
    Ok, Failed
  }

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

  enum CatalogStatus { None, Started, Connected, Cataloguing, Finished };

  /// <summary>
  /// Permit controlled access to catalog variables and scopes
  /// </summary>
  public interface ICatalogVariables {
    Catalog Catalog { get; }
    // create new scope level
    ICatalogVariables PushScope();
    // return to previous scope level
    ICatalogVariables PopScope();
    // Set a value for a variable in the current scope ie assignment
    void SetValue(string name, TypedValue value);
    // Return type of entry
    EntryKinds GetKind(string name);
    // Return raw value of variable 
    TypedValue GetValue(string name);
    // Return raw type of variable 
    DataType GetDataType(string name);
  }

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
    public static readonly string DefaultDatabaseName = "db";
    public static readonly string CatalogTableName = "andl_catalog";
    public static readonly DatabaseKinds DefaultDatabaseKind = DatabaseKinds.Memory;
    public static readonly DatabaseKinds DefaultDatabaseSqlServer = DatabaseKinds.Sqlite;

    public enum CatalogTables {
      Catalog, Variable, Operator, Member
    };

    static readonly Dictionary<CatalogTables, DataHeading> _catalogtableheadings = new Dictionary<CatalogTables, DataHeading> {
      { CatalogTables.Catalog,  DataHeading.Create("Name:text", "Kind:text", "Type:text", "Value:binary") },
      { CatalogTables.Variable, DataHeading.Create("Name:text", "Type:text", "Members:text") },
      { CatalogTables.Operator, DataHeading.Create("Name:text", "Type:text", "Members:text", "Arguments:text") },
      { CatalogTables.Member,   DataHeading.Create("MemberOf:text", "Index:number", "Name:text", "Type:text", "Members:text") },
    };

    public static DataHeading CatalogTableHeading(CatalogTables table) {
      return _catalogtableheadings[table];
    }

    static readonly DataHeading _catalogkey = DataHeading.Create("Name:text", "Kind:text", "Type:text");
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

    static Dictionary<string, Action<Catalog, string>> _setconfigdict = new Dictionary<string, Action<Catalog, string>> {
      { "DatabaseName", (c,s) => c.DatabaseName = s },
      { "DatabasePath", (c,s) => c.DatabasePath = s },
      { "DatabaseKind", (c,s) => c.DatabaseKind = (DatabaseKinds)Enum.Parse(typeof(DatabaseKinds), s) },
      { "Sql", (c,s) => c.SqlFlag = s.SafeBoolParse() ?? false },
      { "Load", (c,s) => c.LoadFlag = s.SafeBoolParse() ?? false },
      { "Save", (c,s) => c.SaveFlag = s.SafeBoolParse() ?? false },
      { "Execute", (c,s) => c.ExecuteFlag = s.SafeBoolParse() ?? false },
      { "Noisy", (c,s) => Logger.Level = s.SafeIntParse() ?? 1 },
    };

    // configuration settings
    public bool InteractiveFlag { get; set; }   // execute during compilation
    public bool ExecuteFlag { get; set; }       // execute after compilation
    public bool LoadFlag { get; set; }          // load an existing catalog (else create new)
    public bool SaveFlag { get; set; }          // save the updated the catalog after compilation
    public bool SqlFlag { get; set; }           // use sql as the database
    public DatabaseKinds DatabaseKind { get; set; }   // kind of database

    public string SystemPattern { get; set; }   // variables protected from update
    public string PersistPattern { get; set; }  // variables that are persisted
    public string DatabasePattern { get; set; } // relvars kept in the (SQL) database

    public string BaseName { get; set; }        // base name for application
    public string DatabaseName { get; private set; } // name of the database (defaults to same as filename)
    public string DatabasePath { get; set; }    // path to the database (either kind)
    public string SourcePath { get; set; }      // base path for reading a source

    CatalogStatus _status = CatalogStatus.None;
    SessionState _sessionstate = SessionState.None;

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
      return String.Format("{0} {1} load:{2} save:{3} [{4}] [{5}]",
        DatabasePath, _status, LoadFlag, SaveFlag, PersistentVars, GlobalVars);
    }

    //-------------------------------------------------------------------------
    // create the catalog
    // then set some options
    // then Start
    // then Restart

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
    public bool SetConfig(Dictionary<string, string> settings) {
      foreach (var key in settings.Keys)
        if (!SetConfig(key, settings[key]))
          Logger.WriteLine(1, $"Invalid setting ignored: '{key}'");
      return true;
    }

    // Configure settings. Most need to be done before Start
    bool SetConfig(string key, string value) {
      if (!_setconfigdict.ContainsKey(key)) return false;
      _setconfigdict[key](this, value);
      return true;
    }

    // open the catalog for use and set database name
    // defer connect until begin session
    public void Start(string path = null) {
      if (_status > CatalogStatus.None) return;  // just the once
      Logger.WriteLine(2, $"Catalog Start path:'{path}'");

      DatabasePath = path ?? DatabasePath ?? DefaultDatabaseName;
      _status = CatalogStatus.Started;
      // if already known to be Sql get connected, else defer until reading directive or begin session
      if (SqlFlag)
        ConnectDatabase();
      Logger.WriteLine(3, "[CS {0}]", this);
      return;
    }

    // Make connection to database based on available flags and current status
    // does not return on error
    void ConnectDatabase() {
      if (_status > CatalogStatus.Started) return;  // just the once
      Logger.Assert(_status == CatalogStatus.Started, _status);
      Logger.WriteLine(2, $"Catalog Connect database {this}");

      // create empty catalog
      var table = DataTableLocal.Create(_catalogtableheading);
      GlobalVars.AddEntry(CatalogTableName, table.DataType, EntryKinds.Value,
        EntryFlags.Public | EntryFlags.System, TypedValue.Create(table));
      GlobalVars.FindEntry(CatalogTableName).Flags |= EntryFlags.Database;

      // Sql or not? Open it.
      var ext = Path.GetExtension(DatabasePath);
      if (ext == "")
        DatabasePath = Path.ChangeExtension(DatabasePath, (SqlFlag) ? DefaultSqlDatabaseExtension : DefaultDatabaseExtension);
      SqlFlag |= (ext == DefaultSqlDatabaseExtension || DatabaseKind != DatabaseKinds.Memory);
      DatabaseName = Path.GetFileNameWithoutExtension(DatabasePath);
      if (SqlFlag) {
        if (DatabaseKind == DatabaseKinds.Memory) DatabaseKind = DatabaseKinds.Sqlite;
        Logger.WriteLine(3, "Catalog database={0} kind={1}", DatabasePath, DatabaseKind);
        if (!SqlTarget.Open(DatabasePath, DatabaseKind))
          throw ProgramError.Fatal("Catalog", "Cannot open database: {0} ({1})", DatabasePath, DatabaseKind);
      } else {
        if (LoadFlag && !Directory.Exists(DatabasePath))
          throw ProgramError.Fatal("Catalog", "Database does not exist: {0}", DatabasePath);
      }
      _status = CatalogStatus.Connected;
      Logger.WriteLine(3, "[CC {0}]", this);
    }

    // Final step is to load or create catalog in connected database
    void EnableCatalog() {
      if (_status > CatalogStatus.Connected) return;  // just the once
      Logger.WriteLine(3, $"Catalog Enable {this}");
      Logger.Assert(_status == CatalogStatus.Connected, _status);
      // load or create catalog (but must start session first)
      if (LoadFlag) {
        if (!LinkRelvar(CatalogTableName))
          throw ProgramError.Fatal("Catalog", "cannot load catalog for '{0}'", DatabaseName);
        LoadFromTable();
        _status = CatalogStatus.Cataloguing;
      } else if (SaveFlag) {
        StoreToTable();  // create empty catalog
        _status = CatalogStatus.Cataloguing;
      }
    }

    // Called by the #catalog directive
    public void Directive() {
      if (_status > CatalogStatus.Connected)
        throw ProgramError.Fatal("Catalog", "invalid catalog options");
      //Connect(true);
    }

    // All done, persist catalog if required
    public bool Finish() {
      Logger.Assert(_status != CatalogStatus.Finished);
      if (_sessionstate > SessionState.None) EndSession(SessionResults.Ok);
      Logger.WriteLine(2, "Catalog Finish '{0}'", this);
      SqlTarget.Close();
      _status = CatalogStatus.Finished;
      return true;
    }

    // Called before carrying out a request (perhaps more than once)
    // Sql will begin a transaction
    public void BeginSession(SessionState starts) {
      if (_sessionstate >= SessionState.Full) return;
      Logger.WriteLine(3, $"Begin session {starts}");
      ConnectDatabase();
      if (SqlFlag)
        SqlTarget.Current.BeginSession();
      if (starts == SessionState.Full) EnableCatalog();
      _sessionstate = starts;
    }

    // Called after processing a script, ok=normal termination
    // Local will save if update true
    // Sql will commit if update true, else abort
    public void EndSession(SessionResults result) {
      if (_sessionstate == SessionState.None) return;
      if (_sessionstate == SessionState.Ended) return;
      Logger.WriteLine(3, $"End session {result}");
      if (SqlFlag)
        SqlTarget.Current.EndSession(result == SessionResults.Ok && SaveFlag);
      else if (result == SessionResults.Ok && SaveFlag)
        StoreToTable();
      _sessionstate = SessionState.Ended;
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
    public DataType GetRelvarType(string source, string what) {
      var islinked = (source == "");
      if (islinked) {
        if (SqlFlag) {
          var heading = SqlTarget.Current.GetTableHeading(what);
          return (heading == null) ? null : DataTypeRelation.Get(heading);
        } else {
          var type = Persist.Create(DatabasePath, false).Peek(what);
          return type;
        }
      } else {
        var heading = DataSourceStream.Create(source, SourcePath).Peek(what);
        if (heading != null) return DataTypeRelation.Get(heading);
      }
      return null;
    }

    // Get the value of a relation from a database
    // Entry previously created by peeking
    public bool LinkRelvar(string name) {
      var entry = GlobalVars.FindEntry(name);
      Logger.Assert(entry != null && entry.IsDatabase);

      var heading = entry.DataType.Heading;
      if (SqlFlag) {
        var sqlheading = SqlTarget.Current.GetTableHeading(name);
        if (sqlheading == null)
          throw ProgramError.Fatal("Catalog", "sql table not found: '{0}'", name);
        // TODO: smarter test, but still may not match exactly
        //if (!heading.Equals(sqlheading))
        if (heading.Degree != sqlheading.Degree)
          throw ProgramError.Fatal("Catalog", "sql table schema mismatch: '{0}'", name);
        var table = DataTableSql.Create(name, heading);
        GlobalVars.SetValue(entry, RelationValue.Create(table));
      } else {
        var tablev = Persist.Create(DatabasePath, false).Load(name);
        if (tablev == null)
          throw ProgramError.Fatal("Catalog", "local table not found: '{0}'", name);
        if (!heading.Equals(tablev.Heading))
          throw ProgramError.Fatal("Catalog", "local table schema mismatch: '{0}'", name);
        GlobalVars.SetValue(entry, RelationValue.Create(tablev.AsTable()));
      }
      return true;
    }

    // Get the value of a relation by importing some other format
    // Entry previously created by peeking
    public bool ImportRelvar(string source, string name, string what, string locator) {
      var entry = GlobalVars.FindEntry(name);
      Logger.Assert(entry != null, name);
      var heading = entry.DataType.Heading;
      var stream = DataSourceStream.Create(source, locator);
      var table = stream.Read(what, heading);
      if (table == null || !heading.Equals(table.Heading))
        throw ProgramError.Fatal("Catalog", "{0} table not found: '{1}'", source, stream.GetPath(what));
      GlobalVars.SetValue(entry, RelationValue.Create(table));
      return true;
    }

    // Add a user type, just so it will get persisted
    public void AddUserType(string name, DataTypeUser datatype, EntryFlags flags) {
      GlobalVars.AddEntry(name, datatype, EntryKinds.Type, flags);
    }

    //--- persistence Mk II

    // Store the persistent catalog and modified tables, local only
    // note: for Sql, only used to create new empty catalog
    public void StoreToTable() {
      Logger.WriteLine(2, "Save catalog for '{0}'", DatabaseName);
      var ctm = CatalogTableMaker.Create(_catalogtableheading);
      var table = ctm.AddEntries(PersistentVars.GetEntries()).Table;
      if (SqlFlag)
        DataTableSql.Create(CatalogTableName, table);
      else {
        Persist.Create(DatabasePath, true).Store(CatalogTableName, RelationValue.Create(table));
        var savers = PersistentVars.GetEntries().Where(e => e.IsUnsaved);
        Logger.WriteLine(2, $"Persist {savers.Count()} entries");
        foreach (var entry in savers)
          Persist.Create(DatabasePath, true).Store(entry.Name, entry.Value);
      }
    }

    public void LoadFromTable() {
      Logger.WriteLine(2, "Load catalog for '{0}'", DatabaseName);
      var centry = GlobalVars.FindEntry(CatalogTableName);
      var table = GlobalVars.GetValue(centry).AsTable();
      foreach (var row in table.GetRows()) {
        var blob = (row.Values[3] as BinaryValue).Value;
        var entry = CatalogEntry.FromBinary(blob);
        PersistentVars.Add(entry);
        if (entry.IsDatabase) {
          if (!LinkRelvar(entry.Name))
            throw ProgramError.Fatal("Catalog", "cannot add '{0}'", entry.Name);
        }
      }
    }

    // Store new entry to catalog, perhaps removing old value first
    // Currently only for Sql -- Local does total dump
    internal void StoreEntry(CatalogEntry entry, CatalogEntry oldentry = null) {
      var ctm = CatalogTableMaker.Create(_catalogtableheading);
      if (SqlFlag) {
        var table = DataTableSql.Create(CatalogTableName, _catalogtableheading);
        if (oldentry != null) {
          var ctmx = CatalogTableMaker.Create(_catalogtableheading);
          ctmx.Table.AddRow(ctmx.MakeEntry(oldentry));
          table.UpdateJoin(ctmx.Table, JoinOps.MINUS);
        }
        ctm.Table.AddRow(ctm.MakeEntry(entry));
        table.UpdateJoin(ctm.Table, JoinOps.UNION);
      }
    }
  }

  ///===========================================================================
  /// <summary>
  /// Implement a single catalog scope level containing variables
  /// </summary>
  public class CatalogScope : ICatalogVariables {
    public Catalog Catalog { get { return _catalog; } }
    Catalog _catalog;
    CatalogScope _parent;
    ScopeLevels _level;
    Dictionary<string, CatalogEntry> _entries = new Dictionary<string, CatalogEntry>();

    public override string ToString() {
      return String.Format("{0} entries:{1}", _level, _entries.Count);
    }

    public static CatalogScope Create(Catalog catalog, ScopeLevels level, CatalogScope parent = null) {
      return new CatalogScope {
        _catalog = catalog,
        _level = level,
        _parent = parent,
      };
    }

    public IEnumerable<CatalogEntry> GetEntries() {
      return _entries.Values;
    }

    #region ICatalogPrivate
    public ICatalogVariables PushScope() {
      return Create(_catalog, ScopeLevels.Local, this);
    }

    public ICatalogVariables PopScope() {
      return _parent;
    }

    public EntryKinds GetKind(string name) {
      var entry = FindEntry(name);
      return (entry == null) ? EntryKinds.None : entry.Kind;
    }

    public DataType GetDataType(string name) {
      return GetEntryType(name);
    }
    #endregion


    // Add a new entry to the catalog.
    // Do kind, flags and other stuff here
    public void AddNewEntry(string name, DataType datatype, EntryKinds kind, EntryFlags flags) {
      if (_catalog.IsPersist(name)) flags |= EntryFlags.Persistent;
      if (kind == EntryKinds.Value && datatype is DataTypeRelation && _catalog.IsDatabase(name))
        flags |= EntryFlags.Database;
      AddEntry(name, datatype, kind, flags, null);   // preliminary entry, value can only be set by code
      //AddEntry(name, datatype, kind, flags, datatype.DefaultValue());   // make sure it has something, to avoid later errrors
      if (kind == EntryKinds.Value) {
        if (datatype is DataTypeRelation)
          (datatype as DataTypeRelation).ProposeCleanName(name);
        else if (datatype is DataTypeTuple)
          (datatype as DataTypeTuple).ProposeCleanName(name);
      }
    }

    // Return type for an entry that is settable
    public DataType GetReturnType(string name) {
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

    // Return raw value from variable
    // special handling of system catalog names
    public TypedValue GetValue(string name) {
      if (_catalog.IsSystem(name)) {
        if (Catalog._catalogtables.ContainsKey(name))
          return _catalog.GetCatalogTableValue(Catalog._catalogtables[name]);
        return null;
      }
      var entry = FindEntry(name);
      return (entry == null) ? null : GetValue(entry);
    }

    // Get a catalog value by entry from database or catalog
    public TypedValue GetValue(CatalogEntry entry) {
      if (entry.IsDatabase) {
        if (_catalog.SqlFlag) {

          // Database sql comes from external table
          var table = DataTableSql.Create(entry.Name, entry.DataType.Heading);
          return RelationValue.Create(table);
        } else if (!entry.IsLoaded) { // lazy load

          // Database non-sql lazy loaded from store on path, then in catalog
          var value = Persist.Create(_catalog.DatabasePath, true).Load(entry.Name);
          if (entry.DataType != value.DataType)
            throw ProgramError.Fatal("Catalog", "Type mismatch for variable {0}", entry.Name);
          entry.IsLoaded = true;
          return Persist.Create(_catalog.DatabasePath, true).Load(entry.Name);
        }
      }
      // Non-database exists only in the catalog
      return entry.Value;
    }

    // Value replaces existing, type should be compatible
    // Supports assignment. Handles linked tables.
    public void SetValue(string name, TypedValue value) {
      if (_catalog.IsSystem(name))
        throw ProgramError.Fatal("Catalog", "cannot set '{0}'", name);
      var entry = FindEntry(name);
      if (entry == null && _level == ScopeLevels.Local) {
        AddEntry(name, value.DataType, EntryKinds.Value);
        entry = FindEntry(name);
      }
      Logger.Assert(entry != null);
      SetValue(entry, value);
    }

    //-------------------------------------------------------------------------
    // implementation

    // Return raw value from variable
    internal CatalogEntry FindEntry(string name) {
      CatalogEntry entry;
      return (_entries.TryGetValue(name, out entry)) ? entry
        : _parent != null ? _parent.FindEntry(name)
        : null;
    }

    // Return type from variable as evaluated if needed
    DataType GetEntryType(string name) {
      var entry = FindEntry(name);
      return entry == null ? null : entry.DataType;
    }

    // Add a named entry
    internal void Add(CatalogEntry entry) {
      _entries[entry.Name] = entry;
    }

    // Add new catalog entry to proper scope
    // Risky: caller may not know where it went; maybe merge Global & Persistent?
    internal void AddEntry(string name, DataType datatype, EntryKinds kind, EntryFlags flags = EntryFlags.None, TypedValue value = null) {
      var scope = (flags.HasFlag(EntryFlags.Persistent)) ? _catalog.PersistentVars : this;
      var entry = new CatalogEntry {
        Name = name,
        DataType = datatype,
        Kind = kind,
        Flags = flags,
        Scope = scope,
        Value = value,
      };
      scope.Add(entry);
      // update catalog
      if (flags.HasFlag(EntryFlags.Persistent) && _catalog.SaveFlag) {
        Logger.Assert(entry.Kind == EntryKinds.Type || entry.Value == null, entry);
        if (entry.Kind == EntryKinds.Type)
          _catalog.StoreEntry(entry);
      }
    }

    // set entry to value, update as needed
    internal void SetValue(CatalogEntry entry, TypedValue value) {
      // Choose where to store and whether to convert
      TypedValue finalvalue;

      if (entry.IsDatabase && _catalog.SqlFlag) {

        // Database + sql => hand it to sql, to create table if needed
        // set a default value to carry the type
        DataTableSql.Create(entry.Name, value.AsTable());
        finalvalue = value.DataType.DefaultValue();
      } else {

        // everything else stored in catalog
        if (value.DataType is DataTypeRelation)
          finalvalue = RelationValue.Create(DataTableLocal.Convert(value.AsTable()));
        else finalvalue = value;
        // set flags for persisting
        if (entry.IsPersistent) {
          entry.IsUnsaved = true;
          entry.IsLoaded = true;
        }
      }

      // store value if changed
      if (finalvalue != entry.Value) {
        var oldvalue = (entry.Value == null) ? null : entry;
        entry.Set(finalvalue);
        if (entry.IsPersistent && _catalog.SaveFlag)
          _catalog.StoreEntry(entry, oldvalue);
      }
    }

    ///------------------------------------------------------------------------
    /// Functions to provide formatted dictionary of strings for external tools
    /// 

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
    public bool IsUnsaved { get; set; }
    public bool IsLoaded { get; set; }

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
        Table.AddRow(MakeEntry(entry));
      }
      return this;
    }

    public DataRow MakeEntry(CatalogEntry entry) {
      return DataRow.Create(Table.Heading, new TypedValue[] {
        TextValue.Create(entry.Name),
        TextValue.Create(entry.Kind.ToString()),
        TextValue.Create(entry.DataType.BaseType.Name),
        BinaryValue.Create(entry.ToBinary())
      });
    }

    // Fill a table of variables
    public CatalogTableMaker AddVariables(IEnumerable<CatalogEntry> entries) {
      foreach (var entry in entries.Where(e => !e.IsCode)) {
        Table.AddRow(MakeVariable(entry.Name, entry.DataType));
      }
      return this;
    }

    public DataRow MakeVariable(string name, DataType datatype) {
      return DataRow.Create(Table.Heading, new TypedValue[]
          { TextValue.Create(name),
            TextValue.Create(datatype.BaseType.Name),
            TextValue.Create(datatype.GetUniqueName ?? "") });
    }

    // Fill a table of operators
    public CatalogTableMaker AddOperators(IEnumerable<CatalogEntry> entries) {
      foreach (var entry in entries.Where(e => e.IsCode)) {
        Table.AddRow(MakeOperator(entry.Name, entry.CodeValue.DataType, entry.CodeValue.Value));
      }
      return this;
    }

    DataRow MakeOperator(string name, DataType datatype, ExpressionBlock value) {
      return DataRow.Create(Table.Heading, new TypedValue[]
          { TextValue.Create(name),
            TextValue.Create(value.DataType.BaseType.Name),
            TextValue.Create(value.DataType.GetUniqueName ?? ""),
            TextValue.Create(value.NumArgs == 0 ? "" : value.SubtypeName) }); // suppress empty arg list
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
