using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Web;
using System.Web.Http;
using SampleWebAppDotNet48.Helpers;

using authapi.Api;
using authapi.Model;
using pricingapi.Api;
using modules;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Data.Entity;
using System.ComponentModel.DataAnnotations.Schema;


namespace SampleWebAppDotNet48.Controllers
{
    public class MainController : ApiController
    {
        private readonly ApplicationDbContext _dbContext;

        public MainController()
        {
            _dbContext = new ApplicationDbContext(); // DbContext のインスタンス化
        }

        // GET /credentials
        [HttpGet]
        [Route("credentials")]
        public async Task<IHttpActionResult> GetCredentials(string code)
        {
            if (string.IsNullOrEmpty(code))
                return BadRequest("The 'code' query parameter is missing.");

            try
            {
                var authApiClientConfig = SaasusApiHelpers.CreateClientConfiguration(c => c.GetAuthApiClientConfig(), Request);
                var credentialApi = new CredentialApi(authApiClientConfig);
                var credentials = await credentialApi.GetAuthCredentialsAsync(code, "tempCodeAuth", null);

                return Ok(credentials);
            }
            catch (Exception ex)
            {
                var (status, body) = SaasusApiHelpers.HandleApiException(ex);
                return Content(status, body);
            }
        }

        // GET /refresh
        [HttpGet]
        [Route("refresh")]
        public async Task<IHttpActionResult> Refresh()
        {
            try
            {
                var refreshTokenCookie = Request.Headers.GetCookies("SaaSusRefreshToken").FirstOrDefault();
                if (refreshTokenCookie == null)
                {
                    return BadRequest("No refresh token found.");
                }

                var authApiClientConfig = SaasusApiHelpers.CreateClientConfiguration(c => c.GetAuthApiClientConfig(), Request);
                var credentialApi = new CredentialApi(authApiClientConfig);
                var credentials = await credentialApi.GetAuthCredentialsAsync(null, "refreshTokenAuth", refreshTokenCookie["SaaSusRefreshToken"].Value);

                return Ok(credentials);
            }
            catch (Exception ex)
            {
                var (status, body) = SaasusApiHelpers.HandleApiException(ex);
                return Content(status, body);
            }
        }

        // GET /userinfo
        [HttpGet]
        [Route("userinfo")]
        public async Task<IHttpActionResult> GetUserInfo()
        {
            try
            {
                var token = SaasusApiHelpers.GetBearerToken(Request);
                var authApiClientConfig = SaasusApiHelpers.CreateClientConfiguration(c => c.GetAuthApiClientConfig(), Request);
                var userInfoApi = new UserInfoApi(authApiClientConfig);
                var userInfo = await userInfoApi.GetUserInfoAsync(token);

                return Ok(userInfo);
            }
            catch (Exception ex)
            {
                var (status, body) = SaasusApiHelpers.HandleApiException(ex);
                return Content(status, body);
            }
        }

        // GET /users
        [HttpGet]
        [Route("users")]
        public async Task<IHttpActionResult> GetUsers(string tenant_id)
        {
            try
            {
                if (string.IsNullOrEmpty(tenant_id))
                {
                    Console.Error.WriteLine("tenant_id query parameter is missing");
                    return BadRequest("The 'tenant_id' query parameter is required.");
                }

                var token = SaasusApiHelpers.GetBearerToken(Request);
                var authApiClientConfig = SaasusApiHelpers.CreateClientConfiguration(c => c.GetAuthApiClientConfig(), Request);
                var userInfoApi = new UserInfoApi(authApiClientConfig);
                var userInfo = await userInfoApi.GetUserInfoAsync(token);

                if (userInfo.Tenants == null || !userInfo.Tenants.Any())
                {
                    return BadRequest("No tenant information available.");
                }

                // ユーザーが所属しているテナントか確認
                var isBelongingTenant = userInfo.Tenants.Any(t => t.Id == tenant_id);
                if (!isBelongingTenant)
                {
                    Console.Error.WriteLine($"Tenant {tenant_id} does not belong to user");
                    return Content(HttpStatusCode.Forbidden, "Tenant does not belong to the user.");
                }

                // テナントユーザーを取得
                var tenantUserApi = new TenantUserApi(authApiClientConfig);
                var users = await tenantUserApi.GetTenantUsersAsync(tenant_id);
                if (users == null || users.VarUsers == null)
                {
                    Console.Error.WriteLine("Failed to get SaaS users: response is null");
                    return InternalServerError(new Exception("Failed to retrieve users."));
                }

                return Ok(users.VarUsers);
            }
            catch (Exception ex)
            {
                var (status, body) = SaasusApiHelpers.HandleApiException(ex);
                return Content(status, body);
            }
        }

