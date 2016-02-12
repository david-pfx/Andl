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
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Andl.Runtime {
  /// <summary>
  /// Information about each Builtin or Addin function
  /// Note: names are required to be unique
  /// </summary>
  public class BuiltinInfo {
    // function name
    public string Name { get; set; }
    // system specific caller info
    public DataType ReturnType { get; set; }
    // array of arguments as columns
    public DataColumn[] Arguments { get; set; }

    // List of definitions for operators preloaded into symbol table
    // Need to force init of Date type before this
    // only works if every function uses only known types
    public static BuiltinInfo[] GetBuiltinInfo() {
      Logger.Assert(Builtin.DateValue.StaticDatatype != null);
      var builtins = new List<BuiltinInfo>();
      var methods = typeof(Builtin).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
      foreach (var method in methods) {
        if (Char.IsUpper(method.Name[0])) { // avoid "get_xxx"
          var rtype = GetDataType(method.ReturnType);
          var parms = method.GetParameters();
          var args = parms.Select(p => DataColumn.Create(p.Name, GetDataType(p.ParameterType))).ToArray();
          builtins.Add(new BuiltinInfo {
            Name = method.Name,
            ReturnType = rtype,
            Arguments = args
          });
        }
      }
      return builtins.ToArray();
    }

    static DataType GetDataType(Type type) {
      Logger.Assert(DataTypes.TypeDict.ContainsKey(type), type);
      return DataTypes.TypeDict[type];
    }
  }

  /// <summary>
  /// Implements the ability to add functions
  /// </summary>
  public class AddinInfo {
    public string Name;
    public int NumArgs;
    public DataType DataType;
    public string Method;
    public static AddinInfo Create(string name, int numargs, DataType datatype, string method) {
      return new AddinInfo { Name = name, NumArgs = numargs, DataType = datatype, Method = method };
    }

    public static AddinInfo[] GetAddinInfo() {
      var addins = new List<AddinInfo>();
      addins.Add(AddinInfo.Create("read", 1, DataTypes.Text, "Read"));
      addins.Add(AddinInfo.Create("write", 1, DataTypes.Void, "Write"));
      addins.Add(AddinInfo.Create("pause", 1, DataTypes.Void, "Pause"));
      addins.Add(AddinInfo.Create("fail", 2, DataTypes.Void, "Fail"));
      addins.Add(AddinInfo.Create("assert", 2, DataTypes.Void, "Assert"));

      addins.Add(AddinInfo.Create("type", 1, DataTypes.Text, "Type"));

      addins.Add(AddinInfo.Create("binary", 1, DataTypes.Binary, "Binary"));
      addins.Add(AddinInfo.Create("bool", 1, DataTypes.Bool, "Bool"));
      addins.Add(AddinInfo.Create("number", 1, DataTypes.Number, "Number,NumberT"));
      addins.Add(AddinInfo.Create("time", 1, DataTypes.Time, "Time,TimeD")); //FIX:
      addins.Add(AddinInfo.Create("text", 1, DataTypes.Text, "Text"));

      addins.Add(AddinInfo.Create("format", 1, DataTypes.Text, "Format"));
      addins.Add(AddinInfo.Create("pp", 1, DataTypes.Text, "PrettyPrint"));
      addins.Add(AddinInfo.Create("length", 1, DataTypes.Number, "Length"));
      addins.Add(AddinInfo.Create("fill", 2, DataTypes.Text, "Fill"));
      addins.Add(AddinInfo.Create("trim", 1, DataTypes.Text, "Trim"));
      addins.Add(AddinInfo.Create("left", 2, DataTypes.Text, "Left"));
      addins.Add(AddinInfo.Create("right", 2, DataTypes.Text, "Right"));
      addins.Add(AddinInfo.Create("before", 2, DataTypes.Text, "Before"));
      addins.Add(AddinInfo.Create("after", 2, DataTypes.Text, "After"));
      addins.Add(AddinInfo.Create("toupper", 1, DataTypes.Text, "ToUpper"));
      addins.Add(AddinInfo.Create("tolower", 1, DataTypes.Text, "ToLower"));

      addins.Add(AddinInfo.Create("bget", 1, DataTypes.Number, "BinaryGet"));
      addins.Add(AddinInfo.Create("bset", 1, DataTypes.Binary, "BinarySet"));
      addins.Add(AddinInfo.Create("blength", 1, DataTypes.Binary, "BinaryLength"));

      addins.Add(AddinInfo.Create("now", 0, DataTypes.Time, "Now"));

      addins.Add(AddinInfo.Create("date", 1, Builtin.DateValue.StaticDatatype, "FromTime"));
      addins.Add(AddinInfo.Create("dateymd", 3, Builtin.DateValue.StaticDatatype, "FromYmd"));
      addins.Add(AddinInfo.Create("year", 1, DataTypes.Number, "Year"));
      addins.Add(AddinInfo.Create("month", 1, DataTypes.Number, "Month"));
      addins.Add(AddinInfo.Create("day", 1, DataTypes.Number, "Day"));
      addins.Add(AddinInfo.Create("dow", 1, DataTypes.Number, "DayOfWeek"));
      addins.Add(AddinInfo.Create("daysdiff", 2, DataTypes.Number, "DaysDifference"));
      //addins.Add(AddinInfo.Create("time", 1, DataTypes.Time, "TimeD"));

      addins.Add(AddinInfo.Create("count", 1, DataTypes.Number, "Count"));
      addins.Add(AddinInfo.Create("degree", 1, DataTypes.Number, "Degree"));
      addins.Add(AddinInfo.Create("schema", 1, DataTypeRelation.Get(DataHeading.Create("Name:text", "Type:text")), "Schema"));
      addins.Add(AddinInfo.Create("seq", 1, DataTypeRelation.Get(DataHeading.Create("N:number")), "Sequence"));
      addins.Add(AddinInfo.Create("andl_variable", 0, DataTypeRelation.Get(Catalog.CatalogTableHeading(Catalog.CatalogTables.Variable)), "Variables"));
      addins.Add(AddinInfo.Create("andl_operator", 0, DataTypeRelation.Get(Catalog.CatalogTableHeading(Catalog.CatalogTables.Operator)), "Operators"));
      addins.Add(AddinInfo.Create("andl_member", 0, DataTypeRelation.Get(Catalog.CatalogTableHeading(Catalog.CatalogTables.Member)), "Members"));

      return addins.ToArray();
    }
  }

  /// <summary>
  /// Implement set of built in functions for Andl language
  /// 
  /// These functions will be loaded by reflection.
  /// </summary>
  //public static class Builtin {
  public class Builtin {
    CatalogPrivate _catalog;
    Evaluator _evaluator;

    public static Builtin Create(CatalogPrivate catalog, Evaluator evaluator) {
      return new Builtin {
        _catalog = catalog,
        _evaluator = evaluator,
      };
    }

    ///=================================================================
    ///
    /// Special operations
    /// 

    // Assign a value to a variable 
    // The variable is identified by a named expression. which must be evaluated
    public VoidValue Assign(CodeValue exprarg) {
      Logger.WriteLine(3, "Assign {0}", exprarg);
      var name = exprarg.Value.Name;
      var value = exprarg.AsEval.Evaluate();
      _catalog.SetValue(name, value);
      Logger.WriteLine(3, "[Ass]");
      return VoidValue.Default;
    }

    // Assign a value to a variable 
    // The variable is identified by name, a value is stored
    public VoidValue Assign2(TextValue name, TypedValue value) {
      Logger.WriteLine(3, "Assign {0}:={1}", name, value);
      _catalog.SetValue(name.Value, value);
      Logger.WriteLine(3, "[Ass]");
      return VoidValue.Default;
    }

    // Assign a code block to a variable 
    // The variable is identified by a named expression, which must be saved
    public VoidValue Defer(CodeValue exprarg) {
      Logger.WriteLine(3, "Defer {0}", exprarg);
      Logger.Assert(exprarg.AsEval == null);
      var name = exprarg.Value.Name;
      _catalog.SetValue(name, exprarg);
      Logger.WriteLine(3, "[Def]");
      return VoidValue.Default;
    }

    // Invoke a do block with its own scope level
    // FIX: does not really need its own code segment?
    public TypedValue DoBlock(CodeValue exprarg, PointerValue accblkarg) {
      Logger.WriteLine(3, "DoBlock {0}", exprarg);
      _catalog.PushScope();
      var accblk = accblkarg.Value as AccumulatorBlock;
      var ret = _evaluator.Exec(exprarg.Value.Code, null, null, accblk);
      _catalog.PopScope();
      Logger.WriteLine(3, "[Do {0}]", ret);
      return ret;
    }

    // IF(expr,true,false)
    public TypedValue If(BoolValue arg1, CodeValue arg2, CodeValue arg3) {
      Logger.WriteLine(3, "If {0},{1},{2}", arg1, arg2, arg3);
      var ret = (arg1.Value) ? arg2.AsEval.Evaluate() : arg3.AsEval.Evaluate();
      Logger.WriteLine(3, "[If {0}]", ret);
      return ret;
    }

    // Invoke a defined function with required argument in scope
    // If folded, applies an offset to get the right accumulator
    public TypedValue Invoke(CodeValue funcarg, PointerValue accblkarg, NumberValue accbasarg, TypedValue[] valargs) {
      Logger.WriteLine(3, "Invoke {0} accbase={1} ({2})", funcarg, accbasarg, String.Join(",", valargs.Select(a => a.ToString()).ToArray()));

      // wrap raw value with evaluator
      var expr = ExpressionEval.Create(_evaluator, funcarg.Value);
      var args = DataRow.CreateNonTuple(expr.Lookup, valargs);
      TypedValue ret;
      if (expr.HasFold) {
        var accblk = accblkarg.Value as AccumulatorBlock;
        var accbase = (int)accbasarg.Value;
        ret = expr.EvalHasFold(args, accblk, accbase);
      } else ret = expr.EvalOpen(args);
      if (ret is RelationValue && !(ret.AsTable() is DataTableLocal))
        ret = RelationValue.Create(DataTableLocal.Convert(ret.AsTable(), args));
      Logger.WriteLine(3, "[Inv {0}]", ret);
      return ret;
    }

    // Create a row by evaluating named expressions against a heading
    // TODO:no heading
    public TupleValue Row(TypedValue hdgarg, params CodeValue[] exprargs) {
      var heading = hdgarg.AsHeading();
      var exprs = exprargs.Select(e => (e as CodeValue).AsEval).ToArray();
      var newrow = DataRow.Create(heading, exprs);
      Logger.WriteLine(3, "[Row={0}]", newrow);
      return TupleValue.Create(newrow);
    }

    // Create a row from values and a heading
    public TupleValue RowV(TypedValue hdgarg, params TypedValue[] valueargs) {
      var heading = hdgarg.AsHeading();
      var newrow = DataRow.Create(heading, valueargs);
      Logger.WriteLine(3, "[Row={0}]", newrow);
      return TupleValue.Create(newrow);
    }

    // Create a row from a heading by converting a value 
    public TupleValue RowC(TypedValue hdgarg, TypedValue[] valuearg) {
      var heading = hdgarg.AsHeading();
      var value = valuearg[0];
      DataRow newrow = null;
      if (value.DataType is DataTypeTuple)
        newrow = value.AsRow();
      else if (value.DataType is DataTypeUser) {
        var user = value as UserValue;
        newrow = DataRow.Create(user.Heading, user.Value);
      } else if (value.DataType is DataTypeRelation) {
        var rel = value.AsTable();
        newrow = rel.GetRows().FirstOrDefault();
        if (newrow == null)
          ProgramError.Error("Builtin", "relation is empty");
      }
      Logger.Assert(newrow != null, "RowT");
      Logger.WriteLine(3, "[Row={0}]", newrow);
      return TupleValue.Create(newrow);
    }

    // Create a Table from a list of expressions that will yield rows
    // Each row has its own heading, which must match.
    public RelationValue Table(HeadingValue hdgarg, params CodeValue[] exprargs) {
      var exprs = exprargs.Select(e => e.AsEval).ToArray();

      var newtable = DataTable.Create(hdgarg.Value, exprs);
      Logger.WriteLine(3, "[Table={0}]", newtable);
      return RelationValue.Create(newtable);
    }

    // Create a Table from row values and a heading
    // Each row has its own heading, which must match.
    public RelationValue TableV(HeadingValue hdgarg, params TypedValue[] rowargs) {
      var newtable = DataTable.Create(hdgarg.Value, rowargs.Select(r => r.AsRow()));
      Logger.WriteLine(3, "[Table={0}]", newtable);
      return RelationValue.Create(newtable);
    }

    // Create a Table by converting a value
    // Each row has its own heading, which must match.
    public RelationValue TableC(HeadingValue hdgarg, params TypedValue[] valueargs) {
      Logger.Assert(valueargs.Length == 1, "TableC");
      var heading = hdgarg.AsHeading();
      var value = valueargs[0];
      DataTable newtable = null;
      if (value.DataType is DataTypeTuple)
        newtable = DataTableLocal.Create(heading, new DataRow[] {
          value.AsRow()
        });
      else if (value.DataType is DataTypeUser) {
        var user = value as UserValue;
        newtable = DataTableLocal.Create(heading, new DataRow[] {
          DataRow.Create(heading, user.Value)
        });
      } else if (value.DataType is DataTypeRelation) {
        newtable = value.AsTable();
      }
      Logger.Assert(newtable != null, "TableC");
      Logger.WriteLine(3, "[Table={0}]", newtable);
      return RelationValue.Create(newtable);
    }

    public UserValue UserSelector(TextValue typename, TypedValue[] valargs) {
      var usertype = DataTypeUser.Find(typename.Value);
      return usertype.CreateValue(valargs);
    }

    // Import a linked or externally held relvar
    public VoidValue Import(TextValue sourcearg, TextValue namearg, TextValue locatorarg) {
      if (sourcearg.Value == "") {
        if (!_catalog.Catalog.LinkRelvar(namearg.Value))
          ProgramError.Error("Connect", "cannot link to '{0}'", namearg.Value);
      } else if (!_catalog.Catalog.ImportRelvar(sourcearg.Value, namearg.Value, locatorarg.Value))
        ProgramError.Error("Connect", "cannot import from '{0}'", namearg.Value);
      return VoidValue.Default;
    }

    ///=================================================================
    ///
    /// Current tuple operations
    /// 

    // Return ordinal as scalar
    public NumberValue Ordinal(PointerValue lookup) {
      var row = lookup.Value as DataRow;
      Logger.Assert(row != null, "lookup is not row");
      return row.Ordinal(false);
    }

    // Return ordinal as scalar
    public NumberValue OrdinalGroup(PointerValue lookup) {
      var row = lookup.Value as DataRow;
      Logger.Assert(row != null, "lookup is not row");
      return row.Ordinal(true);
    }

    // Return value of attribute with tuple indexing
    public TypedValue ValueLead(CodeValue attribute, NumberValue index, PointerValue lookup) {
      var row = lookup.Value as DataRow;
      Logger.Assert(row != null, "lookup is not row");
      return row.ValueOffset(attribute.AsEval, (int)index.Value, OffsetModes.Lead);
    }

    // Return value of attribute with tuple indexing
    public TypedValue ValueLag(CodeValue attribute, NumberValue index, PointerValue lookup) {
      var row = lookup.Value as DataRow;
      Logger.Assert(row != null, "lookup is not row");
      return row.ValueOffset(attribute.AsEval, (int)index.Value, OffsetModes.Lag);
    }

    // Return value of attribute with tuple indexing
    public TypedValue ValueNth(CodeValue attribute, NumberValue index, PointerValue lookup) {
      var row = lookup.Value as DataRow;
      Logger.Assert(row != null, "lookup is not row");
      return row.ValueOffset(attribute.AsEval, (int)index.Value, OffsetModes.Absolute);
    }

    // Return rank of attribute with tuple indexing
    public TypedValue Rank(CodeValue attribute, NumberValue index, PointerValue lookup) {
      var row = lookup.Value as DataRow;
      Logger.Assert(row != null, "lookup is not row");
      var value = attribute.AsEval.EvalOpen(row);
      var offset = row.Heading.FindIndex(attribute.AsEval.Name);
      return NumberValue.Create(index.Value + 1);
    }

    public TypedValue Fold2(TypedValue defarg, CodeValue expr, PointerValue accblkarg, NumberValue accidarg) {
      return null;
    }

    // Fold an operation and one argument over a set of tuples
    public TypedValue Fold(PointerValue accblkarg, NumberValue accidarg, TypedValue defarg, CodeValue expr) {
      Logger.WriteLine(3, "Fold n={0} def={1} expr={2}", accidarg, defarg, expr);
      var accblock = accblkarg.Value as AccumulatorBlock;
      var accid = (int)accidarg.Value;
      var accum = accblock[accid] ?? defarg;
      accblock[accid] = expr.AsEval.EvalIsFolded(null, accblock[accid]);
      Logger.WriteLine(3, "[Fold {0}]", accblock[accid]);
      return accblock[accid];
    }

    // Fold an operation and one argument over a set of tuples
    public TypedValue CumFold(TypedValue accumulator, CodeValue expr) {
      // if accum is Empty this is a request for a default value
      if (accumulator == TypedValue.Empty)
        return expr.AsEval.DataType.DefaultValue();
      return expr.AsEval.EvalIsFolded(null, accumulator);
    }

    // Lift a value out of a relation
    public TypedValue Lift(RelationValue relarg) {
      Logger.WriteLine(3, "Lift {0}", relarg);
      return relarg.Value.Lift();
    }

    ///=================================================================
    ///
    /// Monadic operations
    /// 

    // Create new table with less columns and perhaps less rows; can also rename
    // TODO: optimise one pass
    public RelationValue Project(RelationValue relarg, params CodeValue[] exprargs) {
      Logger.WriteLine(3, "Project {0} {1}", relarg, exprargs.Select(e => e.AsEval.Kind.ToString()).ToArray());
      var rel = relarg.Value;
      var exprs = exprargs.Select(e => (e as CodeValue).AsEval).ToArray();
      var relnew = rel.Project(exprs);
      Logger.WriteLine(3, "[Pr {0}]", relnew);
      return RelationValue.Create(relnew);
    }

    // Rename by applying rename expressions
    // Just switch heading
    public RelationValue Rename(RelationValue relarg, params CodeValue[] exprargs) {
      Logger.WriteLine(3, "Rename {0} {1}", relarg, exprargs.Select(e => e.AsEval.Kind.ToString()).ToArray());
      var renames = exprargs.Select(r => (r as CodeValue).AsEval).ToArray();
      var relnew = relarg.Value.Rename(renames);
      Logger.WriteLine(3, "[Rn {0}]", relnew);
      return RelationValue.Create(relnew);
    }

    // Create new table filtered by evaluating a predicate expressions
    public RelationValue Restrict(RelationValue relarg, params CodeValue[] exprargs) {
      Logger.WriteLine(3, "Restrict {0} {1}", relarg, exprargs.Select(e => e.AsEval.Kind.ToString()).ToArray());
      var relnew = relarg.Value.Restrict(exprargs[0].AsEval);
      Logger.WriteLine(3, "[Rs {0}]", relnew);
      return RelationValue.Create(relnew);
    }

    // Create new table using only the first N rows
    public RelationValue Take(RelationValue relarg, NumberValue howmany) {
      Logger.WriteLine(3, "Take {0} {1}", relarg, howmany);
      var relnew = relarg.Value.Take(howmany);
      Logger.WriteLine(3, "[T {0}]", relnew);
      return RelationValue.Create(relnew);
    }

    // Create new table using only the rows after the first N
    public RelationValue Skip(RelationValue relarg, NumberValue howmany) {
      Logger.WriteLine(3, "Skip {0} {1}", relarg, howmany);
      var relnew = relarg.Value.Skip(howmany);
      Logger.WriteLine(3, "[S {0}]", relnew);
      return RelationValue.Create(relnew);
    }

    // Transform does Rename and/or Project and/or Extend combo
    public RelationValue Transform(RelationValue relarg, params CodeValue[] exprargs) {
      Logger.WriteLine(3, "Transform {0} {1}", relarg, exprargs.Select(e => e.AsEval.Kind.ToString()).ToArray());
      var rel = relarg.Value;
      var exprs = exprargs.Select(e => (e as CodeValue).AsEval).ToArray();
      Logger.Assert(!exprs.Any(e => e.HasFold), "transform folded");
      var heading = DataHeading.Create(exprs);
      var relnew = rel.Transform(heading, exprs);
      Logger.WriteLine(3, "[Tr {0}]", relnew);
      return RelationValue.Create(relnew);
    }

    // Transform plus aggregation
    public RelationValue TransAgg(RelationValue relarg, params CodeValue[] exprargs) {
      Logger.WriteLine(3, "TransAgg {0} {1}", relarg, exprargs.Select(e => e.AsEval.Kind.ToString()).ToArray());
      var rel = relarg.Value;
      var exprs = exprargs.Select(e => e.AsEval).ToArray();
      var heading = DataHeading.Create(exprs);
      var relnew = rel.TransformAggregate(heading, exprs);
      Logger.WriteLine(3, "[TrA {0}]", relnew);
      return RelationValue.Create(relnew);
    }

    // Transform plus ordering
    public RelationValue TransOrd(RelationValue relarg, params CodeValue[] exprargs) {
      Logger.WriteLine(3, "TransOrd {0} {1}", relarg, exprargs.Select(e => e.AsEval.Kind.ToString()).ToArray());
      var rel = relarg.Value;
      var exprs = exprargs.Select(e => (e as CodeValue).AsEval).ToArray();
      var tranexprs = exprs.Where(e => !e.IsOrder).ToArray();
      var orderexps = exprs.Where(e => e.IsOrder).ToArray();
      var heading = DataHeading.Create(tranexprs);
      var relnew = rel.TransformOrdered(heading, tranexprs, orderexps);
      Logger.WriteLine(3, "[TrO {0}]", relnew);
      return RelationValue.Create(relnew);
    }

    // Recursive expansion
    public RelationValue Recurse(RelationValue relarg, NumberValue flags, CodeValue exprarg) {
      Logger.WriteLine(3, "Recurse {0} {1} {2}", relarg, flags, exprarg);

      var relnew = relarg.Value.Recurse((int)flags.Value, exprarg.AsEval);
      Logger.WriteLine(3, "[Rec {0}]", relnew);
      return RelationValue.Create(relnew);
    }

    // Transform tuple
    public TupleValue TransTuple(TupleValue tuparg, params CodeValue[] exprargs) {
      Logger.WriteLine(3, "TransTuple {0} {1}", tuparg, exprargs.Select(e => e.AsEval.Kind.ToString()).ToArray());
      var tup = tuparg.Value;
      var exprs = exprargs.Select(e => (e as CodeValue).AsEval).ToArray();
      var heading = DataHeading.Create(exprs);
      var tupnew = tup.Transform(heading, exprs);
      Logger.WriteLine(3, "[TrT {0}]", tupnew);
      return TupleValue.Create(tupnew);
    }
    
    ///=================================================================
    ///
    /// Dyadic operations
    /// 

    // Dyadic: does Join, Antijoin or Set ops depending on joinop bit flags
    public RelationValue DyadicJoin(RelationValue rel1, RelationValue rel2, NumberValue joparg) {
      var joinop = (JoinOps)joparg.Value;
      var mergeop = (MergeOps)(joinop & JoinOps.MERGEOPS);
      var newheading = DataHeading.Merge(mergeop, rel1.Value.Heading, rel2.Value.Heading);
      Logger.WriteLine(3, "Join {0} {1} n={2} ({3} {4})", rel1, rel2, joparg, mergeop, newheading);

      var rel1res = DataTable.ResolveDyadic(rel1.Value, rel2.Value);
      var relnew = rel1res.DyadicJoin(rel2.Value, joinop, newheading);
      Logger.WriteLine(3, "[J {0}]", relnew);
      return RelationValue.Create(relnew);
    }

    // Dyadic: does Join, Antijoin or Set ops depending on joinop bit flags
    public RelationValue DyadicAntijoin(RelationValue rel1, RelationValue rel2, NumberValue joparg) {
      var joinop = (JoinOps)joparg.Value;
      var mergeop = (MergeOps)(joinop & JoinOps.MERGEOPS);
      var newheading = DataHeading.Merge(mergeop, rel1.Value.Heading, rel2.Value.Heading);
      Logger.WriteLine(3, "Antijoin {0} {1} n={2} ({3} {4})", rel1, rel2, joparg, mergeop, newheading);

      var rel1res = DataTable.ResolveDyadic(rel1.Value, rel2.Value);
      var relnew = rel1res.DyadicAntijoin(rel2.Value, joinop, newheading);
      Logger.WriteLine(3, "[AJ {0}]", relnew);
      return RelationValue.Create(relnew);
    }

    // Dyadic: does Join, Antijoin or Set ops depending on joinop bit flags
    public RelationValue DyadicSet(RelationValue rel1, RelationValue rel2, NumberValue joparg) {
      var joinop = (JoinOps)joparg.Value;
      var mergeop = (MergeOps)(joinop & JoinOps.MERGEOPS);
      var newheading = DataHeading.Merge(mergeop, rel1.Value.Heading, rel2.Value.Heading);
      Logger.WriteLine(3, "Set {0} {1} n={2} ({3} {4})", rel1, rel2, joparg, mergeop, newheading);

      var rel1res = DataTable.ResolveDyadic(rel1.Value, rel2.Value);
      var relnew = rel1res.DyadicSet(rel2.Value, joinop, newheading);
      Logger.WriteLine(3, "[S {0}]", relnew);
      return RelationValue.Create(relnew);
    }

    // For mixed super and subset, ensure that left arg is local
    // Reverse operation if needed

    public BoolValue Subset(RelationValue relarg1, RelationValue relarg2) {
      var ret = (DataTable.CheckDyadic(relarg1.Value, relarg2.Value) == MixedDyadics.RightLocal)
        ? relarg2.Value.Superset(relarg1.Value)
        : relarg1.Value.Subset(relarg2.Value);
      return BoolValue.Create(ret);
    }

    public BoolValue Superset(RelationValue relarg1, RelationValue relarg2) {
      var ret = (DataTable.CheckDyadic(relarg1.Value, relarg2.Value) == MixedDyadics.RightLocal)
        ? relarg2.Value.Subset(relarg1.Value)
        : relarg1.Value.Superset(relarg2.Value);
      return BoolValue.Create(ret);
    }

    public BoolValue Separate(RelationValue relarg1, RelationValue relarg2) {
      var ret = (DataTable.CheckDyadic(relarg1.Value, relarg2.Value) == MixedDyadics.RightLocal)
        ? relarg2.Value.Separate(relarg1.Value)
        : relarg1.Value.Separate(relarg2.Value);
      return BoolValue.Create(ret);
    }

    ///=================================================================
    ///
    /// Update operations
    /// 

    static string ExprShort(ExpressionBlock expr) {
      return string.Format("{0}:{1}[{2}]", expr.Name, expr.Kind, expr.Serial);
    }

    static string ExprShorts(ExpressionBlock[] exprs) {
      return string.Join(",", exprs.Select(e => ExprShort(e).ToArray()));
    }

    // Update Select with predicate and attr exprs
    public VoidValue UpdateTrans(RelationValue rel1, CodeValue predarg, params CodeValue[] exprargs) {
      var exprs = exprargs.Select(e => (e as CodeValue).AsEval).ToArray();
      Logger.WriteLine(3, "UpdateTrans {0} pred={1} exprs=<{2}>", rel1, ExprShort(predarg.AsEval), ExprShorts(exprs));

      var relnew = rel1.Value.UpdateTransform(predarg.AsEval, exprs);
      Logger.WriteLine(3, "[UT]");
      return VoidValue.Default;
    }

    // Update Join with joinop bit flags
    public VoidValue UpdateJoin(RelationValue rel1, RelationValue rel2, NumberValue joparg) {
      var joinop = (JoinOps)joparg.Value;
      Logger.WriteLine(3, "UpdateJoin {0} {1} {2}", rel1, rel2, joinop);

      // note: two different algorithms depending on dyadic status
      rel1.Value.UpJoin(rel2.Value, joinop);
      Logger.WriteLine(3, "[UJ]");
      return VoidValue.Default;
    }

    ///=================================================================
    ///
    /// Logical operations
    /// 

    public BoolValue And(BoolValue arg1, BoolValue arg2) {
      return BoolValue.Create(arg1.Value && arg2.Value);
    }

    public BoolValue Or(BoolValue arg1, BoolValue arg2) {
      return BoolValue.Create(arg1.Value || arg2.Value);
    }

    public BoolValue Xor(BoolValue arg1, BoolValue arg2) {
      return BoolValue.Create(arg1.Value ^ arg2.Value);
    }

    public BoolValue Not(BoolValue arg1) {
      return BoolValue.Create(!arg1.Value);
    }

    // Bitwise overloads
    public NumberValue BitAnd(NumberValue arg1, NumberValue arg2) {
      return NumberValue.Create((Int64)arg1.Value & (Int64)arg2.Value);
    }

    public NumberValue BitOr(NumberValue arg1, NumberValue arg2) {
      return NumberValue.Create((Int64)arg1.Value | (Int64)arg2.Value);
    }

    public NumberValue BitXor(NumberValue arg1, NumberValue arg2) {
      return NumberValue.Create((Int64)arg1.Value ^ (Int64)arg2.Value);
    }

    public NumberValue BitNot(NumberValue arg1) {
      return NumberValue.Create(~(Int64)arg1.Value);
    }

    ///=================================================================
    ///
    /// Arithmetic operations
    /// 

    public NumberValue Neg(NumberValue arg1) {
      return NumberValue.Create(-arg1.Value);
    }
    public NumberValue Add(NumberValue arg1, NumberValue arg2) {
      return NumberValue.Create(arg1.Value + arg2.Value);
    }
    public NumberValue Subtract(NumberValue arg1, NumberValue arg2) {
      return NumberValue.Create(arg1.Value - arg2.Value);
    }
    public NumberValue Multiply(NumberValue arg1, NumberValue arg2) {
      return NumberValue.Create(arg1.Value * arg2.Value);
    }
    public NumberValue Divide(NumberValue arg1, NumberValue arg2) {
      return NumberValue.Create(arg1.Value / arg2.Value);
    }
    public NumberValue Div(NumberValue arg1, NumberValue arg2) {
      return NumberValue.Create(Decimal.Truncate(Decimal.Divide(Decimal.Truncate(arg1.Value), Decimal.Truncate(arg2.Value))));
    }
    public NumberValue Mod(NumberValue arg1, NumberValue arg2) {
      return NumberValue.Create(Decimal.Remainder(Decimal.Truncate(arg1.Value), Decimal.Truncate(arg2.Value)));
    }
    public NumberValue Pow(NumberValue arg1, NumberValue arg2) {
      var v = Math.Pow((double)arg1.Value, (double)arg2.Value);
      return NumberValue.Create((decimal)v);
    }

    // Min/max
    public IOrderedValue Max(IOrderedValue arg1, IOrderedValue arg2) {
      return CheckedLess(arg1, arg2) ? arg2 : arg1;
    }
    public IOrderedValue Min(IOrderedValue arg1, IOrderedValue arg2) {
      return CheckedLess(arg1, arg2) ? arg1 : arg2;
    }

    ///=================================================================
    ///
    /// Comparisons
    /// 
    public BoolValue Eq(TypedValue arg1, TypedValue arg2) {
      return BoolValue.Create(CheckedEqual(arg1, arg2));
    }
    public BoolValue Ne(TypedValue arg1, TypedValue arg2) {
      return BoolValue.Create(!CheckedEqual(arg1, arg2));
    }
    public BoolValue Ge(IOrderedValue arg1, IOrderedValue arg2) {
      return BoolValue.Create(!CheckedLess(arg1, arg2));
    }
    public BoolValue Gt(IOrderedValue arg1, IOrderedValue arg2) {
      return BoolValue.Create(!CheckedLess(arg1, arg2) && !arg1.Equals(arg2));
    }
    public BoolValue Le(IOrderedValue arg1, IOrderedValue arg2) {
      return BoolValue.Create(CheckedLess(arg1, arg2) || arg1.Equals(arg2));
    }
    public BoolValue Lt(IOrderedValue arg1, IOrderedValue arg2) {
      return BoolValue.Create(CheckedLess(arg1, arg2));
    }

    // Match using regex
    public BoolValue Match(TextValue arg1, TextValue arg2) {
      Regex re = new Regex(arg2.Value);
      return BoolValue.Create(re.IsMatch(arg1.Value));
    }

    // Checked Less function common code
    static bool CheckedLess(IOrderedValue arg1, IOrderedValue arg2) {
      Logger.Assert(arg1.DataType.BaseType == arg2.DataType.BaseType);
      return arg1.IsLess(arg2);
    }

    // Checked Equal function common code
    static bool CheckedEqual(TypedValue arg1, TypedValue arg2) {
      Logger.Assert(arg1.DataType.BaseType == arg2.DataType.BaseType);
      return arg1.Equals(arg2);
    }

    ///=================================================================
    ///
    /// Add-in functions
    /// 

    // basic string
    public TextValue Text(TypedValue value) {
      return TextValue.Create(value.ToString());
    }

    // fancier string
    public TextValue Format(TypedValue value) {
      return TextValue.Create(value.Format());
    }

    // special for tables
    public TextValue PrettyPrint(TypedValue value) {
      Logger.WriteLine(3, "PrettyPrint {0}", value);
      var sb = new StringBuilder();
      if (value.DataType is DataTypeRelation) {
        var dss = DataSinkStream.Create(value.AsTable(), new StringWriter(sb));
        dss.OutputTable();
      } else {
        sb.Append(value.DataType.Name + ": " + value.Format());
      }
      var ret = TextValue.Create(sb.ToString());
      Logger.WriteLine(3, "[PP]");
      return ret;
    }

    // Return type name as text
    public TextValue Type(TypedValue arg) {
      return TextValue.Create(arg.DataType.Name);
    }

    // Return cardinality as scalar
    public NumberValue Count(RelationValue arg) {
      return NumberValue.Create(arg.Value.GetCount());
    }

    // Return degree as scalar
    public NumberValue Degree(RelationValue arg) {
      return NumberValue.Create(arg.Value.Degree);
    }

    // relation representing heading
    public RelationValue Schema(RelationValue relarg) {
      var heading = DataHeading.Create("Name:text", "Type:text");
      var table = DataTableLocal.Create(heading);
      foreach (var col in relarg.Value.Heading.Columns) {
        table.AddRow(DataRow.Create(heading, col.Name, col.DataType.Name));
      }
      return RelationValue.Create(table);
    }

    // sequence of integers
    public RelationValue Sequence(NumberValue countarg) {
      var heading = DataHeading.Create("N:number");
      var table = DataTableLocal.Create(heading);
      var n = Decimal.Zero;
      var count = (int)countarg.Value;
      for (var i = 0; i < count; ++i) {
        table.AddRow(DataRow.Create(heading, new TypedValue[] { NumberValue.Create(n) }));
        n += 1;
      }
      return RelationValue.Create(table);
    }

    //--- system tables

    // variables
    public RelationValue All() {
      return _catalog.Catalog.GetCatalogTableValue(Catalog.CatalogTables.Catalog);
    }

    public RelationValue Variables() {
      return _catalog.Catalog.GetCatalogTableValue(Catalog.CatalogTables.Variable);
    }

    public RelationValue Operators() {
      return _catalog.Catalog.GetCatalogTableValue(Catalog.CatalogTables.Operator);
    }

    public RelationValue Members() {
      return _catalog.Catalog.GetCatalogTableValue(Catalog.CatalogTables.Member);
    }

    ///=================================================================
    ///
    /// 

    public BoolValue Bool(TextValue value) {
      if (String.Equals(value.Value, "true", StringComparison.InvariantCultureIgnoreCase)) return BoolValue.True;
      if (String.Equals(value.Value, "false", StringComparison.InvariantCultureIgnoreCase)) return BoolValue.False;
      ProgramError.Error("Convert", "not a valid boolean");
      return BoolValue.Default;
    }

    // Parse string as number
    public NumberValue Number(TextValue value) {
      decimal d;
      if (Decimal.TryParse(value.Value, out d)) return NumberValue.Create(d);
      ProgramError.Error("Convert", "not a valid number");
      return NumberValue.Default;
    }

    // Convert time value to seconds
    public NumberValue NumberT(TimeValue value) {
      return NumberValue.Create(value.Value.Ticks / 10000000.0m);
    }

    public TimeValue Time(TextValue value) {
      DateTime t;
      if (DateTime.TryParse(value.Value, out t)) return TimeValue.Create(t);
      ProgramError.Error("Convert", "not a valid time/date");
      return TimeValue.Default;
    }

    ///=================================================================
    ///
    /// binary operations
    /// 

    public BinaryValue Binary(TypedValue value) {
      if (value.DataType == DataTypes.Text)
        return BinaryValue.Default;   // TODO: conversion
      if (value.DataType == DataTypes.Number) {
        var size = (int)((NumberValue)value).Value;
        return BinaryValue.Create(new byte[size]);
      }
      ProgramError.Error("Binary", "invalid arg type");
      return BinaryValue.Default;
    }

    public NumberValue BinaryLength(BinaryValue arg1) {
      return NumberValue.Create(arg1.Value.Length);
    }
    
    public NumberValue BinaryGet(BinaryValue value, NumberValue index) {
      if (index.Value < 0 || index.Value > value.Value.Length)
        ProgramError.Fatal("Binary", "get index out of range");
      return NumberValue.Create(value.Value[(int)index.Value]);
    }

    public BinaryValue BinarySet(BinaryValue value, NumberValue index, NumberValue newvalue) {
      if (index.Value < 0 || index.Value > value.Value.Length)
        ProgramError.Fatal("Binary", "set index out of range");
      var b = value.Value.Clone() as byte[];
      b[(int)index.Value] = (byte)newvalue.Value;
      return BinaryValue .Create(b);
    }

    ///=================================================================
    ///
    /// Text string operations
    /// 

    // Concatenate. Converts arguments to string.
    public TextValue Concat(TypedValue arg1, TypedValue arg2) {
      return TextValue.Create(arg1.ToString() + arg2.ToString());
    }

    // remove leading and trailing white space
    public TextValue Trim(TextValue arg1) {
      return TextValue.Create(arg1.Value.Trim());
    }

    // Pad to length with spaces, or truncate to length
    public TextValue Left(TextValue arg1, NumberValue arg2) {
      if (arg2.Value < 0) return TextValue.Default;
      var str = arg1.Value;
      var len = (int)arg2.Value;
      var strx = (len >= str.Length) ? str.PadRight(len) : str.Substring(0, len);
      return TextValue.Create(strx);
    }

    // Pad on left with spaces or truncate to right to length
    public TextValue Right(TextValue arg1, NumberValue arg2) {
      if (arg2.Value < 0) return TextValue.Default;
      var str = arg1.Value;
      var len = (int)arg2.Value;
      var strx = (len >= str.Length) ? str.PadLeft(len) : str.Substring(str.Length - len, len);
      return TextValue.Create(strx);
    }

    // Multiple copies of a string to fill a length
    public TextValue Fill(TextValue arg1, NumberValue arg2) {
      if (arg2.Value < 0) return TextValue.Default;
      StringBuilder sb = new StringBuilder();
      var times = ((int)arg2.Value + arg1.Value.Length - 1) / arg1.Value.Length;
      while (times-- > 0)
        sb.Append(arg1.Value);
      return TextValue.Create(sb.ToString(0, (int)arg2.Value));
    }

    // The part of arg1 before arg2, or arg1 if not found
    public TextValue Before(TextValue arg1, TextValue arg2) {
      int pos = arg1.Value.IndexOf(arg2.Value);
      return pos == -1 ? arg1 : TextValue.Create(arg1.Value.Substring(0, pos));
    }

    // The part of arg1 after arg2, or nothing if not found
    public TextValue After(TextValue arg1, TextValue arg2) {
      int pos = arg1.Value.IndexOf(arg2.Value);
      return pos == -1 ? TextValue.Default : TextValue.Create(arg1.Value.Substring(pos + arg2.Value.Length));
    }

    public TextValue ToUpper(TextValue arg1) {
      return TextValue.Create(arg1.Value.ToUpper());
    }

    public TextValue ToLower(TextValue arg1) {
      return TextValue.Create(arg1.Value.ToLower());
    }

    public NumberValue Length(TextValue arg1) {
      return NumberValue.Create(arg1.Value.Length);
    }

    public TimeValue Now() {
      var now = DateTime.Now;
      return TimeValue.Create(now);
    }

    ///=================================================================
    /// Sequential IO
    /// 

    // Write a text value to the console/output
    public VoidValue Write(TextValue line) {
      _evaluator.Output.WriteLine(line.Value);
      return VoidValue.Default;
    }

    // Obtain a text value by reading from the console/standard input output
    public TextValue Read() {
      var input = _evaluator.Input.ReadLine();
      if (input == null) ProgramError.Fatal("Read", "input not available");
      return TextValue.Create(input);
    }

    // optional pause (only when interactive)
    public VoidValue Pause(TextValue value) {
      if (_catalog.Catalog.InteractiveFlag) {
        if (value.Value.Length > 0)
          _evaluator.Output.WriteLine(value.Value);
        _evaluator.Input.ReadLine();
      }
      return VoidValue.Default;
    }

    // trigger an error
    public VoidValue Fail(TextValue source, TextValue message) {
      ProgramError.Error(source.Value, message.Value);
      return VoidValue.Default;
    }

    // maybe trigger a crash
    public VoidValue Assert(BoolValue condition, TextValue message) {
      Logger.Assert(condition.Value, message.Value);
      return VoidValue.Default;
    }

    ///=================================================================
    ///
    /// More types
    /// 

    ///-------------------------------------------------------------------
    /// <summary>
    /// A value that represents a date
    /// </summary>

    public class DateValue : UserValue {
      public static DataTypeUser StaticDatatype { get; private set; }
      public new DateTime Value { 
        get { return (base.Value[0] as TimeValue).Value; }
      }

      // ctor - so we can access base
      DateValue(DateTime value) {
        base.Value = new TypedValue[] { TimeValue.Create(value.Date) };
        _datatype = StaticDatatype;
        _hashcode = CalcHashCode();
      }

      static DateValue() {
        // FIX: better to have a lookup for this
        StaticDatatype = DataTypeUser.Get("date", new DataColumn[] {  DataColumn.Create("super", DataTypes.Time) });
        DataTypes.TypeDict[typeof(DateValue)] = StaticDatatype;
      }

      public static new DateValue Create(DateTime time) {
        return new DateValue(time);
      }

      public static DateValue Create(TimeValue value) {
        return new DateValue(value.Value);
      }
    }

    public DateValue FromTime(TimeValue time) {
      return DateValue.Create(time.Value);
    }
    public DateValue FromYmd(NumberValue year, NumberValue month, NumberValue day) {
      return DateValue.Create(new DateTime((int)year.Value, (int)month.Value, (int)day.Value));
    }

    public TimeValue TimeD(DateValue arg1) { return TimeValue.Create(arg1.Value); }
    public NumberValue Year(DateValue arg1) { return NumberValue.Create(arg1.Value.Year); }
    public NumberValue Month(DateValue arg1) { return NumberValue.Create(arg1.Value.Month); }
    public NumberValue Day(DateValue arg1) { return NumberValue.Create(arg1.Value.Day); }

    public NumberValue DayOfWeek(DateValue arg1) {
      return NumberValue.Create((int)arg1.Value.DayOfWeek);
    }

    public NumberValue DaysDifference(DateValue arg1, DateValue arg2) {
      return NumberValue.Create((int)arg1.Value.Subtract(arg2.Value).TotalDays);
    }
  }
}
