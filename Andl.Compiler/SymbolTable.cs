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
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Andl.Runtime;

namespace Andl.Compiler {
  /// <summary>
  /// Implements a lexical atom ie a token in the input stream
  /// </summary>
  public enum Atoms {
    NUL,
    EOF,      // eod of file
    EOL,      // end of line
    MARK,     // special for expression parser
    ALIAS,    // special for redirect
    SYMBOL,   // known symbol that cannot be an identifier
    LITERAL,  // text literal, cannot be an identifier
    IDENT,    // possible identifier or may have another meaning
    IDLIT,    // possible identifier but otherwise is a string
    // syntactic tokens
    LP, RP, LB, RB, LC, RC, SEP,
    NOT, STAR, LA, RA, COLON,
    PLUS, MINUS, DOLLAR, PERCENT,
    DO, DOT, QUERY,
    DEF,
  };

  /// <summary>
  /// Implements a defined meaning during expression evaluation
  /// </summary>
  public enum SymKinds {
    NUL,
    UNDEF,      // not defined
    LITERAL,    // value known at compile time
    FIELD,      // value during tuple iteration
    CATVAR,     // value as catalog variable
    PARAM,      // value from local declaration
    UNOP,       // unary operator
    BINOP,      // binary operator
    FUNC,       // callable function with args
    FOLD,       // special function
    IF,         // special function
    DEVICE,     // special function
    WITH,       // special function
    VALUE,      // special function
    RANK,       // special function
    SELECTOR,   // for UDT
    COMPONENT,  // for UDT
    SOURCE,
    DB,
  }

  /// <summary>
  /// Define how this function should be called
  /// </summary>
  public enum CallKinds {
    NUL,      // Not
    FUNC,     // Simple function
    VFUNC,    // Function with variable args (CodeValue)
    VFUNCT,   // Ditto, TypedValue
    JFUNC,    // Dyadic join-like
    EFUNC,    // Expression code
    LFUNC,    // tuple lookup function
    UFUNC,    // UDT lookup function
    SFUNC,    // SELECTOR for udt
    CFUNC,    // compile-time function
  };

  /// <summary>
  /// Join operation implemented by this function
  /// Note: LCR must be numerically same as MergeOps
  /// </summary>
  [Flags]
  public enum JoinOps {
    // basic values
    NUL, LEFT = 1, COMMON = 2, RIGHT = 4, 
    SETL = 8, SETC = 16, SETR = 32, 
    ANTI = 64, SET = 128, REV = 256, ERROR = 512,
    // mask combos
    MERGEOPS = LEFT | COMMON | RIGHT,
    SETOPS = SETL | SETC | SETR,
    OTHEROPS = ANTI | REV | ERROR,
    // joins
    JOIN = LEFT | COMMON | RIGHT, 
    COMPOSE = LEFT | RIGHT,
    DIVIDE = LEFT,
    RDIVIDE = RIGHT,
    SEMIJOIN = LEFT | COMMON, 
    RSEMIJOIN = RIGHT | COMMON,
    // antijoins
    ANTIJOIN = ANTI | LEFT | COMMON,
    ANTIJOINL = ANTI | LEFT,
    RANTIJOIN = ANTI | RIGHT | COMMON | REV,
    RANTIJOINR = ANTI | RIGHT | REV, 
    // set
    UNION = SET | COMMON | SETL | SETC | SETR,
    INTERSECT = SET | COMMON | SETC,
    SYMDIFF = SET | COMMON | SETL | SETR,
    MINUS = SET | COMMON | SETL,
    RMINUS = SET | COMMON | SETR | REV,
  };

  // Defines a foldable function
  public enum FoldableFlags {
    NUL, ANY, GROUP, ORDER
  };

  // Defines a fold function seed
  public enum FoldSeeds {
    NUL, ZERO, ONE, MIN, MAX, FALSE
  }
  
