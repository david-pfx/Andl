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
using System.Text;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.IO;
using Andl.Sql;
using Andl.Postgres;
using Andl.Runtime;
using Andl.Common;

namespace Andl.Gateway {

  ///==============================================================================================
  /// <summary>
  /// Provide a static entry point with signature (string)=>int
  /// Called by pClrRuntimeHost->ExecuteInDefaultAppDomain (clrhost)
  /// Create an instance for future use.
  /// 
  /// Then reverse call using P/Invoke passing minimum set of delegates as callbacks
  /// The 4 functions provided here are delegated to ISqlPgFunction
  /// </summary>
  public class Postgres : PostgresInterop {
    static string _initial_arg;
    static Dictionary<int, Postgres> _hlookup = new Dictionary<int, Postgres>();
    string _error_message = null;
    Dictionary<string, string> _settings = new Dictionary<string, string> {
      { "Load", "false" },
      { "Save", "false" },
      { "Noisy", "1" },
      { "Sql", "true" },
      { "DatabaseKind", "Postgres" },
    };
    string _databasename = GatewayBase.DefaultDatabaseName;
    // access to function calls
    public ISqlPgFunction _pgfunction;
    // database
    PostgresDatabase _database;

    //-------------------------------------------------------------------------
    // This is the one and only entry point called when Postgres loads the language handler DLL 
    // At this point there is no way to report problems, so avoid any exceptions. Just store things for later use.
    public static int Entry(string arg) {
      // Leave these here commented out if ever needed
      //Debugger.Launch();
      //pg_elog(ElogLevel.NOTICE, $"Entry: '{arg}'");
      _initial_arg = arg;

      // These are the (static) callback functions. Look up an instance and call it...
      ConnectCallback cncb = (h, o) => GetInstance(h).Connect(o);
      TypeCheckCallback tccb = (h, f, n, a, r) => GetInstance(h).TypeCheck(f, a, r);
      InvokeCallback ivcb = (h, f, n, a, r) => GetInstance(h).Invoke(f, a, r);
      GetMessageCallback gmcb = (h) => GetInstance(h).GetMessage();

      // call back with the real entry points for later use
      return plandl_init_callback(cncb, tccb, ivcb, gmcb);
    }

    //-------------------------------------------------------------------------
    // Retrieve or allocate an instance
    // May not be needed. PG guarantees single-threaded.
    static Postgres GetInstance(int handle) {
      if (!_hlookup.ContainsKey(handle)) _hlookup.Add(handle, new Postgres());
      return _hlookup[handle];
    }

    //-------------------------------------------------------------------------
    // Entry point: Create a gateway connection, return 1 if ok else set up message
    int Connect(string options) {
      //pg_elog(ElogLevel.NOTICE, $"Connect: '{options}'");
      Logger.Open(0);
      try {
        var opts = Regex.Split(options, @"\s*,\s*");
        foreach (var opt in opts) {
          if (opt == "Debug") Debugger.Launch();
          else {
            var o = Regex.Split(opt, @"\s*\=\s*");
            if (o.Length == 2) _settings[o[0]] = o[1];
            else Logger.WriteLine(1, $"Invalid option ignored: {opt}");
          }
        }
        var gateway = GatewayFactory.Create(_databasename, _settings);
        // send tracing as NOTICE so it won't get lost on a crash
        if (Logger.Level >= 2) Logger.Open(5, new ElogWriter());
        Logger.WriteLine(1, $">Connect: {_databasename}, {options}");

        // this is the function call interface
        _database = SqlTarget.Current.Database as PostgresDatabase;  // must be set up by now
        // this is to install any initial functions
        var pgconn = PostgresConnect.Create(gateway, _database);
        Logger.Assert(pgconn != null, "pg function");
        _pgfunction = pgconn;
        SqlTarget.Current.FunctionCreator = pgconn;
        return 1;
      } catch(Exception ex) {
        _error_message = ex.ToString();
        return 0;
      }
    }

    //-------------------------------------------------------------------------
    // Entry point: retrieve error message text, as PG-owned UTF-8 string or NULL
    IntPtr GetMessage() {
      if (_error_message == null) return IntPtr.Zero;
      var bytes = Encoding.UTF8.GetBytes(_error_message);
      var ptr = pg_alloc_mem(bytes.Length + 1);
      Marshal.Copy(bytes, 0, ptr, bytes.Length);
      Marshal.WriteByte(ptr, bytes.Length, 0);
      return ptr;
    }

    //-------------------------------------------------------------------------
    // Entry point: check that function exists and arguments and return value (as OIDs) are correct
    int TypeCheck(string funcname, int[] argoids, int retoid) {
      try {
        return _pgfunction.TypeCheck(funcname, argoids, retoid) ? 1 : 0;
      } catch (Exception ex) {
        _error_message = ex.Message;
        return 0;
      }
    }

    //-------------------------------------------------------------------------
    // Entry point: call a function with arguments and return value (as Datums)
    // assume already type checked
    int Invoke(string funcname, IntPtr[] argvalues, IntPtr retval) {
      try {
        return _pgfunction.Invoke(funcname, argvalues, retval) ? 1 : 0;
      } catch (Exception ex) {
        _error_message = ex.Message;
        return 0;
      }
    }
  }

  ///==============================================================================================
  /// <summary>
  /// Implement TextWriter to allow logging to Postgres elog()
  /// </summary>
  class ElogWriter : TextWriter {
    StringBuilder _sb = new StringBuilder();
    public override Encoding Encoding {
      get { return Encoding.Default; }
    }
    public override void Write(char c) {
      _sb.Append(c);
      if (c == '\n') {
        PostgresInterop.pg_elog(PostgresInterop.ElogLevel.NOTICE, "|" + _sb.ToString());
        _sb.Clear();
      }
    }
  }
}
