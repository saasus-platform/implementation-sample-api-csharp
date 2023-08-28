using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Web;
using System.Web.Http;
using System.Web.Mvc;
using authapi.Api;
using authapi.Model;
using saasus_sdk_csharp.auth;

namespace SampleWebApplication.Controllers
{
    public class CallBackController : ApiController
    {
        // GET api/callback
        public IEnumerable<TenantDetail> Get()
        {

            string baseAuthURL = System.Configuration.ConfigurationManager.AppSettings["BaceAuthUrl"];
            string secretKey = System.Configuration.ConfigurationManager.AppSettings["SaasusSecretKey"];
            string apiKey = System.Configuration.ConfigurationManager.AppSettings["SaasusApiKey"];
            string saasIdKey = System.Configuration.ConfigurationManager.AppSettings["SaasusSaasId"];


            HttpRequestMessage httpRequest = Request;

            var pairs = httpRequest.GetQueryNameValuePairs();
            string codeValue = pairs.FirstOrDefault(p => p.Key == "code").Value;


            AuthConfiguration authConfig = new AuthConfiguration(secretKey, apiKey, saasIdKey, baseAuthURL);

            saasus_sdk_csharp.auth.CallBack callback = new saasus_sdk_csharp.auth.CallBack(authConfig);

            // get token
            string idToken = callback.GetIdToken(codeValue);

            // get user info
            authapi.Model.UserInfo userInfo = callback.GetUserInfo(idToken);

            // get tenant
            var tenantApi = new TenantApi(authConfig.GetAuthConfiguration());
            TenantDetail tenant = tenantApi.GetTenant(tenantId: userInfo.Tenants[0].Id);

            yield return tenant;
        }

        // GET api/values/5
        public string Get(int id)
        {
            return "value";
        }

        // POST api/values
        public void Post([FromBody] string value)
        {
        }

        // PUT api/values/5
        public void Put(int id, [FromBody] string value)
        {
        }

        // DELETE api/values/5
        public void Delete(int id)
        {
        }
    }
}
