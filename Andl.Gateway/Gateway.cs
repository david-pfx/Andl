using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Andl;
using Andl.Runtime;
using Andl.Compiler;
using Andl.Peg;
using System.IO;
using System.Text.RegularExpressions;

namespace Andl.Gateway {
  /// <summary>
  /// Encapsulates a result
  /// 
  /// If successful, there may be a value
  /// If not, there must be a message
  /// </summary>
  public class Result {
    public bool Ok { get; set; }
    public string Message { get; set; }
    public object Value { get; set; }

    public static Result Success(object value) {
      return new Result { Ok = true, Message = null, Value = value };
    }
    public static Result Failure(string message) {
      return new Result { Ok = false, Message = message, Value = null };
    }
  }

  public class KeyValue {
    public string Key;
    public string Value;
  }

  public enum ExecModes { Raw, JsonString, JsonArray, JsonObj };

  /// <summary>
  /// Anstract class representing runtime
  /// </summary>
  public static class GatewayFactory {
    public const string DefaultDatabaseName = "data";

    // Start the engine for a specified database and configuration settings
    public static GatewayBase Create(string database, Dictionary<string, string> settings) {
      return GatewayImpl.Create(database, settings);
    }
  }


  /// <summary>
  /// Anstract class representing runtime
  /// </summary>
  public abstract class GatewayBase {
    //public const string DefaultDatabaseName = "data";

    //// Start the engine and let it configure itself
    //public static GatewayBase Create(string database, Dictionary<string, string> settings) {
    //  return GatewayImpl.Create(database, settings);
    //}

    public bool JsonReturnFlag { get; set; }
    public abstract string DatabaseName { get; }
    public abstract IParser Parser { get; }

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
    public abstract Result Execute(string program, ExecModes kind = ExecModes.Raw);

    //public abstract Result Execute(string program, bool isjson = false);
    // Compile and execute the program, returning error or program output as lines of text
    //public abstract Result Execute(string program, out string output);

    public abstract Dictionary<string, string> GetEntryInfoDict(EntryInfoKind kind);
    public abstract Dictionary<string, string> GetSubEntryInfoDict(string name, EntrySubInfoKind kind);

      //public abstract Dictionary<string, string> GetRelationsDict();
      //public abstract Dictionary<string, string> GetOperatorsDict();
      //public abstract Dictionary<string, string> GetVariablesDict();
      //public abstract Dictionary<string, string> GetTypesDict();

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
      { "DatabaseSqlFlag", SettingOptions.Common },
      { "DatabaseName", SettingOptions.Common },
      //{ "^Database.*$", SettingOptions.Split },
    };

    static Dictionary<string, GatewayBase> _gatewaydict = new Dictionary<string, GatewayBase>();

    //public static GatewayBase AddGateway(Dictionary<string, string> settings) {
    //  var common = settings
    //    .Where(s => _settingsdict.ContainsKey(s.Key) && _settingsdict[s.Key] == SettingOptions.Common)
    //    .ToDictionary(k => k, v => v);
    //  var gateway = GatewayImpl.Create(settings);
    //  if (settings.ContainsKey("DatabaseName"))
    //    _gatewaydict[settings["DatabaseName"]] = gateway;
    //  return gateway;
    //}

