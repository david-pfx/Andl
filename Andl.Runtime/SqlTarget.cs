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

namespace Andl.Runtime {
  /// <summary>
  /// Implement an SQL target, perhaps one of many. Acts as a common layer to the 
  /// underlying database. Tracks transaction state.
  /// 
  /// Semantics: call Open() to access particular provider and database
  /// Then SqlTarget.Current is the (static) instance, containing an instance of 
  /// ISqlDatabase specific to the provider.
  /// 
  /// Call OpenStatement() to perform a query. Calls to OpenStatement() nest, but only 
  /// one statement is active at a time. Queries cannot be concurrent.
  /// </summary>
  /// 
  public class SqlTarget {
    // The single static instance
    public static SqlTarget Current { get; set; }
    // configured to use this database
    public ISqlDatabase Database { get { return _database; } }
    // Sql generator
    public SqlGen SqlGen { get { return _sqlgen; } }
    // Sql function creator -- may be set by gateway
    public ISqlFunctionCreator FunctionCreator { get; set; }
    // true if more data to read
    public bool HasData { get { return _statement.HasData; } }

    // dictionary of known expressions subject to callback
    internal static Dictionary<int, ExpressionEval> ExprDict { get; set; }

    //--- internal
    // sql generator
    SqlGen _sqlgen;
    // currently active database
    ISqlDatabase _database;
    // transaction active if >0 (see also _database.Nesting)
    int _translevel = 0;
    // transaction aborted, not done yet
    bool _transaborted = false;

    // statement used by the current instance
    ISqlStatement _statement { get { return _statements.Peek(); } }
    // nested statements
    Stack<ISqlStatement> _statements = new Stack<ISqlStatement>();

    // functions to convert between DataType value and native object value, indexed by base type
    // Very important they round trip correctly, but up here all the objects are the right type.

    // boxed object => DataType
    public static readonly Dictionary<DataType, Func<object, DataType, TypedValue>> FromObjectDict = new Dictionary<DataType, Func<object, DataType, TypedValue>>() {
      { DataTypes.Binary, (v, dt) => BinaryValue.Create(v as byte[]) },
      { DataTypes.Bool, (v, dt) =>   BoolValue.Create((bool)v) },
      { DataTypes.Number, (v, dt) => NumberValue.Create((decimal)v) },
      { DataTypes.Row, (v, dt) =>    PersistReader.FromBinary(v as byte[], dt) },
      { DataTypes.Table, (v, dt) =>  PersistReader.FromBinary(v as byte[], dt) },
      { DataTypes.Text, (v, dt) =>   TextValue.Create(v as string) },
      { DataTypes.Time, (v, dt) =>   TimeValue.Create((DateTime)v) },
      { DataTypes.User, (v, dt) =>   PersistReader.FromBinary(v as byte[], dt) },
    };

    // DataType => boxed object
    public static readonly Dictionary<DataType, Func<TypedValue, object>> ToObjectDict = new Dictionary<DataType, Func<TypedValue, object>> {
      { DataTypes.Binary, (v) => (v as BinaryValue).Value },
      { DataTypes.Bool, (v) => (v as BoolValue).Value },
      { DataTypes.Number, (v) => (v as NumberValue).Value },
      { DataTypes.Row, (v) => PersistWriter.ToBinary(v) },
      { DataTypes.Table, (v) => PersistWriter.ToBinary(v) },
      { DataTypes.Text, (v) => (v as TextValue).Value },
      { DataTypes.Time, (v) => (v as TimeValue).Value },
      { DataTypes.User, (v) => PersistWriter.ToBinary(v) },
    };

    // Convert from Common type to DataType
    public static Dictionary<SqlCommonType, DataType> FromSqlCommon = new Dictionary<SqlCommonType, DataType> {
      //{ SqlCommonType.None, DataTypes.Unknown },
      { SqlCommonType.Binary, DataTypes.Binary },
      { SqlCommonType.Bool, DataTypes.Bool },
      { SqlCommonType.Number, DataTypes.Number },
      { SqlCommonType.Text, DataTypes.Text },
      { SqlCommonType.Time, DataTypes.Time },
    };

    // Select an SQL common type for each DataType
    public static Dictionary<DataType, SqlCommonType> ToSqlCommon = new Dictionary<DataType, SqlCommonType> {
      //{ DataTypes.Unknown, SqlCommonType.None },
      { DataTypes.Binary, SqlCommonType.Binary },
      { DataTypes.Bool, SqlCommonType.Bool },
      { DataTypes.Number, SqlCommonType.Number },
      { DataTypes.Text, SqlCommonType.Text },
      { DataTypes.Time, SqlCommonType.Time },
      { DataTypes.Row, SqlCommonType.Binary },
      { DataTypes.Table, SqlCommonType.Binary },
      { DataTypes.User, SqlCommonType.Binary },
    };

