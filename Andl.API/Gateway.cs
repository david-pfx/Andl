using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Andl;
using Andl.Runtime;
using Andl.Compiler;
using System.IO;

namespace Andl.API {
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

  /// <summary>
  /// Anstract class representing runtime
  /// </summary>
  public abstract class Gateway {

    // Start the engine and let it configure itself
    public static Gateway StartUp(Dictionary<string, string> settings) {
      return GatewayImpl.Create(settings);
    }

    public bool JsonReturnFlag { get; set; }
    public abstract string DatabaseName { get; }
    public abstract Parser Parser { get; }

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

    // Compile and execute the program, returning error or the last expression as a native value
    public abstract Result Execute(string program);
    // Compile and execute the program, returning error or program output as lines of text
    public abstract Result Execute(string program, out string output);

  }

  ///===========================================================================
  /// <summary>
  /// The implementation of the gateway API for a particular catalog
  /// </summary>
  public class GatewayImpl : Gateway {
    Catalog _catalog;
    Parser _parser;

    public static GatewayImpl Create(Dictionary<string, string> settings) {
      var ret = new GatewayImpl();
      ret.Start(settings);
      return ret;
    }

    // Load and start the catalog
    void Start(Dictionary<string, string> settings) {
      _catalog = Catalog.Create();
      _catalog.LoadFlag = true;
      _catalog.ExecuteFlag = true;
      foreach (var key in settings.Keys)
        _catalog.SetConfig(key, settings[key]);
      _catalog.Start();
      _parser = Parser.Create(_catalog);
    }

    public override string DatabaseName { get { return _catalog.DatabaseName; } }
    public override Parser Parser { get { return _parser; } }


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

    // Main implementation functions
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

    // Build function name
    // NOTE: empty query string here is NOT the same as null
    static string BuildName(string method, string name, bool hasid, bool hasquery) {
      var pref = method.ToLower();
      if (pref == "post") pref = "add";
      var newname = pref + "_" + name + (hasid ? "_id" : "") + (hasquery ? "_q" : "");
      return newname;
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
    public override Result Execute(string program) {
      Logger.WriteLine(3, "Execute {0}", program);
      var result = RequestSession.Create(this, _catalog).Execute(program);
      Logger.WriteLine(3, "[Ex {0}]", result.Ok);
      return result;
    }

    //-- execute and return result
    public override Result Execute(string program, out string output) {
      Logger.WriteLine(3, "Execute2 {0}", program);
      var result = RequestSession.Create(this, _catalog).Execute(program, out output);
      Logger.WriteLine(3, "[Ex {0}]", result.Ok);
      return result;
    }
  }

  ///===========================================================================
  ///
  /// Implement a session for the lifetime of a single request
  /// Supports both native type and json interface
  ///

  internal class RequestSession {
    Gateway _runtime;
    CatalogPrivate _catalogpriv;
    Evaluator _evaluator;
    StringWriter _output = new StringWriter();
    StringReader _input = new StringReader("");

    internal static RequestSession Create(Gateway runtime, Catalog catalog) {
      var ret = new RequestSession();
      ret._runtime = runtime;
      ret._catalogpriv = CatalogPrivate.Create(catalog);
      ret._evaluator = Evaluator.Create(ret._catalogpriv, ret._output, ret._input);
      return ret;
    }

    public Result GetValue(string name) {
      var kind = _catalogpriv.GetKind(name);
      if (kind == EntryKinds.Code)
        return Evaluate(name);
      if (kind != EntryKinds.Value) return Result.Failure("unknown or invalid name");

      var nvalue = TypeMaker.ToNativeValue(_catalogpriv.GetValue(name));
      return Result.Success(nvalue);
    }

    public Result SetValue(string name, object nvalue) {
      var kind = _catalogpriv.GetKind(name);
      if (kind == EntryKinds.Code)
        return Evaluate(name, nvalue);
      if (kind != EntryKinds.Value) return Result.Failure("unknown or invalid name");

      var datatype = _catalogpriv.GetDataType(name);
      var value = TypeMaker.FromNativeValue(nvalue, datatype);
      return Result.Success(null);
    }

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
        if (_runtime.JsonReturnFlag) {
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
      if (_runtime.JsonReturnFlag) {
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

    internal Result Execute(string program) {
      var input = new StringReader(program);
      try {
        var ret = _runtime.Parser.Process(input, _output, _evaluator, "-api-");
        if (ret) return Result.Success(_output.ToString());
        else Result.Failure(_output.ToString());
      } catch (ProgramException ex) {
        return Result.Failure(ex.ToString());
      }
      return null; //??
    }

    internal Result Execute(string program, out string output) {
      throw new NotImplementedException();
    }
  }

}
