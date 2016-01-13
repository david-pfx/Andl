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
using System.Text;
using System.Threading.Tasks;
using Andl.Runtime;

namespace Andl.Peg {
  /// <summary>
  /// Symbols required for lookup
  /// </summary>
  public class LookupItems {

    public DataColumn[] Items { get { return _lookupitems.ToArray(); } }
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

    // Used to track lookup within scope
    public LookupItems LookupItems { get { return _lookupitems; } } 
    LookupItems _lookupitems = new LookupItems();

    // Link to parent
    Scope _parent = null;

    public int Level { get { return _parent == null ? 0 : 1 + _parent.Level; } }

    public static Scope Current { get { return _current; } }
    static Scope _current = null;

    // set this flag for symbols that should be pushed out to catalog
    public bool IsGlobal { get; set; }

    // Create a new scope level
    public static Scope Push() {
      _current = new Scope() {
        Dict = new Dictionary<string, Symbol>(),
        _heading = null,
        _parent = _current,
      };
      Logger.WriteLine(4, "Push scope {0}", _current.Level);
      return _current;
    }

    // Reset scope variables but keep symbols
    public void Reset() {
      _lookupitems = new LookupItems();
    }

    // Create a new scope level, possible empty
    // Note that null heading means look at parent
    public static Scope Push(DataType datatype = null) {
      var scope = Push();
      if (datatype != null && datatype.HasHeading) scope.SetHeading(datatype);
      return scope;
    }

    // Create a new function scope level
    // Note that the function name itself lives outside this scope
    public static Scope Push(Symbol[] argsyms) {
      var scope = Push();
      Logger.WriteLine(4, "Add func args {0}", String.Join(",", argsyms.Select(s => s.ToString()).ToArray()));
      foreach (var sym in argsyms)
        scope.Add(sym);
      return scope;
    }

    // Return to previous scope level
    public static Scope Pop() {
      Logger.WriteLine(4, "Pop scope {0}", _current.Level);
      _current = _current._parent;
      Logger.Assert(_current != null);
      return _current;
    }

    void SetHeading(DataType datatype) {
      Logger.Assert(datatype != null);
      _heading = datatype.Heading;
      Logger.WriteLine(4, "Set heading {0}", _heading);
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

    // Add a symbol -- all go through here
    public void Update(Symbol sym) {
      Dict[sym.Name] = sym;
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

  }
}
