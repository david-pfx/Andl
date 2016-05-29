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
using System.Text;
using static System.Math;

namespace Andl.Postgres {
  class OpenState {
    internal bool IsOpen;
  }

  //////////////////////////////////////////////////////////////////////
  ///
  /// Implement Postgres database access
  /// 
  /// Calling convention: functions return true if they completed successfully.
  /// If false, LastError and LastMessage say why.
  /// 
  /// No 'upward' dependencies. No logging. No Andl types.
  /// Can only use general native types
  ///   Caller types accompanied by SqlCommonType
  ///   Callee types accompanied by Postgres Datum types
  /// 
  public class PostgresDatabase : PostgresInterop, ISqlDatabase, IDisposable {
    public string LastCode { get { return _lastresult.ToString(); } }
    public string LastMessage { get { return _lastmessage; } }
    public bool IsOpen { get; private set; }
    bool IsConnected { get { return Nesting > 0 && _connectstack.Peek().IsOpen; } }
    public int Nesting { get { return _connectstack.Count; } }
    public ISqlEvaluateSerial Evaluator { get { return _evaluator; } }

    internal SpiReturn _lastresult;
    internal string _lastmessage;
    ISqlEvaluateSerial _evaluator;
    PostgresConnect _dbhandle;
    //ISqlStatement _begin;
    //ISqlStatement _commit;
    //ISqlStatement _abort;
    // track SPI nesting
    Stack<OpenState> _connectstack = new Stack<OpenState>();

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

    // close -- also called by Dispose prior to destruction
    public void Close() {
      if (IsOpen) Reset();
      IsOpen = false;
    }

    // return true if last result was OK
    internal bool CheckOk() {
      if (_lastresult >= SpiReturn.OK) return true;
      return false;
    }

    internal bool SetError(SpiReturn result, string message) {
      _lastresult = result;
      _lastmessage = message;
      return true;
    }

    #endregion
    #region publics ----------------------------------------------------

    public static PostgresDatabase Create(string name, ISqlEvaluateSerial evaluator) {
      var pgd = new PostgresDatabase {
        _evaluator = evaluator
      };
      pgd.Open(null);
      return pgd;
    }

    // open database and set status -- first time only
    bool Open(PostgresConnect dbhandle) {
      _dbhandle = dbhandle;
      _connectstack.Push(new OpenState());
      SetError(SpiReturn.OK, "");
      IsOpen = CheckOk();
      return CheckOk();
    }

    // called to reset to start condition
    // note: disconnect if connected
    public void Reset() {
      EnsureDisconnect();
      _connectstack.Clear();
      _connectstack.Push(new OpenState());
      SetError(SpiReturn.OK, "");
    }

    // called on begin and end of (possibly nested) function call
    public void BeginEntry() {
      _connectstack.Push(new OpenState());
      // defer connect until or unless needed
      SetError(SpiReturn.OK, "");
    }

    public void EndEntry() {
      EnsureDisconnect();
      _connectstack.Pop();
      if (Nesting == 0) _connectstack.Push(new OpenState());
      SetError(SpiReturn.OK, "");
    }

    public ISqlStatement BeginStatement() {
      EnsureConnect();
      return new PostgresStatement(this);
    }

    public bool EndStatement(ISqlStatement statement) {
      if (statement != null) statement.Close(); // CHECK:
      return true;
    }

    // FIX:Cannot use any transactions or save points -- why?
    // see src/backend/access/transam/README
    // Needs to be reimplemented using 
    // BeginInternalSubTransaction(), ReleaseCurrentSubTransaction() and
    // RollbackAndReleaseCurrentSubTransaction() to be able to handle exceptions.

    public bool Begin() {
      var stmt = BeginStatement();
      if (!CheckOk()) return false;
      //if (!stmt.ExecuteCommand("SAVEPOINT xyz")) return false;
      //if (!stmt.ExecuteCommand("BEGIN TRANSACTION")) return false;
      return EndStatement(stmt);
    }

    public bool Commit() {
      var stmt = BeginStatement();
      if (!CheckOk()) return false;
      //if (!stmt.ExecuteCommand("RELEASE SAVEPOINT s")) return false;
      //if (!stmt.ExecuteCommand("COMMIT")) return false;
      return EndStatement(stmt);
    }

