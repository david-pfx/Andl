using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using Andl.Runtime;

namespace Andl.Peg {
  ///==============================================================================================
  /// <summary>
  /// Implement factory for AST nodes
  /// </summary>
  public class AstFactory {
    class AccumCounter {
      public bool Enabled { get; set; }
      public int Total { get; set; }
      public int Count { get; set; }
      AccumCounter _parent = null;

      public AccumCounter Push() {
        var ret = new AccumCounter { _parent = this, Enabled = true };
        Logger.WriteLine(4, "Push {0} => {1}", this, ret);
        return ret;
      }
      public AccumCounter Pop() {
        Logger.WriteLine(4, "Pop {0} => {1}", this, _parent);
        return _parent;
      }
      public void Add(int num) {
        Count += num;
        Total += num;
        Logger.WriteLine(4, "Add {0} {1}", num, this);
      }
      public void Reset(bool all) {
        Count = 0;
        if (all) Total = 0;
        Logger.WriteLine(4, "Reset {0} {1}", all, this);
      }
      public override string ToString() {
        return string.Format("Accum:{0} c={1} t={2}", Enabled, Count, Total);
      }
    }

    public TypeSystem Types { get { return Parser.Types; } }
    public SymbolTable Symbols { get { return Parser.Symbols; } }
    public Catalog Cat { get { return Parser.Cat; } }
    public PegParser Parser { get; set; }

    AccumCounter _accum = new AccumCounter();

    // A set of statements inside a do scope
    public AstValue DoBlock(IList<AstStatement> statements) {
      var datatype = (statements == null || statements.Count == 0) ? DataTypes.Void : statements.Last().DataType;
      var block = new AstBlock { Statements = statements.ToArray(), DataType = datatype };
      return new AstDoBlock {
        Func = FindFunc(SymNames.DoBlock), DataType = datatype, Value = block,
      };
    }

    // just a set of statements (may be empty but not null)
    public AstBlock Block(IList<AstStatement> statements) {
      return new AstBlock {
        Statements = statements.ToArray(),
        DataType = (statements.Count == 0) ? DataTypes.Void : statements.Last().DataType
      };
    }

    public AstDefine DefBlock(IList<AstDefine> defines) {
      return new AstDefine { DataType = DataTypes.Void };
    }

    public AstDefine UserType(string ident, AstField[] fields) {
      var ff = fields.Select(a => DataColumn.Create(a.Name, a.DataType)).ToArray();
      var ut = DataTypeUser.Get(ident, ff);
      Symbols.AddUserType(ident, ut);
      Symbols.AddCatalog(Symbols.FindIdent(ident));
      return new AstUserType { DataType = DataTypes.Void };
    }

    public AstDefine SubType(string ident, AstType super) {
      var cols = new DataColumn[] { DataColumn.Create("super", super.DataType) };
      var ut = DataTypeUser.Get(ident, cols);
      Symbols.AddUserType(ident, ut);
      return new AstSubType { DataType = DataTypes.Void };
    }

    public AstStatement Source(string ident, AstLiteral value) {
      var datatype = Cat.GetRelvarType(ident, value.ToString());  //FIX: better to be literal to here
      if (datatype == null) Parser.ParseError("cannot find '{0}' as '{1}'", ident, value);
      Symbols.AddVariable(ident, datatype, SymKinds.CATVAR);
      Symbols.AddCatalog(Symbols.FindIdent(ident));
      return FunCall(FindFunc(SymNames.Import), Args(value, Text(ident), Text(Parser.Cat.SourcePath)));
    }

    public AstStatement Deferred(string ident, AstType rettype, IList<AstField> arguments, AstBodyStatement body) {
      var op = FindDefFunc(ident);
      if (op.DataType == DataTypes.Unknown) op.DataType = body.DataType;
      op.CallInfo.AccumCount = body.AccumCount;
      if (op.NumArgs == 2 && arguments[0].DataType == op.DataType && arguments[1].DataType == op.DataType)
        op.Foldable = FoldableFlags.ANY;
      Symbols.AddCatalog(op);
      Types.CheckTypeMatch(body.DataType, op.DataType);
      return FunCall(FindFunc(SymNames.Defer), Args(Code(body.Statement as AstValue, Headingof(arguments), ident, body.AccumCount, true)));
    }

