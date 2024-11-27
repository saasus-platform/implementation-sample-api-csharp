using System.ComponentModel.DataAnnotations;
using System.Net;
using Newtonsoft.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.EntityFrameworkCore;

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

            // Kestrel�̃|�[�g�ݒ�
            builder.WebHost.ConfigureKestrel(options =>
            {
                options.ListenAnyIP(80);
            });

            // CORS�|���V�[�̓o�^
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

            // Swagger�̐ݒ�
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();

            // appsettings.json �̓ǂݍ���
            var config = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json")
                .Build();

            // �K�v�Ȑݒ�l�����ϐ��Ƃ��ēo�^
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

            // DbContext�̐ݒ�
            builder.Services.AddDbContext<ApplicationDbContext>(options =>
                options.UseNpgsql(databaseUrl));

            var app = builder.Build();

            // �J�����ł̂�Swagger��L����
            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            // CORS�|���V�[��K�p
            app.UseCors("CorsPolicy");


            // Referer��ݒ肷�鋤�ʃN���C�A���g�ݒ�
            dynamic CreateClientConfiguration(Func<Configuration, dynamic> configSelector, HttpContext context)
            {
                var config = new Configuration();
                var clientConfig = configSelector(config);

                if (context.Request.Headers.TryGetValue("Referer", out var referer))
                {
                    clientConfig.SetReferer(referer.ToString());
                }

                return clientConfig;
            }

            // Bearer�g�[�N�����擾
            string? GetBearerToken(HttpContext context)
            {
                if (context.Request.Headers.TryGetValue("Authorization", out var authHeader) &&
                    authHeader.ToString().StartsWith("Bearer "))
                {
                    return authHeader.ToString().Substring("Bearer ".Length).Trim();
                }
                return null;
            }

            // �G���[�n���h�����O���ʏ���
            IResult HandleApiException(Exception ex)
            {
                if (ex is authapi.Client.ApiException authApiEx)
                {
                    return Results.Problem(detail: authApiEx.Message, statusCode: (int)authApiEx.ErrorCode);
                }
                else if (ex is billingapi.Client.ApiException billingApiEx)
                {
                    return Results.Problem(detail: billingApiEx.Message, statusCode: (int)billingApiEx.ErrorCode);
                }
                return Results.Problem(detail: ex.Message, statusCode: 500);
            }


            app.MapGet("/credentials", async (HttpContext context, string code) =>
            {
                if (string.IsNullOrEmpty(code))
                {
                    return Results.BadRequest("The 'code' query parameter is missing.");
                }

                try
                {
                    var authApiClientConfig = CreateClientConfiguration(c => c.GetAuthApiClientConfig(), context);
                    var credentialApi = new CredentialApi(authApiClientConfig);
                    var credentials = await credentialApi.GetAuthCredentialsAsync(code, "tempCodeAuth", null);

                    // JSON�`���Ń��X�|���X��Ԃ�
                    var jsonResponse = credentials.ToJson(); // SDK ���񋟂��� ToJson ���\�b�h�𗘗p
                    return Results.Text(jsonResponse, "application/json");
                }
                catch (Exception ex)
                {
                    return HandleApiException(ex);
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
                    var authApiClientConfig = CreateClientConfiguration(c => c.GetAuthApiClientConfig(), context);
                    var credentialApi = new CredentialApi(authApiClientConfig);
                    var credentials = await credentialApi.GetAuthCredentialsAsync(null, "refreshTokenAuth", refreshToken);

                    // JSON�`���Ń��X�|���X��Ԃ�
                    var jsonResponse = credentials.ToJson(); // SDK ���񋟂��� ToJson ���\�b�h�𗘗p
                    return Results.Text(jsonResponse, "application/json");
                }
                catch (Exception ex)
                {
                    return HandleApiException(ex);
                }
            });

            app.MapGet("/userinfo", async (HttpContext context) =>
            {
                var token = GetBearerToken(context);
                if (string.IsNullOrEmpty(token))
                {
                    return Results.Unauthorized();
                }

                try
                {
                    var authApiClientConfig = CreateClientConfiguration(c => c.GetAuthApiClientConfig(), context);
                    var userInfoApi = new UserInfoApi(authApiClientConfig);
                    var userInfo = await userInfoApi.GetUserInfoAsync(token);

                    // JSON�`���Ń��X�|���X��Ԃ�
                    var jsonResponse = userInfo.ToJson(); // SDK ���񋟂��� ToJson ���\�b�h�𗘗p
                    return Results.Text(jsonResponse, "application/json");
                }
                catch (Exception ex)
                {
                    return HandleApiException(ex);
                }
            });

            app.MapGet("/users", async (HttpContext context) =>
            {
                var token = GetBearerToken(context);
                if (string.IsNullOrEmpty(token))
                {
                    return Results.Unauthorized();
                }

                try
                {
                    var authApiClientConfig = CreateClientConfiguration(c => c.GetAuthApiClientConfig(), context);
                    var userInfoApi = new UserInfoApi(authApiClientConfig);
                    var userInfo = await userInfoApi.GetUserInfoAsync(token);

                    if (userInfo.Tenants == null || !userInfo.Tenants.Any())
                    {
                        return Results.BadRequest("No tenant information available.");
                    }

                    // �N�G���p�����[�^���� tenant_id ���擾
                    var tenantId = context.Request.Query["tenant_id"].ToString();
                    if (string.IsNullOrEmpty(tenantId))
                    {
                        Console.Error.WriteLine("tenant_id query parameter is missing");
                        return Results.BadRequest("tenant_id query parameter is required");
                    }

                    // ���[�U�[���������Ă���e�i���g���m�F
                    var isBelongingTenant = userInfo.Tenants.Any(t => t.Id == tenantId);
                    if (!isBelongingTenant)
                    {
                        Console.Error.WriteLine($"Tenant {tenantId} does not belong to user");
                        return Results.Problem("Tenant that does not belong", statusCode: 403);
                    }


                    // �e�i���g���[�U�[���擾
                    var tenantUserApi = new TenantUserApi(authApiClientConfig);
                    var users = await tenantUserApi.GetTenantUsersAsync(tenantId);
                    if (users == null || users.VarUsers == null)
                    {
                        Console.Error.WriteLine("Failed to get saas users: response is null");
                        return Results.Problem("Internal server error", statusCode: 500);
                    }

                    // JSON�`���Ń��X�|���X��Ԃ�
                    var jsonResponse = JsonConvert.SerializeObject(users.VarUsers); // Newtonsoft.Json �𗘗p
                    return Results.Text(jsonResponse, "application/json");
                }
                catch (Exception ex)
                {
                    return HandleApiException(ex);
                }
            });

            app.MapGet("/tenant_attributes", async (HttpContext context) =>
            {
                var token = GetBearerToken(context);
                if (string.IsNullOrEmpty(token))
                {
                    return Results.Unauthorized();
                }

                try
                {

                    var authApiClientConfig = CreateClientConfiguration(c => c.GetAuthApiClientConfig(), context);
                    var userInfoApi = new UserInfoApi(authApiClientConfig);
                    var userInfo = await userInfoApi.GetUserInfoAsync(token);
                    if (userInfo.Tenants == null || !userInfo.Tenants.Any())
                    {
                        return Results.BadRequest("No tenant information available.");
                    }

                    // �N�G���p�����[�^���� tenant_id ���擾
                    var tenantId = context.Request.Query["tenant_id"].ToString();
                    if (string.IsNullOrEmpty(tenantId))
                    {
                        Console.Error.WriteLine("tenant_id query parameter is missing");
                        return Results.BadRequest("tenant_id query parameter is required");
                    }

                    // ���[�U�[���������Ă���e�i���g���m�F
                    var isBelongingTenant = userInfo.Tenants.Any(t => t.Id == tenantId);
                    if (!isBelongingTenant)
                    {
                        Console.Error.WriteLine($"Tenant {tenantId} does not belong to user");
                        return Results.Problem("Tenant that does not belong", statusCode: 403);
                    }

                    // �e�i���g�����̎擾
                    var tenantAttributeApi = new TenantAttributeApi(authApiClientConfig);
                    var tenantAttributes = await tenantAttributeApi.GetTenantAttributesAsync();

                    // �e�i���g���̎擾
                    var tenantApi = new TenantApi(authApiClientConfig);
                    var tenant = await tenantApi.GetTenantAsync(tenantId);

                    // ���ʂ��i�[���鎫��
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

                    // JSON�`���ŕԋp
                    return Results.Json(result);
                }
                catch (Exception ex)
                {
                    return HandleApiException(ex);
                }
            });

            app.MapGet("/user_attributes", async (HttpContext context) =>
            {
                var token = GetBearerToken(context);
                if (string.IsNullOrEmpty(token))
                {
                    return Results.Unauthorized();
                }

                try
                {
                    var authApiClientConfig = CreateClientConfiguration(c => c.GetAuthApiClientConfig(), context);
                    var userAttributeApi = new UserAttributeApi(authApiClientConfig);
                    var userAttributes = await userAttributeApi.GetUserAttributesAsync();

                    // JSON�`���Ń��X�|���X��Ԃ�
                    var jsonResponse = userAttributes.ToJson(); // SDK ���񋟂��� ToJson ���\�b�h�𗘗p
                    return Results.Text(jsonResponse, "application/json");
                }
                catch (Exception ex)
                {
                    return HandleApiException(ex);
                }
            });

            app.MapGet("/pricing_plan", async (HttpContext context, string plan_id) =>
            {
                var token = GetBearerToken(context);
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
                    var pricingConfig = CreateClientConfiguration(c => c.GetPricingApiClientConfig(), context);
                    var pricingPlansApi = new PricingPlansApi(pricingConfig);
                    var plan = await pricingPlansApi.GetPricingPlanAsync(plan_id);

                    // JSON�`���Ń��X�|���X��Ԃ�
                    var jsonResponse = plan.ToJson(); // SDK ���񋟂��� ToJson ���\�b�h�𗘗p
                    return Results.Text(jsonResponse, "application/json");
                }
                catch (Exception ex)
                {
                    return HandleApiException(ex);
                }
            });


            app.MapPost("/user_register", async ([FromBody] UserRegisterRequest requestBody, HttpContext context) =>
            {
                var token = GetBearerToken(context);
                if (string.IsNullOrEmpty(token))
                {
                    return Results.Unauthorized();
                }

                // �o���f�[�V�����̎��s
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
                    // ���[�U�[���̎擾
                    var authApiClientConfig = CreateClientConfiguration(c => c.GetAuthApiClientConfig(), context);
                    var userInfoApi = new UserInfoApi(authApiClientConfig);
                    var userInfo = await userInfoApi.GetUserInfoAsync(token);
                    if (userInfo.Tenants == null || !userInfo.Tenants.Any())
                    {
                        return Results.BadRequest("No tenant information available.");
                    }

                    // ���[�U�[���������Ă���e�i���g���m�F
                    var isBelongingTenant = userInfo.Tenants.Any(t => t.Id == tenantId);
                    if (!isBelongingTenant)
                    {
                        Console.Error.WriteLine($"Tenant {tenantId} does not belong to user");
                        return Results.Problem("Tenant that does not belong", statusCode: 403);
                    }


                    // ���[�U�[�����̎擾
                    var userAttributeApi = new UserAttributeApi(authApiClientConfig);
                    var userAttributes = await userAttributeApi.GetUserAttributesAsync();

                    // �����̌^�ɉ���������
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
                                return Results.BadRequest(new { error = "Invalid attribute value" });
                            }
                        }
                    }

                    // SaaS ���[�U�[�̓o�^
                    var saasUserApi = new SaasUserApi(authApiClientConfig);
                    var createSaasUserParam = new CreateSaasUserParam(email, password);
                    await saasUserApi.CreateSaasUserAsync(createSaasUserParam);

                    // �e�i���g���[�U�[�̓o�^
                    var tenantUserApi = new TenantUserApi(authApiClientConfig);
                    var createTenantUserParam = new CreateTenantUserParam(email, userAttributeValues);
                    var tenantUser = await tenantUserApi.CreateTenantUserAsync(tenantId, createTenantUserParam);

                    // ���[���̎擾
                    var roleApi = new RoleApi(authApiClientConfig);
                    var roles = await roleApi.GetRolesAsync();

                    // ���[���̐ݒ�
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
                    Console.Error.WriteLine($"Error: {ex.Message}");
                    return Results.Problem(detail: ex.Message, statusCode: 500);
                }
            });

            app.MapDelete("/user_delete", async ([FromBody] UserDeleteRequest requestBody, HttpContext context, ApplicationDbContext dbContext) =>
            {
                var token = GetBearerToken(context);
                if (string.IsNullOrEmpty(token))
                {
                    return Results.Unauthorized();
                }

                // �o���f�[�V�����̎��s
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
                    // ���[�U�[���̎擾
                    var authApiClientConfig = CreateClientConfiguration(c => c.GetAuthApiClientConfig(), context);
                    var userInfoApi = new UserInfoApi(authApiClientConfig);
                    var userInfo = userInfoApi.GetUserInfo(token);
                    if (userInfo.Tenants == null || !userInfo.Tenants.Any())
                    {
                        return Results.BadRequest("No tenant information available.");
                    }

                    // ���[�U�[���������Ă���e�i���g���m�F
                    var isBelongingTenant = userInfo.Tenants.Any(t => t.Id == tenantId);
                    if (!isBelongingTenant)
                    {
                        Console.Error.WriteLine($"Tenant {tenantId} does not belong to user");
                        return Results.Problem("Tenant that does not belong", statusCode: 403);
                    }

                    // ���[�U�[�폜����
                    // SaaSus���烆�[�U�[�����擾
                    var tenantUserApi = new TenantUserApi(authApiClientConfig);
                    var deleteUser = tenantUserApi.GetTenantUser(tenantId, userId);
                    await tenantUserApi.DeleteTenantUserAsync(tenantId, userId);

                    // �f�[�^�x�[�X�ɍ폜���O��ۑ�
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
                    return HandleApiException(ex);
                }
            });

            app.MapGet("/delete_user_log", async (HttpContext context, ApplicationDbContext dbContext) =>
            {
                // �N�G���p�����[�^���� `tenant_id` ���擾
                var tenantId = context.Request.Query["tenant_id"].ToString();
                if (string.IsNullOrEmpty(tenantId))
                {
                    return Results.BadRequest(new { error = "tenant_id query parameter is required" });
                }

                // ���[�U�[�����擾
                var token = GetBearerToken(context);
                if (string.IsNullOrEmpty(token))
                {
                    return Results.Unauthorized();
                }

                try
                {
                    var authApiClientConfig = CreateClientConfiguration(c => c.GetAuthApiClientConfig(), context);
                    var userInfoApi = new UserInfoApi(authApiClientConfig);
                    var userInfo = await userInfoApi.GetUserInfoAsync(token);

                    if (userInfo.Tenants == null || !userInfo.Tenants.Any())
                    {
                        return Results.BadRequest(new { error = "No tenants found for the user" });
                    }

                    // �w�肳�ꂽ�e�i���gID�����[�U�[�̏�������e�i���g�Ɋ܂܂�邩�m�F
                    var isBelongingTenant = userInfo.Tenants.Any(t => t.Id == tenantId);
                    if (!isBelongingTenant)
                    {
                        return Results.BadRequest(new { error = "Tenant that does not belong" });
                    }

                    // �f�[�^�x�[�X����폜���O���擾
                    var deleteUserLogs = await dbContext.DeleteUserLogs
                        .Where(log => log.TenantId == tenantId)
                        .ToListAsync();

                    // ���X�|���X�f�[�^�̐��`
                    var responseData = deleteUserLogs.Select(log => new
                    {
                        id = log.Id,
                        tenant_id = log.TenantId,
                        user_id = log.UserId,
                        email = log.Email,
                        delete_at = log.DeleteAt.ToString("o") // ISO 8601�`���ɕϊ�
                    });

                    return Results.Ok(responseData);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Error: {ex.Message}");
                    return Results.Problem(detail: ex.Message, statusCode: 500);
                }
            });
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

    // DbContext��`
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