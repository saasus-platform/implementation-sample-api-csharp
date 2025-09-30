using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Web.Http;
using Newtonsoft.Json;
using SampleWebAppDotNet48.Helpers;

using authapi.Api;
using authapi.Model;
using pricingapi.Api;
using pricingapi.Model;
using modules;

namespace SampleWebAppDotNet48.Controllers
{
    [RoutePrefix("billing")]
    public class BillingController : ApiController
    {
        // ----------------------- 共通ユーティリティ -----------------------

        // アクセス権限チェック
        private bool HasBillingAccess(UserInfo u, string tenantId)
        {
            if (u.Tenants == null) return false;
            return u.Tenants
                    .Where(t => t.Id == tenantId)
                    .SelectMany(t => t.Envs)
                    .SelectMany(e => e.Roles)
                    .Any(r => r.RoleName == "admin" || r.RoleName == "sadmin");
        }

        // 階層料金計算
        private double CalcTiered(double count, PricingTieredUnit unit)
        {
            if (unit.Tiers == null || !unit.Tiers.Any()) return 0.0;

            foreach (var tier in unit.Tiers)
            {
                var isInf = tier.Inf == true;
                var upTo = tier.UpTo > 0 ? tier.UpTo : int.MaxValue;
                var flat = (double)tier.FlatAmount;
                var price = (double)tier.UnitAmount;

                if (isInf || count <= upTo)
                {
                    return flat + count * price;
                }
            }
            return 0.0;
        }

        // 階層使用量料金計算
        private double CalcTieredUsage(double count, PricingTieredUsageUnit unit)
        {
            if (unit.Tiers == null || !unit.Tiers.Any()) return 0.0;

            double total = 0.0;
            double prev = 0.0;

            foreach (var tier in unit.Tiers)
            {
                var isInf = tier.Inf == true;
                var upTo = tier.UpTo > 0 ? tier.UpTo : int.MaxValue;
                var flat = (double)tier.FlatAmount;
                var price = (double)tier.UnitAmount;

                if (count <= prev) break;

                var usage = (isInf ? count : Math.Min(count, upTo)) - prev;
                total += flat + usage * price;
                prev = upTo;
                if (isInf) break;
            }
            return total;
        }

        // ユニットタイプ別課金額計算
        private double CalculateAmountByUnitType(double count, object unitObj)
        {
            if (unitObj is PricingFixedUnit f)
            {
                return (double)f.UnitAmount;
            }
            else if (unitObj is PricingUsageUnit u)
            {
                return (double)u.UnitAmount * count;
            }
            else if (unitObj is PricingTieredUnit t)
            {
                return CalcTiered(count, t);
            }
            else if (unitObj is PricingTieredUsageUnit tu)
            {
                return CalcTieredUsage(count, tu);
            }
            else
            {
                return 0.0;
            }
        }