    public AstStatement Assignment(string ident, AstValue value) {
      Symbols.AddVariable(ident, value.DataType, SymKinds.CATVAR);
      Symbols.AddCatalog(Symbols.FindIdent(ident));
      return FunCall(FindFunc(SymNames.Assign), Args(Text(ident), value));
    }

    public AstStatement UpdateJoin(string ident, string joinop, AstValue expr) {
      var joinsym = FindFunc(joinop);
      var joinnum = Number((int)joinsym.JoinOp);
      return FunCall(FindFunc(SymNames.UpdateJoin), DataTypes.Void, Args(Variable(ident), expr, joinnum));
    }

    public AstStatement UpdateTransform(string ident, AstTranCall arg) {
      var tran = arg.Transformer ?? Transformer(false, null);
      return FunCall(FindFunc(SymNames.UpdateTransform), DataTypes.Void, Args(Variable(ident), Args(arg.Where, tran.Elements)),
        tran.Elements.Length);
    }

    // body statement in deferred might need an accumulator
    public AstBodyStatement BodyStatement(AstStatement value) {
      return new AstBodyStatement {
        DataType = value.DataType, Statement = value, AccumCount = _accum.Total
        //DataType = value.DataType, Statement = value, AccumCount = _accum.Count
      };
    }

    // Wrap transform tail for later handling
    // update scope for type inference, and to lose any defined symbols
    public AstTranCall TransformTail(AstValue where, AstOrderer order, AstTransformer trans) {
      Reenter(trans == null ? null : trans.DataType);
      return new AstTranCall {
        DataType = DataTypeRelation.Get(CurrentHeading()),
        Where = where, Orderer = order, Transformer = trans,
      };
    }

    // handle a Where rule simply as code (may have fold)
    public AstValue Where(AstValue value) {
      var lookup = DataHeading.Create(Scope.Current.LookupItems.Items);
      var accums = _accum.Total;
      _accum = _accum.Push();
      return Code(value, lookup, "?", accums);
    }

    // create ordering node
    public AstOrderer Orderer(IList<AstOrder> argslist) {
      var args = argslist.ToArray();
      return new AstOrderer { Elements = args, DataType = Typeof(args) };
    }

    // Take a list of transforms, check allbut & lift, return transformer and type for later
    public AstTransformer Transformer(bool allbut, IList<AstField> argslist = null) {
      var args = (argslist == null) ? new AstField[0] : argslist.ToArray();
      var lift = args.Any(a => a is AstLift);
      if (lift) {
        if (allbut || argslist.Count != 1) Parser.ParseError("invalid lift");
        return new AstTransformer { Elements = args, Lift = true, DataType = argslist[0].DataType };
      }
      if (allbut) args = Allbut(CurrentHeading(), args);
      return new AstTransformer { Elements = args, DataType = Typeof(args) };
    }

    // handle generic transform rule and create specific node with all required info
    // each element tracks lookup items and folds
    public AstField Transfield(string name, string rename = null, AstValue value = null) {
      var lookup = DataHeading.Create(Scope.Current.LookupItems.Items);
      var accums = _accum.Count;
      _accum.Reset(false);
      if (name == null) return new AstLift {
        Name = "^", Value = value, DataType = value.DataType, Lookup = lookup, Accums = accums,
      };
      if (rename != null) return new AstRename {
        Name = name, OldName = rename, DataType = FindField(rename).DataType
      };
      if (value != null) return new AstExtend {
        Name = name, DataType = value.DataType, Value = value, Lookup = lookup, Accums = accums,
      };
      return new AstProject {
        Name = name, DataType = FindField(name).DataType
      };
    }

    // handle an Order rule
    public AstOrder Order(string name, bool desc, bool group) {
      return new AstOrder { Name = name, DataType = FindField(name).DataType, Descending = desc, Grouped = group };
    }

    public AstValue Row(AstTransformer transformer) {
      return new AstTabCall {
        Func = FindFunc(SymNames.RowE), DataType = Types.Tupof(transformer.DataType), Arguments = transformer.Elements,
      };
    }
    public AstValue RowValues(IList<AstValue> values) {
      return new AstTabCall {
        Func = FindFunc(SymNames.RowV), DataType = DataTypes.Unknown, Arguments = values.ToArray(),
      };
    }