    ///------------------------------------------------------------------------
    /// High level semantics
    /// 

    // create a target instance
    public static bool Open(string name, DatabaseKinds dskind) {
      Logger.WriteLine(3, $">Sql Open {name} {dskind}");
      ExprDict = new Dictionary<int, ExpressionEval>();
      var sqleval = SqlEvaluator.Create();
      switch (dskind) {
      case DatabaseKinds.Sqlite:
        var database = Sqlite.SqliteDatabase.Create(name, sqleval);
        Current = new SqlTarget {
          _sqlgen = new SqlGen() {
            Templater = new SqliteTemplater(),
          },
          _database = database,
          FunctionCreator = database,
        };
        break;
      case DatabaseKinds.Postgres:
        Current = new SqlTarget {
          _sqlgen = new SqlGen() {
            Templater = new PostgresTemplater(),
          },
          _database = Postgres.PostgresDatabase.Create(name, sqleval),
          // FunctionCreator set later by gateway
        };
        break;
      default:
        throw Logger.Fatal(dskind);
      }
      if (!Current._database.IsOpen)
        throw Current.SqlError("database open failed");
      return true;
    }

    public static void Close() {
      if (Current != null) {
        Logger.WriteLine(2, ">Sql Close");
        Current._database.Close();
      }
    }

    // called before compiling a script
    public void BeginSession() {
      while (_statements.Count > 0) CloseStatement();
      _database.Reset();
      _translevel = 0;
      _transaborted = false;
      Begin();
    }

    // reset state, perhaps after error, with database still open
    public void EndSession(bool ok) {
      if (ok) Commit();
      else RollBack();
      while (_statements.Count > 0) CloseStatement();
      _database.Reset();
      _translevel = 0;
      _transaborted = false;
    }

    public void Begin() {
      if (_translevel++ == 0) {
        Logger.WriteLine(2, ">>>(st){0}", "BEGIN;");
        if (!_database.Begin())
          throw SqlError("begin failed");
      }
    }

    public void Commit() {
      if (_translevel == 0) return;
      if (_transaborted) RollBack();
      else if (--_translevel == 0) {
        Logger.WriteLine(2, ">>>(st){0}", "COMMIT;");
        if (!_database.Commit())
          throw SqlError("commit failed");
      }
    }

    public void RollBack() {
      if (_translevel == 0) return;
      _transaborted = true;
      if (--_translevel == 0) {
        Logger.WriteLine(2, ">>>(st){0}", "ROLLBACK;");
        if (!_database.RollBack())
          throw SqlError("rollback failed");
      }
    }

    // begin a statement, with option to update
    // check whether nested
    // if isupdate, add transaction boundary
    public void OpenStatement(bool isupdate = false) { // OBS:??
      Logger.WriteLine(3, "Open Statement {0} update:{1} level:{2}", _statements.Count+1, isupdate, _translevel);
      _statements.Push(_database.BeginStatement());
      if (_statement == null)
        throw SqlError("Open statement failed");

    }

    public void CloseStatement() {
      Logger.WriteLine(3, "Close Statement {0} aborted:{1} level:{2}", _statements.Count, _transaborted, _translevel);
      Logger.Assert(_statements.Count > 0);
      _database.EndStatement(_statement);
      _statements.Pop();
    }

    // Register a set of expressions
    // Allocate big enough accumulator block for all to use
    public bool RegisterExpressions(params ExpressionEval[] exprs) {
      var numacc = exprs.Where(e => e.HasFold).Sum(e => e.AccumCount);
      foreach (var expr in exprs)
        if (!RegisterExpression(expr, numacc))
          return false;
        return true;
    }

    // Register an expression that will be used in a query
    // An expression is the RHS of an attribute assignment (not just a function)
    // Can only be Rename, Project, Open, Aggregate: only last two needed
    public bool RegisterExpression(ExpressionEval expr, int naccum) {
      if (!(expr.IsOpen || expr.HasFold)) return true;
      if (ExprDict.ContainsKey(expr.Serial)) return true;

      var name = SqlGen.FuncName(expr);
      ExprDict[expr.Serial] = expr;
      Logger.WriteLine(3, "Register {0} naccum={1} expr='{2}'", name, naccum, expr);

      // FIX: would be better in PostgresDatabase, but does not have access to sql gen (and data types).
      // notify database, set up callbacks
      // may require sql to register (PG)
      var args = expr.Lookup.Columns.Select(c => ToSqlCommon[c.DataType.BaseType]).ToArray();
      var retn = ToSqlCommon[expr.DataType.BaseType];
      if (expr.HasFold) {
        // note: type must match low level wrappers
        var stype = DataTypes.Number;
        var init = NumberValue.Zero;
        var col0 = new DataColumn[] { DataColumn.Create("_state_", stype) };
        OptionalExpressionSql(_sqlgen.CreateFunction(name, col0.Concat(expr.Lookup.Columns).ToArray(), stype));
        OptionalExpressionSql(_sqlgen.CreateFunction(name + "F", col0, expr.DataType));
        OptionalExpressionSql(_sqlgen.CreateAggregate(name, expr.Lookup.Columns, stype, init, name, name + "F"));
        return FunctionCreator.CreateAggFunction(name, expr.Serial, naccum, args, retn);
      }
      //if (expr.IsOpen)
      OptionalExpressionSql(_sqlgen.CreateFunction(name, expr.Lookup.Columns, expr.DataType));
      return FunctionCreator.CreateFunction(name, FuncTypes.Open, expr.Serial, args, retn);
    }