        // GET /tenant_attributes
        [HttpGet]
        [Route("tenant_attributes")]
        public async Task<IHttpActionResult> GetTenantAttributes(string tenant_id)
        {
            try
            {
                if (string.IsNullOrEmpty(tenant_id))
                {
                    Console.Error.WriteLine("tenant_id query parameter is missing");
                    return BadRequest("The 'tenant_id' query parameter is required.");
                }
                var token = SaasusApiHelpers.GetBearerToken(Request);
                var authApiClientConfig = SaasusApiHelpers.CreateClientConfiguration(c => c.GetAuthApiClientConfig(), Request);
                var userInfoApi = new UserInfoApi(authApiClientConfig);
                var userInfo = await userInfoApi.GetUserInfoAsync(token);
                if (userInfo.Tenants == null || !userInfo.Tenants.Any())
                {
                    return BadRequest("No tenant information available.");
                }

                // ユーザーが所属しているテナントか確認
                var isBelongingTenant = userInfo.Tenants.Any(t => t.Id == tenant_id);
                if (!isBelongingTenant)
                {
                    Console.Error.WriteLine($"Tenant {tenant_id} does not belong to user");
                    return Content(HttpStatusCode.Forbidden, "Tenant does not belong to the user.");
                }

                // テナント属性の取得
                var tenantAttributeApi = new TenantAttributeApi(authApiClientConfig);
                var tenantAttributes = await tenantAttributeApi.GetTenantAttributesAsync();

                // テナント情報の取得
                var tenantApi = new TenantApi(authApiClientConfig);
                var tenant = await tenantApi.GetTenantAsync(tenant_id);

                // 結果を格納する辞書
                var result = new Dictionary<string, Dictionary<string, object>>();

                foreach (var tenantAttribute in tenantAttributes.VarTenantAttributes)
                {
                    var attributeName = tenantAttribute.AttributeName;

                    // 詳細情報の辞書を作成
                    var detail = new Dictionary<string, object>
    {
        { "display_name", tenantAttribute.DisplayName ?? string.Empty },
        { "attribute_type", tenantAttribute.AttributeType.ToString() },
        { "value", tenant.Attributes.ContainsKey(attributeName) ? tenant.Attributes[attributeName] : null }
    };

                    // 結果辞書に追加
                    result[attributeName] = detail;
                }
                return Ok(result);
            }
            catch (Exception ex)
            {
                var (status, body) = SaasusApiHelpers.HandleApiException(ex);
                return Content(status, body);
            }
        }

        // GET /user_attributes
        [HttpGet]
        [Route("user_attributes")]
        public async Task<IHttpActionResult> GetUserAttributes()
        {
            try
            {
                var token = SaasusApiHelpers.GetBearerToken(Request);
                var authApiClientConfig = SaasusApiHelpers.CreateClientConfiguration(c => c.GetAuthApiClientConfig(), Request);
                var userAttributeApi = new UserAttributeApi(authApiClientConfig);
                var userAttributes = await userAttributeApi.GetUserAttributesAsync();

                return Ok(userAttributes);
            }
            catch (Exception ex)
            {
                var (status, body) = SaasusApiHelpers.HandleApiException(ex);
                return Content(status, body);
            }
        }

        // GET /pricing_plan
        [HttpGet]
        [Route("pricing_plan")]
        public async Task<IHttpActionResult> GetPricingPlan(string plan_id)
        {
            try
            {
                if (string.IsNullOrEmpty(plan_id))
                {
                    Console.Error.WriteLine("plan_id query parameter is missing");
                    return BadRequest("The 'plan_id' query parameter is required.");
                }

                // Bearerトークンを取得
                var token = SaasusApiHelpers.GetBearerToken(Request);

                if (string.IsNullOrEmpty(plan_id))
                {
                    return BadRequest("No price plan found for the tenant");
                }

                var pricingConfig = SaasusApiHelpers.CreateClientConfiguration(c => c.GetPricingApiClientConfig(), Request);
                var pricingPlansApi = new PricingPlansApi(pricingConfig);
                var plan = await pricingPlansApi.GetPricingPlanAsync(plan_id);

                return Ok(plan);
            }
            catch (Exception ex)
            {
                var (status, body) = SaasusApiHelpers.HandleApiException(ex);
                return Content(status, body);
            }
        }

