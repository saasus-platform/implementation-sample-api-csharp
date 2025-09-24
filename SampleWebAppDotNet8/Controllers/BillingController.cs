using Microsoft.AspNetCore.Mvc;
using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using SampleWebAppDotNet8.Helpers;

using authapi.Api;
using authapi.Model;
using pricingapi.Api;
using pricingapi.Model;
using modules;

namespace SampleWebAppDotNet8.Controllers
{
  [ApiController]
  public class BillingController : ControllerBase
  {
    private readonly IConfiguration _configuration;
    private readonly ILogger<BillingController> _logger;

    public BillingController(IConfiguration configuration, ILogger<BillingController> logger)
    {
      _configuration = configuration;
      _logger = logger;
    }

    // 課金アクセス権限チェック
    private bool HasBillingAccess(UserInfo userInfo, string tenantId)
    {
      if (userInfo.Tenants == null || !userInfo.Tenants.Any())
        return false;

      foreach (var tenant in userInfo.Tenants)
      {
        if (tenant.Id != tenantId)
          continue;

        foreach (var env in tenant.Envs)
        {
          foreach (var role in env.Roles)
          {
            if (role.RoleName == "admin" || role.RoleName == "sadmin")
            {
              return true;
            }
          }
        }
      }
      return false;
    }

    // ユニットタイプ別課金額計算
    private double CalculateAmountByUnitType(double count, object unit)
    {
      if (unit is PricingFixedUnit fixedUnit)
      {
        return (double)fixedUnit.UnitAmount;
      }
      else if (unit is PricingUsageUnit usageUnit)
      {
        return (double)usageUnit.UnitAmount * count;
      }
      else if (unit is PricingTieredUnit tieredUnit)
      {
        return CalcTiered(count, tieredUnit);
      }
      else if (unit is PricingTieredUsageUnit tieredUsageUnit)
      {
        return CalcTieredUsage(count, tieredUsageUnit);
      }
      return 0.0;
    }

    // 階層料金計算
    private double CalcTiered(double count, PricingTieredUnit unit)
    {
      var tiers = unit.Tiers;
      if (tiers == null || !tiers.Any())
      {
        return 0.0;
      }

      foreach (var tier in tiers)
      {
        bool inf = tier.Inf == true;
        int upTo = tier.UpTo > 0 ? tier.UpTo : int.MaxValue;
        double flat = (double)tier.FlatAmount;
        double price = (double)tier.UnitAmount;

        if (inf || count <= upTo)
        {
          return flat + count * price;
        }
      }

      return 0.0;
    }

    // 階層使用量料金計算
    private double CalcTieredUsage(double count, PricingTieredUsageUnit unit)
    {
      var tiers = unit.Tiers;
      if (tiers == null || !tiers.Any())
      {
        return 0.0;
      }

      double total = 0.0;
      double prev = 0.0;

      foreach (var tier in tiers)
      {
        bool inf = tier.Inf == true;
        int upTo = tier.UpTo > 0 ? tier.UpTo : int.MaxValue;
        double flat = (double)tier.FlatAmount;
        double price = (double)tier.UnitAmount;

        if (count <= prev)
        {
          break;
        }

        double usage = inf ? (count - prev) : Math.Min(count, upTo) - prev;
        total += flat + usage * price;
        prev = upTo;

        if (inf)
        {
          break;
        }
      }

      return total;
    }

