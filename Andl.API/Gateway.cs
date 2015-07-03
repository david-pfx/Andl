using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Andl;
using Andl.Runtime;

namespace Andl.API {
  /// <summary>
  /// Encapsulates a result
  /// 
  /// If successful, there may be a result
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

  /// <summary>
  /// Anstract class representing runtime
  /// </summary>
  public abstract class Runtime {
    public static Runtime Gateway;

    // Start the engine and let it configure itself
    public static Runtime StartUp(Dictionary<string, string> settings) {
      Gateway = RuntimeImpl.Startup(settings);
      return Gateway;
    }

    // Get the value of a variable, or evaluate a function of no arguments.
    public abstract Result GetValue(string name);
    // Set the result of a variable, or call a command with one argument.
    public abstract Result SetValue(string name, object value);
    // Evaluate a function and return a value. Should never fail.
    public abstract Result Evaluate(string name, params object[] arguments);
    // Evaluate a function that changes state. May fail if not permitted.
    public abstract Result Command(string name, params object[] arguments);

    // Evaluate a function that changes state. May fail if not permitted.
    public abstract Type GetSetterType(string name);
    // Evaluate a function that changes state. May fail if not permitted.
    public abstract Type[] GetArgumentTypes(string name);
  }

  ///===========================================================================
  /// <summary>
  /// The implementation of the gateway API
  /// </summary>
  public class RuntimeImpl : Runtime {
    Catalog _catalog;

    public static RuntimeImpl Startup(Dictionary<string, string> settings) {
      var ret = new RuntimeImpl();
      ret.Start(settings);
      return ret;
    }

    void Start(Dictionary<string, string> settings) {
      _catalog = Catalog.Create();
      foreach (var key in settings.Keys)
        _catalog.SetConfig(key, settings[key]);
      _catalog.Start();
    }

    // Main implementation functions
    public override Result GetValue(string name) {
      return GatewaySession.Create(_catalog).GetValue(name);
    }
    public override Result SetValue(string name, object value) {
      return GatewaySession.Create(_catalog).SetValue(name, value);
    }

    public override Result Evaluate(string name, params object[] arguments) {
      return GatewaySession.Create(_catalog).Evaluate(name, arguments);
    }

    public override Result Command(string name, params object[] arguments) {
      return GatewaySession.Create(_catalog).Evaluate(name, arguments);
    }

    // Support implementation functions
    public override Type[] GetArgumentTypes(string name) {
      return _catalog.GlobalVars.GetArgumentTypes(name);
    }

    public override Type GetSetterType(string name) {
      return _catalog.GlobalVars.GetSetterType(name);
    }
  }

  ///===========================================================================
  ///

  internal class GatewaySession {
    CatalogPrivate _catalogpriv;
    Evaluator _evaluator;

    internal static GatewaySession Create(Catalog catalog) {
      var ret = new GatewaySession();
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

  }

}