        // POST /user_register
        [HttpPost]
        [Route("user_register")]
        public async Task<IHttpActionResult> RegisterUser([FromBody] UserRegisterRequest request)
        {
            // バリデーションのチェック
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            string email = request.Email;
            string password = request.Password;
            string tenantId = request.TenantId;
            var userAttributeValues = request.UserAttributeValues ?? new Dictionary<string, object>();

            try
            {
                // Bearerトークンを取得
                var token = SaasusApiHelpers.GetBearerToken(Request);
                // ユーザー情報の取得
                var authApiClientConfig = SaasusApiHelpers.CreateClientConfiguration(c => c.GetAuthApiClientConfig(), Request);
                var userInfoApi = new UserInfoApi(authApiClientConfig);
                var userInfo = await userInfoApi.GetUserInfoAsync(token);
                if (userInfo.Tenants == null || !userInfo.Tenants.Any())
                {
                    return BadRequest("No tenant information available.");
                }

                // ユーザーが所属しているテナントか確認
                var isBelongingTenant = userInfo.Tenants.Any(t => t.Id == tenantId);
                if (!isBelongingTenant)
                {
                    Console.Error.WriteLine($"Tenant {tenantId} does not belong to user");
                    return Content(HttpStatusCode.Forbidden, "Tenant does not belong to the user.");
                }


                // ユーザー属性の取得
                var userAttributeApi = new UserAttributeApi(authApiClientConfig);
                var userAttributes = await userAttributeApi.GetUserAttributesAsync();

                // 属性の型に応じた処理
                foreach (var attribute in userAttributes.VarUserAttributes)
                {
                    var attributeName = attribute.AttributeName;
                    var attributeType = attribute.AttributeType.ToString();

                    if (userAttributeValues.ContainsKey(attributeName) && attributeType.ToLower() == "number")
                    {
                        if (int.TryParse(userAttributeValues[attributeName]?.ToString(), out int numericValue))
                        {
                            userAttributeValues[attributeName] = numericValue;
                        }
                        else
                        {
                            return BadRequest("Invalid attribute value");
                        }
                    }
                }

                // SaaS ユーザーの登録
                var saasUserApi = new SaasUserApi(authApiClientConfig);
                var createSaasUserParam = new CreateSaasUserParam(email, password);
                await saasUserApi.CreateSaasUserAsync(createSaasUserParam);

                // テナントユーザーの登録
                var tenantUserApi = new TenantUserApi(authApiClientConfig);
                var createTenantUserParam = new CreateTenantUserParam(email, userAttributeValues);
                var tenantUser = await tenantUserApi.CreateTenantUserAsync(tenantId, createTenantUserParam);

                // ロールの取得
                var roleApi = new RoleApi(authApiClientConfig);
                var roles = await roleApi.GetRolesAsync();

                // ロールの設定
                string addRole = "admin";
                if (roles.VarRoles.Any(r => r.RoleName == "user"))
                {
                    addRole = "user";
                }

                var createTenantUserRolesParam = new CreateTenantUserRolesParam(new List<string> { addRole });
                await tenantUserApi.CreateTenantUserRolesAsync(tenantId, tenantUser.Id, 3, createTenantUserRolesParam);

                return Ok(new { message = "User registered successfully", request });
            }
            catch (Exception ex)
            {
                var (status, body) = SaasusApiHelpers.HandleApiException(ex);
                return Content(status, body);
            }
        }

