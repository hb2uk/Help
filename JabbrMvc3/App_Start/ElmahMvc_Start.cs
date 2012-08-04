[assembly: WebActivator.PreApplicationStartMethod(typeof(Jabbr.App_Start.ElmahMvc), "Start")]
namespace Jabbr.App_Start
{
    public class ElmahMvc
    {
        public static void Start()
        {
            Elmah.Mvc.Bootstrap.Initialize();
        }
    }
}