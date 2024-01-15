namespace AIAssistantAPI.Interfaces
{
    public interface IChatService
    {
        Task<string> ProcessChatMessageAsync(string sessionId, string input, CancellationToken cancellationToken);
        Task EndChatSessionAsync(string sessionId, CancellationToken cancellationToken);
    }
}
