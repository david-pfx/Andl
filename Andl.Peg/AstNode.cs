using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Andl.Runtime;

namespace Andl.Peg {
  public class CodeValue { }

  /// <summary>
  /// Base class for AST nodes
  /// </summary>
  public class AstNode {
  }
  public class AstType : AstNode {
    public DataType DataType { get; set; }
  }
  // A statement also can be a define or a value/expression
  public class AstStatement : AstNode {
    public DataType DataType { get; set; }
  }
  public class AstDefine : AstStatement { }
  public class AstUserType : AstDefine { }
  public class AstSubType : AstDefine { }

  /// <summary>
  /// Base class for AST field info
  /// </summary>
  public class AstField : AstNode {
    public string Name { get; set; }
    public DataType DataType { get; set; }
  }
  public class AstProject : AstField {
    public override string ToString() {
      return string.Format("{0}", Name);
    }
  }
  public class AstRename : AstField {
    public string OldName { get; set; }
    public override string ToString() {
      return string.Format("{0}:={1}", Name, OldName);
    }
  }
  public class AstExtend : AstField {
    public AstValue Value { get; set; }
    public override string ToString() {
      return string.Format("{0}:={1}", Name, Value);
    }
  }
  public class AstLift : AstExtend {
    public override string ToString() {
      return string.Format("Lift({0})", Value);
    }
  }
  public class AstOrder : AstField {
    public bool Desc { get; set; }
    public bool Group { get; set; }
    public override string ToString() {
      return string.Format("{0}{1}{2}", Desc ? "-" :"", Group ? "%" : "", Name);
    }
  }

  /// <summary>
  /// Base class for AST values
  /// </summary>
  public class AstValue : AstStatement { } // FIX: probably wrong

  public class AstVariable : AstValue {
    public Symbol Variable { get; set; }
    public override string ToString() {
      return string.Format("{0}:{1}", Variable.Name, DataType);
    }
  }

  public class AstLiteral<T> : AstValue {
    public T Value { get; set; }
    public override string ToString() {
      return Value.ToString();
    }
  }

  public class AstBlock : AstValue {
    public AstStatement[] Statements { get; set; }
    public override string ToString() {
      return string.Format("[{0}]", String.Join("; ", Statements.Select(s => s.ToString())));
    }
  }
  public class AstWrap : AstValue {
    public AstValue Value { get; set; }
    public override string ToString() {
      return string.Format("{0} =>> {1}", Value, DataType);
    }
  }
  public class AstFunCall : AstValue {
    public Symbol Operator { get; set; }
    public AstValue[] Arguments { get; set; }
    public override string ToString() {
      return string.Format("{0}({1}) => {2}", Operator.Name, String.Join(", ", Arguments.Select(a => a.ToString())), DataType);
    }
  }
  public class AstOpCall : AstFunCall { }
  public class AstTranCall : AstOpCall { }

  public class AstOrderer : AstValue {
    public AstOrder[] Elements { get; set; }
    public override string ToString() {
      return string.Format("$({0})", String.Join(",", Elements.Select(e => e.ToString())));
    }
  }

  public class AstTransformer : AstValue {
    public bool Lift { get; set; }
    public AstField[] Elements { get; set; }
    public override string ToString() {
      return string.Format("{0}({1})", Lift ? "lift" : "tran", String.Join(",", Elements.Select(e => e.ToString())));
    }
  }

  public class AstAccumulator : AstValue {
    public int Index { get; set; }
    public override string ToString() {
      return string.Format("Ac~{0}", Index);
    }
  }

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
    public T Node<T> (T node) {
      return node;
    }

