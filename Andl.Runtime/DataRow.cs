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
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace Andl.Runtime {
  /// <summary>
  /// Implement a row of data, consististing of a heading and an ordered array of values.  
  /// Immutable. Hash code based on value and computed on creation.
  /// Created as needed from table, so that the heading will be correct after renames.
  /// Values accessed implicitly, by column order.
  /// </summary>
  public class DataRow : ILookupValue {
    public static DataRow Empty {
      get { return DataRow.Create(DataHeading.Empty, new TypedValue[0]); }
    }

    // Tuple heading: order of columns and types are reliable but names could have changed
    DataHeading _heading;
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
    public DataHeading Heading { get { return _heading; } }
    public TypedValue[] Values { get { return _values; } }
    public DataTypeTuple DataType { get { return DataTypeTuple.Get(_heading); } }

    // OBS:lookups?
    public ILookupValue PreLookup { get; set; }
    public ILookupValue PostLookup { get; set; }

    // Compare rows for equality
    // Must assume different order and provide general result. Callers comparing rows
    // in the same table should not use this.
    public override bool Equals(object obj) {
      var other = obj as DataRow;
      if (other == this) return true;
      if (other == null || other.GetHashCode() != _hashcode || !other.Heading.Equals(_heading))
        return false;
      for (var i = 0; i < _values.Length; ++i) {
        var x = _heading.FindIndex(other.Heading.Columns[i]);
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
      var ss = Enumerable.Range(0, Degree).Select(x => String.Format("{0}:{1}",_heading.Columns[x].Name, _values[x].Format()));
      return String.Format("{{{0}}}", String.Join(",", ss));
    }

    // internal calculate hash code
    // note: must be independent of column order and not use the heading
    // NOTE: special treatment of null, to allow null values, to allow aggregation
    int CalcHashCode() {
      var hash = 0;
      for (var i = 0; i < Degree; ++i) {
        var code = (_values[i] == null) ? 0 : _values[i].GetHashCode();
        hash ^= code;
      }
      return hash;
    }

    /// <summary>
    /// Implement ILookupValue for a data row
    /// </summary>
    public bool LookupValue(string name, ref TypedValue value) {
      if (PreLookup != null && PreLookup.LookupValue(name, ref value))
        return true;
      var x = _heading.FindIndex(name);
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

    ///=================================================================
    ///
    /// Fluent functions, return (possibly new) row as result

    public static DataRow Create(DataHeading newheading, TypedValue[] values) {
      if (values.Length != newheading.Degree) throw new ArgumentOutOfRangeException("values", "wrong degree");
      var dr = new DataRow() { 
        _heading = newheading,
        _values = values,
      };
      dr._hashcode = dr.CalcHashCode();
      return dr;
    }

    public static DataRow Create(DataHeading newheading, params string[] values) {
      var newvalues = values
        .Select((v, x) => TypedValue.Parse(newheading.Columns[x].DataType, v))
        .ToArray();
      return DataRow.Create(newheading, newvalues);
    }

    // Create row by evaluating an expression list
    public static DataRow Create(DataHeading newheading, ExpressionEval[] exprs) {
      var newvalues = newheading.Columns
        .Select(c => exprs.First(e => e.Name == c.Name).Evaluate())
        .ToArray();
      return DataRow.Create(newheading, newvalues);
    }

    // Create new row from this one using a new set of values
    public DataRow Create(TypedValue[] values) {
      return DataRow.Create(_heading, values);
    }

    // Update existing row to match parent table
    // Note: does not affect hash code
    public DataRow Update(DataTable table, int ord) {
      Logger.Assert(table.Heading.Degree == Degree, "length");
      Parent = table;
      _heading = table.Heading;
      Order = ord;
      return this;
    }

    // Update existing row by replacing values
    // Note: does affect hash code -- DO NOT USE ON ENTRY IN DICT
    public DataRow Update(TypedValue[] values) {
      Logger.Assert(values.Length == Degree, "length");
      _values = values;
      _hashcode = CalcHashCode();
      return this;
    }

    // Update existing row by replacing values
    // Note: does affect hash code -- DO NOT USE ON ENTRY IN DICT
    public DataRow Update(DataRow other) {
      return Update(other._values);
    }

    // new row, merge invidivual values from expressions
    // TODO: do not use
    public DataRow Merge(ExpressionEval[] exprs) {
      var values = _values.Clone() as TypedValue[]; //??
      foreach (var expr in exprs)
        values[Heading.FindIndex(expr.Name)] = expr.EvalOpen(this);
      return Create(_heading, values);
    }

    // new row, merge values using index (on this). Empty fields still have nulls.
    // TODO: do not use
    public DataRow Merge(DataRow other, int[] index) {
      var values = _values.Clone() as TypedValue[]; //??
      for (int i = 0; i < index.Length; ++i)
        if (index[i] >= 0)
          values[i] = other.Values[index[i]];
      return Create(_heading, values);
    }

    // Test for match of fields according to index (on this)
    public bool IsMatch(DataRow other, int[] index) {
      for (var i = 0; i < index.Length; ++i) {
        if (index[i] >= 0) {
          Logger.Assert(Heading.Columns[i].Equals(other._heading.Columns[index[i]]));
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
      var value = (ord == -1) ? expr.DataType.Default()
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

    // Merge aggregated fields from old row lookup and accumulators
    // Create new row
    public DataRow Create(DataRow lookup, AccumulatorBlock accblk, ExpressionEval[] exprs) {
      return Create(AccumulateValues(lookup, accblk, exprs));
    }

    // Project this row onto a new heading using the move index
    public DataRow Project(DataHeading newheading, int[] movendx) {
      Logger.Assert(movendx.Length == newheading.Degree, "degree");
      var values = Enumerable.Range(0, movendx.Length).Select(x => _values[movendx[x]]).ToArray();
      return DataRow.Create(newheading, values);
    }

    // Create new row from this one using index (on new). Empty fields have nulls.
    public DataRow Transform(DataHeading newheading, int[] index) {
      var values = new TypedValue[newheading.Degree];
      for (int i = 0; i < index.Length; ++i)
        if (index[i] >= 0)
          values[i] = Values[index[i]];
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
