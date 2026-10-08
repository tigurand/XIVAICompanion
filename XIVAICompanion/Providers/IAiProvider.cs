using System.Threading.Tasks;

namespace XIVAICompanion.Providers
{
    public interface IAiProvider
    {
        string Name { get; }
        Task<ProviderResult> SendPromptAsync(ProviderRequest request, ModelProfile profile);
    }
}
