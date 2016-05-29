using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Andl.Common {
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

  public enum ExecModes { Raw, JsonString, JsonArray, JsonObj };

  // support program execution
  public interface IExecuteGateway {
    Result RunScript(string program, ExecModes kind = ExecModes.Raw, string sourcename = null);
  }
}
