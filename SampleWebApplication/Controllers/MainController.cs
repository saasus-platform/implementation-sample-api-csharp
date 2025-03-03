using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Web.Http;
using authapi.Api;
using authapi.Model;
using pricingapi.Api;
using modules;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Data.Entity;
using System.ComponentModel.DataAnnotations.Schema;


namespace SampleWebApplication.Controllers
{
    public class MainController : ApiController
    {
        private readonly ApplicationDbContext _dbContext;

        public MainController()
        {
            _dbContext = new ApplicationDbContext(); // DbContext のインスタンス化
        }

        // 共通クライアント設定を作成するヘルパー
        private dynamic CreateClientConfiguration(Func<Configuration, dynamic> configSelector)
        {
            var config = new Configuration();
            var clientConfig = configSelector(config);

            // Referer ヘッダーの設定
            var referer = Request.Headers.Referrer?.ToString();
            if (!string.IsNullOrEmpty(referer))
            {
                clientConfig.SetReferer(referer);
            }

            return clientConfig;
        }

        // 共通認証ロジック: Bearer トークンを取得
        private string GetBearerToken(HttpRequestMessage request)
        {
            // Authorization ヘッダーを確認
            var authHeader = request.Headers.Authorization;

            // Authorization ヘッダーが存在し、スキームが "Bearer" であることを確認
            if (authHeader != null && authHeader.Scheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase))
            {
                return authHeader.Parameter?.Trim() ?? throw new HttpResponseException(HttpStatusCode.Unauthorized);
            }

            // Authorization ヘッダーが存在しない場合
            throw new HttpResponseException(HttpStatusCode.Unauthorized);
        }

        // 共通エラーハンドリング
        private IHttpActionResult HandleApiException(Exception ex)
        {
            if (ex is authapi.Client.ApiException authApiEx)
            {
                Debug.WriteLine($"Auth API Error: {authApiEx.ErrorCode} - {authApiEx.Message}");
                return StatusCode((HttpStatusCode)authApiEx.ErrorCode);
            }
            else if (ex is pricingapi.Client.ApiException pricingApiEx)
            {
                Debug.WriteLine($"Pricing API Error: {pricingApiEx.ErrorCode} - {pricingApiEx.Message}");
                return StatusCode((HttpStatusCode)pricingApiEx.ErrorCode);
            }
            // その他のエラー
            return InternalServerError(ex);
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
                var authApiClientConfig = CreateClientConfiguration(c => c.GetAuthApiClientConfig());
                var credentialApi = new CredentialApi(authApiClientConfig);
                var credentials = await credentialApi.GetAuthCredentialsAsync(code, "tempCodeAuth", null);

                return Ok(credentials);
            }
            catch (Exception ex)
            {
                return HandleApiException(ex);
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

                var authApiClientConfig = CreateClientConfiguration(c => c.GetAuthApiClientConfig());
                var credentialApi = new CredentialApi(authApiClientConfig);
                var credentials = await credentialApi.GetAuthCredentialsAsync(null, "refreshTokenAuth", refreshTokenCookie["SaaSusRefreshToken"].Value);

                return Ok(credentials);
            }
            catch (Exception ex)
            {
                return HandleApiException(ex);
            }
        }

        // GET /userinfo
        [HttpGet]
        [Route("userinfo")]
        public async Task<IHttpActionResult> GetUserInfo()
        {
            try
            {
                var token = GetBearerToken(Request);
                var authApiClientConfig = CreateClientConfiguration(c => c.GetAuthApiClientConfig());
                var userInfoApi = new UserInfoApi(authApiClientConfig);
                var userInfo = await userInfoApi.GetUserInfoAsync(token);

                return Ok(userInfo);
            }
            catch (Exception ex)
            {
                return HandleApiException(ex);
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

                var token = GetBearerToken(Request);
                var authApiClientConfig = CreateClientConfiguration(c => c.GetAuthApiClientConfig());
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
                return HandleApiException(ex);
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
                var token = GetBearerToken(Request);
                var authApiClientConfig = CreateClientConfiguration(c => c.GetAuthApiClientConfig());
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
                return HandleApiException(ex);
            }
        }

        // GET /user_attributes
        [HttpGet]
        [Route("user_attributes")]
        public async Task<IHttpActionResult> GetUserAttributes()
        {
            try
            {
                var token = GetBearerToken(Request);
                var authApiClientConfig = CreateClientConfiguration(c => c.GetAuthApiClientConfig());
                var userAttributeApi = new UserAttributeApi(authApiClientConfig);
                var userAttributes = await userAttributeApi.GetUserAttributesAsync();

                return Ok(userAttributes);
            }
            catch (Exception ex)
            {
                return HandleApiException(ex);
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
                var token = GetBearerToken(Request);

                if (string.IsNullOrEmpty(plan_id))
                {
                    return BadRequest("No price plan found for the tenant");
                }

                var pricingConfig = CreateClientConfiguration(c => c.GetPricingApiClientConfig());
                var pricingPlansApi = new PricingPlansApi(pricingConfig);
                var plan = await pricingPlansApi.GetPricingPlanAsync(plan_id);

                return Ok(plan);
            }
            catch (Exception ex)
            {
                return HandleApiException(ex);
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
                var token = GetBearerToken(Request);
                // ユーザー情報の取得
                var authApiClientConfig = CreateClientConfiguration(c => c.GetAuthApiClientConfig());
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
                return HandleApiException(ex);
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
                var token = GetBearerToken(Request);
                // ユーザー情報の取得
                var authApiClientConfig = CreateClientConfiguration(c => c.GetAuthApiClientConfig());
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
                return HandleApiException(ex);
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
                var token = GetBearerToken(Request);
                // ユーザー情報の取得
                var authApiClientConfig = CreateClientConfiguration(c => c.GetAuthApiClientConfig());
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
                return HandleApiException(ex);
            }
        }

        // GET /tenant_attributes_list
        [HttpGet]
        [Route("tenant_attributes_list")]
        public async Task<IHttpActionResult> GetTenantAttributes()
        {
            try
            {
                var token = GetBearerToken(Request);
                var authApiClientConfig = CreateClientConfiguration(c => c.GetAuthApiClientConfig());
                var tenantAttributeApi = new TenantAttributeApi(authApiClientConfig);
                var tenantAttributes = await tenantAttributeApi.GetTenantAttributesAsync();

                return Ok(tenantAttributes);
            }
            catch (Exception ex)
            {
                return HandleApiException(ex);
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
                var token = GetBearerToken(Request);
                // ユーザー情報の取得
                var authApiClientConfig = CreateClientConfiguration(c => c.GetAuthApiClientConfig());
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
                return HandleApiException(ex);
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
