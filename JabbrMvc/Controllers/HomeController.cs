using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using SignalR;
using SignalR.Hubs;

using JabbR.Commands;
using JabbR.ContentProviders.Core;
using JabbR.Infrastructure;
using JabbR.Models;
using JabbR.Services;
using JabbR.ViewModels;
using Newtonsoft.Json;


namespace Jabbr.Controllers
{
    public class HomeController : Controller
    {

        //private readonly IJabbrRepository _repository;
        //private readonly IChatService _service;
        //private readonly ICache _cache;
        //private readonly IResourceProcessor _resourceProcessor;
        //private readonly IApplicationSettings _settings;


        //public HomeController(IApplicationSettings settings, IResourceProcessor resourceProcessor, IChatService service, IJabbrRepository repository, ICache cache)
        //{
        //    _settings = settings;
        //    _resourceProcessor = resourceProcessor;
        //    _service = service;
        //    _repository = repository;
        //    _cache = cache;
        //}

        public ActionResult Index()
        {

            ViewBag.Message = "Modify this template to kick-start your ASP.NET MVC application.";

            return View();
        }

        public ActionResult About()
        {
            ViewBag.Message = "Your app description page.";

            return View();
        }

        public ActionResult Contact()
        {
            ViewBag.Message = "Your contact page.";

            return View();
        }
    }
}