    /// <summary>
    /// プランに含まれるすべてのメータリングユニットについて
    /// 「利用量取得 → 金額算出 → 通貨別合計」を返す共通処理
    /// </summary>
    private async Task<(List<Dictionary<string, object>> Billings,
                       List<Dictionary<string, object>> Totals)>
    CalculateMeteringUnitBillingsAsync(
        string tenantId,
        long periodStart,
        long periodEnd,
        PricingPlan plan,
        PricingApiClientConfig pricingClient)
    {
      var meteringApi = new MeteringApi(pricingClient);
      var billings = new List<Dictionary<string, object>>();
      var currencySum = new ConcurrentDictionary<string, double>();
      var usageCache = new ConcurrentDictionary<string, double>();

      foreach (var menu in plan.PricingMenus ?? Enumerable.Empty<PricingMenu>())
      {
        string menuName = menu.DisplayName ?? "";

        foreach (var unitObj in menu.Units ?? Enumerable.Empty<PricingUnit>())
        {
          object inst = unitObj.ActualInstance;
          string? unitName = null;
          string unitType = "fixed";
          string agg = "sum";
          string currencyStr = "JPY";
          string? dispName = null;

          // ---------- 型ごとにプロパティ抽出 ----------
          switch (inst)
          {
            /* ---------- fixed ---------- */
            case PricingFixedUnit u:
              unitType = u.Type.ToString().ToLower();
              currencyStr = u.Currency.ToString();
              dispName = u.DisplayName;
              break;

            /* ---------- usage ---------- */
            case PricingUsageUnit u:
              unitName = u.MeteringUnitName;
              unitType = u.Type.ToString().ToLower();
              agg = u.AggregateUsage == AggregateUsage.Max ? "max" : "sum";
              currencyStr = u.Currency.ToString();
              dispName = u.DisplayName;
              break;

            /* ---------- tiered ---------- */
            case PricingTieredUnit u:
              unitName = u.MeteringUnitName;
              unitType = u.Type.ToString().ToLower();
              agg = u.AggregateUsage == AggregateUsage.Max ? "max" : "sum";
              currencyStr = u.Currency.ToString();
              dispName = u.DisplayName;
              break;

            /* ---------- tiered_usage ---------- */
            case PricingTieredUsageUnit u:
              unitName = u.MeteringUnitName;
              unitType = u.Type.ToString().ToLower();
              agg = u.AggregateUsage == AggregateUsage.Max ? "max" : "sum";
              currencyStr = u.Currency.ToString();
              dispName = u.DisplayName;
              break;
          }


          // ---------- 使用量取得 ----------
          double count = 0.0;
          if (unitType != "fixed")
          {
            if (unitName == null)
            {
              // Skip this unit if unitName is null to prevent runtime exceptions
              continue;
            }
            if (!usageCache.TryGetValue(unitName, out count))
            {
              var resp = await meteringApi
                  .GetMeteringUnitDateCountByTenantIdAndUnitNameAndDatePeriodAsync(
                      tenantId, unitName, (int)periodStart, (int)periodEnd);

              count = agg == "max"
                    ? resp.Counts.Max(c => (double)c.Count)
                    : resp.Counts.Sum(c => (double)c.Count);

              usageCache[unitName] = count;
            }
          }

          // ---------- 金額計算 ----------
          double amount = CalculateAmountByUnitType(count, inst);
          currencySum.AddOrUpdate(currencyStr, amount, (_, old) => old + amount);

          billings.Add(new Dictionary<string, object>
          {
            ["metering_unit_name"] = unitName ?? "",
            ["metering_unit_type"] = unitType,
            ["function_menu_name"] = menuName,
            ["period_count"] = count,
            ["currency"] = currencyStr,
            ["period_amount"] = amount,
            ["pricing_unit_display_name"] = dispName ?? ""
          });
        }
      }

      var totals = currencySum
          .OrderBy(kvp => kvp.Key)
          .Select(kvp => new Dictionary<string, object>
          {
            ["currency"] = kvp.Key,
            ["total_amount"] = kvp.Value
          })
          .ToList();

      return (billings, totals);
    }

