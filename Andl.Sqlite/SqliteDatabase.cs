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
using System.Linq;
using System.Runtime.InteropServices;
using Andl.Common;
using Andl.Sql;

namespace Andl.Sqlite {

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
  public class SqliteDatabase : SqliteInterop, ISqlDatabase, ISqlFunctionCreator, IDisposable {
    public string LastCode { get { return _lastresult.ToString(); } }
    public string LastMessage { get { return _lastmessage; } }
    public bool IsOpen { get; private set; }
    public int Nesting { get { return _nesting; } } // OBS:??
    public ISqlEvaluateSerial Evaluator { get { return _evaluator; } }

    internal Result _lastresult;
    internal string _lastmessage;
    internal IntPtr _dbhandle = IntPtr.Zero;
    static ISqlEvaluateSerial _evaluator;
    ISqlStatement _begin;
    ISqlStatement _commit;
    ISqlStatement _rollback;
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
      { SqlCommonType.Text, (c, i, v) => sqlite3_bind_text16(c, i, v as string, -1, SQLITE_TRANSIENT) },
      { SqlCommonType.Time, (c, i, v) => sqlite3_bind_text(c, i, ((DateTime)v).ToString("o"), -1, SQLITE_TRANSIENT) },
    };

    // get column value from result set based on Sqlite Datatype
    public static readonly Dictionary<SqlCommonType, Func<IntPtr, int, object>> GetColumnDict = new Dictionary<SqlCommonType, Func<IntPtr, int, object>>() {
      { SqlCommonType.Binary, (s,i) => sqlite3_column_blob_wrapper(s, i) },
      { SqlCommonType.Bool, (s,i) => sqlite3_column_int(s, i) != 0 },
      { SqlCommonType.Double, (s,i) => sqlite3_column_double(s, i) },
      { SqlCommonType.Integer, (s,i) => sqlite3_column_int(s, i) },
      { SqlCommonType.None, (s,i) => null },
      { SqlCommonType.Number, (s,i) => sqlite3_column_text_wrapper(s, i).SafeDecimalParse() },
      { SqlCommonType.Text, (s,i) => sqlite3_column_text16_wrapper(s, i) },
      { SqlCommonType.Time, (s,i) => sqlite3_column_text_wrapper(s, i).SafeDatetimeParse() },
    };

    // get argument value passed to function based on common type
    public static readonly Dictionary<SqlCommonType, Func<IntPtr, object>> GetValueDict = new Dictionary<SqlCommonType, Func<IntPtr, object>>() {
      { SqlCommonType.Binary, (s) => sqlite3_value_blob_wrapper(s) },
      { SqlCommonType.Bool, (s) => sqlite3_value_int(s) != 0 },
      { SqlCommonType.None, (s) => null },
      { SqlCommonType.Number, (s) => sqlite3_value_text_wrapper(s).SafeDecimalParse() },
      { SqlCommonType.Text, (s) => sqlite3_value_text16_wrapper(s) },
      { SqlCommonType.Time, (s) => sqlite3_value_text_wrapper(s).SafeDatetimeParse() },
    };

    // set result of function based on common type
    public static readonly Dictionary<SqlCommonType, Action<IntPtr, object>> PutResultDict = new Dictionary<SqlCommonType, Action<IntPtr, object>> {
      { SqlCommonType.Binary, (c, v) => { var bv = v as byte[]; sqlite3_result_blob(c, bv, bv.Length, SQLITE_TRANSIENT); } },
      { SqlCommonType.Bool,   (c, v) => sqlite3_result_int(c, (bool)v ? 1 : 0) },
      { SqlCommonType.None,   (c, v) => sqlite3_result_null(c) },
      { SqlCommonType.Number, (c, v) => sqlite3_result_text(c, ((decimal)v).ToString(), -1, SQLITE_TRANSIENT) },
      { SqlCommonType.Text,   (c, v) => sqlite3_result_text16(c, v as string, -1, SQLITE_TRANSIENT) },
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
      _lastresult = (Result)sqlite3_open(path, out _dbhandle);
      IsOpen = (_lastresult == Result.OK);
      if (IsOpen) {
        _begin = BeginStatement();
        _begin.Prepare("BEGIN TRANSACTION;");
        _commit = BeginStatement();
        _commit.Prepare("COMMIT;");
        _rollback = BeginStatement();
        _rollback.Prepare("ROLLBACK;");
      }
      return CheckOk();
    }

    // called to clear error condition
    public void Reset() {
      _nesting = 0;
      SetError(Result.OK, "");
    }

    // close -- also called by Dispose prior to destruction
    public void Close() {
      if (IsOpen) {
        Reset();
        _begin.Close();
        _commit.Close();
        _rollback.Close();
        sqlite3_close(_dbhandle);
      }
      IsOpen = false;
    }

    // Check whether operation code indicates success, return true if so
    public bool CheckOk() {
      if (_lastresult == Result.OK || _lastresult == Result.DONE || _lastresult == Result.ROW) return true;
      _lastmessage = sqlite3_errmsg_wrapper(_dbhandle);
      //Console.WriteLine("Error: {0} {1}", LastResult, LastMessage);
      return false;
    }

    public bool SetError(Result result,string message) {
      _lastresult = result;
      _lastmessage = message;
      return false;
    }

