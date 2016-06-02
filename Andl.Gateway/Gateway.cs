using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Andl.Runtime;
using Andl.Peg;
using Andl.Common;

namespace Andl.Gateway {
  class KeyValue {
    internal string Key;
    internal string Value;
  }

  /// <summary>
  /// Implement a factory to create a gateway
  /// FIX: not really a good organisation
  /// </summary>
  public static class GatewayFactory {
    // Start the engine for a specified database and configuration settings
    public static GatewayBase Create(string database, Dictionary<string, string> settings) {
      return GatewayImpl.Create(database, settings);
    }
  }


  /// <summary>
  /// Abstract class representing gateway to runtime
  /// </summary>
  public abstract class GatewayBase : IExecuteGateway {
    public static string DefaultDatabaseName { get { return Catalog.DefaultDatabaseName; } }

    public bool JsonReturnFlag { get; set; }
    public abstract string DatabaseName { get; }
    public abstract DatabaseKinds DatabaseKind { get; }
    public abstract IParser Parser { get; }

    //--- A gateway for a global session
    public abstract void OpenSession();
    public abstract void CloseSession(bool ok = true);

    //--- A gateway for catalog level access

    public abstract Dictionary<string, string> GetEntryInfoDict(EntryInfoKind kind);
    public abstract Dictionary<string, string> GetSubEntryInfoDict(string name, EntrySubInfoKind kind);
    public abstract void Restart(bool save);

    //--- A gateway for accessing variables and functions by name

    // Get the value of a variable, or evaluate a function of no arguments.
    public abstract Result GetValue(string name);
    // Set the result of a variable, or call a command with one argument.
    public abstract Result SetValue(string name, object value);
    // Evaluate a function and return a value. Should never fail.
    public abstract Result Evaluate(string name, params object[] arguments);
    // Evaluate a function that changes state. May fail if not permitted.
    public abstract Result Command(string name, params object[] arguments);
    // Evaluate a function with json arguments and return

    //--- A gateway for making calls with arguments and return value in JSON

    public abstract Result JsonCall(string name, params string[] arguments);
    // Evaluate a function with id, query and json arguments and return
    public abstract Result JsonCall(string name, string id, KeyValuePair<string, string>[] query, string json);

    //--- A gateway for making REST/JSON calls, using convention to generate function names

    // Evaluate a function with method, name, id, query and json arguments and return
    public abstract Result JsonCall(string method, string name, string id, KeyValuePair<string, string>[] query, string json);

    //--- A gateway for making calls using native arguments

    // Make the call using native arguments
    public abstract bool NativeCall(string name, byte[] arguments, out byte[] result);
    // Get the required type for a setter
    public abstract Type GetSetterType(string name);
    // Get the required type for function arguments
    public abstract Type[] GetArgumentTypes(string name);

    //--- A gateway for making calls using a typed value builder (Thrift)

    // Get a TypedValueBuilder for the arguments to a function call
    public abstract TypedValueBuilder GetTypedValueBuilder(string name);
    // Function call using builders
    public abstract Result BuilderCall(string name, TypedValueBuilder arguments);

    //--- a gateway for submitting and executing program fragments

    // Compile and execute the program, returning error or program output or object
    // Result.Ok false means bad request, could not execute, message says why
    // Result.Ok true means request executed, value is return.
    // In raw mode input and output both raw text
    // In Json mode, input is Json string and output is Json object
    public abstract Result RunScript(string program, ExecModes kind = ExecModes.Raw, string sourcname = "");

    //public abstract Result Execute(string program, bool isjson = false);
    // Compile and execute the program, returning error or program output as lines of text
    //public abstract Result Execute(string program, out string output);

    }

    ///===========================================================================
    /// <summary>
    /// Manager/factory for multiple gateways accessed by database name
    /// </summary>
    public static class GatewayManager {
    enum SettingOptions { Ignore, Common, Split }

    static Dictionary<string, SettingOptions> _settingsdict = new Dictionary<string, SettingOptions> {
      { "Noisy", SettingOptions.Common },
      { "DatabasePath", SettingOptions.Common },
      { "Sql", SettingOptions.Common },
      { "DatabaseName", SettingOptions.Common },
    };

    static Dictionary<string, GatewayBase> _gatewaydict = new Dictionary<string, GatewayBase>();

