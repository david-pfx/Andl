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
using Andl.Runtime;
using Andl.Common;

namespace Andl.Peg {
  /// <summary>
  /// Symbols required for lookup
  /// </summary>
  public class LookupItems {

    public DataColumn[] Items { get { return _lookupitems.ToArray(); } }
    public int Count { get { return Items.Length; } }
    public bool IsEmpty { get { return Items.Length == 0; } }

    HashSet<DataColumn> _lookupitems = new HashSet<DataColumn>();

    public void Clear() {
      _lookupitems.Clear();
    }

    // Add one or more lookup symbols
    public void Add(params DataColumn[] cols) {
      foreach (var col in cols)
        _lookupitems.Add(col);
    }
  }
  
  ///-------------------------------------------------------------------
  /// <summary>
  /// Scope implements one level of nested symbols
  /// </summary>
  public class Scope {
    public Dictionary<string, Symbol> Dict { get; private set; }
    // The tuple heading in force, set on push and not changed
    // Get from parent if needed
    public DataHeading Heading {
      get {
        return (_heading != null) ? _heading
          : (_parent != null) ? _parent.Heading
          : null;
      }
    }
    DataHeading _heading;

    // Used to track lookup of fields within scope
    // Should always be a subset of Heading
    public LookupItems LookupItems { get { return _lookupitems; } } 
    LookupItems _lookupitems = new LookupItems();

    // Owner table
    SymbolTable _owner;
    // Link to parent
    Scope _parent = null;

    public int Level { get { return _parent == null ? 0 : 1 + _parent.Level; } }

    // set this flag for symbols that should be pushed out to catalog
    public bool IsGlobal { get; set; }

    public override string ToString() {
      return $"Scope {Level} glob:{IsGlobal} syms:{Dict.Count} luit:{_lookupitems.Count} hdr:{_heading}";
    }
    public string AllToString() {
      return $"Scope {Level} glob:{IsGlobal} syms:{Dict.Count} luit:{_lookupitems.Count} hdr:{_heading} par:{_parent}";
    }

    // Create a new scope level
    public static Scope Create(SymbolTable owner) {
      var news = new Scope() {
        Dict = new Dictionary<string, Symbol>(),
        _lookupitems = new LookupItems(),
        _parent = null,
        _owner = owner,
      };
      owner.CurrentScope = news;
      Logger.WriteLine(4, $"Create {news}");
      return news;
    }

    public Scope Push() {
      var news = new Scope() {
        Dict = new Dictionary<string, Symbol>(),
        _lookupitems = new LookupItems(),
        _parent = this,
        _owner = this._owner,
      };
      _owner.CurrentScope = news;
      Logger.WriteLine(4, $"Push {news}");
      return news;
    }

    // Create a new tuple scope level
    public Scope Push(DataType datatype) {
      var scope = Push();
      if (datatype != null && datatype.HasHeading) scope.SetHeading(datatype);
      return scope;
    }

    // Create a new function scope level
    // Note that the function name itself lives outside this scope
    public Scope Push(Symbol[] argsyms) {
      var scope = Push();
      Logger.WriteLine(4, "Add func args {0}", String.Join(",", argsyms.Select(s => s.ToString()).ToArray()));
      foreach (var sym in argsyms)
        scope.Add(sym);
      return scope;
    }

    // Return to previous scope level
    // Propagate any uncleared lookup items back to parent
    public void Pop() {
      Logger.Assert(!IsGlobal, "pop");
      Logger.WriteLine(4, $"Pop {_owner.CurrentScope} => {_parent}");
      var keepitems = _lookupitems.Items.Where(i => _parent.FindAny(i)).ToArray();
      _owner.CurrentScope = _parent;
      _parent._lookupitems.Add(keepitems);
      Logger.Assert(_owner.CurrentScope != null, "pop");
    }

    // set current heading and add fields/components to symbol table
    void SetHeading(DataType datatype) {
      Logger.Assert(datatype != null);
      Logger.WriteLine(4, "Set heading {0}", datatype.Heading);
      _heading = datatype.Heading;
      foreach (var c in _heading.Columns) {
        Add(new Symbol {
          Kind = (datatype is DataTypeUser) ? SymKinds.COMPONENT : SymKinds.FIELD,
          DataType = c.DataType,
        }, c.Name);
      }
    }

    // Add a symbol -- all go through here
    public void Add(Symbol sym, string name = null) {
      if (name != null) sym.Name = name;
      sym.Level = this.Level;
      Logger.Assert(!Dict.ContainsKey(sym.Name), sym.Name);
      Dict.Add(sym.Name, sym);
    }

    // Find a symbol in this scope, or delegate to parent, or return null
    public Symbol FindAny(string name) {
      Symbol sym = Find(name);
      if (sym == null && _parent != null)
        return _parent.FindAny(name);
      else return sym;
    }

    // Find a symbol in this scope
    public Symbol Find(string name) {
      Symbol sym = null;
      if (Dict.TryGetValue(name, out sym))
        return sym;
      else return null;
    }

    bool FindAny(DataColumn item) {
      return (_heading != null && _heading.Contains(item)) ||
        (_parent != null && _parent.FindAny(item));
    }

  }
}
