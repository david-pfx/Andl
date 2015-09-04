using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Andl;
using Andl.Runtime;

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
    public abstract string CatalogName { get; }

    // Get the value of a variable, or evaluate a function of no arguments.
    public abstract Result GetValue(string name);
    // Set the result of a variable, or call a command with one argument.
    public abstract Result SetValue(string name, object value);
    // Evaluate a function and return a value. Should never fail.
    public abstract Result Evaluate(string name, params object[] arguments);
    // Evaluate a function that changes state. May fail if not permitted.
    public abstract Result Command(string name, params object[] arguments);
    // Evaluate a function with json arguments and return

    public abstract Result JsonCall(string name, params string[] arguments);
    // Evaluate a function with id, query and json arguments and return
    public abstract Result JsonCall(string name, string id, KeyValuePair<string, string>[] query, string json);
    // Evaluate a function with method, name, id, query and json arguments and return
    public abstract Result JsonCall(string method, string name, string id, KeyValuePair<string, string>[] query, string json);

    public abstract bool Call(string name, byte[] arguments, out byte[] result);

    // Get the required type for a setter
    public abstract Type GetSetterType(string name);
    // Get the required type for function arguments
    public abstract Type[] GetArgumentTypes(string name);
  }

  ///===========================================================================
  /// <summary>
  /// The implementation of the gateway API
  /// </summary>
  public class GatewayImpl : Gateway {
    Catalog _catalog;

    public static GatewayImpl Create(Dictionary<string, string> settings) {
      var ret = new GatewayImpl();
      ret.Start(settings);
      return ret;
    }

    // Load and start the catalog
    void Start(Dictionary<string, string> settings) {
      _catalog = Catalog.Create();
      _catalog.LoadFlag = true;
      foreach (var key in settings.Keys)
        _catalog.SetConfig(key, settings[key]);
      _catalog.Start();
    }

    public override string CatalogName {
      get { return _catalog.CatalogName; }
    }

    // Support implementation functions at catalog level
    public override Type[] GetArgumentTypes(string name) {
      return _catalog.GlobalVars.GetArgumentTypes(name);
    }

    public override Type GetSetterType(string name) {
      return _catalog.GlobalVars.GetSetterType(name);
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
      return RequestSession.Create(this, _catalog).JsonCall(name, arguments);
    }

    public override Result JsonCall(string name, string id, KeyValuePair<string, string>[] query, string body) {
      return RequestSession.Create(this, _catalog).JsonCall(name, id, query, body);
    }

    // Implement REST-like interface, building function name according to HTTP method
    public override Result JsonCall(string method, string name, string id, KeyValuePair<string, string>[] query, string body) {
      var newname = BuildName(method, name, id != null, query != null);
      return RequestSession.Create(this, _catalog).JsonCall(newname, id, query, body);
    }

    // Build function name
    // NOTE: empty query string here is NOT the same as null
    static string BuildName(string method, string name, bool hasid, bool hasquery) {
      var pref = method.ToLower();
      if (pref == "post") pref = "add";
      var newname = pref + "_" + name + (hasid ? "_id" : "") + (hasquery ? "_q" : "");
      return newname;
    }

    //-- serialised interface
    public override bool Call(string name, byte[] arguments, out byte[] result) {
      return RequestSession.Create(this, _catalog).Call(name, arguments, out result);
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

    internal static RequestSession Create(Gateway runtime, Catalog catalog) {
      var ret = new RequestSession();
      ret._runtime = runtime;
      ret._catalogpriv = CatalogPrivate.Create(catalog);
      ret._evaluator = Evaluator.Create(ret._catalogpriv);
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
      var args = DataRow.Create(expr.Lookup, argvalues);
      var value = _evaluator.Exec(expr.Code, args);
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
        argvalue = DataRow.Create(expr.Lookup, argvalues);
      } catch {
        return Result.Failure("argument conversion error");
      }
      try {
        retvalue = _evaluator.Exec(expr.Code, argvalue);
      } catch {
        return Result.Failure("execution error");
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
        argvalue = DataRow.Create(expr.Lookup, argvalues);
      } catch {
        return Result.Failure("argument conversion error");
      }
      try {
        retvalue = _evaluator.Exec(expr.Code, argvalue);
      } catch {
        return Result.Failure("evaluator error");
      }
      var nret = (retvalue == VoidValue.Void) ? null : TypeMaker.ToNativeValue(retvalue);
      if (_runtime.JsonReturnFlag) {
        var jret = JsonConvert.SerializeObject(nret);
        return Result.Success(jret);
      }
      return Result.Success(nret);
    }

    // call using serialised arguments, return serialised result
    internal bool Call(string name, byte[] arguments, out byte[] result) {
      var kind = _catalogpriv.GetKind(name);
      if (kind != EntryKinds.Code) return Fail("unknown or invalid name: " + name, out result);
      var expr = (_catalogpriv.GetValue(name) as CodeValue).Value;

      TypedValue[] argvalues = new TypedValue[expr.NumArgs];
      using (var pr = PersistReader.Create(arguments)) {
        for (var i = 0; i < expr.NumArgs; ++i)
        try {
          argvalues[i] = pr.Read(expr.Lookup.Columns[i].DataType); // BUG: needs heading
        } catch {
          return Fail("argument conversion error", out result);
        }
      }
      var argvalue = DataRow.Create(expr.Lookup, argvalues);
      TypedValue retvalue;

      try {
        retvalue = _evaluator.Exec(expr.Code, argvalue);
      } catch {
        return Fail("evaluator error", out result);
      }
      using (var pw = PersistWriter.Create()) {
        pw.Write(retvalue);
        result = pw.ToArray();
      }
      return true;
    }

    bool Fail(string message, out byte[] data) {
      using (var pw = PersistWriter.Create()) {
        pw.Write(message);
        data = pw.ToArray();
      }
      return false;
    }
  }

}