        [HttpDelete]
        [Route("user_delete")]
        public async Task<IHttpActionResult> DeleteUser([FromBody] UserDeleteRequest request)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            string tenantId = request.TenantId;
            string userId = request.UserId;
            try
            {
                // Bearerトークンを取得
                var token = SaasusApiHelpers.GetBearerToken(Request);
                // ユーザー情報の取得
                var authApiClientConfig = SaasusApiHelpers.CreateClientConfiguration(c => c.GetAuthApiClientConfig(), Request);
                var userInfoApi = new UserInfoApi(authApiClientConfig);
                var userInfo = userInfoApi.GetUserInfo(token);
                if (userInfo.Tenants == null || !userInfo.Tenants.Any())
                {
                    return BadRequest("No tenant information available.");
                }

                // ユーザーが所属しているテナントか確認
                var isBelongingTenant = userInfo.Tenants.Any(t => t.Id == tenantId);
                if (!isBelongingTenant)
                {
                    Console.Error.WriteLine($"Tenant {tenantId} does not belong to user");
                    return Content(HttpStatusCode.Forbidden, "Tenant does not belong to the user.");
                }

                // ユーザー削除処理
                // SaaSusからユーザー情報を取得
                var tenantUserApi = new TenantUserApi(authApiClientConfig);
                var deleteUser = tenantUserApi.GetTenantUser(tenantId, userId);
                await tenantUserApi.DeleteTenantUserAsync(tenantId, userId);


                // 新しい削除ログを作成
                var deleteLog = new DeleteUserLog
                {
                    TenantId = tenantId,
                    UserId = userId,
                    Email = deleteUser.Email,
                    DeleteAt = DateTime.Now
                };

                _dbContext.DeleteUserLogs.Add(deleteLog);


                // 保存を実行
                await _dbContext.SaveChangesAsync();


                return Ok(new { message = "User deleted successfully", request });
            }
            catch (Exception ex)
            {
                var (status, body) = SaasusApiHelpers.HandleApiException(ex);
                return Content(status, body);
            }
        }

        [HttpGet]
        [Route("delete_user_log")]
        public async Task<IHttpActionResult> GetDeleteUserLogs(string tenant_id)
        {
            // クエリパラメータのバリデーション
            if (string.IsNullOrEmpty(tenant_id))
            {
                return BadRequest("The 'tenant_id' query parameter is required.");
            }

            try
            {
                // ユーザー情報を取得 (トークンバリデーションなどを追加可能)
                // Bearerトークンを取得
                var token = SaasusApiHelpers.GetBearerToken(Request);
                // ユーザー情報の取得
                var authApiClientConfig = SaasusApiHelpers.CreateClientConfiguration(c => c.GetAuthApiClientConfig(), Request);
                var userInfoApi = new UserInfoApi(authApiClientConfig);
                var userInfo = userInfoApi.GetUserInfo(token);
                if (userInfo.Tenants == null || !userInfo.Tenants.Any())
                {
                    return BadRequest("No tenant information available.");
                }

                // ユーザーが指定されたテナントに属しているか確認
                var isBelongingTenant = userInfo.Tenants.Any(t => t.Id == tenant_id);
                if (!isBelongingTenant)
                {
                    return Content(HttpStatusCode.Forbidden, "Tenant that does not belong.");
                }

                // データベースから削除ログを取得
                var deleteUserLogs = await _dbContext.DeleteUserLogs
                    .Where(log => log.TenantId == tenant_id)
                    .ToListAsync();

                // レスポンスデータの整形
                var responseData = deleteUserLogs.Select(log => new
                {
                    id = log.Id,
                    tenant_id = log.TenantId,
                    user_id = log.UserId,
                    email = log.Email,
                    delete_at = log.DeleteAt.ToString("o") // ISO 8601 形式に変換
                });

                return Ok(responseData);
            }
            catch (Exception ex)
            {
                var (status, body) = SaasusApiHelpers.HandleApiException(ex);
                return Content(status, body);
            }
        }

        // GET /tenant_attributes_list
        [HttpGet]
        [Route("tenant_attributes_list")]
        public async Task<IHttpActionResult> GetTenantAttributes()
        {
            try
            {
                var token = SaasusApiHelpers.GetBearerToken(Request);
                var authApiClientConfig = SaasusApiHelpers.CreateClientConfiguration(c => c.GetAuthApiClientConfig(), Request);
                var tenantAttributeApi = new TenantAttributeApi(authApiClientConfig);
                var tenantAttributes = await tenantAttributeApi.GetTenantAttributesAsync();

                return Ok(tenantAttributes);
            }
            catch (Exception ex)
            {
                var (status, body) = SaasusApiHelpers.HandleApiException(ex);
                return Content(status, body);
            }
        }