  /// <summary>
  /// Implements a symbol table entry.
  /// </summary>
  public class Symbol {
    public Atoms Atom { get; set; }
    public SymKinds Kind { get; set; }
    public DataType DataType { get; set; }
    public DataHeading Heading { get {
      return DataType is DataTypeRelation ? (DataType as DataTypeRelation).Heading
        : DataType is DataTypeTuple ? (DataType as DataTypeRelation).Heading
        : null; // FIX: symbol heading
    } }
    public string Name { get; set; }
    public int Precedence { get; set; }
    public int NumArgs { get; set; }
    public TypedValue Value { get; set; }
    public CallInfo CallInfo { get; set; }
    public TypedValue Seed { get; set; }
    public FoldSeeds FoldSeed { get; set; }
    public MergeOps MergeOp { get; set; }
    public JoinOps JoinKind { get; set; }
    public CallKinds CallKind { get; set; }
    public FoldableFlags Foldable { get; set; }
    public Symbol Link { get; set; }
    public Symbol[] ArgList { get; set; }
    public int Level { get; set; }
    
    public static Symbol None = new Symbol() { Atom = Atoms.NUL, Name = "" };
    public static Symbol Mark = new Symbol() { Atom = Atoms.MARK };

    public const string Assign = ":assign";
    public const string DoBlock = ":doblock";
    public const string Invoke = ":invoke";
    public const string Lift = ":lift";
    public const string Project = ":project";
    public const string Rename = ":rename"; 
    public const string Restrict = ":restrict";
    public const string Row = ":row";
    public const string Table = ":table";
    public const string Transform = ":transform";
    public const string TransAgg = ":transagg";
    public const string TransOrd = ":transord";
    public const string UpdateJoin = ":upjoin";
    public const string UpdateTransform = ":uptransform";
    public const string UserSelector = ":userselector";
    
    public override string ToString() {
      return String.Format("{0}:{1}:{2}:{3}", Name, Atom, Kind, Level);
    }

    // series of tests used by parser
    public bool Is(Atoms atom) { return Atom == atom; }
    public bool IsLiteral { get { return Atom == Atoms.LITERAL; } }
    public bool IsIdent { get { return Atom == Atoms.IDENT; } }
    public bool IsDefinable { get { return Atom == Atoms.IDENT && Level != Scope.Current.Level; } }
    public bool IsUndefIdent { get { return Atom == Atoms.IDENT && Kind == SymKinds.UNDEF; } }
    public bool IsField { get { return Kind == SymKinds.FIELD; } }
    public bool IsLookup { get { return Kind == SymKinds.FIELD || Kind == SymKinds.PARAM; } }
    public bool IsFoldable { get { return Foldable != FoldableFlags.NUL; } }
    public bool IsDyadic { get { return MergeOp != MergeOps.Nul; } }
    public bool IsUnary { get { return Kind == SymKinds.UNOP|| Atom == Atoms.MINUS; } }
    public bool IsBinary { get { return Kind == SymKinds.BINOP; } }
    public bool IsOperator { get { return IsBinary || IsUnary; } }
    public bool IsFunction { get { return CallKind != CallKinds.NUL && !IsOperator; } }
    public bool IsUserType { get { return Kind == SymKinds.SELECTOR || Kind == SymKinds.COMPONENT; } }
    public bool IsBuiltIn { get { return CallInfo != null; } }
    public bool IsDefFunc { get { return Atom == Atoms.IDENT && CallKind == CallKinds.EFUNC; } }
    public bool IsCompareOp { get { return IsBinary && DataType == DataTypes.Bool && !IsFoldable; } }
    public bool IsGlobal { get { return Level == 1; } }

    public TypedValue GetSeed(DataType datatype) {
      if (datatype is DataTypeRelation)
        return RelationValue.Create(DataTable.Create(datatype.Heading));

      switch (FoldSeed) {
      case FoldSeeds.NUL:
        if (datatype == DataTypes.Text) return TextValue.Default;
        break;
      case FoldSeeds.ZERO:
        if (datatype == DataTypes.Number) return NumberValue.Zero;
        break;
      case FoldSeeds.ONE:
        if (datatype == DataTypes.Number) return NumberValue.One;
        break;
      case FoldSeeds.MIN:
        if (datatype == DataTypes.Number) return NumberValue.Minimum;
        if (datatype == DataTypes.Time) return TimeValue.Minimum;
        break;
      case FoldSeeds.MAX:
        if (datatype == DataTypes.Number) return NumberValue.Maximum;
        if (datatype == DataTypes.Time) return TimeValue.Maximum;
        break;
      case FoldSeeds.FALSE:
        if (datatype == DataTypes.Bool) return BoolValue.False;
        break;
      default:
        break;
      }
      return null;
    }
  }

