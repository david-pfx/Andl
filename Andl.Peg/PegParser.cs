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

    public PegParser() {
      Factory = new AstFactory { Parser = this };
      Types = new TypeSystem { Parser = this };
    }

    public AstFactory AST(Cursor state) {
      State = state;
      return Factory;
    }

    List<int> _linestarts = new List<int>();
    int _last_location = -1;

    // Initialise catalog and import symbols, but not until parse has started
    bool _started = false;
    public bool Start() {
      if (_started) return false;
      Cat.Start();
      Symbols.Import(Cat.GlobalVars);
      Symbols.Import(Cat.PersistentVars);
      _started = true;
      return true;
    }

    // Called to restart parse after error
    public AstStatement Restart(ref Cursor state) {
      var cursor = state.WithMutability(mutable: false);
      // next line only needed if memoize active
      //this.storage = new Dictionary<CacheKey, object>();
      Skip(ref cursor);
      var result = MainRestart(ref cursor); // FIX: check for null?
      return result.Value;
    }

    // Get a single line of source code from position
    string GetLine(string s, int pos) {
      var posx = s.IndexOf('\n', pos);
      return (posx == -1) ? "" : s.Substring(pos, posx - pos).Trim('\r', '\n');
    }

    // print the line starting here
    // due to backtracking may be called more than once, so just do once and not off end
    public void PrintLine(Cursor state) {
      if (state.Location > _last_location && state.Location < state.Subject.Length) {
        _linestarts.Add(state.Location);
        Symbols.FindIdent("$lineno$").Value = NumberValue.Create(state.Line);
        if (Logger.Level > 0)
          Output.WriteLine("{0,3}: {1}", state.Line, GetLine(state.Subject, state.Location));
        _last_location = state.Location;
      }
    }

    // Output error message and throw
    public void ParseError(Cursor state, string message = "unknown", params object[] args) {
      var offset = state.Location - _linestarts.Last();
      if (offset > 0) Output.WriteLine("      {0}^", new string(' ', offset));
      Output.WriteLine("Error: {0}!", String.Format(message, args));
      ErrorCount++;
      throw new ParseException(state, message);
    }

    // error message wrapper
    public void ParseError(string message = "unknown", params object[] args) {
      ParseError(State, message, args);
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

    bool IsDefinable(string name) {
      return Symbols.IsDefinable(name);
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

    public string NumToStr(IList<string> strs, int radix) {
      var nums = strs.Select(s => Int32.Parse(s, radix == 16 ? System.Globalization.NumberStyles.AllowHexSpecifier : System.Globalization.NumberStyles.None));
      return string.Concat(nums.Select(n => Char.ConvertFromUtf32(n)));
    }

  }
}
