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

  /// <summary>
  /// Base class for AST definitions
  /// </summary>
  public class AstDefine : AstStatement {
    public string Name { get; set; }
    public DataType Type { get; set; }
    public AstField[] Fields { get; set; }
    public AstStatement Value { get; set; }
  }
  public class AstUserType : AstDefine { }
  public class AstSubType : AstDefine { }
  public class AstDefer : AstDefine { }
  public class AstSource : AstDefine { }
  public class AstAssign : AstDefine { }

  /// <summary>
  /// Base class for AST field info
  /// </summary>
  public class AstField : AstNode {
    public string Name { get; set; }
    public DataType DataType { get; set; }
  }
  public class AstProject : AstField { }
  public class AstRename : AstField {
    public string OldName { get; set; }
  }
  public class AstExtend : AstField {
    public AstValue Value { get; set; }
  }
  public class AstLift : AstExtend { }
  public class AstOrder : AstField {
    public bool Desc { get; set; }
    public bool Group { get; set; }
  }

  /// <summary>
  /// Base class for AST values
  /// </summary>
  public class AstValue : AstStatement { }
  public class AstOperator : AstValue {
    public Symbol Symbol { get; set; }
    public override string ToString() {
      return string.Format("{0}:{1}", Symbol.Name, DataType);
    }
  }
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
  }
  public class AstWrap : AstValue {
    public AstValue Value { get; set; }
    public override string ToString() {
      return string.Format("{0} => {1}", Value, DataType);
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

  public class AstDefCall : AstValue {
    public Symbol Operator { get; set; }
    public AstField[] Fields { get; set; }
    public override string ToString() {
      return string.Format("{0} => {1}", Operator.Name, String.Join(", ", Fields.Select(f => f.ToString())));
    }
  }
  public class AstOrderer : AstValue {
    public AstOrder[] Elements { get; set; }
    public override string ToString() {
      return string.Format("{0}", String.Join(",", Elements.Select(e => e.ToString())));
    }
  }

  public class AstTransformer : AstValue {
    public bool Lift { get; set; }
    //public bool Allbut { get; set; }
    public AstField[] Elements { get; set; }
    public override string ToString() {
      return string.Format("{0}:{1}", Lift, String.Join(", ", Elements.Select(e => e.ToString())));
      //return string.Format("{0}:{1}", Allbut, String.Join(", ", Elements.Select(e => e.ToString())));
    }
  }

  public class AstAccumulator : AstValue {
    public int Index { get; set; }
  }

    ///==============================================================================================
    /// <summary>
    /// Implement factory for AST nodes
    /// </summary>
    public class AstFactory {
    public TypeSystem Types { get; set; }
    public SymbolTable Syms { get; set; }
    public Catalog Cat { get; set; }
    public PegParser Parser { get; set; }

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
      return new AstUserType {
        Name = ident,
        Fields = fields,
        DataType = DataTypes.Void,
      };
    }
    public AstDefine SubType(string ident, AstType super) {
      var cols = new DataColumn[] { DataColumn.Create("super", super.DataType) };
      var ut = DataTypeUser.Get(ident, cols);
      Syms.AddUserType(ident, ut);
      return new AstSubType {
        Name = ident,
        Type = super.DataType,
        DataType = DataTypes.Void,
      };
    }
    public AstDefine Source(string ident, AstLiteral<string> value) {
      var datatype = Cat.GetRelvarType(ident, value.Value);
      Syms.AddVariable(ident, datatype, SymKinds.CATVAR);
      return new AstSource {
        Name = ident,
        Value = value,
        DataType = DataTypes.Void,
      };
    }
    public AstDefine Deferred(string ident, AstType rettype, IList<AstField> arguments, AstStatement body) {
      return new AstDefer {
        Name = ident,
        Type = (rettype == null) ? null : rettype.DataType, // FIX
        Fields = (arguments == null) ? null : arguments.ToArray(),
        Value = body,
        DataType = DataTypes.Void,
      };
    }

    public AstStatement Assignment(string ident, AstValue value) {
      Syms.AddVariable(ident, value.DataType, SymKinds.CATVAR);
      return new AstAssign {
        Name = ident,
        Value = Wrap(value, "code"),
        DataType = DataTypes.Void,
      };
    }
    public AstStatement UpdateJoin(string ident, string op, AstValue expr) {
      return FunCall(":upjoin", OpCall(op, expr)); // FIX:
      //return FunCall(":upjoin", FindCatVar(ident), OpCall(op, expr)); // FIX:
    }
    public AstStatement UpdateTransform(string ident, AstTranCall arg) {
      return FunCall(":uptrans", arg.Arguments);  // BUG:
    }

    // Wrap transform tail for later handling
    public AstTranCall TransformTail(AstValue where, AstOrderer order, AstTransformer trans) {
      if (trans != null) {
        //if (trans.Allbut) trans = Allbut(Scope.Current.Heading, trans);
        Scope.Pop();
        //Scope.Push(Typeof(trans.Elements));
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
      //if (transformer.Allbut) transformer = Allbut(Scope.Current.Heading, transformer);
      //return FunCall(FindOperator(SymNames.Row), Typeof(transformer.Elements), transformer);
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
      //return new AstTransformer { Elements = args, Allbut = allbut, DataType = Typeof(args) };
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
    //public AstValue Code(AstValue value) {
    //  return new AstWrap { Value = value, DataType = DataTypes.Code };
    //}

    public AstValue Wrap(AstValue value, string datatype) {
      return new AstWrap { Value = value, DataType = DataTypes.Find(datatype) };
    }

    // construct a FunCall from the first OpCall, then invoke tail on result
    public AstValue PostFix(AstValue value, IList<AstOpCall> ops) {
      if (ops.Count == 0) return value;
      var newvalue = value;
      if (ops[0] is AstTranCall) {
        if (ops[0].Arguments[0] != null)
          newvalue = FunCall(FindOperator(SymNames.Restrict), value, Wrap(ops[0].Arguments[0], "code[]"));
        if (ops[0].Arguments[1] != null) {
          var args = ops[0].Arguments[1] as AstOrderer;
          Logger.Assert(args != null);
          newvalue = FunCall(FindOperator(SymNames.TransOrd), ops[0].DataType, value, args);
          //newvalue = FunCall(FindOperator(SymNames.TransOrd), value, Wrap(args, "code[]"));
        }
        if (ops[0].Arguments[2] != null) {
          var args = ops[0].Arguments[2] as AstTransformer;
          //Logger.Assert(args != null && !args.Allbut);
          newvalue = FunCall(FindOperator(SymNames.Transform), ops[0].DataType, value, args);
          //newvalue = FunCall(FindOperator(SymNames.Transform), value, Wrap(args, "code[]"));
        }
      } else
        newvalue = FunCall(ops[0].Operator, new AstValue[] { value }.Concat(ops[0].Arguments).ToArray()); // cons
      return PostFix(newvalue, ops.Skip(1).ToList()); // tail
    }

    public AstOpCall Recurse(AstValue arg) {
      return OpCall("recurse", arg);
    }

    public AstOpCall Dot(string arg) {
      // FIX: scope
      return OpCall(arg);
    }

    public AstFunCall If(AstValue condition, AstValue iftrue, AstValue iffalse) {
      Types.CheckTypeMatch(iftrue.DataType, iffalse.DataType);
      return FunCall(FindOperator("if"), iftrue.DataType, condition, Wrap(iftrue, "code"), Wrap(iffalse, "code"));
    }

    public AstFunCall Fold(string oper, AstValue expression) {
      var op = FindOperator(oper);
      var acc = new AstAccumulator { DataType = expression.DataType, Index = -1 };
      var folded = FunCall(op, acc, expression); // FIX: first s/b seed
      return FunCall(FindOperator("cfold"), expression.DataType, acc, Wrap(folded, "code"));
      //return FunCall(FindOperator("fold"), Wrap(folded, "code"));
    }

    AstField[] Allbut(DataHeading heading, AstField[] fields) {
      var proj = fields.Where(e => e is AstProject)
        .Select(e => e.Name);
      var newproj = heading.Columns.Where(c => !(proj.Contains(c.Name)))
        .Select(c => new AstProject { Name = c.Name, DataType = c.DataType });
      var neweles = fields.Where(e => !(e is AstProject))
        .Union(newproj);
      return neweles.ToArray();
    }

    //AstTransformer Allbut(DataHeading heading, AstTransformer fields) {
    //  var proj = fields.Elements.Where(e => e is AstProject)
    //    .Select(e => e.Name);
    //  var newproj = heading.Columns.Where(c => !(proj.Contains(c.Name)))
    //    .Select(c => new AstProject { Name = c.Name, DataType = c.DataType });
    //  var neweles = fields.Elements.Where(e => !(e is AstProject))
    //    .Union(newproj);
    //  return Transformer(false, neweles.ToArray());
    //}

    public AstFunCall FunCall(string name, params AstValue[] args) {
      return FunCall(FindOperator(name), args);
    }
    public AstFunCall FunCall(Symbol op, params AstValue[] args) {
      DataType datatype;
      CallInfo callinfo;
      Types.CheckTypeError(op, out datatype, out callinfo, args.Select(a => a.DataType).ToArray());
      return FunCall(op, datatype, args);
    }
    public AstFunCall FunCall(Symbol op, DataType type, params AstValue[] args) {
      return new AstFunCall { Operator = op, Arguments = args, DataType = type };
    }

    public AstOpCall OpCall(string name, params AstValue[] args) {
      var op = FindOperator(name);
      var type = op.DataType;
      return new AstOpCall() { Operator = op, Arguments = args, DataType = type };
    }
    public AstDefCall DefCall(string name, params AstField[] fields) {
      var op = FindOperator(name);
      var type = op.DataType;
      return new AstDefCall { Operator = op, Fields = fields, DataType = type };
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
    public AstValue Number(string value) {
      decimal dret;
      if (Decimal.TryParse(value, out dret))
        return Literal("number", dret);
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
