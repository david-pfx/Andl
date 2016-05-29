using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Andl.Common {
  /// <summary>
  /// Parse some options and filenames
  /// </summary>
  public class OptionParser {
    public string GetPath(int n) {
      return (n < _paths.Count) ? _paths[n] : null;
    }

    List<string> _paths = new List<string>();
    Dictionary<string, Action<string>> _options;
    string _help;

    public static OptionParser Create(Dictionary<string, Action<string>> options, string help) {
      return new OptionParser { _options = options, _help = help };
    }

    public bool Parse(string[] args) {
      for (var i = 0; i < args.Length; ++i) {
        if (args[i].StartsWith("/") || args[i].StartsWith("-")) {
          if (!Option(args[i].Substring(1), args[i].Substring(2, args[i].Length - 2)))
            return false;
        } else _paths.Add(args[i]);
      }
      return true;
    }

    // Capture the options
    bool Option(string arg, string rest) {
      if (arg == "?") {
        Logger.WriteLine(_help);
        return false;
      } else if (Regex.IsMatch(arg, "[0-9]+")) {
        Logger.Level = int.Parse(arg);
      } else if (_options.ContainsKey(arg)) {
        _options[arg](rest);
      } else {
        Logger.WriteLine("*** Bad option: {0}", arg);
        return false;
      }
      return true;
    }
  }

}