  ///-------------------------------------------------------------------

  /// <summary>
  /// SymbolTable implements the main compiler symbol table.
  /// </summary>
  public class SymbolTable {
    Scope _catalogscope;
    Catalog _catalog;

    //public static SymbolTable Create() {
    public static SymbolTable Create(Catalog catalog) {
      var st = new SymbolTable { _catalog = catalog };
      st.Init();
      return st;
    }

    //--- publics

    // Add a user-defined type
    // Need a selector (getters are private)
    public Symbol AddUserType(string name, DataTypeUser datatype) {
      _catalog.AddUserType(name, datatype);
      return Add(name, Create(name, datatype));
    }

    // define a variable at the current scope level (which must be undefined)
    public void DefineVar(Symbol symbol) {
      Logger.Assert(symbol.Atom == Atoms.IDENT && symbol.Kind == SymKinds.UNDEF);
      symbol.Kind = SymKinds.CATVAR;
      Scope.Current.Add(symbol);
    }

    // Find existing symbol by name
    public Symbol Find(string name) {
      return Scope.Current.FindAny(name);
    }

    // Get symbol from token, add to symbol table as needed
    // Look for existing symbol in catalog and nested scopes
    // If not found, define according to lexer type
    public Symbol GetSymbol(Token token) {
      // First look for existing symbol
      Symbol sym;
      if (token.IsDefinable) {
        sym = Find(token.Value);
        if (sym != null) {
          while (sym.Atom == Atoms.ALIAS)
            sym = sym.Link;
          return sym;
        }
      }
      // source code line token masquerades as another eol
      if (token.TokenType == TokenTypes.LINE)
        return Find(Token.EolName);
      // Create new symbol from token
      if (token.TokenType == TokenTypes.Number || token.TokenType == TokenTypes.HexNumber)
        sym = MakeLiteral(NumberValue.Create(token.GetNumber() ?? Decimal.Zero));
      else if (token.TokenType == TokenTypes.Time)
        sym = MakeLiteral(TimeValue.Create(token.GetTime() ?? DateTime.MinValue));
      else if (token.TokenType == TokenTypes.Identifier || token.TokenType == TokenTypes.IdLit)
        sym = MakeIdent();
      else if (token.TokenType == TokenTypes.Binary)
        sym = MakeLiteral(BinaryValue.Create(token.GetBinary()));
      else
        sym = MakeLiteral(TextValue.Create(token.Value));
      // note: only names for those we might define
      sym.Name = token.Value;
      return sym;
    }

    //--- setup

    void Init() {
      Scope.Push();
      AddSymbols();
      foreach (var info in AddinInfo.GetAddinInfo())
        AddBuiltinFunction(info.Name, info.NumArgs, info.DataType, info.Method);
      _catalogscope = Scope.Push();
      Add(_catalog);
    }

    // Process catalog to add all entries from persistent level
    // Called functions should discard duplicates, or flag errors???
    public void Add(Catalog catalog) {
      foreach (var entry in catalog.GetEntries(ScopeLevels.Persistent)) {
        var value = entry.Value;
        var datatype = (value.DataType == DataTypes.Code) ? (value as CodeValue).Value.DataType : value.DataType;
        if (_catalogscope.Find(entry.Name) == null)
          Logger.WriteLine(2, "From catalog add {0}:{1}", entry.Name, datatype.BaseType.Name);
        if (entry.Kind == EntryKinds.Type)
          _catalogscope.Add(Create(entry.Name, datatype as DataTypeUser));
        else _catalogscope.Add(new Symbol {
          Name = entry.Name,
          Atom = Atoms.IDENT,
          Kind = SymKinds.CATVAR,
          Value = value,
          DataType = datatype,
        });
      }
    }

    // Add a built in function (from a library)
    Symbol AddBuiltinFunction(string name, int numargs, DataType type, string method) {
      return AddFunction(name, Atoms.IDENT, numargs, type, CallKinds.FUNC, method, SymKinds.FUNC);
    }

