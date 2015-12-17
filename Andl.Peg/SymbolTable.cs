using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Andl.Peg {
  /// <summary>
  /// Simple type system for parser
  /// </summary>
  public class SymbolTable {
    public Dictionary<string, Symbol> Members {get; set; }
    public SymbolTable() {
      Members = new Dictionary<string, Symbol>();

    }
    public void Add(string name, DataType datatype) {
      Members.Add(name, new Symbol { Name = name, DataType = datatype });
    }
    public void AddOperator(string name, DataType datatype, DataType returntype) {
      Members.Add(name, new OpSymbol { Name = name, DataType = datatype, ReturnType = returntype }); 
    }

    public Symbol Find(string name) {
      if (Members.ContainsKey(name)) return Members[name];
      //Console.WriteLine("Symbol not found: {0}", name);
      return null;
    }
  }
  ///
  /// An individual symbol
  /// 
  public class Symbol {
    public string Name { get; set; }
    public DataType DataType { get; set; }
  }
  public class OpSymbol : Symbol {
    public DataType ReturnType { get; set; }
  }
}
