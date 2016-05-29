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
using System.Text;
using System.Runtime.InteropServices;
using System.Diagnostics;
using Andl.Common;
using Andl.Sql;

namespace Andl.Postgres {
  public class PostgresException : Exception {
    public PostgresException(string msg) : base(msg) { }
  }

  ///==============================================================================================
  /// <summary>
  /// Implement a connection to Postgres and to Gateway
  /// 
  /// Provide entry points for invoking functions.
  /// Manage function call type checking and invocation
  /// </summary>
  public class PostgresConnect : PostgresInterop, ISqlPgFunction, ISqlFunctionCreator {
    string _error_message = null;
    IExecuteGateway _gateway;
    PostgresDatabase _database;
    string _sourcename = "plandl";

    //-------------------------------------------------------------------------
    // Create a new instance of PG connection and interop
    public static PostgresConnect Create(IExecuteGateway gateway, PostgresDatabase database) {
      var pgd = new PostgresConnect() {
        _gateway = gateway,
        _database = database,
      };
      // the initial functions; more will be added, to mirror the catalog
      pgd.AddFunctions();
      return pgd;
    }

    ///========================================================================
    ///
    /// Function Handling 
    /// 

    // Add known functions for type checking and execution
    void AddFunctions() {
      AddWrapper("plandl_compile", 
        new SqlCommonType[] { SqlCommonType.Text, SqlCommonType.Text }, // program text, source file name
        SqlCommonType.Text, DoCompile);
      // remainder are toy functions used for testing
      AddWrapper("addint", new SqlCommonType[] { SqlCommonType.Integer, SqlCommonType.Integer }, SqlCommonType.Integer, DoAddInt);
      AddWrapper("foo", new SqlCommonType[] { SqlCommonType.Integer, SqlCommonType.Bool, SqlCommonType.Binary,
        SqlCommonType.Number, SqlCommonType.Text, SqlCommonType.Time }, SqlCommonType.Text, DoFoo);
      AddWrapper("query", new SqlCommonType[] { SqlCommonType.Text }, SqlCommonType.Text, DoQuery);
    }

    // initial entry -- compile and execute a chunk of code, which should update the catalog
    object DoCompile(object[] args) {
      if (args.Length >= 2) _sourcename = args[1] as string;
      var res = _gateway.RunScript(args[0] as string, ExecModes.Raw, _sourcename);
      if (!res.Ok) throw new AndlException(res.Message);
      return (res.Value as string).Replace("\r", "");
    }

    // Toy functions
    object DoFoo(object[] args) {
      return String.Format("Func={0} nargs={1} arg=<{2}>", "foo", args.Length, string.Join(", ", args));
    }

    object DoAddInt(object[] args) {
      return (int)(args[0]) + (int)(args[1]);
    }

    object DoQuery(object[] args) {
      if (CheckError(pg_spi_connect(), "connect")) return Result.Failure(_error_message);
      IntPtr plan;
      if (CheckError(pg_spi_prepare_cursor("select sum(status)from s", 0, new int[0], 0, out plan), "prepare"))
        return Result.Failure(_error_message);
      if (CheckError(pg_spi_execute_plan(plan, 0, new IntPtr[0], false), "execute")) return Result.Failure(_error_message);
      IntPtr ret;
      if (CheckError(pg_spi_getdatum(0, 1, out ret), "getdatum")) return Result.Failure(_error_message);
      if (CheckError(pg_spi_finish(), "finish")) return Result.Failure(_error_message);
      return String.Format("Query ret={0}", ret.ToInt32());
    }

    bool CheckError(int errcode, string message) {
      if (errcode >= 0) return false;
      _error_message = string.Format("Error {0} <{1}>", errcode, message);
      return true;
    }

    #region ISqlPgFunction  ----------------------------------------------------

    // entry points defined by ISqlPgFunction  

    //-------------------------------------------------------------------------
    // Entry point: check that function exists and arguments and return value (as OIDs) are correct
    public bool TypeCheck(string funcname, int[] argoids, int retoid) {
      if (!_typecheckdict.ContainsKey(funcname)) throw new PostgresException($"unknown function: {funcname}");
      return _typecheckdict[funcname](argoids, retoid);
    }