        /// <summary>
        /// プランに含まれるすべてのメータリングユニットについて
        /// 「利用量取得 → 金額算出 → 通貨別合計」を返す共通処理
        /// </summary>
        private async Task<(List<Dictionary<string, object>> billings,
                           List<Dictionary<string, object>> totals)>
            CalcMeteringUnitBillingsAsync(string tenantId,
                                          long periodStart,
                                          long periodEnd,
                                          PricingPlan plan,
                                          PricingApiClientConfig pricingCfg)
        {
            var meteringApi = new MeteringApi(pricingCfg);
            var billings = new List<Dictionary<string, object>>();
            var currencySum = new ConcurrentDictionary<string, double>();
            var usageCache = new ConcurrentDictionary<string, double>();

            foreach (var menu in plan.PricingMenus ?? Enumerable.Empty<PricingMenu>())
            {
                var menuName = menu.DisplayName ?? string.Empty;

                foreach (var unit in menu.Units ?? Enumerable.Empty<PricingUnit>())
                {
                    var inst = unit.ActualInstance;
                    string unitName = null;
                    string unitType = "fixed";
                    string agg = "sum";
                    string currency = "JPY";
                    string dispName = null;

                    // ---------- 型ごとにプロパティ抽出 ----------
                    if (inst is PricingFixedUnit fixedUnit)
                    {
                        unitType = fixedUnit.Type.ToString().ToLower();
                        currency = fixedUnit.Currency.ToString();
                        dispName = fixedUnit.DisplayName;
                    }
                    else if (inst is PricingUsageUnit usageUnit)
                    {
                        unitName = usageUnit.MeteringUnitName;
                        unitType = usageUnit.Type.ToString().ToLower();
                        agg = usageUnit.AggregateUsage == AggregateUsage.Max ? "max" : "sum";
                        currency = usageUnit.Currency.ToString();
                        dispName = usageUnit.DisplayName;
                    }
                    else if (inst is PricingTieredUnit tieredUnit)
                    {
                        unitName = tieredUnit.MeteringUnitName;
                        unitType = tieredUnit.Type.ToString().ToLower();
                        agg = tieredUnit.AggregateUsage == AggregateUsage.Max ? "max" : "sum";
                        currency = tieredUnit.Currency.ToString();
                        dispName = tieredUnit.DisplayName;
                    }
                    else if (inst is PricingTieredUsageUnit tieredUsageUnit)
                    {
                        unitName = tieredUsageUnit.MeteringUnitName;
                        unitType = tieredUsageUnit.Type.ToString().ToLower();
                        agg = tieredUsageUnit.AggregateUsage == AggregateUsage.Max ? "max" : "sum";
                        currency = tieredUsageUnit.Currency.ToString();
                        dispName = tieredUsageUnit.DisplayName;
                    }

                    /* ----- 使用量取得 ----- */
                    double count = 0.0;
                    if (unitType != "fixed")
                    {
                        if (unitName == null)
                        {
                            throw new InvalidOperationException("Metering unit name is null for a non-fixed unit type. This is not allowed.");
                        }
                        double cached;
                        if (!usageCache.TryGetValue(unitName, out cached))
                        {
                            var resp = await meteringApi
                                .GetMeteringUnitDateCountByTenantIdAndUnitNameAndDatePeriodAsync(
                                    tenantId, unitName, (int)periodStart, (int)periodEnd);

                            count = agg == "max"
                                        ? resp.Counts.Max(c => (double)c.Count)
                                        : resp.Counts.Sum(c => (double)c.Count);

                            usageCache[unitName] = count;
                        }
                        else
                        {
                            count = cached;
                        }
                    }

                    /* ----- 金額計算 & 集計 ----- */
                    var amount = CalculateAmountByUnitType(count, inst);
                    currencySum.AddOrUpdate(currency, amount, (k, old) => old + amount);

                    billings.Add(new Dictionary<string, object>
                    {
                        ["metering_unit_name"] = unitName ?? string.Empty,
                        ["metering_unit_type"] = unitType,
                        ["function_menu_name"] = menuName,
                        ["period_count"] = count,
                        ["currency"] = currency,
                        ["period_amount"] = amount,
                        ["pricing_unit_display_name"] = dispName ?? string.Empty
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

        // ──────────────────────────── GET /billing/dashboard ────────────────────────────
        [HttpGet, Route("dashboard")]
        public async Task<IHttpActionResult> GetBillingDashboard(
            [FromUri(Name = "tenant_id")] string tenantId,
            [FromUri(Name = "plan_id")] string planId,
            [FromUri(Name = "period_start")] long periodStart,
            [FromUri(Name = "period_end")] long periodEnd)
        {
            try
            {
                // APIクライアント初期化
                var authCfg = SaasusApiHelpers.CreateClientConfiguration(c => c.GetAuthApiClientConfig(), Request);
                var pricingCfg = SaasusApiHelpers.CreateClientConfiguration(c => c.GetPricingApiClientConfig(), Request);

                // 認証と権限チェック
                var idToken = SaasusApiHelpers.GetBearerToken(Request);
                var user = await new UserInfoApi(authCfg).GetUserInfoAsync(idToken);

                if (!HasBillingAccess(user, tenantId))
                {
                    return Content(HttpStatusCode.Forbidden, new { error = "Insufficient permissions" });
                }

                // プラン & テナント取得
                var planApi = new PricingPlansApi(pricingCfg);
                var plan = await planApi.GetPricingPlanAsync(planId);
                var tenantApi = new TenantApi(authCfg);
                var tenant = await tenantApi.GetTenantAsync(tenantId);

                // 税率取得
                TaxRate matchedTax = null;
                var history = tenant.PlanHistories == null ? null :
                              tenant.PlanHistories
                                    .OrderByDescending(h => h.PlanAppliedAt)
                                    .FirstOrDefault(h => h.PlanId == planId && h.PlanAppliedAt <= periodStart);

                if (history != null && !string.IsNullOrEmpty(history.TaxRateId))
                {
                    var taxRateApi = new TaxRateApi(pricingCfg);
                    var taxRates = await taxRateApi.GetTaxRatesAsync();
                    matchedTax = taxRates.VarTaxRates.FirstOrDefault(t => t.Id == history.TaxRateId);
                }

                // 使用量取得と集計
                var calcResult = await CalcMeteringUnitBillingsAsync(
                                        tenantId, periodStart, periodEnd, plan, pricingCfg);
                var billings = calcResult.billings;
                var totals = calcResult.totals;

                var response = new
                {
                    summary = new
                    {
                        total_by_currency = totals,
                        total_metering_units = billings.Count
                    },
                    metering_unit_billings = billings,
                    pricing_plan_info = new
                    {
                        plan_id = plan.Id,
                        display_name = plan.DisplayName,
                        description = plan.Description
                    },
                    tax_rate = matchedTax
                };

                return Ok(response);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to get billing dashboard: {ex.Message}");
                return Content(HttpStatusCode.InternalServerError, new { error = "Failed to get billing dashboard" });
            }
        }

        // ──────────────────────────── GET /billing/plan_periods ────────────────────────────
        [HttpGet, Route("plan_periods")]
        public async Task<IHttpActionResult> GetPlanPeriods([FromUri(Name = "tenant_id")] string tenantId)
        {
            try
            {
                // 入力チェック
                if (string.IsNullOrWhiteSpace(tenantId))
                {
                    return Content(HttpStatusCode.BadRequest, new { detail = "tenant_id required" });
                }

                // APIクライアント初期化
                var authCfg = SaasusApiHelpers.CreateClientConfiguration(c => c.GetAuthApiClientConfig(), Request);
                var pricingCfg = SaasusApiHelpers.CreateClientConfiguration(c => c.GetPricingApiClientConfig(), Request);

                // 認証と権限チェック
                var idToken = SaasusApiHelpers.GetBearerToken(Request);
                var user = await new UserInfoApi(authCfg).GetUserInfoAsync(idToken);

                if (!HasBillingAccess(user, tenantId))
                {
                    return Content(HttpStatusCode.Forbidden, new { error = "Insufficient permissions" });
                }

                // テナント情報取得
                var tenant = await new TenantApi(authCfg).GetTenantAsync(tenantId);
                if (tenant.PlanHistories == null || !tenant.PlanHistories.Any())
                {
                    return Ok(new List<Dictionary<string, object>>());
                }

                // 境界点（plan history）を昇順に整列
                var histories = tenant.PlanHistories.OrderBy(h => h.PlanAppliedAt).ToList();
                var plansApi = new PricingPlansApi(pricingCfg);

                // JST 設定（Tokyo Standard Time）
                var zone = TimeZoneInfo.FindSystemTimeZoneById("Tokyo Standard Time");
                var jstOffset = zone.BaseUtcOffset;
                string Label(DateTime start, DateTime end) =>
                    string.Format("{0:yyyy年MM月dd日 HH:mm:ss} ～ {1:yyyy年MM月dd日 HH:mm:ss}", start, end);

                // 現在プラン期間の終了点
                var lastEnd = tenant.CurrentPlanPeriodEnd > 0
                                ? tenant.CurrentPlanPeriodEnd
                                : DateTimeOffset.UtcNow.ToUnixTimeSeconds();

                var results = new List<Dictionary<string, object>>();

                // プラン履歴ごとに区間を作成
                for (int i = 0; i < histories.Count; i++)
                {
                    var hist = histories[i];
                    var planId = hist.PlanId;
                    if (string.IsNullOrEmpty(planId)) continue;

                    // プラン取得
                    var plan = await plansApi.GetPricingPlanAsync(planId);

                    // 開始・終了 Epoch 秒
                    var startEpoch = hist.PlanAppliedAt;
                    long endEpoch = (i + 1 < histories.Count)
                                        ? histories[i + 1].PlanAppliedAt - 1
                                        : lastEnd;

                    // JST へ変換
                    var periodStart = DateTimeOffset.FromUnixTimeSeconds(startEpoch)
                                                    .ToOffset(jstOffset).DateTime;
                    var periodEnd = DateTimeOffset.FromUnixTimeSeconds(endEpoch)
                                                    .ToOffset(jstOffset).DateTime;

                    // 年払い判定
                    var yearly = PlanHasYearUnit(plan);

                    // step (1 ヶ月 / 1 年) で区間分割
                    var cur = periodStart;
                    while (cur <= periodEnd)
                    {
                        var next = yearly ? cur.AddYears(1) : cur.AddMonths(1);
                        var segEnd = next.AddSeconds(-1);
                        if (segEnd > periodEnd) segEnd = periodEnd;
                        if (segEnd <= cur) break;

                        results.Add(new Dictionary<string, object>
                        {
                            ["label"] = Label(cur, segEnd),
                            ["plan_id"] = planId,
                            ["start"] = new DateTimeOffset(cur, jstOffset).ToUnixTimeSeconds(),
                            ["end"] = new DateTimeOffset(segEnd, jstOffset).ToUnixTimeSeconds()
                        });

                        if (segEnd == periodEnd) break;
                        cur = segEnd.AddSeconds(1);
                    }
                }

                // start DESC でソート
                results.Sort((a, b) => ((long)b["start"]).CompareTo((long)a["start"]));
                return Ok(results);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to get plan periods: {ex.Message}");
                return Content(HttpStatusCode.InternalServerError, new { error = "Failed to get plan periods" });
            }
        }

        // プランに年単位があるかチェック
        private bool PlanHasYearUnit(PricingPlan plan)
        {
            foreach (var m in plan.PricingMenus ?? Enumerable.Empty<PricingMenu>())
            {
                if (m.Units == null) continue;
                foreach (var u in m.Units)
                {
                    var interval = ExtractRecurringInterval(u);
                    if (interval == RecurringInterval.Year) return true;
                }
            }
            return false;
        }

        // 課金間隔を抽出
        private RecurringInterval ExtractRecurringInterval(PricingUnit u)
        {
            if (u.ActualInstance is PricingFixedUnit f) return f.RecurringInterval;
            if (u.ActualInstance is PricingUsageUnit usg) return usg.RecurringInterval;
            if (u.ActualInstance is PricingTieredUnit t) return t.RecurringInterval;
            if (u.ActualInstance is PricingTieredUsageUnit tu) return tu.RecurringInterval;
            return RecurringInterval.Month;
        }

        // ──────────────────────────── POST /billing/metering/{tenantId}/{unit}/{ts} ────────────────────────────
        [HttpPost, Route("metering/{tenantId}/{unit}/{ts:int}")]
        public async Task<IHttpActionResult> UpdateMeteringCount(
            string tenantId, string unit, long ts,
            [FromBody] UpdateMeteringCountRequest reqBody)
        {
            try
            {
                // 1. 権限チェック
                var authCfg = SaasusApiHelpers.CreateClientConfiguration(c => c.GetAuthApiClientConfig(), Request);
                var idToken = SaasusApiHelpers.GetBearerToken(Request);
                var user = await new UserInfoApi(authCfg).GetUserInfoAsync(idToken);

                if (!HasBillingAccess(user, tenantId))
                {
                    return Content(HttpStatusCode.Forbidden, new { error = "Insufficient permissions" });
                }

                // 2. 入力バリデーション
                if (!new[] { "add", "sub", "direct" }.Contains(reqBody.Method))
                {
                    return Content(HttpStatusCode.BadRequest, new { error = "Method must be add | sub | direct" });
                }
                if (reqBody.Count < 0)
                {
                    return Content(HttpStatusCode.BadRequest, new { error = "Count must be >= 0" });
                }

                // 3. SaaSus API 呼び出し
                var pricingCfg = SaasusApiHelpers.CreateClientConfiguration(c => c.GetPricingApiClientConfig(), Request);
                var meteringApi = new MeteringApi(pricingCfg);

                UpdateMeteringUnitTimestampCountMethod method;
                if (reqBody.Method == "add") method = UpdateMeteringUnitTimestampCountMethod.Add;
                else if (reqBody.Method == "sub") method = UpdateMeteringUnitTimestampCountMethod.Sub;
                else if (reqBody.Method == "direct") method = UpdateMeteringUnitTimestampCountMethod.Direct;
                else method = UpdateMeteringUnitTimestampCountMethod.Add;

                var param = new UpdateMeteringUnitTimestampCountParam
                {
                    Method = method,
                    Count = reqBody.Count
                };

                var result = await meteringApi.UpdateMeteringUnitTimestampCountAsync(
                                tenantId, unit, (int)ts, param);

                return Ok(result);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to update metering count: {ex.Message}");
                return Content(HttpStatusCode.InternalServerError, new { error = "Failed to update metering count" });
            }
        }

        // ──────────────────────────── POST /billing/metering/{tenantId}/{unit} (現在時刻) ────────────────────────────
        [HttpPost, Route("metering/{tenantId}/{unit}")]
        public async Task<IHttpActionResult> UpdateMeteringCountNow(
            string tenantId, string unit,
            [FromBody] UpdateMeteringCountRequest reqBody)
        {
            try
            {
                // 1. 権限チェック
                var authCfg = SaasusApiHelpers.CreateClientConfiguration(c => c.GetAuthApiClientConfig(), Request);
                var idToken = SaasusApiHelpers.GetBearerToken(Request);
                var user = await new UserInfoApi(authCfg).GetUserInfoAsync(idToken);

                if (!HasBillingAccess(user, tenantId))
                {
                    return Content(HttpStatusCode.Forbidden, new { error = "Insufficient permissions" });
                }

                // 2. 入力バリデーション
                if (!new[] { "add", "sub", "direct" }.Contains(reqBody.Method))
                {
                    return Content(HttpStatusCode.BadRequest, new { error = "Method must be add | sub | direct" });
                }
                if (reqBody.Count < 0)
                {
                    return Content(HttpStatusCode.BadRequest, new { error = "Count must be >= 0" });
                }

                // 3. SaaSus API 呼び出し
                var pricingCfg = SaasusApiHelpers.CreateClientConfiguration(c => c.GetPricingApiClientConfig(), Request);
                var meteringApi = new MeteringApi(pricingCfg);
                var tsNow = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

                UpdateMeteringUnitTimestampCountMethod method;
                if (reqBody.Method == "add") method = UpdateMeteringUnitTimestampCountMethod.Add;
                else if (reqBody.Method == "sub") method = UpdateMeteringUnitTimestampCountMethod.Sub;
                else if (reqBody.Method == "direct") method = UpdateMeteringUnitTimestampCountMethod.Direct;
                else method = UpdateMeteringUnitTimestampCountMethod.Add;

                var param = new UpdateMeteringUnitTimestampCountParam
                {
                    Method = method,
                    Count = reqBody.Count
                };

                var result = await meteringApi.UpdateMeteringUnitTimestampCountAsync(
                                tenantId, unit, (int)tsNow, param);

                return Ok(result);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to update metering count now: {ex.Message}");
                return Content(HttpStatusCode.InternalServerError, new { error = "Failed to update metering count" });
            }
        }

        // ──────────────────────────── GET /pricing_plans ────────────────────────────
        [HttpGet, Route("~/pricing_plans")]
        public async Task<IHttpActionResult> GetPricingPlans()
        {
            try
            {
                // 1. 認証チェック
                var authCfg = SaasusApiHelpers.CreateClientConfiguration(c => c.GetAuthApiClientConfig(), Request);
                var userInfoApi = new UserInfoApi(authCfg);
                var idToken = SaasusApiHelpers.GetBearerToken(Request);
                if (string.IsNullOrEmpty(idToken))
                    return Content(HttpStatusCode.Unauthorized, new { error = "Unauthorized" });
                var userInfo = await userInfoApi.GetUserInfoAsync(idToken);

                // 2. 料金プラン一覧を取得
                var pricingCfg = SaasusApiHelpers.CreateClientConfiguration(c => c.GetPricingApiClientConfig(), Request);
                var plansApi = new PricingPlansApi(pricingCfg);
                var plans = await plansApi.GetPricingPlansAsync();

                // JSON形式でレスポンスを返す
                return Ok(plans.VarPricingPlans);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to get pricing plans: {ex.Message}");
                return Content(HttpStatusCode.InternalServerError, new { error = "Failed to get pricing plans" });
            }
        }

        // ──────────────────────────── GET /tax_rates ────────────────────────────
        [HttpGet, Route("~/tax_rates")]
        public async Task<IHttpActionResult> GetTaxRates()
        {
            try
            {
                // 1. 認証チェック
                var authCfg = SaasusApiHelpers.CreateClientConfiguration(c => c.GetAuthApiClientConfig(), Request);
                var userInfoApi = new UserInfoApi(authCfg);
                var idToken = SaasusApiHelpers.GetBearerToken(Request);
                if (string.IsNullOrEmpty(idToken))
                    return Content(HttpStatusCode.Unauthorized, new { error = "Unauthorized" });
                var userInfo = await userInfoApi.GetUserInfoAsync(idToken);

                // 2. 税率一覧を取得
                var pricingCfg = SaasusApiHelpers.CreateClientConfiguration(c => c.GetPricingApiClientConfig(), Request);
                var taxRatesApi = new TaxRateApi(pricingCfg);
                var taxRates = await taxRatesApi.GetTaxRatesAsync();

                // JSON形式でレスポンスを返す
                return Ok(taxRates.VarTaxRates);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to get tax rates: {ex.Message}");
                return Content(HttpStatusCode.InternalServerError, new { error = "Failed to get tax rates" });
            }
        }

        // ──────────────────────────── GET /tenants/{tenantId}/plan ────────────────────────────
        [HttpGet, Route("~/tenants/{tenantId}/plan")]
        public async Task<IHttpActionResult> GetTenantPlanInfo(string tenantId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(tenantId))
                {
                    return Content(HttpStatusCode.BadRequest, new { error = "tenant_id is required" });
                }

                // 1. 認証・認可チェック
                var authCfg = SaasusApiHelpers.CreateClientConfiguration(c => c.GetAuthApiClientConfig(), Request);
                var userInfoApi = new UserInfoApi(authCfg);
                var idToken = SaasusApiHelpers.GetBearerToken(Request);
                if (string.IsNullOrEmpty(idToken))
                    return Content(HttpStatusCode.Unauthorized, new { error = "Unauthorized" });
                var userInfo = await userInfoApi.GetUserInfoAsync(idToken);

                if (!HasBillingAccess(userInfo, tenantId))
                {
                    return Content(HttpStatusCode.Forbidden, new { error = "Insufficient permissions" });
                }

                // 2. テナント詳細情報を取得
                var tenantApi = new TenantApi(authCfg);
                var tenant = await tenantApi.GetTenantAsync(tenantId);

                // 3. 現在のプランの税率情報を取得（プラン履歴の最新エントリから）
                string currentTaxRateId = null;
                if (tenant.PlanHistories != null && tenant.PlanHistories.Any())
                {
                    var latestPlanHistory = tenant.PlanHistories.Last();
                    if (!string.IsNullOrEmpty(latestPlanHistory.TaxRateId))
                    {
                        currentTaxRateId = latestPlanHistory.TaxRateId;
                    }
                }

                // 4. レスポンスを構築
                object planReservation = null;
                if (tenant.UsingNextPlanFrom > 0)
                {
                    planReservation = new
                    {
                        next_plan_id = tenant.NextPlanId,
                        using_next_plan_from = tenant.UsingNextPlanFrom,
                        next_plan_tax_rate_id = tenant.NextPlanTaxRateId
                    };
                }

                var response = new
                {
                    id = tenant.Id,
                    name = tenant.Name,
                    plan_id = tenant.PlanId,
                    tax_rate_id = currentTaxRateId,
                    plan_reservation = planReservation
                };

                return Ok(response);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to retrieve tenant detail: {ex.Message}");
                return Content(HttpStatusCode.InternalServerError, new { error = "Failed to retrieve tenant detail" });
            }
        }

        // ──────────────────────────── PUT /tenants/{tenantId}/plan ────────────────────────────
        [HttpPut, Route("~/tenants/{tenantId}/plan")]
        public async Task<IHttpActionResult> UpdateTenantPlan(
            string tenantId,
            [FromBody] UpdateTenantPlanRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(tenantId))
                {
                    return Content(HttpStatusCode.BadRequest, new { error = "tenant_id is required" });
                }

                // 1. 認証・認可チェック
                var authCfg = SaasusApiHelpers.CreateClientConfiguration(c => c.GetAuthApiClientConfig(), Request);
                var userInfoApi = new UserInfoApi(authCfg);
                var idToken = SaasusApiHelpers.GetBearerToken(Request);
                if (string.IsNullOrEmpty(idToken))
                    return Content(HttpStatusCode.Unauthorized, new { error = "Unauthorized" });
                var userInfo = await userInfoApi.GetUserInfoAsync(idToken);

                if (!HasBillingAccess(userInfo, tenantId))
                {
                    return Content(HttpStatusCode.Forbidden, new { error = "Insufficient permissions" });
                }

                // 2. テナントプランを更新
                var tenantApi = new TenantApi(authCfg);
                var updateParam = new authapi.Model.PlanReservation();

                // NextPlanIdが指定されている場合のみ設定（プラン解除時は空文字列またはnull）
                if (!string.IsNullOrEmpty(request.NextPlanId))
                {
                    updateParam.NextPlanId = request.NextPlanId;
                }

                // 税率IDが指定されている場合のみ設定
                if (!string.IsNullOrEmpty(request.TaxRateId))
                {
                    updateParam.NextPlanTaxRateId = request.TaxRateId;
                }

                // using_next_plan_fromが指定されている場合のみ設定
                if (request.UsingNextPlanFrom.HasValue && request.UsingNextPlanFrom.Value > 0)
                {
                    updateParam.UsingNextPlanFrom = (int)request.UsingNextPlanFrom.Value;
                }

                await tenantApi.UpdateTenantPlanAsync(tenantId, updateParam);

                return Ok(new { message = "Tenant plan updated successfully" });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to update tenant plan: {ex.Message}");
                return Content(HttpStatusCode.InternalServerError, new { error = "Failed to update tenant plan" });
            }
        }
    }

    /* ──────────────────────────────────────────────────────────
     *  DTO
     * ────────────────────────────────────────────────────────── */
    public sealed class UpdateMeteringCountRequest
    {
        [Required, RegularExpression("^(add|sub|direct)$")]
        public string Method { get; set; }

        [Range(0, int.MaxValue)]
        public int Count { get; set; }
    }

    /// <summary>
    /// テナントプラン更新リクエスト
    /// </summary>
    public class UpdateTenantPlanRequest
    {
        [JsonProperty("next_plan_id")]
        public string NextPlanId { get; set; }

        [JsonProperty("tax_rate_id")]
        public string TaxRateId { get; set; }

        [JsonProperty("using_next_plan_from")]
        public long? UsingNextPlanFrom { get; set; }
    }
}