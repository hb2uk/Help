using System.Web;
using System.Web.Optimization;

namespace JabbR
{
    public class BundleConfig
    {
        public static void RegisterBundles(BundleCollection bundles)
        {
            //bundles.Add(new ScriptBundle("~/bundles/jquery").Include(
            //            "~/Scripts/jquery-1.*"));

            //bundles.Add(new ScriptBundle("~/bundles/jqueryui").Include(
            //            "~/Scripts/jquery-ui*"));

            //bundles.Add(new ScriptBundle("~/bundles/jqueryval").Include(
            //            "~/Scripts/jquery.unobtrusive*",
            //            "~/Scripts/jquery.validate*"));

            bundles.Add(new ScriptBundle("~/bundles/jquery").Include(
                        "~/Scripts/jquery-1.7.js",
                        "~/Scripts/json2.min.js",
                        "~/Scripts/bootstrap.js",
                        "~/Scripts/jquery.KeyTips.js",
                        "~/Scripts/jquery-ui-1.8.17.min.js",
                        "~/Scripts/jquery.signalR-0.5.2.js",
                        "~/Chat.js",
                        "~/Chat.utility.js",
                        "~/Chat.emoji.js",
                        "~/Chat.toast.js",
                        "~/Chat.ui.js",
                        "~/Chat.documentOnWrite.js",
                        "~/Chat.twitter.js",
                        "~/Chat.pinnedWindows.js",
                        "~/Chat.githubissues.js"));

            bundles.Add(new ScriptBundle("~/bundles/jqueryval").Include(
            "~/Scripts/jQuery.tmpl.min.js",
                "~/Scripts/jquery.cookie.js",
                "~/Scripts/jquery.autotabcomplete.js",
                "~/Scripts/jquery.timeago.0.10.js",
                "~/Scripts/jquery.captureDocumentWrite.min.js",
                "~/Scripts/jquery.sortElements.js",
                "~/Scripts/quicksilver.js",
                "~/Scripts/jquery.livesearch.js",
                "~/Scripts/Markdown.Converter.js",
                "~/Scripts/jquery.history.js"));

            //bundles.Add(new ScriptBundle("~/bundles/tachat").Include(
            //"~/Chat.utility.js",
            //    "~/Chat.emoji.js",
            //    "~/Chat.toast.js",
            //    "~/Chat.ui.js",
            //    "~/Chat.documentOnWrite.js",
            //    "~/Chat.twitter.js",
            //    "~/Chat.pinnedWindows.js",
            //    "~/Chat.githubissues.js",
            //    "~/Chat.js"));
            
            bundles.Add(new ScriptBundle("~/bundles/modernizr").Include(
                        "~/Scripts/modernizr-*"));

            bundles.Add(new StyleBundle("~/Content/css").Include(
                
                        "~/Chat.nuget.css",
                        "~/Chat.bbcnews.css",
                        "~/Chat.githubissues.css",
                        "~/Chat.dictionary.css",
                        "~/Content/KeyTips.css",
                        "~/Content/bootstrap.min.css",
                        "~/Content/emoji20.css",
                        "~/Content/site.css"));

            bundles.Add(new StyleBundle("~/Content/themes/base/css").Include(
                        "~/Content/themes/base/jquery.ui.core.css",
                        "~/Content/themes/base/jquery.ui.resizable.css",
                        "~/Content/themes/base/jquery.ui.selectable.css",
                        "~/Content/themes/base/jquery.ui.accordion.css",
                        "~/Content/themes/base/jquery.ui.autocomplete.css",
                        "~/Content/themes/base/jquery.ui.button.css",
                        "~/Content/themes/base/jquery.ui.dialog.css",
                        "~/Content/themes/base/jquery.ui.slider.css",
                        "~/Content/themes/base/jquery.ui.tabs.css",
                        "~/Content/themes/base/jquery.ui.datepicker.css",
                        "~/Content/themes/base/jquery.ui.progressbar.css",
                        "~/Content/themes/base/jquery.ui.theme.css"));
        }
    }
}