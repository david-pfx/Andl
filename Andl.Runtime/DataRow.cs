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
using Andl.Common;

namespace Andl.Runtime {
  /// <summary>
  /// Implement a row of data, consististing of a heading and an ordered array of values.  
  /// Immutable. Hash code based on value and computed on creation.
  /// 
  /// NOTE: values are ordered as per the heading, but heading order is not preserved 
  /// for tuples. Entry points that take a list or values must respect this. 
  /// Two strategies.
  /// 
  /// 1. Where the argument is a heading and a set of values, the heading must be non-tuple.
  ///    To create a tuple the heading is converted and the values are reordered.
  /// 2. Where the argument is a heading and a set of expressions, the expressions are
  ///    re-ordered to match the final heading.
  /// </summary>
  public class DataRow : ILookupValue {
    public static DataRow Empty {
      get { return DataRow.Create(DataHeading.Empty, new TypedValue[0]); }
    }

    // Specified heading. Required.
    public DataHeading Heading { get; protected set; }
    // Data type. Must match Heading or be null.
    public DataTypeTuple DataType { get; protected set; }
    
    // Actual values: order and types given by heading
    TypedValue[] _values;
    // Hash code calculated from values
    int _hashcode;
    // Ordinal value in table, set when retrieved from table
    public int Order { get; private set; }
    // Parent table, set when retrieved from table
    public DataTable Parent { get; private set; }
    // Information required for row ordinal functions
    public OrderedIndex OrderedIndex { get; set; }

    public int Degree { get { return _values.Length; } }
    public TypedValue[] Values { get { return _values; } }

    // OBS:lookups?
    public ILookupValue PreLookup { get; set; }
    public ILookupValue PostLookup { get; set; }

    // Compare rows for equality
    // Must assume different order and provide general result. Callers comparing rows
    // in the same table should not use this.
    public override bool Equals(object obj) {
      var other = obj as DataRow;
      if (other == this) return true;
      if (other == null || other.GetHashCode() != _hashcode || !other.Heading.Equals(Heading))
        return false;
      for (var i = 0; i < _values.Length; ++i) {
        var x = Heading.FindIndex(other.Heading.Columns[i]);
        if (!_values[x].Equals(other._values[i])) return false;
      }
      return true;
    }

    // override hash code
    public override int GetHashCode() {
      Logger.Assert(_hashcode == CalcHashCode(), "datarow hashcode");
      return _hashcode; 
    }

    public override string ToString() {
      return string.Format("({0})", String.Join(", ", GetValues()));
    }

    public string Format() {
      var ss = Enumerable.Range(0, Degree).Select(x => String.Format("{0}:{1}",Heading.Columns[x].Name, _values[x].Format()));
      return String.Format("{{{0}}}", String.Join(",", ss));
    }

    // internal calculate hash code
    // note: must be independent of column order and not use the heading; no null values
    int CalcHashCode() {
      var hash = 0;
      for (var i = 0; i < Degree; ++i) {
        var code = _values[i].GetHashCode();
        hash ^= code;
      }
      return hash;
    }

    string ShowHashCode() {
      return String.Format("[{0}=>{1}]", String.Join(",", _values.Select(v => v.GetHashCode().ToString())), CalcHashCode());
    }

    /// <summary>
    /// Implement ILookupValue for a data row
    /// </summary>
    public bool LookupValue(string name, ref TypedValue value) {
      if (PreLookup != null && PreLookup.LookupValue(name, ref value))
        return true;
      var x = Heading.FindIndex(name);
      if (x >= 0)
        value = _values[x];
      else if (PostLookup != null && PostLookup.LookupValue(name, ref value))
        return true;
      return x >= 0;
    }

    // Return all the values as an array of strings
    public string[] GetValues() {
      return _values.Select(v => v.Format()).ToArray();
    }

    // Get a value (or null) given a column
    public TypedValue GetValue(DataColumn column) {
      var x = Heading.FindIndex(column);
      return (x == -1) ? null : Values[x];
    }

    ///=================================================================
    ///
    /// Creation functions
    /// 
    /// The primary purpose of a DataRow is as a row of a table. Such a row must have the same heading
    /// and the same attribute order as the table. It ensures this by conforming to the DataTypeTuple.
    /// Values are passed in with a heading that has the same attributes but perhaps in a different order, 
    /// and must then be put in the correct order.
    /// 
    /// One exception is a heading known to come from the same table. No reordering necessary.
    /// The other exception is a row used as a function argument. The value order must be preserved.
    /// 

    // Create a row that has no data type and can be used as an argument
    public static DataRow CreateNonTuple(DataHeading heading, TypedValue[] values) {
      if (values.Length != heading.Degree) throw new ArgumentOutOfRangeException("values", "wrong degree");
      Logger.Assert(values.All(v => v != null), "null value");
      Logger.Assert(!heading.IsTuple, "istuple");
      var dr = new DataRow() {
        Heading = heading,
        DataType = null,
        _values = values,
      };
      dr._hashcode = dr.CalcHashCode();
      dr.Heading.CheckValues(dr.Values);
      return dr;
    }

    // Create a row that belongs to a table. 
    // Can be called with a tuple or non-tuple heading. If the latter, reorder values to match tuple heading.
    public static DataRow Create(DataHeading heading, TypedValue[] values) {
      if (values.Length != heading.Degree) throw new ArgumentOutOfRangeException("values", "wrong degree");
      Logger.Assert(values.All(v => v != null), "null value");
      var newheading = (heading.IsTuple) ? heading : DataTypeTuple.Get(heading).Heading;
      var dr = new DataRow() {
        DataType = DataTypeTuple.Get(heading),
        Heading = newheading,
        _values = (heading.IsTuple) ? values : newheading.Columns.Select(c => values[heading.FindIndex(c)]).ToArray(),
      };
      dr._hashcode = dr.CalcHashCode();
      dr.Heading.CheckValues(dr.Values);
      return dr;
    }

