using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Andl.API {
  public abstract class Value { }
  public abstract class Result { }

  public abstract class Gateway {

    public abstract Value GetValue(string name);
    public abstract void SetValue(string name, Value value);
    public abstract Value Evaluate(string name, params Value[] arguments);
    public abstract Result Command(string name, params Value[] arguments);
  }
}
