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
using System.Threading.Tasks;

namespace Andl.Runtime {
  /// <summary>
  /// Modes for calculating offsets
  /// </summary>
  public enum OffsetModes {
    None, Lead, Lag, Absolute
  };

  /// <summary>
  /// Segment info for the sort index.
  /// </summary>
  public struct SegmentInfo {
    internal DataType datatype; 
    internal bool descending;
    internal bool grouped;
    internal int columnno;
  }

  /// <summary>
  /// Implements a comparer for the sort
  /// Last entry is ordinal to resolve ties
  /// </summary>
  public class ArrayComparer : IComparer<object[]> {
    public SegmentInfo[] _seginfo;
    public int Compare(object[] first, object[] second) {
      for (int x = 0; x < first.Length; ++x) {
        if (x == first.Length - 1) {
          var diff = (int)first[x] - (int)second[x];
          return diff < 0 ? -1 : diff > 0 ? 1 : 0;
        }
        if (!first[x].Equals(second[x])) {
          var less = ((IOrderedValue)first[x]).IsLess((IOrderedValue)second[x]);
          return less == _seginfo[x].descending ? 1 : -1;
        }
      }
      return 0;
    }
  }

  /// <summary>
  /// Implements an ordering based on a list of segments
  /// </summary>
  public class OrderedIndex {
    public SortedList<object[], int> RowList { get { return _slist; } }
    SortedList<object[], int> _slist;   // index of keys and ordinals
    public SegmentInfo[] _seginfo;      // segment info
    int _groupseg;                      // segment index for grouping (-1 if none)
    int _firstkeyx;                     // index to key that starts a group

    public IEnumerable<int> RowOrdinals {
      get {
        _firstkeyx = 0;
        for (var keyx = 0; keyx < _slist.Count; ++keyx) {
          var key = _slist.Keys[keyx];
          var value = _slist.Values[keyx];
          Logger.WriteLine(5, "rowlist {0} {1}", String.Join(",", key), value);
          if (_groupseg != -1 && !key[_groupseg].Equals(_slist.Keys[_firstkeyx][_groupseg])) // BUG: must compare all prior segs
            _firstkeyx = keyx;
          yield return value;
        }
      }
    }


    // Create from seginfo
    public static OrderedIndex Create(SegmentInfo[] seginfo) {
      var ordi = new OrderedIndex {
        _seginfo = seginfo,
        _slist = new SortedList<object[], int>(new ArrayComparer { _seginfo = seginfo }),
      };
      ordi._groupseg = Enumerable.Range(1, seginfo.Length).LastOrDefault(x => seginfo[x-1].grouped) - 1;
      return ordi;
    }

    // Create from expressions and heading
    public static OrderedIndex Create(ExpressionBlock[] exprs, DataHeading heading) {
      var seginfo = exprs.Select(e => new SegmentInfo {
        datatype = e.DataType,
        descending = e.IsDesc,
        grouped = e.IsGrouped,
        columnno = heading.FindIndex(e.MakeDataColumn()),
      }).ToArray();
      return Create(seginfo);
    }

    // Add key fields from a row to the sorted list, with ord as final segment
    public void Add(DataRow row, int ord) {
      var values = BuildKey(row, ord);
      _slist.Add(values, ord);
    }

    // Calculate an offset from the row and mode, returning the ordinal of some other row
    // Currently this is required to be within the same group (last ordering segment)
    public int Offset(DataRow row, int index, OffsetModes mode) {
      if (index < 0) return -1;
      var key = BuildKey(row, row.Order);
      var xofk = _slist.IndexOfKey(key);
      var xofkx = (mode == OffsetModes.Lead) ? xofk + index 
        : (mode == OffsetModes.Lag) ? xofk - index
        : (mode == OffsetModes.Absolute) ? _firstkeyx + index
        : index;
      if (!(xofkx >= _firstkeyx && xofkx < _slist.Count)) return -1;
      if (_groupseg >= 0) {
        var v1 = _slist.Keys[xofk][_groupseg] as TypedValue;
        var v2 = _slist.Keys[xofkx][_groupseg] as TypedValue;
        if (!v1.Equals(v2)) return -1;  // BUG: must compare all prior segs
      }
      return _slist.Values[xofkx];
    }

    object[] BuildKey(DataRow row, int ord) {
      var values = new object[_seginfo.Length + 1];
      for (var i = 0; i < _seginfo.Length; ++i)
        values[i] = row.Values[_seginfo[i].columnno];
      values[_seginfo.Length] = ord;
      return values;
    }

  }

}
