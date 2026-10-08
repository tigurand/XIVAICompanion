using System.Threading.Tasks;
using XIVAICompanion.Providers;

namespace XIVAICompanion.Utils
{
    public class WebSearchResult
    {
        public bool Success { get; set; }
        public string Text { get; set; } = string.Empty;
    }

    public static class SearchEngineHelper
    {
        public static async Task<WebSearchResult> SearchAsync(SearchEngineType engine, string query, string apiKey)
        {
            switch (engine)
            {
                case SearchEngineType.Exa:
                    return await ExaSearchHelper.SearchAsync(query, apiKey);                
                case SearchEngineType.Firecrawl:
                    return await FirecrawlSearchHelper.SearchAsync(query, apiKey);
                case SearchEngineType.Parallel:
                    return await ParallelSearchHelper.SearchAsync(query, apiKey);
                case SearchEngineType.Tavily:
                    return await TavilySearchHelper.SearchAsync(query, apiKey);
                default:
                    return new WebSearchResult { Success = false, Text = "No external search engine selected." };
            }
        }

        public static string GetEngineDisplayName(SearchEngineType engine)
        {
            switch (engine)
            {
                case SearchEngineType.Tavily: return "Tavily";
                case SearchEngineType.Exa: return "Exa";
                case SearchEngineType.Parallel: return "Parallel";
                case SearchEngineType.Firecrawl: return "Firecrawl";
                default: return "Google";
            }
        }
    }
}