    // Add one gateway for each DatabaseN key, plus common settings
    public static void AddGateways(Dictionary<string, string> settings) {
      //Logger.Open(3);
      //Logger.OpenTrace(5);
      var cd = Directory.GetCurrentDirectory();
      var common = settings
        .Where(s => _settingsdict.ContainsKey(s.Key) && _settingsdict[s.Key] == SettingOptions.Common)
        .ToDictionary(k => k.Key, v => v.Value);
      // root folder is set from Server object during app startup
      var root = (settings.ContainsKey("RootFolder")) ? settings["RootFolder"] : ".";
      foreach (var key in settings.Keys) {
        if (Regex.IsMatch(key, "^Database.*$")) {
          var values = settings[key].Split(',');
          var path = values[2].Replace("$RootFolder$", root);
          var gateway = GatewayFactory.Create(path, common);

          // This is an attempt to avoid later multi-threaded problems loading the catalog.
          // The type system is single-instance and is shared across database instances,
          // which may not be a good thing.
          gateway.OpenSession();
          _gatewaydict[values[0]] = gateway;
        }
      }
    }

    public static GatewayBase GetGateway(string database) {
      return _gatewaydict[database];
    }

  }

  ///===========================================================================
  /// <summary>
  /// The implementation of the gateway API for a particular catalog
  /// </summary>
  public class GatewayImpl : GatewayBase {
    Catalog _catalog;
    IParser _parser;
    string _database;
    Dictionary<string, string> _settings;

    public override string ToString() {
      return $"Gateway {_catalog.DatabaseName} {_catalog.DatabaseKind}";
    }

    public static GatewayImpl Create(string database, Dictionary<string, string> settings) {
      var ret = new GatewayImpl() {
        _database = database,
        _settings = settings
      };
      ret.Start();
      return ret;
    }

    // Load and start the catalog
    void Start() {
      Logger.WriteLine(2, ">Gateway: Start");
      _catalog = Catalog.Create();
      _catalog.LoadFlag = true;
      _catalog.ExecuteFlag = true;
      _catalog.SetConfig(_settings);
      _catalog.Start(_database);
      _parser = PegCompiler.Create(_catalog);
    }

    // Finish the catalog, optionally with different settings eg Save
    void Finish(Dictionary<string, string> settings) {
      _catalog.SetConfig(settings);
      _catalog.Finish();
    }

    public override DatabaseKinds DatabaseKind { get { return _catalog.DatabaseKind; } }
    public override string DatabaseName { get { return _catalog.DatabaseName; } }
    public override IParser Parser { get { return _parser; } }

    ///--------------------------------------------------------------------------------------------
    /// Catalog access functions

    // Support implementation functions at catalog level
    public override void Restart(bool save) {
      var settings = new Dictionary<string, string> { { "Save", save.ToString() } };
      Finish(settings);
      Start();
    }

    public override Type[] GetArgumentTypes(string name) {
      var types = _catalog.GlobalVars.GetArgumentTypes(name);
      if (types == null) return null;
      return types.Select(t => t.NativeType).ToArray();
    }

    public override Type GetSetterType(string name) {
      var type = _catalog.GlobalVars.GetReturnType(name);
      if (type == null) return null;
      return type.NativeType;
    }

    // Support implementation functions at catalog level
    public override TypedValueBuilder GetTypedValueBuilder(string name) {
      var types = _catalog.GlobalVars.GetArgumentTypes(name);
      if (types == null) return null;
      return TypedValueBuilder.Create(types);
    }

    public override Dictionary<string, string> GetEntryInfoDict(EntryInfoKind kind) {
      return _catalog.PersistentVars.GetEntryInfoDict(kind);
    }

    public override Dictionary<string, string> GetSubEntryInfoDict(string name, EntrySubInfoKind kind) {
      return _catalog.PersistentVars.GetSubEntryInfoDict(kind, name);
    }

    public override void OpenSession() {
      _catalog.BeginSession(SessionState.Full);
    }
    public override void CloseSession(bool ok) {
      _catalog.EndSession(ok ? SessionResults.Ok : SessionResults.Failed);
    }

    ///--------------------------------------------------------------------------------------------
    /// Main implementation functions, request session based
    /// 

    //-- direct calls, native types
    public override Result GetValue(string name) {
      var session = RequestSession.Open(this, _catalog);
      var value = session.GetValue(name);
      session.Close();
      return value;
    }
    public override Result SetValue(string name, object value) {
      var session = RequestSession.Open(this, _catalog);
      var result = session.SetValue(name, value);
      session.Close();
      return result;
    }

    public override Result Evaluate(string name, params object[] arguments) {
      var session = RequestSession.Open(this, _catalog);
      var result = session.Evaluate(name, arguments);
      session.Close();
      return result;
    }