    //-------------------------------------------------------------------------
    // Entry point: call a function with arguments and return value (as Datums)
    // assume already type checked
    // inform database of possible nested call
    public bool Invoke(string funcname, IntPtr[] argvalues, IntPtr retval) {
      _database.BeginEntry();
      var ret = _invokedict[funcname](argvalues);
      Marshal.WriteIntPtr(retval, ret);
      _database.EndEntry();
      //_database.Reset();
      return true;
    }

    // Entry point: Create generic wrappers for type checking, type conversion and evaluation
    // All type conversion happens here in the generated wrappers (closures)
    public void AddWrapper(string funcname, SqlCommonType[] argtypes, SqlCommonType rettype, Func<object[], object> funcbody) {
      // type check wrapper
      Func<int[], int, bool> ftc = (a, r) => CheckTypes(a, r, argtypes, rettype);
      _typecheckdict.Add(funcname, ftc);

      // function invocation wrapper
      // note: raises exception on error
      Func<IntPtr[], IntPtr> evc = (a) => {
        var ret = funcbody(Datum.ConvertTo(argtypes, a));
        return Datum.ConvertFrom(rettype, ret);
      };
      _invokedict.Add(funcname, evc);
    }

    //=========================================================================
    // Support for functions, type checking and conversions
    //

    // A type check for each registered function
    Dictionary<string, Func<int[], int, bool>> _typecheckdict = new Dictionary<string, Func<int[], int, bool>>();
    // An invoke for each registered function
    Dictionary<string, Func<IntPtr[], IntPtr>> _invokedict = new Dictionary<string, Func<IntPtr[], IntPtr>>();

    // Check that the OID arg and return types are compatible with common type definition
    bool CheckTypes(int[] argtypes, int rettyp, SqlCommonType[] sargtypes, SqlCommonType srettyp) {
      _database.SetError(SpiReturn.OK, "");
      if (argtypes.Length != sargtypes.Length)
        _database.SetError(SpiReturn.CONVERSION, $"expected {sargtypes.Length} arguments, found {argtypes.Length}");
      else {
        var fx = Enumerable.Range(1, argtypes.Length).FirstOrDefault(x => !TypeMatch(argtypes[x - 1], sargtypes[x - 1]));
        if (fx != 0)
          _database.SetError(SpiReturn.CONVERSION, $"argument {fx} type mismatch, expected {sargtypes[fx - 1]}");
        else if (!TypeMatch(rettyp, srettyp))
          _database.SetError(SpiReturn.CONVERSION, $"return type mismatch, expected {srettyp}");
      }
      return _database.CheckOk();
    }

    // Check that an OID and common type are compatible
    bool TypeMatch(int inttype, SqlCommonType exptype) {
      return _database.OidToCommon(inttype) == exptype;
    }

    #endregion

    // Create one function for each expression as a closure, capturing the serial and type for that function

    // Create an open or predicate function
    public bool CreateFunction(string name, FuncTypes functype, int serial, SqlCommonType[] argtypes, SqlCommonType rettype) {
      Func<object[], object> func = (v) => _database.Evaluator.EvalSerialOpen(serial, functype, v);
      AddWrapper(name, argtypes, rettype, func);
      return true;
    }

    // Create an aggregating function
    public bool CreateAggFunction(string name, int serial, int accnum, SqlCommonType[] argtypes, SqlCommonType rettype) {
      // state argument type
      var arg0 = new SqlCommonType[] { SqlCommonType.Number };
      //var arg0 = new SqlCommonType[] { SqlCommonType.Integer };  // problem: cannot generate SQL
      //var arg0 = new SqlCommonType[] { SqlCommonType.Binary };  // problem: null first time (forgot to set initial value)

      // wrapper calls evaluator, discards return value and substitutes state argument
      Func<object[], object> sfunc = (v) => {
        _database.Evaluator.EvalSerialAggOpen(serial, FuncTypes.Aggregate, v.Skip(1).ToArray(), accnum);
        return v[0];
      };
      AddWrapper(name, arg0.Concat(argtypes).ToArray(), arg0[0], sfunc);

      // wrapper calls evaluator, ignoring state argument
      Func<object[], object> ffunc = (v) => _database.Evaluator.EvalSerialAggFinal(serial, FuncTypes.Aggregate);
      AddWrapper(name + "F", arg0, rettype, ffunc);
      return true;
    }
  }