    public AstValue Table(AstValue heading, IList<AstValue> rows) {
      var rowtype = (heading != null) ? Types.Tupof(heading.DataType)
        : rows.Count > 0 ? rows[0].DataType
        : DataTypeTuple.Empty;
      foreach (var r in rows) {
        if (heading != null) r.DataType = rowtype;
        if (r.DataType != rowtype) Parser.ParseError("row type mismatch");
      }
      return new AstTabCall {
        Func = FindFunc(SymNames.Table), DataType = Types.Relof(rowtype), Arguments = rows.ToArray()
      };
    }

    public AstValue Heading(IList<AstField> fields) {
      var type = (fields == null) ? DataTypeRelation.Empty : Typeof(fields);
      return new AstValue { DataType = type };
    }

    public AstValue Table() {
      var row = Row();
      return new AstTabCall {
        Func = FindFunc(SymNames.Table), DataType = Types.Relof(row.DataType), Arguments = Args(row),
      };
    }

    public AstValue Row() {
      var trans = Transformer(true, null);
      return new AstTabCall {
        Func = FindFunc(SymNames.RowE), DataType = Types.Tupof(trans.DataType), Arguments = trans.Elements,
      };
    }

    ///--------------------------------------------------------------------------------------------
    /// Headings etc
    /// 

    public AstField FieldTerm(string ident, AstType type) {
      return new AstField() {
        Name = ident,
        DataType = (type == null) ? Types.Find("text") : type.DataType
      };
    }

    ///--------------------------------------------------------------------------------------------
    /// Calls with arguments
    /// 
    public AstValue Binop(AstValue value, IList<AstOpCall> ops) {
      var ret = Binop(value, ops.ToArray());
      if (Logger.Level >= 4) Logger.WriteLine("Binop {0}", ret);
      return ret;
    }

    // construct a FunCall from the first OpCall, then invoke tail on result
    public AstValue PostFix(AstValue value, IList<AstCall> ops) {
      if (ops.Count == 0) return value;
      var newvalue = (ops[0] is AstTranCall)
        ? PostFix(value, ops[0] as AstTranCall)
        : PostFix(value, ops[0] as AstOpCall);
      return PostFix(newvalue, ops.Skip(1).ToList()); // tail
    }

    // Dot that is a function call
    public AstOpCall DotFunc(string name) {
      var op = FindFunc(name);
      return new AstOpCall() { Func = op, DataType = op.DataType, Arguments = Args() };
    }

    public AstOpCall DotComponent(string name) {
      var op = FindComponent(name);
      return new AstOpCall() { Func = op, DataType = op.DataType, Arguments = Args() };
    }

    public AstOpCall DotField(string name) {
      var op = FindField(name);
      return new AstOpCall() { Func = op, DataType = op.DataType, Arguments = Args() };
    }

    public AstFunCall If(AstValue condition, AstValue iftrue, AstValue iffalse) {
      Types.CheckTypeMatch(iftrue.DataType, iffalse.DataType);
      return FunCall(FindFunc(SymNames.If), iftrue.DataType, Args(condition, Code(iftrue), Code(iffalse)));
    }

    public AstFoldCall Fold(string oper, AstValue expression) {
      var op = FindFunc(oper);
      if (!op.IsFoldable) Parser.ParseError("not foldable");
      DataType datatype;
      CallInfo callinfo;
      Types.CheckTypeError(op, out datatype, out callinfo, expression.DataType, expression.DataType); // as if there were two
      var accum = _accum.Total;
      _accum.Add(1);
      return new AstFoldCall {
        Func = FindFunc(SymNames.Fold), FoldedOp = op, DataType = datatype,
        AccumIndex = accum, FoldedExpr = expression, CallInfo = callinfo,
        InvokeOp = (op.IsDefFunc) ? Symbols.FindIdent(SymNames.Invoke) : null,
      };
    }

    public AstFunCall Function(string name, params AstValue[] args) {
      var op = FindFunc(name);
      return FunCall(op, args);
    }

    public AstOpCall OpCall(string name, params AstValue[] args) {
      var op = FindFunc(name);
      var type = op.DataType;
      return new AstOpCall() { Func = op, Arguments = args, DataType = type };
    }