        // POST /self_sign_up
        [HttpPost]
        [Route("self_sign_up")]
        public async Task<IHttpActionResult> SelfSignUp([FromBody] SelfSignUpRequest request)
        {
            // バリデーションのチェック
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            string tenantName = request.TenantName;
            var userAttributeValues = request.UserAttributeValues ?? new Dictionary<string, object>();
            var tenantAttributeValues = request.TenantAttributeValues ?? new Dictionary<string, object>();

            try
            {
                // Bearerトークンを取得
                var token = SaasusApiHelpers.GetBearerToken(Request);
                // ユーザー情報の取得
                var authApiClientConfig = SaasusApiHelpers.CreateClientConfiguration(c => c.GetAuthApiClientConfig(), Request);
                var userInfoApi = new UserInfoApi(authApiClientConfig);
                var userInfo = await userInfoApi.GetUserInfoAsync(token);
                if (userInfo.Tenants != null && userInfo.Tenants.Any())
                {
                    return BadRequest("User is already associated with a tenant.");
                }

                // テナント属性の取得
                var tenantAttributeApi = new TenantAttributeApi(authApiClientConfig);
                var tenantAttributes = await tenantAttributeApi.GetTenantAttributesAsync();

                // 属性の型に応じた処理
                foreach (var attribute in tenantAttributes.VarTenantAttributes)
                {
                    var attributeName = attribute.AttributeName;
                    var attributeType = attribute.AttributeType.ToString();

                    if (tenantAttributeValues.ContainsKey(attributeName) && attributeType.ToLower() == "number")
                    {
                        if (int.TryParse(tenantAttributeValues[attributeName]?.ToString(), out int numericValue))
                        {
                            tenantAttributeValues[attributeName] = numericValue;
                        }
                        else
                        {
                            return BadRequest("Invalid attribute value");
                        }
                    }
                }

                // テナントの登録
                var tenantApi = new TenantApi(authApiClientConfig);
                var tenantProps = new TenantProps(
                    name: tenantName,
                    attributes: tenantAttributeValues,
                    backOfficeStaffEmail: userInfo.Email
                );


                var createdTenant = await tenantApi.CreateTenantAsync(tenantProps);
                var tenantId = createdTenant.Id;

                // ユーザー属性の取得
                var userAttributeApi = new UserAttributeApi(authApiClientConfig);
                var userAttributes = await userAttributeApi.GetUserAttributesAsync();

                // 属性の型に応じた処理
                foreach (var attribute in userAttributes.VarUserAttributes)
                {
                    var attributeName = attribute.AttributeName;
                    var attributeType = attribute.AttributeType.ToString();

                    if (userAttributeValues.ContainsKey(attributeName) && attributeType.ToLower() == "number")
                    {
                        if (int.TryParse(userAttributeValues[attributeName]?.ToString(), out int numericValue))
                        {
                            userAttributeValues[attributeName] = numericValue;
                        }
                        else
                        {
                            return BadRequest("Invalid attribute value");
                        }
                    }
                }


                // テナントユーザーの登録
                var tenantUserApi = new TenantUserApi(authApiClientConfig);
                var createTenantUserParam = new CreateTenantUserParam(userInfo.Email, userAttributeValues);
                var tenantUser = await tenantUserApi.CreateTenantUserAsync(tenantId, createTenantUserParam);


                var createTenantUserRolesParam = new CreateTenantUserRolesParam(new List<string> { "admin" });
                await tenantUserApi.CreateTenantUserRolesAsync(tenantId, tenantUser.Id, 3, createTenantUserRolesParam);

                return Ok(new { message = "User successfully registered to the tenant", request });
            }
            catch (Exception ex)
            {
                var (status, body) = SaasusApiHelpers.HandleApiException(ex);
                return Content(status, body);
            }
        }

        [HttpPost]
        [Route("logout")]
        public IHttpActionResult Logout()
        {
            // クッキーを削除
            var cookie = new HttpCookie("SaaSusRefreshToken")
            {
                Expires = System.DateTime.Now.AddDays(-1), // 過去の日付に設定して削除
                HttpOnly = true
            };

            HttpContext.Current.Response.Cookies.Add(cookie);

            return Ok(new { message = "Logged out successfully" });
        }

