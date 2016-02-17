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

namespace Andl.Peg {
  /// <summary>
  /// Implements meaning for the symbol
  /// </summary>
  public enum SymKinds {
    NUL,
    ALIAS,      // alias
    CONST,      // value known at compile time
    FIELD,      // value during tuple iteration
    CATVAR,     // value as catalog variable
    PARAM,      // value from local declaration
    FUNC,       // callable function with args
    SELECTOR,   // for UDT
    COMPONENT,  // for UDT
  }

  /// <summary>
  /// Categories of special function
  /// </summary>
  public enum FuncKinds {
    NUL,
    DO,
    FOLD,
    IF,
    RANK,
    RESTRICT,
    SKIPTAKE,
    VALUE,
    WHILE,
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
    MERGEOPS = LEFT | COMMON | RIGHT,   // numeric match for MergeOps
    SETOPS = SETL | SETC | SETR,        // >> 3 is numeric match for MergeOps
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
    NUL, ZERO, ONE, MIN, MAX, FALSE, TRUE
  }

  // internal names for builtins
  public class SymNames {
    public const string Assign = ":assign";
    public const string Defer = ":defer";
    public const string DoBlock = ":doblock";
    public const string Fold = ":fold";
    public const string If = ":if";
    public const string Invoke = ":invoke";
    public const string Import = ":import";
    public const string Lift = ":lift";
    public const string Project = ":project";
    public const string Rename = ":rename";
    public const string RowE = ":rowe";
    public const string RowV = ":rowv";
    public const string RowC = ":rowc";
    public const string Table = ":table";
    public const string TableV = ":tablev";
    public const string TableC = ":tablec";
    public const string Transform = ":transform";
    public const string TransAgg = ":transagg";
    public const string TransOrd = ":transord";
    public const string TransTuple = ":transtup";
    public const string UpdateJoin = ":upjoin";
    public const string UpdateTransform = ":uptrans";
    public const string UnaryMinus = ":-";
    public const string UserSelector = ":userselector";
  }

  /// <summary>
  /// Implements a symbol table entry.
  /// </summary>
  public class Symbol {
    public SymKinds Kind { get; set; }
    public DataType DataType { get; set; }
    public DataHeading Heading { get {
      return DataType is DataTypeRelation ? (DataType as DataTypeRelation).Heading
        : DataType is DataTypeTuple ? (DataType as DataTypeRelation).Heading
        : null;
    } }
    public string Name { get; set; }
    public int Precedence { get; set; }
    public int NumArgs { get; set; }
    public TypedValue Value { get; set; }
    public CallInfo CallInfo { get; set; }
    public TypedValue Seed { get; set; }
    public FoldSeeds FoldSeed { get; set; }
    public JoinOps JoinOp { get; set; }
    public CallKinds CallKind { get; set; }
    public FuncKinds FuncKind { get; set; }
    public FoldableFlags Foldable { get; set; }
    public Symbol Link { get; set; }
    public int Level { get; set; }

    public override string ToString() {
      return String.Format("{0}:{1}:{2}:{3}", Name, Kind, CallKind, Level);
    }

    // series of tests used by parser
    public bool IsConst { get { return Kind == SymKinds.CONST; } }
    public bool IsCatVar { get { return Kind == SymKinds.CATVAR; } } // note: includes deferred
    public bool IsField { get { return Kind == SymKinds.FIELD; } }
    public bool IsParam { get { return Kind == SymKinds.PARAM; } }
    public bool IsComponent { get { return Kind == SymKinds.COMPONENT; } }
    public bool IsUserType { get { return Kind == SymKinds.SELECTOR; } }
    // Variable means a name bound to a value
    public bool IsVariable { get { return IsConst || IsCatVar || IsField || IsParam; } }

