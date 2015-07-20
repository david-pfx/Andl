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

    Dictionary<string, string> _settingsdict = new Dictionary<string, string> {
      { "DatabasePath", "DatabasePath" },
      { "DatabasePathSqlFlag", "DatabaseSqlFlag" },
      { "CatalogName", "CatalogName" },
      { "Noisy", "Noisy" },
    };

    public static Andl.API.Gateway Runtime;

    void AppStartup() {
      var appsettings = ConfigurationManager.AppSettings;
      var settings = appsettings.AllKeys
        .Where(k => _settingsdict.ContainsKey(k))
        .ToDictionary(k => _settingsdict[k], k => appsettings[k]);
      Runtime = Andl.API.Gateway.StartUp(settings);
    }
  }
}
