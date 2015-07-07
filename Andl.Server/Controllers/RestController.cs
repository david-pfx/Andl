using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Web.Http;
using Andl.API;
using Newtonsoft.Json;
using System.Threading.Tasks;

namespace Andl.Server.Controllers {
  public class RestController : ApiController {
    // GET: rest/name
    public IHttpActionResult Get(string name) {
      return Common("findall_" + name);
    }

    // GET: rest/name/5
    public IHttpActionResult Get(string name, string id) {
      return Common("find_" + name, id);
    }

    // POST: rest/name
    public async Task <IHttpActionResult> Post(string name) {
      string body = await Request.Content.ReadAsStringAsync();
      return Common("create_" + name, null, body);
    }

    // PUT: rest/name/5
    public async Task <IHttpActionResult> Put(string name, string id) {
      string body = await Request.Content.ReadAsStringAsync();
      return Common("update_" + name, id, body);
    }

    // DELETE: rest/name/5
    public IHttpActionResult Delete(string name, string id) {
      return Common("delete_" + name, id);
    }

    IHttpActionResult Common(string name, string id = null, string body = null) {
      object value = null;
      if (body != null) {
        Type[] types = Runtime.Gateway.GetArgumentTypes(name);
        if (types == null || types.Length == 0) return BadRequest("unknown name");
        value = JsonConvert.DeserializeObject(body, types[types.Length - 1]);
        if (value == null) return BadRequest("bad argument");
      }
      var args = new List<object>();
      if (id != null)
        args.Add(id);
      if (value != null)
        args.Add(value);
      var ret = Runtime.Gateway.Evaluate(name, args.ToArray());
      if (ret.Ok) return Ok(ret.Value);
      return BadRequest(ret.Message);
    }


  }
}
