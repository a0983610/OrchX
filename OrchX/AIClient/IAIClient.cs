using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using OrchX.UI;

namespace OrchX.AIClient
{
    public interface IAIClient
    {
        string ModelName { get; }
        Task<string> GenerateContentAsync(GenerateContentRequest request, System.Threading.CancellationToken cancellationToken = default);
        object CreateSimpleContents(string prompt);
        object[] DefineTools(params object[] functionDeclarations);
        object CreateFunctionDeclaration(string name, string description, object parameters);
        System.Collections.ArrayList ExtractResponseParts(Dictionary<string, object> data, out Dictionary<string, object> modelContent);
        string ExtractTextFromResponseData(Dictionary<string, object> data);
        
        (int promptTokens, int candidateTokens, int totalTokens) ExtractTokenUsage(Dictionary<string, object> data);
        Task<(bool hasFunctionCall, List<object> toolResponseParts)> ProcessModelPartsAsync(
            System.Collections.ArrayList parts, 
            IAgentUI ui, 
            string currentModelName,
            Func<string, Dictionary<string, object>, Task<string>> toolExecutor,
            System.Threading.CancellationToken cancellationToken = default);
            
        bool TryGetTextFromPart(object partObj, out string text);
        bool TryGetFunctionCallFromPart(object partObj, out string functionName, out Dictionary<string, object> args);
        bool TryGetFunctionResponseFromPart(object partObj, out string functionName, out string responseContent);
        bool TryGetRoleAndPartsFromMessage(object messageObj, out string role, out IEnumerable<object> parts);
        object BuildToolResponsePart(string funcName, string result);
        object BuildFunctionMessageContent(List<object> toolResponseParts);
        object BuildMessageContent(string role, string text);
        object BuildImageMessageContent(string role, string text, string mimeType, string base64Data);
    }
}