    //------------------------------------------------------------------
    //-- ops

    static Symbol Create(string name, DataTypeUser datatype) {
      var callinfo = CallInfo.Create(name, datatype, datatype.Heading.Columns.ToArray());
      return new Symbol {
        Name = name,
        Atom = Atoms.IDENT,
        Kind = SymKinds.SELECTOR,
        CallKind = CallKinds.SFUNC,
        NumArgs = callinfo.NumArgs,
        DataType = datatype,
        CallInfo = callinfo,
      };
    }

    static Symbol MakeLiteral(TypedValue value) {
      Symbol sym = new Symbol {
        Atom = Atoms.LITERAL,
        Kind = SymKinds.LITERAL,
        DataType = value.DataType,
        Value = value
      };
      return sym;
    }

    static internal Symbol MakeIdent(string name = null) {
      Symbol sym = new Symbol {
        Name = name,
        Atom = Atoms.IDENT,
        Kind = SymKinds.UNDEF,
        DataType = DataTypes.Unknown,
      };
      return sym;
    }

    // Load and initialise the symbol table
    void AddSymbols() {
      AddSym(Token.EolName, Atoms.EOL);
      AddSym(Token.EofName, Atoms.EOF);
      AddSym("(", Atoms.LP);
      AddSym(")", Atoms.RP);
      AddSym("[", Atoms.LB);
      AddSym("]", Atoms.RB);
      AddSym("{", Atoms.LC);
      AddSym("}", Atoms.RC);
      AddSym(",", Atoms.SEP);
      AddSym(".", Atoms.DOT);
      AddSym("?", Atoms.QUERY);
      AddSym("$", Atoms.DOLLAR);
      AddSym("%", Atoms.PERCENT);
      AddSym(":", Atoms.COLON);
      AddSym("=>", Atoms.RA);
      AddSym(":=", Atoms.LA);
      AddSym("do", Atoms.DO);
      AddSym("def", Atoms.DEF);

      AddLiteral("true", BoolValue.True, DataTypes.Bool);
      AddLiteral("false", BoolValue.False, DataTypes.Bool);
      AddIdent("output", SymKinds.DEVICE, TextValue.Create("output"), DataTypes.Text);
      AddIdent("input", SymKinds.DEVICE, TextValue.Create("input"), DataTypes.Text);
      AddIdent("csv", SymKinds.SOURCE, TextValue.Create("csv"), DataTypes.Text);
      AddIdent("txt", SymKinds.SOURCE, TextValue.Create("txt"), DataTypes.Text);
      AddIdent("sql", SymKinds.SOURCE, TextValue.Create("sql"), DataTypes.Text);
      AddIdent("con", SymKinds.SOURCE, TextValue.Create("con"), DataTypes.Text);
      AddIdent("file", SymKinds.SOURCE, TextValue.Create("file"), DataTypes.Text);
      AddIdent("oledb", SymKinds.SOURCE, TextValue.Create("oledb"), DataTypes.Text);
      AddIdent("odbc", SymKinds.SOURCE, TextValue.Create("odbc"), DataTypes.Text);

      AddOperator("not", Atoms.NOT, 1, 9, DataTypes.Bool, "Not");
      AddOperator("**", Atoms.SYMBOL, 2, 9, DataTypes.Number, "Pow");
      AddOperator("u-", Atoms.MINUS, 1, 8, DataTypes.Number, "Neg");
      AddFoldableOp("*", Atoms.STAR, 2, 7, DataTypes.Number, FoldableFlags.ANY, FoldSeeds.ONE, "Multiply");
      AddFoldableOp("/", Atoms.SYMBOL, 2, 7, DataTypes.Number, FoldableFlags.ORDER, FoldSeeds.ONE, "Divide");
      AddOperator("div", Atoms.IDENT, 2, 7, DataTypes.Number, "Div");
      AddOperator("mod", Atoms.IDENT, 2, 7, DataTypes.Number, "Mod");
      AddFoldableOp("+", Atoms.PLUS, 2, 6, DataTypes.Number, FoldableFlags.ANY, FoldSeeds.ZERO, "Add");
      AddFoldableOp("-", Atoms.MINUS, 2, 6, DataTypes.Number, FoldableFlags.ORDER, FoldSeeds.ONE, "Subtract");
      AddFoldableOp("&", Atoms.SYMBOL, 2, 5, DataTypes.Text, FoldableFlags.ORDER, FoldSeeds.NUL, "Concat");

      AddOperator("=", Atoms.SYMBOL, 2, 4, DataTypes.Bool, "Eq");
      AddOperator("<>", Atoms.SYMBOL, 2, 4, DataTypes.Bool, "Ne");
      AddOperator(">", Atoms.SYMBOL, 2, 4, DataTypes.Bool, "Gt");
      AddOperator(">=", Atoms.SYMBOL, 2, 4, DataTypes.Bool, "Ge");
      AddOperator("<", Atoms.SYMBOL, 2, 4, DataTypes.Bool, "Lt");
      AddOperator("<=", Atoms.SYMBOL, 2, 4, DataTypes.Bool, "Le");
      AddOperator("=~", Atoms.SYMBOL, 2, 4, DataTypes.Bool, "Match");

      // These are overloaded by bit operations on integers
      AddFoldableOp("and", Atoms.IDENT, 2, 3, DataTypes.Unknown, FoldableFlags.ANY, FoldSeeds.FALSE, "And,BitAnd");
      AddFoldableOp("or", Atoms.IDENT, 2, 2, DataTypes.Unknown, FoldableFlags.ANY, FoldSeeds.FALSE, "Or,BitOr");
      AddFoldableOp("xor", Atoms.IDENT, 2, 2, DataTypes.Unknown, FoldableFlags.ANY, FoldSeeds.FALSE, "Xor,BitXor");

      AddOperator("sub", Atoms.IDENT, 2, 4, DataTypes.Bool, "Subset");
      AddOperator("sup", Atoms.IDENT, 2, 4, DataTypes.Bool, "Superset");
      AddOperator("sep", Atoms.IDENT, 2, 4, DataTypes.Bool, "Separate");

      AddFunction(Symbol.Assign, Atoms.NUL, 1, DataTypes.Void, CallKinds.FUNC, "Assign");
      AddFunction(Symbol.DoBlock, Atoms.NUL, 1, DataTypes.Any, CallKinds.FUNC, "DoBlock");
      AddFunction(Symbol.Invoke, Atoms.NUL, 1, DataTypes.Any, CallKinds.VFUNCT, "Invoke");
      AddFunction(Symbol.Lift, Atoms.NUL, 1, DataTypes.Void, CallKinds.FUNC, "Lift");
      AddFunction(Symbol.Project, Atoms.NUL, 2, DataTypes.Table, CallKinds.VFUNC, "Project");
      AddFunction(Symbol.Rename, Atoms.NUL, 2, DataTypes.Table, CallKinds.VFUNC, "Rename");
      AddFunction(Symbol.Row, Atoms.NUL, 2, DataTypes.Row, CallKinds.VFUNC, "Row");
      AddFunction(Symbol.Restrict, Atoms.NUL, 2, DataTypes.Table, CallKinds.VFUNC, "Restrict");
      AddFunction(Symbol.Transform, Atoms.NUL, 2, DataTypes.Table, CallKinds.VFUNC, "Transform");
      AddFunction(Symbol.TransAgg, Atoms.NUL, 2, DataTypes.Table, CallKinds.VFUNC, "TransAgg");
      AddFunction(Symbol.TransOrd, Atoms.NUL, 2, DataTypes.Table, CallKinds.VFUNC, "TransOrd");
      AddFunction(Symbol.Table, Atoms.NUL, 2, DataTypes.Table, CallKinds.VFUNC, "Table");
      AddFunction(Symbol.UpdateJoin, Atoms.NUL, 3, DataTypes.Bool, CallKinds.FUNC, "UpdateJoin");
      AddFunction(Symbol.UpdateTransform, Atoms.NUL, 3, DataTypes.Bool, CallKinds.VFUNC, "UpdateTrans");
      AddFunction(Symbol.UserSelector, Atoms.NUL, 2, DataTypes.User, CallKinds.VFUNCT, "UserSelector");
      //AddFunction("create", Atoms.IDENT, 2, DataTypes.Void, CallKinds.FUNC, "CreateTable", SymKinds.CREATE);
      //AddFunction("source", Atoms.IDENT, 2, DataTypes.Unknown, CallKinds.FUNC, "Source");

      AddFoldableFunction("max", Atoms.IDENT, 2, DataTypes.Ordered, CallKinds.FUNC, FoldableFlags.ANY, FoldSeeds.MIN, "Max");
      AddFoldableFunction("min", Atoms.IDENT, 2, DataTypes.Ordered, CallKinds.FUNC, FoldableFlags.ANY, FoldSeeds.MAX, "Min");
      AddFunction("fold", Atoms.IDENT, 0, DataTypes.Unknown, CallKinds.FUNC, "Fold", SymKinds.FOLD);
      AddFunction("cfold", Atoms.IDENT, 2, DataTypes.Unknown, CallKinds.FUNC, "CumFold", SymKinds.FOLD);
      AddFunction("if", Atoms.IDENT, 3, DataTypes.Unknown, CallKinds.FUNC, "If", SymKinds.IF);

      AddFunction("ord", Atoms.IDENT, 0, DataTypes.Number, CallKinds.LFUNC, "Ordinal");
      AddFunction("ordg", Atoms.IDENT, 0, DataTypes.Number, CallKinds.LFUNC, "OrdinalGroup");
      AddFunction("lead", Atoms.IDENT, 0, DataTypes.Unknown, CallKinds.LFUNC, "ValueLead", SymKinds.VALUE);
      AddFunction("lag", Atoms.IDENT, 0, DataTypes.Unknown, CallKinds.LFUNC, "ValueLag", SymKinds.VALUE);
      AddFunction("nth", Atoms.IDENT, 0, DataTypes.Unknown, CallKinds.LFUNC, "ValueNth", SymKinds.VALUE);
      AddFunction("rank", Atoms.IDENT, 0, DataTypes.Unknown, CallKinds.LFUNC, "Rank", SymKinds.RANK);

      AddDyadic("join", Atoms.IDENT, 2, 4, JoinOps.JOIN, "DyadicJoin");
      AddDyadic("compose", Atoms.IDENT, 2, 4, JoinOps.COMPOSE, "DyadicJoin");
      AddDyadic("divide", Atoms.IDENT, 2, 4, JoinOps.DIVIDE, "DyadicJoin");
      AddDyadic("rdivide", Atoms.IDENT, 2, 4, JoinOps.RDIVIDE, "DyadicJoin");
      AddDyadic("semijoin", Atoms.IDENT, 2, 4, JoinOps.SEMIJOIN, "DyadicJoin");
      AddDyadic("rsemijoin", Atoms.IDENT, 2, 4, JoinOps.RSEMIJOIN, "DyadicJoin");

      AddDyadic("ajoin", Atoms.IDENT, 2, 4, JoinOps.ANTIJOIN, "DyadicAntijoin");
      AddDyadic("rajoin", Atoms.IDENT, 2, 4, JoinOps.RANTIJOIN, "DyadicAntijoin");
      AddDyadic("ajoinl", Atoms.IDENT, 2, 4, JoinOps.ANTIJOINL, "DyadicAntijoin");
      AddDyadic("rajoinr", Atoms.IDENT, 2, 4, JoinOps.RANTIJOINR, "DyadicAntijoin");

      AddDyadic("union", Atoms.IDENT, 2, 4, JoinOps.UNION, "DyadicSet");
      AddDyadic("intersect", Atoms.IDENT, 2, 4, JoinOps.INTERSECT, "DyadicSet");
      AddDyadic("symdiff", Atoms.IDENT, 2, 4, JoinOps.SYMDIFF, "DyadicSet");
      AddDyadic("minus", Atoms.IDENT, 2, 4, JoinOps.MINUS, "DyadicSet");
      AddDyadic("rminus", Atoms.IDENT, 2, 4, JoinOps.RMINUS, "DyadicSet");

      AddAlias("matching", "semijoin");
      AddAlias("notmatching", "ajoin");
      AddAlias("joinlr", "compose");
      AddAlias("joinlc", "matching");
      AddAlias("joinl", "divide");
      AddAlias("joincr", "rsemijoin");
      AddAlias("joinr", "rdivide");

      //AddFunction("create", Atoms.IDENT, 2, DataTypes.Table, CallKinds.FUNC, "CreateSql");
      AddFunction("db", Atoms.IDENT, 0, DataTypes.Void, CallKinds.FUNC, "Connect", SymKinds.DB);
    }

