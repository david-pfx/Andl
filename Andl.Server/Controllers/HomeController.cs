using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;

namespace Andl.Server.Controllers {
  /// <summary>
  /// Implement web page controller for sample application.
  /// 
  /// This is the only controller for loading web pages for all the samples. Incoming
  /// API calls are routed separately to an ApiController.
  /// 
  /// Note: each action is named after its CSHTML page, so each action returns a default view.
  /// </summary>
  public class HomeController : Controller {
    public ActionResult Index() {
      ViewBag.Title = "Home Page";

      return View();
    }

    public ActionResult AppSpRest() {
      ViewBag.Title = "Supplier Parts REST Sample";

      return View();
    }

    public ActionResult AppSpApi() {
      ViewBag.Title = "Supplier Parts API Sample";

      return View();
    }

    public ActionResult AppEmpRest() {
      ViewBag.Title = "Employee Info REST Sample";

      return View();
    }

    //TODO: implement Web API for this
    public ActionResult AppRepl() {
      ViewBag.Title = "Supplier Info REPL Sample";

      return View();
    }
  }
}
