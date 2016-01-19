using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Andl.Workshop {
  public class TreeDataViewModel {

    //-- props
    public CatalogItem[] Catalogs {
      get { return _database.GetCatalogs().Select(c => new CatalogItem { Info = c }).ToArray(); }
    }
    public RelationItem[] Relations {
      get { return _catalog.GetRelations().Select(r => new RelationItem { Info = r }).ToArray(); }
    }
    public OperatorItem[] Operators {
      get { return _catalog.GetOperators().Select(r => new OperatorItem { Info = r }).ToArray(); }
    }
    public TypeItem[] Types {
      get { return _catalog.GetTypes().Select(r => new TypeItem { Info = r }).ToArray(); }
    }

    public string CatalogName {
      get { return _catalog.Name; }
      set { _catalog = _database.OpenCatalog(value); }
    }
    public CatalogConnector Catalog { get { return _catalog; } }

    //-- privates
    DatabaseConnector _database;
    CatalogConnector _catalog;

    //-- ctor
    public TreeDataViewModel(DatabaseConnector database) {
      _database = database;
      _catalog = _database.OpenCatalog();  // default/empty
    }
  }

  //-- simple data transfer objects for each object of interest

  public struct CatalogItem {
    public CatalogInfo Info;
    public string Name { get { return Info.Name; } }
  }

  public struct RelationItem {
    public RelationInfo Info;
    public string Name { get { return Info.Name; } }
    public int Count { get { return Info.Count; } }
    public AttributeItem[] Attributes {
      get { return Info.Attributes.Select(a => new AttributeItem { Info = a }).ToArray(); }
    }
    public string Display { get { return String.Format("{0} : ({1})", Name, Count); } }

    public bool IsExpanded { get; set; }
    public bool IsSelected { get; set; }
  }

  public struct OperatorItem {
    public OperatorInfo Info;
    public string Name { get { return Info.Name; } }
    public string Type { get { return Info.Type; } }
    public AttributeItem[] Arguments {
      get { return Info.Arguments.Select(a => new AttributeItem { Info = a }).ToArray(); }
    }
    public string Display { get { return String.Format("{0} : {1}", Name, Type); } }

    public bool IsExpanded { get; set; }
    public bool IsSelected { get; set; }
  }

  public struct TypeItem {
    public TypeInfo Info;
    public string Name { get { return Info.Name; } }
    public AttributeItem[] Components {
      get { return Info.Components.Select(a => new AttributeItem { Info = a }).ToArray(); }
    }
    public string Display { get { return String.Format("{0}", Name); } }

    public bool IsExpanded { get; set; }
    public bool IsSelected { get; set; }
  }

  public struct AttributeItem {
    public AttributeInfo Info;
    public string Name { get { return Info.Name; } }
    public string Type { get { return Info.Type; } }
    public string Display { get { return String.Format("{0} : {1}", Name, Type); } }

    public bool IsExpanded { get; set; }
    public bool IsSelected { get; set; }
  }

}