    public override Result Command(string name, params object[] arguments) {
      var session = RequestSession.Open(this, _catalog);
      var result = session.Evaluate(name, arguments);
      session.Close();
      return result;
    }

    //--- json calls
    public override Result JsonCall(string name, string[] arguments) {
      Logger.WriteLine(3, "JsonCall {0} args {1}", name, arguments.Length);
      var session = RequestSession.Open(this, _catalog);
      var result = session.JsonCall(name, arguments);
      session.Close();
      Logger.WriteLine(3, "[JC {0}]", result.Ok);
      return result;
    }

    public override Result JsonCall(string name, string id, KeyValuePair<string, string>[] query, string body) {
      Logger.WriteLine(3, "JsonCall {0} id={1} q={2} body={3}", name, id, query != null, body);
      var session = RequestSession.Open(this, _catalog);
      var result = session.JsonCall(name, id, query, body);
      session.Close();
      Logger.WriteLine(3, "[JC {0}]", result.Ok);
      return result;
    }

    // Implement REST-like interface, building function name according to HTTP method
    public override Result JsonCall(string method, string name, string id, KeyValuePair<string, string>[] query, string body) {
      Logger.WriteLine(3, "JsonCall {0} method={1} id={2} q={3} body={4}", name, method, id, query != null, body);
      var newname = BuildName(method, name, id != null, query != null);
      var session = RequestSession.Open(this, _catalog);
      var result = session.JsonCall(newname, id, query, body);
      session.Close();
      Logger.WriteLine(3, "[JC {0}]", result.Ok);
      return result;
    }

    //-- serialised native interface
    public override bool NativeCall(string name, byte[] arguments, out byte[] output) {
      var session = RequestSession.Open(this, _catalog);
      var result = session.NativeCall(name, arguments, out output);
      session.Close();
      return result;
    }

    //-- builder interface
    public override Result BuilderCall(string name, TypedValueBuilder arguments) {
      Logger.WriteLine(3, "BuilderCall {0} args={1}", name, arguments.StructSize);
      var session = RequestSession.Open(this, _catalog);
      var result = session.BuilderCall(name, arguments);
      session.Close();
      Logger.WriteLine(3, "[BC {0}]", result.Ok);
      return result;
    }

    //-- execute and return result
    // session state connect only, to allow program to set catalog options
    public override Result RunScript(string program, ExecModes kind, string sourcename) {
      var session = RequestSession.Open(this, _catalog, SessionState.Connect);
      Logger.WriteLine(2, ">RunScript <{0}> len={1} kind={2}", program.Shorten(20), program.Length, kind);
      var result = (kind == ExecModes.Raw) ? session.RunScriptRaw(program, sourcename)
        : session.RunScriptJson(program, sourcename);
      session.Close();
      return result;
    }

    ///--------------------------------------------------------------------------------------------
    /// Utility

    // Build function name
    // NOTE: empty query string here is NOT the same as null
    static string BuildName(string method, string name, bool hasid, bool hasquery) {
      var pref = method.ToLower();
      if (pref == "post")
        pref = "add";
      var newname = pref + "_" + name + (hasid ? "_id" : "") + (hasquery ? "_q" : "");
      return newname;
    }

  }

  ///===========================================================================
  ///
  /// Implement a session for the lifetime of a single request
  /// Supports both native type and json interface
  ///

  internal class RequestSession {
    GatewayBase _runtime;
    Catalog _catalog;
    ICatalogVariables _catvars;
    Evaluator _evaluator;
    StringWriter _output = new StringWriter();
    StringReader _input = new StringReader("");
    TextWriter _savelog;

    // Open a request session
    // Session state defaults to full, but may be connect only to allow script to set catalog options
    internal static RequestSession Open(GatewayBase runtime, Catalog catalog, SessionState state = SessionState.Full) {
      var ret = new RequestSession() {
        _runtime = runtime,
        _catalog = catalog,
        _catvars = catalog.GlobalVars.PushScope(),
        _savelog = Logger.Out,
      };
      Logger.Out = ret._output;
      ret._evaluator = Evaluator.Create(ret._catvars, ret._output, ret._input);
      ret._catalog.BeginSession(state);
      return ret;
    }

    // Close the session, restore logging
    public void Close() {
      _catalog.EndSession(SessionResults.Ok);
      Logger.Out = _savelog;
    }