  ///==============================================================================================
  /// <summary>
  /// Conversion functions, Datum <=> object, 6 core types
  /// </summary>

  internal struct Datum {
    static Dictionary<SqlCommonType, Func<IntPtr, object>> _totypec = new Dictionary<SqlCommonType, Func<IntPtr, object>> {
      { SqlCommonType.Bool,   (p) => p.ToInt32() != 0 },
      { SqlCommonType.Binary, (p) => ToBinary(p) },
      { SqlCommonType.Integer,(p) => p.ToInt32() },
      { SqlCommonType.Number, (p) => ToNumber(p) },
      { SqlCommonType.Text,   (p) => ToText(p) },
      { SqlCommonType.Time,   (p) => ToTime(p) },
    };
    static Dictionary<SqlCommonType, Func<object, IntPtr>> _fromtypec = new Dictionary<SqlCommonType, Func<object, IntPtr>> {
      { SqlCommonType.Bool,   (o) => new IntPtr((bool)o ? 1 : 0) },
      { SqlCommonType.Binary, (o) => From(o as byte[]) },
      { SqlCommonType.Integer,(o) => new IntPtr((int)o) },
      { SqlCommonType.Number, (o) => From((decimal)o) },
      { SqlCommonType.Text,   (o) => From(o as string) },
      { SqlCommonType.Time,   (o) => From((DateTime)o) },
    };

    // Convert from Datum to common type
    internal static object ConvertTo(SqlCommonType type, IntPtr value) {
      if (_totypec.ContainsKey(type)) return _totypec[type](value);
      return null;
    }

    // Convert list from Datum to common type
    internal static object[] ConvertTo(SqlCommonType[] types, IntPtr[] values) {
      return types.Select((t, x) => ConvertTo(t, values[x])).ToArray();
    }

    // Convert from common type to Datum
    internal static IntPtr ConvertFrom(SqlCommonType type, object value) {
      if (_fromtypec.ContainsKey(type)) return _fromtypec[type](value);
      return IntPtr.Zero;
    }

    // Convert list from common type to Datum
    internal static IntPtr[] ConvertFrom(SqlCommonType[] types, object[] values) {
      return types.Select((t, x) => ConvertFrom(t, values[x])).ToArray();
    }

    //-----------------------------------------------------
    // individual conversion functions, from common to Datum
    static IntPtr From(string value) {
      var bytes = Encoding.UTF8.GetBytes(value);
      var ptr = PostgresInterop.pg_alloc_datum(bytes.Length);
      Marshal.Copy(bytes, 0, ptr + 4, bytes.Length);
      return ptr;
    }

    static IntPtr From(byte[] value) {
      var ptr = PostgresInterop.pg_alloc_datum(value.Length);
      Marshal.Copy(value, 0, ptr + 4, value.Length);
      return ptr;
    }

    static IntPtr From(DateTime value) {
      return PostgresInterop.pg_cstring_to_timestamp(value.ToString("yyyy-MM-dd HH:mm:ss.fff"));
    }

    static IntPtr From(decimal value) {
      return PostgresInterop.pg_cstring_to_numeric(value.ToString());
    }

    //-----------------------------------------------------
    // individual conversion functions, from Datum to common
    // IntPtr points to varlena struct
    // First int is length<<2 (two bits left as flags)
    // length includes header, so data length is (len/4)-4

    // Get raw binary
    static byte[] ToBinary(IntPtr value) {
      var dval = PostgresInterop.pg_detoast_bytea(value);
      var len = Marshal.ReadInt32(dval) / 4;
      var buf = new byte[len - 4];
      Marshal.Copy(dval + 4, buf, 0, buf.Length);
      return buf;
    }

    static string ToText(IntPtr value) {
      return Encoding.UTF8.GetString(ToBinary(value));
    }

    static decimal ToNumber(IntPtr value) {
      // cannot use default marshalling because it will free the memory
      var ret = PostgresInterop.pg_numeric_to_cstring(value);
      var s = Marshal.PtrToStringAnsi(ret);
      return s.SafeDecimalParse() ?? 0;
    }

    static DateTime ToTime(IntPtr value) {
      var ret = PostgresInterop.pg_timestamp_to_cstring(value);
      var s = Marshal.PtrToStringAnsi(ret);
      return s.SafeDatetimeParse() ?? DateTime.MinValue;
    }
  }
}