    // just a set of statements
    public AstBlock Block(IList<AstStatement> statements) {
      return new AstBlock {
        Statements = statements.ToArray(),
        DataType = (statements == null || statements.Count == 0) ? DataTypes.Void : statements.Last().DataType };
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

    public AstStatement Source(string ident, AstLiteral<string> value) {
      var datatype = Cat.GetRelvarType(ident, value.Value);
      Syms.AddVariable(ident, datatype, SymKinds.CATVAR);
      return FunCall(FindOperator(SymNames.Import), value, Text(ident), Text(Parser.Cat.SourcePath));
    }

    public AstStatement Deferred(string ident, AstType rettype, IList<AstField> arguments, AstStatement body) {
      var op = FindDefFunc(ident);
      if (op.DataType == DataTypes.Unknown) op.DataType = body.DataType;
      Types.CheckTypeMatch(body.DataType, op.DataType);
      return FunCall(FindOperator(SymNames.Defer), Text(ident), body as AstValue);
    }

    public AstStatement Assignment(string ident, AstValue value) {
      Syms.AddVariable(ident, value.DataType, SymKinds.CATVAR);
      return FunCall(FindOperator(SymNames.Assign), Text(ident), value);
    }

    public AstStatement UpdateJoin(string ident, string joinop, AstValue expr) {
      var joinsym = FindOperator(joinop);
      var joinnum = Number((int)joinsym.JoinOp);
      return FunCall(FindOperator(SymNames.UpdateJoin), DataTypes.Void, Variable(ident), expr, joinnum);
    }

    public AstStatement UpdateTransform(string ident, AstTranCall arg) {
      // FIX: should not need code[]
      return FunCall(FindOperator(SymNames.UpdateTransform), DataTypes.Void, Code(arg.Arguments[0]), Code(arg.Arguments[2], "code[]"));
    }

    // Wrap transform tail for later handling
    public AstTranCall TransformTail(AstValue where, AstOrderer order, AstTransformer trans) {
      if (trans != null) {
        Scope.Pop();
        Scope.Push(trans.DataType);
      }
      return new AstTranCall {
        DataType = DataTypeRelation.Get(Scope.Current.Heading),
        Arguments = new AstValue[] { where, order, trans }
      };
    }

    public AstField Field(string ident, AstType type) {
      return new AstField() {
        Name = ident,
        DataType = (type == null) ? Types.Find("text") : type.DataType
      };
    }
    public AstField Transform(string name, string rename = null, AstValue value = null) {
      if (name == null) return new AstLift { Value = value, DataType = value.DataType };
      if (rename != null) return new AstRename { Name = name, OldName = rename, DataType = FindField(rename).DataType };
      if (value != null) return new AstExtend { Name = name, Value = value, DataType = value.DataType };
      return new AstProject { Name = name, DataType = FindField(name).DataType };
    }
    public AstOrder Order(string name, bool desc, bool group) {
      return new AstOrder { Name = name, Desc = desc, Group = group };
    }

    public AstValue Row(AstTransformer transformer) {
      return FunCall(FindOperator(SymNames.Row), transformer.DataType, transformer);
    }
    public AstValue RowValues(IList<AstValue> values) {
      return FunCall(FindOperator(SymNames.Row), DataTypeTuple.Empty, values.ToArray()); //FIX:
    }

    public AstValue Table(AstValue heading, IList<AstValue> rows) {
      var rowtype = (heading != null) ? heading.DataType 
        : rows.Count > 0 ? rows[0].DataType
        : DataTypeTuple.Empty;
      if (heading != null)
        foreach (var r in rows) r.DataType = rowtype;
      return FunCall(FindOperator(SymNames.Table), Types.Relof(rowtype), rows == null ? null : rows.ToArray());
    }

    public AstValue Heading(IList<AstField> fields) {
      var type = (fields == null) ? DataTypeRelation.Empty : Typeof(fields);
      return new AstValue { DataType = type };
    }

    public AstValue Table(bool star) {
      var ret = FunCall(FindOperator(SymNames.Table), DataTypeRelation.Empty, Transformer(true, null)); //FIX:
      return ret;
    }

    ///--------------------------------------------------------------------------------------------
    /// Headings etc
    /// 

    public AstOrderer Orderer(IList<AstOrder> argslist) {
      var args = argslist.ToArray();
      return new AstOrderer { Elements = args, DataType = DataTypes.Find("code[]") };
      //return new AstOrderer { Elements = args, DataType = Typeof(args) };
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
      if (ops.Length == 1) return FunCall(ops[0].Operator, value, ops[0].Arguments[0]);
      if (ops[0].Operator.Precedence >= ops[1].Operator.Precedence)
        return Expression(FunCall(ops[0].Operator, value, ops[0].Arguments[0]), ops.Skip(1).ToArray());
      return FunCall(ops[0].Operator, value, Expression(ops[0].Arguments[0], ops.Skip(1).ToArray()));
    }
    public AstValue Code(AstValue value, string type = "code") {
      return new AstWrap { Value = value, DataType = DataTypes.Find(type) };
    }

    // construct a FunCall from the first OpCall, then invoke tail on result
    public AstValue PostFix(AstValue value, IList<AstOpCall> ops) {
      if (ops.Count == 0) return value;
      var newvalue = value;
      if (ops[0] is AstTranCall) {
        if (ops[0].Arguments[0] != null)
          newvalue = FunCall(FindOperator(SymNames.Restrict), value, Code(ops[0].Arguments[0], "code[]"));
        if (ops[0].Arguments[1] != null) {
          var args = ops[0].Arguments[1] as AstOrderer;
          Logger.Assert(args != null);
          newvalue = FunCall(FindOperator(SymNames.TransOrd), ops[0].DataType, value, Code(args));
        }
        if (ops[0].Arguments[2] != null) {
          var args = ops[0].Arguments[2] as AstTransformer;
          newvalue = FunCall(FindOperator(SymNames.Transform), ops[0].DataType, value, Code(args));
        }
      } else
        newvalue = FunCall(ops[0].Operator, new AstValue[] { value }.Concat(ops[0].Arguments).ToArray()); // cons
      return PostFix(newvalue, ops.Skip(1).ToList()); // tail
    }

    // TODO: 
    public AstOpCall Recurse(AstValue arg) {
      return OpCall("recurse", arg);
    }

    // TODO: scope
    public AstOpCall Dot(string arg) {
      return OpCall(arg);
    }

    public AstFunCall If(AstValue condition, AstValue iftrue, AstValue iffalse) {
      Types.CheckTypeMatch(iftrue.DataType, iffalse.DataType);
      return FunCall(FindOperator(SymNames.If), iftrue.DataType, condition, Code(iftrue), Code(iffalse));
    }

    public AstFunCall Fold(string oper, AstValue expression) {
      var op = FindOperator(oper);
      if (!op.IsFoldable) Parser.ParseError("not foldable");
      var acc = new AstAccumulator { DataType = expression.DataType, Index = -1 };
      var folded = FunCall(op, acc, expression); // FIX: first s/b seed
      return FunCall(FindOperator(SymNames.Fold), expression.DataType, Number((int)op.FoldSeed), Code(folded)); //TODO:
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

    public AstFunCall Function(string name, params AstValue[] args) {
      var op = FindOperator(name);
      return FunCall(op, args);
    }
    AstFunCall FunCall(Symbol op, params AstValue[] args) {
      DataType datatype;
      CallInfo callinfo;
      Types.CheckTypeError(op, out datatype, out callinfo, args.Select(a => a.DataType).ToArray());
      return FunCall(op, datatype, args);
    }
    AstFunCall FunCall(Symbol op, DataType type, params AstValue[] args) {
      Logger.Assert(type.IsVariable || type == DataTypes.Void, type);
      return new AstFunCall { Operator = op, Arguments = args, DataType = type };
    }

    public AstOpCall OpCall(string name, params AstValue[] args) {
      var op = FindOperator(name);
      var type = op.DataType;
      return new AstOpCall() { Operator = op, Arguments = args, DataType = type };
    }
    public AstValue[] ValueList(params AstValue[] args) {
      return args;
    }

    ///--------------------------------------------------------------------------------------------
    /// Variables and literals
    /// 
    public AstVariable Variable(string name) {
      var v = FindVariable(name);
      return new AstVariable { Variable = v, DataType = v.DataType };
    }

    public AstLiteral<T> Literal<T>(string type, T value) {
      return new AstLiteral<T> { Value = value, DataType = Types.Find(type) };
    }

    public AstValue Binary(string value) {
      var b = new byte[value.Length / 2];
      for (var i = 0; i < b.Length; ++i) {
        int n;
        if (!Int32.TryParse(value.Substring(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out n))
          return null;
        b[i] = (byte)n;
      }
      return Literal("binary", b);

    }
    public AstValue Bool(string value) {
      return Literal("bool", value[0] == 't');
    }
    public AstValue Number(decimal value) {
      return Literal("number", value);
    }
    public AstValue Number(string value) {
      decimal dret;
      if (Decimal.TryParse(value, out dret))
        return Number(dret);
      return null;
    }
    public AstLiteral<string> Text(string value) {
      return Literal("text", value);
    }
    public AstValue Time(string value) {
      DateTime tret;
      if (DateTime.TryParse(value, out tret))
        return Literal("time", tret);
      return null;
    }

    ///--------------------------------------------------------------------------------------------
    ///  Utility
    /// 
    public AstType FindType(string name) {
      return new AstType { DataType = Types.Find(name) };
    }
    public AstType Typeof(AstValue value) {
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
    public Symbol FindOperator(string name) {
      var ret = Syms.FindIdent(name);
      return ret != null && ret.IsCallable ? ret : null;
    }
    // get a heading type
    public DataType Typeof(IEnumerable<AstField> fields) {
      var typelist = fields.Select(f => new Field { Name = f.Name, Type = f.DataType });
      return Types.Find(typelist);
    }
  }
}