    // Get a native value from a variable or parameterless function
    public Result GetValue(string name) {
      var kind = _catvars.GetKind(name);
      if (kind == EntryKinds.Code)
        return Evaluate(name);
      if (kind != EntryKinds.Value) return Result.Failure("unknown or invalid name");

      var nvalue = TypeMaker.ToNativeValue(_catvars.GetValue(name));
      return Result.Success(nvalue);
    }

    // Set a native value to a variable or call a single parameter void function
    public Result SetValue(string name, object nvalue) {
      var kind = _catvars.GetKind(name);
      if (kind == EntryKinds.Code)
        return Evaluate(name, nvalue);
      if (kind != EntryKinds.Value) return Result.Failure("unknown or invalid name");

      var datatype = _catvars.GetDataType(name);
      var value = TypeMaker.FromNativeValue(nvalue, datatype);
      return Result.Success(null);
    }

    // Call a function with native arguments, get a return value or null if void
    public Result Evaluate(string name, params object[] arguments) {
      var kind = _catvars.GetKind(name);
      if (kind != EntryKinds.Code) return Result.Failure("unknown or invalid name");

      var expr = (_catvars.GetValue(name) as CodeValue).Value;
      if (arguments.Length != expr.Lookup.Degree) return Result.Failure("wrong no of args");

      var argvalues = arguments.Select((a, x) => TypeMaker.FromNativeValue(a, expr.Lookup.Columns[x].DataType)).ToArray();
      var args = DataRow.CreateNonTuple(expr.Lookup, argvalues);
      TypedValue value = null;
      try {
        value = _evaluator.Exec(expr.Code, args);
      } catch (ProgramException ex) {
        return Result.Failure(ex.ToString());
      }
      var nvalue = (value == VoidValue.Void) ? null : TypeMaker.ToNativeValue(value);
      return Result.Success(nvalue);
    }

    // call a function with args passed in json, return Result in json
    public Result JsonCall(string name, params string[] jsonargs) {
      var kind = _catvars.GetKind(name);
      if (kind != EntryKinds.Code) return Result.Failure("unknown or invalid name");
      var expr = (_catvars.GetValue(name) as CodeValue).Value;
      if (expr.Lookup.Degree != jsonargs.Length) return Result.Failure("wrong no of args");

      DataRow argvalue;
      TypedValue retvalue;
      try {
        var argvalues = jsonargs.Select((a, x) => {
          var datatype = expr.Lookup.Columns[x].DataType;
          var nvalue = JsonConvert.DeserializeObject(a, datatype.NativeType);
          return TypeMaker.FromNativeValue(nvalue, datatype);
        }).ToArray();
        argvalue = DataRow.CreateNonTuple(expr.Lookup, argvalues);
      } catch {
        return Result.Failure("argument conversion error");
      }
      try {
        retvalue = _evaluator.Exec(expr.Code, argvalue);
      } catch (ProgramException ex) {
        return Result.Failure(ex.ToString());
      }
      if (retvalue != VoidValue.Void) {
        var nret = TypeMaker.ToNativeValue(retvalue);
        if (_runtime.JsonReturnFlag) {    // FIX: s/b default
          var jret = JsonConvert.SerializeObject(nret);
          return Result.Success(jret);
        }
        return Result.Success(nret);
      }
      return Result.Success(null);
    }

    // call a function with args passed as id, query and json, return Result with message or json
    // NOTE: empty query array here is NOT the same as a null
    public Result JsonCall(string name, string id, KeyValuePair<string, string>[] query, string json) {
      var kind = _catvars.GetKind(name);
      if (kind != EntryKinds.Code) return Result.Failure("unknown or invalid name: " + name);
      var expr = (_catvars.GetValue(name) as CodeValue).Value;

      var argcount = (id != null ? 1 : 0) + (query != null ? 1 : 0) + (json != null ? 1 : 0);
      if (expr.Lookup.Degree != argcount) return Result.Failure("wrong no of args, expected " + expr.Lookup.Degree.ToString());

      DataRow argvalue;
      List<object> nargvalues = new List<object>();
      TypedValue retvalue;
      // convert each argument to a native form TypeMaker will understand
      if (id != null)
        nargvalues.Add(id);
      if (query != null && query.Length > 0)
        nargvalues.Add(query.Select(kvp => new KeyValue { Key = kvp.Key, Value = kvp.Value }).ToList());
      if (json != null) {
        try {
          var datatype = expr.Lookup.Columns[nargvalues.Count].DataType;
          nargvalues.Add(JsonConvert.DeserializeObject(json, datatype.NativeType));
        } catch {
          return Result.Failure("json conversion error");
        }
      }
      try {
        var argvalues = nargvalues.Select((nv, x) => {
          var datatype = expr.Lookup.Columns[x].DataType;
          return TypeMaker.FromNativeValue(nv, datatype);
        }).ToArray();
        argvalue = DataRow.CreateNonTuple(expr.Lookup, argvalues);
      } catch {
        return Result.Failure("argument conversion error");
      }
      try {
        retvalue = _evaluator.Exec(expr.Code, argvalue);
      } catch (ProgramException ex) {
        return Result.Failure(ex.ToString());
      }
      var nret = (retvalue == VoidValue.Void) ? null : TypeMaker.ToNativeValue(retvalue);
      if (_runtime.JsonReturnFlag) {    // FIX: s/b default
        var jret = JsonConvert.SerializeObject(nret);
        return Result.Success(jret);
      }
      return Result.Success(nret);
    }

