[assembly: WebActivator.PreApplicationStartMethod(typeof(JabbR.App_Start.NinjectWebCommon), "Start")]
[assembly: WebActivator.ApplicationShutdownMethodAttribute(typeof(JabbR.App_Start.NinjectWebCommon), "Stop")]

namespace JabbR.App_Start
{
    using System;
    using System.Web;

    using Microsoft.Web.Infrastructure.DynamicModuleHelper;

    using System.Collections.Generic;
    using System.Configuration;
    using System.Data.Entity.Migrations;
    using System.Linq;
    using System.Net.Http.Formatting;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Web.Hosting;
    using System.Web.Http;
    using System.Web.Routing;
    using Elmah;
    using JabbR;
    using JabbR.Auth;
    using JabbR.ContentProviders.Core;
    using JabbR.Infrastructure;
    using JabbR.Models;
    using JabbR.Services;
    using JabbR.ViewModels;
    
    using Ninject;
    using Ninject.Web.Common;

    using SignalR;
    using SignalR.Hosting.Common;
    using SignalR.Hubs;
    using SignalR.Ninject;

    using RouteMagic;

    using Newtonsoft.Json;
    using Newtonsoft.Json.Serialization;

    
    public static class NinjectWebCommon 
    {
        private static readonly Bootstrapper bootstrapper = new Bootstrapper();

        private static bool _sweeping;
        private static Timer _timer;
        private static readonly TimeSpan _sweepInterval = TimeSpan.FromMinutes(10);
        internal static IKernel Kernel = null;
        private const string SqlClient = "System.Data.SqlClient";

        /// <summary>
        /// Starts the application
        /// </summary>
        public static void Start() 
        {
            DynamicModuleUtility.RegisterModule(typeof(OnePerRequestHttpModule));
            DynamicModuleUtility.RegisterModule(typeof(NinjectHttpModule));
            bootstrapper.Initialize(CreateKernel);
        }
        
        /// <summary>
        /// Stops the application.
        /// </summary>
        public static void Stop()
        {
            bootstrapper.ShutDown();
        }
        
        /// <summary>
        /// Creates the kernel that will manage your application.
        /// </summary>
        /// <returns>The created kernel.</returns>
        private static IKernel CreateKernel()
        {
            var kernel = new StandardKernel();
            kernel.Bind<Func<IKernel>>().ToMethod(ctx => () => new Bootstrapper().Kernel);
            kernel.Bind<IHttpModule>().To<HttpApplicationInitializationHttpModule>();
                        
            RegisterServices(kernel);

            return kernel;
        }

        /// <summary>
        /// Load your modules or register your services here!
        /// </summary>
        /// <param name="kernel">The kernel.</param>
        private static void RegisterServices(IKernel kernel)
        {

            kernel.Bind<JabbrContext>()
                .To<JabbrContext>()
                .InRequestScope();

            kernel.Bind<IJabbrRepository>()
                .To<PersistedRepository>()
                .InRequestScope();

            kernel.Bind<IChatService>()
                  .To<ChatService>()
                  .InRequestScope();

            kernel.Bind<ICryptoService>()
                .To<CryptoService>()
                .InSingletonScope();

            kernel.Bind<IResourceProcessor>()
                .To<ResourceProcessor>()
                .InSingletonScope();

            kernel.Bind<IApplicationSettings>()
                  .To<ApplicationSettings>()
                  .InSingletonScope();

            kernel.Bind<IVirtualPathUtility>()
                  .To<VirtualPathUtilityWrapper>();

            kernel.Bind<IJavaScriptMinifier>()
                  .To<AjaxMinMinifier>()
                  .InSingletonScope();

            kernel.Bind<ICache>()
                  .To<AspNetCache>()
                  .InSingletonScope();

            var serializer = new JsonNetSerializer(new JsonSerializerSettings
            {
                DateFormatHandling = DateFormatHandling.MicrosoftDateFormat
            });

            kernel.Bind<IJsonSerializer>()
                  .ToConstant(serializer);

            var resolver = new NinjectDependencyResolver(kernel);

            var host = new Host(resolver);
            host.Configuration.KeepAlive = TimeSpan.FromSeconds(30);

            RouteTable.Routes.MapHubs(resolver);

            var repositoryFactory = new Func<IJabbrRepository>(() => kernel.Get<IJabbrRepository>());
            _timer = new Timer(_ => Sweep(repositoryFactory, resolver), null, _sweepInterval, _sweepInterval);


            GlobalHost.DependencyResolver = new SignalR.Ninject.NinjectDependencyResolver(kernel);

            SetupErrorHandling();

            ClearConnectedClients(repositoryFactory());
            SetupRoutes(kernel);
            SetupWebApi(kernel);

        }

