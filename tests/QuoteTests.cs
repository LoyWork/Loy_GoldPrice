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
    }
}
