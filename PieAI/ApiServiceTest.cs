using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Amazon.BedrockAgentRuntime;
using Amazon.BedrockAgentRuntime.Model;
using Amazon.Runtime;
using System.Text;
using System.Text.Json;
using Amazon;

public class ApiServiceTest
{
    private readonly AmazonBedrockRuntimeClient _runtimeClient;
    private readonly AmazonBedrockAgentRuntimeClient _agentRuntimeClient;
    private readonly string _modelIdForDirectInvoke; // para InvokeModel (sem KB)
                                                     // >>>> Ajuste estes dois valores para a sua KB <<<<
    private readonly string _knowledgeBaseId; // ex: "KB1234567890abcdef"
    private readonly string _modelArnOrInferenceProfile;
    // ex de ModelArn (ou Inference Profile ARN):
    // "arn:aws:bedrock:us-east-1:123456789012:inference-profile/us.anthropic.claude-3-5-sonnet-20240620-v1:0"

    public ApiServiceTest()
    {
        var credentials = new SessionAWSCredentials("ASIARLTQVAQHU4ZXCMJD", "pe2vYtpaKNw0TGzglfxKGxJ3X/2BHPkQ86NgjqR1", "IQoJb3JpZ2luX2VjEIr//////////wEaCXVzLWVhc3QtMSJGMEQCIHKWvoCx230RNJpRL1FYaqyStz6erBBzIcnsLFzbOxiHAiANVM6mbizEICHQy0Tg20WmURGOeMMfOKGkaLURUPt8zSqZAggTEAEaDDA5MzY1MTc5NzAwNyIM9vYi88LrVtJLed4aKvYBi4yRo/vTlDjhI96Adzi9ggCAv2x2wMx9gNiSHjr+ZhHyhFxDJNbMVuZ8n5wOPR8k4dgb1MK5wIyGA5QSQnlnee214xlUSbrgdgIvLcvF9NGgbveVYCM1Y8NFItkoyrqCvBgGe6aNryLR3f/MoAQbi9XXuxjoe7u9zme0TLI0koY6oDViOxbespBFtxLvNug/JbM8Fw3lF20M3yeL2wlcvNw3GtV4ECJnce4Z4zIciwhcL9AhhGaOmEGmDl1Chx0FtJ3zt/VZadWJruFcUrGFRUuWv1IJxRKvHUhKJTHCTuOnUocJAe6KOKu1ljfOiKvVNTGYMF/+MJiLv8YGOp4BrlCoIWk5A4hzbBqaF7yfM0OTSrKt+9wZDr1C+Ly4gjJOZLyGlS9VOwsnJxEj56vnYyG4idpDRz+NO9d0LAcVtqTWdlaHMSR84PSaZ20aVUjdOerTLPLSHBHY24+RM3Hd8LWJkcdJyBfSeb8SbW+t9+/V8de6RK/S9ETnsJrOP/s/kWQisBM5/nLtAuAuCceS4MCXefJZ9nW8SzDYsYM=");
        var regionEndpoint = RegionEndpoint.GetBySystemName("us-east-1");

        _runtimeClient = new AmazonBedrockRuntimeClient(credentials, regionEndpoint);
        _agentRuntimeClient = new AmazonBedrockAgentRuntimeClient(credentials, regionEndpoint);

        // ids fixos (ajusta com os teus valores)
        _modelIdForDirectInvoke = "us.anthropic.claude-3-5-sonnet-20240620-v1:0";
        _knowledgeBaseId = "62E6WO367Z"; // id da tua KB
        _modelArnOrInferenceProfile = "anthropic.claude-3-sonnet-20240229-v1:0";
    }

    // ===== 1) Igual ao seu método atual: chamada direta ao modelo, sem KB =====
    public async Task<string> SendPrompt(string prompt)
    {
        var requestBody = new
        {
            anthropic_version = "bedrock-2023-05-31",
            max_tokens = 200,
            temperature = 0.5,
            top_p = 0.9,
            messages = new[]
            {
            new {
                role = "user",
                content = new[] { new { type = "text", text = prompt } }
            }
        }
        };

        var bodyString = JsonSerializer.Serialize(requestBody);
        var request = new InvokeModelRequest
        {
            ModelId = _modelIdForDirectInvoke,
            ContentType = "application/json",
            Accept = "application/json",
            Body = new MemoryStream(Encoding.UTF8.GetBytes(bodyString))
        };

        var response = await _runtimeClient.InvokeModelAsync(request);
        using var reader = new StreamReader(response.Body);
        var responseString = await reader.ReadToEndAsync();

        using var doc = JsonDocument.Parse(responseString);
        var text = doc.RootElement.GetProperty("content")[0].GetProperty("text").GetString();
        return text ?? "ERRO no pedido";
    }

    // ===== 2) NOVO: consulta a Knowledge Base (RAG) com RetrieveAndGenerate =====
    public async Task<string> SendPromptWithKnowledgeBase(string prompt)
    {
        string answer = null;
        try
        {
            var req = new RetrieveAndGenerateRequest
            {
                Input = new RetrieveAndGenerateInput { Text = prompt },
                RetrieveAndGenerateConfiguration = new RetrieveAndGenerateConfiguration
                {
                    Type = RetrieveAndGenerateType.KNOWLEDGE_BASE,
                    KnowledgeBaseConfiguration = new KnowledgeBaseRetrieveAndGenerateConfiguration
                    {
                        KnowledgeBaseId = _knowledgeBaseId,
                        ModelArn = _modelArnOrInferenceProfile,

                        #region optional
                        // (Opcional) Ajustes finos de geração/recuperação:
                        // GenerationConfiguration = new GenerationConfiguration
                        // {
                        //     InferenceConfig = new InferenceConfiguration
                        //     {
                        //         MaxTokens = 4000,
                        //         Temperature = 0.3
                        //     }
                        // },
                        // RetrievalConfiguration = new KnowledgeBaseRetrievalConfiguration
                        // {
                        //     VectorSearchConfiguration = new KnowledgeBaseVectorSearchConfiguration
                        //     {
                        //         NumberOfResults = 8
                        //     }
                        // }
                        #endregion
                    }
                }
            };

            var resp = await _agentRuntimeClient.RetrieveAndGenerateAsync(req);

            // Texto final
            answer = resp.Output?.Text ?? string.Empty;

        }
        catch (AmazonBedrockAgentRuntimeException ex)
        {
            Console.WriteLine($"AWS Error: {ex.Message}");
        }
        catch (Exception ex)
        {
            // Handle other potential exceptions
            Console.WriteLine($"General Error: {ex.Message}");
        }
        #region citacion

        //// Coletar citações (fontes) retornadas
        //var citations = new List<string>();
        //if (resp.Citations != null)
        //{
        //    foreach (var c in resp.Citations)
        //    {
        //        // cada c.SourceCitations contém um ou mais trechos com referências
        //        if (c.RetrievedReferences != null)
        //        {
        //            foreach (var sc in c.RetrievedReferences)
        //            {
        //                // Tenta compor algo útil: título/URI/ID do documento
        //                var label = !string.IsNullOrWhiteSpace(sc.Title) ? sc.Title : sc.ReferenceArn ?? sc.SourceId;
        //                if (!string.IsNullOrWhiteSpace(sc.Uri))
        //                    label = $"{label} ({sc.Uri})";
        //                if (!string.IsNullOrWhiteSpace(label))
        //                    citations.Add(label);
        //            }
        //        }
        //    }
        //}

        #endregion
        return (string.IsNullOrWhiteSpace(answer) ? "Sem resposta gerada." : answer);

    }
}