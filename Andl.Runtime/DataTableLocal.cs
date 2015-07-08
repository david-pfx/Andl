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
  /// <summary>
  /// Implements a table consisting of a heading and unordered rows of data.
  /// Mutable, with no duplicates. Hash code based on heading and cardinality.
  /// 
  /// The rows (currently) do contain a heading, but it is ignored. Values are 
  /// in the same order as columns in the table heading. Internal functions 
  /// reference data values directly. Public functions supply a heading when
  /// needed.
  /// </summary>
  public class DataTableLocal : DataTable {
    // Return the row count.
    public int Cardinality { get { return _rows.Count; } }
    // Return the Local flag
    public override bool IsLocal { get { return true; } }

    // List of rows with same heading. May add or delete.
    List<DataRow> _rows = new List<DataRow>();
    // Hash index of all rows. Updated as needed.
    Dictionary<DataRow, int> _dict = new Dictionary<DataRow, int>();

    // OUCH: used by AggOrd to find lookup
    internal DataRow GetRow(int ord) {
      Logger.Assert(_dict[_rows[ord]] == ord);
      return _rows[ord].Update(this, ord);
    }

    // Return each of the rows. The heading is unreliable because of renames and must be replaced.
    public override IEnumerable<DataRow> GetRows() {
      for (var ord = 0; ord < _rows.Count; ++ord) {
        Logger.Assert(_dict[_rows[ord]] == ord);
        _rows[ord].Update(this, ord);
        yield return _rows[ord];
      }
    }

    // overrides -------------------------------------------------------

    // check whether tables are equal
    public override bool Equals(object obj) {
      var other = obj as DataTable;
      if (obj == this) return true;
      if (!Heading.Equals(other.Heading)) return false;
      return IsEqual(other);
    }

    public override int GetHashCode() {
      return Heading.GetHashCode() ^ Cardinality;
    }

    public override string ToString() {
      var v = (Cardinality == 0) ? "()" : _rows[0].ToString();
      return String.Format("{{{0}{1}[{2}]}}", Heading.ToString(), v, Cardinality);
    }

    public override string Format() {
      var v = (Cardinality == 0) ? "()" : String.Join("", _rows.Select(r => r.ToString()));
      return String.Format("{{{0}{1}[{2}]}}", Heading.ToString(), v, Cardinality);
    }

    // factories -------------------------------------------------------

    // Create new empty table
    public new static DataTableLocal Create(DataHeading heading) {
      DataTableLocal newtable = new DataTableLocal() { 
        Heading = heading,
      };
      return newtable;
    }

    // Create new table as a copy (the other might be a different kind)
    public static DataTableLocal Create(DataHeading heading, IEnumerable<DataRow> rows) {
      DataTableLocal newtable = DataTableLocal.Create(heading);
      foreach (var row in rows)
        newtable.AddRow(row);
      return newtable;
    }

    // Create new table and add tuples to it
    public new static DataTableLocal Create(DataHeading heading, IEnumerable<ExpressionEval> exprs) {
      DataTableLocal newtable = DataTableLocal.Create(heading);
      foreach (var expr in exprs)
        newtable.AddRow(expr.Evaluate().AsRow());
      return newtable;
    }

    // Create new table by converting another
    public static DataTableLocal Convert(DataTable other) {
      if (other is DataTableLocal) return other as DataTableLocal;
      else return Create(other.Heading, other.GetRows());
    }

    // Create new table by converting another
    // NOTE: 'this' not used, but allows desired dispatch
    public override DataTable ConvertWrap(DataTable other) {
      return Convert(other);
    }

    // Convert using lookup as context. Used by Invoke.
    public static DataTableLocal Convert(DataTable other, ILookupValue lookup) {
      if (other is DataTableLocal)
        return other as DataTableLocal;
      else {
        Evaluator.Current.PushLookup(lookup);
        var ret = Create(other.Heading, other.GetRows());
        Evaluator.Current.PopLookup();
        return ret;
      }
    }

    ///--- ops ----------------------------------------------------------
    ///
    /// Functions are internal to this class. Any functions that access a row
    /// ignore the heading in that row. It is the responsibility of caller
    /// to make sure data is valid.

    // new row from values provided in the right order
    // discard rows if any nulls. FIX: this may not be right.
    bool AddRaw(DataRow row) {
      Logger.Assert(Enumerable.Range(0, Degree).All(x => row.Heading.Columns[x] == Heading.Columns[x]), "column");
      if (!_dict.ContainsKey(row)) {
        _rows.Add(row);
        _dict.Add(row, _rows.Count - 1);
        Logger.Assert(_dict[_rows[_rows.Count - 1]] == _rows.Count - 1);
        return true;
      }
      return false;
    }

    // Replace values in row
    // Must remove from dict before updating, hash code may change
    void Replace(DataRow row, TypedValue[] values) {
      var ord = _dict[row];
      _dict.Remove(row);
      row.Update(values);
      _dict.Add(row, ord);
    }

    // Replace an old row with a new one
    // Must remove from dict before updating, hash code may change
    void Replace(DataRow rold, DataRow rnew) {
      var ord = _dict[rold];
      _dict.Remove(rold);
      _rows[ord].Update(rnew);
      _dict.Add(rnew, ord);
    }

    // insert new row from values provided in the right order, used for insertion sort
    // Note: index must be built at end
    void InsertRaw(DataRow row, int ord) {
      _rows.Insert(ord, row);
    }

    void ReindexRaw() {
      _dict = new Dictionary<DataRow,int>();
      for (int i = 0; i < _rows.Count; ++i) {
        _dict.Add(_rows[i], i);
      }
    }

    // Delete a row by ordinal (so collection not disturbed)
    // Need to keep the index intact by moving top row down, if it's not top
    void DeleteRaw(int ord) {
      _dict.Remove(_rows[ord]);
      var last = _rows.Count - 1;
      if (ord != last) {      // if it's not the last, move one down
        _rows[ord] = _rows[last];
        _dict[_rows[ord]] = ord;
      }
      _rows.RemoveAt(last);  // and remove last row
    }

    void DeleteRow(DataRow row) {
      DeleteRaw(_dict[row]);
    }

    void AddRowSorted(DataRow other, int[] sortindex) {
      var pos = -1;
      for (var ord = 0; ord < _rows.Count && pos == -1; ++ord) {
        var cmp = 0;
        for (var x = 0; x < sortindex.Length && cmp == 0; ++x) {
          var absx = Math.Abs(sortindex[x]);
          cmp = compare(other.Values[absx] as IOrderedValue, _rows[ord].Values[absx] as IOrderedValue);
          if (sortindex[x] < 0) cmp = -cmp;
        }
        if (cmp == -1) pos = ord;
      }
      InsertRaw(other, pos == -1 ? _rows.Count : pos);
    }

    // Less function common code
    static int compare(IOrderedValue arg1, IOrderedValue arg2) {
      Logger.Assert(arg1 != null && arg2 != null);
      return arg1.Equals(arg2) ? 0
        : arg1.IsLess(arg2) ? -1 : 1;
    }

    // Test for match of fields using index on first
    // They may have different headings (renames) but values match
    public bool Matches(DataRow row1, DataRow row2, int[] index) {
      for (var i = 0; i < index.Length; ++i) {
        if (index[i] >= 0) {
          if (!row1.Values[i].Equals(row2.Values[index[i]]))
            return false;
        }
      }
      return true;
    }

    // Test of set membership
    private bool Contains(DataRow row) {
      return _dict.ContainsKey(row);
    }

    // Find row and return it for updating
    private DataRow Find(DataRow row) {
      int n;
      if (_dict.TryGetValue(row, out n))
        return GetRow(n);
      //.return _rows[n];
      return null;
    }

    // Test of membership of partial row
    bool HasMatch(DataRow row, int[] index) {
      foreach (var r in GetRows())  //TODO:Enumerable
        if (r.IsMatch(row, index))
          return true;
      return false;
    }

    void ClearRows() {
      _rows.Clear();
      _dict.Clear();
    }

    ///=================================================================
    ///
    /// implementations -- common code
    /// 

    // Join via naive cross product and project onto given header
    // Handles anything, but not necessarily optimal
    // Needs to generate extra rows for multiple matches
    DataTableLocal GeneralisedJoin(DataTableLocal other, DataHeading newheading, DataHeading joinhdr) {
      Logger.WriteLine(4, "GenJoin L={0} R={1} new={2} j={3}", this.Heading, other.Heading, newheading, joinhdr);
      var cmpindex = Heading.MakeIndex(other.Heading);
      var thisindex = newheading.MakeIndex(Heading);
      var otherindex = newheading.MakeIndex(other.Heading);

      var newtable = DataTableLocal.Create(newheading);
      foreach (var row1 in this.GetRows()) {  //TODO:Enumerable
        foreach (var row2 in other.GetRows()) {  //TODO:Enumerable
          if (Matches(row1, row2, cmpindex)) {
            var newrow = row1.Transform(newheading, thisindex).Merge(row2, otherindex);
            newtable.AddRow(newrow);
          }
        }
      }
      Logger.WriteLine(4, "[Join={0}]", newtable);
      return newtable;
    }

    // Antijoin via hash index and project onto given header
    DataTableLocal GeneralisedAntijoin(DataTableLocal other, DataHeading newheading, DataHeading joinhdng) {
      Logger.WriteLine(4, "GenAntijoin L={0} R={1} new={2} j={3}", this.Heading, other.Heading, newheading, joinhdng);

      // Build a dictionary on other
      var odict = new Dictionary<DataRow, int>();
      BuildIndex(other, joinhdng, odict);

      // Build each new row based on join heading and if it's not in the odict
      // add row based on newheading to the new table
      var cmpndx = joinhdng.MakeIndex(Heading);
      var movndx = newheading.MakeIndex(Heading);
      var newtable = DataTableLocal.Create(newheading);
      foreach (var row in this.GetRows()) {  //TODO:Enumerable
        var newrow = row.Project(joinhdng, cmpndx);
        if (!odict.ContainsKey(newrow))
          newtable.AddRow(row.Project(newheading, movndx));
      }
      Logger.WriteLine(4, "[Antijoin={0}]", newtable);
      return newtable;
    }

    // Generalised Set via naive cross product
    // Handles all cases, projecting onto common heading, not necessarily optimal
    DataTableLocal GeneralisedSet(DataTableLocal other, DataHeading newheading, JoinOps joinops) {
      Logger.WriteLine(4, "GenSet L={0} R={1} new={2} j={3}", this.Heading, other.Heading, newheading, joinops);

      var ldict = new Dictionary<DataRow, int>();
      var rdict = new Dictionary<DataRow, int>();
      switch (joinops) {
      case JoinOps.MINUS:
      case JoinOps.INTERSECT:
        BuildIndex(other, newheading, rdict);
        break;
      case JoinOps.SYMDIFF:
        BuildIndex(this, newheading, ldict);
        BuildIndex(other, newheading, rdict);
        break;
      }

      var newtable = DataTableLocal.Create(newheading);
      if (joinops == JoinOps.UNION || rdict.Count > 0) {
        var lmovndx = newheading.MakeIndex(Heading);
        foreach (var row in this.GetRows()) {  //TODO:Enumerable
          var newrow = row.Project(newheading, lmovndx);
          var ok = (joinops == JoinOps.MINUS || joinops == JoinOps.SYMDIFF) ? !rdict.ContainsKey(newrow)
            : (joinops == JoinOps.INTERSECT) ? rdict.ContainsKey(newrow)
            : true;
          if (ok)
            newtable.AddRow(newrow);
        }
      }
      if (joinops == JoinOps.UNION || ldict.Count > 0) {
        var rmovndx = newheading.MakeIndex(other.Heading);
        foreach (var row in other.GetRows()) {  //TODO:Enumerable
          var newrow = row.Project(newheading, rmovndx);
          var ok = (joinops == JoinOps.SYMDIFF) ? !ldict.ContainsKey(newrow)
            : true;
          if (ok)
            newtable.AddRow(newrow);
        }
      }
      Logger.WriteLine(4, "[GenSet={0}]", newtable);
      return newtable;
    }

    // Build index on key
    // Note: keys overwrite so only last one left
    // TODO: index with duplicates for join
    void BuildIndex(DataTableLocal table, DataHeading keyhdg, Dictionary<DataRow, int> dict) {
      var ndx = keyhdg.MakeIndex(table.Heading);
      foreach (var row in table.GetRows()) {
        var values = Enumerable.Range(0, keyhdg.Degree).Select(x => row.Values[ndx[x]]).ToArray();
        dict[DataRow.Create(keyhdg, values)] = row.Order;
      }
    }

    // natural join
    private DataTableLocal Join(DataTableLocal other) {
      var newtable = GeneralisedJoin(other, Heading.Union(other.Heading), Heading.Intersect(other.Heading));
      Logger.WriteLine(4, "[Join={0}]", newtable);
      return newtable;
    }

    // add rows in this that if there is no match in the other on join columns
    private DataTableLocal Antijoin(DataTableLocal other) {
      var cmpndx = other.Heading.MakeIndex(Heading);
      var newtable = DataTableLocal.Create(Heading);
      foreach (var row in GetRows()) {  //TODO:Enumerable
        if (!other.HasMatch(row, cmpndx)) {
          newtable.AddRow(row);
        }
      }
      Logger.WriteLine(4, "[NotMatching={0}]", newtable);
      return newtable;
    }

    // add rows in this if there is a match in the other on join columns
    private DataTableLocal Semijoin(DataTableLocal other) {
      var cmpndx = other.Heading.MakeIndex(Heading);
      var newtable = DataTableLocal.Create(Heading);
      foreach (var row in GetRows()) {  //TODO:Enumerable
        if (other.HasMatch(row, cmpndx)) {
          newtable.AddRow(row);
        }
      }
      Logger.WriteLine(4, "[Matching={0}]", newtable);
      return newtable;
    }

    // add rows in this if there is a match in the other on join columns
    private DataTableLocal Divide(DataTableLocal other, DataHeading newheading) {
      var cmpndx = other.Heading.MakeIndex(Heading);
      var movendx = newheading.MakeIndex(Heading);
      var newtable = DataTableLocal.Create(newheading);
      foreach (var row in GetRows()) {  //TODO:Enumerable
        if (other.HasMatch(row, cmpndx))
          newtable.AddRow(row.Project(newheading, movendx));
      }
      Logger.WriteLine(4, "[Matching={0}]", newtable);
      return newtable;
    }

    // Rows from both tables projected on common heading
    private DataTableLocal Union(DataTableLocal other, DataHeading newheading) {
      var rmovendx = newheading.MakeIndex(Heading);
      var lmovendx = newheading.MakeIndex(other.Heading);
      var newtable = DataTableLocal.Create(newheading);
      foreach (var row in this.GetRows())  //TODO:Enumerable
        newtable.AddRow(row.Project(newheading, lmovendx));
      foreach (var row in other.GetRows())  //TODO:Enumerable
        newtable.AddRow(row.Project(this.Heading, rmovendx));
      Logger.WriteLine(4, "[Union={0}]", newtable);
      return newtable;
    }

    // Simpler algorithm when both have same heading
    private DataTableLocal Union(DataTableLocal other) {
      if (!this.Heading.Equals(other.Heading)) throw new EvaluatorException("tables have different headings");
      // for each column in table 1 find its index in table 2
      var newtable = DataTableLocal.Create(this.Heading);
      foreach (var row in this.GetRows())  //TODO:Enumerable
        newtable.AddRow(row);
      foreach (var row in other.GetRows())  //TODO:Enumerable
        newtable.AddRow(row);
      Logger.WriteLine(4, "[Union={0}]", newtable);
      return newtable;
    }

    // set difference
    private DataTableLocal Minus(DataTableLocal other) {
      if (!this.Heading.Equals(other.Heading)) throw new EvaluatorException("tables have different headings");
      var newtable = DataTableLocal.Create(this.Heading);
      foreach (var row in this.GetRows())  //TODO:Enumerable
        if (!other.Contains(row))
          newtable.AddRow(row);
      Logger.WriteLine(4, "[Minus={0}]", newtable);
      return newtable;
    }

    ///=================================================================
    /// 
    /// Public and overridable operations
    /// 

    // Add new row from a row which may be in a different order
    public bool AddRow(DataRow row) {
      Logger.Assert(row.Heading.Equals(Heading), "heading");
      var values = Heading.Columns.Select(c => row.Values[row.Heading.FindIndex(c)]).ToArray();
      return AddRaw(DataRow.Create(Heading, values));
    }

    // Add new row from strings in the right order
    public bool AddRow(params string[] values) {
      return AddRaw(DataRow.Create(Heading, values));
    }

    // Add new row from strings in the right order
    public bool AddRow(params TypedValue[] values) {
      return AddRaw(DataRow.Create(Heading, values));
    }

    ///=================================================================
    ///
    /// Monadic Table operations
    /// 

    // Return a single value from a relation
    // First tuple if more than one, default value if none
    public override TypedValue Lift() {
      if (!(Degree > 0)) return TypedValue.Empty;
      if (!(Cardinality > 0)) return Heading.Columns[0].DataType.Default();
      return _rows[0].Values[0];
    }

    // Project onto named columns
    public override DataTable Project(ExpressionEval[] exprs) {
      var newheading = DataHeading.Create(exprs);
      var newtable = DataTableLocal.Create(newheading);
      foreach (var row in GetRows())  //TODO:Enumerable
        newtable.AddRow(row.Transform(newheading, exprs));
      Logger.WriteLine(4, "[Project={0}]", newtable);
      return newtable;
    }

    // Rename by copy and graft on new heading
    public override DataTable Rename(ExpressionEval[] exprs) {
      // note: this is an explicit heading. Order matters.
      var heading = Heading.Rename(exprs);
      var newtable = DataTableLocal.Create(heading);
      foreach (var row in GetRows())
        newtable.AddRow(DataRow.Create(heading, row.Values));
      Logger.WriteLine(4, "[Rename={0}]", newtable);
      return newtable;
    }

    // Restrict -- new table containing rows that pass the test
    public override DataTable Restrict(ExpressionEval expr) {
      var newtable = DataTableLocal.Create(Heading);
      foreach (var row in GetRows()) {  //TODO:Enumerable
        if (expr.EvalPred(row).Value)
          newtable.AddRow(row);
      }
      Logger.WriteLine(4, "Restrict {0}", newtable);
      return newtable;
    }

    // Transform -- new table containing new columns generated by expressions
    public override DataTable Transform(DataHeading newheading, ExpressionEval[] exprs) {
      Logger.WriteLine(4, "Transform {0} exprs={1}", newheading, exprs.Count());
      Logger.Assert(exprs.Count() == newheading.Degree, "degree");
      var newtable = DataTableLocal.Create(newheading);
      foreach (var row in GetRows()) {  //TODO:Enumerable
        newtable.AddRow(row.Transform(newheading, exprs));
      }
      Logger.WriteLine(4, "[{0}]", newtable);
      return newtable;
    }

    // Transform with Aggregation - transform but with aggregation so different algorithm
    // TODO: tidy up to match Ordered
    public override DataTable TransformAggregate(DataHeading newheading, ExpressionEval[] exprs) {
      Logger.WriteLine(4, "TransformAggregate {0} exprs={1}", newheading, exprs.Length);

      var numacc = exprs.Where(e => e.HasFold).Sum(e => e.AccumCount);
      var newtable = DataTableLocal.Create(newheading);
      // create a dictionary for output records
      var dict = new Dictionary<DataRow, int>();
      var accblks = new List<AccumulatorBlock>();

      foreach (var oldrow in this.GetRows()) {  //TODO:Enumerable
        var temprow = oldrow.Transform(newheading, exprs);
        if (!dict.ContainsKey(temprow)) {
          var accblk = AccumulatorBlock.Create(numacc);
          var newrow = oldrow.TransformAggregate(newheading, accblk, exprs);
          newtable.AddRaw(newrow);
          Logger.Assert(newtable._dict[newtable._rows[newtable.Cardinality - 1]] == newtable.Cardinality - 1);
          dict.Add(temprow, newtable.Cardinality - 1);
          accblks.Add(accblk);
        } else {
          var ord = dict[temprow];
          var newrow = newtable._rows[ord];
          var accblk = accblks[ord];
          newtable.Replace(newrow, oldrow.TransformAggregate(newheading, accblk, exprs));
        }
      }
      Logger.WriteLine(4, "[{0}]", newtable);
      return newtable;
    }

    // Transform with ordered calculations - different algorithm
    // 1. Build index
    // 2. Read input file using index
    // 3. Transform and write output file
    public override DataTable TransformOrdered(DataHeading newheading, ExpressionEval[] exprs, ExpressionEval[] orderexps) {
      Logger.WriteLine(4, "TransformOrdered {0} exprs={1},{2}", newheading, exprs.Count(), orderexps.Count());

      var numacc = exprs.Where(e => e.HasFold).Sum(e => e.AccumCount);
      var newtable = DataTableLocal.Create(newheading);
      var ordidx = OrderedIndex.Create(orderexps, Heading);

      // Build index
      for (var ord = 0; ord < Cardinality; ++ord) { //TODO:Enumerable
        ordidx.Add(GetRow(ord), ord);
      }
      AccumulatorBlock accblk = null;

      // Read in index order, with access to ordering info
      foreach (var ord in ordidx.RowOrdinals) {
        var oldrow = _rows[ord];
        oldrow.OrderedIndex = ordidx;     // so row functions can access it
        //FIX: find a nicer way to test if this is the start of a new group
        //var prevord = ordidx.Offset(oldrow, 1, OffsetModes.Lag);
        //if (prevord == -1)
        if (ordidx.IsBreak)
          accblk = AccumulatorBlock.Create(numacc);
        DataRow newrow = oldrow.TransformAggregate(newheading, accblk, exprs);
        newtable.AddRaw(newrow);
      }
      Logger.WriteLine(4, "[{0}]", newtable);
      return newtable;
    }

    // Recursive expansion
    // Creates new empty table, add seed, join op (only union for now) and expression
    public override DataTable Recurse(int flags, ExpressionEval expr) {
      Logger.WriteLine(4, "Recurse {0} {1}", flags, expr);
      Logger.Assert(expr.DataType == DataType);

      var newtable = DataTableLocal.Create(Heading);
      foreach (var row in _rows)
        AddRaw(row);

      for (var ord = 0; ord < _rows.Count; ++ord) {
        var newrows = expr.EvalOpen(_rows[ord]).AsTable();
        foreach (var row in newrows.GetRows())
          AddRow(row);
      }
      return this;
    }


    ///=================================================================
    ///
    /// Scalar operations
    /// 

    // Count is COUNT(*) -- easy to do here
    public override int GetCount() {
      return Cardinality;
    }

    // True if tables are equal
    // other is a forward only iterator, so count matches.
    public override bool IsEqual(DataTable other) {
      Logger.Assert(Heading.Equals(other.Heading));
      //if (!other.IsRemote && other.Cardinality != Cardinality) return false;
      if (other is DataTableLocal && (other as DataTableLocal).Cardinality != Cardinality) return false;
      var matched = 0;
      foreach (var row in other.GetRows()) {
        if (Contains(row)) {
          if (++matched > Cardinality)
            break;
        } else {
          other.DropRows(); // because of early termination
          return false;
        }
      }
      return matched == Cardinality;
    }

    // True if this is a subset of other
    // other is a forward only iterator, so count matches.
    public override bool Subset(DataTable other) {
      Logger.Assert(Heading.Equals(other.Heading));
      if (other is DataTableLocal && (other as DataTableLocal).Cardinality < Cardinality) return false;
      var matched = 0;
      foreach (var row in other.GetRows()) {
        if (Contains(row) && ++matched == Cardinality) {
          other.DropRows();
          return true;
        }
      }
      return false;
    }

    // True if this is a superset of other
    public override bool Superset(DataTable other) {
      Logger.Assert(Heading.Equals(other.Heading));
      if (other is DataTableLocal && (other as DataTableLocal).Cardinality > Cardinality) return false;
      foreach (var row in other.GetRows()) {
        if (!Contains(row)) {
          other.DropRows();
          return false;
        }
      }
      return true;
    }

    // True if relations are disjunct
    // OPT: loop on smaller table?
    public override bool Separate(DataTable other) {
      Logger.Assert(Heading.Equals(other.Heading));
      foreach (var row in other.GetRows()) {
        if (Contains(row)) {
          other.DropRows();
          return false;
        }
      }
      return true;
    }

    ///=================================================================
    ///
    /// Dyadic Table operations
    /// 

    // Implement Join by dispatch to preferred algorithm
    public override DataTable DyadicJoin(DataTable otherarg, JoinOps joinops, DataHeading newheading) {
      var other = Convert(otherarg);
      // pick off the custom implementations
      switch (joinops) {
      case JoinOps.DIVIDE:
        return Divide(other, newheading);
      case JoinOps.SEMIJOIN:
        return Semijoin(other);
      case JoinOps.RSEMIJOIN:
        return other.Semijoin(this);
      }
      // use generalised fallback
      var joinhdg = Heading.Intersect(other.Heading);
      return (joinops.HasFlag(JoinOps.REV))
        ? other.GeneralisedJoin(this, newheading, joinhdg)
        : GeneralisedJoin(other, newheading, joinhdg);
    }

    // Implement Antijoin by dispatch to preferred algorithm
    public override DataTable DyadicAntijoin(DataTable otherarg, JoinOps joinops, DataHeading newheading) {
      var other = Convert(otherarg);
      // pick off the custom implementations
      switch (joinops) {
      case JoinOps.ANTIJOIN:
        return Antijoin(other);
      case JoinOps.RANTIJOIN:
        return other.Antijoin(this);
      }
      // use generalised fallback
      var joinhdg = Heading.Intersect(other.Heading);
      return (joinops.HasFlag(JoinOps.REV))
        ? other.GeneralisedAntijoin(this, newheading, joinhdg)
        : GeneralisedAntijoin(other, newheading, joinhdg);
    }

    // Implement Set operations by dispatch to preferred algorithm
    public override DataTable DyadicSet(DataTable otherarg, JoinOps joinops, DataHeading newheading) {
      var other = Convert(otherarg);
      if (Heading.Equals(other.Heading) && Heading.Equals(newheading)) {
        // pick off the custom implementations
        switch (joinops) {
        case JoinOps.UNION:
          return Union(other);
        case JoinOps.MINUS:
          return Minus(other);
        case JoinOps.RMINUS:
          return other.Minus(this);
        }
      }
      // use generalised fallback
      return (joinops == JoinOps.RMINUS) 
        ? other.GeneralisedSet(this, newheading, JoinOps.MINUS)
        : GeneralisedSet(other, newheading, joinops);
    }

    ///=================================================================
    /// Updates -- return original table modified
    /// 

    // Update Join
    // Handles Insert and others, but requires common heading
    public override DataTable UpJoin(DataTable otherarg, JoinOps joinops) {
      Logger.WriteLine(4, "UpJoin {0} j={1}", Heading, joinops);
      var other = Convert(otherarg);

      var left = joinops.HasFlag(JoinOps.SETL);
      var common = joinops.HasFlag(JoinOps.SETC);
      var right = joinops.HasFlag(JoinOps.SETR);

      // pass 1 - new rows
      var reladd = DataTableLocal.Create(Heading);
      if (right && left && common) {
        reladd = other;
      } else if (right) {
        foreach (var row in other.GetRows())
          if (!Contains(row))
            reladd.AddRow(row);
      }

      // pass 2 - deletions
      if (left && common) {
      } else if (left) {
        foreach (var row in GetRows())
          if (other.Contains(row))
            DeleteRow(row);
      } else if (common) {
        foreach (var row in GetRows())
          if (!other.Contains(row))
            DeleteRow(row);
      } else {
        ClearRows();
      } 

      // pass 3 - additions
      foreach (var row in reladd.GetRows()) {
        AddRow(row);
      }

      // TODO: update persistence store

      Logger.WriteLine(4, "[UpJoin={0}]", this);
      return this;
    }

    // Update Transform, handles Delete and Update
    public override DataTable UpdateTransform(ExpressionEval pred, ExpressionEval[] exprs) {
      Logger.WriteLine(4, "UpdateTransform {0}", Heading);

      // pass 1 - new rows
      var relins = DataTableLocal.Create(Heading);
      for (var ord = 0; ord < _rows.Count; ) { //TODO:Enumerable
        if (pred.EvalPred(_rows[ord]).Value) {
          if (exprs.Length > 0)
            relins.AddRow(_rows[ord].Transform(Heading, exprs));
          // deleting a row will replace row at ord with a different one, not yet tested
          DeleteRaw(ord);
        } else ord++;
      }
      foreach (var row in relins.GetRows())
        AddRaw(row);

      // TODO: update persistence store

      Logger.WriteLine(4, "[UpSelect={0}]", this);
      return this;
    }

  }
}