        // GET /invitations
        [HttpGet]
        [Route("invitations")]
        public async Task<IHttpActionResult> GetInvitations(string tenant_id)
        {
            try
            {
                if (string.IsNullOrEmpty(tenant_id))
                {
                    Console.Error.WriteLine("tenant_id query parameter is missing");
                    return BadRequest("The 'tenant_id' query parameter is required.");
                }

                var token = SaasusApiHelpers.GetBearerToken(Request);
                var authApiClientConfig = SaasusApiHelpers.CreateClientConfiguration(c => c.GetAuthApiClientConfig(), Request);
                var userInfoApi = new UserInfoApi(authApiClientConfig);
                var userInfo = await userInfoApi.GetUserInfoAsync(token);

                if (userInfo.Tenants == null || !userInfo.Tenants.Any())
                {
                    return BadRequest("No tenant information available.");
                }

                // ユーザーが所属しているテナントか確認
                var isBelongingTenant = userInfo.Tenants.Any(t => t.Id == tenant_id);
                if (!isBelongingTenant)
                {
                    Console.Error.WriteLine($"Tenant {tenant_id} does not belong to user");
                    return Content(HttpStatusCode.Forbidden, "Tenant does not belong to the user.");
                }

                // 招待一覧を取得
                var invitationApi = new InvitationApi(authApiClientConfig);
                var invitations = await invitationApi.GetTenantInvitationsAsync(tenant_id);

                return Ok(invitations.VarInvitations);
            }
            catch (Exception ex)
            {
                var (status, body) = SaasusApiHelpers.HandleApiException(ex);
                return Content(status, body);
            }
        }

        // POST /user_invitation
        [HttpPost]
        [Route("user_invitation")]
        public async Task<IHttpActionResult> UserInvitation([FromBody] UserInvitationRequest request)
        {
            // バリデーションのチェック
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            string email = request.Email;
            string tenantId = request.TenantId;

            try
            {
                // 招待を作成するユーザーのアクセストークンを取得
                var accessToken = HttpContext.Current.Request.Headers.Get("X-Access-Token");

                var authApiClientConfig = SaasusApiHelpers.CreateClientConfiguration(c => c.GetAuthApiClientConfig(), Request);
                var invitationApi = new InvitationApi(authApiClientConfig);

                // envsに追加するオブジェクトを作成
                var invitedEnv = new InvitedUserEnvironmentInformationInner(
                    id: 3, // 本番環境のID:3を設定
                    roleNames: new List<string> { "admin" }
                );

                // envsのリストを作成
                var envsList = new List<InvitedUserEnvironmentInformationInner> { invitedEnv };

                // テナント招待のパラメータを作成
                var createTenantInvitationParam = new CreateTenantInvitationParam(
                    email,
                    accessToken,
                    envsList
                );

                await invitationApi.CreateTenantInvitationAsync(tenantId, createTenantInvitationParam);

                // 結果を返す
                return Ok(new { message = "Create tenant user invitation successfully", request });
            }
            catch (Exception ex)
            {
                var (status, body) = SaasusApiHelpers.HandleApiException(ex);
                return Content(status, body);
            }
        }

        // MFA ステータス確認エンドポイント
        [HttpGet]
        [Route("mfa_status")]
        public async Task<IHttpActionResult> GetMfaStatus()
        {
            try
            {
                // IDトークン（Bearerトークン）を取得
                var token = SaasusApiHelpers.GetBearerToken(Request);

                // 認証APIクライアントを初期化
                var authApiClientConfig = SaasusApiHelpers.CreateClientConfiguration(c => c.GetAuthApiClientConfig(), Request);

                // ユーザー情報を取得
                var userInfoApi = new UserInfoApi(authApiClientConfig);
                var userInfo = await userInfoApi.GetUserInfoAsync(token);

                // MFA設定を取得
                var saasUserApi = new SaasUserApi(authApiClientConfig);
                var mfaPref = await saasUserApi.GetUserMfaPreferenceAsync(userInfo.Id);

                // MFAの有効状態を返す
                return Ok(new { enabled = mfaPref.Enabled });
            }
            catch (Exception ex)
            {
                var (status, body) = SaasusApiHelpers.HandleApiException(ex);
                return Content(status, body);
            }
        }

