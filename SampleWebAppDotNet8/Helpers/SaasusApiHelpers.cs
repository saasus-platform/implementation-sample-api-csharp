using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using authapi.Api;   // Configuration クラスの名前空間
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
        return authHeader.ToString()["Bearer ".Length..].Trim();
      }
      return null;
    }

    /// <summary>
    /// SaaSus SDK の ApiException → HTTP レスポンス変換
    /// Minimal-API 用 IResult を返す版
    /// </summary>
    public static IResult HandleApiException(Exception ex)
    {
      return ex switch
      {
        authapi.Client.ApiException aex => Results.Problem(aex.Message,
                                                              statusCode: (int)aex.ErrorCode),
        pricingapi.Client.ApiException pex => Results.Problem(pex.Message,
                                                              statusCode: (int)pex.ErrorCode),
        /* 例外型を追加したいときは ↓ にケースを足すだけ */
        // billingapi.Client.ApiException bex => Results.Problem(bex.Message, (int)bex.ErrorCode),

        _ => Results.Problem(ex.Message, statusCode: 500)
      };
    }

    public static IActionResult HandleApiExceptionMvc(Exception ex, ControllerBase ctrl)
    {
        return ex switch
        {
            authapi.Client.ApiException a =>
                ctrl.Problem(detail: a.Message, statusCode: (int)a.ErrorCode),

            pricingapi.Client.ApiException p =>
                ctrl.Problem(detail: p.Message, statusCode: (int)p.ErrorCode),

            _ => ctrl.Problem(detail: ex.Message, statusCode: 500)
        };
    }
  }
}