    void OptionalExpressionSql(string sql) {
      if (sql.Length > 0) {
        OpenStatement();
        ExecuteCommand(sql);
        CloseStatement();
      }
    }

    // execute a command -- no return
    public void ExecuteCommand(string sqls) {
      foreach (var sql in sqls.Split('\n')) {
        Logger.WriteLine(2, ">>>(cm){0};", sql);
        if (!_statement.ExecuteCommand(sql))
          throw SqlError("command failed");
      }
    }

    // execute a query -- expect one result row
    public void ExecuteQuery(string sql) {
      Logger.WriteLine(2, ">>>(xq){0};", sql);
      if (!_statement.ExecuteQuery(sql))
        throw SqlError("query failed");
    }

    // execute a query -- expect zero or more result rows
    public void ExecuteQueryMulti(string sql) {
      Logger.WriteLine(2, ">>>(xm){0};", sql);
      if (!_statement.ExecuteQueryMulti(sql))
        throw SqlError("queries failed");
    }

    // prepare a statement for a typed set of arguments
    public void Prepare(string sql, DataType[] types) {
      var ctypes = types.Select(t => ToSqlCommon[t.BaseType]).ToArray();
      Logger.WriteLine(2, ">>>(pr){0};", sql);
      Logger.WriteLine(3, "(pr) types={0}", ctypes.Join(","));
      if (!_statement.Prepare(sql, ctypes))
        throw SqlError("prepare failed");
    }

    // execute a prepared statement with an actual set of values
    public void ExecuteSend(TypedValue[] values) {
      Logger.WriteLine(3, "Send <{0}>", values.Join(","));
      var ctypes = values.Select(v => ToSqlCommon[v.DataType.BaseType]).ToArray();
      var ovalues = values.Select(v => ToObjectDict[v.DataType.BaseType](v)).ToArray();
      if (!_statement.ExecuteSend(ctypes, ovalues))
          throw SqlError("send failed");
    }

    // if possible fetch a row from a previous query
    // sets HasData if available
    public void Fetch() {
      Logger.WriteLine(3, "Fetch");
      if (!_statement.Fetch())
        throw SqlError("fetch failed");
    }

    // get values from fetch to match a heading
    public void GetData(DataHeading heading, TypedValue[] values) {
      Logger.WriteLine(4, "GetData {0}", heading);
      var ovalues = new object[heading.Degree];
      var atypes = heading.Columns.Select(c => ToSqlCommon[c.DataType.BaseType]).ToArray();
      if (!_statement.GetValues(atypes, ovalues))
        throw SqlError("get data failed");
      for (int i = 0; i < heading.Degree; ++i) {
        var datatype = heading.Columns[i].DataType;
        values[i] = (ovalues[i] == null) ? datatype.DefaultValue()
          : FromObjectDict[datatype.BaseType](ovalues[i], datatype);
      }
    }

    // get a single int scalar from a previous query
    public void GetData(out int value) {
      Logger.WriteLine(3, "GetData int");
      var ctypes = new SqlCommonType[] { SqlCommonType.Integer };
      var fields = new object[1];
      if (!_statement.GetValues(ctypes, fields))
        throw SqlError("get int failed");
      value = (int)fields[0];
    }

    // get a single bool scalar from a previous query
    public void GetData(out bool value) {
      Logger.WriteLine(3, "GetData bool");
      value = HasData;
    }

    ///-------------------------------------------------------------------
    ///
    /// Catalog info
    /// 

