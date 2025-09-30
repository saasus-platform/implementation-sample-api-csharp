using System.ComponentModel.DataAnnotations;
using System.Net;
using Newtonsoft.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.EntityFrameworkCore;
using SampleWebAppDotNet8.Helpers;

using authapi.Api;
using authapi.Model;
using pricingapi.Api;
using pricingapi.Model;
using modules;

namespace SampleWebApp
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Kestrelのポート設定
            builder.WebHost.ConfigureKestrel(options =>
            {
                options.ListenAnyIP(80);
            });

            // CORSポリシーの登録
            builder.Services.AddCors(options =>
            {
                options.AddPolicy("CorsPolicy", policyBuilder =>
                {
                    policyBuilder.WithOrigins("http://localhost:3000")
                                 .AllowAnyMethod()
                                 .AllowAnyHeader()
                                 .AllowCredentials();
                });
            });

            // Swaggerの設定
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();

            // コントローラーサポートを追加
            builder.Services.AddControllers();

            // appsettings.json の読み込み
            var config = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json")
                .Build();

            // 必要な設定値を環境変数として登録
            Environment.SetEnvironmentVariable("SAASUS_API_URL_BASE", config["SaasusSettings:SAASUS_API_URL_BASE"] ?? string.Empty);
            Environment.SetEnvironmentVariable("SAASUS_SECRET_KEY", config["SaasusSettings:SAASUS_SECRET_KEY"] ?? string.Empty);
            Environment.SetEnvironmentVariable("SAASUS_API_KEY", config["SaasusSettings:SAASUS_API_KEY"] ?? string.Empty);
            Environment.SetEnvironmentVariable("SAASUS_SAAS_ID", config["SaasusSettings:SAASUS_SAAS_ID"] ?? string.Empty);
            Environment.SetEnvironmentVariable("DATABASE_URL", config["SaasusSettings:DATABASE_URL"] ?? string.Empty);

            var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");
            if (string.IsNullOrEmpty(databaseUrl))
            {
                throw new InvalidOperationException("DATABASE_URL is not set in the environment variables.");
            }

            // DbContextの設定
            builder.Services.AddDbContext<ApplicationDbContext>(options =>
                options.UseNpgsql(databaseUrl));

            var app = builder.Build();

            // 開発環境でのみSwaggerを有効化
            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            // CORSポリシーを適用
            app.UseCors("CorsPolicy");

            static object ConvertToExpectedType(object value, string expectedType)
            {
                switch (expectedType.ToLowerInvariant())
                {
                    case "string":
                        return value?.ToString() ?? string.Empty;
                    case "number":
                        if (int.TryParse(value?.ToString(), out int numericValue))
                            return numericValue;
                        throw new InvalidOperationException("Invalid number value.");
                    case "boolean":
                    case "bool":
                        if (bool.TryParse(value?.ToString(), out bool boolValue))
                            return boolValue;
                        throw new InvalidOperationException("Invalid boolean value.");
                    case "date":
                        if (DateTime.TryParse(value?.ToString(), out DateTime dateValue))
                        {
                            return dateValue.ToString("yyyy-MM-dd");
                        }
                        throw new InvalidOperationException("Invalid date value.");
                    default:
                        throw new InvalidOperationException($"Unsupported type: {expectedType}");
                }
            }

            app.MapGet("/credentials", async (HttpContext context, string code) =>
            {
                if (string.IsNullOrEmpty(code))
                {
                    return Results.BadRequest("The 'code' query parameter is missing.");
                }

                try
                {
                    var authApiClientConfig = SaasusApiHelpers.CreateClientConfiguration(c => c.GetAuthApiClientConfig(), context);
                    var credentialApi = new CredentialApi(authApiClientConfig);
                    var credentials = await credentialApi.GetAuthCredentialsAsync(code, "tempCodeAuth", null);

                    // JSON形式でレスポンスを返す
                    var jsonResponse = credentials.ToJson(); // SDK が提供する ToJson メソッドを利用
                    return Results.Text(jsonResponse, "application/json");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to get credentials: {ex.Message}");
                    return Results.Problem("Failed to get credentials", statusCode: 500);
                }
            });

            app.MapGet("/refresh", async (HttpContext context) =>
            {
                var refreshToken = context.Request.Cookies["SaaSusRefreshToken"];
                if (string.IsNullOrEmpty(refreshToken))
                {
                    return Results.BadRequest("No refresh token found.");
                }

                try
                {
                    var authApiClientConfig = SaasusApiHelpers.CreateClientConfiguration(c => c.GetAuthApiClientConfig(), context);
                    var credentialApi = new CredentialApi(authApiClientConfig);
                    var credentials = await credentialApi.GetAuthCredentialsAsync(null, "refreshTokenAuth", refreshToken);

                    // JSON形式でレスポンスを返す
                    var jsonResponse = credentials.ToJson(); // SDK が提供する ToJson メソッドを利用
                    return Results.Text(jsonResponse, "application/json");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to refresh credentials: {ex.Message}");
                    return Results.Problem("Failed to refresh credentials", statusCode: 500);
                }
            });

            app.MapGet("/userinfo", async (HttpContext context) =>
            {
                var token = SaasusApiHelpers.GetBearerToken(context);
                if (string.IsNullOrEmpty(token))
                {
                    return Results.Unauthorized();
                }

                try
                {
                    var authApiClientConfig = SaasusApiHelpers.CreateClientConfiguration(c => c.GetAuthApiClientConfig(), context);
                    var userInfoApi = new UserInfoApi(authApiClientConfig);
                    var userInfo = await userInfoApi.GetUserInfoAsync(token);

                    // JSON形式でレスポンスを返す
                    var jsonResponse = userInfo.ToJson(); // SDK が提供する ToJson メソッドを利用
                    return Results.Text(jsonResponse, "application/json");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to get user info: {ex.Message}");
                    return Results.Problem("Failed to get user info", statusCode: 500);
                }
            });

            app.MapGet("/users", async (HttpContext context) =>
            {
                var token = SaasusApiHelpers.GetBearerToken(context);
                if (string.IsNullOrEmpty(token))
                {
                    return Results.Unauthorized();
                }

                try
                {
                    var authApiClientConfig = SaasusApiHelpers.CreateClientConfiguration(c => c.GetAuthApiClientConfig(), context);
                    var userInfoApi = new UserInfoApi(authApiClientConfig);
                    var userInfo = await userInfoApi.GetUserInfoAsync(token);

                    if (userInfo.Tenants == null || !userInfo.Tenants.Any())
                    {
                        return Results.BadRequest("No tenant information available.");
                    }

                    // クエリパラメータから tenant_id を取得
                    var tenantId = context.Request.Query["tenant_id"].ToString();
                    if (string.IsNullOrEmpty(tenantId))
                    {
                        Console.Error.WriteLine("tenant_id query parameter is missing");
                        return Results.BadRequest("tenant_id query parameter is required");
                    }

                    // ユーザーが所属しているテナントか確認
                    var isBelongingTenant = userInfo.Tenants.Any(t => t.Id == tenantId);
                    if (!isBelongingTenant)
                    {
                        Console.Error.WriteLine($"Tenant {tenantId} does not belong to user");
                        return Results.Problem("Tenant that does not belong", statusCode: 403);
                    }


                    // テナントユーザーを取得
                    var tenantUserApi = new TenantUserApi(authApiClientConfig);
                    var users = await tenantUserApi.GetTenantUsersAsync(tenantId);
                    if (users == null || users.VarUsers == null)
                    {
                        Console.Error.WriteLine("Failed to get saas users: response is null");
                        return Results.Problem("Internal server error", statusCode: 500);
                    }

                    // JSON形式でレスポンスを返す
                    var jsonResponse = JsonConvert.SerializeObject(users.VarUsers); // Newtonsoft.Json を利用
                    return Results.Text(jsonResponse, "application/json");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to get users: {ex.Message}");
                    return Results.Problem("Failed to get users", statusCode: 500);
                }
            });

            app.MapGet("/tenant_attributes", async (HttpContext context) =>
            {
                var token = SaasusApiHelpers.GetBearerToken(context);
                if (string.IsNullOrEmpty(token))
                {
                    return Results.Unauthorized();
                }

                try
                {

                    var authApiClientConfig = SaasusApiHelpers.CreateClientConfiguration(c => c.GetAuthApiClientConfig(), context);
                    var userInfoApi = new UserInfoApi(authApiClientConfig);
                    var userInfo = await userInfoApi.GetUserInfoAsync(token);
                    if (userInfo.Tenants == null || !userInfo.Tenants.Any())
                    {
                        return Results.BadRequest("No tenant information available.");
                    }

                    // クエリパラメータから tenant_id を取得
                    var tenantId = context.Request.Query["tenant_id"].ToString();
                    if (string.IsNullOrEmpty(tenantId))
                    {
                        Console.Error.WriteLine("tenant_id query parameter is missing");
                        return Results.BadRequest("tenant_id query parameter is required");
                    }

                    // ユーザーが所属しているテナントか確認
                    var isBelongingTenant = userInfo.Tenants.Any(t => t.Id == tenantId);
                    if (!isBelongingTenant)
                    {
                        Console.Error.WriteLine($"Tenant {tenantId} does not belong to user");
                        return Results.Problem("Tenant that does not belong", statusCode: 403);
                    }

                    // テナント属性の取得
                    var tenantAttributeApi = new TenantAttributeApi(authApiClientConfig);
                    var tenantAttributes = await tenantAttributeApi.GetTenantAttributesAsync();

                    // テナント情報の取得
                    var tenantApi = new TenantApi(authApiClientConfig);
                    var tenant = await tenantApi.GetTenantAsync(tenantId);

                    // 結果を格納する辞書
                    var result = new Dictionary<string, Dictionary<string, object?>>();

                    foreach (var tenantAttribute in tenantAttributes.VarTenantAttributes)
                    {
                        var attributeName = tenantAttribute.AttributeName;
                        var detail = new Dictionary<string, object?>
                        {
                { "display_name", tenantAttribute.DisplayName },
                { "attribute_type", tenantAttribute.AttributeType.ToString() },
                { "value", tenant.Attributes.ContainsKey(attributeName) ? tenant.Attributes[attributeName] : null }
                        };

                        result[attributeName] = detail;
                    }

                    // JSON形式で返却
                    return Results.Json(result);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to get tenant attributes: {ex.Message}");
                    return Results.Problem("Failed to get tenant attributes", statusCode: 500);
                }
            });

            app.MapGet("/user_attributes", async (HttpContext context) =>
            {
                var token = SaasusApiHelpers.GetBearerToken(context);
                if (string.IsNullOrEmpty(token))
                {
                    return Results.Unauthorized();
                }

                try
                {
                    var authApiClientConfig = SaasusApiHelpers.CreateClientConfiguration(c => c.GetAuthApiClientConfig(), context);
                    var userAttributeApi = new UserAttributeApi(authApiClientConfig);
                    var userAttributes = await userAttributeApi.GetUserAttributesAsync();

                    // JSON形式でレスポンスを返す
                    var jsonResponse = userAttributes.ToJson(); // SDK が提供する ToJson メソッドを利用
                    return Results.Text(jsonResponse, "application/json");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to get user attributes: {ex.Message}");
                    return Results.Problem("Failed to get user attributes", statusCode: 500);
                }
            });

            app.MapGet("/pricing_plan", async (HttpContext context, string plan_id) =>
            {
                var token = SaasusApiHelpers.GetBearerToken(context);
                if (string.IsNullOrEmpty(token))
                {
                    return Results.Unauthorized();
                }

                if (string.IsNullOrEmpty(plan_id))
                {
                    return Results.BadRequest("No price plan found for the tenant.");
                }

                try
                {
                    var pricingConfig = SaasusApiHelpers.CreateClientConfiguration(c => c.GetPricingApiClientConfig(), context);
                    var pricingPlansApi = new PricingPlansApi(pricingConfig);
                    var plan = await pricingPlansApi.GetPricingPlanAsync(plan_id);

                    // JSON形式でレスポンスを返す
                    var jsonResponse = plan.ToJson(); // SDK が提供する ToJson メソッドを利用
                    return Results.Text(jsonResponse, "application/json");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to get pricing plan: {ex.Message}");
                    return Results.Problem("Failed to get pricing plan", statusCode: 500);
                }
            });


            app.MapPost("/user_register", async ([FromBody] UserRegisterRequest requestBody, HttpContext context) =>
            {
                var token = SaasusApiHelpers.GetBearerToken(context);
                if (string.IsNullOrEmpty(token))
                {
                    return Results.Unauthorized();
                }

                // バリデーションの実行
                var validationResults = new List<ValidationResult>();
                var validationContext = new ValidationContext(requestBody);
                if (!Validator.TryValidateObject(requestBody, validationContext, validationResults, true))
                {
                    var errors = validationResults.Select(vr => new { Field = vr.MemberNames.FirstOrDefault(), Error = vr.ErrorMessage });
                    return Results.BadRequest(new { error = "Validation failed.", details = errors });
                }

                string email = requestBody.Email;
                string password = requestBody.Password;
                string tenantId = requestBody.TenantId;
                var userAttributeValues = requestBody.UserAttributeValues ?? new Dictionary<string, object>();

                try
                {
                    // ユーザー情報の取得
                    var authApiClientConfig = SaasusApiHelpers.CreateClientConfiguration(c => c.GetAuthApiClientConfig(), context);
                    var userInfoApi = new UserInfoApi(authApiClientConfig);
                    var userInfo = await userInfoApi.GetUserInfoAsync(token);
                    if (userInfo.Tenants == null || !userInfo.Tenants.Any())
                    {
                        return Results.BadRequest("No tenant information available.");
                    }

                    // ユーザーが所属しているテナントか確認
                    var isBelongingTenant = userInfo.Tenants.Any(t => t.Id == tenantId);
                    if (!isBelongingTenant)
                    {
                        Console.Error.WriteLine($"Tenant {tenantId} does not belong to user");
                        return Results.Problem("Tenant that does not belong", statusCode: 403);
                    }


                    // ユーザー属性の取得
                    var userAttributeApi = new UserAttributeApi(authApiClientConfig);
                    var userAttributes = await userAttributeApi.GetUserAttributesAsync();

                    // 属性の型に応じた処理
                    foreach (var attribute in userAttributes.VarUserAttributes)
                    {
                        var attributeName = attribute.AttributeName;
                        var attributeType = attribute.AttributeType.ToString();

                        if (userAttributeValues.ContainsKey(attributeName))
                        {    
                            userAttributeValues[attributeName] = ConvertToExpectedType(userAttributeValues[attributeName], attributeType);
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

                    return Results.Ok(new { message = "User registered successfully" });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to register user: {ex.Message}");
                    return Results.Problem("Failed to register user", statusCode: 500);
                }
            });

            app.MapDelete("/user_delete", async ([FromBody] UserDeleteRequest requestBody, HttpContext context, ApplicationDbContext dbContext) =>
            {
                var token = SaasusApiHelpers.GetBearerToken(context);
                if (string.IsNullOrEmpty(token))
                {
                    return Results.Unauthorized();
                }

                // バリデーションの実行
                var validationResults = new List<ValidationResult>();
                var validationContext = new ValidationContext(requestBody);
                if (!Validator.TryValidateObject(requestBody, validationContext, validationResults, true))
                {
                    var errors = validationResults.Select(vr => new { Field = vr.MemberNames.FirstOrDefault(), Error = vr.ErrorMessage });
                    return Results.BadRequest(new { error = "Validation failed.", details = errors });
                }

                string tenantId = requestBody.TenantId;
                string userId = requestBody.UserId;

                try
                {
                    // ユーザー情報の取得
                    var authApiClientConfig = SaasusApiHelpers.CreateClientConfiguration(c => c.GetAuthApiClientConfig(), context);
                    var userInfoApi = new UserInfoApi(authApiClientConfig);
                    var userInfo = userInfoApi.GetUserInfo(token);
                    if (userInfo.Tenants == null || !userInfo.Tenants.Any())
                    {
                        return Results.BadRequest("No tenant information available.");
                    }

                    // ユーザーが所属しているテナントか確認
                    var isBelongingTenant = userInfo.Tenants.Any(t => t.Id == tenantId);
                    if (!isBelongingTenant)
                    {
                        Console.Error.WriteLine($"Tenant {tenantId} does not belong to user");
                        return Results.Problem("Tenant that does not belong", statusCode: 403);
                    }

                    // ユーザー削除処理
                    // SaaSusからユーザー情報を取得
                    var tenantUserApi = new TenantUserApi(authApiClientConfig);
                    var deleteUser = tenantUserApi.GetTenantUser(tenantId, userId);
                    await tenantUserApi.DeleteTenantUserAsync(tenantId, userId);

                    // データベースに削除ログを保存
                    var deleteLog = new DeleteUserLog
                    {
                        TenantId = tenantId,
                        UserId = userId,
                        Email = deleteUser.Email,
                    };

                    dbContext.DeleteUserLogs.Add(deleteLog);
                    await dbContext.SaveChangesAsync();
                    return Results.Ok(new { message = "User deleted successfully" });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to delete user: {ex.Message}");
                    return Results.Problem("Failed to delete user", statusCode: 500);
                }
            });

            app.MapGet("/delete_user_log", async (HttpContext context, ApplicationDbContext dbContext) =>
            {
                // クエリパラメータから `tenant_id` を取得
                var tenantId = context.Request.Query["tenant_id"].ToString();
                if (string.IsNullOrEmpty(tenantId))
                {
                    return Results.BadRequest(new { error = "tenant_id query parameter is required" });
                }

                // ユーザー情報を取得
                var token = SaasusApiHelpers.GetBearerToken(context);
                if (string.IsNullOrEmpty(token))
                {
                    return Results.Unauthorized();
                }

                try
                {
                    var authApiClientConfig = SaasusApiHelpers.CreateClientConfiguration(c => c.GetAuthApiClientConfig(), context);
                    var userInfoApi = new UserInfoApi(authApiClientConfig);
                    var userInfo = await userInfoApi.GetUserInfoAsync(token);

                    if (userInfo.Tenants == null || !userInfo.Tenants.Any())
                    {
                        return Results.BadRequest(new { error = "No tenants found for the user" });
                    }

                    // 指定されたテナントIDがユーザーの所属するテナントに含まれるか確認
                    var isBelongingTenant = userInfo.Tenants.Any(t => t.Id == tenantId);
                    if (!isBelongingTenant)
                    {
                        return Results.BadRequest(new { error = "Tenant that does not belong" });
                    }

                    // データベースから削除ログを取得
                    var deleteUserLogs = await dbContext.DeleteUserLogs
                        .Where(log => log.TenantId == tenantId)
                        .ToListAsync();

                    // レスポンスデータの整形
                    var responseData = deleteUserLogs.Select(log => new
                    {
                        id = log.Id,
                        tenant_id = log.TenantId,
                        user_id = log.UserId,
                        email = log.Email,
                        delete_at = log.DeleteAt.ToString("o") // ISO 8601形式に変換
                    });

                    return Results.Ok(responseData);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to get delete user log: {ex.Message}");
                    return Results.Problem("Failed to get delete user log", statusCode: 500);
                }
            });

            app.MapGet("/tenant_attributes_list", async (HttpContext context) =>
            {
                var token = SaasusApiHelpers.GetBearerToken(context);
                if (string.IsNullOrEmpty(token))
                {
                    return Results.Unauthorized();
                }

                try
                {
                    var authApiClientConfig = SaasusApiHelpers.CreateClientConfiguration(c => c.GetAuthApiClientConfig(), context);
                    var tenantAttributeApi = new TenantAttributeApi(authApiClientConfig);
                    var tenantAttributes = await tenantAttributeApi.GetTenantAttributesAsync();

                    // JSON形式でレスポンスを返す
                    var jsonResponse = tenantAttributes.ToJson(); // SDK が提供する ToJson メソッドを利用
                    return Results.Text(jsonResponse, "application/json");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to get tenant attributes list: {ex.Message}");
                    return Results.Problem("Failed to get tenant attributes list", statusCode: 500);
                }
            });

            app.MapPost("/self_sign_up", async ([FromBody] SelfSignUpRequest requestBody, HttpContext context) =>
            {
                var token = SaasusApiHelpers.GetBearerToken(context);
                if (string.IsNullOrEmpty(token))
                {
                    return Results.Unauthorized();
                }

                // バリデーションの実行
                var validationResults = new List<ValidationResult>();
                var validationContext = new ValidationContext(requestBody);
                if (!Validator.TryValidateObject(requestBody, validationContext, validationResults, true))
                {
                    var errors = validationResults.Select(vr => new { Field = vr.MemberNames.FirstOrDefault(), Error = vr.ErrorMessage });
                    return Results.BadRequest(new { error = "Validation failed.", details = errors });
                }

                string tenantName = requestBody.TenantName;
                var userAttributeValues = requestBody.UserAttributeValues ?? new Dictionary<string, object>();
                var tenantAttributeValues = requestBody.TenantAttributeValues ?? new Dictionary<string, object>();

                try
                {
                    // ユーザー情報の取得
                    var authApiClientConfig = SaasusApiHelpers.CreateClientConfiguration(c => c.GetAuthApiClientConfig(), context);
                    var userInfoApi = new UserInfoApi(authApiClientConfig);
                    var userInfo = await userInfoApi.GetUserInfoAsync(token);
                    if (userInfo.Tenants != null && userInfo.Tenants.Any())
                    {
                        return Results.BadRequest("User is already associated with a tenant.");
                    }

                    // テナント属性の取得
                    var tenantAttributeApi = new TenantAttributeApi(authApiClientConfig);
                    var tenantAttributes = await tenantAttributeApi.GetTenantAttributesAsync();

                    // 属性の型に応じた処理
                    foreach (var attribute in tenantAttributes.VarTenantAttributes)
                    {
                        var attributeName = attribute.AttributeName;
                        var attributeType = attribute.AttributeType.ToString();

                        if (tenantAttributeValues.ContainsKey(attributeName))
                        {
                            tenantAttributeValues[attributeName] = ConvertToExpectedType(tenantAttributeValues[attributeName], attributeType);
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

                        if (userAttributeValues.ContainsKey(attributeName))
                        {
                            userAttributeValues[attributeName] = ConvertToExpectedType(userAttributeValues[attributeName], attributeType);
                        }
                    }

                    // テナントユーザーの登録
                    var tenantUserApi = new TenantUserApi(authApiClientConfig);
                    var createTenantUserParam = new CreateTenantUserParam(userInfo.Email, userAttributeValues);
                    var tenantUser = await tenantUserApi.CreateTenantUserAsync(tenantId, createTenantUserParam);


                    // ロールを設定
                    var createTenantUserRolesParam = new CreateTenantUserRolesParam(new List<string> { "admin" });
                    await tenantUserApi.CreateTenantUserRolesAsync(tenantId, tenantUser.Id, 3, createTenantUserRolesParam);

                    return Results.Ok(new { message = "User successfully registered to the tenant"});
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to self sign up: {ex.Message}");
                    return Results.Problem("Failed to self sign up", statusCode: 500);
                }
            });

            app.MapPost("/logout", (HttpContext context) =>
            {
                context.Response.Cookies.Delete("SaaSusRefreshToken");

                return Results.Json(new { message = "Logged out successfully" });
            });

            app.MapGet("/invitations", async (HttpContext context) =>
            {
                var token = SaasusApiHelpers.GetBearerToken(context);
                if (string.IsNullOrEmpty(token))
                {
                    return Results.Unauthorized();
                }

                try
                {
                    var authApiClientConfig = SaasusApiHelpers.CreateClientConfiguration(c => c.GetAuthApiClientConfig(), context);
                    var userInfoApi = new UserInfoApi(authApiClientConfig);
                    var userInfo = await userInfoApi.GetUserInfoAsync(token);

                    if (userInfo.Tenants == null || !userInfo.Tenants.Any())
                    {
                        return Results.BadRequest("No tenant information available.");
                    }

                    // クエリパラメータから tenant_id を取得
                    var tenantId = context.Request.Query["tenant_id"].ToString();
                    if (string.IsNullOrEmpty(tenantId))
                    {
                        Console.Error.WriteLine("tenant_id query parameter is missing");
                        return Results.BadRequest("tenant_id query parameter is required");
                    }

                    // ユーザーが所属しているテナントか確認
                    var isBelongingTenant = userInfo.Tenants.Any(t => t.Id == tenantId);
                    if (!isBelongingTenant)
                    {
                        Console.Error.WriteLine($"Tenant {tenantId} does not belong to user");
                        return Results.Problem("Tenant that does not belong", statusCode: 403);
                    }

                    // 招待一覧を取得
                    var invitationApi = new InvitationApi(authApiClientConfig);
                    var invitations = await invitationApi.GetTenantInvitationsAsync(tenantId);

                    // JSON形式でレスポンスを返す
                    var jsonResponse = JsonConvert.SerializeObject(invitations.VarInvitations);

                    return Results.Text(jsonResponse, "application/json");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to get invitations: {ex.Message}");
                    return Results.Problem("Failed to get invitations", statusCode: 500);
                }
            });

            app.MapPost("/user_invitation", async ([FromBody] UserInvitationRequest requestBody, HttpContext context) =>
            {
                var token = SaasusApiHelpers.GetBearerToken(context);
                if (string.IsNullOrEmpty(token))
                {
                    return Results.Unauthorized();
                }

                // バリデーションの実行
                var validationResults = new List<ValidationResult>();
                var validationContext = new ValidationContext(requestBody);
                if (!Validator.TryValidateObject(requestBody, validationContext, validationResults, true))
                {
                    var errors = validationResults.Select(vr => new { Field = vr.MemberNames.FirstOrDefault(), Error = vr.ErrorMessage });
                    return Results.BadRequest(new { error = "Validation failed.", details = errors });
                }

                string email = requestBody.Email;
                string tenantId = requestBody.TenantId;

                try
                {
                    // 招待を作成するユーザーのアクセストークンを取得
                    var accessToken = context.Request.Headers["X-Access-Token"].FirstOrDefault();

                    // アクセストークンがリクエストヘッダーに含まれていなかったらエラー
                    if (string.IsNullOrEmpty(accessToken))
                    {
                        return Results.Unauthorized();
                    }

                    var authApiClientConfig = SaasusApiHelpers.CreateClientConfiguration(c => c.GetAuthApiClientConfig(), context);
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

                    // テナントへの招待を作成
                    await invitationApi.CreateTenantInvitationAsync(tenantId, createTenantInvitationParam);

                    return Results.Ok(new { message = "Create tenant user invitation successfully" });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to invite user: {ex.Message}");
                    return Results.Problem("Failed to invite user", statusCode: 500);
                }
            });

            app.MapGet("/mfa_status", async (HttpContext context) =>
            {
                // AuthorizationヘッダーからBearerトークンを取得
                var token = SaasusApiHelpers.GetBearerToken(context);
                if (string.IsNullOrEmpty(token))
                {
                    return Results.Unauthorized();
                }

                try
                {
                    // APIクライアントを初期化し、ユーザー情報を取得
                    var authApiClientConfig = SaasusApiHelpers.CreateClientConfiguration(c => c.GetAuthApiClientConfig(), context);
                    var userInfoApi = new UserInfoApi(authApiClientConfig);
                    var userInfo = await userInfoApi.GetUserInfoAsync(token);

                    // MFAステータスを取得
                    var saasUserApi = new SaasUserApi(authApiClientConfig);
                    var mfaPref = await saasUserApi.GetUserMfaPreferenceAsync(userInfo.Id);

                    // JSON形式で返却
                    return Results.Json(new { enabled = mfaPref.Enabled });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to get MFA status: {ex.Message}");
                    return Results.Problem("Failed to get MFA status", statusCode: 500);
                }
            });

            app.MapGet("/mfa_setup", async (HttpContext context) =>
            {
                // アクセストークン（X-Access-Token）とIDトークン（Authorization）を取得
                var token = SaasusApiHelpers.GetBearerToken(context);
                var accessToken = context.Request.Headers["X-Access-Token"].FirstOrDefault();
                if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(accessToken))
                {
                    return Results.Unauthorized();
                }

                try
                {
                    // ユーザー情報を取得
                    var authApiClientConfig = SaasusApiHelpers.CreateClientConfiguration(c => c.GetAuthApiClientConfig(), context);
                    var userInfoApi = new UserInfoApi(authApiClientConfig);
                    var userInfo = await userInfoApi.GetUserInfoAsync(token);

                    // シークレットコードを作成
                    var saasUserApi = new SaasUserApi(authApiClientConfig);
                    var secretCode = await saasUserApi.CreateSecretCodeAsync(
                        userInfo.Id,
                        new CreateSecretCodeParam(accessToken)
                    );

                    // QRコードURLを生成（Google Authenticator形式）
                    var qrCodeUrl = $"otpauth://totp/SaaSusPlatform:{userInfo.Email}?secret={secretCode.SecretCode}&issuer=SaaSusPlatform";

                    return Results.Json(new { qrCodeUrl });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to setup MFA: {ex.Message}");
                    return Results.Problem("Failed to setup MFA", statusCode: 500);
                }
            });

            app.MapPost("/mfa_verify", async ([FromBody] MfaVerifyRequest requestBody, HttpContext context) =>
            {
                // トークンと認証コードを取得
                var token = SaasusApiHelpers.GetBearerToken(context);
                var accessToken = context.Request.Headers["X-Access-Token"].FirstOrDefault(); 
                string verificationCode = requestBody.VerificationCode;

                if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(accessToken) || string.IsNullOrEmpty(verificationCode))
                {
                    return Results.BadRequest("Missing required information.");
                }

                try
                {
                    // ユーザー情報取得
                    var authApiClientConfig = SaasusApiHelpers.CreateClientConfiguration(c => c.GetAuthApiClientConfig(), context);
                    var userInfoApi = new UserInfoApi(authApiClientConfig);
                    var userInfo = await userInfoApi.GetUserInfoAsync(token);

                    // 認証コードを検証してソフトウェアトークンを有効化
                    var saasUserApi = new SaasUserApi(authApiClientConfig);
                    await saasUserApi.UpdateSoftwareTokenAsync(
                        userInfo.Id,
                        new UpdateSoftwareTokenParam(accessToken, verificationCode)
                    );

                    return Results.Ok(new { message = "MFA verification successful" });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to verify MFA: {ex.Message}");
                    return Results.Problem("Failed to verify MFA", statusCode: 500);
                }
            });

            app.MapPost("/mfa_enable", async (HttpContext context) =>
            {
                // IDトークン取得
                var token = SaasusApiHelpers.GetBearerToken(context);
                if (string.IsNullOrEmpty(token))
                {
                    return Results.Unauthorized();
                }

                try
                {
                    // ユーザー情報取得
                    var authApiClientConfig = SaasusApiHelpers.CreateClientConfiguration(c => c.GetAuthApiClientConfig(), context);
                    var userInfoApi = new UserInfoApi(authApiClientConfig);
                    var userInfo = await userInfoApi.GetUserInfoAsync(token);

                    // MFAを有効化する設定を送信
                    var saasUserApi = new SaasUserApi(authApiClientConfig);
                    var mfaPreference = new MfaPreference(enabled: true, method: MfaPreference.MethodEnum.SoftwareToken);
                    await saasUserApi.UpdateUserMfaPreferenceAsync(userInfo.Id, mfaPreference);

                    return Results.Ok(new { message = "MFA has been enabled" });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to enable MFA: {ex.Message}");
                    return Results.Problem("Failed to enable MFA", statusCode: 500);
                }
            });

            app.MapPost("/mfa_disable", async (HttpContext context) =>
            {
                // IDトークン取得
                var token = SaasusApiHelpers.GetBearerToken(context);
                if (string.IsNullOrEmpty(token))
                {
                    return Results.Unauthorized();
                }

                try
                {
                    // ユーザー情報取得
                    var authApiClientConfig = SaasusApiHelpers.CreateClientConfiguration(c => c.GetAuthApiClientConfig(), context);
                    var userInfoApi = new UserInfoApi(authApiClientConfig);
                    var userInfo = await userInfoApi.GetUserInfoAsync(token);

                    // MFAを無効化する設定を送信
                    var saasUserApi = new SaasUserApi(authApiClientConfig);
                    var mfaPreference = new MfaPreference(enabled: false, method: MfaPreference.MethodEnum.SoftwareToken);
                    await saasUserApi.UpdateUserMfaPreferenceAsync(userInfo.Id, mfaPreference);

                    return Results.Ok(new { message = "MFA has been disabled" });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to disable MFA: {ex.Message}");
                    return Results.Problem("Failed to disable MFA", statusCode: 500);
                }
            });

            // コントローラーのルーティングを追加
            app.MapControllers();

            app.Run();

        }
    }

    public class UserRegisterRequest
    {
        [Required]
        public string Email { get; set; } = string.Empty;

        [Required]
        public string Password { get; set; } = string.Empty;

        [Required]
        public string TenantId { get; set; } = string.Empty;

        public Dictionary<string, object> UserAttributeValues { get; set; } = new Dictionary<string, object>();
    }

    public class UserDeleteRequest
    {
        [Required]
        public string TenantId { get; set; } = string.Empty;

        [Required]
        public string UserId { get; set; } = string.Empty;
    }

    public class DeleteUserLog
    {
        [Key]
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

        public DateTime DeleteAt { get; set; } = DateTime.UtcNow;

        public DeleteUserLog()
        {
            TenantId = string.Empty;
            UserId = string.Empty;
            Email = string.Empty;
        }
    }

    public class SelfSignUpRequest
    {
        [Required]
        public string TenantName { get; set; } = string.Empty;

        public Dictionary<string, object> UserAttributeValues { get; set; } = new Dictionary<string, object>();

        public Dictionary<string, object> TenantAttributeValues { get; set; } = new Dictionary<string, object>();

    }

    public class UserInvitationRequest
    {
        [Required(ErrorMessage = "Email is required.")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "TenantId is required.")]
        public string TenantId { get; set; } = string.Empty;
    }
    public class MfaVerifyRequest
    {
        [Required]
        [JsonPropertyName("verification_code")]
        public string VerificationCode { get; set; } = string.Empty;
    }

    // DbContext定義
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }
        public DbSet<DeleteUserLog> DeleteUserLogs { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<DeleteUserLog>(entity =>
            {
                entity.ToTable("delete_user_log");

                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id)
                      .HasColumnName("id")
                      .ValueGeneratedOnAdd();

                entity.Property(e => e.TenantId)
                      .HasColumnName("tenant_id")
                      .IsRequired()
                      .HasMaxLength(100);

                entity.Property(e => e.UserId)
                      .HasColumnName("user_id")
                      .IsRequired()
                      .HasMaxLength(100);

                entity.Property(e => e.Email)
                      .HasColumnName("email")
                      .IsRequired()
                      .HasMaxLength(100);

                entity.Property(e => e.DeleteAt)
                      .HasColumnName("delete_at")
                      .HasDefaultValueSql("CURRENT_TIMESTAMP");
            });
        }

    }
}