    /// <summary>
    /// 課金ダッシュボード取得
    /// </summary>
    [HttpGet("/billing/dashboard")]
    public async Task<IActionResult> GetBillingDashboard(
        [FromQuery(Name = "tenant_id")] string tenantId,
        [FromQuery(Name = "plan_id")] string planId,
        [FromQuery(Name = "period_start")] int start,
        [FromQuery(Name = "period_end")] int end)
    {
      try
      {
        // 1. クライアント初期化
        var authApiClientConfig = SaasusApiHelpers.CreateClientConfiguration(c => c.GetAuthApiClientConfig(), HttpContext);
        var pricingConfig = SaasusApiHelpers.CreateClientConfiguration(c => c.GetPricingApiClientConfig(), HttpContext);

        // 2. 認証・認可チェック
        var userInfoApi = new UserInfoApi(authApiClientConfig);
        var idToken = SaasusApiHelpers.GetBearerToken(HttpContext);
        if (string.IsNullOrEmpty(idToken))
          return Unauthorized();
        var userInfo = await userInfoApi.GetUserInfoAsync(idToken);


        if (!HasBillingAccess(userInfo, tenantId))
        {
          return StatusCode(403, new { error = "Insufficient permissions" });
        }

        // プラン取得
        var plansApi = new PricingPlansApi(pricingConfig);
        var plan = await plansApi.GetPricingPlanAsync(planId);
        // テナント取得
        var tenantApi = new TenantApi(authApiClientConfig);
        var tenant = await tenantApi.GetTenantAsync(tenantId);

        // 税率取得
        TaxRate? matchedTax = null;
        var history = tenant.PlanHistories?
            .OrderByDescending(h => h.PlanAppliedAt)
            .FirstOrDefault(h => h.PlanId == planId && h.PlanAppliedAt <= start);

        if (history?.TaxRateId != null)
        {
          var taxRatesApi = new TaxRateApi(pricingConfig);
          var taxRates = await taxRatesApi.GetTaxRatesAsync();
          matchedTax = taxRates.VarTaxRates.FirstOrDefault(t => t.Id == history.TaxRateId);
        }

        // 使用量取得と集計
        var (billings, totals) = await CalculateMeteringUnitBillingsAsync(
            tenantId, start, end, plan, pricingConfig);

        // 6. summary と pricing plan 情報
        var summary = new Dictionary<string, object>
        {
          ["total_by_currency"] = totals,
          ["total_metering_units"] = billings.Count
        };

        var pricingPlanInfo = new Dictionary<string, object>
        {
          ["plan_id"] = planId,
          ["display_name"] = plan.DisplayName,
          ["description"] = plan.Description
        };

        // 7. レスポンス構築
        var response = new Dictionary<string, object?>
        {
          ["summary"] = summary,
          ["metering_unit_billings"] = billings,
          ["pricing_plan_info"] = pricingPlanInfo,
          ["tax_rate"] = matchedTax
        };

        return Ok(response);
      }
      catch (Exception ex)
      {
        return SaasusApiHelpers.HandleApiExceptionMvc(ex, this);
      }
    }

    /// <summary>
    /// プラン期間取得
    /// </summary>
    [HttpGet("/billing/plan_periods")]
    public async Task<IActionResult> GetPlanPeriods([FromQuery(Name = "tenant_id")] string tenantId)
    {
      try
      {
        // 0. 入力チェック
        if (string.IsNullOrWhiteSpace(tenantId))
        {
          return BadRequest(new { detail = "tenant_id required" });
        }

        // 1. APIクライアント初期化
        var config = new Configuration();
        var authClient = config.GetAuthApiClientConfig();
        var pricingClient = config.GetPricingApiClientConfig();

        // 2. 認証と権限チェック
        var userInfoApi = new UserInfoApi(authClient);
        var idToken = SaasusApiHelpers.GetBearerToken(HttpContext);
        if (string.IsNullOrEmpty(idToken))
          return Unauthorized();
        var userInfo = await userInfoApi.GetUserInfoAsync(idToken);

        if (!HasBillingAccess(userInfo, tenantId))
        {
          return StatusCode(403, new { error = "Insufficient permissions" });
        }

        // 3. テナント情報取得とタイムゾーン指定
        var tenantApi = new TenantApi(authClient);
        var tenant = await tenantApi.GetTenantAsync(tenantId);

        var zone = TimeZoneInfo.FindSystemTimeZoneById("Tokyo Standard Time");

        // 4. 境界点（plan history）を昇順に整列
        var sortedHistories = tenant.PlanHistories.OrderBy(h => h.PlanAppliedAt).ToList();

        var results = new List<Dictionary<string, object>>();
        var plansApi = new PricingPlansApi(pricingClient);

        // 5. プラン期間終了点を決定
        var currentEnd = tenant.CurrentPlanPeriodEnd;
        long fixedLastEpoch = currentEnd > 0 ? currentEnd : DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // 6. 境界ごとに分割して区間リストを作成
        for (int i = 0; i < sortedHistories.Count; i++)
        {
          var hist = sortedHistories[i];
          var planId = hist.PlanId;
          if (string.IsNullOrEmpty(planId))
          {
            continue;
          }

          // プラン取得
          var plan = await plansApi.GetPricingPlanAsync(planId);

          // 開始・終了日時
          long startEpoch = hist.PlanAppliedAt > 0 ? hist.PlanAppliedAt : 0L;
          long endEpoch = (i + 1 < sortedHistories.Count)
              ? (sortedHistories[i + 1].PlanAppliedAt > 0 ? sortedHistories[i + 1].PlanAppliedAt : 0L) - 1
              : fixedLastEpoch;

          var periodStart = DateTimeOffset.FromUnixTimeSeconds(startEpoch).ToOffset(TimeSpan.FromHours(9)).DateTime;
          var periodEnd = DateTimeOffset.FromUnixTimeSeconds(endEpoch).ToOffset(TimeSpan.FromHours(9)).DateTime;

          // 年払いプランかどうか判定
          bool isYearly = PlanHasYearUnit(plan);

          // 区間を step（1か月/1年）単位で分割
          var cur = periodStart;
          while (cur <= periodEnd)
          {
            var next = isYearly ? cur.AddYears(1) : cur.AddMonths(1);
            var segEnd = next.AddSeconds(-1);
            if (segEnd > periodEnd)
              segEnd = periodEnd;
            if (segEnd <= cur)
              break;

            var segment = new Dictionary<string, object>
            {
              ["label"] = $"{cur:yyyy年MM月dd日 HH:mm:ss} ～ {segEnd:yyyy年MM月dd日 HH:mm:ss}",
              ["plan_id"] = planId,
              ["start"] = new DateTimeOffset(cur, TimeSpan.FromHours(9)).ToUnixTimeSeconds(),
              ["end"] = new DateTimeOffset(segEnd, TimeSpan.FromHours(9)).ToUnixTimeSeconds()
            };
            results.Add(segment);

            if (segEnd == periodEnd)
              break;
            cur = segEnd.AddSeconds(1);
          }
        }

        // 7. start DESC でソート
        results.Sort((o1, o2) =>
        {
          var s1 = Convert.ToInt64(o1["start"]);
          var s2 = Convert.ToInt64(o2["start"]);
          return s2.CompareTo(s1); // 降順
        });

        return Ok(results);
      }
      catch (Exception ex)
      {
        return SaasusApiHelpers.HandleApiExceptionMvc(ex, this);
      }
    }

