using System.Collections.Generic;
using System.Web.Http;
using System.Web.Http.Description;
using Andl.API;

namespace Andl.Server.Controllers {
  public class AndlController : ApiController {
    // GET api/<catalog>/<name>
    public IHttpActionResult GetValueOrFunction(string catalog, string name, [FromBody]string value) {
      //return "Value for: [" + catalog + "]/[" + name + "]";
      if (name == "products") return Ok(Product.Data2);
      var ret = Runtime.Gateway.GetValue(name);
      if (ret.Ok) return Ok(ret.Value);
      return BadRequest(ret.Message);
    }
    
    // PUT api/<catalog>/<name>
    [ResponseType(typeof(OperationResult))]
    public IHttpActionResult PutValueOrCommand(string catalog, string name, [FromBody]string value) {
      if (string.IsNullOrEmpty(value))
        return BadRequest("value not set");

      if (name == "test_fail")
        return Ok(new OperationResult { Success = false, Message = "Operation failed" });
      return Ok(new OperationResult { Success = true });
    }

  }

  public class Supplier {
    public string Sid { get; set; }
    public string Sname { get; set; }
    public decimal Status { get; set; }
    public string City { get; set; }

    public static Supplier[] Data = new Supplier[] {
      new Supplier { Sid = "S1", Sname = "Smith", Status = 20, City = "London"},
      new Supplier { Sid = "S2", Sname = "Smith", Status = 20, City = "London"},
      new Supplier { Sid = "S3", Sname = "Smith", Status = 20, City = "London"},
      new Supplier { Sid = "S4", Sname = "Smith", Status = 20, City = "London"},
    };
    public static List<Supplier> Data2 = new List<Supplier> {
      new Supplier { Sid = "S1", Sname = "Smith", Status = 20, City = "London"},
      new Supplier { Sid = "S2", Sname = "Smith", Status = 20, City = "London"},
      new Supplier { Sid = "S3", Sname = "Smith", Status = 20, City = "London"},
      new Supplier { Sid = "S4", Sname = "Smith", Status = 20, City = "London"},
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