    // Create a row from a set of string values
    public static DataRow Create(DataHeading heading, params string[] values) {
      if (values.Length != heading.Degree) throw new ArgumentOutOfRangeException("values", "wrong degree");
      var newvalues = values
        .Select((v, x) => TypedValue.Parse(heading.Columns[x].DataType, v))
        .ToArray();
      return DataRow.Create(heading, newvalues);
    }

    // Create row by evaluating an expression list
    public static DataRow Create(DataHeading heading, ExpressionEval[] exprs) {
      var newvalues = heading.Columns
        .Select(c => exprs.First(e => e.Name == c.Name).Evaluate())
        .ToArray();
      return DataRow.Create(heading, newvalues);
    }

    // Create row by merging two other rows onto a heading
    public static DataRow Create(DataHeading heading, DataRow row1, DataRow row2) {
      var newvalues = heading.Columns.Select(c => row1.GetValue(c) ?? row2.GetValue(c));
      return DataRow.Create(heading, newvalues.ToArray());
    }

    // Update existing row to match parent table
    // Note: does not affect hash code
    public DataRow Update(DataTable table, int ord) {
      Logger.Assert(table.Heading.Degree == Degree, "length");
      Parent = table;
      Order = ord;
      return this;
    }

    // Update existing row by replacing values
    // Note: does affect hash code -- REMOVE FROM DICT BEFORE USE
    public DataRow Update(TypedValue[] values) {
      Logger.Assert(values.Length == Degree, "length");
      _values = values;
      _hashcode = CalcHashCode();
      return this;
    }

    // Update existing row by replacing values
    // Note: does affect hash code -- REMOVE FROM DICT BEFORE USE
    public DataRow Update(DataRow other) {
      return Update(other._values);
    }

    // New set of values, by merging another row using two complementary indexes
    public TypedValue[] MergeValues(int[] index, DataRow other, int[] oindex) {
      Logger.Assert(index.Length == oindex.Length);
      var values = new TypedValue[index.Length];
      for (int i = 0; i < index.Length; ++i) {
        values[i] = (index[i] >= 0) ? _values[index[i]] : other._values[oindex[i]];
      }
      return values;
    }

    // Test for match of fields according to index (on this)
    public bool IsMatch(DataRow other, int[] index) {
      for (var i = 0; i < index.Length; ++i) {
        if (index[i] >= 0) {
          Logger.Assert(Heading.Columns[i].Equals(other.Heading.Columns[index[i]]));
          if (!Values[i].Equals(other.Values[index[i]]))
            return false;
        }
      }
      return true;
    }

    // Return value of attribute with tuple indexing
    // FIX: needs to get from this row to some other row via parent table to use as lookup
    public TypedValue ValueOffset(ExpressionEval expr, int index, OffsetModes mode) {
      var parent = Parent as DataTableLocal;
      Logger.Assert(parent != null);
      var ord = OrderedIndex.Offset(this, index, mode);
      var value = (ord == -1) ? expr.DataType.DefaultValue()
        : expr.EvalOpen(parent.GetRow(ord));
      return value;
    }

    // Return ordinal value for row, optionally within group
    public NumberValue Ordinal(bool isgroup) {
      var ret = isgroup ? OrderedIndex.Offset(this, 0, OffsetModes.Absolute) : Order;
      return NumberValue.Create(ret);
    }

    ///=================================================================
    ///
    /// Row operations
    /// 
    /// All operators return a row
    /// 

    // Merge aggregated fields from old row lookup and accumulators into a new set of values
    public TypedValue[] AccumulateValues(DataRow lookup, AccumulatorBlock accblk, ExpressionEval[] exprs) {
      var values = _values.Clone() as TypedValue[];
      foreach (var x in Enumerable.Range(0, exprs.Length)) {
        if (exprs[x].HasFold)
          values[x] = exprs[x].EvalHasFold(lookup, accblk);
      }
      return values;
    }

    // Merge aggregated fields from old row, lookup and accumulators
    // In situ, so must update values properly
    public DataRow Merge(DataRow lookup, AccumulatorBlock accblk, ExpressionEval[] exprs) {
      return Update(AccumulateValues(lookup, accblk, exprs));
    }

    // Project this row onto a new heading using the move index
    public DataRow Project(DataHeading newheading, int[] movendx) {
      Logger.Assert(movendx.Length == newheading.Degree, "degree");
      var values = Enumerable.Range(0, movendx.Length).Select(x => _values[movendx[x]]).ToArray();
      return DataRow.Create(newheading, values);
    }

    // Create new row from this one by evaluating expressions (extend/project)
    // Evaluates everything, but folds will just get default value (no accumulation)
    public DataRow Transform(DataHeading newheading, IEnumerable<ExpressionEval> exprs) {
      var newvalues = exprs
        .Select(e => e.EvalOpen(this))
        .ToArray();
      return DataRow.Create(newheading, newvalues);
    }

    // Create new row from this one by evaluating expressions (extend/project/transform ordered)
    // Accumulator block is updated by call to Fold()
    public DataRow TransformAggregate(DataHeading newheading, AccumulatorBlock accblock, IEnumerable<ExpressionEval> exprs) {
      var newvalues = exprs
        .Select(e => e.HasFold ? e.EvalHasFold(this, accblock) : e.EvalOpen(this))
        .ToArray();
      return DataRow.Create(newheading, newvalues);
    }

  }
}
