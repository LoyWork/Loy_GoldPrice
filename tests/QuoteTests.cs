using System;
using System.Drawing;
using LoyGoldPrice;

internal static class QuoteTests
{
    static Quote Parse(string field)
    {
        return Quotes.Parse("{\"success\":true,\"code\":\"SUC0000\",\"data\":{\"NowTime\":\"2026/10/27 11:48:09\",\"MarketDay\":true,\"QuotedPrice\":true,\"FQAMBPRCZ1\":{\"zBuyPrc\":\"865.53\",\"zSelPrc\":\"860.53\",\"zPrcDpt\":\"1.00\",\"zPrcDif\":\"0.02\"" + field + "}}}");
    }
    static void Check(bool ok, string name) { if (!ok) { Console.Error.WriteLine("FAIL: " + name); throw new Exception(name); } }
    static void Main()
    {
        try { Run(); }
        catch (Exception ex) { Console.Error.WriteLine(ex.GetType().Name + ": " + ex.Message);
            if (ex.InnerException != null) Console.Error.WriteLine(ex.InnerException.GetType().Name + ": " + ex.InnerException.Message);
            Environment.ExitCode = 1; }
    }
    static void Run()
    {
        var down = Parse(",\"zDvlCur\":\"-0.47\"");
        Check(down.Buy == 865.53m && down.Sell == 860.53m && down.DailyChange == -0.47m, "Read correct change field and buy/sell mapping");
        Check(down.Buy - down.DailyChange == 866m, "Daily reference arithmetic");
        Color red = PriceColors.ForChange(1m, false), green = PriceColors.ForChange(down.DailyChange, false);
        Check(red.R > red.G && green.G > green.R, "Up red, down green");
        var neutral = PriceColors.ForChange(0m, false);
        Check(neutral != red && neutral != green, "Unchanged neutral");
        foreach (string field in new[] { "", ",\"zDvlCur\":null", ",\"zDvlCur\":\"--\"", ",\"zDvlCur\":\"9999\"" })
            Check(!Parse(field).DailyChange.HasValue && PriceColors.ForChange(Parse(field).DailyChange, false) == neutral, "Missing/invalid reference neutral");
        var stale = PriceColors.ForChange(1m, true);
        Check(stale == PriceColors.ForChange(-1m, true) && stale == PriceColors.ForChange(null, true) && stale != neutral, "Stale overrides direction");
        Console.WriteLine("PASS: mapping, daily reference, up/down/flat, missing/invalid data, stale priority");
        string sample = "var hq_str_hf_XAU=\"4030.61,3930.090,4030.61,4030.96,4038.32,3919.85,16:32:00,3930.09,3930.14,0,0,0,2026-11-04,Gold\";";
        var xau = InternationalQuotes.Parse(sample);
        Check(xau.Price == 4030.61m && xau.PreviousClose == 3930.09m && xau.Change == 100.52m, "XAU spot mapping and daily direction");
        var utc = new DateTimeOffset(2026, 11, 4, 8, 32, 0, TimeSpan.Zero);
        Check(xau.Time == utc && !xau.IsStale(utc) && xau.IsStale(utc.AddMinutes(3)) && xau.IsStale(utc.AddMinutes(-3)), "XAU Beijing timestamp and stale/future protection");
        foreach (string bad in new[] { "var hq_str_hf_XAU=\"\";", sample.Replace("hf_XAU", "hf_GC"), sample.Replace("4030.61,", "0,"), sample.Replace("3930.09,", "--,"), sample.Replace("2026-11-04", "invalid") })
        {
            bool rejected = false;
            try { InternationalQuotes.Parse(bad); } catch (System.IO.InvalidDataException) { rejected = true; }
            Check(rejected, "Reject missing/wrong-symbol/invalid XAU quote");
        }
        Console.WriteLine("PASS: XAU spot mapping, unit-source separation, timestamps, stale/invalid payloads");
        string[] etfFields = new string[33];
        for (int i = 0; i < etfFields.Length; i++) etfFields[i] = "0";
        etfFields[0] = "纳指ETF华泰柏瑞";
        etfFields[1] = "2.480"; // Open differs from last and previous close.
        etfFields[2] = "2.470";
        etfFields[3] = "2.457";
        etfFields[30] = "2026-11-12";
        etfFields[31] = "14:02:35";
        string etfSample = "var hq_str_sh513110=\"" + string.Join(",", etfFields) + "\";";
        var etf = EtfQuotes.Parse(etfSample);
        Check(etf.Price == 2.457m && etf.PreviousClose == 2.470m && etf.Change == -0.013m, "ETF last price, previous close, three-decimal precision");
        var etfUtc = new DateTimeOffset(2026, 11, 12, 6, 2, 35, TimeSpan.Zero);
        Check(etf.Time == etfUtc && !etf.IsStale(etfUtc) && etf.IsStale(etfUtc.AddMinutes(3)) && etf.IsStale(etfUtc.AddMinutes(-3)), "ETF Beijing timestamp and stale/future protection");
        foreach (string bad in new[] { "var hq_str_sh513110=\"\";", etfSample.Replace("sh513110", "sh513100"), etfSample.Replace("2.457", "0"), etfSample.Replace("2.470", "--"), etfSample.Replace("2026-11-12", "invalid") })
        {
            bool rejected = false;
            try { EtfQuotes.Parse(bad); } catch (System.IO.InvalidDataException) { rejected = true; }
            Check(rejected, "Reject missing/wrong-symbol/invalid ETF quote");
        }
        Console.WriteLine("PASS: 513110 exchange price mapping, precision, timestamps, stale/invalid payloads");
        string navSample = "var hq_str_f_513110=\"基金名称,2.1459,3.0000,2.1729,2026-11-11,22.2144\";";
        var nav = EtfNavs.Parse(navSample);
        Check(nav.Value == 2.1459m && nav.Date == new DateTime(2026, 11, 11), "Use unit NAV, not accumulated NAV or previous NAV");
        Check(nav.Premium(etf) == (2.457m / 2.1459m - 1m) * 100m, "Premium uses full precision unit NAV");
        Check(new EtfNav { Value = etf.Price, Date = nav.Date }.Premium(etf) == 0m, "Flat premium");
        Check(new EtfNav { Value = 3m, Date = nav.Date }.Premium(etf) < 0m, "Discount is negative");
        Check(nav.Premium(null) == null && new EtfNav { Value = 0m }.Premium(etf) == null &&
            new EtfNav { Value = 2m, Date = etf.Time.Date.AddDays(1) }.Premium(etf) == null, "Missing, zero, or future NAV cannot produce premium");
        foreach (string bad in new[] { "var hq_str_f_513110=\"\";", navSample.Replace("f_513110", "f_513100"), navSample.Replace("2.1459", "0"), navSample.Replace("2.1459", "--"), navSample.Replace("2026-11-11", "invalid") })
        {
            bool rejected = false;
            try { EtfNavs.Parse(bad); } catch (System.IO.InvalidDataException) { rejected = true; }
            Check(rejected, "Reject invalid or wrong-symbol NAV");
        }
        Console.WriteLine("PASS: unit NAV/date mapping, premium/discount arithmetic, missing/future/invalid NAV");
        var indexSeries = new EstimateSeries { Latest = 240m, MarketDate = new DateTime(2026, 11, 19), Time = new DateTimeOffset(2026, 11, 19, 21, 0, 0, TimeSpan.Zero) };
        var fxSeries = new EstimateSeries { Latest = 7.1m, MarketDate = new DateTime(2026, 11, 20), Time = indexSeries.Time.AddHours(9) };
        var baseDate = new DateTime(2026, 11, 17);
        indexSeries.Closes[baseDate] = 200m;
        fxSeries.Closes[baseDate] = 7m;
        var market = new EstimateMarket { Index = indexSeries, Fx = fxSeries };
        var estimated = market.Calculate(new EtfNav { Value = 100m, Date = baseDate });
        Check(Math.Abs(estimated.Value - 121.71428571428571428571428571m) < 0.00000001m, "Estimate includes cumulative index AND CNY conversion from NAV date");
        Check(market.Calculate(new EtfNav { Value = 100m, Date = baseDate.AddDays(-1) }) == null, "Missing exact baseline must not be replaced with previous close");
        indexSeries.Closes[baseDate.AddDays(1)] = 220m;
        fxSeries.Closes[baseDate.AddDays(1)] = 7.05m;
        Check(market.Calculate(new EtfNav { Value = 105m, Date = baseDate.AddDays(1) }).Value == 105m * 240m / 220m * 7.1m / 7.05m, "New published NAV rebases estimate");
        Check(!estimated.IsStale(fxSeries.Time) && estimated.IsStale(fxSeries.Time.AddDays(5)) && estimated.IsStale(indexSeries.Time.AddMinutes(-6)), "Estimate old/future timestamps");
        Check(estimated.Premium(new EtfQuote { Price = estimated.Value }) == 0m, "Estimated premium calculation");
        string history = "{\"chart\":{\"error\":null,\"result\":[{\"meta\":{\"symbol\":\"^NDX\",\"regularMarketPrice\":240,\"regularMarketTime\":1795122000},\"timestamp\":[1794940200,1795026600],\"indicators\":{\"quote\":[{\"close\":[200,null]}]}}]}}";
        var parsedSeries = EstimateMarkets.Parse(history, "^NDX", true);
        Check(parsedSeries.Latest == 240m && parsedSeries.Closes.Count == 1 && parsedSeries.Closes[new DateTime(2026, 11, 17)] == 200m && parsedSeries.MarketDate == new DateTime(2026, 11, 19), "Chart latest/history mapping and missing closes");
        foreach (string bad in new[] { "{}", history.Replace("^NDX", "^IXIC"), history.Replace("240", "0"), history.Replace("200,null", "200"), "{\"chart\":{\"error\":{\"description\":\"unavailable\"}}}" })
        {
            bool rejected = false;
            try { EstimateMarkets.Parse(bad, "^NDX", true); } catch (System.IO.InvalidDataException) { rejected = true; }
            Check(rejected, "Reject bad estimate symbol, price, shape and endpoint error");
        }
        Console.WriteLine("PASS: cumulative index/FX estimate, NAV rebasing, missing baseline, timestamps, chart parsing");
    }
}
