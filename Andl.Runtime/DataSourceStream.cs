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
using Microsoft.VisualBasic.FileIO;
using System.Text.RegularExpressions;
using System.IO;

namespace Andl.Runtime {
  /// <summary>
  /// Generic source of relational data
  /// </summary>
  public class DataSourceStream {
    protected string _locator;
    protected string _ext;
    protected bool _hasid = false;

    // Create parses first part of command string, decides which driver, and passes remainder to it
    public static DataSourceStream Create(string source, string locator) {
      switch (source) {
      case "con":
        return DataSourceCon.Create(locator);
      case "txt":
        return DataSourceFile.Create(locator);
      case "csv":
        return DataSourceCsv.Create(locator);
      case "sql":
        return DataSourceSql.Create(locator);
      case "oledb":
        return DataSourceOleDb.Create(locator);
      case "odbc":
        return DataSourceOdbc.Create(locator);
      default:
        Logger.Assert(false, source);
        break;
      }
      return null;
    }
    //public static DataSourceStream Create(string command) {
    //  var re = new Regex(@"([a-z]*:) *(.*)", RegexOptions.IgnoreCase);
    //  var m = re.Match(command);
    //  var arg = m.Groups[2].Value;
    //  if (m.Success)
    //    switch (m.Groups[1].Value) {
    //    case "con:":
    //      return DataSourceCon.Create(arg);
    //    case "file:":
    //      return DataSourceFile.Create(arg);
    //    case "csv:":
    //      return DataSourceCsv.Create(arg);
    //    case "sql:":
    //      return DataSourceSql.Create(arg);
    //    case "oledb:":
    //      return DataSourceOleDb.Create(arg);
    //    case "odbc:":
    //      return DataSourceOdbc.Create(arg);
    //    //return new DataSourceSql() {
    //      //  _locator = arg
    //      //};
    //    default:
    //      Logger.Assert(false, command);
    //      break;
    //    }
    //  return new DataSourceStream() {
    //    _locator = command,
    //  };
    //}

    public string GetPath(string filename) {
      if (Path.GetExtension(filename) == String.Empty && _ext != null)
        return Path.Combine(_locator, filename + _ext);
      else return Path.Combine(_locator, filename);
    }

    // default input handler does nothing
    public virtual DataTable Input(string file, bool preview = false) {
      return DataTable.Empty;
    }
  }

  /// <summary>
  /// Source that reads CSV files
  /// </summary>
  public class DataSourceCsv : DataSourceStream {
    public static DataSourceCsv Create(string locator) {
      return new DataSourceCsv {
        _locator = locator,
        _ext = ".csv",
      };
    }

    public override DataTable Input(string file, bool preview = false) {
      var table = DataTable.Empty as DataTableLocal;
      var path = GetPath(file);
      if (!File.Exists(path)) return null;
      using (var rdr = new TextFieldParser(path) { 
              TextFieldType = FieldType.Delimited,
              Delimiters = new string[] { "," }, 
            }) {
        for (var id = 0; !rdr.EndOfData; ++id ) {
          var row = rdr.ReadFields();
          if (id == 0) {
            if (_hasid)
              row = (new string[] { "Id:number" })
                .Concat(row).ToArray();
            table = DataTableLocal.Create(DataHeading.Create(row));
            if (preview)
              break;
          } else {
            if (_hasid)
              row = (new string[] { id.ToString() })
                .Concat(row).ToArray();
            table.AddRow(row);
          }
        }
      }
      return table;
    }

  }

  /// <summary>
  /// Source that a serial file
  /// </summary>
  public class DataSourceFile : DataSourceStream {
    public static DataSourceFile Create(string locator) {
      return new DataSourceFile {
        _locator = locator,
        _ext = ".txt",
      };
    }

    public override DataTable Input(string file, bool preview = false) {
      var heading = DataHeading.Create("Line");
      var newtable = DataTableLocal.Create(heading);
      var path = GetPath(file);
      if (!File.Exists(path)) return null;
      if (preview) return newtable;
      using (var rdr = File.OpenText(path)) { 
        for (var line = rdr.ReadLine(); line != null; line = rdr.ReadLine()) {
          newtable.AddRow(DataRow.Create(heading, line));
        }
      }
      return newtable;
    }
  }


  /// <summary>
  /// Source that is a console
  /// </summary>
  public class DataSourceCon : DataSourceStream {
    public static DataSourceCon Create(string locator) {
      return new DataSourceCon {
        _locator = locator,
      };
    }

    public override DataTable Input(string file, bool preview = false) {
      var heading = DataHeading.Create("line");
      var newtable = DataTableLocal.Create(heading);
      if (preview) return newtable;
      Console.WriteLine(file);
      var line = Console.ReadLine();
      newtable.AddRow(DataRow.Create(heading, line));
      return newtable;
    }
  }
}