    // Add a symbol to the current scope
    Symbol Add(string name, Symbol sym) {
      Scope.Current.Add(sym, name);
      return sym;
    }

    Symbol AddSym(string name, Atoms atom) {
      return Add(name, new Symbol { 
        Atom = atom,
      });
    }

    Symbol AddSym(string name, Atoms atom, SymKinds kind, DataType datatype) {
      return Add(name, new Symbol {
        Atom = atom,
        Kind = kind,
        DataType = datatype,
      });
    }

    Symbol AddLiteral(string name, TypedValue value, DataType type) {
      return Add(name, new Symbol {
        Atom = Atoms.LITERAL,
        Kind = SymKinds.LITERAL,
        DataType = type,
        Value = value
      });
    }

    Symbol AddIdent(string name, SymKinds kind, TypedValue value, DataType type) {
      return Add(name, new Symbol {
        Atom = Atoms.IDENT,
        Kind = kind,
        DataType = type,
        Value = value
      });
    }

    Symbol AddOperator(string name, Atoms atom, int numargs, int precedence, DataType type, string method) {
      return Add(name, new Symbol {
        Atom = atom,
        Kind = (numargs == 1) ? SymKinds.UNOP : (numargs == 2) ? SymKinds.BINOP : SymKinds.NUL,
        CallKind = CallKinds.FUNC,
        NumArgs = numargs,
        Precedence = precedence,
        DataType = type,
        CallInfo = CallInfo.Get(method),
      });
    }

