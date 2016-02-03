using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Pegasus.Common;
using System.IO;
using Andl.Runtime;
using System.Text.RegularExpressions;

namespace Andl.Peg {
  /// <summary>
  /// Special exception to handle parse errors and enable restart of parser
  /// </summary>
  public class ParseException : Exception {
    public Cursor State { get; set; }
    public ParseException(Cursor state, string message) : base(message) {
      State = state;
    }
  }

  /// <summary>
  /// Additional parsing functions for Pegasus parser
  /// </summary>
  partial class PegParser {
    public TextWriter Output { get; set; }
    public TypeSystem Types { get; set; }
    public SymbolTable Symbols { get; set; }
    public AstFactory Factory { get; set; }
    public Catalog Cat { get; set; }
    public int ErrorCount = 0;
    public bool Done = false;
    public string InputText { get; private set; }
    public Cursor State { get; private set; }

    Stack<string> _inputpaths = new Stack<string>();
    bool _skip = false;

    public PegParser() {
      Factory = new AstFactory { Parser = this };
      Types = new TypeSystem { Parser = this };
    }

    public AstFactory AST(Cursor state) {
      State = state;
      PrintLine(state);
      return Factory;
    }

    List<int> _linestarts = new List<int>();
    int _last_location = -1;

    // Initialise catalog and import symbols, but not until parse has started
    bool _started = false;
    public bool Start() {
      if (_started) return false;
      Logger.WriteLine(4, "Start parse");
      Cat.Start();
      Symbols.ResetScope();
      Symbols.Import(Cat.GlobalVars);
      Symbols.Import(Cat.PersistentVars);
      return true;
    }

    // Called to restart parse after error
    public AstStatement Restart(ref Cursor state) {
      Logger.WriteLine(4, "Restart parse skip={0} line={1} column={2} location={3}", _skip, state.Line, state.Column, state.Location);
      var cursor = state.WithMutability(mutable: false);
      // next line only needed if memoize active
      //this.storage = new Dictionary<CacheKey, object>();

      var restart = _skip;
      _skip = false;
      var result = (restart) ? MainRestart(ref cursor) : MainNext(ref cursor);
      return result.Value;
    }

    // Get a single line of source code from position
    string GetLine(string s, int pos) {
      var posx = s.IndexOf('\n', pos);
      var line = (posx == -1) ? s.Substring(pos) : s.Substring(pos, posx - pos);
      return line.Trim('\r', '\n');
    }

    // true predicate to print the line containing the current location
    // due to backtracking may be called more than once, so just do once and not off end
    public bool PrintLine(Cursor state, bool force = false) {
      var bol = state.Location - state.Column + 1;
      if (bol > _last_location) {
        _linestarts.Add(bol);
        Symbols.FindIdent("$lineno$").Value = NumberValue.Create(state.Line);
        if (Logger.Level >= 1 || force)
          Output.WriteLine("{0,3}: {1} {2}", state.Line, GetLine(state.Subject, bol),
            (Logger.Level >= 4) ? " <bol="+bol.ToString()+">" : "");
        _last_location = bol;
      }
      return true;
    }

    // Parser output error message and throw
    public bool Error(Cursor state, string message, params object[] args) {
      State = state;
      ParseError(State, message, args);
      return false; // never happens
    }

    // Internal call, mostly type errors
    public void ParseError(string message, params object[] args) {
      ParseError(State, message, args);
    }

    // common error handling -- set skip and throw
    void ParseError(Cursor state, string message, params object[] args) {
      Logger.WriteLine(4, "Error msg='{0}' line={1} column={2} location={3}", message, state.Line, state.Column, state.Location);
      PrintLine(state, true);
      var offset = state.Location - _linestarts.Last();
      if (offset > 0) Output.WriteLine("      {0}^", new string(' ', offset));
      Output.WriteLine("Error: {0}!", String.Format(message, args));
      ErrorCount++;
      _skip = true;
      throw new ParseException(state, message);
    }

    ///============================================================================================
    ///
    /// Handle input files
    ///

    // Load up the first source file
    public void Start(TextReader input, string filename) {
      Symbols.FindIdent("$filename$").Value = TextValue.Create(filename);
      _inputpaths.Push(filename);
      InputText = input.ReadToEnd();
    }

    // Insert a file into this one
    public bool Include(string input) {
      if (!File.Exists(input)) return false;
      ParseError("#include not supported");
      using (StreamReader sr = File.OpenText(input)) {
        Symbols.FindIdent("$filename$").Value = TextValue.Create(input);
        _inputpaths.Push(input);
        InputText = InputText.Insert(State.Location, sr.ReadToEnd());
        //State.Subject = InputText; <<-- no can do
        _inputpaths.Pop();
        Symbols.FindIdent("$filename$").Value = TextValue.Create(_inputpaths.Peek());
      }
      return true;
    }

    ///============================================================================================
    ///
    /// Handle directives
    ///

    string CatalogDirective(Cursor state, IList<string> options) {
      State = state;
      Cat.LoadFlag = !options.Any(o => o == "new");
      Cat.SaveFlag = options.Any(o => o == "update");
      return "";
    }
    string IncludeDirective(Cursor state, string path) {
      State = state;
      if (!Include(path))
        ParseError("cannot include '{0}'", path);
      return "";
    }
    string NoisyDirective(Cursor state, string level) {
      State = state;
      Logger.Level = int.Parse(level);
      return "";
    }
    string SourceDirective(Cursor state, string path) {
      State = state;
      Cat.SourcePath = (path.Length >= 2) ? Unquote(path) : "";
      return "";
    }
    string StopDirective(Cursor state, string level) {
      State = state;
      if (level != "") Logger.Level = int.Parse(level);
      Done = true;
      throw new ParseException(null, null);
    }

    ///============================================================================================
    ///
    /// utility and semantic functions
    /// 

    public bool SetState(Cursor state) {
      State = state;
      return true;
    }

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
      var sym = Symbols.FindIdent(name);
      return (sym != null && sym.IsUserType) || Types.Find(name) != null;
    }

    bool IsSourceName(string name) {
      return Symbols.IsSource(name);
    }

    bool CanDefGlobal(string name) {
      return Symbols.CanDefGlobal(name);
    }

    bool CanDefLocal(string name) {
      return Symbols.CanDefLocal(name);
    }

    bool IsField(string name) {
      var sym = Symbols.FindIdent(name);
      return sym != null && sym.IsField;
    }
    bool IsComponent(string name) {
      var sym = Symbols.FindIdent(name);
      return sym != null && sym.IsComponent;
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

    string NumToStr(IList<string> strs, int radix) {
      var nums = strs.Select(s => Int32.Parse(s, radix == 16 ? System.Globalization.NumberStyles.AllowHexSpecifier : System.Globalization.NumberStyles.None));
      return string.Concat(nums.Select(n => Char.ConvertFromUtf32(n)));
    }
    string Unquote(string s) {
      if (Regex.IsMatch(s, "^'.*'$") || Regex.IsMatch(s, "^\".*\"$"))
        return s.Substring(1, s.Length - 2);
      else return s;
    }

  }
}
