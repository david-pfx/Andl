using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Andl.Workshop {
  /// <summary>
  /// Implements a connection to an Andl database
  /// </summary>
  public class DatabaseConnector {
    static public DatabaseConnector Create() {
      return new DatabaseConnector();
    }

    public CatalogInfo[] GetCatalogs() {
      return GetTestCatalogInfo();
    }

    public CatalogConnector OpenCatalog(string name = "data") {
      return new CatalogConnector {
        Database = this,
        Name = name,
      };
    }

    CatalogInfo[] GetTestCatalogInfo() {
      return new CatalogInfo[] {
        new CatalogInfo {
          Name = "data",
        },
        new CatalogInfo {
          Name = "second",
        },
        new CatalogInfo {
          Name = "third",
        },
      };
    }

  }

  /// <summary>
  /// Implements a connection to an Andl Catalog within a known database
  /// </summary>
  public class CatalogConnector {
    public DatabaseConnector Database { get; set; }
    public string Name { get; set; }

    public RelationInfo[] GetRelations() {
      return GetTestRelationInfo();
    }

    public OperatorInfo[] GetOperators() {
      return GetTestOperatorInfo();
    }

    public TypeInfo[] GetTypes() {
      return new TypeInfo[0];
    }

    public string Execute(string command) {
      return String.Format("Results of {0} executing '{1}' go here.", Name, command);
    }

    RelationInfo[] GetTestRelationInfo() {
      return new RelationInfo[] {
        new RelationInfo {
          Name = "testrel",
          Count = 99,
          Attributes = new AttributeInfo[] {
            new AttributeInfo {
              Name="testatt1",
              Type="text",
            },
            new AttributeInfo {
              Name="testatt2",
              Type="number",
            },
          },
        },
        new RelationInfo {
          Name = "testrel2",
          Count = 98,
          Attributes = new AttributeInfo[] { },
        },
      };
    }

    private OperatorInfo[] GetTestOperatorInfo() {
      return new OperatorInfo[] {
        new OperatorInfo {
          Name = "testop",
          Type = "time",
          Arguments = new AttributeInfo[] {
            new AttributeInfo {
              Name="testarg1",
              Type="text",
            },
            new AttributeInfo {
              Name="testarg2",
              Type="number",
            },
          },
        },
        new OperatorInfo {
          Name = "testop2",
          Type = "time",
          Arguments = new AttributeInfo[] { },
        },
      };
    }

  }

  public struct CatalogInfo {
    public string Name;
  }

  public struct RelationInfo {
    public string Name;
    public int Count;
    public AttributeInfo[] Attributes;
  }

  public struct AttributeInfo {
    public string Name;
    public string Type;
  }

  public struct OperatorInfo {
    public string Name;
    public string Type;
    public AttributeInfo[] Arguments;
  }

  public struct TypeInfo {
    public string Name;
    public AttributeInfo[] Components;
  }

}