    Symbol AddFoldableOp(string name, Atoms atom, int numargs, int precedence, DataType type, FoldableFlags foldable, FoldSeeds seed, string method) {
      return Add(name, new Symbol {
        Atom = atom,
        Kind = (numargs == 1) ? SymKinds.UNOP : (numargs == 2) ? SymKinds.BINOP : SymKinds.NUL,
        CallKind = CallKinds.FUNC,
        NumArgs = numargs,
        Precedence = precedence,
        DataType = type,
        Foldable = foldable,
        FoldSeed = seed,
        CallInfo = CallInfo.Get(method),
      });
    }

    Symbol AddDyadic(string name, Atoms atom, int numargs, int precedence, JoinOps joinop, string method) {
      return Add(name, new Symbol {
        Atom = atom,
        Kind = SymKinds.BINOP,
        CallKind = CallKinds.JFUNC,
        NumArgs = numargs,
        Precedence = precedence,
        JoinKind = joinop,
        MergeOp = (MergeOps)(joinop & JoinOps.MERGEOPS),
        DataType = DataTypes.Unknown,
        CallInfo = CallInfo.Get(method),
        Foldable = (joinop.HasFlag(JoinOps.LEFT) == joinop.HasFlag(JoinOps.RIGHT)) ? FoldableFlags.ANY : FoldableFlags.NUL,
      });
    }

    Symbol AddFunction(string name, Atoms atom, int numargs, DataType type, CallKinds callkind, string method, SymKinds kind = SymKinds.FUNC) {
      return Add(name, new Symbol {
        Atom = atom,
        Kind = kind,
        CallKind = callkind,
        NumArgs = numargs,
        DataType = type,
        CallInfo = CallInfo.Get(method),
      });
    }

    Symbol AddFoldableFunction(string name, Atoms atom, int numargs, DataType type, CallKinds callkind, FoldableFlags foldable, FoldSeeds seed, string method) {
      return Add(name, new Symbol {
        Atom = atom,
        Kind = SymKinds.FUNC,
        CallKind = callkind,
        NumArgs = numargs,
        DataType = type,
        Foldable = foldable,
        FoldSeed = seed,
        CallInfo = CallInfo.Get(method),
      });
    }

    Symbol AddAlias(string name, string other) {
      return Add(name, new Symbol {
        Atom = Atoms.ALIAS,
        Link = Find(other),
      });
    }

  }
}
