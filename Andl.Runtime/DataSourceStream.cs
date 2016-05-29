﻿/// Andl is A New Data Language. See andl.org.
///
/// Copyright © David M. Bennett 2015 as an unpublished work. All rights reserved.
///
/// If you have received this file directly from me then you are hereby granted 
/// permission to use it for personal study. For any other use you must ask my 
/// permission. Not to be copied, distributed or used commercially without my 
/// explicit written permission.
///
using System;
using System.Linq;
using Microsoft.VisualBasic.FileIO;
using System.IO;
using Andl.Common;

namespace Andl.Runtime {
  public enum InputMode {
    Preview, Import
  };
  /// <summary>
  /// Generic source of relational data
  /// </summary>
  public class DataSourceStream {
    protected string _locator;
    protected string _ext;
    protected bool _hasid = false;

    // Create an input source of given type and location
    // The locator argument is a path or connection string. The actual filename or table name comes later.
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

    public string GetPath(string filename) {
      if (Path.GetExtension(filename) == String.Empty && _ext != null)
        return Path.Combine(_locator, filename + _ext);
      else return Path.Combine(_locator, filename);
    }

    // default input handlers do nothing

    // peek the file and return a heading
    public virtual DataHeading Peek(string file) {
      return DataHeading.Empty;
    }

    // read the file using the heading
    public virtual DataTable Read(string file, DataHeading heading) {
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

    public override DataHeading Peek(string file) {
      var path = GetPath(file);
      if (!File.Exists(path)) return null;
      using (var rdr = new TextFieldParser(path) {
        TextFieldType = FieldType.Delimited,
        Delimiters = new string[] { "," },
      }) {
        var row = rdr.ReadFields();
        if (_hasid)
          row = (new string[] { "Id:number" })
            .Concat(row).ToArray();
        return DataHeading.Create(row);
      }
    }

    public override DataTable Read(string file, DataHeading heading) {
      var path = GetPath(file);
      if (!File.Exists(path)) return null;
      var table = DataTableLocal.Create(heading);
      using (var rdr = new TextFieldParser(path) {
        TextFieldType = FieldType.Delimited,
        Delimiters = new string[] { "," },
      }) {
        for (var id = 0; !rdr.EndOfData; ++id) {
          var row = rdr.ReadFields();
          if (id > 0) {
            if (_hasid)
              row = (new string[] { id.ToString() })
                .Concat(row).ToArray();
            try {
              table.AddRow(row);
            } catch(Exception ex) {
              throw ProgramError.Fatal("Source Csv", "Error in row {0} of {1}: {2}", id, path, ex.Message);
            }
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

    public override DataHeading Peek(string file) {
      return DataHeading.Create("Line");
    }

    public override DataTable Read(string file, DataHeading heading) {
      var path = GetPath(file);
      if (!File.Exists(path)) return null;
      var newtable = DataTableLocal.Create(heading);
      using (var rdr = File.OpenText(path)) {
        for (var line = rdr.ReadLine(); line != null; line = rdr.ReadLine()) {
          newtable.AddRow(DataRow.Create(heading, line));
        }
      }
      return newtable;
    }
}

/// <summary>
/// Source that is a console (really!)
/// </summary>
public class DataSourceCon : DataSourceStream {
    public static DataSourceCon Create(string locator) {
      return new DataSourceCon {
        _locator = locator,
      };
    }

    public override DataHeading Peek(string file) {
      return DataHeading.Create("line");
    }

    public override DataTable Read(string file, DataHeading heading) {
      var newtable = DataTableLocal.Create(heading);
      Console.WriteLine(file);
      var line = Console.ReadLine();
      newtable.AddRow(DataRow.Create(heading, line));
      return newtable;
    }
  }
}