    // Construct and return a heading for a database table
    // return null if not found or error
    // TODO:
    public DataHeading GetTableHeading(string table) {
      Tuple<string, SqlCommonType>[] columns;

      // optional sql 
      var sql = SqlGen.GetColumns(table);
      var optsql = sql.Length > 0;
      Logger.WriteLine(3, "GetTableHeading {0}", optsql ? sql : table);
      if (optsql) Logger.WriteLine(2, ">>>(th){0}", sql);
      Logger.Assert(_database != null, "gth");
      var ok = _database.GetTableColumns(optsql ? sql : table, out columns);
      if (!ok || columns.Length == 0) return null;
      var cols = columns.Select(c => DataColumn.Create(c.Item1, FromSqlCommon[c.Item2]));
      return DataHeading.Create(cols);    // ignore column order
    }

    SqlException SqlError(string message) {
      return new SqlException($"{message} code={_database.LastCode} message={_database.LastMessage}");
    }

  }

  //////////////////////////////////////////////////////////////////////
  /// <summary>
  /// Implement an evaluator that can be called from Sql
  /// </summary>
  public class SqlEvaluator : ISqlEvaluateSerial {
    const int _accumnul = -1;
    ISqlDatabase _database { get { return SqlTarget.Current.Database; } }
    Dictionary<int, AccumulatorBlock> _accumstore = new Dictionary<int, AccumulatorBlock>();

    public static ISqlEvaluateSerial Create() {
      return new SqlEvaluator();
    }

    // Callback function to evaluate a non-aggregate expression
    public object EvalSerialOpen(int serial, FuncTypes functype, object[] values) {
      return EvaluateCommon(SqlTarget.ExprDict[serial], functype, values, _accumnul, 0);
    }

    // Callback function to evaluate an expression
    // Note: use serial as accum handle
    public object EvalSerialAggOpen(int serial, FuncTypes functype, object[] values, int naccum) {
      return EvaluateCommon(SqlTarget.ExprDict[serial], functype, values, serial, naccum);
    }

    // Callback function to finalise an aggregation
    // No value arguments, so uses the value stored in the accumulator result field
    // TODO: handle default value
    public object EvalSerialAggFinal(int serial, FuncTypes functype) {
      var expr = SqlTarget.ExprDict[serial];
      var accblk = GetAccum(serial, 0);
      FreeAccum(serial);
      if (accblk.Result == null)
        return SqlTarget.ToObjectDict[expr.DataType.BaseType](expr.DataType.DefaultValue());
      return SqlTarget.ToObjectDict[accblk.Result.DataType.BaseType](accblk.Result);
    }

    // Callback function to evaluate an expression
    public object EvaluateCommon(ExpressionEval expr, FuncTypes functype, object[] values, int haccum, int naccum) {
      var lookup = GetLookup(expr, values);
      TypedValue retval = null;
      switch (functype) {
      case FuncTypes.Open:
        retval = expr.EvalOpen(lookup);
        break;
      case FuncTypes.Predicate:
        retval = expr.EvalPred(lookup);
        break;
      case FuncTypes.Aggregate:
      case FuncTypes.Ordered:
        var accblk = GetAccum(haccum, naccum);
        retval = expr.EvalHasFold(lookup, accblk as AccumulatorBlock);
        accblk.Result = retval;
        PutAccum(haccum, accblk);
        break;
      }
      return SqlTarget.ToObjectDict[retval.DataType.BaseType](retval);
    }

    // create a lookup for an expression from a set of values
    LookupHolder GetLookup(ExpressionEval expr, object[] ovalues) {
      // build the lookup
      var lookup = new LookupHolder();
      for (int i = 0; i < expr.NumArgs; ++i) {
        var datatype = expr.Lookup.Columns[i].DataType;
        var value = (ovalues[i] == null) ? datatype.DefaultValue()
          : SqlTarget.FromObjectDict[datatype.BaseType](ovalues[i], datatype);
        lookup.LookupDict.Add(expr.Lookup.Columns[i].Name, value);
      }
      return lookup;
    }

    // return (or possibly allocate) an accumulator block
    AccumulatorBlock GetAccum(int haccum, int naccum) {
      if (!_accumstore.ContainsKey(haccum))
        return AccumulatorBlock.Create(naccum);
      return _accumstore[haccum];
    }

    // save (and possibly allocate) an accumulator block
    void PutAccum(int haccum, AccumulatorBlock accblk) {
      _accumstore[haccum] = accblk;
    }

    // possible free an accumulator block
    void FreeAccum(int haccum) {
      _accumstore.Remove(haccum);
    }
  }

  /// <summary>
  /// stub class to hold call via delegate
  /// </summary>
  public class LookupHolder : ILookupValue {
    public Dictionary<string, TypedValue> LookupDict = new Dictionary<string, TypedValue>();
    public bool LookupValue(string name, ref TypedValue value) {
      if (!LookupDict.ContainsKey(name)) return false;
      //Logger.Assert(LookupDict.ContainsKey(name), name);
      value = LookupDict[name];
      return true;
    }
  }

  
}