    public bool IsUserSel { get { return CallKind == CallKinds.SFUNC; } }
    public bool IsDefFunc { get { return CallKind == CallKinds.EFUNC; } }
    public bool IsOrdFunc { get { return FuncKind == FuncKinds.VALUE || FuncKind == FuncKinds.RANK; } }
    public bool IsDo{ get { return FuncKind == FuncKinds.DO; } }
    public bool IsFold { get { return FuncKind == FuncKinds.FOLD; } }
    public bool IsIf { get { return FuncKind == FuncKinds.IF; } }
    public bool IsSkipTake { get { return FuncKind == FuncKinds.SKIPTAKE; } }
    public bool IsRestrict { get { return FuncKind == FuncKinds.RESTRICT; } }
    public bool IsWhile { get { return FuncKind == FuncKinds.WHILE; } }

    public bool IsCallable { get { return CallKind != CallKinds.NUL; } }
    public bool IsOperator { get { return IsCallable && Precedence != 0; } }
    public bool IsFoldable { get { return IsCallable && Foldable != FoldableFlags.NUL; } }
    public bool IsDyadic { get { return IsCallable && JoinOp != JoinOps.NUL; } }
    public bool IsUnary { get { return IsOperator && NumArgs == 1; } }
    public bool IsBinary { get { return IsOperator && NumArgs == 2; } }
    public bool IsCompareOp { get { return IsBinary && DataType == DataTypes.Bool && !IsFoldable; } }
    public bool IsPredefined { get { return Level == 0; } }
    public bool IsGlobal { get { return Level == 1; } }

    public DataType ReturnType { get { return CallInfo.ReturnType; } }
    public DataColumn AsColumn() { return DataColumn.Create(Name, DataType); }

    public static MergeOps ToMergeOp(JoinOps joinop) {
      return (MergeOps)(joinop & JoinOps.MERGEOPS);
    }
    public static MergeOps ToTupleOp(JoinOps joinop) {
      return (MergeOps)((int)(joinop & JoinOps.SETOPS) >> 3);
    }

