using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace XIVAICompanion.Utils
{
    public static class FirecrawlSearchHelper
    {
        public static async Task<WebSearchResult> SearchAsync(string query, string apiKey)
        {
            if (string.IsNullOrEmpty(apiKey))
                return new WebSearchResult { Success = false, Text = "Firecrawl API key is missing." };

            try
            {
                using var client = new HttpClient();
                var requestBody = new
                {
                    query = query,
                    limit = 5
                };

                var content = new StringContent(JsonConvert.SerializeObject(requestBody), Encoding.UTF8, "application/json");
                var request = new HttpRequestMessage(HttpMethod.Post, "https://api.firecrawl.dev/v2/search")
                {
                    Content = content
                };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

                var response = await client.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                {
                    return new WebSearchResult { Success = false, Text = $"Firecrawl search failed with status: {response.StatusCode}" };
                }

                var rawJson = await response.Content.ReadAsStringAsync();
                var json = JObject.Parse(rawJson);

                JArray? webResults = null;
                var data = json["data"];
                if (data is JObject dataObj)
                {
                    // v2 response: { "success": true, "data": { "web": [...], "news": [...], "images": [...] } }
                    webResults = dataObj["web"] as JArray;
                }
                else if (data is JArray dataArray)
                {
                    // v1 response: { "success": true, "data": [ ... ] }
                    webResults = dataArray;
                }

                if (webResults == null || webResults.Count == 0)
                {
                    return new WebSearchResult { Success = false, Text = "No search results found (Firecrawl)." };
                }

                var sb = new StringBuilder();
                sb.AppendLine("Search Results (Firecrawl):");
                foreach (var result in webResults)
                {
                    string title = (string?)result["title"] ?? "No Title";
                    string url = (string?)result["url"] ?? "No URL";

                    string snippet = (string?)result["description"] ?? (string?)result["markdown"] ?? "No Content";
                    if (snippet.Length > 1200) snippet = snippet.Substring(0, 1200) + "...";

                    sb.AppendLine($"- {title} ({url}): {snippet}");
                }

                return new WebSearchResult { Success = true, Text = sb.ToString().TrimEnd() };
            }
            catch (Exception ex)
            {
                return new WebSearchResult { Success = false, Text = $"An error occurred during Firecrawl search: {ex.Message}" };
            }
        }
    }
}
