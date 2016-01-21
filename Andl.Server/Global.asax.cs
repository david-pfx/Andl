using Andl.Gateway;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web;
using System.Web.Http;
using System.Web.Mvc;
using System.Web.Optimization;
using System.Web.Routing;

namespace Andl.Server {
  public class WebApiApplication : System.Web.HttpApplication {
    protected void Application_Start() {
      AreaRegistration.RegisterAllAreas(); 
      GlobalConfiguration.Configure(WebApiConfig.Register);
      FilterConfig.RegisterGlobalFilters(GlobalFilters.Filters);
      RouteConfig.RegisterRoutes(RouteTable.Routes);
      BundleConfig.RegisterBundles(BundleTable.Bundles);

      //Andl.API.Runtime.StartUp();
      AppStartup();
    }

    void AppStartup() {
      var appsettings = ConfigurationManager.AppSettings;
      var settings = appsettings.AllKeys
        .ToDictionary(k => k, v => appsettings[v]);
      GatewayManager.AddGateways(settings);
    }

    // Access the required database
    public static GatewayBase GetGateway(string database) {
      return GatewayManager.GetGateway(database);
    }

    //enum SettingOptions { Ignore, Andl, Other }

    //Dictionary<string, SettingOptions> _settingsdict = new Dictionary<string,SettingOptions> {
    //  { "Noisy", SettingOptions.Andl },
    //};

    //// Access the required database
    //public static Gateway GetGateway(string database = null) {
    //  return _gateway[database];
    //}
    //static Dictionary<string, Gateway> _gateway = new Dictionary<string,Gateway>();

    //void AppStartup() {
    //  var appsettings = ConfigurationManager.AppSettings;
    //  var settings = appsettings.AllKeys
    //    .Where(k => _settingsdict.ContainsKey(k) && _settingsdict[k] == SettingOptions.Andl)
    //    .ToDictionary(k => k, v => appsettings[v]);
    //  foreach (var key in appsettings.AllKeys) {
    //    if (Regex.IsMatch(key, "^Database.*$")) {
    //      var values = appsettings[key].Split(',');
    //      var settingsx = new Dictionary<string, string>(settings);
    //      settingsx.Add("DatabaseName", values[0]);
    //      if (values.Length >= 2) settingsx.Add("DatabaseSqlFlag", values[1]);
    //      if (values.Length >= 3) settingsx.Add("DatabasePath", values[2]);
    //      _gateway[values[0]] = Andl.API.Gateway.StartUp(settingsx);
    //    }
    //  }
    //  //_gateway = Andl.API.Gateway.StartUp(settings);
    //}
  }
}
