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
  public class DataColumn {
    string _name;
    DataType _datatype;
    //int _maxlen = 0;

    // Name of column
    public string Name { get { return _name; } }
    // Type of column
    public DataType DataType { get { return _datatype; } }
    // Heading, in case this is RVA/TVA
    //public DataHeading Heading { get { return _datatype.Heading; } }
    //.public DataHeading Heading { get; private set; }
    // Maximum display length of data in column
    //public int MaxLen { get { return _maxlen; } set { _maxlen = value; } }

    // --- override ----------------------------------------------------

    // equal if name and type are equal
    public override bool Equals(object obj) {
      var other = obj as DataColumn;
      if (!(other != null
        && _name.Equals(other._name)
        //&& _name.Equals(other._name, StringComparison.OrdinalIgnoreCase) 
        && _datatype.Equals(other._datatype)))
      //.if (!(other != null && other._name == _name && _datatype.Equals(other._datatype)))
        return false;
      return true;
    }

    // hash code depends on name and name of type
    public override int GetHashCode() {
      return _name.GetHashCode();
    }

    public override string ToString() {
      return String.Format("{0}:{1}", Name, DataType);
    }

    public string Format() {
      return ToString();
    }

    // --- create ------------------------------------------------------

    // Create column from name[:type[:length]] -- default is string
    public static DataColumn Create(string definition) {
      var split = definition.Split(':');
      return Create(split[0],
        split.Length >= 2 ? split[1] : null);
    }

    // Create column with string typename
    public static DataColumn Create(string name, string typename) {
      return Create(name, DataTypes.Find(typename) ?? DataTypes.Default);
    }

    // Create column with data type
    public static DataColumn Create(string name, DataType datatype) {
      Logger.Assert(datatype != null);
      var newcol = new DataColumn {
        _name = name,
        _datatype = datatype,
      };
      return newcol;
    }

    // --- static

    static bool TestOp(MergeOps a, MergeOps b) { return (a & b) != 0; }

    // merge two sets of columns, matching on equality
    // note that this may include columns with the same name and different types, so create heading will fail
    public static DataColumn[] Merge(MergeOps op, IEnumerable<DataColumn> leftcols, IEnumerable<DataColumn> rightcols) {
      switch (op) {
      case MergeOps.UseAllLeft:
        return leftcols.ToArray();
      case MergeOps.UseAllRight:
        return rightcols.ToArray();
      default:
        var cols1 = leftcols.Where(c => rightcols.Contains(c) ? TestOp(op, MergeOps.Match) : TestOp(op, MergeOps.Left));
        var cols2 = rightcols.Where(c => leftcols.Contains(c) ? false : TestOp(op, MergeOps.Right));
        return cols1.Concat(cols2).ToArray();
      }
    }

    // --- operations

    // Create column by renaming existing
    public DataColumn Rename(string name) {
      return new DataColumn {
        _name = name,
        _datatype = this._datatype,
        //.Heading = this.Heading,
      };
    }

  }
}
