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
    public TypeSystem Types { get { return Parser.Types; } }
    public SymbolTable Syms { get { return Parser.Symbols; } }
    public Catalog Cat { get { return Parser.Cat; } }
    public PegParser Parser { get; set; }


    // just a set of statements
    public AstBlock Block(IList<AstStatement> statements) {
      return new AstBlock {
        Statements = statements.ToArray(),
        DataType = (statements == null || statements.Count == 0) ? DataTypes.Void : statements.Last().DataType
      };
    }

    public AstDefine UserType(string ident, AstField[] fields) {
      var ff = fields.Select(a => DataColumn.Create(a.Name, a.DataType)).ToArray();
      var ut = DataTypeUser.Get(ident, ff);
      Syms.AddUserType(ident, ut);
      Syms.AddCatalog(Syms.FindIdent(ident));
      return new AstUserType { DataType = DataTypes.Void };
    }

    public AstDefine SubType(string ident, AstType super) {
      var cols = new DataColumn[] { DataColumn.Create("super", super.DataType) };
      var ut = DataTypeUser.Get(ident, cols);
      Syms.AddUserType(ident, ut);
      return new AstSubType { DataType = DataTypes.Void };
    }

    public AstStatement Source(string ident, AstLiteral value) {
      var datatype = Cat.GetRelvarType(ident, value.Value.ToString());  //FIX: better to be literal to here
      Syms.AddVariable(ident, datatype, SymKinds.CATVAR);
      Syms.AddCatalog(Syms.FindIdent(ident));
      return FunCall(FindFunc(SymNames.Import), Args(value, Text(ident), Text(Parser.Cat.SourcePath)));
    }

    public AstStatement Deferred(string ident, AstType rettype, IList<AstField> arguments, AstStatement body) {
      var op = FindDefFunc(ident);
      if (op.DataType == DataTypes.Unknown) op.DataType = body.DataType;
      Syms.AddCatalog(op);
      Types.CheckTypeMatch(body.DataType, op.DataType);
      return FunCall(FindFunc(SymNames.Defer), Args(Code(body as AstValue, Headingof(arguments), ident, -1, true))); //FIX: accum
    }

    public AstStatement Assignment(string ident, AstValue value) {
      Syms.AddVariable(ident, value.DataType, SymKinds.CATVAR);
      Syms.AddCatalog(Syms.FindIdent(ident));
      return FunCall(FindFunc(SymNames.Assign), Args(Text(ident), value));
    }

    public AstStatement UpdateJoin(string ident, string joinop, AstValue expr) {
      var joinsym = FindFunc(joinop);
      var joinnum = Number((int)joinsym.JoinOp);
      return FunCall(FindFunc(SymNames.UpdateJoin), DataTypes.Void, Args(Variable(ident), expr, joinnum));
    }

    public AstStatement UpdateTransform(string ident, AstTranCall arg) {
      return FunCall(FindFunc(SymNames.UpdateTransform), DataTypes.Void,
        Args(arg.Where, Code(arg.Transformer, Scope.Current.Heading)));
    }

    // Wrap transform tail for later handling, update scope for type inference
    public AstTranCall TransformTail(AstValue where, AstOrderer order, AstTransformer trans) {
      if (trans != null) {
        Scope.Pop();
        Scope.Push(trans.DataType);
      }
      return new AstTranCall {
        DataType = DataTypeRelation.Get(Scope.Current.Heading),
        Where = where, Transformer = trans, Orderer = order,
      };
    }

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
      if (allbut) args = Allbut(Scope.Current.Heading, args);
      return new AstTransformer { Elements = args, DataType = Typeof(args) };
    }

    // take generic transform element and create specific type of AST with all required info
    // each element uses the same scope, but tracks individual lookup items and folds
    public AstField Transform(string name, string rename = null, AstValue value = null) {
      var lookup = DataHeading.Create(Scope.Current.LookupItems.Items);
      var accums = Scope.Current.Accums;
      Scope.Current.Reset();
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

    public AstValue Where(AstValue value) {
      var lookup = DataHeading.Create(Scope.Current.LookupItems.Items);
      var accums = Scope.Current.Accums;
      Scope.Current.Reset();
      return Code(value, lookup, "?");
    }

    public AstOrder Order(string name, bool desc, bool group) {
      return new AstOrder { Name = name, Descending = desc, Grouped = group };
    }

    public AstValue Row(AstTransformer transformer) {
      return new AstTabCall {
        Func = FindFunc(SymNames.RowE), DataType = transformer.DataType, Arguments = transformer.Elements,
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

    public AstValue Table(bool star) {
      var ret = FunCall(FindFunc(SymNames.Table), DataTypeRelation.Empty, Args(Transformer(true, null))); //FIX:
      return ret;
    }

    ///--------------------------------------------------------------------------------------------
    /// Headings etc
    /// 

    public AstField Field(string ident, AstType type) {
      return new AstField() {
        Name = ident,
        DataType = (type == null) ? Types.Find("text") : type.DataType
      };
    }

    ///--------------------------------------------------------------------------------------------
    /// Calls with arguments
    /// 
    public AstValue Expression(AstValue value, IList<AstOpCall> ops) {
      var ret = Expression(value, ops.ToArray());
      if (Logger.Level >= 4) Logger.WriteLine("Expression {0}", ret);
      return ret;
    }
    public AstValue Expression(AstValue value, AstOpCall[] ops) {
      if (ops.Length == 0) return value;
      if (ops.Length == 1) return FunCall(ops[0].Func, Args(value, ops[0].Arguments[0]));
      if (ops[0].Func.Precedence >= ops[1].Func.Precedence)
        return Expression(FunCall(ops[0].Func, Args(value, ops[0].Arguments[0])), ops.Skip(1).ToArray());
      return FunCall(ops[0].Func, Args(value, Expression(ops[0].Arguments[0] as AstValue, ops.Skip(1).ToArray())));
    }

    // construct a FunCall from the first OpCall, then invoke tail on result
    public AstValue PostFix(AstValue value, IList<AstCall> ops) {
      if (ops.Count == 0) return value;
      var newvalue = (ops[0] is AstTranCall)
        ? PostFix(value, ops[0] as AstTranCall)
        : PostFix(value, ops[0] as AstOpCall);
      return PostFix(newvalue, ops.Skip(1).ToList()); // tail
    }

    // translate set of transform calls into a function call (or two)
    public AstValue PostFix(AstValue value, AstTranCall tran) {
      var newvalue = value;
      if (tran.Where != null)
        newvalue = FunCall(FindFunc(SymNames.Restrict), value.DataType, 1, Args(value, tran.Where));
      var args = new List<AstNode> { newvalue };
      if (tran.Orderer != null) args.AddRange(tran.Orderer.Elements);
      if (tran.Transformer != null) args.AddRange(tran.Transformer.Elements);
      if (args.Count > 1) {
        var opname = (tran.Orderer != null) ? SymNames.TransOrd
          : tran.Transformer.Elements.Any(e => e is AstExtend && (e as AstExtend).Accums > 0) ? SymNames.TransAgg
          : tran.Transformer.Elements.Any(e => e is AstExtend) ? SymNames.Transform
          : tran.Transformer.Elements.Any(e => e is AstRename) ? SymNames.Rename
          : SymNames.Project;
        newvalue = FunCall(FindFunc(opname), tran.DataType, args.Count - 1, args.ToArray());
        if (tran.Transformer != null && tran.Transformer.Lift)
          newvalue = FunCall(FindFunc(SymNames.Lift), newvalue.DataType, Args(newvalue));
      }
      return newvalue;
    }

    public AstValue PostFix(AstValue value, AstOpCall op) {
      return FunCall(op.Func, new AstValue[] { value }.Concat(op.Arguments).ToArray()); // cons
    }

    // TODO: 
    public AstOpCall Recurse(AstValue arg) {
      return OpCall("recurse", arg);
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

    public AstFunCall If(AstValue condition, AstValue iftrue, AstValue iffalse) {
      Types.CheckTypeMatch(iftrue.DataType, iffalse.DataType);
      return FunCall(FindFunc(SymNames.If), iftrue.DataType, Args(condition, Code(iftrue), Code(iffalse)));
    }

    public AstFoldCall Fold(string oper, AstValue expression) {
      var op = FindFunc(oper);
      if (!op.IsFoldable) Parser.ParseError("not foldable");
      var accum = Scope.Current.Accums++;
      return new AstFoldCall {
        Func = FindFunc(SymNames.Fold), FoldedOp = op, Accum = accum, DataType = expression.DataType, FoldedExpr = expression,
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
        Name = name, Value = value, DataType = DataTypes.Code, Accum = accums, Lookup = lookup, Defer = defer,
      };
    }

    AstField[] Allbut(DataHeading heading, AstField[] fields) {
      var newcols = heading.Columns.Select(c => c.Name)
        .Except(fields.Select(f => f.Name))
        .Except(fields.Where(f => f is AstRename).Select(f => (f as AstRename).OldName));
      var newproj = heading.Columns.Where(c => newcols.Contains(c.Name))
        .Select(c => new AstProject { Name = c.Name, DataType = c.DataType });
      var neweles = fields.Where(e => !(e is AstProject))
        .Union(newproj);
      return neweles.ToArray();
    }

    // Generic function call, handles type checking, overloads and def funcs
    AstFunCall FunCall(Symbol op, AstNode[] args) {
      if (op.IsComponent) {
        Logger.Assert(args.Length == 1);
        Types.CheckTypeMatch(DataTypes.User, args[0].DataType);
        // FIX: check if component of type?
        return new AstComponent {
          Func = op, DataType = op.DataType, Arguments = args,
        };
      }
      DataType datatype;
      CallInfo callinfo;
      Types.CheckTypeError(op, out datatype, out callinfo, args.Select(a => a.DataType).ToArray());
      if (op.IsDefFunc) return new AstDefCall {
        Func = FindFunc(SymNames.Invoke), DataType = datatype, DefFunc = op, Arguments = args,
        NumVarArgs = args.Length, CallInfo = callinfo,
      };
      if (op.IsUserSel) return new AstUserCall {
        Func = FindFunc(SymNames.UserSelector), UserFunc = op, DataType = op.DataType,
        Arguments = args, NumVarArgs = args.Length,
      };
      return new AstFunCall {
        Func = op, DataType = datatype, Arguments = args, CallInfo = callinfo,
      };
    }

    // Internal function call, for known data type only
    AstFunCall FunCall(Symbol op, DataType datatype, AstNode[] args) {
      return new AstFunCall { Func = op, DataType = datatype, Arguments = args };
    }

    // Internal function call, for varargs and known data type only
    AstFunCall FunCall(Symbol op, DataType datatype, int nvarargs, AstNode[] args) {
      return new AstFunCall { Func = op, DataType = datatype, Arguments = args, NumVarArgs = nvarargs };
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
    public AstType FindType(string name) {
      var ut = FindUserType(name);
      var datatype = (ut != null) ? ut.DataType : Types.Find(name);
      return new AstType { DataType = datatype };
    }
    public AstType GetType(AstValue value) {
      return new AstType { DataType = value.DataType };
    }

    public Symbol FindCatVar(string name) {
      var ret = Syms.FindIdent(name);
      return ret != null && ret.IsCatVar ? ret : null;
    }
    public Symbol FindDefFunc(string name) {
      var ret = Syms.FindIdent(name);
      return ret != null && ret.IsDefFunc ? ret : null;
    }
    public Symbol FindField(string name) {
      var ret = Syms.FindIdent(name);
      return ret != null && ret.IsField ? ret : null;
    }
    public Symbol FindVariable(string name) {
      var ret = Syms.FindIdent(name);
      return ret != null && ret.IsVariable ? ret : null;
    }
    public Symbol FindFunc(string name) {
      var ret = Syms.FindIdent(name);
      return ret != null && ret.IsCallable ? ret : null;
    }
    public Symbol FindComponent(string name) {
      var ret = Syms.FindIdent(name);
      return ret != null && ret.IsComponent ? ret : null;
    }
    public Symbol FindUserType(string name) {
      var ret = Syms.FindIdent(name);
      return ret != null && ret.IsUserType ? ret : null;
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
  }
}