    // プランに年単位があるかチェック
    private bool PlanHasYearUnit(PricingPlan plan)
    {
      try
      {
        foreach (var menu in plan.PricingMenus ?? Enumerable.Empty<PricingMenu>())
        {
          foreach (var unit in menu.Units ?? Enumerable.Empty<PricingUnit>())
          {
            if (ExtractRecurringInterval(unit) == RecurringInterval.Year)
            {
              return true;   // 年単位のユニットを発見
            }
          }
        }
      }
      catch (Exception e)
      {
        _logger.LogError(e, "[PlanHasYearUnit] Error occurred while checking for year unit in plan");
      }
      return false;
    }

    // 課金間隔を抽出
    private RecurringInterval ExtractRecurringInterval(PricingUnit unit) =>
        unit.ActualInstance switch
        {
          PricingFixedUnit f => f.RecurringInterval,
          PricingUsageUnit u => u.RecurringInterval,
          PricingTieredUnit t => t.RecurringInterval,
          PricingTieredUsageUnit tu => tu.RecurringInterval,
          _ => RecurringInterval.Month  // デフォルト値
        };

    /// <summary>
    /// メータリング数更新
    /// </summary>
    [HttpPost("/billing/metering/{tenantId}/{unit}/{ts}")]
    public async Task<IActionResult> UpdateMeteringCount(
        [FromRoute] string tenantId,
        [FromRoute] string unit,
        [FromRoute] long ts,
        [FromBody] UpdateMeteringCountRequest body)
    {
      try
      {
        // 1. 権限チェック
        var authApiClientConfig = SaasusApiHelpers.CreateClientConfiguration(c => c.GetAuthApiClientConfig(), HttpContext);

        var userInfoApi = new UserInfoApi(authApiClientConfig);
        var idToken = SaasusApiHelpers.GetBearerToken(HttpContext);
        if (string.IsNullOrEmpty(idToken))
          return Unauthorized();
        var userInfo = await userInfoApi.GetUserInfoAsync(idToken);

        if (!HasBillingAccess(userInfo, tenantId))
        {
          return StatusCode(403, new { error = "Insufficient permissions" });
        }

        // 2. 入力バリデーション
        var method = body.Method;
        var count = body.Count;

        var allowedMethods = new[] { "add", "sub", "direct" };
        if (!allowedMethods.Contains(method))
        {
          return BadRequest(new { error = "Invalid method: must be one of add, sub, direct" });
        }
        if (count < 0)
        {
          return BadRequest(new { error = "Count must be >= 0" });
        }

        // 3. SaaSus API 呼び出し
        var pricingConfig = SaasusApiHelpers.CreateClientConfiguration(c => c.GetPricingApiClientConfig(), HttpContext);
        var meteringApi = new MeteringApi(pricingConfig);
        var param = new UpdateMeteringUnitTimestampCountParam
        {
          Method = method switch
          {
            "add" => UpdateMeteringUnitTimestampCountMethod.Add,
            "sub" => UpdateMeteringUnitTimestampCountMethod.Sub,
            "direct" => UpdateMeteringUnitTimestampCountMethod.Direct,
            _ => UpdateMeteringUnitTimestampCountMethod.Add
          },
          Count = count
        };

        var meteringCount = await meteringApi.UpdateMeteringUnitTimestampCountAsync(
            tenantId, unit, (int)ts, param);

        return Ok(meteringCount);
      }
      catch (Exception ex)
      {
        return SaasusApiHelpers.HandleApiExceptionMvc(ex, this);
      }
    }

