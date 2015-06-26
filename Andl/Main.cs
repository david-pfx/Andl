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
using Andl.Compiler;
using Andl.Runtime;
using System.Text.RegularExpressions;

namespace Andl.Main {
  /// <summary>
  /// Mainline for Andl compiler.
  /// 
  /// Handles command line arguments.
  /// </summary>
  class Program {
    static bool _xsw = false; // compile only
    static bool _isw = false; // interactive
    static bool _nsw = true;  // new catalog
    static bool _usw = false; // update catalog
    static bool _ssw = false; // sql
    //static string _persistpattern = @"^[@A-Za-z].*$";
    //static string _databasepattern = @"^[A-Za-z].*$";
    static string _defaultinput = @"test.andl";
    //static string _databasepath = "andltest.store";
    //static string _sqlpath = "andltest.sqlite";
    //static string _catalogname = "andl_catalog";
    //static string _sqlpath = "Chinook_Sqlite.sqlite";
    static Catalog _catalog;
    static Evaluator _evaluator;

    static List<string> _paths = new List<string>();
    static string _help = "Andl [<input path> [<database path>]] options\n"
      + "\t\tDefault is compile only with new catalog and local database\n"
      + "\t/c[nu]\tUse existing catalog, n for new, u for update\n"
      + "\t/i\tInteractive, execute one line at a time\n"
      + "\t/x\tExecute after compilation\n"
      + "\t/s\tUse Sql to access the database\n"
      + "\t/1\tListing to console\n"
      + "\t/2\tShow generated SQL\n"
      + "\t/3\tShow generated VM code\n"
      + "\t/4+\tMore detailed debug\n";

    static void Main(string[] args) {
      Logger.Open(0);   // no default logging
      Logger.WriteLine("Andl 1.0");
      for (var i = 0; i < args.Length; ++i) {
        if (args[i].StartsWith("/") || args[i].StartsWith("-")) {
          if (!Option(args[i].Substring(1))) {
            return;
          }
        } else _paths.Add(args[i]);
      }
      try {
        if (!Start())
          return;
        if (!Compile())
          return;
        Finish();
      } catch (Exception ex) {
        if (ex.GetBaseException() is RuntimeErrorException)
          Logger.WriteLine("*** {0}", ex.Message);
        else if (ex.GetBaseException() is UtilAssertException)
          Logger.WriteLine("*** Assert failure: {0}", ex.ToString());
        else {
          Logger.WriteLine("*** Unexpected exception: {0}", ex.ToString());
        }
        Logger.WriteLine("*** Abort ***");
      }
      Logger.WriteLine("");
    }

    // Capture the options
    static bool Option(string arg) {
      if (arg == "?") {
        Logger.WriteLine(_help);
        return false;
      } else if (arg.StartsWith("c")) {
        _nsw = arg.Contains("n");
        _usw = arg.Contains("u");
      } else if (arg == "x")
        _xsw = true;
      else if (arg == "i")
        _isw = true;
      else if (arg == "s")
        _ssw = true;
      else if (Regex.IsMatch(arg, "[0-9]+"))
        Logger.Level = int.Parse(arg);
      else {
        Logger.WriteLine("*** Bad option: {0}", arg);
        return false;
      }
      return true;
    }

    // Process options, construct components
    // Note: start catalog deferred until flags read from source
    static bool Start() {
      if (_paths.Count == 0)
        _paths.Add(_defaultinput);
      if (!File.Exists(_paths[0])) {
        Logger.WriteLine("File not found: {0}", _paths[0]);
        return false;
      }
      // set up components
      if (_isw && Logger.Level == 0) Logger.Level = 1;
      _catalog = Catalog.Create();
      _catalog.InteractiveFlag = _isw;
      _catalog.ExecuteFlag = _xsw && !_isw;
      _catalog.PersistFlag = _usw;
      _catalog.DatabaseSqlFlag = _ssw;
      _catalog.NewFlag = _nsw;
      if (_paths.Count > 1)
        _catalog.DatabasePath = _paths[1];
      //_catalog.CatalogName = _catalogname;
      _catalog.SourcePath = ".";

      _evaluator = (!_xsw) ? Evaluator.Create(_catalog) : null;

      return true;
    }

    static bool Compile() {
      Logger.WriteLine("*** Compiling: {0} ***", _paths[0]);
      var parser = Parser.Create(_catalog, _evaluator);
      using (StreamReader sr = File.OpenText(_paths[0])) {
        var ret = parser.Compile(sr);
        Logger.WriteLine("*** Compiled {0} {1} ***", _paths[0], ret ? "OK"
          : "with error count = " + parser.ErrorCount.ToString());
        return parser.ErrorCount == 0;
      }
    }

    static void Finish() {
      if (_catalog.PersistFlag && _catalog.ExecuteFlag)
        Logger.WriteLine("*** Updating: {0} ***", _paths[1]);
      _catalog.Finish();
    }
  }
}
