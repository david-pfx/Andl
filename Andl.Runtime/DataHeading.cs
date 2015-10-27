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
using System.Text;
using System.Threading.Tasks;

namespace Andl.Runtime {
  // Note: LMR must be numerically same as JoinOps
  [Flags]
  public enum MergeOps {
    Nul = 0, Left = 1, Match = 2, Right = 4,
    Union = 7, NotMatch = 5, 
    UseAllLeft = 3,
    UseAllRight = 6
  }

  /// <summary>
  /// Internal type for the heading of a tuple, relation, user type or function argument
  /// 
  /// For tuple and relation, istuple is true and column order is not preserved.
  /// For user type and function argument, istuple is false and column order is preserved.
  /// </summary>
  public class DataHeading {
    public static DataHeading Empty;
    static DataHeading()  {
      Empty = DataHeading.Create(new DataColumn[0]);
    }
      
    DataColumn[] _columns;
    Dictionary<string, int> _coldict = new Dictionary<string, int>();

    public int Degree { get { return _columns.Length; } }
    public DataColumn[] Columns { get { return _columns; } }
    // True if this heading is for a tuple type, so column order may have changed
    public bool IsTuple { get; private set; }

    // overrides -------------------------------------------------------
    public override bool Equals(object obj) {
      var other = obj as DataHeading;
      if (other == null || other.GetHashCode() != GetHashCode() || other.Degree != Degree)
        return false;
      foreach (var col in other.Columns)
        if (!Contains(col))
          return false;
      return true;
    }

    public override int GetHashCode() { /// TODO: cache hash code
      var h = Degree;
      // note: must not rely on order of dict
      for (var i = 0; i < Degree; ++i)
        h = h ^ _columns[i].GetHashCode();
      return h;
    }

    public override string ToString() {
      var s = String.Join(",", _columns.Select(c => c.ToString()).ToArray());
      return "{" + s + "}";
      //return String.Format("{{{0}}}", s);
    }

    internal string Format() {
      var s = String.Join(",", _columns.Select(c => c.Format()).ToArray());
      return "{" + s + "}";
    }

    // Return true if has column of same name and type
    public bool Contains(DataColumn column) {
      int col;
      var ok = _coldict.TryGetValue(column.Name, out col);
      return ok && column.Equals(_columns[col]);
    }

    // Return true if this heading contains other
    public bool Contains(DataHeading other) {
      return other.Columns.All(c => this.Contains(c));
    }

    // Returns true if headings have same columns in same order
    public bool EqualInOrder(DataHeading heading) {
      if (!Equals(heading)) return false;
      return Columns.SequenceEqual(heading.Columns);
    }

    // Return column index if found else -1
    public int FindIndex(string column) {
      int col;
      return _coldict.TryGetValue(column, out col) 
        ? col : -1;
    }

    // Return column index if found else -1
    public int FindIndex(DataColumn column) {
      int col;
      return _coldict.TryGetValue(column.Name, out col) && column.Equals(_columns[col])
        ? col : -1;
    }

    // Make an index (on this) where to find our fields in another heading
    // missing fields are -1
    public int[] MakeIndex(DataHeading other) {
      return Enumerable.Range(0, this.Degree)
        .Select(x => other.FindIndex(Columns[x]))
        .ToArray();
    }

    // Make a sort index (on this), just for the fields used
    // Both headers will be the same. Descending fields are negative
    public int[] MakeSortIndex(ExpressionBlock[] exprs) {
      Logger.Assert(exprs.All(e => FindIndex(e.Name) >= 0));
      return exprs.Select(e => FindIndex(e.Name) * (e.IsDesc ? -1 : 1)).ToArray();
    }

    // Reorder expressions to match heading
    public ExpressionEval[] Reorder(ExpressionEval[] exprs) {
      Logger.Assert(exprs.All(e => FindIndex(e.Name) >= 0), "reorder mismatch");
      var newexprs = new ExpressionEval[exprs.Length];
      foreach (var e in exprs)
        newexprs[FindIndex(e.Name)] = e;
      return newexprs;
    }

    // Check that the values provided match this heading
    public void CheckValues(TypedValue[] values) {
      Logger.Assert(values.Length == Degree, "values length");
      Logger.Assert(values.Select((v, x) => v.DataType == Columns[x].DataType).All(b => b), "values type");
    }

    // --- create ------------------------------------------------------

    // from existing columns, with normalisation by default
    public static DataHeading Create(IEnumerable<DataColumn> columns, bool istuple = true) {
      var dh = new DataHeading() {
        _columns = columns.ToArray(),
        IsTuple = istuple,
      };
      dh._coldict = Enumerable.Range(0, dh._columns.Length)
        .ToDictionary(x => dh._columns[x].Name, x => x);
      return (istuple) ? DataTypeTuple.Get(dh).Heading : dh;
    }

    // from name:types
    // created as non-tuple to preserve order, although eventually will be a tuple
    public static DataHeading Create(params string[] names) {
      return Create(names.Select(a => DataColumn.Create(a)), false);
    }

    // from expressions -- assume tuple since caller can track order
    public static DataHeading Create(IEnumerable<ExpressionBlock> exprs) {
      return Create(exprs.Select(e => DataColumn.Create(e.Name, e.DataType)));
    }

    // Merge two tuple headings
    public static DataHeading Merge(MergeOps op, DataHeading left, DataHeading right) {
      return Create(DataColumn.Merge(op, left.Columns, right.Columns));
    }

    //--- new from old

    // from existing with (possibly partial) renames applied
    // existing heading order must be preserved, so use direct lookup
    public DataHeading Rename(IEnumerable<ExpressionBlock> exprs) {
      var dict = exprs.ToDictionary(e => e.OldName);
      return Create(this._columns
        .Select(c => dict.ContainsKey(c.Name) ? c.Rename(dict[c.Name].Name) : c));
    }

    // Form union of this tuple heading and another
    public DataHeading Union(DataHeading other) {
      return Create(this._columns.Union(other.Columns).ToArray());
    }

    // Form intersection of this tuple heading and another
    public DataHeading Intersect(DataHeading other) {
      return Create(this._columns.Intersect(other.Columns).ToArray());
    }

  }
}
