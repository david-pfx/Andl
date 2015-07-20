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
    Gateway Runtime { get { return WebApiApplication.Runtime; } }

    // GET: rest/name
    public IHttpActionResult Get(string name) {
      var query = Request.GetQueryNameValuePairs().ToArray();
      if (query.Count() == 0) return Common("findall_" + name, null);
      return Common("findsome_" + name, null, query);
    }

    // GET: rest/name/5
    public IHttpActionResult Get(string name, string id) {
      return Common("find_" + name, id);
    }

    // POST: rest/name
    public async Task <IHttpActionResult> Post(string name) {
      var body = await Request.Content.ReadAsStringAsync();
      return Common("create_" + name, null, null, body);
    }

    // PUT: rest/name/5
    public async Task <IHttpActionResult> Put(string name, string id) {
      var body = await Request.Content.ReadAsStringAsync();
      return Common("update_" + name, id, null, body);
    }

    // DELETE: rest/name/5
    public IHttpActionResult Delete(string name, string id) {
      return Common("delete_" + name, id);
    }

    // Common code for all requests
    IHttpActionResult Common(string name, string id, KeyValuePair<string, string>[] query = null, string jsonbody = null) {
      var ret = Runtime.JsonCall(name, id, query, jsonbody);
      if (ret.Ok) return Ok(ret.Value);
      return BadRequest(ret.Message);
    }

  }
}
