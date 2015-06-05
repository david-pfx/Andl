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
using System.Text;
using System.Threading.Tasks;

namespace Andl.Runtime {
  public class EvaluatorException : Exception {
    public EvaluatorException(string message, params object[] args) : base("Evaluator error: " + String.Format(message, args)) { }
  }

  public class RuntimeException : Exception {
    public RuntimeException(string message, params object[] args) : base("Runtime error: " + String.Format(message, args)) { }
  }
  public class SqlException : Exception {
    public SqlException(string message, params object[] args) : base("Sql error: " + String.Format(message, args)) { }
  }

  public class RuntimeErrorException : Exception {
    public bool Fatal = true;
    public string Code = "Unspecified";
    public RuntimeErrorException(string message) : base(message) { }
  }

  public class RuntimeError {
    public static void Fatal(string code, string message, params object[] args) {
      var msg = code + ": " + String.Format(message, args);
      throw new RuntimeErrorException(msg) {
        Fatal = true,
        Code = code,
      };
    }

  }

}
