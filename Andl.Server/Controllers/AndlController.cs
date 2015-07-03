using System.Collections.Generic;
using System.Web.Http;
using System.Web.Http.Description;
using System.Linq;
using Andl.API;
using System.Threading.Tasks;
using System;
using System.Runtime.Serialization.Json;
using Newtonsoft.Json;

namespace Andl.Server.Controllers {
  public class AndlController : ApiController {
    // GET api/<catalog>/<name>

    public async Task<IHttpActionResult> GetValue(string catalog, string name) {
      string body = await Request.Content.ReadAsStringAsync();
      var ret = Runtime.Gateway.GetValue(name);
      if (ret.Ok) return Ok(ret.Value);
      return BadRequest(ret.Message);
    }

    public async Task<IHttpActionResult> PostEvaluate(string catalog, string name) {
      string body = await Request.Content.ReadAsStringAsync();
      Type[] types = Runtime.Gateway.GetArgumentTypes(name);
      if (types == null) return BadRequest("unknown name");
      var values = types.Select(t => JsonConvert.DeserializeObject(body, t)).ToArray();
      var ret = Runtime.Gateway.Evaluate(name, values);
      if (ret.Ok) return Ok(ret.Value);
      return BadRequest(ret.Message);
    }

    public async Task<IHttpActionResult> PutValue(string catalog, string name) {
      string body = await Request.Content.ReadAsStringAsync();
      Type type = Runtime.Gateway.GetSetterType(name);
      if (type == null) return BadRequest("unknown name");
      var value = JsonConvert.DeserializeObject(body, type);
      var ret = Runtime.Gateway.Evaluate(name, value);
      if (ret.Ok) return Ok(ret.Value);
      return BadRequest(ret.Message);
    }

    //public IHttpActionResult GetValueOrFunction(string catalog, string name, [FromBody]string value) {
    //  if (name == "products") return Ok(Product.Data2);
    //  string body = Request.Content.ReadAsStringAsync();
    //  var ret = Runtime.Gateway.GetValue(name);
    //  if (ret.Ok) return Ok(ret.Value);
    //  return BadRequest(ret.Message);
    //}
    
  }

  public class Supplier {
    public string Sid { get; set; }
    public string SNAME { get; set; }
    public decimal STATUS { get; set; }
    public string CITY { get; set; }

    public static Supplier[] Data = new Supplier[] {
      new Supplier { Sid = "S1", SNAME = "Smith", STATUS = 20, CITY = "London"},
      new Supplier { Sid = "S2", SNAME = "Smith", STATUS = 20, CITY = "London"},
      new Supplier { Sid = "S3", SNAME = "Smith", STATUS = 20, CITY = "London"},
      new Supplier { Sid = "S4", SNAME = "Smith", STATUS = 20, CITY = "London"},
    };
    public static List<Supplier> Data2 = new List<Supplier> {
      new Supplier { Sid = "S1", SNAME = "Smith", STATUS = 20, CITY = "London"},
      new Supplier { Sid = "S2", SNAME = "Smith", STATUS = 20, CITY = "London"},
      new Supplier { Sid = "S3", SNAME = "Smith", STATUS = 20, CITY = "London"},
      new Supplier { Sid = "S4", SNAME = "Smith", STATUS = 20, CITY = "London"},
    };
  }

  public class Product {
    public int Id { get; set; }
    public string Name { get; set; }
    public string Category { get; set; }
    public decimal Price { get; set; }

    public static Product[] Data = new Product[] { 
        new Product { Id = 1, Name = "Tomato Soup", Category = "Groceries", Price = 1 }, 
        new Product { Id = 2, Name = "Yo-yo", Category = "Toys", Price = 3.75M }, 
        new Product { Id = 3, Name = "Hammer", Category = "Hardware", Price = 16.99M },
    };
    public static List<Product> Data2 = new List<Product> {
        new Product { Id = 1, Name = "-Tomato Soup", Category = "Groceries", Price = 1 }, 
        new Product { Id = 2, Name = "-Yo-yo", Category = "Toys", Price = 3.75M }, 
        new Product { Id = 3, Name = "-Hammer", Category = "Hardware", Price = 16.99M },
    };
  }


  public class OperationResult {
    public bool Success { get; set; }
    public string Message { get; set; }
  }
}
