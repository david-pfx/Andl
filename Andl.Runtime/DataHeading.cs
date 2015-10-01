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
  /// Internal type for the heading of a tuple or relation
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

    // Return true if column by this name and columns test equal
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

    // Make an index of columns that are not in another heading
    public int[] MakeExtendIndex(DataHeading other) {
      return Enumerable.Range(0, Degree)
        .Where(x => !other.Contains(Columns[x]))
        .ToArray();
    }

    // Make an index for moving columns from another heading
    // but ignore any columns in the exclude heading
    public int[] MakeMoveIndex(DataHeading other, DataHeading exclude) {
      return Enumerable.Range(0, this.Degree)
        .Where(x => !exclude.Contains(other.Columns[x]))
        .Select(x => other.FindIndex(Columns[x]))
        .Where(y => y >= 0)
        .ToArray();
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

    // --- create ------------------------------------------------------

    // from an existing heading, with normalisation
    public static DataHeading Create(DataHeading heading) {
      return DataTypeTuple.Get(heading).Heading;
    }

    // from existing columns, with normalisation
    public static DataHeading Create(IEnumerable<DataColumn> columns) {
      var dh = new DataHeading() {
        _columns = columns.ToArray()
      };
      dh._coldict = Enumerable.Range(0, dh._columns.Length)
        .ToDictionary(x => dh._columns[x].Name, x => x);
      return Create(dh);
    }

    // from name:types
    public static DataHeading Create(params string[] names) {
      return Create(names.Select(a => DataColumn.Create(a)));
    }

    // from expressions
    public static DataHeading Create(IEnumerable<ExpressionBlock> exprs) {
      return Create(exprs.Select(e => DataColumn.Create(e.Name, e.DataType)));
    }

    // from existing with renames applied
    // existing heading order must be preserved, so use direct lookup
    // TODO: newheading requires normalisation???
    public DataHeading Rename(IEnumerable<ExpressionBlock> exprs) {
      var dict = exprs.ToDictionary(e => e.OldName);
      return Create(this._columns
        .Select(c => dict.ContainsKey(c.Name) ? c.Rename(dict[c.Name].Name) : c));
    }

    // from existing with extends applied
    public DataHeading Extend(IEnumerable<DataColumn> newcols) {
      return Create(this._columns.Concat(newcols));
    }

    // from existing with some removed applied
    public DataHeading Minus(IEnumerable<DataColumn> notcols) {
      return Create(this._columns.Except(notcols));
    }

    // from existing with some in common
    public DataHeading Union(IEnumerable<DataColumn> notcols) {
      return Create(this._columns.Union(notcols));
    }

    public static DataHeading Merge(MergeOps op, DataHeading left, DataHeading right) {
      return Create(DataColumn.Merge(op, left.Columns, right.Columns));
    }

    // form union of two headings
    public DataHeading Union(DataHeading other) {
      return Create(this._columns.Union(other.Columns).ToArray());
    }

    // form intersection of two headings.
    public DataHeading Intersect(DataHeading other) {
      return Create(this._columns.Intersect(other.Columns).ToArray());
    }

    // form difference of two headings.
    public DataHeading Minus(DataHeading other) {
      return Create(this._columns.Except(other.Columns).ToArray());
    }

    // form bi-difference of two headings.
    public DataHeading BiDifference(DataHeading other) {
      var u = this._columns.Union(other.Columns);
      var i = this._columns.Intersect(other.Columns);
      return Create(u.Except(i).ToArray());
    }

    // return a new heading with only those columns with names passed in
    public DataHeading Only(params string[] names) {
      var newcols = Columns
        .Where(c => names.Contains(c.Name)).ToArray();
      Logger.Assert(newcols.Length == names.Length, "length");    // misspelling will trigger this
      return DataHeading.Create(newcols);
    }

    // return a new heading omitting those columns with names passed in
    public DataHeading AllBut(params string[] names) {
      var newcols = Columns
        .Where(c => !names.Contains(c.Name)).ToArray();
      Logger.Assert(newcols.Length == Columns.Length - names.Length, "length");    // misspelling will trigger this
      return DataHeading.Create(newcols);
    }

    // return a new heading with additonal columns from those passed in
    public DataHeading Add(DataColumn[] newcols) {
      return DataHeading.Create(Columns.Union(newcols).ToArray());

    }

  }
}
