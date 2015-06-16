using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Andl.API {
  /// <summary>
  /// Abstract class representing the catalog
  /// </summary>
  public abstract class Catalog {
    //public IEnumerable<CatalogEntry> GetEntries();
  }

  public enum EntryKind {
    Variable, Function, Type
  };

  public struct CatalogEntry {
    public string Name;
    public EntryKind Kind;
    public Type Type;

  }
}