    ///--------------------------------------------------------------------------------------------
    ///--- internal functions
    ///
    AstValue Code(AstValue value, DataHeading lookup = null, string name = "?", int accums = -1, bool defer = false) {
      return new AstCode {
        Name = name, Value = value, DataType = DataTypes.Code, Accums = accums, Lookup = lookup, Defer = defer,
      };
    }

    AstValue Binop(AstValue value, AstOpCall[] ops) {
      Logger.WriteLine(4, "expr {0} op={1}", value, String.Join(",", ops.Select(o => o.ToString())));
      if (ops.Length == 0) return value;
      var op = ops[0];
      if (ops.Length == 1) return FunCall(op.Func, Args(value, op.Arguments));
      var opnext = ops[1];
      var optail = ops.Skip(1);
      //Func<AstCall, AstCall, bool> op high = (op1, op2) => !op1.Func.IsOperator || op1.Func.Precedence >= op2.Func.Precedence;
      if (!op.Func.IsOperator || op.Func.Precedence >= opnext.Func.Precedence)
        return Binop(FunCall(op.Func, Args(value, op.Arguments)), optail.ToArray());
      // The hard case -- rewrite the tree
      // extract higher precedence ops and do those first; then lower
      var hiprec = optail.TakeWhile(o => !o.Func.IsOperator || o.Func.Precedence > op.Func.Precedence);
      var hivalue = Binop(op.Arguments[0] as AstValue, hiprec.ToArray());
      var loprec = optail.SkipWhile(o => !o.Func.IsOperator || o.Func.Precedence > op.Func.Precedence);
      return Binop(FunCall(op.Func, Args(value, hivalue)), loprec.ToArray());
    }

    // translate set of transform calls into a function call (or two)
    AstValue PostFix(AstValue value, AstTranCall tran) {
      var newvalue = value;
      if (tran.Where != null)
        newvalue = FunCall(FindFunc(SymNames.Restrict), value.DataType, Args(value, tran.Where), 1);
      var args = new List<AstNode> { newvalue };
      if (tran.Orderer != null) args.AddRange(tran.Orderer.Elements);
      if (tran.Transformer != null) args.AddRange(tran.Transformer.Elements);
      else if (tran.Orderer != null) args.AddRange(Allbut(value.DataType.Heading, new AstField[0]));
      if (args.Count > 1) {
        var opname = (tran.Orderer != null) ? SymNames.TransOrd
          : tran.Transformer.Elements.Any(e => e is AstExtend && (e as AstExtend).Accums > 0) ? SymNames.TransAgg
          : tran.Transformer.Elements.Any(e => e is AstExtend) ? SymNames.Transform
          : tran.Transformer.Elements.All(e => e is AstRename) && tran.Transformer.Elements.Length == value.DataType.Heading.Degree ? SymNames.Rename
          : SymNames.Project;
        newvalue = FunCall(FindFunc(opname), tran.DataType, args.ToArray(), args.Count - 1);
        if (tran.Transformer != null && tran.Transformer.Lift)
          newvalue = FunCall(FindFunc(SymNames.Lift), tran.Transformer.DataType, Args(newvalue));
      }
      return newvalue;
    }

    AstValue PostFix(AstValue value, AstOpCall op) {
      return FunCall(op.Func, new AstValue[] { value }.Concat(op.Arguments).ToArray()); // cons
    }

    // implement allbut: remove projects, add renames, extends and other columns as projects
    AstField[] Allbut(DataHeading heading, AstField[] fields) {
      // deletions first -- remove all columns matching a project
      var newcols = heading.Columns.Where(c => !fields.Any(f => f is AstProject && (f as AstProject).Name == c.Name));
      // changes
      var newfields = newcols.Select(c => {
        var field = fields.FirstOrDefault(f => (f is AstRename) ? (f as AstRename).OldName == c.Name : f.Name == c.Name);
        return field ?? new AstProject { Name = c.Name, DataType = c.DataType };
      });
      // additions
      var newext = fields.Where(f => f is AstExtend && !newcols.Any(c => c.Name == f.Name));
      return newfields.Concat(newext).ToArray();
    }

