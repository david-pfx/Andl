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

  public enum RteCodes {
    CnvNvb,
    CnvNvn,
    CnvNvt,
    BinGxoor,
    BinSxoor,
    CatLfis,
    CatDbne,
  }

  public enum ErrorKind { 
    Warn,     // never fatal
    Error,    // fatal if not handled
    Fatal,    // call handler but always fatal
    Panic     // immediately fatal
  };

  public class RuntimeError {
    public delegate bool ErrorHandler(string code, string message);
    public static ErrorHandler ErrorEvent;

    public static void Raise(ErrorKind kind, string code, string message) {
      bool handled = (kind != ErrorKind.Panic && ErrorEvent != null && ErrorEvent(code, message))
        || kind == ErrorKind.Warn;
      if (!handled) {
        string msg = "Fatal error " + code + ": " + message;
        if (kind == ErrorKind.Warn)
          Console.WriteLine(msg);
        else {
          throw new RuntimeErrorException(msg) {
            Fatal = true,
            Code = code,
          };
        }
      }
    }
//    public static void Error(string code, string message) {
      //Raise(ErrorKind.Warn, code, message);
    //}

    public static void Warn(string code, string format, params object[] args) {
      Raise(ErrorKind.Warn, code, args.Length == 0 ? format : String.Format(format, args));
    }

    public static void Error(string code, string format, params object[] args) {
      Raise(ErrorKind.Error, code, args.Length == 0 ? format : String.Format(format, args));
    }

    public static void Fatal(string code, string format, params object[] args) {
      Raise(ErrorKind.Fatal, code, args.Length == 0 ? format : String.Format(format, args));
    }

    public static void Panic(string code, string format, params object[] args) {
      Raise(ErrorKind.Fatal, code, args.Length == 0 ? format : String.Format(format, args));
    }

  }

}
