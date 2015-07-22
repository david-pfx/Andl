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
    public IHttpActionResult Get(string catalog, string name) {
      var query = Request.GetQueryNameValuePairs().ToArray();
      return Common("get", catalog, name, null, query.Count() > 0 ? query : null);
    }

    // GET: rest/name/5
    public IHttpActionResult Get(string catalog, string name, string id) {
      return Common("get", catalog, name, id);
    }

    // POST: rest/name
    public async Task<IHttpActionResult> Post(string catalog, string name) {
      var body = await Request.Content.ReadAsStringAsync();
      return Common("post", catalog, name, null, null, body);
    }

    // PUT: rest/name/5
    public async Task<IHttpActionResult> Put(string catalog, string name, string id) {
      var body = await Request.Content.ReadAsStringAsync();
      return Common("put", catalog, name, id, null, body);
    }

    // DELETE: rest/name/5
    public IHttpActionResult Delete(string catalog, string name, string id) {
      return Common("delete", catalog, name, id);
    }

    // Common code for all requests
    IHttpActionResult Common(string method, string catalog, string name, string id, KeyValuePair<string, string>[] query = null, string jsonbody = null) {

      var gateway = WebApiApplication.GetGateway(catalog);
      var ret = (gateway == null) ? API.Result.Failure("catalog not found: " + catalog)
        : gateway.JsonCall(method, name, id, query, jsonbody);
      if (ret.Ok) return Ok(ret.Value);
      return BadRequest(ret.Message);
    }

  }
}
