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
  public class KeyValue {
    public string Key;
    public string Value;
  }

  public class RestController : ApiController {
    // GET: rest/name
    public IHttpActionResult Get(string name) {
      var query = Request.GetQueryNameValuePairs();
      if (query.Count() == 0) return Common("findall_" + name);
      var args = query.Select(kvp => new KeyValue { Key = kvp.Key, Value = kvp.Value }).ToList();
      return Common("findsome_" + name, args as object);
    }

    // GET: rest/name/5
    public IHttpActionResult Get(string name, string id) {
      return Common("find_" + name, id);
    }

    // POST: rest/name
    public async Task <IHttpActionResult> Post(string name) {
      var reqname = "create_" + name;
      object body;
      if (!ParseBody(reqname, await Request.Content.ReadAsStringAsync(), out body))
        return BadRequest(body as string);
      return Common(reqname, body);
    }

    // PUT: rest/name/5
    public async Task <IHttpActionResult> Put(string name, string id) {
      var reqname = "update_" + name;
      object body;
      if (!ParseBody(reqname, await Request.Content.ReadAsStringAsync(), out body))
        return BadRequest(body as string);
      return Common(reqname, id, body);
    }

    // DELETE: rest/name/5
    public IHttpActionResult Delete(string name, string id) {
      return Common("delete_" + name, id);
    }

    IHttpActionResult Common(string name, params object[] args) {
      var ret = Runtime.Gateway.Evaluate(name, args);
      if (ret.Ok) return Ok(ret.Value);
      return BadRequest(ret.Message);
    }

    bool ParseBody(string name, string body, out object value) {
      Type[] types = Runtime.Gateway.GetArgumentTypes(name);
      if (types == null || types.Length == 0) value = "unknown name";
      else try {
        value = JsonConvert.DeserializeObject(body, types[types.Length - 1]);
        return true;
      } catch {
        value = "bad argument";
      }
      return false;
    }
  }
}
