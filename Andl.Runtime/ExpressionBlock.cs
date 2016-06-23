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

namespace Andl.Runtime {

  public enum ExpressionKinds {
    Nul,        // nul, does nothing
    Value,      // a named value without evaluation
    Project,    // copies value, no code
    Rename,     // renames value, no code
    IsOrder,    // attributes used for sorting/grouping, no code
    Closed,     // simple calculation, no lookup
    Open,       // iterated calculation requires lookup
    IsFolded,   // fold expression that accumulates values using aggregation
    HasFold,    // uses accumulator(s) during finalisation for final calculation
    HasWin, // contains function that depends on ordering (and fold maybe)
  }

  /// <summary>
  /// Implements named expression for evaluation
  /// </summary>
  public interface IApiExpression {
    //// Name of expression
    //string Name { get; }
    //// Kind of expression
    //ExpressionKinds Kind { get; }
    //// Return type for this expression
    //DataType DataType { get; }
    //// Used by rename
    //string OldName { get; }

    //bool IsRename { get; }
    //bool IsFolded { get; }
    //bool IsDesc { get; }
  }

  /// <summary>
  /// Implementation of named expression based on Scode
  /// </summary>
  public class ExpressionBlock : IApiExpression {
    public string Name { get; protected set; }
    // project/rename/extend etc
    public ExpressionKinds Kind { get; protected set; }
    // Datatype returned from expression
    public DataType ReturnType { get; set; }
    // substitution values: attributes or arg list
    public DataHeading Lookup { get { return InternalLookup ?? DataHeading.Empty; } }
    // previous name for when field is renamed (current is new name)
    public string OldName { get; protected set; }
    // no of accumulators needed for this expression
    public int AccumCount { get; protected set; }
    // value used by kind=value
    public TypedValue Value { get; protected set; }
    // the actual executable code
    public ByteCode Code { get; protected set; }
    // unique number for use by runtime
    public int Serial { get; protected set; }

    public bool IsGrouped { get; protected set; } // true to perform grouping
    public bool IsDesc { get; protected set; }    // true to sort descending
    // 'real' value of lookup can be null if argless
    public DataHeading InternalLookup { get; protected set; }

    // true for 'lazy' function that looks like a variable
    public bool IsArgless { get { return InternalLookup == null; } }
    // full datatype is the function signature
    public DataTypeCode FullDataType { get { return DataTypeCode.Get(ReturnType, Lookup); } }

    static int _serialcounter = 0;

    public int NumArgs { get { return (InternalLookup == null) ? 0 : Lookup.Degree; } }
    public bool IsValue { get { return Kind == ExpressionKinds.Value; } }
    public bool IsRename { get { return Kind == ExpressionKinds.Rename; } }
    public bool IsProject { get { return Kind == ExpressionKinds.Project; } }
    public bool IsOrder { get { return Kind == ExpressionKinds.IsOrder; } }
    public bool IsOpen { get { return Kind == ExpressionKinds.Open; } }
    public bool IsFolded { get { return Kind == ExpressionKinds.IsFolded; } }
    public bool HasFold { get { return Kind == ExpressionKinds.HasFold; } }
    public bool HasWin { get { return Kind == ExpressionKinds.HasWin; } }

    // Unique name for heading used as arguments
    public string SubtypeName { get { return "(" + Serial + ")"; } }

    public static ExpressionBlock Empty {
      get { return Create("&e", ExpressionKinds.Nul, new ByteCode(), DataTypes.Void); }
    }
    public static ExpressionBlock True {
      get { return Create("&t", BoolValue.True); }
    }

    public override string ToString() {
      if (Kind == ExpressionKinds.Value)
        return String.Format("{0} {1}={2}", Kind, Name, Value);
      if (Kind == ExpressionKinds.Rename || Kind == ExpressionKinds.Project)
        return String.Format("{0} {1}<-{2} @{3}", Kind, Name, OldName, Lookup.ToString());
      if (Kind == ExpressionKinds.IsOrder)
        return String.Format("{0} {1} {2} {3}", Kind, Name, IsGrouped ? "grp" : "ord", IsDesc ? "desc" : "asc");
      return String.Format("{0} {1}:{2} ac:{3} code:{4} #{5}", Kind, Name,
        FullDataType, AccumCount, Code.Length, Serial);
    }

    public string ToFormat() {
      return ToString();
    }

