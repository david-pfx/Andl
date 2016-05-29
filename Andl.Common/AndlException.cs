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

namespace Andl.Common {
  public class AndlException : Exception {
    public AndlException(string message) : base(message) { }
  }

  // error in evaluator VM
  public class EvaluatorException : AndlException {
    public EvaluatorException(string message, params object[] args) : base("(Evaluator): " + String.Format(message, args)) { }
  }

  // error in runtime support libraries
  public class RuntimeException : AndlException {
    public RuntimeException(string message, params object[] args) : base("(Runtime): " + String.Format(message, args)) { }
  }

  // error in sql libraries
  public class SqlException : AndlException {
    public SqlException(string message, params object[] args) : base("(Sql): " + String.Format(message, args)) { }
  }

  // error in application program code or data
  public class ProgramException : AndlException {
    public ErrorKind Kind = ErrorKind.Error;
    public override string ToString() {
      return String.Format("({0}): {1}", Source, Message);
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

    static ProgramException Throwable(ErrorKind kind, string source, string message) {
      return new ProgramException(message) {
        Kind = kind,
        Source = source,
      };
    }

    // common code for errors that can be handled
    static void Raise(ErrorKind kind, string source, string message) {
      bool handled = (kind != ErrorKind.Panic && ErrorEvent != null && ErrorEvent(source, message))
        || kind == ErrorKind.Warn;
      if (!handled) {
        //string msg = "Fatal error " + source + ": " + message;
        if (kind == ErrorKind.Warn)
          Logger.WriteLine("Program error ({0}): {1}", source, message);
        else throw Throwable(kind, source, message);
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
    public static ProgramException Fatal(string code, string format, params object[] args) {
      return Throwable(ErrorKind.Fatal, code, args.Length == 0 ? format : String.Format(format, args));
    }

    public static ProgramException Panic(string code, string format, params object[] args) {
      return Throwable(ErrorKind.Panic, code, args.Length == 0 ? format : String.Format(format, args));
    }

  }

}