    // call using serialised native arguments, return serialised native result
    internal bool NativeCall(string name, byte[] arguments, out byte[] result) {
      var kind = _catvars.GetKind(name);
      if (kind != EntryKinds.Code) return NativeFail("unknown or invalid name: " + name, out result);
      var expr = (_catvars.GetValue(name) as CodeValue).Value;

      TypedValue[] argvalues = new TypedValue[expr.NumArgs];
      using (var pr = PersistReader.Create(arguments)) {
        for (var i = 0; i < expr.NumArgs; ++i)
        try {
          argvalues[i] = pr.Read(expr.Lookup.Columns[i].DataType); // BUG: needs heading
        } catch {
          return NativeFail("argument conversion error", out result);
        }
      }
      var argvalue = DataRow.CreateNonTuple(expr.Lookup, argvalues);
      TypedValue retvalue;

      try {
        retvalue = _evaluator.Exec(expr.Code, argvalue);
      } catch (ProgramException ex) {
        return NativeFail(ex.ToString(), out result);
      }
      using (var pw = PersistWriter.Create()) {
        pw.Write(retvalue);
        result = pw.ToArray();
      }
      return true;
    }

    // format message for failed native call
    bool NativeFail(string message, out byte[] data) {
      using (var pw = PersistWriter.Create()) {
        pw.Write(message);
        data = pw.ToArray();
      }
      return false;
    }

    //public bool Call(string name, TypedValueBuilder arguments, out TypedValueBuilder result) {
    //  result = null;
    //  return false;
    //}

    // call supporting Thrift interface
    internal Result BuilderCall(string name, TypedValueBuilder arguments) {
      var kind = _catvars.GetKind(name);
      Logger.Assert(kind == EntryKinds.Code);
      var expr = (_catvars.GetValue(name) as CodeValue).Value;
      TypedValue retvalue;
      try {
        var argvalue = DataRow.CreateNonTuple(expr.Lookup, arguments.FilledValues());
        retvalue = _evaluator.Exec(expr.Code, argvalue);
        return Result.Success(TypedValueBuilder.Create(new TypedValue[] { retvalue }));
      } catch (ProgramException ex) {
        return Result.Failure(ex.ToString());
      }
    }

    // call to execute a piece of Andl code against the current catalog
    // raw text in, Result var out
    internal Result RunScriptRaw(string program, string sourcename = "-raw-") {
      Logger.WriteLine(1, "*** Compiling: {0}", sourcename);
      var input = new StringReader(program);
      try {
        var ret = _runtime.Parser.RunScript(input, _output, _evaluator, sourcename);
        Logger.WriteLine(1, "*** Compiled {0} {1} ", sourcename, ret ? "OK" : "with errors");
        if (ret) return Result.Success(_output.ToString());
        else return Result.Failure(_output.ToString());
      } catch (AndlException ex) {
        Logger.WriteLine(1, "*** Compiled {0} {1} ", sourcename, "aborted");
        _catalog.EndSession(SessionResults.Failed);
        return Result.Failure($"{_output}\n{ex}");
      }
    }

    // call to execute a piece of Andl code against the current catalog
    // Json in, Json out
    internal Result RunScriptJson(string program, string sourcename = "-json-") {
      var input = new StringReader(JsonConvert.DeserializeObject<string>(program));
      try {
        var ret = _runtime.Parser.RunScript(input, _output, _evaluator, sourcename);
        // Construct object for Json return
        var result = new { ok = ret, value = _output.ToString() };
        return Result.Success(JsonConvert.SerializeObject(result));
      } catch (ProgramException ex) {
        return Result.Failure(ex.ToString());
      }
    }
  }

}