    // Create an expression block with code to evaluate.
    public static ExpressionBlock Create(string name, ExpressionKinds kind, ByteCode code, 
        DataType rtntype, DataHeading lookup = null, int accums = 0, int serial = 0) {
      //Logger.Assert(!(rtntype is DataTypeCode), "expr code");
      return new ExpressionBlock { 
        Name = name ?? "!", 
        Kind = kind,
        Serial = serial == 0 ? ++_serialcounter : serial,
        ReturnType = rtntype, 
        Code = code, 
        AccumCount = accums,
        InternalLookup = lookup,
      };
    }

    // Create a codeless expression block for renames and projects
    // Create a lookup so later code can easily track total set of inputs
    public static ExpressionBlock Create(string name, string oldname, DataType rtntype) {
      return new ExpressionBlock {
        Kind = (name == oldname) ? ExpressionKinds.Project : ExpressionKinds.Rename,
        Name = name,
        ReturnType = rtntype,
        OldName = oldname,
        InternalLookup = DataHeading.Create(new DataColumn[] { DataColumn.Create(oldname, rtntype) }),
      };
    }

    // Create a codeless expression block for sorts
    public static ExpressionBlock Create(string name, DataType rtntype, bool grouped, bool descending) {
      return new ExpressionBlock {
        Kind = ExpressionKinds.IsOrder,
        Name = name,
        ReturnType = rtntype,
        IsGrouped = grouped,
        IsDesc = descending,
      };
    }

    // Create a codeless expression block for a value
    public static ExpressionBlock Create(string name, TypedValue value) {
      return new ExpressionBlock {
        Kind = ExpressionKinds.Value,
        Name = name,
        ReturnType = value.DataType,
        Value = value,
      };
    }

    public DataColumn ToDataColumn() {
      return DataColumn.Create(Name, ReturnType);
    }

  }

  //////////////////////////////////////////////////////////////////////
  /// <summary>
  /// Implements an expression block bound to an evaluator
  /// </summary>
  public class ExpressionEval : ExpressionBlock {
    // This is the evaluator
    public Evaluator Evaluator { get; set; }

    public static ExpressionEval Create(Evaluator evaluator, ExpressionBlock expr) {
      return new ExpressionEval {
        Evaluator = evaluator,
        Name = expr.Name,
        Kind = expr.Kind,
        ReturnType = expr.ReturnType,
        InternalLookup = expr.InternalLookup,
        OldName = expr.OldName,
        Code = expr.Code,
        AccumCount = expr.AccumCount,
        IsGrouped  = expr.IsGrouped,
        IsDesc = expr.IsDesc,
        Serial = expr.Serial,
      };
    }

    // evaluate a closed expression returning a typed value
    // also used for rename
    public TypedValue Evaluate() {
      return EvalOpen(null);
    }

    // evaluate an open expression returning a typed value
    // rename -- do it here
    // aggregate -- not safe to eval, so just return typed default
    public TypedValue EvalOpen(ILookupValue lookup) {
      if (IsValue)
        return Value;
      if (HasFold)
        return ReturnType.DefaultValue();
      TypedValue ret;
      if (IsRename || IsProject)
        ret = Evaluator.Lookup(OldName, lookup);
      else {
        Logger.Assert(IsOpen || HasWin || lookup == null, Name);
        Logger.Assert(Evaluator != null, "evaluator null");
        ret = Evaluator.Exec(Code, lookup);
      }
      CheckReturnType(ret);
      return ret;
    }

    // evaluate an open predicate expression returning true/false
    public BoolValue EvalPred(ILookupValue lookup) {
      Logger.Assert(ReturnType == DataTypes.Bool, Name);
      if (Code.Length == 0) return BoolValue.True;
      return EvalOpen(lookup) as BoolValue;
    }

    // evaluate a fold expression with previous value returning an updated value
    public TypedValue EvalIsFolded(ILookupValue lookup, TypedValue aggregate) {
      Logger.Assert(IsFolded, Name);
      var ret = Evaluator.Exec(Code, lookup, aggregate);
      CheckReturnType(ret);
      return ret;
    }

    // evaluate a post-fold expression that depends on accumulator values
    public TypedValue EvalHasFold(ILookupValue lookup, AccumulatorBlock accblock, int accbase = 0) {
      Logger.Assert(HasFold, Name);
      accblock.IndexBase = accbase;
      var ret = Evaluator.Exec(Code, lookup, null, accblock);
      CheckReturnType(ret);
      return ret;
    }

    // Check that return type matches, but allow null (needed in aggregation)
    // Check heading, but if null this is first usage to set it
    void CheckReturnType(TypedValue value) {
      if (ReturnType == DataTypes.Unknown) return;    // suppress checking why???
      if (ReturnType == DataTypes.Code) return;    // suppress checking why???
      Logger.Assert(value.DataType.Equals(ReturnType), "type mismatch");
    }
  }
}