    // Generic function call, handles type checking, overloads and def funcs
    AstFunCall FunCall(Symbol op, AstNode[] args) {
      if (op.IsComponent) return Component(op, args);
      if (op.IsField) return FieldOf(op, args);
      DataType datatype;
      CallInfo callinfo;
      Types.CheckTypeError(op, out datatype, out callinfo, args.Select(a => a.DataType).ToArray());
      if (op.IsDefFunc) return DefCall(op, datatype, args, callinfo);
      if (op.IsOrdFunc) return FunCall(op, datatype, Args(Code(args[0] as AstValue, CurrentHeading()), args.Skip(1).ToArray()));
      if (op.IsRecurse) return FunCall(op, datatype, Args(args[0], args[1], Code(args[2] as AstValue, CurrentHeading())));
      if (op.IsUserSel) return UserCall(op, args);
      return FunCall(op, datatype, args, 0, callinfo);
    }

    // Call user function
    AstDefCall DefCall(Symbol op, DataType datatype, AstNode[] args, CallInfo callinfo) {
      var accbase = _accum.Total;
      _accum.Add(callinfo.AccumCount);
      return new AstDefCall {
        Func = FindFunc(SymNames.Invoke), DataType = datatype, DefFunc = op, Arguments = args,
        NumVarArgs = args.Length, CallInfo = callinfo, AccumBase = accbase,
      };
    }

    AstComponent Component(Symbol op, AstNode[] args) {
      Logger.Assert(args.Length == 1);
      Types.CheckTypeMatch(DataTypes.User, args[0].DataType);
      // FIX: check if component of type?
      return new AstComponent {
        Func = op, DataType = op.DataType, Arguments = args,
      };
    }

    AstFieldOf FieldOf(Symbol op, AstNode[] args) {
      Logger.Assert(args.Length == 1);
      Types.CheckTypeMatch(DataTypes.Row, args[0].DataType);
      return new AstFieldOf {
        Func = op, DataType = op.DataType, Arguments = args,
      };
    }

    // Call selector for UDT
    AstUserCall UserCall(Symbol op, AstNode[] args) {
      return new AstUserCall {
        Func = FindFunc(SymNames.UserSelector), UserFunc = op, DataType = op.DataType,
        Arguments = args, NumVarArgs = args.Length,
      };
    }

    // Internal function call, for known data type only
    AstFunCall FunCall(Symbol op, DataType datatype, AstNode[] args, int nvarargs = 0, CallInfo callinfo = null) {
      return new AstFunCall { Func = op, DataType = datatype, Arguments = args, NumVarArgs = nvarargs, CallInfo = callinfo };
    }

    // Arg array builder with variable args
    AstNode[] Args(params AstNode[] args) {
      return args;
    }

    // Arg array builder with simple flattening
    AstNode[] Args(AstNode arg, AstNode[] args) {
      return new AstNode[] { arg }.Concat(args).ToArray();
    }

    ///--------------------------------------------------------------------------------------------
    /// Variables and literals
    /// 
    public AstVariable Variable(string name) {
      var v = FindVariable(name);
      if (v.IsField) Scope.Current.LookupItems.Add(v.AsColumn());
      return new AstVariable { Variable = v, DataType = v.DataType };
    }

    public AstLiteral Literal(TypedValue value) {
      return new AstLiteral { Value = value, DataType = value.DataType };
    }