        // MFA シークレット作成エンドポイント
        [HttpGet]
        [Route("mfa_setup")]
        public async Task<IHttpActionResult> SetupMfa()
        {
            try
            {
                // トークンの取得
                var token = SaasusApiHelpers.GetBearerToken(Request);
                var accessToken = HttpContext.Current.Request.Headers.Get("X-Access-Token");

                if (string.IsNullOrEmpty(accessToken))
                {
                    return Content(HttpStatusCode.Unauthorized, "Missing X-Access-Token header");
                }

                // APIクライアント初期化
                var authApiClientConfig = SaasusApiHelpers.CreateClientConfiguration(c => c.GetAuthApiClientConfig(), Request);

                // ユーザー情報取得
                var userInfoApi = new UserInfoApi(authApiClientConfig);
                var userInfo = await userInfoApi.GetUserInfoAsync(token);

                // シークレットコード生成
                var saasUserApi = new SaasUserApi(authApiClientConfig);
                var secretCode = await saasUserApi.CreateSecretCodeAsync(
                    userInfo.Id,
                    new CreateSecretCodeParam(accessToken)
                );

                // QRコード形式のURL生成
                var qrCodeUrl = $"otpauth://totp/SaaSusPlatform:{userInfo.Email}?secret={secretCode.SecretCode}&issuer=SaaSusPlatform";

                return Ok(new { qrCodeUrl });
            }
            catch (Exception ex)
            {
                var (status, body) = SaasusApiHelpers.HandleApiException(ex);
                return Content(status, body);
            }
        }

        // MFA 認証コード検証エンドポイント
        [HttpPost]
        [Route("mfa_verify")]
        public async Task<IHttpActionResult> VerifyMfa([FromBody] MfaVerifyRequest request)
        {
            try
            {
                // トークンとX-Access-Tokenを取得
                var token = SaasusApiHelpers.GetBearerToken(Request);
                var accessToken = HttpContext.Current.Request.Headers.Get("X-Access-Token");
                var verificationCode = request.VerificationCode;

                if (string.IsNullOrEmpty(accessToken) || string.IsNullOrEmpty(verificationCode))
                {
                    return BadRequest("Missing accessToken or verificationCode");
                }

                // APIクライアント初期化とユーザー取得
                var authApiClientConfig = SaasusApiHelpers.CreateClientConfiguration(c => c.GetAuthApiClientConfig(), Request);
                var userInfoApi = new UserInfoApi(authApiClientConfig);
                var userInfo = await userInfoApi.GetUserInfoAsync(token);

                // トークンを使って検証APIを実行
                var saasUserApi = new SaasUserApi(authApiClientConfig);
                await saasUserApi.UpdateSoftwareTokenAsync(
                    userInfo.Id,
                    new UpdateSoftwareTokenParam(accessToken, verificationCode)
                );

                return Ok(new { message = "MFA verification successful" });
            }
            catch (Exception ex)
            {
                var (status, body) = SaasusApiHelpers.HandleApiException(ex);
                return Content(status, body);
            }
        }

        // MFA 有効化エンドポイント
        [HttpPost]
        [Route("mfa_enable")]
        public async Task<IHttpActionResult> EnableMfa()
        {
            try
            {
                var token = SaasusApiHelpers.GetBearerToken(Request);
                var authApiClientConfig = SaasusApiHelpers.CreateClientConfiguration(c => c.GetAuthApiClientConfig(), Request);
                var userInfoApi = new UserInfoApi(authApiClientConfig);
                var userInfo = await userInfoApi.GetUserInfoAsync(token);

                var saasUserApi = new SaasUserApi(authApiClientConfig);
                var mfaPreference = new MfaPreference(enabled: true, method: MfaPreference.MethodEnum.SoftwareToken);
                await saasUserApi.UpdateUserMfaPreferenceAsync(userInfo.Id, mfaPreference);

                return Ok(new { message = "MFA has been enabled" });
            }
            catch (Exception ex)
            {
                var (status, body) = SaasusApiHelpers.HandleApiException(ex);
                return Content(status, body);
            }
        }