    public TypedValue GetSeed(DataType datatype) {
      if (datatype is DataTypeRelation)
        return RelationValue.Create(DataTable.Create(datatype.Heading));

      switch (FoldSeed) {
      case FoldSeeds.NUL:
        if (datatype == DataTypes.Bool) return BoolValue.False;
        if (datatype == DataTypes.Text) return TextValue.Default;
        if (datatype == DataTypes.Number) return NumberValue.Zero;
        if (datatype == DataTypes.Time) return TimeValue.Minimum;
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
      case FoldSeeds.TRUE:
        if (datatype == DataTypes.Bool) return BoolValue.True;
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
  /// 
  /// Predefined scope contains symbols loaded here.
  /// Global scope contains symbols imported and later defined outside any block.
  /// Local scopes may be nested, and will be created dynamically at runtime.
  /// A predefined name can be redefined, but others cannot.
  /// TODO: Namespaces.
  /// </summary>
  public class SymbolTable {
    // current scope
    public Scope CurrentScope { get; set; }

    Scope _predefscope; // level = 0
    Scope _globalscope; // level = 1
    Catalog _catalog;
    HashSet<string> _sources = new HashSet<string>();

    public override string ToString() {
      return String.Format("SymTab {0}", CurrentScope.AllToString());
    }

    //public static SymbolTable Create() {
    public static SymbolTable Create(Catalog catalog) {
      var st = new SymbolTable { _catalog = catalog };
      st.Init();
      Logger.WriteLine(3, "[SymbolTable Create: {0}]", st);
      return st;
    }

    //--- publics

    // Add a symbol to the catalog, but only if it is global
    public void AddCatalog(Symbol symbol) {
      if (CurrentScope.IsGlobal) {
        var kind = symbol.IsUserType ? EntryKinds.Type
          : symbol.IsDefFunc ? EntryKinds.Code
          : EntryKinds.Value;
        var flags = EntryFlags.Public;  // FIX: when visibility control implemented
        _catalog.GlobalVars.AddNew(symbol.Name, symbol.DataType, kind, flags);
      }
    }

    // Find existing symbol by name
    public Symbol FindIdent(string name) {
      var sym = CurrentScope.FindAny(name);
      return (sym != null && sym.Kind == SymKinds.ALIAS) ? sym.Link : sym;
    }

    // Check if symbol can be defined globally without conflict
    public bool CanDefGlobal(string name) {
      var sym = CurrentScope.FindAny(name);
      return sym == null || sym.IsPredefined;
    }

    // Check if symbol can be defined in this scope without conflict
    public bool CanDefLocal(string name) {
      var sym = CurrentScope.Find(name);
      return sym == null;
    }

    // Find existing source by name
    public bool IsSource(string name) {
      return _sources.Contains(name);
    }

    public void AddUserType(string name, DataTypeUser datatype) {
      CurrentScope.Add(MakeUserType(name, datatype));
    }

    public void AddVariable(string name, DataType datatype, SymKinds kind) {
      CurrentScope.Add(MakeVariable(name, datatype, kind));
    }

    public void AddDeferred(string name, DataType rettype, DataColumn[] args) {
      CurrentScope.Add(MakeDeferred(name, rettype, args, 0));
    }

    ///-------------------------------------------------------------------

    // Make symbols that can be user-defined
    internal static Symbol MakeUserType(string name, DataTypeUser datatype) {
      var callinfo = CallInfo.Create(name, datatype, datatype.Heading.Columns.ToArray());
      return new Symbol {
        Name = name,
        Kind = SymKinds.SELECTOR,
        CallKind = CallKinds.SFUNC,
        NumArgs = callinfo.NumArgs,
        DataType = datatype,
        CallInfo = callinfo,
      };
    }

    // Create a symbol for a variable
    public static Symbol MakeVariable(string name, DataType datatype, SymKinds kind) {
      Symbol sym = new Symbol {
        Name = name,
        Kind = kind,
        DataType = datatype,
      };
      return sym;
    }

    // Create a sumbol for a deferred function
    public static Symbol MakeDeferred(string name, DataType datatype, DataColumn[] args, int accums) {
      Symbol sym = new Symbol {
        Name = name,
        Kind = SymKinds.CATVAR,
        DataType = datatype,
        CallKind = CallKinds.EFUNC,
        CallInfo = CallInfo.Create(name, datatype, args, accums),
        NumArgs = args.Length,
      };
      return sym;
    }

    //--- setup

    void Init() {
      _predefscope = Scope.Create(this);
      AddPredefinedSymbols();
      foreach (var info in AddinInfo.GetAddinInfo())
        AddBuiltinFunction(info.Name, info.NumArgs, info.DataType, info.Method);
      _globalscope = _predefscope.Push();  // reserve a level for imported symbols
      _globalscope.IsGlobal = true;
      CurrentScope = _globalscope;
    }

    public void ResetScope() {
      CurrentScope = _predefscope;
      _globalscope = CurrentScope.Push();  // reserve a level for imported symbols
      CurrentScope.IsGlobal = true;
      Logger.WriteLine(3, "[Reset scope: {0}]", this);
    }

    // Process catalog to add all entries from persistent level
    // Called functions should discard duplicates, or flag errors???
    public void Import(CatalogScope catalogscope) {
      Logger.WriteLine(3, "SymbolTable Import: {0}", catalogscope);
      foreach (var entry in catalogscope.GetEntries()) {
        var value = entry.Value;
        if (_globalscope.Find(entry.Name) == null)
          Logger.WriteLine(4, "From catalog add {0}:{1}", entry.Name, entry.DataType.BaseType.Name);

        if (entry.Kind == EntryKinds.Type)
          _globalscope.Add(MakeUserType(entry.Name, entry.DataType as DataTypeUser));
        else if (entry.Kind == EntryKinds.Value)
          _globalscope.Add(MakeVariable(entry.Name, entry.DataType, SymKinds.CATVAR));
        else if (entry.Kind == EntryKinds.Code)
          _globalscope.Add(MakeDeferred(entry.Name, entry.DataType, 
            entry.CodeValue.Value.Lookup.Columns, entry.CodeValue.Value.AccumCount));
      }
      Logger.WriteLine(3, "[SSTI {0}]", this);
    }

    // Add a built in function (from a library)
    Symbol AddBuiltinFunction(string name, int numargs, DataType type, string method) {
      return AddFunction(name, numargs, type, CallKinds.FUNC, method);
    }

    //------------------------------------------------------------------
    //-- ops

    // Load and initialise the symbol table
    void AddPredefinedSymbols() {
      AddIdent("true", SymKinds.CONST, BoolValue.True, DataTypes.Bool);
      AddIdent("false", SymKinds.CONST, BoolValue.False, DataTypes.Bool);
      AddIdent("$lineno$", SymKinds.CONST, NumberValue.Zero, DataTypes.Number);
      AddIdent("$filename$", SymKinds.CONST, TextValue.Empty, DataTypes.Text);

      AddOperator("not", 1, 9, DataTypes.Bool, "Not");
      AddOperator("**", 2, 9, DataTypes.Number, "Pow");
      AddOperator(SymNames.UnaryMinus, 1, 8, DataTypes.Number, "Neg");
      AddOperator("*", 2, 7, DataTypes.Number, "Multiply", FoldableFlags.ANY, FoldSeeds.ONE);
      AddOperator("/", 2, 7, DataTypes.Number, "Divide", FoldableFlags.ORDER, FoldSeeds.ONE);
      AddOperator("div", 2, 7, DataTypes.Number, "Div");
      AddOperator("mod", 2, 7, DataTypes.Number, "Mod");
      AddOperator("+", 2, 6, DataTypes.Number, "Add", FoldableFlags.ANY, FoldSeeds.ZERO);
      AddOperator("-", 2, 6, DataTypes.Number, "Subtract", FoldableFlags.ORDER, FoldSeeds.ZERO);
      AddOperator("&", 2, 5, DataTypes.Text, "Concat", FoldableFlags.ORDER, FoldSeeds.NUL);

      AddOperator("=", 2, 4, DataTypes.Bool, "Eq");
      AddOperator("<>", 2, 4, DataTypes.Bool, "Ne");
      AddOperator(">", 2, 4, DataTypes.Bool, "Gt");
      AddOperator(">=", 2, 4, DataTypes.Bool, "Ge");
      AddOperator("<", 2, 4, DataTypes.Bool, "Lt");
      AddOperator("<=", 2, 4, DataTypes.Bool, "Le");
      AddOperator("=~", 2, 4, DataTypes.Bool, "Match");

      // These are overloaded by bit operations on integers
      AddOperator("and", 2, 3, DataTypes.Unknown, "And,BitAnd", FoldableFlags.ANY, FoldSeeds.TRUE);
      AddOperator("or", 2, 2, DataTypes.Unknown, "Or,BitOr", FoldableFlags.ANY, FoldSeeds.FALSE);
      AddOperator("xor", 2, 2, DataTypes.Unknown, "Xor,BitXor", FoldableFlags.ANY, FoldSeeds.FALSE);

      AddOperator("sub", 2, 4, DataTypes.Bool, "Subset");
      AddOperator("sup", 2, 4, DataTypes.Bool, "Superset");
      AddOperator("sep", 2, 4, DataTypes.Bool, "Separate");

      AddFunction(SymNames.Assign, 2, DataTypes.Void, CallKinds.FUNC, "Assign2");
      AddFunction(SymNames.Defer, 1, DataTypes.Void, CallKinds.FUNC, "Defer");
      AddFunction("do", 1, DataTypes.Any, CallKinds.FUNC, "DoBlock", FuncKinds.DO);
      //AddFunction(SymNames.DoBlock, 1, DataTypes.Any, CallKinds.FUNC, "DoBlock", FuncKinds.DO);
      AddFunction(SymNames.Import, 3, DataTypes.Void, CallKinds.FUNC, "Import");
      AddFunction(SymNames.Invoke, 2, DataTypes.Any, CallKinds.VFUNCT, "Invoke");
      AddFunction(SymNames.Lift, 1, DataTypes.Void, CallKinds.FUNC, "Lift");
      AddFunction(SymNames.Project, 2, DataTypes.Table, CallKinds.VFUNC, "Project");
      AddFunction(SymNames.Rename, 2, DataTypes.Table, CallKinds.VFUNC, "Rename");
      AddFunction(SymNames.RowE, 2, DataTypes.Row, CallKinds.VFUNC, "Row");
      AddFunction(SymNames.RowV, 2, DataTypes.Row, CallKinds.VFUNCT, "RowV");
      AddFunction(SymNames.RowC, 2, DataTypes.Row, CallKinds.VFUNCT, "RowC");
      AddFunction("while", 3, DataTypes.Unknown, CallKinds.FUNC, "Recurse", FuncKinds.WHILE);
      AddFunction("where", 2, DataTypes.Table, CallKinds.VFUNC, "Restrict", FuncKinds.RESTRICT);
      AddFunction(SymNames.Transform, 2, DataTypes.Table, CallKinds.VFUNC, "Transform");
      AddFunction(SymNames.TransAgg, 2, DataTypes.Table, CallKinds.VFUNC, "TransAgg");
      AddFunction(SymNames.TransOrd, 2, DataTypes.Table, CallKinds.VFUNC, "TransOrd");
      AddFunction(SymNames.TransTuple, 2, DataTypes.Table, CallKinds.VFUNC, "TransTuple");
      AddFunction(SymNames.Table, 2, DataTypes.Table, CallKinds.VFUNC, "Table");
      AddFunction(SymNames.TableV, 2, DataTypes.Table, CallKinds.VFUNCT, "TableV");
      AddFunction(SymNames.TableC, 2, DataTypes.Table, CallKinds.VFUNCT, "TableC");
      AddFunction(SymNames.UpdateJoin, 3, DataTypes.Bool, CallKinds.FUNC, "UpdateJoin");
      AddFunction(SymNames.UpdateTransform, 3, DataTypes.Bool, CallKinds.VFUNC, "UpdateTrans");
      AddFunction(SymNames.UserSelector, 2, DataTypes.User, CallKinds.VFUNCT, "UserSelector");

      AddFunction("take", 2, DataTypes.Table, CallKinds.FUNC, "Take", FuncKinds.SKIPTAKE);
      AddFunction("skip", 2, DataTypes.Table, CallKinds.FUNC, "Skip", FuncKinds.SKIPTAKE);
      AddFunction("max", 2, DataTypes.Ordered, CallKinds.FUNC, "Max", FoldableFlags.ANY, FoldSeeds.MIN);
      AddFunction("min", 2, DataTypes.Ordered, CallKinds.FUNC, "Min", FoldableFlags.ANY, FoldSeeds.MAX);
      AddFunction("fold", 2, DataTypes.Unknown, CallKinds.FUNC, "Fold", FuncKinds.FOLD);
      //AddFunction("cfold", 2, DataTypes.Unknown, CallKinds.FUNC, "CumFold", FuncKinds.FOLD);
      AddFunction("if", 3, DataTypes.Unknown, CallKinds.FUNC, "If", FuncKinds.IF);

      AddFunction("ord", 0, DataTypes.Number, CallKinds.LFUNC, "Ordinal");
      AddFunction("ordg", 0, DataTypes.Number, CallKinds.LFUNC, "OrdinalGroup");
      AddFunction("lead", 2, DataTypes.Unknown, CallKinds.LFUNC, "ValueLead", FuncKinds.VALUE);
      AddFunction("lag", 2, DataTypes.Unknown, CallKinds.LFUNC, "ValueLag", FuncKinds.VALUE);
      AddFunction("nth", 2, DataTypes.Unknown, CallKinds.LFUNC, "ValueNth", FuncKinds.VALUE);
      AddFunction("rank", 2, DataTypes.Number, CallKinds.LFUNC, "Rank", FuncKinds.RANK);

      AddDyadic("join", 2, 5, JoinOps.JOIN, "DyadicJoin");
      AddDyadic("compose", 2, 5, JoinOps.COMPOSE, "DyadicJoin");
      AddDyadic("divide", 2, 5, JoinOps.DIVIDE, "DyadicJoin");
      AddDyadic("rdivide", 2, 5, JoinOps.RDIVIDE, "DyadicJoin");
      AddDyadic("semijoin", 2, 5, JoinOps.SEMIJOIN, "DyadicJoin");
      AddDyadic("rsemijoin", 2, 5, JoinOps.RSEMIJOIN, "DyadicJoin");

      AddDyadic("ajoin", 2, 5, JoinOps.ANTIJOIN, "DyadicAntijoin");
      AddDyadic("rajoin", 2, 5, JoinOps.RANTIJOIN, "DyadicAntijoin");
      AddDyadic("ajoinl", 2, 5, JoinOps.ANTIJOINL, "DyadicAntijoin");
      AddDyadic("rajoinr", 2, 5, JoinOps.RANTIJOINR, "DyadicAntijoin");

      AddDyadic("union", 2, 5, JoinOps.UNION, "DyadicSet,DyadicTuple");
      AddDyadic("intersect", 2, 5, JoinOps.INTERSECT, "DyadicSet,DyadicTuple");
      AddDyadic("symdiff", 2, 5, JoinOps.SYMDIFF, "DyadicSet,DyadicTuple");
      AddDyadic("minus", 2, 5, JoinOps.MINUS, "DyadicSet,DyadicTuple");
      AddDyadic("rminus", 2, 5, JoinOps.RMINUS, "DyadicSet,DyadicTuple");

      AddAlias("matching", "semijoin");
      AddAlias("notmatching", "ajoin");
      AddAlias("joinlr", "compose");
      AddAlias("joinlc", "matching");
      AddAlias("joinl", "divide");
      AddAlias("joincr", "rsemijoin");
      AddAlias("joinr", "rdivide");

      AddSource("csv");
      AddSource("txt");
      AddSource("sql");
      AddSource("con");
      AddSource("file");
      AddSource("oledb");
      AddSource("odbc");
    }

    // Add a symbol to the current scope
    Symbol Add(string name, Symbol sym) {
      CurrentScope.Add(sym, name);
      return sym;
    }

    Symbol AddIdent(string name, SymKinds kind, TypedValue value, DataType type) {
      return Add(name, new Symbol {
        Kind = kind,
        DataType = type,
        Value = value
      });
    }

    Symbol AddOperator(string name, int numargs, int precedence, DataType type, string method, FoldableFlags foldable = FoldableFlags.NUL, FoldSeeds seed = FoldSeeds.NUL) {
      return Add(name, new Symbol {
        Kind = SymKinds.FUNC,
        CallKind = CallKinds.FUNC,
        NumArgs = numargs,
        Precedence = precedence,
        DataType = type,
        Foldable = foldable,
        FoldSeed = seed,
        CallInfo = CallInfo.Get(method),
      });
    }

    Symbol AddDyadic(string name, int numargs, int precedence, JoinOps joinop, string method) {
      return Add(name, new Symbol {
        Kind = SymKinds.FUNC,
        CallKind = CallKinds.JFUNC,
        NumArgs = numargs,
        Precedence = precedence,
        JoinOp = joinop,
        DataType = DataTypes.Unknown,
        CallInfo = CallInfo.Get(method),
        Foldable = (joinop.HasFlag(JoinOps.LEFT) == joinop.HasFlag(JoinOps.RIGHT)) ? FoldableFlags.ANY : FoldableFlags.NUL,
      });
    }

    Symbol AddFunction(string name, int numargs, DataType type, CallKinds callkind, string method, FoldableFlags foldable = FoldableFlags.NUL, FoldSeeds seed = FoldSeeds.NUL) {
      return Add(name, new Symbol {
        Kind = SymKinds.FUNC,
        CallKind = callkind,
        NumArgs = numargs,
        DataType = type,
        Foldable = foldable,
        FoldSeed = seed,
        CallInfo = CallInfo.Get(method),
      });
    }

    Symbol AddFunction(string name, int numargs, DataType type, CallKinds callkind, string method, FuncKinds funckind) {
      return Add(name, new Symbol {
        Kind = SymKinds.FUNC,
        CallKind = callkind,
        NumArgs = numargs,
        DataType = type,
        FuncKind = funckind,
        CallInfo = CallInfo.Get(method),
      });
    }

    Symbol AddAlias(string name, string other) {
      return Add(name, new Symbol {
        Kind = SymKinds.ALIAS,
        Link = FindIdent(other),
      });
    }

    void AddSource(string name) {
      _sources.Add(name);
    }

  }
}
