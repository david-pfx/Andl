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
using Andl.Common;
using Andl.Peg;

namespace Andl.Main {
  /// <summary>
  /// Mainline for Andl compiler.
  /// 
  /// Handles command line arguments.
  /// </summary>
  class Program {
    const string AndlVersion = "Andl 1.0b1606";
    static bool _csw = false; // compile only
    static bool _isw = false; // interactive: min level 1, can accept input
    static bool _nsw = false;  // new catalog
    static bool _tsw = false; // Thrift IDL
    static bool _usw = false; // update catalog
    static bool _ssw = false; // sql
    static string _defaultinput = @"test.andl";

    static TextWriter _output;
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
      _output = Logger.Out;  // FIX:?
      _output.WriteLine(AndlVersion);
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
          _output.WriteLine("*** {0} error ({1}): {2}", (ex as ProgramException).Kind, (ex as ProgramException).Source, ex.Message);
        else if (ex.GetBaseException() is UtilAssertException)
          _output.WriteLine("*** Assert failure: {0}", ex.ToString());
        else {
          _output.WriteLine("*** Unexpected exception: {0}", ex.ToString());
        }
        _output.WriteLine("*** Abort");
      }
      _output.WriteLine("");
    }

    // Capture the options
    static bool Option(string arg) {
      if (arg == "?") {
        _output.WriteLine(_help);
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
      else if (Regex.IsMatch(arg, "[0-9]+"))
        Logger.Level = int.Parse(arg);
      else {
        _output.WriteLine("*** Bad option: {0}", arg);
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
        _output.WriteLine("File not found: {0}", _paths[0]);
        return false;
      }
      // set up components
      if (_isw && Logger.Level == 0) Logger.Level = 1;
      _catalog = Catalog.Create();
      _catalog.InteractiveFlag = _isw;
      _catalog.ExecuteFlag = !_csw;
      _catalog.LoadFlag = _paths.Count > 1 && !_nsw;
      _catalog.SaveFlag = _usw;
      _catalog.SqlFlag = _ssw;
      _catalog.BaseName = Path.GetFileNameWithoutExtension(_paths[0]);
      if (_paths.Count > 1)
        _catalog.DatabasePath = _paths[1];
      _catalog.Start();

      // Create private catalog with access to global level, for evaluator
      _evaluator = Evaluator.Create(_catalog.GlobalVars, Console.Out, Console.In);
      return true;
    }

    // Compile using selected parser
    // Code is (now) always executed as compiled unless or until there is an error
    static bool Compile(string path) {
      _output.WriteLine("*** Compiling: {0}", path);
      IParser parser = PegCompiler.Create(_catalog);
      //IParser parser = (!_psw) ? 
      //  PegCompiler.Create(_catalog) : 
      //  OldCompiler.Create(_catalog);
      using (StreamReader input = File.OpenText(path)) {
        var ret = parser.RunScript(input, Console.Out, _evaluator, path);
        _output.WriteLine("*** Compiled {0} {1} ", path, ret ? "OK"
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
          _output.WriteLine("*** Writing: {0}", thriftname);
          (new CatalogInterfaceWriter()).WriteThrift(sw, _catalog.BaseName, _catalog.PersistentVars.GetEntries());
        }
      }
      if (_catalog.SaveFlag) {
        _output.WriteLine("*** Updating catalog: {0}", _catalog.DatabasePath);
        _catalog.Finish();
      }
    }
  }
}
