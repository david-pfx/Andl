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
using System.Text.RegularExpressions;
using Andl.Runtime;
using Andl.Peg;

namespace Andl.Main {
  /// <summary>
  /// Mainline for Andl compiler.
  /// 
  /// Handles command line arguments.
  /// </summary>
  class Program {
    const string AndlVersion = "Andl 1.0b2";
    static bool _csw = false; // compile only
    static bool _isw = false; // interactive
    static bool _nsw = false;  // new catalog
    static bool _tsw = false; // Thrift IDL
    static bool _usw = false; // update catalog
    static bool _ssw = false; // sql
    static bool _psw = false; // PEG
    static string _defaultinput = @"test.andl";

    static Catalog _catalog;
    static Evaluator _evaluator;

    static List<string> _paths = new List<string>();
    static string _help = "Andl [<input path> [<database name or path>]] options\n"
      + "\t\tDefault is compile and execute with new catalog and local database, no update.\n"
      + "\t\tDefault database is 'data', path is 'data.sandl' or 'data.sqandl' for sql.\n"
      + "\t/c[nu]\tUse existing catalog, n for new, u for update\n"
      + "\t/i\tInteractive, using console screen and keyboard\n"
      + "\t/t\tOutput Thrift IDL file\n"
      + "\t/x[n]\tExecute after compilation, n for not\n"
      + "\t/s\tUse Sql to access the database\n"
      + "\t/1\tListing to console\n"
      + "\t/2\tShow generated SQL\n"
      + "\t/3\tShow generated VM code\n"
      + "\t/4+\tMore detailed debug\n";

    static void Main(string[] args) {
      Logger.Open(0);   // no default logging
      Logger.WriteLine(AndlVersion);
      for (var i = 0; i < args.Length; ++i) {
        if (args[i].StartsWith("/") || args[i].StartsWith("-")) {
          if (!Option(args[i].Substring(1))) {
            return;
          }
        } else _paths.Add(args[i]);
      }
      try {
        var ok = Start() && Compile(_paths[0]);
        if (ok && _catalog.ExecuteFlag) Finish(_paths[0]);
      } catch (Exception ex) {
        if (ex.GetBaseException() is ProgramException)
          Logger.WriteLine("*** {0} error ({1}): {2}", (ex as ProgramException).Kind, (ex as ProgramException).Source, ex.Message);
        else if (ex.GetBaseException() is UtilAssertException)
          Logger.WriteLine("*** Assert failure: {0}", ex.ToString());
        else {
          Logger.WriteLine("*** Unexpected exception: {0}", ex.ToString());
        }
        Logger.WriteLine("*** Abort");
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
      } else if (arg.StartsWith("x")) {
        _csw = arg.Contains("n");
      } else if (arg == "i")
        _isw = true;
      else if (arg == "s")
        _ssw = true;
      else if (arg == "t")
        _tsw = true;
      else if (arg == "p")
        _psw = true;
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
      _catalog.ExecuteFlag = !_csw;
      _catalog.LoadFlag = _paths.Count > 1 && !_nsw;
      _catalog.SaveFlag = _usw;
      _catalog.DatabaseSqlFlag = _ssw;
      _catalog.BaseName = Path.GetFileNameWithoutExtension(_paths[0]);
      if (_paths.Count > 1)
        _catalog.DatabasePath = _paths[1];

      // Create private catalog with access to global level, for evaluator
      var catalogp = CatalogPrivate.Create(_catalog, true);
      // Create evaluator (may not get used)
      _evaluator = Evaluator.Create(catalogp, Console.Out, Console.In);

      return true;
    }

    // Compile using selected parser
    // Code is (now) always executed as compiled unless or until there is an error
    static bool Compile(string path) {
      Logger.WriteLine("*** Compiling: {0}", path);
      IParser parser = PegCompiler.Create(_catalog);
      //IParser parser = (!_psw) ? 
      //  PegCompiler.Create(_catalog) : 
      //  OldCompiler.Create(_catalog);
      using (StreamReader input = File.OpenText(path)) {
        var ret = parser.Process(input, Console.Out, _evaluator, path);
        Logger.WriteLine("*** Compiled {0} {1} ", path, ret ? "OK"
          : parser.Aborted ? "- terminated with fatal error"
          : "with error count = " + parser.ErrorCount.ToString());
        return parser.ErrorCount == 0;
      }
    }

    // Write out Thrift and Catalog if required
    static void Finish(string path) {
      if (_tsw) {
        var thriftname = Path.ChangeExtension(path, ".thrift");
        using (StreamWriter sw = new StreamWriter(thriftname)) {
          Logger.WriteLine("*** Writing: {0}", thriftname);
          (new CatalogInterfaceWriter()).WriteThrift(sw, _catalog.BaseName, _catalog.PersistentVars.GetEntries());
        }
      }
      if (_catalog.SaveFlag) {
      //if (_catalog.SaveFlag && _catalog.ExecuteFlag) {
        Logger.WriteLine("*** Updating catalog: {0}", _catalog.DatabasePath);
        _catalog.Finish();
      }
    }
  }
}