    // Add one gateway for each DatabaseN key, plus common settings
    public static void AddGateways(Dictionary<string, string> settings) {
      var common = settings
        .Where(s => _settingsdict.ContainsKey(s.Key) && _settingsdict[s.Key] == SettingOptions.Common)
        .ToDictionary(k => k, v => v);
      foreach (var key in settings.Keys) {
        if (Regex.IsMatch(key, "^Database.*$")) {
          var values = settings[key].Split(',');
          var settingsx = new Dictionary<string, string>(settings);
          _gatewaydict[values[0]] = GatewayFactory.Create(values[2], settingsx);
          //settingsx.Add("DatabaseName", values[0]);
          //if (values.Length >= 2) settingsx.Add("DatabaseSqlFlag", values[1]);
          //if (values.Length >= 3) settingsx.Add("DatabasePath", values[2]);
          //_gatewaydict[values[0]] = GatewayImpl.Create(settingsx);
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

    public static GatewayImpl Create(string database, Dictionary<string, string> settings) {
      var ret = new GatewayImpl();
      ret.Start(database, settings);
      return ret;
    }

    // Load and start the catalog
    void Start(string database, Dictionary<string, string> settings) {
      _catalog = Catalog.Create();
      _catalog.LoadFlag = true;
      _catalog.ExecuteFlag = true;
      foreach (var key in settings.Keys)
        _catalog.SetConfig(key, settings[key]);
      _catalog.Start(database);
      //_parser = Parser.Create(_catalog);
      // FIX: allow access to old compiler?
      //_parser = PegCompiler.Create(_catalog);
      _parser = OldCompiler.Create(_catalog);
    }

    public override string DatabaseName { get { return _catalog.DatabaseName; } }
    public override IParser Parser { get { return _parser; } }

    ///--------------------------------------------------------------------------------------------
    /// Catalog access functions

    // Support implementation functions at catalog level
    public override Type[] GetArgumentTypes(string name) {
      var types = _catalog.GlobalVars.GetArgumentTypes(name);
      if (types == null) return null;
      return types.Select(t => t.NativeType).ToArray();
    }

    public override Type GetSetterType(string name) {
      var type = _catalog.GlobalVars.GetSetterType(name);
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

    //public override Dictionary<string, string> GetRelationsDict() {
    //  return _catalog.PersistentVars.GetRelationsDict();
    //}
    //public override Dictionary<string, string> GetOperatorsDict() {
    //  return _catalog.PersistentVars.GetOperatorsDict();
    //}
    //public override Dictionary<string, string> GetVariablesDict() {
    //  return _catalog.PersistentVars.GetVariablesDict();
    //}
    //public override Dictionary<string, string> GetTypesDict() {
    //  return _catalog.PersistentVars.GetTypesDict();
    //}


    ///--------------------------------------------------------------------------------------------
    /// Main implementation functions, request session based
    /// 

    //-- direct calls, native types
    public override Result GetValue(string name) {
      return RequestSession.Create(this, _catalog).GetValue(name);
    }
    public override Result SetValue(string name, object value) {
      return RequestSession.Create(this, _catalog).SetValue(name, value);
    }

    public override Result Evaluate(string name, params object[] arguments) {
      return RequestSession.Create(this, _catalog).Evaluate(name, arguments);
    }

    public override Result Command(string name, params object[] arguments) {
      return RequestSession.Create(this, _catalog).Evaluate(name, arguments);
    }

    //--- json calls
    public override Result JsonCall(string name, string[] arguments) {
      Logger.WriteLine(3, "JsonCall {0} args {1}", name, arguments.Length);
      var result = RequestSession.Create(this, _catalog).JsonCall(name, arguments);
      Logger.WriteLine(3, "[JC {0}]", result.Ok);
      return result;
    }

    public override Result JsonCall(string name, string id, KeyValuePair<string, string>[] query, string body) {
      Logger.WriteLine(3, "JsonCall {0} id={1} q={2} body={3}", name, id, query != null, body);
      var result = RequestSession.Create(this, _catalog).JsonCall(name, id, query, body);
      Logger.WriteLine(3, "[JC {0}]", result.Ok);
      return result;
    }

    // Implement REST-like interface, building function name according to HTTP method
    public override Result JsonCall(string method, string name, string id, KeyValuePair<string, string>[] query, string body) {
      Logger.WriteLine(3, "JsonCall {0} method={1} id={2} q={3} body={4}", name, method, id, query != null, body);
      var newname = BuildName(method, name, id != null, query != null);
      var result = RequestSession.Create(this, _catalog).JsonCall(newname, id, query, body);
      Logger.WriteLine(3, "[JC {0}]", result.Ok);
      return result;
    }

    //-- serialised native interface
    public override bool NativeCall(string name, byte[] arguments, out byte[] result) {
      return RequestSession.Create(this, _catalog).NativeCall(name, arguments, out result);
    }

    //-- builder interface
    public override Result BuilderCall(string name, TypedValueBuilder arguments) {
      Logger.WriteLine(3, "BuilderCall {0} args={1}", name, arguments.StructSize);
      var result = RequestSession.Create(this, _catalog).BuilderCall(name, arguments);
      Logger.WriteLine(3, "[BC {0}]", result.Ok);
      return result;
    }

    //-- execute and return result
    public override Result Execute(string program, ExecModes kind) {
      Logger.WriteLine(3, "Execute {0} kind={1}", program, kind);
      var result = (kind == ExecModes.Raw) ? RequestSession.Create(this, _catalog).RawExecute(program)
        : RequestSession.Create(this, _catalog).JsonExecute(program);
      Logger.WriteLine(3, "[Ex {0}]", result.Ok);
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
    CatalogPrivate _catalogpriv;
    Evaluator _evaluator;
    StringWriter _output = new StringWriter();
    StringReader _input = new StringReader("");

    // Create a request session
    internal static RequestSession Create(GatewayBase runtime, Catalog catalog) {
      var ret = new RequestSession();
      ret._runtime = runtime;
      ret._catalogpriv = CatalogPrivate.Create(catalog);
      ret._evaluator = Evaluator.Create(ret._catalogpriv, ret._output, ret._input);
      return ret;
    }

    // Get a native value from a variable or parameterless function
    public Result GetValue(string name) {
      var kind = _catalogpriv.GetKind(name);
      if (kind == EntryKinds.Code)
        return Evaluate(name);
      if (kind != EntryKinds.Value) return Result.Failure("unknown or invalid name");

      var nvalue = TypeMaker.ToNativeValue(_catalogpriv.GetValue(name));
      return Result.Success(nvalue);
    }

    // Set a native value to a variable or call a single parameter void function
    public Result SetValue(string name, object nvalue) {
      var kind = _catalogpriv.GetKind(name);
      if (kind == EntryKinds.Code)
        return Evaluate(name, nvalue);
      if (kind != EntryKinds.Value) return Result.Failure("unknown or invalid name");

      var datatype = _catalogpriv.GetDataType(name);
      var value = TypeMaker.FromNativeValue(nvalue, datatype);
      return Result.Success(null);
    }

    // Call a function with native arguments, get a return value or null if void
    public Result Evaluate(string name, params object[] arguments) {
      var kind = _catalogpriv.GetKind(name);
      if (kind != EntryKinds.Code) return Result.Failure("unknown or invalid name");

      var expr = (_catalogpriv.GetValue(name) as CodeValue).Value;
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
      var kind = _catalogpriv.GetKind(name);
      if (kind != EntryKinds.Code) return Result.Failure("unknown or invalid name");
      var expr = (_catalogpriv.GetValue(name) as CodeValue).Value;
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
      var kind = _catalogpriv.GetKind(name);
      if (kind != EntryKinds.Code) return Result.Failure("unknown or invalid name: " + name);
      var expr = (_catalogpriv.GetValue(name) as CodeValue).Value;

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
      var kind = _catalogpriv.GetKind(name);
      if (kind != EntryKinds.Code) return NativeFail("unknown or invalid name: " + name, out result);
      var expr = (_catalogpriv.GetValue(name) as CodeValue).Value;

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
      var kind = _catalogpriv.GetKind(name);
      Logger.Assert(kind == EntryKinds.Code);
      var expr = (_catalogpriv.GetValue(name) as CodeValue).Value;
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
    internal Result RawExecute(string program, string source = "Line") {
      var input = new StringReader(program);
      try {
        var ret = _runtime.Parser.Process(input, _output, _evaluator, source);
        if (ret) return Result.Success(_output.ToString());
        else return Result.Failure(_output.ToString());
      } catch (ProgramException ex) {
        return Result.Failure(ex.ToString());
      }
    }

    // call to execute a piece of Andl code against the current catalog
    // Json in, Json out
    internal Result JsonExecute(string program) {
      var input = new StringReader(JsonConvert.DeserializeObject<string>(program));
      try {
        var ret = _runtime.Parser.Process(input, _output, _evaluator, "-api-");
        // Construct object for Json return
        var result = new { ok = ret, value = _output.ToString() };
        return Result.Success(JsonConvert.SerializeObject(result));
      } catch (ProgramException ex) {
        return Result.Failure(ex.ToString());
      }
    }
  }

}