    #endregion
    #region publics ----------------------------------------------------

    public static SqliteDatabase Create(string path, ISqlEvaluateSerial evaluator) {
      var ret = new SqliteDatabase();
      _evaluator = evaluator;
      ret.Open(path);
      return ret;
    }

    public ISqlStatement BeginStatement() {
      return new SqliteStatement(this);
    }

    public bool EndStatement(ISqlStatement statement) {
      if (statement != null) statement.Close();
      return true;
    }

    public bool Begin() {
      return _begin.Execute();
    }

    public bool Commit() {
      return _commit.Execute();
    }

    public bool RollBack() {
      return _rollback.Execute();
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
      _lastresult = (Result)sqlite3_create_function_scalar(_dbhandle, name, -1,
        (int)Encoding.UTF8, IntPtr.Zero, callback, IntPtr.Zero, IntPtr.Zero);
      return CheckOk();
    }

    // Wrapper
    public bool CreateAggFunction(string name, UserFunctionStepCallback scallback, UserFunctionFinalCallback fcallback) {
      _lastresult = (Result)sqlite3_create_function_aggregate(_dbhandle, name, -1,
        (int)Encoding.UTF8, IntPtr.Zero, IntPtr.Zero, scallback, fcallback);
      return CheckOk();
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

      // No need for context -- using serial as accum handle
      var ret = _evaluator.EvalSerialAggOpen(serial, FuncTypes.Aggregate, svalues, accnum);
      SetResult(retn, context, ret);
    }

    // Callback for an aggregating function to finalise the result
    public static void EvalFunctionFinal(IntPtr context, int serial, SqlCommonType[] args, SqlCommonType retn) {
      var ret = _evaluator.EvalSerialAggFinal(serial, FuncTypes.Aggregate);
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
      var stmt = BeginStatement();
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

  }

  //////////////////////////////////////////////////////////////////////
  /// <summary>
  /// Implement a prepared statement that tracks its own state
  /// </summary>
  public class SqliteStatement : SqliteInterop, ISqlStatement, IDisposable {
    public Result _lastresult { get {return _parent._lastresult; } set {_parent._lastresult=value; } }
    public string _lastmessage { get {return _parent._lastmessage; } set {_parent._lastmessage=value; } }
    public bool IsPrepared { get; private set; }
    public bool HasData { get; private set; }

    SqliteDatabase _parent { get; set; }
    IntPtr _dbhandle { get { return _parent._dbhandle; } }
    IntPtr _statement;
    bool _isdone;

    bool CheckOk() { return _parent.CheckOk(); }
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
      IsPrepared = _isdone = HasData = false;
    }

    // prepare a statement from sql
    // Sqlite does not use types
    public bool Prepare(string sql, SqlCommonType[] types = null) {
      Close();
      _lastresult = (Result)sqlite3_prepare_v2(_dbhandle, sql, -1, out _statement, IntPtr.Zero);
      IsPrepared = (_lastresult == Result.OK);
      return (CheckOk());
    }

    // Clear ready for execute again.
    // Return false if not statement, but real error happens if it gets executed.
    bool Reset() {
      HasData = _isdone = false;
      if (!IsPrepared) return false;
      _lastresult = (Result)sqlite3_reset(_statement);
      sqlite3_clear_bindings(_statement);
      return (CheckOk());
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

    // Prepare and execute a query for multiple rows
    public bool ExecuteQueryMulti(string sql) {
      return ExecuteQuery(sql);
    }

    // Execute a query, return true if OK
    // It is an error if there is any data.
    // Reset so it can be executed again.
    public bool Execute() {
      if (!IsPrepared) return SetError(Result.MISUSE, "not prepared");
      if (_isdone) return SetError(Result.MISUSE, "already done");
      _lastresult = (Result)sqlite3_step(_statement);
      if (!CheckOk()) return false;
      if (_lastresult != Result.DONE) return SetError(Result.MISUSE, "did not complete");
      Reset();
      return true;
    }

    // Bind values to the prepared statement
    // Execute, check no data, reset.
    public bool ExecuteSend(SqlCommonType[] types, object[] values) {
      if (!IsPrepared) return SetError(Result.MISUSE, "not prepared");
      if (_isdone) return SetError(Result.MISUSE, "already done");
      for (int i = 0; i < values.Length; ++i) {
        var type = values[i].GetType();
        _lastresult = (Result)SqliteDatabase.PutBindDict[types[i]](_statement, i + 1, values[i]);
        if (!CheckOk()) return false;
      }
      _lastresult = (Result)sqlite3_step(_statement);
      if (!CheckOk()) return false;
      if (_lastresult != Result.DONE) return SetError(Result.MISUSE, "did not complete");
      Reset();
      return true;
    }

    // Fetch the next row, return true if OK.
    // HasData says if data is available, IsDone if no more.
    public bool Fetch() {
      if (!IsPrepared) return SetError(Result.MISUSE, "nothing to execute");
      if (_isdone) return SetError(Result.MISUSE, "already done");
      _lastresult = (Result)sqlite3_step(_statement);
      HasData = (_lastresult == Result.ROW);
      _isdone = (_lastresult == Result.DONE);
      return CheckOk();
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
