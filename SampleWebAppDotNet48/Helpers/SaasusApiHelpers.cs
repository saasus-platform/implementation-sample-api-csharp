using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Web.Http;
using authapi.Api;
using pricingapi.Api;
using modules;

namespace SampleWebAppDotNet48.Helpers
{
  /// <summary>
  /// SaaSus 共通ヘルパー
  /// </summary>
  public static class SaasusApiHelpers
  {
    /* ------------------------------------------------------------
     * 1. クライアント生成（Referer 自動セット）
     * ---------------------------------------------------------- */
    public static T CreateClientConfiguration<T>(
        Func<Configuration, T> selector,
        HttpRequestMessage request) where T : class
    {
      var cfg = new Configuration();
      var client = selector(cfg);

      // X‑Saasus‑Referer → Referer の順で参照
      if (request.Headers.TryGetValues("X-Saasus-Referer", out var saasusRef) &&
          saasusRef.Any())
      {
        ((dynamic)client).SetReferer(saasusRef.First());
      }
      else if (request.Headers.Referrer != null)
      {
        ((dynamic)client).SetReferer(request.Headers.Referrer.ToString());
      }

      return client;
    }

    /* ------------------------------------------------------------
     * 2. Bearer トークン取得
     * ---------------------------------------------------------- */
    public static string GetBearerToken(HttpRequestMessage request)
    {
      var auth = request.Headers.Authorization;

      if (auth != null &&
          auth.Scheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase) &&
          !string.IsNullOrWhiteSpace(auth.Parameter))
      {
        return auth.Parameter.Trim();
      }

      // トークンが無ければ 401 を投げる
      throw new HttpResponseException(HttpStatusCode.Unauthorized);
    }

    /* ------------------------------------------------------------
     * 3. 共通エラーハンドリング
     *    ApiController から呼び出すことを想定
     * ---------------------------------------------------------- */
    public static (HttpStatusCode, object) HandleApiException(Exception ex)
    {
      switch (ex)
      {
        case authapi.Client.ApiException a:
          Debug.WriteLine($"Auth API Error: {a.ErrorCode} - {a.Message}");
          return ((HttpStatusCode)a.ErrorCode, new { error = a.Message });

        case pricingapi.Client.ApiException p:
          Debug.WriteLine($"Pricing API Error: {p.ErrorCode} - {p.Message}");
          return ((HttpStatusCode)p.ErrorCode, new { error = p.Message });

        default:
          Debug.WriteLine($"Unhandled Error: {ex.Message}");
          return (HttpStatusCode.InternalServerError, new { error = ex.Message });
      }
    }
  }
}
