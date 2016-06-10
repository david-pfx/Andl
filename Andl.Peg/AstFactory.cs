using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using Andl.Runtime;
using Andl.Common;

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
      public bool HasWin { get; set; }
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
        HasWin = false;
        if (all) Total = 0;
        Logger.WriteLine(4, "Reset {0} {1}", all, this);
      }
      public override string ToString() {
        return string.Format("Accum:{0} c={1} t={2} w={3}", Enabled, Count, Total, HasWin);
      }
    }

    public TypeSystem Types { get { return Parser.Types; } }
    public SymbolTable Symbols { get { return Parser.Symbols; } }
    public Catalog Cat { get { return Parser.Cat; } }
    public PegParser Parser { get; set; }

    AccumCounter _accum = new AccumCounter();

    // eod of file
    public AstStatement Eof() {
      return new AstEof();
    }

    // null statement used for directives and blank lines
    public AstStatement Empty() {
      return new AstEmpty();
    }

    // A set of statements inside a do scope
    // May include Empty statements, which must be discarded
    public AstValue DoBlock(string name, IList<AstStatement> statements) {
      var stmts = statements.Where(s => !(s is AstEmpty)).ToArray();
      var datatype = (stmts.Length == 0) ? DataTypes.Void : stmts.Last().DataType;
      var block = new AstBlock { Statements = stmts, DataType = datatype };
      return new AstDoBlock {
        Func = FindFunc(name), DataType = datatype, Value = block,
      };
    }

    // just a set of statements (may be empty but not null)
    public AstBlock VarBlock(IList<AstStatement> statements) {
      return new AstVarBlock {
        Statements = statements.ToArray(),
        DataType = (statements.Count == 0) ? DataTypes.Void : statements.Last().DataType
      };
    }

    public AstTypedef DefBlock(IList<AstTypedef> defines) {
      return new AstTypedef { DataType = DataTypes.Void };
    }

    public AstTypedef UserType(string ident, AstField[] fields) {
      var ff = fields.Select(a => DataColumn.Create(a.Name, a.DataType)).ToArray();
      var ut = DataTypeUser.Get(ident, ff);
      Symbols.AddUserType(ident, ut);
      Symbols.AddCatalog(Symbols.FindIdent(ident));
      return new AstUserType { DataType = DataTypes.Void };
    }

    public AstTypedef SubType(string ident, AstType super) {
      var cols = new DataColumn[] { DataColumn.Create("super", super.DataType) };
      var ut = DataTypeUser.Get(ident, cols);
      Symbols.AddUserType(ident, ut);
      return new AstSubType { DataType = DataTypes.Void };
    }

    // name, type and source of a single VAR declaration, can be
    // a) VAR ident : type
    // b) VAR ident [: typewithheading] (source)
    public AstStatement VarDecl(string ident, AstType type, AstLiteral source) {
      var what = ident; // reserved for future use
      var isimport = (source != null);
      var datatype = (type == null) ? null
        : isimport ? Types.Relof(type.DataType)
        : type.DataType;
      if (isimport && datatype == null) {
        if (type != null) Parser.ParseError($"type with heading required for '{ident}'");
        datatype = Cat.GetRelvarType(source.ToString(), what); // peek the file
        if (datatype == null) Parser.ParseError("cannot find '{0}' as '{1}'", ident, source);
      }
      Symbols.AddVariable(ident, datatype, SymKinds.CATVAR, true);
      Symbols.AddCatalog(Symbols.FindIdent(ident));
      return (isimport) ? GenFunCall(FindFunc(SymNames.Import), Args(source, Text(ident), Text(what), Text(Parser.Cat.SourcePath)))
        : GenFunCall(FindFunc(SymNames.Assign), Args(Text(ident), Literal(datatype.DefaultValue())));
    }

    // finalise function definition, from basic definition previously created
    // set or check return type
    // BUG: a persistent function must not reference a non-persistent global
    public AstStatement Deferred(string ident, AstType rettype, IList<AstField> arguments, AstBodyStatement body) {
      var op = FindDefFunc(ident);
      if (op.DataType == DataTypes.Unknown) op.DataType = body.DataType;
      else if (op.DataType != body.DataType) Parser.ParseError($"{ident} return type mismatch");

      // assume top overload is the one being defined
      op.CallInfo.ReturnType = op.DataType;
      op.CallInfo.AccumCount = body.AccumCount;
      op.CallInfo.HasWin = body.HasWin;
      // symbol is foldable if all overloads are foldable
      var foldable = (op.NumArgs == 2 && arguments[0].DataType == op.DataType && arguments[1].DataType == op.DataType)
        && (op.CallInfo.OverLoad == null || op.IsFoldable);
      if (foldable && !op.IsFoldable) op.Foldable = FoldableFlags.ANY;
      Symbols.AddCatalog(op);

      // assemble node
      var code = Code(body.Statement as AstValue, Headingof(arguments), op.CallInfo.Name, true, body.AccumCount, body.HasWin);
      return GenFunCall(FindFunc(SymNames.Defer), Args(code));
    }

    public AstStatement Assignment(string ident, AstValue value, string varopt) {
      if (Symbols.CanDefGlobal(ident)) {
        Symbols.AddVariable(ident, value.DataType, SymKinds.CATVAR, varopt != null);
        Symbols.AddCatalog(Symbols.FindIdent(ident));
        //if (varopt != null) Symbols.AddCatalog(Symbols.FindIdent(ident));
      } else {
        var sym = FindCatVar(ident);
        if (sym == null || sym.IsCallable || !sym.Mutable) Parser.ParseError("cannot assign to '{0}'", sym.Name);
        if (sym.DataType != value.DataType) Parser.ParseError("type mismatch: '{0}'", sym.Name);
      }
      return GenFunCall(FindFunc(SymNames.Assign), Args(Text(ident), value));
    }

    // UPDATE rel <set op> relexpr
    public AstStatement UpdateSetop(AstValue relvar, string joinop, string joinop2, AstValue expr) {
      var joinsym = FindFunc(joinop);
      var joinnum = Number((int)joinsym.JoinOp);
      return FunCall(FindFunc(SymNames.UpdateJoin), DataTypes.Void, Args(relvar, expr, joinnum));
    }

    // UPDATE rel .where(pred) .{ sets }
    public AstStatement UpdateWhere(AstValue relvar, AstValue wherearg, AstTransformer tran) {
      var where = (wherearg != null) ? (wherearg as AstWhere).Arguments[0] : null;
      // null means delete
      if (tran == null)
        return FunCall(FindFunc(SymNames.UpdateTransform), DataTypes.Void, 
          Args(relvar, Args(where)), 0);
      if (tran.DataType.Heading != relvar.DataType.Heading) Parser.ParseError("field mismatch");
      return FunCall(FindFunc(SymNames.UpdateTransform), DataTypes.Void, 
        Args(relvar, Args(where, tran.Elements)), tran.Elements.Length);
    }

    // body statement in deferred might need an accumulator
    public AstBodyStatement BodyStatement(AstStatement value) {
      return new AstBodyStatement {
        DataType = value.DataType, Statement = value, AccumCount = _accum.Total
      };
    }

    public AstOpCall While(string name, AstValue expr) {
      var datatype = Types.Relof(CurrentHeading());
      if (expr.DataType != datatype) Parser.ParseError("type mismatch");
      return new AstOpCall() {
        Func = FindFunc(name),
        DataType = Types.Relof(CurrentHeading()),
        Arguments = Args(Number(0), expr),
      };
    }

    // Where is just a funcall with a code predicate (that may have a fold)
    public AstWhere Where(string name, AstValue predicate) {
      var lookup = DataHeading.Create(Symbols.CurrentScope.LookupItems.Items);
      var code = Code(predicate, lookup, "?", false, _accum.Total, _accum.HasWin);
      _accum = _accum.Push();
      return new AstWhere {
        Func = FindFunc(name),
        DataType = Types.Relof(CurrentHeading()),
        Arguments = Args(code),
      };
    }

    // create ordering node as a call
    public AstOrderer Orderer(IList<AstOrderField> argslist) {
      return new AstOrderer {
        DataType = Types.Relof(CurrentHeading()),
        Elements = argslist.ToArray(),
      };
    }

    // handle an Order rule as a call
    public AstOrderField OrderField(string name, bool desc, bool group) {
      return new AstOrderField {
        Name = name,
        DataType = FindField(name).DataType,
        Descending = desc,
        Grouped = group
      };
    }

    // Take a list of transforms, check allbut & lift, return transformer as simply a call
    // Reenter enclosing scope with different datatype
    public AstTransformer Transformer(bool allbut, IList<AstField> argslist) {
      var args = argslist.ToArray();
      var lift = args.Any(a => a is AstLift);
      if (lift) {
        if (allbut || args.Length != 1) Parser.ParseError("invalid lift");
        var dtlift = args[0].DataType;
        Reenter(dtlift);
        return new AstTransformer { Elements = args, Lift = true, DataType = dtlift };
      }
      if (allbut) args = Allbut(CurrentHeading(), args);
      // add project and rename to lookup
      //Symbols.CurrentScope.LookupItems.Add(args.Where(a => a is AstProject || a is AstRename)
      //  .Select(a => DataColumn.Create(a.Name, a.DataType)).ToArray());
      var datatype = Typeof(args);
      Reenter(datatype);
      return new AstTransformer { Elements = args, DataType = datatype };
    }

    // limited version used in constructing a row
    public AstTransformer Transformer(IList<AstField> argslist) {
      var args = argslist.ToArray();
      var datatype = Typeof(args);
      return new AstTransformer {
        Elements = args,
        DataType = datatype };
    }

    // handle generic transform rule and create specific node with all required info
    // each element tracks lookup items and folds
    public AstField Transfield(string name, string rename = null, AstValue value = null) {
      // Add symbols that were never referenced as variables
      if (rename != null) // Rename
        Symbols.CurrentScope.LookupItems.Add(DataColumn.Create(rename, FindField(rename).DataType));
      else if (value == null) // Project
        Symbols.CurrentScope.LookupItems.Add(DataColumn.Create(name, FindField(name).DataType));
      var lookup = DataHeading.Create(Symbols.CurrentScope.LookupItems.Items);

      //Symbols.CurrentScope.LookupItems.Clear();
      var accums = _accum.Count;
      var haswin = _accum.HasWin;
      _accum.Reset(false);
      if (name == null) return new AstLift {
        Name = "^", Value = value, DataType = value.DataType, Lookup = lookup, Accums = accums, HasWin = haswin,
      };
      if (rename != null) return new AstRename {
        Name = name, OldName = rename, DataType = FindField(rename).DataType
      };
      if (value != null) return new AstExtend {
        Name = name, DataType = value.DataType, Value = value, Lookup = lookup, Accums = accums, HasWin = haswin,
      };
      return new AstProject {
        Name = name, DataType = FindField(name).DataType
      };
    }

    ///--------------------------------------------------------------------------------------------
    /// Calls with arguments
    /// 

    // Handle a list of infix operators
    public AstValue Binop(AstValue value, IList<AstOpCall> ops) {
      var ret = Binop(value, ops.ToArray());
      return ret;
    }

    // Handle a list of postfix operators
    // construct a FunCall from the first OpCall (or first two), then invoke tail on result
    public AstValue PostFix(AstValue value, IList<AstCall> ops) {
      if (ops.Count == 0) return value;
      var newvalue = value;
      var tail = ops.Skip(1).ToList();
      // special handling for Transformer, possibly preceded by Orderer
      if (ops[0] is AstTransformer) {
        var tran = ops[0] as AstTransformer;
        if (value.DataType is DataTypeRelation) newvalue = PostFixTranRel(value, tran as AstTransformer, null);
        else if (value.DataType is DataTypeTuple) newvalue = PostFixTranTup(value, tran as AstTransformer);
        else Parser.ParseError("relation or tuple type expected");
        if (tran.Lift)
          newvalue = FunCall(FindFunc(SymNames.Lift), tran.DataType, Args(newvalue));
      } else if (ops[0] is AstOrderer) {
        if (!(value.DataType is DataTypeRelation)) Parser.ParseError("relation type expected");
        if (tail.Count > 0 && tail[0] is AstTransformer) {
          var tran = tail[0] as AstTransformer;
          newvalue = PostFixTranRel(value, tran as AstTransformer, ops[0] as AstOrderer);
          if (tran.Lift)
            newvalue = FunCall(FindFunc(SymNames.Lift), tran.DataType, Args(newvalue));
          tail = tail.Skip(1).ToList();
        } else newvalue = PostFixTranRel(value, null, ops[0] as AstOrderer);
      } else {
        var op = ops[0] as AstOpCall;
        newvalue = GenFunCall(op.Func, Args(value, op.Arguments));
      }
      return PostFix(newvalue, tail);
    }

    // Dot that is a function call
    public AstOpCall DotFunc(string name, IList<AstValue> args = null) {
      var op = FindFunc(name);
      return new AstOpCall() { Func = op, DataType = op.DataType, Arguments = (args == null) ? Args() : args.ToArray() };
    }

    public AstOpCall DotComponent(string name) {
      var op = FindComponent(name);
      return new AstOpCall() { Func = op, DataType = op.DataType, Arguments = Args() };
    }

    public AstOpCall DotField(string name) {
      var op = FindField(name);
      return new AstOpCall() { Func = op, DataType = op.DataType, Arguments = Args() };
    }

    public AstFunCall If(string name, AstValue condition, AstValue iftrue, AstValue iffalse) {
      Types.CheckTypeMatch(iftrue.DataType, iffalse.DataType);
      return FunCall(FindFunc(name), iftrue.DataType, Args(condition, Code(iftrue), Code(iffalse)));
    }

    public AstFoldCall Fold(string name, string oper, AstValue expression) {
      var op = FindFunc(oper);
      if (!op.IsFoldable) Parser.ParseError("not foldable");
      DataType datatype;
      CallInfo callinfo;
      Types.CheckTypeError(op, out datatype, out callinfo, expression.DataType, expression.DataType); // as if there were two
      var accndx = _accum.Total;
      _accum.Add(1);
      return new AstFoldCall {
        Func = FindFunc(name), FoldedOp = op, DataType = datatype,
        AccumIndex = accndx, FoldedExpr = expression, CallInfo = callinfo,
        InvokeOp = (op.IsDefFunc) ? Symbols.FindIdent(SymNames.Invoke) : null,
      };
    }

    public AstFunCall Function(string name, params AstValue[] args) {
      var op = FindFunc(name);
      return GenFunCall(op, args);
    }

    public AstFunCall Unop(string name, params AstValue[] args) {
      var op = FindFunc(name == "-" ? SymNames.UnaryMinus : name);
      return GenFunCall(op, args);
    }

    public AstOpCall OpCall(string name, params AstValue[] args) {
      var op = FindFunc(name);
      var type = op.DataType;
      return new AstOpCall() { Func = op, Arguments = args, DataType = type };
    }

    ///--------------------------------------------------------------------------------------------
    /// Tables and Rows
    /// 

    // {{*}} singleton
    public AstValue Table() {
      // add to lookup because Table has to pass lookup to Row
      foreach (var col in CurrentHeading().Columns)
        Symbols.CurrentScope.LookupItems.Add(col);
      //Logger.WriteLine(4, $"table1 {Symbols.CurrentScope}");
      var row = Row();
      //Logger.WriteLine(4, $"table2 {Symbols.CurrentScope}");
      return new AstTabCall {
        Func = FindFunc(SymNames.TableV), DataType = Types.Relof(row.DataType), Arguments = Args(row),
      };
    }

    // Table as set of rows
    public AstValue Table(IList<AstValue> rows) {
      if (rows.Count == 1) return Table(rows[0]);
      var rowtype = rows.Count > 0 ? rows[0].DataType : DataTypeTuple.Empty;
      if (rows.Any(r => r.DataType != rowtype)) Parser.ParseError("row type mismatch");
      return new AstTabCall {
        Func = FindFunc(SymNames.TableV), DataType = Types.Relof(rowtype), Arguments = rows.ToArray()
      };
    }

    // Table with separate heading
    public AstValue Table(AstType heading, IList<AstValue> rows) {
      if (!heading.DataType.HasHeading) Parser.ParseError("type has no heading");
      var rowtype = Types.Tupof(heading.DataType);
      foreach (var r in rows)
        r.DataType = rowtype;
      return new AstTabCall {
        Func = FindFunc(SymNames.TableV), DataType = Types.Relof(rowtype), Arguments = rows.ToArray()
      };
    }

    // Relation by conversion
    public AstValue Table(AstValue value) {
      if (!value.DataType.HasHeading) Parser.ParseError("value has no heading");
      return new AstTabCall {
        Func = FindFunc(SymNames.TableC), DataType = Types.Relof(value.DataType), Arguments = Args(value),
      };
    }

    // Current row {*} using single arg -- not easily implemented in SQL
    //public AstValue Row() {
    //  return new AstTabCall {
    //    Func = FindFunc(SymNames.RowR), DataType = Types.Tupof(CurrentHeading()), Arguments = Args(),
    //  };
    //}

    // Current row {*}
    public AstValue Row() {
      // slightly hokey way to get all exprs for current heading
      var trans = Transformer(true, new AstField[0]);
      // add to lookup or we won't know about them
      foreach (var col in trans.DataType.Heading.Columns)
        Symbols.CurrentScope.LookupItems.Add(col);
      return new AstTabCall {
        Func = FindFunc(SymNames.RowE), DataType = Types.Tupof(trans.DataType), Arguments = trans.Elements,
      };
    }

    // Row by Transform
    public AstValue Row(AstTransformer transformer) {
      return new AstTabCall {
        Func = FindFunc(SymNames.RowE), DataType = Types.Tupof(transformer.DataType), Arguments = transformer.Elements,
      };
    }

    // Row by list of values
    public AstValue Row(IList<AstValue> values) {
      return new AstTabCall {
        Func = FindFunc(SymNames.RowV), DataType = DataTypes.Unknown, Arguments = values.ToArray(),
      };
    }
    public AstValue Row(AstValue value) {
      if (!value.DataType.HasHeading) Parser.ParseError("value has no heading");
      return new AstTabCall {
        Func = FindFunc(SymNames.RowC), DataType = DataTypeTuple.Get(value.DataType.Heading), Arguments = Args(value),
      };
    }

    // value of row in table as list of paren values
    public AstValue TableExprRow(IList<AstValue> values) {
      return new AstTabCall {
        Func = FindFunc(SymNames.RowV), DataType = DataTypes.Unknown, Arguments = values.ToArray(),
      };
    }

    ///--------------------------------------------------------------------------------------------
    /// Headings etc
    /// 

    // Heading
    public AstType Heading(IList<AstField> fields) {
      var type = (fields == null) ? DataTypeRelation.Empty : Typeof(fields);
      return new AstType { DataType = type };
    }

    // A field name and optional type
    public AstField FieldTerm(string ident, AstType type) {
      return new AstField() {
        Name = ident,
        DataType = (type == null) ? DataTypes.Default : type.DataType
      };
    }

    ///--------------------------------------------------------------------------------------------
    ///--- internal functions
    ///
    AstValue Code(AstValue value, DataHeading lookup = null, string name = "?", 
      bool ascode = false, int accums = -1, bool hasord = false) {
      return new AstCode {
        Name = name, Value = value, DataType = DataTypes.Code, Accums = accums, Lookup = lookup, AsCode = ascode, HasWin = hasord,
      };
    }

    // Handle Binop with precedence
    AstValue Binop(AstValue value, AstOpCall[] ops) {
      Logger.WriteLine(4, "expr {0} op={1}", value, ops.Join(","));
      if (ops.Length == 0) return value;
      var op = ops[0];
      if (ops.Length == 1) return GenFunCall(op.Func, Args(value, op.Arguments));
      var opnext = ops[1];
      var optail = ops.Skip(1);
      //Func<AstCall, AstCall, bool> op high = (op1, op2) => !op1.Func.IsOperator || op1.Func.Precedence >= op2.Func.Precedence;
      if (!op.Func.IsOperator || op.Func.Precedence >= opnext.Func.Precedence)
        return Binop(GenFunCall(op.Func, Args(value, op.Arguments)), optail.ToArray());
      // The hard case -- rewrite the tree
      // extract higher precedence ops and do those first; then lower
      var hiprec = optail.TakeWhile(o => !o.Func.IsOperator || o.Func.Precedence > op.Func.Precedence);
      var hivalue = Binop(op.Arguments[0] as AstValue, hiprec.ToArray());
      var loprec = optail.SkipWhile(o => !o.Func.IsOperator || o.Func.Precedence > op.Func.Precedence);
      return Binop(GenFunCall(op.Func, Args(value, hivalue)), loprec.ToArray());
    }

    // translate transform call and possible order into a function call
    // note that tran may be empty, and this has meaning!
    AstValue PostFixTranRel(AstValue value, AstTransformer tran, AstOrderer order) {
      Types.CheckTypeMatch(DataTypes.Table, value.DataType);
      var args = new List<AstNode> { value };
      if (order != null) args.AddRange(order.Elements);
      if (tran != null) args.AddRange(tran.Elements);
      if (tran != null) {
        // transformation, possibly ordered
        // Decide which processor to use depending on level of elements
        var opname = tran.Elements.Any(e => e is AstExtend && (e as AstExtend).HasWin) ? SymNames.TransWin
          : (order != null) ? SymNames.TransOrd
          : tran.Elements.Any(e => e is AstExtend && (e as AstExtend).Accums > 0) ? SymNames.TransAgg
          : tran.Elements.Any(e => e is AstExtend) ? SymNames.Transform
          : tran.Elements.All(e => e is AstRename) && tran.Elements.Length == value.DataType.Heading.Degree ? SymNames.Rename
          : SymNames.Project;
        return FunCall(FindFunc(opname), tran.DataType, args.ToArray(), args.Count - 1);
      } else if (order != null) {
        // just a sort, convenient way to include all fields in heading
        args.AddRange(Allbut(value.DataType.Heading, new AstField[0]));
        return FunCall(FindFunc(SymNames.TransOrd), value.DataType, args.ToArray(), args.Count - 1);
      } else
        return value;  // nothing do to
    }

    // translate transform calls on tuple into a function call
    AstValue PostFixTranTup(AstValue value, AstTransformer tran) {
      Types.CheckTypeMatch(DataTypes.Row, value.DataType);
      if (tran == null) return value;
      var args = Args(value, tran.Elements);
      return FunCall(FindFunc(SymNames.TransTuple), tran.DataType, args, args.Length - 1);
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
    AstFunCall GenFunCall(Symbol op, AstNode[] args) {
      if (op.IsComponent) return Component(op, args);
      if (op.IsField) return FieldOf(op, args);
      DataType datatype;
      CallInfo callinfo;
      Types.CheckTypeError(op, out datatype, out callinfo, args.Select(a => a.DataType).ToArray());
      // deferred function
      if (op.IsDefFunc) return DefCall(op, datatype, args, callinfo);
      // WHERE
      if (op.IsRestrict) return FunCall(op, datatype, args, 1);
      //if (op.IsRestFunc) return FunCall(op, datatype, Args(args[0], Code(args[1] as AstValue, CurrentHeading())), 1);
      // Ordered
      if (op.IsWin) return FunCall(op, datatype, Args(Code(args[0] as AstValue, CurrentHeading()), args.Skip(1).ToArray()));
      // WHILE
      if (op.IsWhile) return FunCall(op, datatype, Args(args[0], args[1], Code(args[2] as AstValue, CurrentHeading())));
      // Skip/Take
      if (op.IsSkipTake) return FunCall(op, datatype, args);
      // user selector
      if (op.IsUserSel) return UserCall(op, args);
      return FunCall(op, datatype, args, 0, callinfo);
    }

    // Call user function via Invoke
    AstDefCall DefCall(Symbol op, DataType datatype, AstNode[] args, CallInfo callinfo) {
      var accbase = _accum.Total;
      _accum.Add(callinfo.AccumCount);
      _accum.HasWin |= callinfo.HasWin;
      return new AstDefCall {
        Func = FindFunc(SymNames.Invoke), DataType = datatype, Callinfo = callinfo, Arguments = args,
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
      _accum.HasWin |= op.IsWin;
      return new AstFunCall {
        Func = op, DataType = datatype, Arguments = args, NumVarArgs = nvarargs, CallInfo = callinfo
      };
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
      if (v.IsField) Symbols.CurrentScope.LookupItems.Add(v.AsColumn());
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
    public AstType TupType(AstType type) {
      if (!type.DataType.HasHeading) Parser.ParseError("value has no heading");
      return new AstType { DataType = DataTypeTuple.Get(type.DataType.Heading) };
    }
    public AstType RelType(AstType type) {
      if (!type.DataType.HasHeading) Parser.ParseError("value has no heading");
      return new AstType { DataType = DataTypeRelation.Get(type.DataType.Heading) };
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
      if (ret == null || !(reqtype(ret))) Parser.ParseError("unknown or invalid: {0}", name);
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
      // if the symbols if found, it's an overload; else check it can be defined
      var overfunc = Symbols.FindIdent(ident) != null && Symbols.FindIdent(ident).IsCallable 
        ? Symbols.FindIdent(ident) : null;
      if (overfunc == null && !Symbols.CanDefGlobal(ident)) Parser.ParseError("already defined: {0}", ident);

      // missing args means lazy; else check for dups
      if (arguments != null) {
        var dups = arguments.GroupBy(a => a.Name).Where(g => g.Count() > 1).Select(d => d.Key);
        if (dups.Count() > 0) Parser.ParseError($"duplicate parameter '{dups.First()}'");
      }
      var args = (arguments == null) ? null
        : arguments.Select(a => DataColumn.Create(a.Name, a.DataType)).ToArray();
      var rtype = (rettype == null) ? DataTypes.Unknown : rettype.DataType;

      // create new symbol or add an overload
      if (overfunc == null) Symbols.AddDeferred(ident, rtype, args);
      else {
        if (rtype == DataTypes.Unknown) rtype = overfunc.DataType;
        else if (rtype != overfunc.DataType) Parser.ParseError($"overload must be same return type: '{ident}'");
        if (args == null) Parser.ParseError($"overload not allowed: '{ident}'");
        if (!Symbols.AddOverload(overfunc, args)) Parser.ParseError($"overload argument type conflict: '{ident}'");
      }
      Symbols.CurrentScope.Push();
      if (args != null) foreach (var a in args)
        Symbols.AddVariable(a.Name, a.DataType, SymKinds.PARAM, false);
      _accum = _accum.Push();
      return true;
    }

    // Enter scope for a transform, with accumulator tracking
    public bool Enter(AstValue value) {
      Symbols.CurrentScope.Push(value.DataType);
      _accum = _accum.Push();
      return true;
    }

    // Continue scope but perhaps change heading, reset accumulators
    public bool Reenter(DataType datatype) {
      var dt = datatype ?? Types.Relof(CurrentHeading()); // HACK:
      Symbols.CurrentScope.Pop();
      Symbols.CurrentScope.Push(dt);
      _accum.Reset(true);
      return true;
    }

    // Enter scope for a do block -- no accumulators
    public bool Enter() {
      Symbols.CurrentScope.Push();
      return true;
    }

    public bool Exit(bool accums = false) {
      Symbols.CurrentScope.Pop();
      if (accums) _accum = _accum.Pop();
      return true;
    }

    DataHeading CurrentHeading() {
      return Symbols.CurrentScope.Heading ?? DataHeading.Empty;
    }
  }
}
