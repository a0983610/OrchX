using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Antigravity02.UI;

namespace Antigravity02.AIClient
{
    public interface IAIClient
    {
        string ModelName { get; }
        Task<string> GenerateContentAsync(GenerateContentRequest request);
        object CreateSimpleContents(string prompt);
        object[] DefineTools(params object[] functionDeclarations);
        object CreateFunctionDeclaration(string name, string description, object parameters);
        System.Collections.ArrayList ExtractResponseParts(Dictionary<string, object> data, out Dictionary<string, object> modelContent);
        string ExtractTextFromResponseData(Dictionary<string, object> data);
        
        (int promptTokens, int candidateTokens, int totalTokens) ExtractTokenUsage(Dictionary<string, object> data);
        void AppendFixedInfoToLastUserMessage(List<object> requestContents, string additionalInfo);
        Task<(bool hasFunctionCall, List<object> toolResponseParts)> ProcessModelPartsAsync(
            System.Collections.ArrayList parts, 
            IAgentUI ui, 
            string currentModelName,
            Func<string, Dictionary<string, object>, Task<string>> toolExecutor);
    }
}
