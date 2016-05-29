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
using Andl.Common;

namespace Andl.Sql {
  // Types of function that can be registered
  public enum FuncTypes {
    Open, Predicate, Aggregate, Ordered
  };

  // Common types used for data interchange
  public enum SqlCommonType {
    None, Binary, Bool, Integer, Double, Number, Text, Time
  }

  // support PG function registration and calling
  // note: ints are oids and intptrs are Datums
  public interface ISqlPgFunction {
    bool TypeCheck(string funcname, int[] argtypes, int rettype);
    bool Invoke(string funcname, IntPtr[] argvalues, IntPtr retval);
    void AddWrapper(string funcname, SqlCommonType[] argtypes, SqlCommonType rettype, Func<object[], object> funcbody);
  }

  /// <summary>
  /// Define an interface that can evaluate functions identified by serial
  /// </summary>
  public interface ISqlEvaluateSerial {
    // open function
    object EvalSerialOpen(int serial, FuncTypes functype, object[] values);
    // aggregating function
    object EvalSerialAggOpen(int serial, FuncTypes functype, object[] values, int naccnum);
    // final function
    object EvalSerialAggFinal(int serial, FuncTypes functype);
  }

  // HACK: needs a different solution
  public struct LenPtrPair {
    public int Length;
    public IntPtr Pointer;
  }

  /// <summary>
  /// Interface for access to SQL database
  /// </summary>
  public interface ISqlDatabase {
    string LastCode { get; }
    string LastMessage { get; }
    bool IsOpen { get; }
    int Nesting { get; }
    ISqlEvaluateSerial Evaluator { get; }

    // create a statement object
    ISqlStatement BeginStatement();
    bool EndStatement(ISqlStatement statement);

    // transaction boundaries
    bool Begin();
    bool Commit();
    bool RollBack();

    // Get information about a named table
    bool GetTableColumns(string table, out Tuple<string, SqlCommonType>[] columns);

    void Reset();
    void Close();
  }

  /// <summary>
  /// Interface for creating SQL function callbacks
  /// </summary>
  public interface ISqlFunctionCreator {
    // Create an open or predicate function
    bool CreateFunction(string name, FuncTypes functype, int serial, SqlCommonType[] args, SqlCommonType retn);
    // Create an aggregating function
    bool CreateAggFunction(string name, int serial, int accnum, SqlCommonType[] args, SqlCommonType retn);
  }

  /// <summary>
  /// Interface for access to SQL statement
  /// </summary>
  public interface ISqlStatement {
    bool IsPrepared { get; }
    bool HasData { get; }

    bool Prepare(string sql, SqlCommonType[] types = null);
    void Close();
    bool ExecuteCommand(string sql);
    bool ExecuteQuery(string sql);
    bool ExecuteQueryMulti(string sql);
    bool Execute();
    bool ExecuteSend(SqlCommonType[] types, object[] values);
    bool Fetch();
    bool GetValues(SqlCommonType[] types, object[] fields);
  }

}
