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
        var credentials = new SessionAWSCredentials("ASIARLTQVAQHVFCUZQUH", "/h8NA1TGKOCyOZVCsEe8MMxr1yjNEPOWsvyixoRz", "IQoJb3JpZ2luX2VjEHkaCXVzLWVhc3QtMSJHMEUCIQCxuHIBRTiLmyBr/1MCzvvHDTp/lI45KvNCo22zhA/cwwIgRaOfZBsgErUrt+wpty3pp384xQCysTAM7MjlkhdAHioqogII8v//////////ARABGgwwOTM2NTE3OTcwMDciDMM8fKrx+/erzlChQyr2AQVGTmzE8SzLhw4czqDLtYWjitD3zawpYGwjcUoxcffezVI77/sgajOpaxZX3AIoNXmp4Z+hHndn3NSIbSEEZweGVnLKy3KPE4GZErSRlJPYwjP2grFQCBKRConC5Bj3h3fvKi9VRDb50bx/mbFhikuyQz79bjn0gwSqbWeBglrcEMeUpDuE3J49pvDMdzt89uDEpD1miCYtrTqoywHlo0bLfISS6/21V28hjEFVDGx52xxomm+Fn1lV1uTU9DEElywcEBqrOGAQJXBI9SuDRwKU+/aFN3HNkTZeyG+8XfrCeo82hsX8t1yfO4GN/u3iC3vlTKTUHDD4s7vGBjqdAYDJ9wLpc95vMNpmomL5HVfBNUnXlj1tN0WLH7WUQ2x+3032Lpv82jDtT8QGQJSNWw0TAK+gXDRPG78+ivVDmpuyMT/ZQcW06rrsm2q+dQkd6WTTp6Rs7y404gcbpyiabinYL5QSMbTdvtKiB9GByfGEwB2nVDC7vLzhlhUyBM7rEZb4hSdVf54J5sMJwSySegLD6/56X1FMY4BUOOg=");
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