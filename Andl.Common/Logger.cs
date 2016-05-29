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
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Andl.Common;

namespace Andl.Common {
  public class UtilAssertException : AndlException {
    internal UtilAssertException(string msg) : base(msg) { }
  }
  /// <summary>
  /// Operates a glass teletype on a supplied file or console
  /// 
  /// First level added is the driver and controls what is logger. 
  /// The rest are passive watchers: may see less, not more.
  /// Lines starting with ">" are timed
  /// Lines starting with "|" are not padded
  /// </summary>
  public static class Logger {
    static List<TextWriter> _tws = new List<TextWriter>();
    static List<int> _levels = new List<int>();
    public static TextWriter Out {
      get { CheckInit(); return _tws[0]; }
      set { CheckInit(); _tws[0] = value; }
    }
    public static int Level { 
      get { CheckInit(); return _levels[0]; } 
      set { CheckInit(); _levels[0] = value; } 
    }
    public static bool _neednl;

    static DateTime FirstTime = DateTime.Now;
    static DateTime LastTime = DateTime.Now;
    static DateTime TimeNow = DateTime.Now;

    public static void Open(int level) {
      Open(level, Console.Out);
    }
    public static void Open(int level, string name) {
      Open(level, new StreamWriter(name));
    }
    public static void Open(int level, TextWriter tw) {
      _levels.Add(level);
      _tws.Add(tw);
    }
    // special for writing to debugger trace output
    public static void OpenTrace(int level) {
      _levels.Add(level);
      _tws.Add(new TraceWriter());
    }
    public static void Close() {
      foreach (var tw in _tws)
        tw.Close();
      _tws.Clear();
      _levels.Clear();
    }

    public static void Flush() {
      for (int i = 0; i < _levels.Count; ++i) {
        _tws[i].Flush();
      }
    }

    public static void Write(int level, string msg) {
      Write(level, msg, false);
    }
    public static void WriteLine(int level, string msg) {
      Write(level, msg, true);
    }
    public static void WriteLine(int level) {
      Write(level, "", true);
    }
    public static void Write(int level, string format, params object[] args) {
      Write(level, String.Format(format, args), false);
    }
    public static void WriteLine(int level, string format, params object[] args) {
      Write(level, String.Format(format, args), true);
    }
    public static void WriteLine(string format, params object[] args) {
      Write(0, String.Format(format, args), true);
    }
    public static void Assert(bool test, string format, params object[] args) {
      if (!test) {
        var msg = String.Format(format, args);
        //Write(0, msg, true);
        Flush();
        throw new UtilAssertException(msg);
      }
    }
    public static void Assert(bool test, object thing = null) {
      if (!test) {
        var msg = (thing == null ? "" : ("<" + thing.ToString() + ">"));
        Flush();
        throw new UtilAssertException(msg);
      }
    }
    public static UtilAssertException Fatal(object thing = null) {
      var msg = (thing == null ? "" : ("<" + thing.ToString() + ">"));
      Flush();
      return new UtilAssertException(msg);
    }

    //--- impl

    static void CheckInit() {
      if (_levels.Count == 0)
        Open(0);
    }

    // common writer
    public static void Write(int level, string msg, bool newline) {
      if (level > Level) return;    // base level controls what is logged
      TimeNow = DateTime.Now;
      for (int i = 0; i < _levels.Count; ++i) {
        if (level <= _levels[i]) {
          if (newline) {
            if (_neednl) _tws[i].WriteLine();
            _tws[i].WriteLine(Pad(level, msg));
          } else
            _tws[i].Write((_neednl) ? msg + ";" : Pad(level, msg) + ";");
        }
      }
      LastTime = TimeNow;
      _neednl = !newline;
    }

    static string Pad(int level, string msg) {
      bool timeit = msg.StartsWith(">");
      bool nopad = msg.StartsWith("|");
      string ret;
      if (timeit) {
        ret = String.Format(@"{0:mm\:ss\.fff} {1,3:N0}ms : ", TimeNow - FirstTime, (TimeNow - LastTime).TotalMilliseconds);
      } else if (nopad) ret = "";
      else ret = new string(' ', 2 * level);
      return ret + msg;
    }

  }

  /// <summary>
  /// Implement writer using debug output
  /// </summary>
  class TraceWriter : TextWriter {
    public override Encoding Encoding {
      get { return Encoding.Default; }
    }
    public override void Write(string text) {
      Trace.Write(text);
    }
    public override void WriteLine() {
      Trace.WriteLine("");
    }
  }
}
