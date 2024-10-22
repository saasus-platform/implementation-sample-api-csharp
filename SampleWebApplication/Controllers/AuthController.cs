using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Web.Http;
using authapi.Api;
using authapi.Model;
using saasus_sdk_csharp.auth;
using authapi.Client;

namespace SampleWebApplication.Controllers
{
    public class AuthController : ApiController
    {
        // 認証関連の設定情報を読み込む
        private string baseAuthURL = System.Configuration.ConfigurationManager.AppSettings["BaceAuthUrl"];
        private string secretKey = System.Configuration.ConfigurationManager.AppSettings["SaasusSecretKey"];
        private string apiKey = System.Configuration.ConfigurationManager.AppSettings["SaasusApiKey"];
        private string saasIdKey = System.Configuration.ConfigurationManager.AppSettings["SaasusSaasId"];

        // Authorization ヘッダーから Bearer トークンを取得
        private string GetBearerToken(HttpRequestMessage request)
        {
            var authHeader = request.Headers.Authorization?.ToString();
            if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer "))
            {
                return authHeader.Substring("Bearer ".Length).Trim();
            }
            return null;
        }

        private void SetRefererHeader(HttpRequestMessage request, AuthConfiguration authConfig)
        {
            var referer = request.Headers.Referrer?.ToString();
            if (!string.IsNullOrEmpty(referer))
            {
                authConfig.GetAuthConfiguration().DefaultHeaders.Add("Referer", referer);
            }
        }

        // GET /credentials
        // クレデンシャルを取得
        [HttpGet]
        [Route("credentials")]
        public IHttpActionResult GetCredentials()
        {
            // リクエストからクエリパラメータ "code" を取得
            HttpRequestMessage httpRequest = Request;
            var pairs = httpRequest.GetQueryNameValuePairs();
            string codeValue = pairs.FirstOrDefault(p => p.Key == "code").Value;

            if (string.IsNullOrEmpty(codeValue))
            {
                return BadRequest("The 'code' query parameter is missing.");
            }

            try
            {
                // 認証設定の作成
                AuthConfiguration authConfig = new AuthConfiguration(secretKey, apiKey, saasIdKey, baseAuthURL);

                // Refererヘッダーを設定
                SetRefererHeader(Request, authConfig);

                // CredentialApiのインスタンスを作成
                var credentialApi = new CredentialApi(authConfig.GetAuthConfiguration());

                // クレデンシャルを取得
                var credentials = credentialApi.GetAuthCredentials(codeValue, "tempCodeAuth", null);

                return Ok(credentials);
            }
            catch (ApiException apiEx)
            {
                // SaaSus APIエラー時のエラーハンドリング
                Debug.WriteLine($"API Error: {apiEx.ErrorCode} - {apiEx.Message}");
                return StatusCode((HttpStatusCode)apiEx.ErrorCode);
            }
            catch (Exception ex)
            {
                // その他のエラーハンドリング
                return InternalServerError(ex);
            }
        }

        // GET /refresh
        // リフレッシュトークンからクレデンシャルを取得
        [HttpGet]
        [Route("refresh")]
        public IHttpActionResult RefreshToken()
        {
            // リクエストからSaaSusRefreshTokenクッキーを取得
            var refreshTokenCookie = Request.Headers.GetCookies("SaaSusRefreshToken").FirstOrDefault();
            if (refreshTokenCookie == null)
            {
                return BadRequest("No refresh token found.");
            }

            try
            {
                // 認証設定の作成
                AuthConfiguration authConfig = new AuthConfiguration(secretKey, apiKey, saasIdKey, baseAuthURL);

                // Refererヘッダーを設定
                SetRefererHeader(Request, authConfig);

                // CredentialApiのインスタンスを作成
                var credentialApi = new CredentialApi(authConfig.GetAuthConfiguration());

                // クレデンシャルを取得 (リフレッシュトークンを使って)
                var credentials = credentialApi.GetAuthCredentials(null, "refreshTokenAuth", refreshTokenCookie["SaaSusRefreshToken"].Value);

                return Ok(credentials);
            }
            catch (ApiException apiEx)
            {
                // SaaSus APIエラー時のエラーハンドリング
                Debug.WriteLine($"API Error: {apiEx.ErrorCode} - {apiEx.Message}");
                return StatusCode((HttpStatusCode)apiEx.ErrorCode);
            }
            catch (Exception ex)
            {
                // その他のエラーハンドリング
                return InternalServerError(ex);
            }
        }

        // GET /userinfo
        // ユーザー情報を取得
        [HttpGet]
        [Route("userinfo")]
        public IHttpActionResult GetUserInfo()
        {
            var token = GetBearerToken(Request);
            if (string.IsNullOrEmpty(token))
            {
                return Unauthorized();
            }

            try
            {
                // 認証設定の作成
                AuthConfiguration authConfig = new AuthConfiguration(secretKey, apiKey, saasIdKey, baseAuthURL);

                // Refererヘッダーを設定
                SetRefererHeader(Request, authConfig);

                // UserInfoApiを使用してユーザー情報を取得
                var userInfoApi = new UserInfoApi(authConfig.GetAuthConfiguration());
                authapi.Model.UserInfo userInfo = userInfoApi.GetUserInfo(token);

                return Ok(userInfo);
            }
            catch (ApiException apiEx)
            {
                // SaaSus APIエラー時のエラーハンドリング
                Debug.WriteLine($"API Error: {apiEx.ErrorCode} - {apiEx.Message}");
                return StatusCode((HttpStatusCode)apiEx.ErrorCode);
            }
            catch (Exception ex)
            {
                // その他のエラーハンドリング
                return InternalServerError(ex);
            }
        }

        // GET /users
        // 登録されたユーザーの一覧を取得
        [HttpGet]
        [Route("users")]
        public IHttpActionResult GetUsers()
        {
            var token = GetBearerToken(Request);
            if (string.IsNullOrEmpty(token))
            {
                return Unauthorized();
            }

            try
            {
                // 認証設定の作成
                AuthConfiguration authConfig = new AuthConfiguration(secretKey, apiKey, saasIdKey, baseAuthURL);

                // Refererヘッダーを設定
                SetRefererHeader(Request, authConfig);

                // UserInfoApiを使用してユーザー情報を取得
                var userInfoApi = new UserInfoApi(authConfig.GetAuthConfiguration());
                authapi.Model.UserInfo userInfo = userInfoApi.GetUserInfo(token);

                // テナントIDをTenantsリストから取得
                if (userInfo.Tenants == null || !userInfo.Tenants.Any())
                {
                    return BadRequest("No tenant information available.");
                }

                string tenantId = userInfo.Tenants[0].Id;

                // テナント情報を取得
                var tenantUserApi = new TenantUserApi(authConfig.GetAuthConfiguration());
                Users users = tenantUserApi.GetTenantUsers(tenantId);

                return Ok(users.VarUsers);
            }
            catch (ApiException apiEx)
            {
                // SaaSus APIエラー時のエラーハンドリング
                Debug.WriteLine($"API Error: {apiEx.ErrorCode} - {apiEx.Message}");
                return StatusCode((HttpStatusCode)apiEx.ErrorCode);
            }
            catch (Exception ex)
            {
                // その他のエラーハンドリング
                return InternalServerError(ex);
            }
        }
    }
}