    /// <summary>
    /// 現在時刻（UTC now）のタイムスタンプでメータリング数を更新する
    /// POST /metering/{tenantId}/{unit}
    /// </summary>
    [HttpPost("/billing/metering/{tenantId}/{unit}")]
    public async Task<IActionResult> UpdateMeteringCountNow(
        [FromRoute] string tenantId,
        [FromRoute] string unit,
        [FromBody] UpdateMeteringCountRequest body)
    {
      try
      {
        /* ---------- 1. 権限チェック ---------- */
        var authApiClientConfig = SaasusApiHelpers.CreateClientConfiguration(c => c.GetAuthApiClientConfig(), HttpContext);
        var userInfoApi = new UserInfoApi(authApiClientConfig);
        var idToken = SaasusApiHelpers.GetBearerToken(HttpContext);
        if (string.IsNullOrEmpty(idToken))
          return Unauthorized();
        var userInfo = await userInfoApi.GetUserInfoAsync(idToken);

        if (!HasBillingAccess(userInfo, tenantId))
          return StatusCode(403, new { error = "Insufficient permissions" });

        /* ---------- 2. 入力バリデーション ---------- */
        var allowed = new[] { "add", "sub", "direct" };
        if (!allowed.Contains(body.Method))
          return BadRequest(new { error = "Method must be add | sub | direct" });

        if (body.Count < 0)
          return BadRequest(new { error = "Count must be >= 0" });

        /* ---------- 3. SaaSus API 呼び出し ---------- */
        var pricingConfig = SaasusApiHelpers.CreateClientConfiguration(c => c.GetPricingApiClientConfig(), HttpContext);
        var meteringApi = new MeteringApi(pricingConfig);

        // 現在時刻（秒単位, UTC）をタイムスタンプとして使用
        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var param = new UpdateMeteringUnitTimestampCountParam
        {
          Method = body.Method switch
          {
            "add" => UpdateMeteringUnitTimestampCountMethod.Add,
            "sub" => UpdateMeteringUnitTimestampCountMethod.Sub,
            "direct" => UpdateMeteringUnitTimestampCountMethod.Direct,
            _ => UpdateMeteringUnitTimestampCountMethod.Add
          },
          Count = body.Count
        };

        var result = await meteringApi.UpdateMeteringUnitTimestampCountAsync(
            tenantId, unit, (int)ts, param);

        return Ok(result);
      }
      catch (Exception ex)
      {
        return SaasusApiHelpers.HandleApiExceptionMvc(ex, this);
      }
    }

  }

  /// <summary>
  /// メータリング数更新リクエスト
  /// </summary>
  public class UpdateMeteringCountRequest
  {
    [Required(ErrorMessage = "Method is required")]
    [RegularExpression("^(add|sub|direct)$", ErrorMessage = "Method must be one of: add, sub, direct")]
    public string Method { get; set; } = string.Empty;

    [Range(0, int.MaxValue, ErrorMessage = "Count must be greater than or equal to 0")]
    public int Count { get; set; }
  }
}