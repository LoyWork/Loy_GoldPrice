using System;
using System.Drawing;
using LoyGoldPrice;

internal static class QuoteTests
{
    static Quote Parse(string field)
    {
        return Quotes.Parse("{\"success\":true,\"code\":\"SUC0000\",\"data\":{\"NowTime\":\"2026/10/27 11:48:09\",\"MarketDay\":true,\"QuotedPrice\":true,\"FQAMBPRCZ1\":{\"zBuyPrc\":\"865.53\",\"zSelPrc\":\"860.53\",\"zPrcDpt\":\"1.00\",\"zPrcDif\":\"0.02\"" + field + "}}}");
    }
    static void Check(bool ok, string name) { if (!ok) throw new Exception(name); }
    static void Main()
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
    }
}
