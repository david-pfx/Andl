using System;
using System.Collections.Generic;
using System.Linq;
using Pegasus.Common;
using System.IO;
using Andl.Runtime;
using System.Text.RegularExpressions;
using Andl.Common;

namespace Andl.Peg {
  /// <summary>
  /// Special exception to handle parse errors and enable restart of parser
  /// </summary>
  public class ParseException : Exception {
    public Cursor State { get; private set; }
    public bool Stop { get; private set; }
    public ParseException(Cursor state, string message, bool stop = false) : base(message) {
      State = state;
      Stop = stop;
    }
  }

  /// <summary>
  /// Implement holder for include files
  /// </summary>
  public class ParserInput {
    public ParserInput Parent;
    public string Filename;
    public string InputText;
    public Cursor State;
    public List<int> LineStarts = new List<int>();
    public int LastLocation = -1;
    public bool IsIncluded { get { return Parent != null; } }
    public int Level { get { return Parent == null ? 0 : 1 + Parent.Level; } }
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
    public ParserInput NewInput { get; set; }
    public string InputText {  get { return _input.InputText; } }
    public Cursor State {
      get { return _input.State; }
      set { _input.State = value; }
    }

    ParserInput _input;
    bool _started = false;
    bool _skip = false;       // error restart via skip to next

    public PegParser() {
      Factory = new AstFactory { Parser = this };
      Types = new TypeSystem { Parser = this };
    }

    public AstFactory AST(Cursor state) {
      State = state;
      PrintLine(state);
      return Factory;
    }

    // Import symbols, but not until parse has started
    public bool Start() {
      if (_started) return false;
      Logger.WriteLine(4, "Start parse");
      // critical: enable cataloguing here and not before
      Cat.BeginSession(SessionState.Full);
      Symbols.ResetScope();
      Symbols.Import(Cat.GlobalVars);
      Symbols.Import(Cat.PersistentVars);
      return true;
    }

    // Called to restart parse after possible error or switched input
    public AstStatement Restart(ref Cursor state) {
      Logger.WriteLine(4, "Restart parse skip={0} line={1} column={2} location={3}", _skip, state.Line, state.Column, state.Location);
      var cursor = state.WithMutability(mutable: false);
      // next line only needed if memoize active
      //this.storage = new Dictionary<CacheKey, object>();

      // true if was error, need to skip to known good point
      var restart = _skip;
      _skip = false;
      _input.State = cursor;
      var result = (restart) ? MainRestart(ref cursor) : MainNext(ref cursor);
      return result.Value;
    }

    // Push state stack
    public bool TryPushState() {
      if (NewInput == null) return false;
      NewInput.Parent = _input;
      NewInput.State = new Cursor(NewInput.InputText, 0, NewInput.Filename);
      _input = NewInput;
      NewInput = null;
      return true;
    }

    // Pop state stack or return false
    public bool TryPopState() {
      if (_input.Parent == null) return false;
      _input = _input.Parent;
      return true;
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
      if (NewInput != null) return true;   // suppress printing if #include pending
      var bol = state.Location - state.Column + 1;
      if (bol > _input.LastLocation) {
        _input.LineStarts.Add(bol);
        Symbols.FindIdent("$lineno$").Value = NumberValue.Create(state.Line);
        if (Logger.Level >= 1 || force)
          Output.WriteLine("{0}{1,3}: {2} {3}", 
            new string('>', _input.Level),
            state.Line, GetLine(state.Subject, bol),
            (Logger.Level >= 4) ? " <bol="+bol.ToString()+">" : "");
        _input.LastLocation = bol;
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
      var offset = state.Location - _input.LineStarts.Last();
      if (offset > 0) Output.WriteLine("      {0}^", new string(' ', offset));
      Output.WriteLine("Error: {0}!", String.Format(message, args));
      _skip = true;
      throw new ParseException(state, message);
    }

    ///============================================================================================
    ///
    /// Handle input files
    ///

    // Load up the first source file
    public void LoadSource(TextReader input, string filename) {
      Symbols.FindIdent("$filename$").Value = TextValue.Create(filename);
      _input = new ParserInput {
        InputText = input.ReadToEnd()
      };
    }

    // Insert a file into this one
    // TODO: pass new input back up the tree
    public bool Include(string filename) {
      if (!File.Exists(filename)) return false;
      using (StreamReader sr = File.OpenText(filename)) {
        NewInput = new ParserInput {
          Filename = filename, InputText = sr.ReadToEnd(), 
        };
        Symbols.FindIdent("$filename$").Value = TextValue.Create(filename);
      }
      return true;
    }

    ///============================================================================================
    ///
    /// Handle directives
    ///

    // #catalog with options additional to any command line switches !?
    string CatalogDirective(Cursor state, IList<string> options) {
      State = state;
      Cat.LoadFlag = !options.Any(o => o == "new");
      Cat.SaveFlag |= options.Any(o => o == "update");
      Cat.SqlFlag |= options.Any(o => o == "sql");
      Cat.Directive();
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
      try {
        Cat.SourcePath = Path.GetFullPath(path);
      } catch {
        ParseError("invalid path '{0}'", path);
      }
      return "";
    }
    string StopDirective(Cursor state, string level) {
      State = state;
      if (level != "") Logger.Level = int.Parse(level);
      throw new ParseException(null, null, true);
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

    bool CanDefGlobal(string name) { return Symbols.CanDefGlobal(name); }
    bool CanDefLocal(string name) { return Symbols.CanDefLocal(name); }
    bool IsBinop(string name) { return IsKind(name, (s) => s.IsBinary); }
    bool IsCatVar(string name) { return IsKind(name, (s) => s.IsCatVar); }
    bool IsComponent(string name) { return IsKind(name, (s) => s.IsComponent); }
    bool IsDo(string name) { return IsKind(name, (s) => s.IsDo); }
    bool IsField(string name) { return IsKind(name, (s) => s.IsField); }
    bool IsFold(string name) { return IsKind(name, (s) => s.IsFold); }
    bool IsFoldable(string name) { return IsKind(name, (s) => s.IsFoldable); }
    bool IsFuncop(string name) { return IsKind(name, (s) => s.IsCallable); }
    bool IsIf(string name) { return IsKind(name, (s) => s.IsIf); }
    bool IsRestrict(string name) { return IsKind(name, (s) => s.IsRestrict); }
    bool IsSourceName(string name) { return Symbols.IsSource(name); }
    bool IsUnop(string name) { return (name == "-") || IsKind(name, (s) => s.IsUnary); }
    bool IsVariable(string name) { return IsKind(name, (s) => s.IsVariable); }
    bool IsWhile(string name) { return IsKind(name, (s) => s.IsWhile); }

    // generic symbol type checker
    bool IsKind(string name, Func<Symbol, bool> func) {
      var sym = Symbols.FindIdent(name);
      return sym != null && func(sym);
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
