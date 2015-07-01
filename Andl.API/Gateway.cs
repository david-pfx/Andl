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
  }

  public class RuntimeImpl : Runtime {
    Catalog _catalog;
    //Evaluator _evaluator;

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

    public override Result GetValue(string name) {
      var catalogpriv = CatalogPrivate.Create(_catalog);
      var evaluator = Evaluator.Create(catalogpriv);

      var value = catalogpriv.GetValue(name);
      if (value == null) return Result.Failure("unknown name");
      if (value.DataType == DataTypes.Code)
        value = evaluator.Exec((value as CodeValue).Value.Code);
      var nvalue = TypeMaker.GetNativeValue(value);
      return Result.Success(nvalue);
    }
    public override Result SetValue(string name, object value) {
      //_catalog.SetValue(value??)
      return Result.Failure("not implemented");
    }
    public override Result Evaluate(string name, params object[] arguments) {
      return Result.Failure("not implemented");
    }
    public override Result Command(string name, params object[] arguments) {
      return Result.Failure("not implemented");
    }
  }
}
