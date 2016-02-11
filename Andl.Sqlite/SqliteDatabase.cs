using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Andl.Sqlite {
  public enum FuncTypes { Open, Predicate, Aggregate, Ordered };
  /// <summary>
  /// Define an interface that can evaluate functions identified by serial
  /// </summary>
  public interface ISqlEvaluateSerial {
    // open function
    object EvalSerialOpen(int serial, FuncTypes functype, object[] values);
    // aggregating function
    object EvalSerialAggOpen(int serial, FuncTypes functype, object[] values, IntPtr accptr, int naccnum);
    // final function
    object EvalSerialAggFinal(int serial, FuncTypes functype, IntPtr accptr);
  }

  public struct LenPtrPair {
    public int Length;
    public IntPtr Pointer;
  }

  // Common types used for data interchange
  public enum SqlCommonType {
    None, Binary, Bool, Integer, Double, Number, Text, Time
  }

  //////////////////////////////////////////////////////////////////////
  ///
  /// Implement Sqlite database access
  /// 
  /// Calling convention: functions return true if they completed successfully.
  /// If false, LastError and LastMessage say why.
  /// 
  /// No 'upward' dependencies. No logging. No Andl types.
  /// Can only use general native types
  ///   Caller types accompanied by SqlCommonType
  ///   Callee types accompanied by Sqlite.Datatype
  /// 
  public class SqliteDatabase : SqliteInterop, IDisposable {
    public Result LastResult { get; set; }
    public string LastMessage { get; set; }
    public bool IsOpen { get; private set; }
    public int Nesting { get { return _nesting; } }
    
    public IntPtr _dbhandle;
    static ISqlEvaluateSerial _evaluator;
    SqliteStatement _begin;
    SqliteStatement _commit;
    SqliteStatement _abort;
    int _nesting = 0;

    // Type-aware functions for handling values (should be part of Sqlite IMHO)

    // bind values to query based on common type
    public static readonly Dictionary<SqlCommonType, Func<IntPtr, int, object, int>> PutBindDict = new Dictionary<SqlCommonType, Func<IntPtr, int, object, int>> {
      { SqlCommonType.Binary, (c, i, v) => { var bv = v as byte[]; return sqlite3_bind_blob(c, i, bv, bv.Length, SQLITE_TRANSIENT); } },
      { SqlCommonType.Bool, (c, i, v) => sqlite3_bind_int(c, i, (bool)v ? 1 : 0) },
      { SqlCommonType.Double, (c, i, v) => sqlite3_bind_double(c, i, (double)v) },
      { SqlCommonType.Integer, (c, i, v) => sqlite3_bind_int(c, i, (int)v) },
      { SqlCommonType.None, (c,i, v) => sqlite3_bind_null(c, i) },
      { SqlCommonType.Number, (c, i, v) => sqlite3_bind_text(c, i, ((decimal)v).ToString(), -1, SQLITE_TRANSIENT) },
      { SqlCommonType.Text, (c, i, v) => sqlite3_bind_text(c, i, v as string, -1, SQLITE_TRANSIENT) }, // TODO:utf
      { SqlCommonType.Time, (c, i, v) => sqlite3_bind_text(c, i, ((DateTime)v).ToString("o"), -1, SQLITE_TRANSIENT) },
    };

    // get column value from result set based on Sqlite Datatype
    public static readonly Dictionary<SqlCommonType, Func<IntPtr, int, object>> GetColumnDict = new Dictionary<SqlCommonType, Func<IntPtr, int, object>>() {
      { SqlCommonType.Binary, (s,i) => sqlite3_column_blob_wrapper(s, i) },
      { SqlCommonType.Bool, (s,i) => sqlite3_column_int(s, i) != 0 },
      { SqlCommonType.Double, (s,i) => sqlite3_column_double(s, i) },
      { SqlCommonType.Integer, (s,i) => sqlite3_column_int(s, i) },
      { SqlCommonType.None, (s,i) => null },
      { SqlCommonType.Number, (s,i) => SafeDecimalParse(sqlite3_column_text_wrapper(s, i)) },
      { SqlCommonType.Text, (s,i) => sqlite3_column_text_wrapper_utf(s, i) },
      { SqlCommonType.Time, (s,i) => SafeDatetimeParse(sqlite3_column_text_wrapper(s, i)) },
    };

    // get argument value passed to function based on common type
    public static readonly Dictionary<SqlCommonType, Func<IntPtr, object>> GetValueDict = new Dictionary<SqlCommonType, Func<IntPtr, object>>() {
      { SqlCommonType.Binary, (s) => sqlite3_value_blob_wrapper(s) },
      { SqlCommonType.Bool, (s) => sqlite3_value_int(s) != 0 },
      { SqlCommonType.None, (s) => null },
      { SqlCommonType.Number, (s) => SafeDecimalParse(sqlite3_value_text_wrapper(s)) },
      { SqlCommonType.Text, (s) => sqlite3_value_text_wrapper_utf(s) },
      { SqlCommonType.Time, (s) => SafeDatetimeParse(sqlite3_value_text_wrapper(s)) },
    };

    // set result of function based on common type
    public static readonly Dictionary<SqlCommonType, Action<IntPtr, object>> PutResultDict = new Dictionary<SqlCommonType, Action<IntPtr, object>> {
      { SqlCommonType.Binary, (c, v) => { var bv = v as byte[]; sqlite3_result_blob(c, bv, bv.Length, SQLITE_TRANSIENT); } },
      { SqlCommonType.Bool,   (c, v) => sqlite3_result_int(c, (bool)v ? 1 : 0) },
      { SqlCommonType.None,   (c, v) => sqlite3_result_null(c) },
      { SqlCommonType.Number, (c, v) => sqlite3_result_text(c, ((decimal)v).ToString(), -1, SQLITE_TRANSIENT) },
      { SqlCommonType.Text,   (c, v) => sqlite3_result_text(c, v as string, -1, SQLITE_TRANSIENT) }, // TODO: utf
      { SqlCommonType.Time,   (c, v) => sqlite3_result_text(c, ((DateTime)v).ToString("o"), -1, SQLITE_TRANSIENT) },
    };

    // Conversion see http://www.sqlite.org/datatype3.html
    // Translate unknown column type into common type
    static Tuple<string, SqlCommonType>[] _converttype = new Tuple<string, SqlCommonType>[] { 
      new Tuple<string, SqlCommonType>("INT", SqlCommonType.Number),
      new Tuple<string, SqlCommonType>("CHAR", SqlCommonType.Text),
      new Tuple<string, SqlCommonType>("CLOB", SqlCommonType.Text),
      new Tuple<string, SqlCommonType>("TEXT", SqlCommonType.Text),
      new Tuple<string, SqlCommonType>("BLOB", SqlCommonType.Binary),
      new Tuple<string, SqlCommonType>("REAL", SqlCommonType.Number),
      new Tuple<string, SqlCommonType>("FLOA", SqlCommonType.Number),
      new Tuple<string, SqlCommonType>("DOUB", SqlCommonType.Number),
      new Tuple<string, SqlCommonType>("DATE", SqlCommonType.Time),
    };

#if not_yet
    static Dictionary<SqlCommonType, string> _datatypetosql = new Dictionary<SqlCommonType, string> {
      { SqlCommonType.Binary, "BLOB" },
      { SqlCommonType.Bool, "BOOLEAN" },
      { SqlCommonType.Number, "TEXT" },
      { SqlCommonType.Text, "TEXT" },
      { SqlCommonType.Time, "TEXT" },
    };
#endif


    #region IDisposable ------------------------------------------------

    protected virtual void Dispose(bool disposing) {
      if (disposing) Close();
    }

    public void Dispose() {
      Dispose(true);
      GC.SuppressFinalize(this);
    }

    #endregion
    #region internals --------------------------------------------------

    // open database and set status -- cannot be used except to get error code
    bool Open(string path) {
      LastResult = (Result)sqlite3_open(path, out _dbhandle);
      IsOpen = (LastResult == Result.OK);
      if (IsOpen) {
        _begin = CreateStatement();
        _begin.Prepare("BEGIN TRANSACTION;");
        _commit = CreateStatement();
        _commit.Prepare("COMMIT;");
        _abort = CreateStatement();
        _abort.Prepare("ROLLBACK;");
      }
      return CheckError();
    }

    // close -- only called by Dispose prior to destruction
    void Close() {
      sqlite3_close(_dbhandle);
    }

    public bool CheckError() {
      if (LastResult == Result.OK || LastResult == Result.DONE || LastResult == Result.ROW) return true;
      LastMessage = sqlite3_errmsg(_dbhandle);
      //Console.WriteLine("Error: {0} {1}", LastResult, LastMessage);
      return false;
    }

    public bool SetError(Result result,string message) {
      LastResult = result;
      LastMessage = message;
      //Console.WriteLine("Error: {0} {1}", LastResult, LastMessage);
      return true;
    }

    #endregion
    #region publics ----------------------------------------------------

    public static SqliteDatabase Create(string path, ISqlEvaluateSerial evaluator) {
      var ret = new SqliteDatabase();
      _evaluator = evaluator;
      ret.Open(path);
      return ret;
    }

    public SqliteStatement CreateStatement() {
      return new SqliteStatement(this);
    }

    public bool Begin() {
      ++_nesting;
      return (_nesting == 1) ? _begin.Execute() : true;
    }

    public bool Commit() {
      if (_nesting > 0) --_nesting;
      return (_nesting == 0) ? _commit.Execute() : true;
    }

    public bool Abort() {
      if (_nesting > 0) --_nesting;
      return (_nesting == 0) ? _abort.Execute() : true;
    }

    public IntPtr MemAlloc(int bytes) {
      return sqlite3_malloc(bytes);
    }
    
    public IntPtr MemRealloc(IntPtr ptr, int bytes) {
      return sqlite3_realloc(ptr, bytes);
    }

    public void MemFree(IntPtr ptr) {
      sqlite3_free(ptr);
    }
    #endregion

    ///-----------------------------------------------------------------
    ///
    /// Callbacks
    /// 

    // static delegates kept alive for unmanaged code
    // Indexed by serial, so cannot grow

    static List<UserFunctionCallback> _userfuncs = new List<UserFunctionCallback>();
    static List<UserFunctionStepCallback> _stepfuncs = new List<UserFunctionStepCallback>();
    static List<UserFunctionFinalCallback> _finalfuncs = new List<UserFunctionFinalCallback>();

    // Create one function for each expression as a closure, capturing the serial and type for that function

    // Create an open or predicate function
    public bool CreateFunction(string name, FuncTypes functype, int serial, SqlCommonType[] args, SqlCommonType retn) {
      UserFunctionCallback func = (c, nv, v) => EvalFunctionOpen(c, nv, v, functype, serial, args, retn);
      _userfuncs.Add(func);
      return CreateFunction(name, func);
    }

    // Create an aggregating function
    public bool CreateAggFunction(string name, int serial, int accnum, SqlCommonType[] args, SqlCommonType retn) {
      UserFunctionStepCallback sfunc = (c, nv, v) => EvalFunctionStep(c, nv, v, accnum, serial, args, retn);
      UserFunctionFinalCallback ffunc = (c) => EvalFunctionFinal(c, serial, args, retn);
      _stepfuncs.Add(sfunc);
      _finalfuncs.Add(ffunc);
      return CreateAggFunction(name, sfunc, ffunc);
    }

    // Wrapper
    public bool CreateFunction(string name, UserFunctionCallback callback) {
      LastResult = (Result)sqlite3_create_function_scalar(_dbhandle, name, -1,
        (int)Encoding.UTF8, IntPtr.Zero, callback, IntPtr.Zero, IntPtr.Zero);
      return CheckError();
    }

    // Wrapper
    public bool CreateAggFunction(string name, UserFunctionStepCallback scallback, UserFunctionFinalCallback fcallback) {
      LastResult = (Result)sqlite3_create_function_aggregate(_dbhandle, name, -1,
        (int)Encoding.UTF8, IntPtr.Zero, IntPtr.Zero, scallback, fcallback);
      return CheckError();
    }

    // These are the callback functions called by the DBMS
    // At this stage no access to Andl internals

    // Callback for an open function
    public static void EvalFunctionOpen(IntPtr context, int nvalues, IntPtr[] values, FuncTypes functype, int serial, SqlCommonType[] args, SqlCommonType retn) {
      var svalues = GetArgs(args, values, nvalues);
      var ret = _evaluator.EvalSerialOpen(serial, functype, svalues);
      SetResult(retn, context, ret);
    }

    // Callback for an aggregating function under the fold
    public static void EvalFunctionStep(IntPtr context, int nvalues, IntPtr[] values, int accnum, int serial, SqlCommonType[] args, SqlCommonType retn) {
      var svalues = GetArgs(args, values, nvalues);

      // get an IntPtr containing a pointer to a struct with length and pointer
      // Sqlite will allocate memory first time
      var accptr = sqlite3_aggregate_context(context, Marshal.SizeOf(typeof(LenPtrPair)));
      var ret = _evaluator.EvalSerialAggOpen(serial, FuncTypes.Aggregate, svalues, accptr, accnum);
      SetResult(retn, context, ret);
    }

    // Callback for an aggregating function to finalise the result
    public static void EvalFunctionFinal(IntPtr context, int serial, SqlCommonType[] args, SqlCommonType retn) {
      var accptr = sqlite3_aggregate_context(context, 0);
      var ret = _evaluator.EvalSerialAggFinal(serial, FuncTypes.Aggregate, accptr);
      SetResult(retn, context, ret);
    }

    // set the function result using boxed value
    static void SetResult(SqlCommonType type, IntPtr context, object ret) {
      PutResultDict[type](context, ret);
    }

    // get argument values as array of boxed values
    static object[] GetArgs(SqlCommonType[] types, IntPtr[] ivalues, int ncols) {
      var svalues = new object[ncols];
      for (int x = 0; x < ncols; ++x) {
        svalues[x] = GetValueDict[types[x]](ivalues[x]);
      }
      return svalues;
    }

    ///-----------------------------------------------------------------
    /// 
    /// Catalog info
    /// 

    // get table columns, translating from create type => common type
    public bool GetTableColumns(string table, out Tuple<string, SqlCommonType>[] columns) {
      columns = null;
      var stmt = CreateStatement();
      var sql = "PRAGMA table_info(" + table + ");";
      if (!stmt.ExecuteQuery(sql)) return false;
      var cols = new List<Tuple<string, SqlCommonType>>();
      while (stmt.HasData) {
        var ctypes = new SqlCommonType[TableInfoColumnCount];
        ctypes[TableInfoNameColumn] = SqlCommonType.Text;
        ctypes[TableInfoTypeColumn] = SqlCommonType.Text;
        var fields = new object[TableInfoColumnCount];
        if (!stmt.GetValues(ctypes, fields)) return false;
        cols.Add(Tuple.Create(fields[TableInfoNameColumn] as string, 
                              ConvertToCommonType(fields[TableInfoTypeColumn] as string)));
        if (!stmt.Fetch()) return false;
      }
      stmt.Close();
      columns = cols.ToArray();
      return true;
    }

    // translate create type to common type
    public SqlCommonType ConvertToCommonType(string coltype) {
      if (coltype == "") return SqlCommonType.None;
      var ct = _converttype.FirstOrDefault(t => coltype.Contains(t.Item1));
      return ct == null ? SqlCommonType.Number : ct.Item2;
    }

    public static DateTime? SafeDatetimeParse(string s) {
      DateTime dt;
      return DateTime.TryParse(s as string, out dt) ? dt as DateTime? : null;
    }

    public static decimal? SafeDecimalParse(string s) {
      decimal dt;
      return Decimal.TryParse(s as string, out dt) ? dt as decimal? : null;
    }
  }

  //////////////////////////////////////////////////////////////////////
  /// <summary>
  /// Implement a prepared statement that tracks its own state
  /// </summary>
  public class SqliteStatement : SqliteInterop, IDisposable {
    public Result LastResult { get {return _parent.LastResult; } set {_parent.LastResult=value; } }
    public string LastMessage { get {return _parent.LastMessage; } set {_parent.LastMessage=value; } }
    public bool IsPrepared { get; private set; }
    public bool IsDone { get; private set; }
    public bool HasData { get; private set; }

    SqliteDatabase _parent { get; set; }
    IntPtr _dbhandle { get { return _parent._dbhandle; } }
    IntPtr _statement;

    bool CheckError() { return _parent.CheckError(); }
    bool SetError(Result result, string message) { return _parent.SetError(result, message); }

    public SqliteStatement(SqliteDatabase parent) {
      _parent = parent;
    }

    #region IDisposable ------------------------------------------------

    protected virtual void Dispose(bool disposing) {
      if (disposing) Close();
    }

    public void Dispose() {
      Dispose(true);
      GC.SuppressFinalize(this);
    }

    #endregion

    // close the statement -- can prepare again
    public void Close() {
      if (IsPrepared) {
        sqlite3_finalize(_statement);
        _statement = IntPtr.Zero;
      }
      IsPrepared = IsDone = HasData = false;
    }

    // prepare a statement from sql
    public bool Prepare(string sql) {
      Close();
      LastResult = (Result)sqlite3_prepare_v2(_dbhandle, sql, -1, out _statement, IntPtr.Zero);
      IsPrepared = (LastResult == Result.OK);
      return (CheckError());
    }

    // Clear ready for execute again.
    // Return false if not statement, but real error happens if it gets executed.
    public bool Reset() {
      HasData = IsDone = false;
      if (!IsPrepared) return false;
      LastResult = (Result)sqlite3_reset(_statement);
      sqlite3_clear_bindings(_statement);
      return (CheckError());
    }

    // Prepare and execute a command
    public bool ExecuteCommand(string sql) {
      if (!Prepare(sql)) return false;
      return Execute();
    }

    // Prepare and execute a query
    public bool ExecuteQuery(string sql) {
      if (!Prepare(sql)) return false;
      if (!Fetch()) return false;
      return true;
    }

    // Prepare and execute a query with binding
    //public bool ExecuteSend(string sql, string[] values) {
    //  if (!Prepare(sql)) return false;
    //  return Send(values);
    //}

    // Execute a query, return true if OK
    // It is an error if there is any data.
    // Reset so it can be executed again.
    public bool Execute() {
      if (!IsPrepared) return SetError(Result.MISUSE, "nothing to bind");
      if (IsDone) return SetError(Result.MISUSE, "already done");
      LastResult = (Result)sqlite3_step(_statement);
      if (!CheckError()) return false;
      if (LastResult != Result.DONE) return SetError(Result.MISUSE, "did not complete");
      Reset();
      return true;
    }

    // Bind values to the prepared statement
    // Execute, check no data, reset.
    public bool PutValues(SqlCommonType[] types, object[] values) {
      if (!IsPrepared) return SetError(Result.MISUSE, "nothing to bind");
      if (IsDone) return SetError(Result.MISUSE, "already done");
      for (int i = 0; i < values.Length; ++i) {
        var type = values[i].GetType();
        LastResult = (Result)SqliteDatabase.PutBindDict[types[i]](_statement, i + 1, values[i]);
        if (!CheckError()) return false;
      }
      LastResult = (Result)sqlite3_step(_statement);
      if (!CheckError()) return false;
      if (LastResult != Result.DONE) return SetError(Result.MISUSE, "did not complete");
      Reset();
      return true;
    }

    // Fetch the next row, return true if OK.
    // HasData says if data is available, IsDone if no more.
    public bool Fetch() {
      if (!IsPrepared) return SetError(Result.MISUSE, "nothing to execute");
      if (IsDone) return SetError(Result.MISUSE, "already done");
      LastResult = (Result)sqlite3_step(_statement);
      HasData = (LastResult == Result.ROW);
      IsDone = (LastResult == Result.DONE);
      return CheckError();
    }

    // Retrieve data from last query or fetch based on Type name
    public bool GetValues(SqlCommonType[] types, object[] fields) {
      if (!HasData) return false;
      var ncols = sqlite3_column_count(_statement);
      // special case to handle _dummy_
      if (fields.Length == 0 && ncols == 1) return true;
      if (fields.Length != ncols) return SetError(Result.MISUSE, "field count mismatch");
      for (var colno = 0; colno < ncols; ++colno) {
        var type = (Datatype)sqlite3_column_type(_statement, colno);
        if (type == Datatype.NULL) fields[colno] = null;
        else fields[colno] = SqliteDatabase.GetColumnDict[types[colno]](_statement, colno);
      }
      return true;
    }

  }
}
