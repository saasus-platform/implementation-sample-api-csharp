using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Http;
using System.Web.Mvc;
using System.Web.Optimization;
using System.Web.Routing;

namespace SampleWebAppDotNet48
{
    public class WebApiApplication : System.Web.HttpApplication
    {
        protected void Application_Start()
        {
            AreaRegistration.RegisterAllAreas();
            GlobalConfiguration.Configure(WebApiConfig.Register);
            FilterConfig.RegisterGlobalFilters(GlobalFilters.Filters);
            RouteConfig.RegisterRoutes(RouteTable.Routes);
            BundleConfig.RegisterBundles(BundleTable.Bundles);

            // ���ϐ��̐ݒ�
            SetEnvironmentVariables();
        }

        private void SetEnvironmentVariables()
        {
            string baseAuthURL = System.Configuration.ConfigurationManager.AppSettings["SAASUS_API_URL_BASE"];
            string secretKey = System.Configuration.ConfigurationManager.AppSettings["SAASUS_SECRET_KEY"];
            string apiKey = System.Configuration.ConfigurationManager.AppSettings["SAASUS_API_KEY"];
            string saasIdKey = System.Configuration.ConfigurationManager.AppSettings["SAASUS_SAAS_ID"];
 
            // SDK�Ŏg�p������ϐ���ݒ�
            Environment.SetEnvironmentVariable("SAASUS_API_URL_BASE", baseAuthURL);
            Environment.SetEnvironmentVariable("SAASUS_SECRET_KEY", secretKey);
            Environment.SetEnvironmentVariable("SAASUS_API_KEY", apiKey);
            Environment.SetEnvironmentVariable("SAASUS_SAAS_ID", saasIdKey);
        }

        protected void Application_BeginRequest(object sender, EventArgs e)
        {
            HttpContext.Current.Response.Headers.Add("Access-Control-Allow-Origin", "http://localhost:3000"); // �Œ�I���W��
            HttpContext.Current.Response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, PUT, DELETE, OPTIONS");
            HttpContext.Current.Response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Authorization, X-Requested-With");
            HttpContext.Current.Response.Headers.Add("Access-Control-Allow-Credentials", "true");

            // OPTIONS���N�G�X�g�ւ̑Ή��i�v���t���C�g���N�G�X�g�j
            if (HttpContext.Current.Request.HttpMethod == "OPTIONS")
            {
                HttpContext.Current.Response.StatusCode = 200;
                HttpContext.Current.Response.End();
            }
        }
    }
}
