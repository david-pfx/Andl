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
using System.Linq;
//using System.Reflection;
using System.Text;
//using System.Threading.Tasks;
using Andl.Runtime;

namespace Andl.Compiler {
  /// <summary>
  /// Manages the information required to call a function (method/operator/etc)
  /// 
  /// For Builtin methods, take info from MethodInfo
  /// For Defs, create as required
  /// </summary>
  public class CallInfo {
    // dictionary of functions discovered by reflection, some not yet linked for use
    static Dictionary<string, CallInfo> _callables = new Dictionary<string, CallInfo>();
    //static Dictionary<string, CallInfo> Callables = new Dictionary<string, CallInfo>();

    // function name
    public string Name { get; private set; }
    // system specific caller info
    public DataType ReturnType { get; private set; }
    // array of arguments as columns
    public DataColumn[] Arguments { get; private set; }
    // chain for overloaded functions with same name
    public CallInfo OverLoad { get; private set; }
    // number of accumulators used
    public int AccumCount { get; set; }
    // declared number of arguments
    public int NumArgs { get { return Arguments.Length; } }
    // return true if a fold was used
    public bool HasFold { get { return AccumCount > 0; } }

    // flag indicating final init
    bool _finalinit = false;

    // find the call info by name (including overloads)
    public static CallInfo Get(string name) {
      if (_callables.Count == 0)
        Init(BuiltinInfo.GetBuiltinInfo());
      var names = name.Split(',');
      // Chain overloads together, but make sure to only do this once, for static
      CallInfo callinfo = null;
      foreach (var overload in names) {
        Logger.Assert(_callables.ContainsKey(overload), overload);
        if (_callables[overload]._finalinit) break;
        Logger.Assert(_callables[overload].OverLoad == null, overload);
        _callables[overload].OverLoad = callinfo;
        _callables[overload]._finalinit = true;
        callinfo = _callables[overload];
      }
      return _callables[names.Last()];
    }

    // Create for user defined
    public static CallInfo Create(string name, DataType rettype, DataColumn[] argcols) {
      var ci = new CallInfo {
        Name = name,
        ReturnType = rettype,
        Arguments = argcols,
      };
      return ci;
    }

    // load up all the methods and index by name
    static void Init(BuiltinInfo[] builtins) {
      foreach (var builtin in builtins)
        _callables[builtin.Name] = CallInfo.Create(builtin.Name, builtin.ReturnType, builtin.Arguments);
    }

  }
}
