using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using modules;

namespace SampleWebAppDotNet8.Helpers
{
  public static class SaasusApiHelpers
  {
    /// <summary>
    /// SaaSus SDK の各種 ApiClientConfig を取得し、
    /// Referer が来ていれば SetReferer も済ませて返す共通ヘルパー
    /// </summary>
    public static T CreateClientConfiguration<T>(
        Func<Configuration, T> selector,
        HttpContext ctx) where T : class
    {
      var cfg = new Configuration();
      var client = selector(cfg);

      // 優先: X-Saasus-Referer → 次点: Referer
      if (ctx.Request.Headers.TryGetValue("X-Saasus-Referer", out var saasusRef))
      {
        ((dynamic)client).SetReferer(saasusRef.ToString());
      }
      else if (ctx.Request.Headers.TryGetValue("Referer", out var stdRef))
      {
        ((dynamic)client).SetReferer(stdRef.ToString());
      }

      return client;
    }

    /// <summary>
    /// Authorization: Bearer &lt;token&gt; 形式からトークンだけを取り出す
    /// </summary>
    public static string? GetBearerToken(HttpContext context)
    {
      if (context.Request.Headers.TryGetValue("Authorization", out var authHeader) &&
          authHeader.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
      {
        return authHeader.ToString().Substring("Bearer ".Length).Trim();
      }
      return null;
    }

  }
}