        private static void SetupWebApi(IKernel kernel)
        {
            GlobalConfiguration.Configuration.Formatters.Clear();
            JsonMediaTypeFormatter jsonFormatter = new JsonMediaTypeFormatter();
            jsonFormatter.SerializerSettings.ContractResolver = new CamelCasePropertyNamesContractResolver();
            GlobalConfiguration.Configuration.Formatters.Add(jsonFormatter);
            GlobalConfiguration.Configuration.DependencyResolver = new NinjectWebApiDependencyResolver(kernel);
        }

        private static void SetupRoutes(IKernel kernel)
        {
            RouteTable.Routes.MapHttpRoute(
                name: "MessagesV1",
                routeTemplate: "api/v1/{controller}/{room}"
            );

            RouteTable.Routes.MapHttpHandler<ProxyHandler>("proxy", "proxy/{*path}");

            RouteTable.Routes.MapHttpRoute(
                            name: "DefaultApi",
                            routeTemplate: "api",
                            defaults: new { controller = "ApiFrontPage" }
                        );

        }

        private static void ClearConnectedClients(IJabbrRepository repository)
        {
            try
            {
                repository.RemoveAllClients();
                repository.CommitChanges();
            }
            catch (Exception ex)
            {
                Elmah.ErrorLog.GetDefault(null).Log(new Error(ex));
            }
        }

        //private static void DoMigrations()
        //{
        //    // Get the Jabbr connection string
        //    var connectionString = ConfigurationManager.ConnectionStrings["Jabbr"];

        //    if (String.IsNullOrEmpty(connectionString.ProviderName) ||
        //        !connectionString.ProviderName.Equals(SqlClient, StringComparison.OrdinalIgnoreCase))
        //    {
        //        return;
        //    }

        //    // Only run migrations for SQL server (Sql ce not supported as yet)
        //    var settings = new JabbR.Models.Migrations.MigrationsConfiguration();
        //    var migrator = new DbMigrator(settings);
        //    migrator.Update();
        //}

        private static void SetupErrorHandling()
        {
            TaskScheduler.UnobservedTaskException += (sender, e) =>
            {
                try
                {
                    Elmah.ErrorLog.GetDefault(null).Log(new Error(e.Exception.GetBaseException()));
                }
                catch
                {
                    // Swallow!
                }
                finally
                {
                    e.SetObserved();
                }
            };
        }

        private static void Sweep(Func<IJabbrRepository> repositoryFactory, IDependencyResolver resolver)
        {
            if (_sweeping)
            {
                return;
            }

            _sweeping = true;

            try
            {
                using (IJabbrRepository repo = repositoryFactory())
                {
                    MarkInactiveUsers(repo, resolver);

                    repo.CommitChanges();
                }
            }
            catch (Exception ex)
            {
                Elmah.ErrorLog.GetDefault(null).Log(new Error(ex));
            }
            finally
            {
                _sweeping = false;
            }
        }

        private static void MarkInactiveUsers(IJabbrRepository repo, IDependencyResolver resolver)
        {
            var connectionManager = resolver.Resolve<IConnectionManager>();
            var clients = connectionManager.GetHubContext<Chat>().Clients;
            var inactiveUsers = new List<ChatUser>();

            IQueryable<ChatUser> users = repo.Users.Online();

            foreach (var user in users)
            {
                var status = (UserStatus)user.Status;
                var elapsed = DateTime.UtcNow - user.LastActivity;

                if (elapsed.TotalMinutes > 15)
                {
                    user.Status = (int)UserStatus.Inactive;
                    inactiveUsers.Add(user);
                }
            }

            if (inactiveUsers.Count > 0)
            {
                var roomGroups = from u in inactiveUsers
                                 from r in u.Rooms
                                 select new { User = u, Room = r } into tuple
                                 group tuple by tuple.Room into g
                                 select new
                                 {
                                     Room = g.Key,
                                     Users = g.Select(t => new UserViewModel(t.User))
                                 };

                foreach (var roomGroup in roomGroups)
                {
                    clients[roomGroup.Room.Name].markInactive(roomGroup.Users).Wait();
                }
            }
        }

    }

}
