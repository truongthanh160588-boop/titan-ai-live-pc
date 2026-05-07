using System.Text.RegularExpressions;
using TitanAILivePC.Models;

namespace TitanAILivePC.Services;

/// <summary>
/// Kịch bản FAQ livestream — khớp trước GPT và trước nhánh kỹ thuật chuyên sâu (trừ khi đã có báo giá catalog).
/// </summary>
public static class TitanLivestreamScript
{
    private static readonly Regex DigitKhachRegex = new(@"\b\d{2,4}\s*khach\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private const string GreetingReply =
        "Em chào anh. Titan Audio luôn online hỗ trợ kỹ thuật và tư vấn hệ thống âm thanh chuyên nghiệp cho karaoke, sự kiện và sân khấu.";

    private const string ComboAudienceReply =
        "Với số lượng khách như vậy, Titan sẽ tính theo không gian, độ phủ loa và nhu cầu sử dụng thực tế để hệ thống hoạt động ổn định và hiệu quả nhất. Anh vui lòng liên hệ hotline 0974 70 4444 để kỹ thuật Titan tư vấn cấu hình phù hợp.";

    private const string EcosystemReply =
        "Hệ sinh thái Titan được đồng bộ từ loa, main công suất, processor, mixer đến preset xử lý. Khi đồng bộ toàn hệ thống sẽ cho độ ổn định cao, bảo vệ thiết bị tốt hơn, dễ setup và chất âm đồng đều hơn.";

    private const string PresetReply =
        "Titan có preset đồng bộ sẵn theo từng cấu hình loa và main công suất. Khi setup đúng hệ sinh thái, kỹ thuật chỉ cần tinh chỉnh nhẹ theo không gian thực tế là hệ thống đã hoạt động rất hiệu quả.";

    private const string T60Reply =
        "T-60 là processor thế hệ mới của Titan, xử lý ổn định, chống hú hiệu quả, preset dễ dùng và đồng bộ rất tốt với hệ sinh thái Titan. Đây là dòng được nhiều kỹ thuật sử dụng cho karaoke và sân khấu sự kiện.";

    private const string SpeakerLineReply =
        "Titan hướng tới chất âm sạch, lực tốt và hoạt động ổn định lâu dài. Hệ thống được tính toán đồng bộ từ củ loa, thùng loa, phân tần đến preset xử lý để đạt hiệu quả thực tế cao.";

    private const string BrandCompareReply =
        "Mỗi hệ thống có định hướng khác nhau. Titan tập trung vào hiệu quả thực tế, độ ổn định, dễ setup và khả năng đồng bộ toàn hệ thống để phù hợp điều kiện sử dụng tại Việt Nam.";

    private const string PowerReply =
        "Công suất hệ thống Titan được tính theo hiệu quả hoạt động thực tế và độ ổn định lâu dài. Khi phối ghép đúng preset và đúng main công suất, hệ thống hoạt động rất bền và ổn định.";

    private const string SetupReply =
        "Titan hỗ trợ preset, hướng dẫn setup và đồng hành kỹ thuật để người dùng dễ vận hành hơn. Khi đồng bộ đúng hệ sinh thái thì việc setup sẽ nhanh và hiệu quả hơn rất nhiều.";

    private const string PriceNeedModelReply =
        "Anh vui lòng cho Titan xin mã sản phẩm hoặc nhu cầu sử dụng cụ thể để kỹ thuật hỗ trợ báo giá và cấu hình phù hợp nhất.";

    private const string PurchaseReply =
        "Titan hỗ trợ giao hàng toàn quốc, có bảo hành và hỗ trợ kỹ thuật. Anh vui lòng liên hệ hotline 0974 70 4444 để được hỗ trợ nhanh nhất.";

    private const string SoundQualityReply =
        "Titan ưu tiên chất âm sạch, độ phủ đều và khả năng hoạt động ổn định lâu dài. Mỗi cấu hình đều được đồng bộ preset để đạt hiệu quả thực tế tốt nhất.";

    private const string HeartReactionReply =
        "Titan cảm ơn anh/chị đã thả tim ủng hộ live. Anh/chị cần em tư vấn mẫu nào em hỗ trợ ngay ạ.";

    private const string HahaReactionReply =
        "Titan cảm ơn anh/chị đã tương tác haha cùng live. Nếu cần tư vấn nhanh sản phẩm phù hợp, em hỗ trợ liền nhé.";

    private const string LoveReactionReply =
        "Titan cảm ơn anh/chị đã thả thương thương. Anh/chị quan tâm mẫu nào, em báo cấu hình và giá nhanh ngay ạ.";

    public static bool TryMatch(string? comment, out string replyBody, out AiReplyType replyType)
    {
        replyBody = string.Empty;
        replyType = AiReplyType.ScriptedFaq;

        if (string.IsNullOrWhiteSpace(comment))
        {
            return false;
        }

        var n = TitanKnowledgeBase.NormalizeViComment(comment);
        var blob = ProductKeyNormalizer.NormalizeProductKey(comment);

        if (TryMatchGreeting(n, blob, out replyBody))
        {
            replyType = AiReplyType.Greeting;
            return true;
        }

        if (TryMatchReactionEngagement(comment, n, out replyBody))
        {
            replyType = AiReplyType.Greeting;
            return true;
        }

        if (TryMatchComboAudience(n))
        {
            replyBody = ComboAudienceReply;
            return true;
        }

        if (TryMatchEcosystem(n))
        {
            replyBody = EcosystemReply;
            return true;
        }

        if (TryMatchPreset(n))
        {
            replyBody = PresetReply;
            return true;
        }

        if (TryMatchT60(n))
        {
            replyBody = T60Reply;
            return true;
        }

        if (TryMatchSpeakerLine(n))
        {
            replyBody = SpeakerLineReply;
            return true;
        }

        if (TryMatchBrandCompare(n))
        {
            replyBody = BrandCompareReply;
            return true;
        }

        if (TryMatchPower(n))
        {
            replyBody = PowerReply;
            return true;
        }

        if (TryMatchSetup(n))
        {
            replyBody = SetupReply;
            return true;
        }

        if (TryMatchPurchaseShipping(n))
        {
            replyBody = PurchaseReply;
            return true;
        }

        if (TryMatchSoundQuality(n))
        {
            replyBody = SoundQualityReply;
            return true;
        }

        if (TryMatchPriceWithoutModel(n, blob))
        {
            replyBody = PriceNeedModelReply;
            return true;
        }

        return false;
    }

    private static bool TryMatchReactionEngagement(string raw, string n, out string replyBody)
    {
        replyBody = string.Empty;

        var normalizedRaw = (raw ?? string.Empty).ToLowerInvariant();

        if (n.Contains("tha tim") ||
            n.Contains("tim tim") ||
            n.Contains("tha love") ||
            n.Contains("tha trai tim") ||
            n.Contains("da tha tim") ||
            n.Contains("bay to cam xuc tim") ||
            normalizedRaw.Contains("❤️") ||
            normalizedRaw.Contains("♥"))
        {
            replyBody = HeartReactionReply;
            return true;
        }

        if (n.Contains("haha") ||
            n.Contains("ha ha") ||
            n.Contains("tha haha") ||
            n.Contains("da tha haha") ||
            n.Contains("bay to cam xuc haha") ||
            normalizedRaw.Contains("😂") ||
            normalizedRaw.Contains("🤣"))
        {
            replyBody = HahaReactionReply;
            return true;
        }

        if (n.Contains("thuong thuong") ||
            n.Contains("tha thuong") ||
            n.Contains("da tha thuong") ||
            n.Contains("bay to cam xuc yeu thich") ||
            n.Contains("yeu qua"))
        {
            replyBody = LoveReactionReply;
            return true;
        }

        return false;
    }

    private static bool LikelyProductSkuBlob(string blob)
    {
        if (blob.Length < 4)
        {
            return false;
        }

        return blob.Contains("vf", StringComparison.Ordinal) ||
               blob.Contains("f712", StringComparison.Ordinal) ||
               blob.Contains("f124", StringComparison.Ordinal) ||
               blob.Contains("su118", StringComparison.Ordinal) ||
               blob.Contains("t416", StringComparison.Ordinal) ||
               blob.Contains("tfx", StringComparison.Ordinal) ||
               blob.Contains("t46", StringComparison.Ordinal) ||
               blob.Contains("t218", StringComparison.Ordinal) ||
               blob.Contains("t212", StringComparison.Ordinal) ||
               blob.Contains("t226", StringComparison.Ordinal) ||
               blob.Contains("t208", StringComparison.Ordinal) ||
               (blob.Contains("t60") && !blob.Contains("t600")) ||
               Regex.IsMatch(blob, @"\d{6,}");
    }

    private static bool TryMatchGreeting(string n, string blob, out string replyBody)
    {
        replyBody = string.Empty;
        if (LikelyProductSkuBlob(blob))
        {
            return false;
        }

        if (n.Contains("gia") || n.Contains("bao nhieu") || n.Contains("bao gia"))
        {
            return false;
        }

        if (DigitKhachRegex.IsMatch(n) || (n.Contains("combo") && n.Contains("khach")))
        {
            return false;
        }

        var onlineIntent = (n.Contains("titan") || n.Contains("shop")) &&
                           (n.Contains("online") || n.Contains("co online"));

        var greetingIntent =
            n.Contains("xin chao", StringComparison.Ordinal) ||
            n.Contains("chao anh", StringComparison.Ordinal) ||
            n.Contains("chao chi", StringComparison.Ordinal) ||
            (n.StartsWith("chao ", StringComparison.Ordinal) && n.Length <= 42) ||
            n.Contains("alo shop", StringComparison.Ordinal) ||
            (n.TrimStart().StartsWith("alo", StringComparison.Ordinal) && n.Length <= 22 && !n.Contains("vf"));

        if (!greetingIntent && !onlineIntent)
        {
            return false;
        }

        replyBody = GreetingReply;
        return true;
    }

    private static bool TryMatchComboAudience(string n)
    {
        return DigitKhachRegex.IsMatch(n) ||
               (n.Contains("combo") && n.Contains("khach")) ||
               ((n.Contains("san khau") || n.Contains("dan ") || n.StartsWith("dan ", StringComparison.Ordinal)) &&
                n.Contains("khach"));
    }

    private static bool TryMatchEcosystem(string n)
    {
        return (n.Contains("dong bo") && n.Contains("titan")) ||
               (n.Contains("full") && n.Contains("titan")) ||
               n.Contains("he sinh thai") ||
               (n.Contains("titan") && n.Contains("on dinh") && !n.Contains("t60")) ||
               ((n.Contains("sao phai") || n.Contains("vi sao")) &&
                (n.Contains("dong bo") || n.Contains("titan")));
    }

    private static bool TryMatchPreset(string n)
    {
        return n.Contains("preset") ||
               (n.Contains("chinh") && n.Contains("nhieu")) ||
               (n.Contains("mua") && n.Contains("danh") && (n.Contains("lien") || n.Contains("ngay")));
    }

    private static bool TryMatchT60(string n)
    {
        var mentionsT60 = n.Contains("t60") || Regex.IsMatch(n, @"\bt\s*[\-\s]*60\b");
        if (!mentionsT60)
        {
            return false;
        }

        return n.Contains("hay") ||
               n.Contains(" on ") ||
               n.EndsWith(" on", StringComparison.Ordinal) ||
               n.Contains("on khong") ||
               n.Contains("o khong") ||
               n.Contains("chong hu") ||
               n.Contains("vang") ||
               n.Contains("processor") ||
               n.Contains("xu ly") ||
               n.Contains("co gi");
    }

    private static bool TryMatchSpeakerLine(string n)
    {
        return n.Contains("loa") && n.Contains("titan") &&
               (n.Contains("luc") || n.Contains("sach") || n.Contains("ben"));
    }

    private static bool TryMatchBrandCompare(string n)
    {
        return n.Contains("hang khac") ||
               n.Contains("line array") ||
               n.Contains("loa ngoai") ||
               n.Contains("so sanh") ||
               (n.Contains("titan") && n.Contains("so voi")) ||
               (n.Contains("hon") && (n.Contains("hang") || n.Contains("line")));
    }

    private static bool TryMatchPower(string n)
    {
        return (n.Contains("cong suat") && (n.Contains("that") || n.Contains("anh"))) ||
               n.Contains("ngoai troi") ||
               (n.Contains("nong") && (n.Contains("chay") || n.Contains("lau") || n.Contains("may")));
    }

    private static bool TryMatchSetup(string n)
    {
        return (n.Contains("setup") && (n.Contains("kho") || n.Contains("de"))) ||
               n.Contains("nguoi moi") ||
               (n.Contains("ho tro") && n.Contains("ky thuat") && (n.Contains("khong") || n.Contains("co")));
    }

    private static bool TryMatchPurchaseShipping(string n)
    {
        return n.Contains("ship") ||
               n.Contains("bao hanh") ||
               n.Contains("dai ly");
    }

    private static bool TryMatchSoundQuality(string n)
    {
        return (n.Contains("tieng") || n.Contains("chat am") || n.Contains("am thanh")) &&
               (n.Contains("sach") || n.Contains("sub") || n.Contains("trung am"));
    }

    private static bool TryMatchPriceWithoutModel(string n, string blob)
    {
        if (LikelyProductSkuBlob(blob))
        {
            return false;
        }

        var asksPrice =
            n.Contains("bao nhieu tien") ||
            Regex.IsMatch(n, @"\bgia\s+sao\b") ||
            (n.Contains("bao gia") && n.Length < 44) ||
            (n.Trim().Equals("bao nhieu", StringComparison.OrdinalIgnoreCase));

        if (!asksPrice)
        {
            return false;
        }

        return true;
    }
}
