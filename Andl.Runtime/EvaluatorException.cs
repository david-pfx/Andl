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
  // error in evaluator VM
  public class EvaluatorException : Exception {
    public EvaluatorException(string message, params object[] args) : base("Evaluator error: " + String.Format(message, args)) { }
  }

  // error in runtime support libraries
  public class RuntimeException : Exception {
    public RuntimeException(string message, params object[] args) : base("Runtime error: " + String.Format(message, args)) { }
  }

  // error in sql libraries
  public class SqlException : Exception {
    public SqlException(string message, params object[] args) : base("Sql error: " + String.Format(message, args)) { }
  }

  // error in application program code or data
  public class ProgramException : Exception {
    public ErrorKind Kind = ErrorKind.Error;
    public override string ToString() {
      return String.Format("Program error ({0}): {1}", Source, Message);
    }

    public ProgramException(string message) : base(message) { }
  }

  public enum ErrorKind { 
    Warn,     // never fatal
    Error,    // fatal if not handled
    Fatal,    // call handler but always fatal
    Panic     // immediately fatal
  };

  public class ProgramError {
    public delegate bool ErrorHandler(string code, string message);
    public static ErrorHandler ErrorEvent;

    public static void Raise(ErrorKind kind, string source, string message) {
      bool handled = (kind != ErrorKind.Panic && ErrorEvent != null && ErrorEvent(source, message))
        || kind == ErrorKind.Warn;
      if (!handled) {
        //string msg = "Fatal error " + source + ": " + message;
        if (kind == ErrorKind.Warn)
          Logger.WriteLine("Program error ({0}): {1}", source, message);
        else {
          throw new ProgramException(message) {
            Kind = kind,
            Source = source,
          };
        }
      }
    }

    // Will always return
    public static void Warn(string code, string format, params object[] args) {
      Raise(ErrorKind.Warn, code, args.Length == 0 ? format : String.Format(format, args));
    }

    // Will return if handled
    public static void Error(string code, string format, params object[] args) {
      Raise(ErrorKind.Error, code, args.Length == 0 ? format : String.Format(format, args));
    }

    // Will never return
    public static void Fatal(string code, string format, params object[] args) {
      Raise(ErrorKind.Fatal, code, args.Length == 0 ? format : String.Format(format, args));
    }

    public static void Panic(string code, string format, params object[] args) {
      Raise(ErrorKind.Fatal, code, args.Length == 0 ? format : String.Format(format, args));
    }

  }

}