    public bool RollBack() {
      var stmt = BeginStatement();
      if (!CheckOk()) return false;
      //if (!stmt.ExecuteCommand("ROLLBACK TO SAVEPOINT s")) return false;
      //if (!stmt.ExecuteCommand("ROLLBACK")) return false;
      return EndStatement(stmt);
    }

    // common code
    bool EnsureConnect() {
      SetError(SpiReturn.OK, "");
      if (!IsConnected) {
        SetError((SpiReturn)pg_spi_connect(), "connect");
        _connectstack.Peek().IsOpen = true;
      }
      return CheckOk();
    }

    bool EnsureDisconnect() {
      SetError(SpiReturn.OK, "");
      if (IsConnected) {
        SetError((SpiReturn)pg_spi_finish(), "finish");
        _connectstack.Peek().IsOpen = false;
      }
      return CheckOk();
    }
    #endregion

    ///-----------------------------------------------------------------
    /// 
    /// Catalog info
    /// 

    // get table columns, translating from create type => common type
    // suitable sql is generated by caller (not nice!)
    public bool GetTableColumns(string sql, out Tuple<string, SqlCommonType>[] columns) {
      columns = null;
      var ftypes = new SqlCommonType[] { SqlCommonType.Text, SqlCommonType.Text, };
      var cols = new List<Tuple<string, SqlCommonType>>();

      var stmt = BeginStatement();
      if (!CheckOk()) return false;
      if (!stmt.ExecuteQueryMulti(sql)) return false;
      while (stmt.HasData) {
        var fields = new object[2];
        if (!stmt.GetValues(ftypes, fields)) return false;
        cols.Add(Tuple.Create(fields[0] as string, ConvertToCommonType(fields[1] as string)));
        stmt.Fetch();
      }
      columns = cols.ToArray();
      return EndStatement(stmt);
    }

    // These are object IDs used internally by PG, with conversion to common type
    static Dictionary<TypeOid, SqlCommonType> _oidtocommondict = new Dictionary<TypeOid, SqlCommonType> {
      { TypeOid.BOOLOID, SqlCommonType.Bool },
      { TypeOid.BYTEAOID, SqlCommonType.Binary },
      { TypeOid.INT4OID, SqlCommonType.Integer },
      { TypeOid.TEXTOID, SqlCommonType.Text },
      { TypeOid.FLOAT8OID, SqlCommonType.Double },
      { TypeOid.NUMERICOID, SqlCommonType.Number },
      { TypeOid.TIMESTAMPOID, SqlCommonType.Time },
    };

    internal int[] CommonToOid(SqlCommonType[] common) {
      return common.Select(i => CommonToOid(i)).ToArray();
    }
    internal int CommonToOid(SqlCommonType common) {
      return (int)_oidtocommondict.FirstOrDefault(kv => kv.Value == common).Key;
    }
    internal SqlCommonType[] OidToCommon(int[] oid) {
      return oid.Select(i => OidToCommon(i)).ToArray();
    }
    internal SqlCommonType OidToCommon(int oid) {
      return _oidtocommondict.ContainsKey((TypeOid)oid) ? _oidtocommondict[(TypeOid)oid] : SqlCommonType.None;
    }

    Dictionary<string, SqlCommonType> _texttocommondict = new Dictionary<string, SqlCommonType> {
      { "BYTEA", SqlCommonType.Binary },
      { "BOOLEAN", SqlCommonType.Bool },
      { "NUMERIC", SqlCommonType.Number },
      { "TEXT", SqlCommonType.Text },
      { "TIMESTAMP", SqlCommonType.Time },
    };

    internal SqlCommonType ConvertToCommonType(string text) {
      var t = text.Substring(0, Min(9, text.Length)).ToUpper();
      return _texttocommondict.ContainsKey(t) ? _texttocommondict[t] : SqlCommonType.None;
    }

  }

