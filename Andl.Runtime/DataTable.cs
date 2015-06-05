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

namespace Andl.Runtime {
  // Check for mixed local and sql dyadics 
  public enum MixedDyadics { None, LeftLocal, RightLocal };

  abstract public class DataTable {
    // Data type, fully derived
    public DataTypeRelation DataType { get { return DataTypeRelation.Get(Heading); } }
    // Explicit heading, in same order as row values
    public DataHeading Heading { get; protected set; }
    // Return the number of columns.
    public int Degree { get { return Heading.Degree; } }

    public abstract string Format();
    public abstract bool IsLocal { get; }
    public abstract IEnumerable<DataRow> GetRows();
    public virtual void DropRows() { }
    public abstract int GetCount();
    public abstract bool IsEqual(DataTable other);
    public abstract bool Subset(DataTable other);
    public abstract bool Superset(DataTable other);
    public abstract bool Separate(DataTable other);
    public abstract TypedValue Lift();
    public abstract DataTable Project(ExpressionBlock[] exprs);
    public abstract DataTable Rename(ExpressionBlock[] exprs);
    public abstract DataTable Restrict(ExpressionBlock expr);
    public abstract DataTable Transform(DataHeading newheading, ExpressionBlock[] exprs);
    public abstract DataTable TransformAggregate(DataHeading newheading, ExpressionBlock[] exprs);
    public abstract DataTable TransformOrdered(DataHeading newheading, ExpressionBlock[] exprs, ExpressionBlock[] orderexps);
    public abstract DataTable DyadicJoin(DataTable other, JoinOps joinops, DataHeading newheading);
    public abstract DataTable DyadicAntijoin(DataTable other, JoinOps joinops, DataHeading newheading);
    public abstract DataTable DyadicSet(DataTable other, JoinOps joinops, DataHeading newheading);
    public abstract DataTable UpJoin(DataTable other, JoinOps joinops);
    public abstract DataTable UpdateTransform(ExpressionBlock pred, ExpressionBlock[] exprs);
    public abstract DataTable ConvertWrap(DataTable other);

    // default empty value
    internal static DataTable Empty {
      get { return DataTable.Create(DataHeading.Empty); }
    }

    public static DataTable Create(DataHeading heading) {
      return DataTableLocal.Create(heading);
    }

    public static DataTable Create(DataHeading heading, IEnumerable<ExpressionBlock> exprs) {
      return DataTableLocal.Create(heading, exprs);
    }

    public static MixedDyadics CheckDyadic(DataTable rel1, DataTable rel2) {
      if (rel1.IsLocal  && !rel2.IsLocal) return MixedDyadics.LeftLocal;
      if (!rel1.IsLocal && rel2.IsLocal) return MixedDyadics.RightLocal;
      return MixedDyadics.None;
    }

    // Check for mixed local and sql dyadics and return left arg according to preferred usage
    public static DataTable ResolveDyadic(DataTable rel1, DataTable rel2) {
      // Current rule says resolve all mixed dyadics by converting to local
      // This could be a configurable option
      if (CheckDyadic(rel1, rel2) == MixedDyadics.RightLocal) return rel2.ConvertWrap(rel1);
      else return rel1;
    }

  }
}
