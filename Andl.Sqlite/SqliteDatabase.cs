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
  /// // Can only use general types (no Andl yet)
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

  //////////////////////////////////////////////////////////////////////
  ///
  /// Implement Sqlite database access
  /// 
  /// Calling convention: functions return true if they completed successfully.
  /// If false, LastError and LastMessage say why.
  /// 
  /// No 'upward' dependencies. No logging.
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

    // bind values to query
    public static readonly Dictionary<Type, Func<IntPtr, int, object, int>> PutBindDict = new Dictionary<Type, Func<IntPtr, int, object, int>> {
      { typeof(bool), (c, i, v) => sqlite3_bind_int(c, i, (bool)v ? 1 : 0) },
      { typeof(byte[]), (c, i, v) => { var bv = v as byte[]; return sqlite3_bind_blob(c, i, bv, bv.Length, SQLITE_TRANSIENT); } },
      { typeof(DateTime), (c, i, v) => sqlite3_bind_text(c, i, v as string, -1, SQLITE_TRANSIENT) },
      { typeof(decimal), (c, i, v) => sqlite3_bind_double(c, i, (double)(decimal)v) },
      { typeof(int), (c, i, v) => sqlite3_bind_int(c, i, (int)v) },
      { typeof(string), (c, i, v) => sqlite3_bind_text(c, i, v as string, -1, SQLITE_TRANSIENT) },
    };

    // get column value from result set
    public static readonly Dictionary<Datatype, Func<IntPtr, int, object>> GetColumnDict = new Dictionary<Datatype, Func<IntPtr, int, object>>() {
      { Datatype.BLOB, (s,i) => sqlite3_column_blob_wrapper(s, i) },
      { Datatype.FLOAT, (s,i) => sqlite3_column_double(s, i) },
      { Datatype.INTEGER, (s,i) => sqlite3_column_double(s, i) },
      //{ Datatype.INTEGER, (s,i) => sqlite3_column_int(s, i) },
      { Datatype.NULL, (s,i) => null },
      { Datatype.TEXT, (s,i) => sqlite3_column_text_wrapper(s, i) },
    };

    // get column value from result set
    public static readonly Dictionary<string, Func<IntPtr, int, object>> GetColumnDict2 = new Dictionary<string, Func<IntPtr, int, object>>() {
      { "NONE", (s,i) => sqlite3_column_blob_wrapper(s, i) },
      { "REAL", (s,i) => sqlite3_column_double(s, i) },
      { "TEXT", (s,i) => sqlite3_column_text_wrapper(s, i) },
      { "TIME", (s,i) => sqlite3_column_text_wrapper(s, i) },
    };

    // get argument value passed to function
    public static readonly Dictionary<Datatype, Func<IntPtr, object>> GetValueDict = new Dictionary<Datatype, Func<IntPtr, object>>() {
      { Datatype.BLOB, (s) => sqlite3_value_blob_wrapper(s) },
      { Datatype.FLOAT, (s) => sqlite3_value_double(s) },
      { Datatype.INTEGER, (s) => sqlite3_value_double(s) },
      //{ Datatype.INTEGER, (s) => sqlite3_value_int(s) },
      { Datatype.NULL, (s) => null },
      { Datatype.TEXT, (s) => sqlite3_value_text_wrapper(s) },
    };

    // set result of function
    public static readonly Dictionary<Type, Action<IntPtr, object>> PutResultDict = new Dictionary<Type, Action<IntPtr, object>> {
      { typeof(bool), (c, v) => sqlite3_result_int(c, (bool)v ? 1 : 0) },
      { typeof(byte[]), (c, v) => { var bv = v as byte[]; sqlite3_result_blob(c, bv, bv.Length, SQLITE_TRANSIENT); } },
      { typeof(DateTime), (c, v) => sqlite3_result_text(c, v as string, -1, SQLITE_TRANSIENT) },
      { typeof(decimal), (c, v) => sqlite3_result_double(c, (double)(decimal)v) },
      { typeof(int), (c, v) => sqlite3_result_int(c, (int)v) },
      { typeof(string), (c, v) => sqlite3_result_text(c, v as string, -1, SQLITE_TRANSIENT) },
    };

    // Conversion see http://www.sqlite.org/datatype3.html
    static string[,] _converttype = new string[,] {
      { "INT", "INTEGER"},
      { "CHAR", "TEXT"},
      { "CLOB", "TEXT"},
      { "TEXT", "TEXT"},
      { "BLOB", "NONE"},
      { "REAL", "REAL"},
      { "FLOA", "REAL"},
      { "DOUB", "REAL"},
      { "DATE", "TIME"}, // unsafe?
    };

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

    public bool CreateFunction(string name, UserFunctionCallback callback) {
      LastResult = (Result)sqlite3_create_function_scalar(_dbhandle, name, -1,
        (int)Encoding.UTF8, IntPtr.Zero, callback, IntPtr.Zero, IntPtr.Zero);
      return CheckError();
    }

    public bool CreateAggFunction(string name, UserFunctionStepCallback scallback, UserFunctionFinalCallback fcallback) {
      LastResult = (Result)sqlite3_create_function_aggregate(_dbhandle, name, -1,
        (int)Encoding.UTF8, IntPtr.Zero, IntPtr.Zero, scallback, fcallback);
      return CheckError();
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
    public bool CreateFunction(string name, FuncTypes functype, int serial) {
      UserFunctionCallback func = (c, nv, v) => EvalFunctionOpen(c, nv, v, functype, serial);
      _userfuncs.Add(func);
      return CreateFunction(name, func);
    }

    // Create an aggregating function
    public bool CreateAggFunction(string name, int serial, int accnum) {
      UserFunctionStepCallback sfunc = (c, nv, v) => EvalFunctionStep(c, nv, v, accnum, serial);
      UserFunctionFinalCallback ffunc = (c) => EvalFunctionFinal(c, serial);
      _stepfuncs.Add(sfunc);
      _finalfuncs.Add(ffunc);
      return CreateAggFunction(name, sfunc, ffunc);
    }

    // These are the callback functions called by the DBMS
    // At this stage no access to Andl internals

    // Callback for an open function
    public static void EvalFunctionOpen(IntPtr context, int nvalues, IntPtr[] values, FuncTypes functype, int serial) {
      var svalues = GetArgs(values, nvalues);
      var ret = _evaluator.EvalSerialOpen(serial, functype, svalues);
      SetResult(context, ret);
    }

    // Callback for an aggregating function under the fold
    public static void EvalFunctionStep(IntPtr context, int nvalues, IntPtr[] values, int accnum, int serial) {
      var svalues = GetArgs(values, nvalues);

      // get an IntPtr containing a pointer to a struct with length and pointer
      // Sqlite will allocate memory first time
      var accptr = sqlite3_aggregate_context(context, Marshal.SizeOf(typeof(LenPtrPair)));
      var ret = _evaluator.EvalSerialAggOpen(serial, FuncTypes.Aggregate, svalues, accptr, accnum);
      SetResult(context, ret);
    }

    // Callback for an aggregating function to finalise the result
    public static void EvalFunctionFinal(IntPtr context, int serial) {
      var accptr = sqlite3_aggregate_context(context, 0);
      var ret = _evaluator.EvalSerialAggFinal(serial, FuncTypes.Aggregate, accptr);
      SetResult(context, ret);
    }

    // set the function result using boxed value
    static void SetResult(IntPtr context, object ret) {
      var t = ret.GetType();
      PutResultDict[t](context, ret);
    }

    // get argument values as array of boxed values
    static object[] GetArgs(IntPtr[] ivalues, int ncols) {
      var svalues = new object[ncols];
      for (int x = 0; x < ncols; ++x) {
        var type = (Datatype)sqlite3_value_type(ivalues[x]);
        svalues[x] = GetValueDict[type](ivalues[x]);
      }
      return svalues;
    }

    ///-----------------------------------------------------------------
    /// 
    /// Catalog info
    /// 

    // get table columns, translating from create type => affinity
    public bool GetTableColumns(string table, out Tuple<string, string>[] columns) {
      columns = null;
      var stmt = CreateStatement();
      var sql = "PRAGMA table_info(" + table + ");";
      if (!stmt.ExecuteQuery(sql)) return false;
      var cols = new List<Tuple<string, string>>();
      while (stmt.HasData) {
        var fields = new object[TableInfoColumnCount];
        if (!stmt.GetData(fields)) return false;
        cols.Add(Tuple.Create(fields[TableInfoNameColumn] as string, 
                              ConvertToDatatype(fields[TableInfoTypeColumn] as string)));
        if (!stmt.Fetch()) return false;
      }
      stmt.Close();
      columns = cols.ToArray();
      return true;
    }

    // translate create type to affinity
    // https://www.sqlite.org/datatype3.html s2.1
    public string ConvertToDatatype(string coltype) {
      if (coltype == "") return "NONE";
      for (var i = 0; i < _converttype.Length / 2; ++i)
        if (coltype.Contains(_converttype[i,0])) 
          return _converttype[i,1];
      return "NUMERIC";
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
    public bool ExecuteSend(string sql, string[] values) {
      if (!Prepare(sql)) return false;
      return Send(values);
    }

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
    public bool Send(object[] values) {
      if (!IsPrepared) return SetError(Result.MISUSE, "nothing to bind");
      if (IsDone) return SetError(Result.MISUSE, "already done");
      for (int i = 0; i < values.Length; ++i) {
        var type = values[i].GetType();
        LastResult = (Result)SqliteDatabase.PutBindDict[type](_statement, i + 1, values[i]);
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

    // Retrieve data from last query or fetch
    public bool GetData(object[] fields) {
      if (!HasData) return false;
      var ncols = sqlite3_column_count(_statement);
      // special case to handle _dummy_
      if (fields.Length == 0 && ncols == 1) return true;
      if (fields.Length != ncols) return SetError(Result.MISUSE, "field count mismatch");
      for (var i = 0; i < ncols; ++i) {
        var type = (Datatype)sqlite3_column_type(_statement, i);
        fields[i] = SqliteDatabase.GetColumnDict[type](_statement, i);
      }
      return true;
    }

    // Retrieve data from last query or fetch
    public bool GetData(string[] types, object[] fields) {
      if (!HasData) return false;
      var ncols = sqlite3_column_count(_statement);
      // special case to handle _dummy_
      if (fields.Length == 0 && ncols == 1) return true;
      if (fields.Length != ncols) return SetError(Result.MISUSE, "field count mismatch");
      for (var colno = 0; colno < ncols; ++colno) {
        var type = (Datatype)sqlite3_column_type(_statement, colno);
        if (type == Datatype.NULL) fields[colno] = null;
        else fields[colno] = ConvertFieldData(types[colno], colno);
        //fields[i] = SqliteDatabase.GetColumnDict2[types[i]](_statement, i);
      }
      return true;
    }

    object ConvertFieldData(string type, int colno) {
      var ret = SqliteDatabase.GetColumnDict2[type](_statement, colno);
      if (type == "TIME" && ret != null) {
        DateTime dt;
        return DateTime.TryParse(ret as string, out dt) ? (object)dt : null;
      }
      return ret;
    }

  }
}