  //////////////////////////////////////////////////////////////////////
  /// <summary>
  /// Implement a prepared statement that tracks its own state
  /// </summary>
  public class PostgresStatement : PostgresInterop, ISqlStatement, IDisposable {
    public SpiReturn _lastresult { get { return _parent._lastresult; } set { _parent._lastresult = value; } }
    public string _lastmessage { get { return _parent._lastmessage; } set { _parent._lastmessage = value; } }
    public bool IsPrepared { get; private set; }
    public bool IsDone { get; private set; }
    public bool HasData { get; private set; }
    public bool HasCursor { get; private set; }

    // use error fields in parent
    PostgresDatabase _parent { get; set; }
    bool CheckOk() { return _parent.CheckOk(); }
    bool SetError(SpiReturn result, string message) { return _parent.SetError(result, message); }
    // prepared statement
    IntPtr _plan;
    // open cursor
    IntPtr _cursor;

    public PostgresStatement(PostgresDatabase parent) {
      _parent = parent;
      //SetError((SpiReturn)pg_spi_connect(), "connect");
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
      _plan = IntPtr.Zero;
      IsPrepared = IsDone = HasData = false;
      SetError(SpiReturn.OK, "");
    }

    // Directly a command (no results)
    public bool ExecuteCommand(string sql) {
      Close();
      SetError((SpiReturn)pg_spi_execute(sql, false), $"execute {sql}");
      return CheckOk();
    }

    // Directly execute a query (one result)
    public bool ExecuteQuery(string sql) {
      Close();
      // Note: read_only set false so preceding changes will be visible
      SetError((SpiReturn)pg_spi_execute(sql, false), $"execute readonly {sql}");
      HasData = CheckOk(); // TODO: would be nice to check there is data
      return CheckOk();
    }

    // Directly execute a query (multiple results)
    public bool ExecuteQueryMulti(string sql) {
      Close();
      // Note: read_only set false so preceding changes will be visible
      SetError((SpiReturn)pg_spi_cursor_execute(sql, false, out _cursor), $"execute multi {sql}");
      if (CheckOk()) Fetch(); // Find out if any data, fetch first row
      return CheckOk();
    }

    // prepare a statement from sql, that can be used with parameters
    public bool Prepare(string sql, SqlCommonType[] argtypes = null) {
      Close();
      var atyp = argtypes ?? new SqlCommonType[0];
      var oidtypes = atyp.Select(t => (int)_parent.CommonToOid(t)).ToArray();
      SetError((SpiReturn)pg_spi_prepare_cursor(sql, oidtypes.Length, oidtypes, 0, out _plan), $"prepare {sql}");
      IsPrepared = CheckOk();
      return CheckOk();
    }

    // Execute a simple prepared statement
    public bool Execute() {
      if (!IsPrepared) return SetError(SpiReturn.MISUSE, "not prepared");
      SetError((SpiReturn)pg_spi_execute_plan(_plan, 0, new IntPtr[0], false), "execute plan");
      return CheckOk();
    }

    // Bind values to the prepared statement - keep until used
    public bool ExecuteSend(SqlCommonType[] argtypes, object[] argvalues) {
      if (!IsPrepared) return SetError(SpiReturn.MISUSE, "not prepared");
      var values = Datum.ConvertFrom(argtypes, argvalues);
      SetError((SpiReturn)pg_spi_execute_plan(_plan, values.Length, values, false), "execute plan args");
      return true;
    }

    // Fetch the next row from a cursor.
    // HasData says if data is available.
    public bool Fetch() {
      if (_cursor == null) return SetError(SpiReturn.MISUSE, "no cursor");
      if (IsDone) return SetError(SpiReturn.MISUSE, "already done");
      SetError((SpiReturn)pg_spi_cursor_fetch(_cursor), "fetch");
      HasData = (_lastresult == SpiReturn.SPI_OK_FETCH);
      IsDone |= !HasData;
      return CheckOk();
    }

    // Retrieve data from last query or fetch based on Type name
    public bool GetValues(SqlCommonType[] types, object[] fields) {
      if (!HasData) return false;
      // TODO: special case to handle _dummy_???
      for (var colno = 0; colno < types.Length; ++colno) {
        IntPtr datum;
        SetError((SpiReturn)pg_spi_getdatum(0, colno + 1, out datum), $"get datum {colno}");
        if (!CheckOk()) return false;
        fields[colno] = Datum.ConvertTo(types[colno], datum);
      }
      return true;
    }
  }

}
