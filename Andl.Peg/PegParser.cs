using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Pegasus.Common;

namespace Andl.Peg {

  public interface IParser {

  }

  /// <summary>
  /// 
  /// </summary>
  public class ParseException : Exception {
    public Cursor State { get; set; }
    public ParseException(Cursor state, string message) : base(message) {
      State = state;
    }
  }

  /// <summary>
  /// 
  /// </summary>
  partial class PegParser {
    public TypeSystem Types;
    public SymbolTable SymbolTable;
    public AstFactory AST;
    public int Noisy = 1;
    public int ErrorCount = 0;

    List<int> _linestarts = new List<int>();

    public AstBlock Restart(ref Cursor state) {
      var cursor = state.WithMutability(mutable: false);
      Skip(ref cursor);
      var result = Main(ref cursor); // FIX: check for null?
      return result.Value;
    }

    string GetLine(string s, int pos) {
      var posx = s.IndexOf('\n', pos);
      return (posx == -1) ? "" : s.Substring(pos, posx - pos).Trim('\r', '\n');
    }
    public void PrintLine(Cursor state) {
      _linestarts.Add(state.Location);
      if (Noisy > 0)
        Console.WriteLine("{0,4}: {1}", state.Line, GetLine(state.Subject, state.Location));
    }

    public void ParseError(Cursor state, string message = "unknown") {
      var offset = state.Location - _linestarts.Last();
      if (offset > 0) Console.WriteLine("      {0}^", new string(' ', offset));
      Console.WriteLine("Error: {0}!", message);
      ErrorCount++;
      throw new ParseException(state, message);
    }

    string CatalogDirective(IList<string> options) {
      //{ (CatalogOptions)Enum.Parse(typeof(CatalogOptions), v) }
      return "";
    }
    string IncludeDirective(string path) {
      return "";
    }
    string NoisyDirective(string level) {
      Noisy = int.Parse(level);
      return "";
    }
    string StopDirective(string level) {
      if (level != "") Noisy = int.Parse(level);
      return "";
    }

    // utility
    T Single<T>(IList<T> list) where T : class {
      return list.Count > 0 ? list[0] : null;
    }

    bool IsTypeName(string name) {
      return Types.Find(name) != null;
    }

    string[] source_names = new string[] {
      "csv", "txt", "sql", "con", "file", "oledb", "odbc"
      };
    bool IsSourceName(string name) {
      return source_names.Contains(name);
    }

  }
}
