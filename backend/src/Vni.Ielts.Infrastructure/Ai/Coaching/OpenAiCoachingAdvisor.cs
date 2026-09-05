using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vni.Ielts.Application.Learning;

namespace Vni.Ielts.Infrastructure.Ai.Coaching;

/// <summary>
/// OpenAI-compatible adapter that phrases coaching advice around facts the
/// application already computed.
///
/// <b>The prompt carries numbers only.</b> A target band and four current bands
/// are personal data in the PDPL sense, so this goes through
/// <see cref="AiEgress"/> as <see cref="AiDataClassification.LearnerPersonal"/>
/// and is refused unless the cross-border switch is on — the same gate the
/// Writing marker lives behind. No name, no essay, no transcript ever reaches
/// the prompt, and the reply is validated before a learner reads it.
/// </summary>
public sealed class OpenAiCoachingAdvisor(
    IHttpClientFactory httpFactory,
    IOptions<AiOptions> aiOptions,
    ILogger<OpenAiCoachingAdvisor> logger) : ICoachingAdvisor
{
    public const string PromptVersion = "coaching-advice-v1";

    public bool IsConfigured
    {
        get
        {
            if (!aiOptions.Value.OpenAi.IsConfigured) return false;
            try
            {
                AiEgress.Authorise(aiOptions.Value, "OpenAi", AiDataClassification.LearnerPersonal);
                return true;
            }
            catch (AiEgressRefusedException)
            {
                return false;
            }
        }
    }

    public async Task<CoachingAdviceResult> AdviseAsync(CoachingFacts facts, CancellationToken ct)
    {
        AiEgressTicket ticket;
        try
        {
            ticket = AiEgress.Authorise(aiOptions.Value, "OpenAi", AiDataClassification.LearnerPersonal);
        }
        catch (AiEgressRefusedException e)
        {
            return CoachingAdviceResult.Failed($"COACHING_EGRESS_{e.Refusal.ToString().ToUpperInvariant()}");
        }

        var body = new JsonObject
        {
            ["model"] = ticket.Model,
            ["messages"] = new JsonArray(
                new JsonObject { ["role"] = "system", ["content"] = SystemPrompt },
                new JsonObject { ["role"] = "user", ["content"] = UserPrompt(facts) }),
            ["response_format"] = new JsonObject { ["type"] = "json_object" },
            ["temperature"] = 0.4,
            ["max_tokens"] = 900,
        };

        var http = httpFactory.CreateClient(nameof(OpenAiCoachingAdvisor));
        var baseUrl = string.IsNullOrWhiteSpace(ticket.BaseUrl)
            ? "https://api.openai.com/v1/"
            : ticket.BaseUrl.TrimEnd('/') + "/";

        using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(baseUrl), "chat/completions"))
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ticket.RevealApiKey());

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(45));

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(message, deadline.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return CoachingAdviceResult.Failed("COACHING_PROVIDER_TIMEOUT");
        }
        catch (HttpRequestException)
        {
            return CoachingAdviceResult.Failed("COACHING_PROVIDER_FAILED");
        }

        using (response)
        {
            var payload = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Coaching advice request rejected with status {Status}. Body: {Body}",
                    (int)response.StatusCode,
                    payload.Length <= 300 ? payload : payload[..300] + "…");
                return CoachingAdviceResult.Failed("COACHING_PROVIDER_REJECTED");
            }

            try
            {
                var content = MessageContent(payload);
                var parsed = CoachingAdviceValidator.Parse(content);
                if (parsed is null) return CoachingAdviceResult.Failed("COACHING_ADVICE_INVALID");

                return CoachingAdviceResult.Ok(new CoachingAdvice(
                    parsed.Value.Summary, parsed.Value.Tips, "openai", ticket.Model, PromptVersion));
            }
            catch (JsonException)
            {
                return CoachingAdviceResult.Failed("COACHING_PROVIDER_MALFORMED");
            }
        }
    }

    private static string MessageContent(string payload)
    {
        using var doc = JsonDocument.Parse(payload);
        var content = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();
        return content ?? throw new JsonException("No message content.");
    }

    private const string SystemPrompt =
        """
        Bạn là giáo viên IELTS. Bạn nhận band mục tiêu và band hiện tại của bốn kỹ năng,
        và viết lời khuyên ngắn, cụ thể, bằng tiếng Việt, cho người học tự ôn.
        Chỉ dựa vào các con số được đưa. Không bịa thêm điểm, không nêu mốc thời gian
        cụ thể, không đưa đường link, không dùng HTML hay Markdown.
        Trả về đúng một JSON object:
        {"summary": "<2–3 câu nêu kỹ năng cần ưu tiên và vì sao>",
         "tips": [{"module": "reading|listening|writing|speaking", "text": "<một việc cụ thể nên làm>"}]}
        Tối đa 5 tips, mỗi tip dưới 200 ký tự, ưu tiên kỹ năng có khoảng cách lớn nhất.
        """;

    private static string UserPrompt(CoachingFacts facts)
    {
        var sb = new StringBuilder();
        sb.Append("Mục tiêu: ").Append(facts.TargetBand.ToString("0.0")).AppendLine();
        foreach (var skill in facts.Skills)
        {
            sb.Append(skill.Module).Append(": ")
              .Append(skill.CurrentBand?.ToString("0.0") ?? skill.Detail ?? "chưa có điểm");
            if (skill.Gap is { } gap) sb.Append(" (chênh ").Append(gap.ToString("+0.0;-0.0;0.0")).Append(')');
            sb.AppendLine();
        }
        sb.Append("Trả về JSON.");
        return sb.ToString();
    }
}
