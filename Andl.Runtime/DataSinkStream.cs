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
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Andl.Runtime {
  public class DataSinkStream {
    const string _delim = ", ";
    const string _colsep = " | ";
    const string _rowsep = "-";
    const string T0 = " | \n---";
    const string T1 = " | \n---\n | ";
    const int MaxColumnWidth = 60;
    const int DefaultColumnWidth = 10;
    public int MaxWidth { get; private set; }
    DataTable _table;
    TextWriter _writer;
    int _width = 0; 
    public List<string> Lines { get; private set; }
    
    // Minimum column widths for display purposes
    static readonly Dictionary<string, int> _default_sizes = new Dictionary<string, int>() {
      { "bool",     5 },
      { "number",   6 },
      { "time",    10 },
      { "text",     5 },
      { "binary",  20 }
    };

    public static DataSinkStream Create(DataTable table, TextWriter writer = null) {
      var ds = new DataSinkStream() { 
        _table = table,
        _writer = writer ?? Console.Out,
        MaxWidth = 79,
        Lines = new List<string>(),
      };
      return ds;
    }

    void WriteLine(string line) {
      _width = Math.Max(_width, line.Length);
      Lines.Add(line);
    }

    public void OutputList() {
      foreach (var row in _table.GetRows()) {
        var hc = _table.Heading.Columns;
        for (var i = 0; i < _table.Heading.Degree; ++i) {
          var dv = row.GetValues();
          WriteLine(String.Format("{0}{1}: {2}", (i == 0 ? "" : "  "), hc[i].Name, dv[i]));
        }
      }
      foreach (var line in Lines)
        _writer.WriteLine(line);
    }

    public void OutputCsv() {
      WriteLine(String.Join(_delim, _table.Heading.Columns.Select(c => c.Name)));
      foreach (var row in _table.GetRows()) {
        WriteLine(String.Join(_delim, (row.GetValues())));
      }
      foreach (var line in Lines)
        _writer.WriteLine(line);
    }

    public void OutputTable() {
      WriteTable();
      foreach (var line in Lines)
        _writer.WriteLine(line);
    }

    // Internal table writer, called recursively
    void WriteTable() {
      if (_table.Degree == 0) {
        WriteLine(_table.GetCount() == 0 ? T0 : T1);
        return;
      }
      var rowvalues = new List<object[]>();
      int[] widths = _table.Heading.Columns.Select(c => ColumnWidth(c)).ToArray();
      bool[] rjust = _table.Heading.Columns.Select(c => RightJustify(c.DataType.Name)).ToArray();

      foreach (var row in _table.GetRows()) {
        var rowval = new object[_table.Degree];
        for (var col = 0; col < _table.Degree; ++col) {
          var dtype = _table.Heading.Columns[col].DataType;
          var value = row.Values[col];
          if (dtype is DataTypeRelation) {
            var sink = DataSinkStream.Create(value.AsTable());
            sink.WriteTable();
            rowval[col] = sink;
            widths[col] = Math.Max(widths[col], sink._width);
          } else {
            var val = value.ToString();
            if (val.Length > MaxColumnWidth) {
              var s = String.Format("(...{0})", val.Length);
              val = val.Substring(0, MaxColumnWidth - s.Length) + s;
            }
            rowval[col] = val;
            widths[col] = Math.Max(widths[col], val.Length);
          }
        }
        rowvalues.Add(rowval);
      }

      WriteLine(String.Join(_colsep,
        _table.Heading.Columns.Zip(widths, (c, w) => c.Name.PadRight(w))));
      WriteLine(new string(_rowsep[0], widths.Sum() + (widths.Length - 1) * _colsep.Length));

      foreach (var rowvalue in rowvalues) {
        var more = true;
        for (int subrow = 0; more; subrow++) {
          more = false;
          string[] vv = new string[_table.Degree];
          for (int col = 0; col < _table.Degree; ++col) {
            if (rowvalue[col] is string) {
              vv[col] = (subrow == 0) ? rowvalue[col] as string : "";
            } else {
              var dss = rowvalue[col] as DataSinkStream;
              vv[col] = (subrow < dss.Lines.Count) ? dss.Lines[subrow] : "";
              if (subrow + 1 < dss.Lines.Count) more = true;
            }
          }
          WriteLine(String.Join(_colsep, Enumerable.Range(0, vv.Length).Select(x => PadToWidth(vv[x], widths[x], rjust[x]))));
        }
      }
    }
    
    // Get default column width info for Column
    int ColumnWidth(DataColumn column) {
      int def = _default_sizes.ContainsKey(column.DataType.Name) ? _default_sizes[column.DataType.Name] 
        : DefaultColumnWidth;
      return Math.Max(def, column.Name.Length);
    }

    string PadToWidth(string field, int length, bool rj) {
      if (rj)
        return (field.Length > length) ? field.Remove(0, field.Length - length)
          : field.PadLeft(length);
      else return (field.Length > length) ? field.Substring(0, length)
        : field.PadRight(length);
    }

    bool RightJustify(string name) {
      return (name == "time" || name == "number");
    }
  }
}
