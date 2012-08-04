[assembly: WebActivator.PreApplicationStartMethod(typeof(Jabbr.App_Start.NinjectWebCommon), "Start")]
[assembly: WebActivator.ApplicationShutdownMethodAttribute(typeof(Jabbr.App_Start.NinjectWebCommon), "Stop")]

namespace Jabbr.App_Start
{
    using System;
    using System.Web;

    using Microsoft.Web.Infrastructure.DynamicModuleHelper;

    using Ninject;
    using Ninject.Web.Common;

    using SignalR;
    using SignalR.Hosting.Common;
    using SignalR.Hubs;
    using SignalR.Ninject;

    using System.Web.Routing;

    using Newtonsoft.Json;
    using Newtonsoft.Json.Serialization;

    using JabbR.Auth;
    using JabbR.ContentProviders.Core;
    using JabbR.Infrastructure;
    using JabbR.Models;
    using JabbR.Services;
    using JabbR.ViewModels;

    public static class NinjectWebCommon 
    {
        private static readonly Bootstrapper bootstrapper = new Bootstrapper();

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

        }        
    }
}
