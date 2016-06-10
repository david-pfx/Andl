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
using Andl.Runtime;
using Andl.Common;

namespace Andl.Peg {
  /// <summary>
  /// Manages the information required to call a function (method/operator/etc)
  /// 
  /// For Builtin methods, take info from MethodInfo
  /// For Defs, create as required
  /// </summary>
  public class CallInfo {
    // dictionary of functions discovered by reflection, some not yet linked for use
    static Dictionary<string, CallInfo> _callables = new Dictionary<string, CallInfo>();

    // function name
    public string Name { get; private set; }
    // system specific caller info
    public DataType ReturnType { get; set; }
    // array of arguments as columns
    public DataColumn[] Arguments { get; private set; }
    // chain for overloaded functions with same name
    public CallInfo OverLoad { get; private set; }
    // number of accumulators used
    public int AccumCount { get; set; }
    // declared number of arguments
    public int NumArgs { get { return Arguments == null ? 0 : Arguments.Length; } }
    // return true if a fold was used
    public bool HasFold { get { return AccumCount > 0; } }
    // return true if an ordered func was used
    public bool HasWin { get; set; }

    // flag indicating final init
    bool _finalinit = false;

    // find the call info by name (including overloads), return a chain
    public static CallInfo Get(string name) {
      if (_callables.Count == 0)
        Init(BuiltinInfo.GetBuiltinInfo());
      var ovnames = name.Split(',');
      // Chain overloads together, but make sure to only do this once, for static
      CallInfo callinfo = null;
      foreach (var ovname in ovnames) {
        Logger.Assert(_callables.ContainsKey(ovname), ovname);
        if (_callables[ovname]._finalinit) break;
        Logger.Assert(_callables[ovname].OverLoad == null, ovname);
        _callables[ovname].OverLoad = callinfo;
        _callables[ovname]._finalinit = true;
        callinfo = _callables[ovname];
      }
      return _callables[ovnames.Last()];
    }

    // Create for user defined
    public static CallInfo Create(string name, DataType rettype, DataColumn[] argcols, int accums = 0, CallInfo overload = null) {
      var ci = new CallInfo {
        Name = name,
        ReturnType = rettype,
        Arguments = argcols,
        AccumCount = accums,
        OverLoad = overload,
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
