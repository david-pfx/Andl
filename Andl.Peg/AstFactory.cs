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

    // Do nothing method to allow state to be marked
    public T Node<T>(T node) {
      return node;
    }

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
      return new AstUserType();
    }

    public AstDefine SubType(string ident, AstType super) {
      var cols = new DataColumn[] { DataColumn.Create("super", super.DataType) };
      var ut = DataTypeUser.Get(ident, cols);
      Syms.AddUserType(ident, ut);
      return new AstSubType();
    }

    public AstStatement Source(string ident, AstLiteral value) {
      var datatype = Cat.GetRelvarType(ident, value.Value.ToString());  //FIX: better to be literal to here
      Syms.AddVariable(ident, datatype, SymKinds.CATVAR);
      return FunCall(FindFunc(SymNames.Import), Args(value, Text(ident), Text(Parser.Cat.SourcePath)));
    }

    public AstStatement Deferred(string ident, AstType rettype, IList<AstField> arguments, AstStatement body) {
      var op = FindDefFunc(ident);
      if (op.DataType == DataTypes.Unknown) op.DataType = body.DataType;
      Types.CheckTypeMatch(body.DataType, op.DataType);
      return FunCall(FindFunc(SymNames.Defer), Args(Code(body as AstValue, Headingof(arguments), ident, 0))); //FIX: accum
    }

    public AstStatement Assignment(string ident, AstValue value) {
      Syms.AddVariable(ident, value.DataType, SymKinds.CATVAR);
      return FunCall(FindFunc(SymNames.Assign), Args(Text(ident), value));
    }

    public AstStatement UpdateJoin(string ident, string joinop, AstValue expr) {
      var joinsym = FindFunc(joinop);
      var joinnum = Number((int)joinsym.JoinOp);
      return FunCall(FindFunc(SymNames.UpdateJoin), DataTypes.Void, Args(Variable(ident), expr, joinnum));
    }

    public AstStatement UpdateTransform(string ident, AstTranCall arg) {
      return FunCall(FindFunc(SymNames.UpdateTransform), DataTypes.Void, 
        Args(Code(arg.Where, Scope.Current.Heading), Code(arg.Transformer, Scope.Current.Heading)));
    }

    // Wrap transform tail for later handling
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

    public AstField Transform(string name, string rename = null, AstValue value = null) {
      if (name == null) return new AstLift { Value = value, Lookup = Scope.Current.Heading, DataType = value.DataType };
      if (rename != null) return new AstRename { Name = name, OldName = rename, DataType = FindField(rename).DataType };
      if (value != null) return new AstExtend { Name = name, Value = value, Lookup = Scope.Current.Heading, DataType = value.DataType };
      return new AstProject { Name = name, DataType = FindField(name).DataType };
    }
    public AstOrder Order(string name, bool desc, bool group) {
      return new AstOrder { Name = name, Descending = desc, Grouped = group };
    }

    public AstValue Row(AstTransformer transformer) {
      return new AstTabCall {
        Func = FindFunc(SymNames.Row), DataType = transformer.DataType, Arguments = transformer.Elements,
      };
      //return FunCall(FindFunc(SymNames.Row), transformer.DataType, Args(transformer));
    }
    public AstValue RowValues(IList<AstValue> values) {
      return new AstTabCall {
        Func = FindFunc(SymNames.Row), DataType = DataTypes.Unknown, Arguments = values.ToArray(),
      };
      //return FunCall(FindFunc(SymNames.Row), DataTypes.Unknown, values.ToArray()); //FIX:
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
    public AstValue Code(AstValue value, DataHeading lookup = null, string name = "?", int accums = 0) {
      return new AstCode {
        Name = name, Value = value, DataType = DataTypes.Code, Accums = accums,
        Lookup = (lookup == null) ? null : lookup,
      };
    }

    // construct a FunCall from the first OpCall, then invoke tail on result
    public AstValue PostFix(AstValue value, IList<AstCall> ops) {
      if (ops.Count == 0) return value;
      var newvalue = (ops[0] is AstTranCall)
        ? PostFix(value, ops[0] as AstTranCall)
        : PostFix(value, ops[0] as AstOpCall);
      return PostFix(newvalue, ops.Skip(1).ToList()); // tail
    }

    public AstValue PostFix(AstValue value, AstTranCall op) {
      var newvalue = value;
      if (op.Where != null)
        newvalue = FunCall(FindFunc(SymNames.Restrict), Args(value, Code(op.Where, Scope.Current.Heading)));
      var args = new List<AstNode> { newvalue };
      if (op.Orderer != null) args.AddRange(op.Orderer.Elements);
      if (op.Transformer != null) args.AddRange(op.Transformer.Elements);
      if (args.Count > 1) {
        var opname = (op.Orderer != null) ? SymNames.TransOrd
          : op.Transformer.Elements.Any(e => e is AstExtend) ? SymNames.Transform // FIX: transagg
          : op.Transformer.Elements.Any(e => e is AstRename) ? SymNames.Rename
          : SymNames.Project;
        newvalue = FunCall(FindFunc(opname),
          op.DataType, args.Count - 1, args.ToArray());
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

    // TODO: scope?
    public AstOpCall Dot(string arg) {
      return OpCall(arg);
    }

    public AstFunCall If(AstValue condition, AstValue iftrue, AstValue iffalse) {
      Types.CheckTypeMatch(iftrue.DataType, iffalse.DataType);
      return FunCall(FindFunc(SymNames.If), iftrue.DataType, Args(condition, Code(iftrue), Code(iffalse)));
    }

    public AstFoldCall Fold(string oper, AstValue expression) {
      var op = FindFunc(oper);
      if (!op.IsFoldable) Parser.ParseError("not foldable");
      return new AstFoldCall {
        Func = FindFunc(SymNames.Fold), FoldedFunc = op, Accums = 1, DataType = expression.DataType, FoldedExpr = expression,
      };
      //var op = FindFunc(oper);
      //if (!op.IsFoldable) Parser.ParseError("not foldable");
      //var acc = new AstAccumulator { DataType = expression.DataType, Index = -1 };
      //var folded = FunCall(op, acc, expression); // FIX: first s/b seed
      //return FunCall(FindFunc(SymNames.Fold), expression.DataType, Number((int)op.FoldSeed), Code(folded)); //TODO:
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

    //---
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

    AstFunCall FunCall(Symbol op, AstNode[] args) {
      DataType datatype;
      CallInfo callinfo;
      Types.CheckTypeError(op, out datatype, out callinfo, args.Select(a => a.DataType).ToArray());
      return FunCall(op, datatype, args);
    }
    AstFunCall FunCall(Symbol op, DataType type, AstNode[] args) {
      if (op.IsDefFunc) return new AstDefCall {
        Func = FindFunc(SymNames.Invoke), DefFunc = op, Arguments = args, DataType = type, NumVarArgs = args.Length,
      };
      return new AstFunCall { Func = op, Arguments = args, DataType = type };
    }

    AstFunCall FunCall(Symbol op, DataType type, int nvarargs, AstNode[] args) {
      return new AstFunCall { Func = op, Arguments = args, NumVarArgs = nvarargs, DataType = type };
    }

    AstNode[] Args(params AstNode[] args) {
      return args;
    }

    AstNode[] Args(AstNode arg, AstNode[] args) {
      return new AstNode[] { arg }.Concat(args).ToArray();
    }

    ///--------------------------------------------------------------------------------------------
    /// Variables and literals
    /// 
    public AstVariable Variable(string name) {
      var v = FindVariable(name);
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
      decimal dret;
      if (Decimal.TryParse(value, out dret))
        return Number(dret);
      return null;
    }
    public AstLiteral Text(string value) {
      return Literal(TextValue.Create(value));
    }
    public AstValue Time(string value) {
      DateTime tret;
      if (DateTime.TryParse(value, out tret))
        return Literal(TimeValue.Create(tret));
      return null;
    }

    ///--------------------------------------------------------------------------------------------
    ///  Utility
    /// 
    public AstType FindType(string name) {
      return new AstType { DataType = Types.Find(name) };
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
      return DataHeading.Create(typelist, true);
    }
  }
}