    public AstValue Binary(string value) {
      var b = new byte[value.Length / 2];
      for (var i = 0; i < b.Length; ++i) {
        int n;
        if (!Int32.TryParse(value.Substring(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out n))
          return null;
        b[i] = (byte)n;
      }
      return Literal(BinaryValue.Create(b));

    }
    public AstValue Bool(string value) {
      return Literal(BoolValue.Create(value[0] == 't'));
    }
    public AstValue Number(decimal value) {
      return Literal(NumberValue.Create(value));
    }
    public AstValue Number(string value) {
      Int64 iret;
      if (value[0] == '$' && Int64.TryParse(value.Substring(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out iret))
        return Number(Convert.ToDecimal(iret));
      decimal dret;
      if (Decimal.TryParse(value, out dret))
        return Number(dret);
      Parser.ParseError("invalid number: {0}", value);
      return null;
    }
    public AstLiteral Text(string value) {
      return Literal(TextValue.Create(value));
    }
    public AstValue Time(string value) {
      DateTime tret;
      if (DateTime.TryParse(value, out tret))
        return Literal(TimeValue.Create(tret));
      Parser.ParseError("invalid date or time: {0}", value);
      return null;
    }

    ///--------------------------------------------------------------------------------------------
    ///  Utility
    ///  
    /// The Find() routines are unconditional, and throw an error if they fail
    /// 
    public AstType FindType(string name) {
      var ut = Symbols.FindIdent(name);
      var datatype = (ut != null && ut.IsUserType) ? ut.DataType : Types.Find(name);
      if (datatype == null) Parser.ParseError("unknown type: {0}", name);
      return new AstType { DataType = datatype };
    }
    public AstType GetType(AstValue value) {
      return new AstType { DataType = value.DataType };
    }

    public Symbol FindCatVar(string name) {
      return FindIdent(name, o => o.IsCatVar);
    }
    public Symbol FindDefFunc(string name) {
      return FindIdent(name, o => o.IsDefFunc);
    }
    public Symbol FindField(string name) {
      return FindIdent(name, o => o.IsField);
    }
    public Symbol FindVariable(string name) {
      return FindIdent(name, o => o.IsVariable);
    }
    public Symbol FindFunc(string name) {
      return FindIdent(name, o => o.IsCallable);
    }
    public Symbol FindComponent(string name) {
      return FindIdent(name, o => o.IsComponent);
    }
    public Symbol FindUserType(string name) {
      return FindIdent(name, o => o.IsUserType);
    }

    // find a symbol that matches a predicate, or error
    public Symbol FindIdent(string name, Func<Symbol, bool> reqtype) {
      var ret = Symbols.FindIdent(name);
      if (!(reqtype(ret))) Parser.ParseError("unknown or invalid: {0}", name);
      return ret;
    }

    // find a symbol, or error
    public Symbol FindIdent(string name) {
      var ret = Symbols.FindIdent(name);
      if (ret == null) Parser.ParseError("not found: {0}", name);
      return ret;
    }
    // get a heading type
    public DataType Typeof(IEnumerable<AstField> fields) {
      if (fields == null) return DataTypes.Unknown;
      var typelist = fields.Select(f => DataColumn.Create(f.Name, f.DataType));
      return Types.Find(typelist);
    }
    // get a heading type
    public DataHeading Headingof(IEnumerable<AstField> fields) {
      if (fields == null) return DataHeading.Empty;
      var typelist = fields.Select(f => DataColumn.Create(f.Name, f.DataType));
      return DataHeading.Create(typelist, false);
    }

    ///============================================================================================
    ///
    /// scopes
    /// 

    // Enter scope for a function definition, with accumulator tracking
    public bool Enter(string ident, AstType rettype, IList<AstField> arguments) {
      var args = (arguments == null) ? new DataColumn[0] : arguments.Select(a => DataColumn.Create(a.Name, a.DataType)).ToArray();
      var rtype = (rettype == null) ? DataTypes.Unknown : rettype.DataType;
      Symbols.AddDeferred(ident, rtype, args);
      Scope.Push();
      foreach (var a in args)
        Symbols.AddVariable(a.Name, a.DataType, SymKinds.PARAM);
      _accum = _accum.Push();
      return true;
    }

    // Enter scope for a transform, with accumulator tracking
    public bool Enter(AstValue value) {
      Scope.Push(value.DataType);
      _accum = _accum.Push();
      return true;
    }

    // Continue scope but perhaps change heading, reset accumulators
    public bool Reenter(DataType datatype) {
      var dt = datatype ?? DataTypeRelation.Get(CurrentHeading()); // HACK:
      Scope.Pop();
      Scope.Push(dt);
      _accum.Reset(true);
      return true;
    }

    // Enter scope for a do block -- no accumulators
    public bool Enter() {
      Scope.Push();
      return true;
    }

    public bool Exit(bool accums = false) {
      Scope.Pop();
      if (accums) _accum = _accum.Pop();
      return true;
    }

    DataHeading CurrentHeading() {
      return Scope.Current.Heading ?? DataHeading.Empty;
    }
  }
}