        // MFA 無効化エンドポイント
        [HttpPost]
        [Route("mfa_disable")]
        public async Task<IHttpActionResult> DisableMfa()
        {
            try
            {
                var token = SaasusApiHelpers.GetBearerToken(Request);
                var authApiClientConfig = SaasusApiHelpers.CreateClientConfiguration(c => c.GetAuthApiClientConfig(), Request);
                var userInfoApi = new UserInfoApi(authApiClientConfig);
                var userInfo = await userInfoApi.GetUserInfoAsync(token);

                var saasUserApi = new SaasUserApi(authApiClientConfig);
                var mfaPreference = new MfaPreference(enabled: false, method: MfaPreference.MethodEnum.SoftwareToken);
                await saasUserApi.UpdateUserMfaPreferenceAsync(userInfo.Id, mfaPreference);

                return Ok(new { message = "MFA has been disabled" });
            }
            catch (Exception ex)
            {
                var (status, body) = SaasusApiHelpers.HandleApiException(ex);
                return Content(status, body);
            }
        }

        public class UserRegisterRequest
        {
            [Required(ErrorMessage = "Email is required.")]
            public string Email { get; set; }
            [Required(ErrorMessage = "Password is required.")]
            public string Password { get; set; }
            [Required(ErrorMessage = "TenantId is required.")]
            public string TenantId { get; set; }
            public Dictionary<string, object> UserAttributeValues { get; set; }
        }

        public class SelfSignUpRequest
        {
            [Required(ErrorMessage = "TenantName is required.")]
            public string TenantName { get; set; }
            public Dictionary<string, object> UserAttributeValues { get; set; }
            public Dictionary<string, object> TenantAttributeValues { get; set; }
        }

        public class UserDeleteRequest
        {
            public string TenantId { get; set; }
            public string UserId { get; set; }
        }

        public class UserInvitationRequest
        {
            [Required(ErrorMessage = "Email is required.")]
            public string Email { get; set; }
            [Required(ErrorMessage = "TenantId is required.")]
            public string TenantId { get; set; }
        }

        public class MfaVerifyRequest
        {
            [Required(ErrorMessage = "VerificationCode is required.")]
            [Newtonsoft.Json.JsonProperty("verification_code")]
            public string VerificationCode { get; set; }
        }

        // ApplicationDbContext クラス
        public class ApplicationDbContext : DbContext
        {
            // コンストラクタで Web.config の接続文字列を使用
            public ApplicationDbContext() : base("DefaultConnection")
            {
            }

            // DeleteUserLog の DbSet
            public DbSet<DeleteUserLog> DeleteUserLogs { get; set; }

            // モデルの設定
            protected override void OnModelCreating(DbModelBuilder modelBuilder)
            {
                modelBuilder.HasDefaultSchema("public"); // PostgreSQL のデフォルトスキーマ
                base.OnModelCreating(modelBuilder);

                // DeleteUserLog テーブルの設定
                modelBuilder.Entity<DeleteUserLog>()
                    .ToTable("delete_user_log")
                    .HasKey(e => e.Id);

                modelBuilder.Entity<DeleteUserLog>()
                    .Property(e => e.TenantId)
                    .HasColumnName("tenant_id")
                    .IsRequired()
                    .HasMaxLength(100);

                modelBuilder.Entity<DeleteUserLog>()
                    .Property(e => e.UserId)
                    .HasColumnName("user_id")
                    .IsRequired()
                    .HasMaxLength(100);

                modelBuilder.Entity<DeleteUserLog>()
                    .Property(e => e.Email)
                    .HasColumnName("email")
                    .IsRequired()
                    .HasMaxLength(100);

                modelBuilder.Entity<DeleteUserLog>()
                    .Property(e => e.DeleteAt)
                    .HasColumnName("delete_at")
                    .HasColumnType("timestamp");
            }
        }

        // DeleteUserLog エンティティ
        public class DeleteUserLog
        {
            [Key]
            [Column("id")]
            public int Id { get; set; }

            [Required]
            [MaxLength(100)]
            public string TenantId { get; set; }

            [Required]
            [MaxLength(100)]
            public string UserId { get; set; }

            [Required]
            [MaxLength(100)]
            public string Email { get; set; }

            public DateTime DeleteAt { get; set; }
        }
    }
}
