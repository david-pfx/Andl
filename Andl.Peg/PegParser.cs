using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Pegasus.Common;
using System.IO;
using Andl.Runtime;

namespace Andl.Peg {
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
    public TextWriter Output { get; set; }
    public TypeSystem Types { get; set; }
    public SymbolTable Symbols { get; set; }
    public AstFactory Factory { get; set; }
    public Catalog Cat { get; set; }
    //public int Noisy = 1;
    public int ErrorCount = 0;
    public bool Done = false;

    public AstFactory AST(Cursor state) {
      _aststate = state;
      return Factory;
    }

    Cursor _aststate;
    List<int> _linestarts = new List<int>();

    public AstBlock Restart(ref Cursor state) {
      var cursor = state.WithMutability(mutable: false);
      // next line on needed if memoize active
      //this.storage = new Dictionary<CacheKey, object>();
      Skip(ref cursor);
      var result = MainRestart(ref cursor); // FIX: check for null?
      return result.Value;
    }

    string GetLine(string s, int pos) {
      var posx = s.IndexOf('\n', pos);
      return (posx == -1) ? "" : s.Substring(pos, posx - pos).Trim('\r', '\n');
    }
    public void PrintLine(Cursor state) {
      _linestarts.Add(state.Location);
      if (Logger.Level > 0)
        Output.WriteLine("{0,4}: {1}", state.Line, GetLine(state.Subject, state.Location));
    }

    public void ParseError(Cursor state, string message = "unknown", params object[] args) {
      var offset = state.Location - _linestarts.Last();
      if (offset > 0) Output.WriteLine("      {0}^", new string(' ', offset));
      Output.WriteLine("Error: {0}!", String.Format(message, args));
      ErrorCount++;
      throw new ParseException(state, message);
    }

    public void ParseError(string message = "unknown", params object[] args) {
      ParseError(_aststate, message, args);
    }

      ///============================================================================================
      ///
      /// Handle directives
      ///

      string CatalogDirective(IList<string> options) {
      //{ (CatalogOptions)Enum.Parse(typeof(CatalogOptions), v) }
      return "";
    }
    string IncludeDirective(string path) {
      return "";
    }
    string NoisyDirective(string level) {
      Logger.Level = int.Parse(level);
      return "";
    }
    string StopDirective(string level) {
      if (level != "") Logger.Level = int.Parse(level);
      Done = true;
      throw new ParseException(null, null);
    }

    ///============================================================================================
    ///
    /// scopes
    /// 

    bool DefScope(string ident, AstType rettype, IList<AstField> arguments) {
      var args = (arguments == null) ? new DataColumn[0] : arguments.Select(a => DataColumn.Create(a.Name, a.DataType)).ToArray();
      var rtype = (rettype == null) ? DataTypes.Unknown : rettype.DataType;
      Symbols.AddDeferred(ident, rtype, args);
      Scope.Push();
      foreach (var a in args)
        Symbols.AddVariable(a.Name, a.DataType, SymKinds.PARAM);
      return true;
    }

    public bool PushScope(AstValue value) {
      Scope.Push(value.DataType);
      //if (value.DataType.HasHeading) Scope.Push(value.DataType);
      return true;
    }

    bool PopScope() {
      Scope.Pop();
      return true;
    }

    ///============================================================================================
    ///
    /// utility and semantic functions
    /// 

    //public bool PopScope(AstValue value) {
    //  Scope.Pop();
    //  //if (value.DataType.HasHeading) Scope.Pop();
    //  return true;
    //}

    public bool Check(bool condition, Cursor state, string message) {
      if (!condition) ParseError(state, message);
      return true;
    }

    public bool IsRel(AstValue rel) {
      return rel.DataType is DataTypeRelation;
    }

    T Single<T>(IList<T> list) where T : class {
      return list.Count > 0 ? list[0] : null;
    }

    bool IsTypename(string name) {
      return Types.Find(name) != null;
    }

    bool IsSourceName(string name) {
      return Symbols.IsSource(name);
    }

    bool IsDefinable(string name) {
      return Symbols.IsDefinable(name);
    }

    bool IsField(string name) {
      var sym = Symbols.FindIdent(name);
      return sym != null && sym.IsField;
    }
    bool IsCatVar(string name) {
      var sym = Symbols.FindIdent(name);
      return sym != null && sym.IsCatVar;
    }
    bool IsVariable(string name) {
      var sym = Symbols.FindIdent(name);
      return sym != null && sym.IsVariable;
    }
    bool IsFuncop(string name) {
      var sym = Symbols.FindIdent(name);
      return sym != null && sym.IsCallable;
    }
    bool IsBinop(string name) {
      var sym = Symbols.FindIdent(name);
      return sym != null && sym.IsBinary;
    }
    bool IsUnop(string name) {
      var sym = Symbols.FindIdent(name);
      return sym != null && sym.IsUnary;
    }
    bool IsFoldableop(string name) {
      var sym = Symbols.FindIdent(name);
      return sym != null && sym.IsFoldable;
    }

    public string NumToStr(IList<string> strs, int radix) {
      var nums = strs.Select(s => Int32.Parse(s, radix == 16 ? System.Globalization.NumberStyles.AllowHexSpecifier : System.Globalization.NumberStyles.None));
      return string.Concat(nums.Select(n => Char.ConvertFromUtf32(n)));
    }

  }
}
