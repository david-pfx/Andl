using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Andl.Peg {
  public class Field {
    public string Name;
    public DataType Type;
  }

  /// <summary>
  /// Simple type system for parser
  /// </summary>
  public class TypeSystem {
    public Dictionary<string, DataType> Members {get; set; }
    public TypeSystem() {
      Members = new Dictionary<string, DataType>();
      Add("bool");
      Add("binary");
      Add("number");
      Add("text");
      Add("time");
      Add("tuple");
      Add("relation");
      Add("user");
      Add("code");

    }
    public void Add(string name) {
      Members.Add(name, new DataType { Name = name });
    }

    public DataType Find(string name) {
      if (Members.ContainsKey(name)) return Members[name];
      return null;
    }

    internal DataType Find(IEnumerable<Field> typelist) {
      return Members["tuple"];
    }
    internal DataType Tupof(DataType type) {
      return Members["tuple"];
    }
    internal DataType Relof(DataType type) {
      return Members["relation"];
    }
  }
  ///
  /// An individual type - just the name for now
  /// 
  public class DataType {
    public string Name { get; set; }
  }
}
