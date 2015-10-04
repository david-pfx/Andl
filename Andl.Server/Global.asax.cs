using Andl.API;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
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

    enum SettingOptions { Ignore, Andl, Other }

    Dictionary<string, SettingOptions> _settingsdict = new Dictionary<string,SettingOptions> {
      { "DatabasePath", SettingOptions.Andl },
      { "DatabasePathSqlFlag", SettingOptions.Andl },
      { "DatabaseName", SettingOptions.Andl },
      { "Noisy", SettingOptions.Andl },
    };

    // Access the required catalog
    // TODO: support non-default catalog
    public static Gateway GetGateway(string catalog = null) {
      return (catalog == null || catalog == _gateway.DatabaseName) ? _gateway : null;
    }
    static Gateway _gateway;

    void AppStartup() {
      var appsettings = ConfigurationManager.AppSettings;
      var settings = appsettings.AllKeys
        .Where(k => _settingsdict.ContainsKey(k) && _settingsdict[k] == SettingOptions.Andl)
        .ToDictionary(k => k, v => appsettings[v]);
      _gateway = Andl.API.Gateway.StartUp(settings);
    }
  }
}
